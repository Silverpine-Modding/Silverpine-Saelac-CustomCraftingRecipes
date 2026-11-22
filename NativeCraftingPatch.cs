#nullable enable
using System;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
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
        var access = __instance.GetComponent<NativeRecipeAccess>();
        access?.ClearPreview();
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
        bool canCraft = CraftTransaction.CanComplete(player.worldInventory.inventory, ingredients, BatchResult.Units(result), out string error);
        var station = data is AlchemyCraftingUIData ? CraftingStation.Alchemy : CraftingStation.Cooking;
        if (access == null) access = __instance.gameObject.AddComponent<NativeRecipeAccess>();
        access.SetPreview(player,station,result,___craftButton,___resultTextMesh,canCraft,error);
        ___craftButton.onClick.AddListener(() => CraftExecution.Start(player, data, station, ingredients, result, __instance.Close));
        return false;
    }
}

// The native UI polls only when a selection changes. Refresh just the displayed
// lock, not GetResultData (which could roll a new potion or replace listeners).
internal sealed class NativeRecipeAccess : MonoBehaviour
{
    private Player? player;
    private CraftingStation station;
    private ResultData? result;
    private Button? button;
    private TextMeshProUGUI? text;
    private bool hasCapacity;
    private string capacityError = "";
    private string? previousLock;
    private int accessRevision, packRevision;
    private float nextPoll;

    internal void SetPreview(Player owner, CraftingStation at, ResultData preview, Button craft, TextMeshProUGUI label, bool canFit, string error)
    {
        player = owner; station = at; result = preview; button = craft; text = label;
        hasCapacity = canFit; capacityError = error;
        RefreshLock(true);
    }

    private void Update()
    {
        if (result != null && (CraftingApi.AccessRevision != accessRevision || RecipeRegistry.Revision != packRevision ||
            Time.unscaledTime >= nextPoll)) RefreshLock(false);
    }

    private void RefreshLock(bool force)
    {
        accessRevision = CraftingApi.AccessRevision; packRevision = RecipeRegistry.Revision; nextPoll = Time.unscaledTime + 0.5f;
        string? reason = CraftingApi.LockReason(player!,station,result!);
        if (!force && reason == previousLock) return;
        previousLock = reason;
        button!.interactable = hasCapacity && reason == null;
        text!.text = result!.resultText + (reason != null ? "\nLocked: " + reason : hasCapacity ? "" : "\n" + capacityError);
    }

    internal void ClearPreview() { result = null; player = null; button = null; text = null; previousLock = null; }
    private void OnDisable() => ClearPreview();
}
