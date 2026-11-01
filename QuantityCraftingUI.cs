#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Silverpine.ModdingTools;
using UnityEngine;

namespace SilverpineMods.CustomCraftingRecipes;

internal sealed class QuantityCraftingUI : MonoBehaviour
{
    private sealed class IngredientSelection { internal string ItemName = ""; internal int Quantity; }
    private static QuantityCraftingUI? instance;
    private readonly List<IngredientSelection> selections = new();
    private readonly Dictionary<string, int> available = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
    private CraftingUIData? craftingData;
    private Player? player;
    private Inventory? inventory;
    private ResultData result = ResultData.None;
    private string resultName = "";
    private List<Item> ingredients = new();
    private Vector2 pickerScroll, ingredientScroll, resultScroll;
    private string pickerSearch = "";
    private int pickerSlot = -1, registryRevision = -1, catalogRevision = -1;
    private bool open, dirty, inventoryDirty, ownsInputBlock;
    private bool canCraft, lastAllowEncumbered;
    private int lastMaxBulk, lastMaxItems;
    private string craftError = "";
    private List<Item> pickerItems = new();
    private string cachedPickerSearch = "";
    private bool pickerDirty = true;
    private GUIStyle? resultStyle;

    internal static void Open(CraftingUIData data)
    {
        Player current = Player.Instance;
        if (current == null || current.worldInventory == null) return;
        if (instance == null)
        {
            GameObject root = new("Quantity Custom Crafting UI");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<QuantityCraftingUI>();
        }
        if (instance.open) return;
        instance.craftingData = data;
        instance.player = current;
        instance.inventory = current.worldInventory.inventory;
        instance.inventory.OnInventoryChanged += instance.InventoryChanged;
        instance.pickerSlot = -1;
        instance.pickerSearch = "";
        instance.selections.Clear();
        for (int i = 0; i < CraftingStation.CustomCrafting.Capacity(); i++) instance.selections.Add(new IngredientSelection());
        instance.open = true;
        instance.dirty = instance.inventoryDirty = true;
        current.SetInputBlock(true);
        instance.ownsInputBlock = true;
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged += instance.SceneChanged;
    }

    private void SceneChanged(UnityEngine.SceneManagement.Scene oldScene, UnityEngine.SceneManagement.Scene newScene) => Close();
    private void InventoryChanged() => inventoryDirty = true;
    private void Update()
    {
        if (!open) return;
        if (player == null || Player.Instance != player || player.worldInventory.inventory != inventory)
        { Close(); return; }
        KeyCode closeKey = SettingsUI.Instance != null ? SettingsUI.Instance.closeMenuKeyCode : KeyCode.Escape;
        if (Input.GetKeyDown(closeKey) || Input.GetKeyDown(KeyCode.Escape))
        {
            if (pickerSlot >= 0) pickerSlot = -1; else Close();
        }
        if (open) Refresh();
    }

    private void Refresh()
    {
        ItemCatalog.Refresh();
        if (inventory!.maxBulk != lastMaxBulk || inventory.maxItems != lastMaxItems || inventory.allowEncumbered != lastAllowEncumbered)
        {
            dirty = true;
            lastMaxBulk = inventory.maxBulk;
            lastMaxItems = inventory.maxItems;
            lastAllowEncumbered = inventory.allowEncumbered;
        }
        if (registryRevision != RecipeRegistry.Revision || catalogRevision != ItemCatalog.Revision)
        { dirty = true; registryRevision = RecipeRegistry.Revision; catalogRevision = ItemCatalog.Revision; }
        if (inventoryDirty)
        {
            available.Clear();
            foreach (Item item in inventory!.items)
                if (item != null)
                { available.TryGetValue(item.name, out int count); available[item.name] = count + 1; }
            inventoryDirty = false;
            dirty = true;
        }
        if (!dirty) return;
        dirty = false;
        pickerDirty = true;
        counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (IngredientSelection selection in selections)
            if (selection.Quantity > 0 && selection.ItemName.Length > 0) counts[selection.ItemName] = selection.Quantity;
        RecipeRegistry.TryGetConfiguredResult(CraftingStation.CustomCrafting, counts, out result);
        resultName = result.resultItem != null ? result.resultItem.GetFinalName() : "";
        ingredients = CraftTransaction.Select(inventory!, counts);
        craftError = "";
        canCraft = result.resultItem != null && CraftTransaction.CanComplete(inventory!, ingredients, result.resultItem, out craftError);
    }

    private void OnGUI()
    {
        if (!open || player == null || inventory == null) return;
        using ModGuiScope scope = ModGui.BeginScaled(1920, 1080);
        GUI.enabled = true;
        GUI.color = Color.white;
        GUI.backgroundColor = Color.white;
        GUI.depth = -950;
        GUI.color = new Color(0.035f, 0.05f, 0.065f, 0.985f);
        GUI.DrawTexture(new Rect(0, 0, 1920, 1080), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUILayout.BeginArea(new Rect(300, 90, 1320, 900), GUI.skin.box);
        if (pickerSlot >= 0) DrawItemPicker(); else DrawCraftingPanel();
        GUILayout.EndArea();
    }

    private void DrawCraftingPanel()
    {
        resultStyle ??= new GUIStyle(GUI.skin.box) { richText = true, wordWrap = true };
        GUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label($"Custom Crafting — {CraftingStation.CustomCrafting.Capacity()} ingredient types");
        if (GUILayout.Button("Close", GUILayout.Width(100))) { Close(); GUIUtility.ExitGUI(); }
        GUILayout.EndHorizontal();
        ingredientScroll = GUILayout.BeginScrollView(ingredientScroll, GUILayout.Height(550));
        for (int i = 0; i < selections.Count; i++) DrawIngredientRow(i, selections[i]);
        GUILayout.EndScrollView();
        Refresh();
        resultScroll = GUILayout.BeginScrollView(resultScroll, GUILayout.Height(180));
        if (result.resultItem != null) GUILayout.Label("Result: " + resultName, resultStyle);
        else GUILayout.Label(counts.Count == 0 ? "Select ingredients to find a recipe." :
            string.IsNullOrWhiteSpace(result.resultText) ? "No recipe matches these items and quantities." : result.resultText, resultStyle);
        if (result.resultItem != null)
        {
            GUILayout.Label(result.resultText, resultStyle);
            GUILayout.Label($"Work time: {result.workMinutes} in-game minute(s)");
        }
        GUILayout.EndScrollView();
        GUI.enabled = canCraft;
        if (GUILayout.Button("Craft", GUILayout.MinHeight(60)))
        {
            CraftExecution.Start(player!, craftingData!, ingredients.ToList(), result, Close);
            GUIUtility.ExitGUI();
        }
        GUI.enabled = true;
        if (!canCraft && result.resultItem != null) GUILayout.Label(craftError);
    }

    private void DrawIngredientRow(int index, IngredientSelection selection)
    {
        available.TryGetValue(selection.ItemName, out int owned);
        GUILayout.BeginHorizontal(GUI.skin.box, GUILayout.MinHeight(44));
        GUILayout.Label($"Slot {index + 1}", GUILayout.Width(85));
        GUILayout.Label(selection.ItemName.Length == 0 ? "Empty" : selection.ItemName, GUILayout.MinWidth(360));
        if (GUILayout.Button("Choose", GUILayout.Width(100)))
        { pickerSlot = index; pickerSearch = ""; pickerScroll = Vector2.zero; pickerDirty = true; }
        GUI.enabled = selection.ItemName.Length > 0;
        if (GUILayout.Button("Clear", GUILayout.Width(85))) { selection.ItemName = ""; selection.Quantity = 0; dirty = true; }
        GUI.enabled = selection.Quantity > 1;
        if (GUILayout.Button("−", GUILayout.Width(48))) { selection.Quantity--; dirty = true; }
        GUI.enabled = selection.ItemName.Length > 0 && selection.Quantity < Math.Min(owned, RecipeRules.MaxQuantity);
        if (GUILayout.Button("+", GUILayout.Width(48))) { selection.Quantity++; dirty = true; }
        GUI.enabled = true;
        GUILayout.Label($"Qty {selection.Quantity} / {owned}", GUILayout.Width(145));
        GUILayout.EndHorizontal();
    }

    private void DrawItemPicker()
    {
        GUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label($"Choose ingredient for slot {pickerSlot + 1}");
        if (GUILayout.Button("Cancel", GUILayout.Width(100))) { pickerSlot = -1; GUIUtility.ExitGUI(); }
        GUILayout.EndHorizontal();
        GUILayout.Label("Search");
        pickerSearch = GUILayout.TextField(pickerSearch);
        if (pickerDirty || cachedPickerSearch != pickerSearch)
        {
            pickerDirty = false;
            cachedPickerSearch = pickerSearch;
            HashSet<string> selected = selections.Where((s, i) => i != pickerSlot).Select(s => s.ItemName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            pickerItems = ItemCatalog.Items.Where(item => available.ContainsKey(item.name) &&
                RecipeRegistry.IsIngredientName(CraftingStation.CustomCrafting, item.name) && !selected.Contains(item.name) &&
                item.name.IndexOf(pickerSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }
        GUILayout.Label($"{pickerItems.Count} available ingredient type(s)");
        Item? chosen = VirtualItemList.Draw(pickerItems, ref pickerScroll, item => $"{item.name}  •  Available: {available[item.name]}");
        if (chosen == null) return;
        selections[pickerSlot].ItemName = chosen.name;
        selections[pickerSlot].Quantity = 1;
        pickerSlot = -1;
        dirty = true;
        GUIUtility.ExitGUI();
    }

    private void Close()
    {
        open = false;
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= SceneChanged;
        if (inventory != null) inventory.OnInventoryChanged -= InventoryChanged;
        if (ownsInputBlock)
        {
            ownsInputBlock = false;
            if (player != null) player.SetInputBlock(false);
        }
        inventory = null;
        player = null;
        craftingData = null;
        result = ResultData.None;
        ingredients.Clear();
        pickerSlot = -1;
    }
    private void OnDisable() => Close();
    private void OnDestroy() { Close(); if (instance == this) instance = null; }
}
