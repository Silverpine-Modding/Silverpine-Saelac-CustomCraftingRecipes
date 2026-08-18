#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Silverpine.ModdingTools;
using UnityEngine;

namespace SilverpineMods.CustomCraftingRecipes;

internal sealed class QuantityCraftingUI : MonoBehaviour
{
    private const float DesignWidth = 1920f;
    private const float DesignHeight = 1080f;
    private const int SlotCount = 10;

    private sealed class IngredientSelection
    {
        internal string ItemName = "";
        internal int Quantity;
    }

    private static QuantityCraftingUI? instance;

    private readonly List<IngredientSelection> selections = new();
    private CraftingUIData? craftingData;
    private Vector2 pickerScroll;
    private string pickerSearch = "";
    private int pickerSlot = -1;
    private bool open;

    internal static void Open(CraftingUIData data)
    {
        EnsureExists();
        if (instance!.open)
            return;

        instance.craftingData = data;
        instance.pickerSlot = -1;
        instance.pickerSearch = "";
        instance.selections.Clear();
        for (int index = 0; index < SlotCount; index++)
            instance.selections.Add(new IngredientSelection());
        instance.open = true;
        Player.Instance.SetInputBlock(state: true);
        Plugin.Log.LogInfo(
            "Opened quantity-aware CustomCrafting UI with ten slots.");
    }

    private static void EnsureExists()
    {
        if (instance != null)
            return;
        GameObject root = new("Quantity Custom Crafting UI");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<QuantityCraftingUI>();
    }

    private void Update()
    {
        if (!open || !Input.GetKeyDown(KeyCode.Escape))
            return;
        if (pickerSlot >= 0)
            pickerSlot = -1;
        else
            Close();
    }

    private void OnGUI()
    {
        if (!open)
            return;

        GUI.enabled = true;
        GUI.color = Color.white;
        GUI.backgroundColor = Color.white;
        GUI.depth = -950;
        using ModGuiScope scope = ModGui.BeginScaled(
            DesignWidth,
            DesignHeight);

        Color old = GUI.color;
        GUI.color = new Color(0.035f, 0.05f, 0.065f, 0.985f);
        GUI.DrawTexture(
            new Rect(0f, 0f, DesignWidth, DesignHeight),
            Texture2D.whiteTexture);
        GUI.color = old;

        GUILayout.BeginArea(
            new Rect(300f, 90f, 1320f, 900f),
            GUI.skin.box);
        if (pickerSlot >= 0)
            DrawItemPicker();
        else
            DrawCraftingPanel();
        GUILayout.EndArea();
    }

    private void DrawCraftingPanel()
    {
        GUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label("Custom Crafting", GUILayout.Width(260f));
        GUILayout.Label("Ten ingredient types • adjustable quantities");
        if (GUILayout.Button("Close", GUILayout.Width(100f)))
            Close();
        GUILayout.EndHorizontal();

        for (int index = 0; index < selections.Count; index++)
            DrawIngredientRow(index, selections[index]);

        GUILayout.Space(12f);
        Dictionary<string, int> counts = BuildSelectedCounts();
        bool matched = RecipeRegistry.TryGetConfiguredResult(
            CraftingStation.CustomCrafting,
            counts,
            out ResultData result);
        if (counts.Count == 0)
            GUILayout.Label("Select ingredients to find a recipe.", GUI.skin.box);
        else if (!matched)
            GUILayout.Label(
                "No CustomCrafting recipe matches these items and quantities.",
                GUI.skin.box);
        else
        {
            GUILayout.Label(
                "Result: " + result.resultItem.GetFinalName(),
                GUI.skin.box);
            if (!string.IsNullOrWhiteSpace(result.resultText))
                GUILayout.Label(result.resultText, GUI.skin.box);
            GUILayout.Label(
                $"Work time: {result.workMinutes} in-game minute(s)");
        }

        bool inventoryHasIngredients = HasSelectedQuantities(counts);
        bool canAddResult = matched &&
            result.resultItem != null &&
            Player.Instance.worldInventory.inventory.CanAddItem(
                result.resultItem);
        GUI.enabled = matched && inventoryHasIngredients && canAddResult;
        if (GUILayout.Button("Craft", GUILayout.MinHeight(64f)))
            StartCraft(counts, result);
        GUI.enabled = true;

        if (matched && !inventoryHasIngredients)
            GUILayout.Label("Your inventory no longer has the selected quantities.");
        else if (matched && !canAddResult)
            GUILayout.Label("Your inventory cannot hold the result.");
    }

    private void DrawIngredientRow(
        int index,
        IngredientSelection selection)
    {
        int available = string.IsNullOrWhiteSpace(selection.ItemName)
            ? 0
            : InventoryCount(selection.ItemName);
        if (selection.Quantity > available)
            selection.Quantity = available;

        GUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label($"Slot {index + 1}", GUILayout.Width(90f));
        GUILayout.Label(
            string.IsNullOrWhiteSpace(selection.ItemName)
                ? "Empty"
                : selection.ItemName,
            GUILayout.MinWidth(360f));
        if (GUILayout.Button("Choose", GUILayout.Width(100f)))
        {
            pickerSlot = index;
            pickerSearch = "";
            pickerScroll = Vector2.zero;
        }
        GUI.enabled = !string.IsNullOrWhiteSpace(selection.ItemName);
        if (GUILayout.Button("Clear", GUILayout.Width(85f)))
        {
            selection.ItemName = "";
            selection.Quantity = 0;
        }
        GUI.enabled = selection.Quantity > 1;
        if (GUILayout.Button("−", GUILayout.Width(48f)))
            selection.Quantity--;
        GUI.enabled = available > 0 && selection.Quantity < available;
        if (GUILayout.Button("+", GUILayout.Width(48f)))
            selection.Quantity++;
        GUI.enabled = true;
        GUILayout.Label(
            string.IsNullOrWhiteSpace(selection.ItemName)
                ? "Qty —"
                : $"Qty {selection.Quantity} / {available}",
            GUILayout.Width(145f));
        GUILayout.EndHorizontal();
    }

    private void DrawItemPicker()
    {
        GUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label(
            $"Choose ingredient for slot {pickerSlot + 1}",
            GUILayout.Width(300f));
        GUILayout.Label("Only loaded CustomCrafting recipe ingredients appear.");
        if (GUILayout.Button("Cancel", GUILayout.Width(100f)))
            pickerSlot = -1;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label("Search", GUILayout.Width(80f));
        pickerSearch = GUILayout.TextField(pickerSearch);
        if (GUILayout.Button("Clear", GUILayout.Width(90f)))
            pickerSearch = "";
        GUILayout.EndHorizontal();

        HashSet<string> selectedElsewhere = selections
            .Where((selection, index) =>
                index != pickerSlot &&
                !string.IsNullOrWhiteSpace(selection.ItemName))
            .Select(selection => selection.ItemName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<Item> choices = Player.Instance.worldInventory.inventory
            .GetAllItemsShallowListCopy()
            .Where(item =>
                RecipeRegistry.IsIngredientName(
                    CraftingStation.CustomCrafting,
                    item.name) &&
                !selectedElsewhere.Contains(item.name) &&
                (string.IsNullOrWhiteSpace(pickerSearch) ||
                 item.name.IndexOf(
                     pickerSearch,
                     StringComparison.OrdinalIgnoreCase) >= 0))
            .GroupBy(item => item.name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        GUILayout.Label($"{choices.Count} available ingredient type(s)");
        pickerScroll = GUILayout.BeginScrollView(pickerScroll);
        foreach (Item item in choices)
        {
            int count = InventoryCount(item.name);
            if (GUILayout.Button(
                    $"{item.GetFinalName()}  •  Available: {count}",
                    GUILayout.MinHeight(48f)))
            {
                IngredientSelection selection = selections[pickerSlot];
                selection.ItemName = item.name;
                selection.Quantity = Math.Min(Math.Max(1, selection.Quantity), count);
                pickerSlot = -1;
            }
        }
        GUILayout.EndScrollView();
    }

    private Dictionary<string, int> BuildSelectedCounts()
    {
        Dictionary<string, int> counts =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (IngredientSelection selection in selections)
        {
            if (string.IsNullOrWhiteSpace(selection.ItemName) ||
                selection.Quantity <= 0)
                continue;
            counts[selection.ItemName] = selection.Quantity;
        }
        return counts;
    }

    private static int InventoryCount(string itemName) =>
        Player.Instance.worldInventory.inventory
            .GetAllItemsShallowListCopy()
            .Count(item => item.name.Equals(
                itemName,
                StringComparison.OrdinalIgnoreCase));

    private static bool HasSelectedQuantities(
        IReadOnlyDictionary<string, int> counts) =>
        counts.All(pair => InventoryCount(pair.Key) >= pair.Value);

    private void StartCraft(
        IReadOnlyDictionary<string, int> counts,
        ResultData result)
    {
        if (craftingData == null || result.resultItem == null)
            return;

        Dictionary<string, int> required = counts.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        Item resultItem = result.resultItem;
        CraftingUIData data = craftingData;
        int workTurns = result.workMinutes * 2;
        Close();
        data.startCallback?.Invoke();
        Player.Instance.StartLongInteraction(
            delegate
            {
                List<Item> inventoryItems = Player.Instance.worldInventory
                    .inventory.GetAllItemsShallowListCopy();
                List<Item> toRemove = new();
                foreach (KeyValuePair<string, int> pair in required)
                {
                    List<Item> matches = inventoryItems
                        .Where(item => item.name.Equals(
                            pair.Key,
                            StringComparison.OrdinalIgnoreCase))
                        .Take(pair.Value)
                        .ToList();
                    if (matches.Count < pair.Value)
                    {
                        Plugin.Log.LogWarning(
                            "Custom craft cancelled at completion because " +
                            $"'{pair.Key}' quantity changed.");
                        UpperNotificationUI.Instance.OneOff(
                            "You no longer have the required ingredients.");
                        data.finishCallback?.Invoke();
                        return;
                    }
                    toRemove.AddRange(matches);
                }

                Player.Instance.worldInventory.inventory.RemoveItems(toRemove);
                Player.Instance.worldInventory.inventory.TryAddItem(resultItem);
                AnimationHelper.Instance.SpawnItemFloatingText(resultItem);
                if (resultItem.itemSound != ItemSound.None)
                {
                    AudioPlayer.Instance.PlayAudioAtPosition(
                        Resources.Load<AudioClip>(
                            "SFX/ItemSounds/sound_item_" +
                            resultItem.itemSound.ToString().ToLower()),
                        1f,
                        Player.Instance.transform.GetVector2IntPosition());
                }
                data.finishCallback?.Invoke();
            },
            workTurns,
            data.finishCallback);
    }

    private void Close()
    {
        if (!open)
            return;
        open = false;
        pickerSlot = -1;
        craftingData = null;
        Player.Instance.SetInputBlock(state: false);
    }
}
