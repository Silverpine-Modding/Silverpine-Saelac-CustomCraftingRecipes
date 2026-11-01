#nullable enable
using System;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine.UI;

namespace SilverpineMods.CustomCraftingRecipes;

// Use the same checked delivery for the stations whose result rules we extend.
// Capture the selected item instances before closing the native fields.
[HarmonyPatch(typeof(CraftingUI), "PollFields")]
internal static class NativeCraftingPatch
{
    private static bool Prefix(CraftingUI __instance, CraftingUIData ___currentCraftingUIData,
        TextMeshProUGUI ___resultTextMesh, UnityEngine.GameObject ___resultField, Button ___craftButton)
    {
        CraftingUIData data = ___currentCraftingUIData;
        if (data is not CookingCraftingUIData && data is not AlchemyCraftingUIData &&
            data?.GetType().FullName != "SilverpineMods.CustomItemLoader.CustomItemCookingCraftingUIData") return true;
        var items = data.itemSelectionFieldInfos.Select(field => field.itemSelectionField.selectedItem).ToList();
        ResultData result = data.GetResultData(items);
        ___resultTextMesh.text = result.resultText;
        var label = ___resultField.GetComponentInChildren<TextMeshProUGUI>();
        var images = ___resultField.GetComponentsInChildren<Image>();
        ___craftButton.ClearListenersAndResubscribe();
        ___craftButton.interactable = false;
        label.text = "";
        foreach (Image image in images.Take(2)) image.enabled = result.resultItem != null;
        if (result.resultItem == null) return false;
        Player player = Player.Instance;
        CraftingResultPreview.Prepare(result.resultItem, player.worldInventory.inventory);
        label.text = result.resultItem.GetFinalName();
        var sprite = result.resultItem.Sprite;
        foreach (Image image in images.Take(2)) image.sprite = sprite;
        var ingredients = items.Where(item => item != null).ToList();
        bool canCraft = CraftTransaction.CanComplete(player.worldInventory.inventory, ingredients, result.resultItem, out string error);
        ___craftButton.interactable = canCraft;
        if (!canCraft) ___resultTextMesh.text += "\n" + error;
        ___craftButton.onClick.AddListener(() => CraftExecution.Start(player, data, ingredients, result, __instance.Close));
        return false;
    }
}
