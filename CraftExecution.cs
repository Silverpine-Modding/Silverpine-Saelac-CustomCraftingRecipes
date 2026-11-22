#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SilverpineMods.CustomCraftingRecipes;

internal static class CraftExecution
{
    internal static bool CanStart(Player player, CraftingStation station, ResultData result, Inventory inventory,
        IReadOnlyList<Item> ingredients, out string error)
    {
        string? denied = CraftingApi.LockReason(player, station, result);
        if (denied != null) { error = denied; return false; }
        return CraftTransaction.CanComplete(inventory, ingredients, BatchResult.Units(result), out error);
    }

    internal static void Start(Player player, CraftingUIData data, CraftingStation station, List<Item> ingredients, ResultData result, Action close)
    {
        Inventory inventory = player.worldInventory.inventory;
        if (result.resultItem == null) return;
        var units = BatchResult.Units(result);
        if (!CanStart(player, station, result, inventory, ingredients, out string validationError))
        { UpperNotificationUI.Instance?.OneOff(validationError); return; }
        bool finished = false, settling = false;
        void Finish()
        {
            if (finished) return;
            finished = true;
            try { data.finishCallback?.Invoke(); }
            catch (Exception exception) { Plugin.Log.LogError("Craft finish callback failed: " + exception); }
        }
        close();
        try
        {
            data.startCallback?.Invoke();
            player.StartLongInteraction(() =>
            {
                if (finished || settling) return; // Interrupted or duplicate/reentrant success callback.
                settling = true;
                try
                {
                    if (player == null || Player.Instance != player || player.worldInventory.inventory != inventory) return;
                    if (!CraftingApi.TryDeliver(player, station, result, inventory, ingredients, out var delivered, out string error))
                    {
                        Plugin.Log.LogWarning(error);
                        UpperNotificationUI.Instance?.OneOff(error);
                        return;
                    }
                    Item first = delivered[0];
                    if (delivered.Count > units.Count)
                        UpperNotificationUI.Instance?.OneOff($"Crafted {delivered.Count} × {first.name} (+{delivered.Count - units.Count} bonus).");
                    AnimationHelper.Instance.SpawnItemFloatingText(first);
                    if (first.itemSound != ItemSound.None)
                    {
                        AudioClip clip = Resources.Load<AudioClip>("SFX/ItemSounds/sound_item_" + first.itemSound.ToString().ToLowerInvariant());
                        if (clip != null) AudioPlayer.Instance.PlayAudioAtPosition(clip, 1f, player.transform.GetVector2IntPosition());
                    }
                }
                catch (Exception exception) { Plugin.Log.LogError("Craft completion failed: " + exception); }
                finally { Finish(); }
            }, result.workMinutes * 2, Finish);
        }
        catch (Exception exception)
        {
            Finish();
            Plugin.Log.LogError("Could not start crafting: " + exception);
        }
    }
}
