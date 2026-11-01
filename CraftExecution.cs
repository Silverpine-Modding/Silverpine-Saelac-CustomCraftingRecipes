#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SilverpineMods.CustomCraftingRecipes;

internal static class CraftExecution
{
    internal static void Start(Player player, CraftingUIData data, List<Item> ingredients, ResultData result, Action close)
    {
        Inventory inventory = player.worldInventory.inventory;
        if (result.resultItem == null) return;
        if (!CraftTransaction.CanComplete(inventory, ingredients, result.resultItem, out string validationError))
        { UpperNotificationUI.Instance?.OneOff(validationError); return; }
        bool finished = false;
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
                try
                {
                    if (player == null || Player.Instance != player || player.worldInventory.inventory != inventory) return;
                    if (!CraftTransaction.TryComplete(inventory, ingredients, result.resultItem, out string error))
                    {
                        Plugin.Log.LogWarning(error);
                        UpperNotificationUI.Instance?.OneOff(error);
                        return;
                    }
                    AnimationHelper.Instance.SpawnItemFloatingText(result.resultItem);
                    if (result.resultItem.itemSound != ItemSound.None)
                    {
                        AudioClip clip = Resources.Load<AudioClip>("SFX/ItemSounds/sound_item_" + result.resultItem.itemSound.ToString().ToLowerInvariant());
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
