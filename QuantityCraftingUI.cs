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
    private CraftingStation station;
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
    private bool browsingRecipes;
    private string recipeSearch = "", lastRecipeSearch = "";
    private int bookRevision = -1;
    private Vector2 bookScroll;
    private List<CompiledRecipe> bookEntries = new();
    private readonly Dictionary<CompiledRecipe,string?> bookLocks = new();
    private string? skillLock;
    private int accessRevision = -1;
    private float nextAccessPoll;

    internal static void Open(CraftingUIData data, CraftingStation station = CraftingStation.CustomCrafting)
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
        instance.station = station;
        instance.bookRevision = -1;
        instance.accessRevision = -1;
        instance.ingredientScroll = instance.resultScroll = Vector2.zero;
        instance.player = current;
        instance.inventory = current.worldInventory.inventory;
        instance.inventory.OnInventoryChanged += instance.InventoryChanged;
        instance.pickerSlot = -1;
        instance.pickerSearch = "";
        instance.selections.Clear();
        for (int i = 0; i < station.Capacity(); i++) instance.selections.Add(new IngredientSelection());
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
            if (browsingRecipes) browsingRecipes = false;
            else if (pickerSlot >= 0) pickerSlot = -1;
            else Close();
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
        if (!dirty) { RefreshAccess(false); return; }
        dirty = false;
        pickerDirty = true;
        counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (IngredientSelection selection in selections)
            if (selection.Quantity > 0 && selection.ItemName.Length > 0) counts[selection.ItemName] = selection.Quantity;
        RecipeRegistry.TryGetConfiguredResult(station, counts, out result);
        resultName = result.resultItem != null ? result.resultItem.GetFinalName() : "";
        ingredients = CraftTransaction.Select(inventory!, counts);
        craftError = "";
        canCraft = result.resultItem != null && CraftTransaction.CanComplete(inventory!, ingredients, BatchResult.Units(result), out craftError);
        RefreshAccess(true);
    }

    private void RefreshAccess(bool resultChanged)
    {
        int revision = CraftingApi.AccessRevision;
        bool poll = accessRevision != revision || Time.unscaledTime >= nextAccessPoll;
        if (poll)
        {
            accessRevision = revision; nextAccessPoll = Time.unscaledTime + 0.5f;
            bookLocks.Clear();
        }
        if (poll || resultChanged) skillLock = CraftingApi.LockReason(player!,station,result);
    }

    private string? BookLock(CompiledRecipe recipe)
    {
        if (!bookLocks.TryGetValue(recipe,out var reason))
            bookLocks[recipe] = reason = CraftingApi.LockReason(player!,station,recipe);
        return reason;
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
        if (browsingRecipes) DrawRecipeBook();
        else if (pickerSlot >= 0) DrawItemPicker();
        else DrawCraftingPanel();
        GUILayout.EndArea();
    }

    private void DrawCraftingPanel()
    {
        resultStyle ??= new GUIStyle(GUI.skin.box) { richText = true, wordWrap = true };
        GUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label($"{station.Label()} — {station.Capacity()} ingredient types");
        if (GUILayout.Button("Browse recipes", GUILayout.Width(180)))
        {
            browsingRecipes = true; recipeSearch = ""; bookScroll = Vector2.zero; GUIUtility.ExitGUI();
        }
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
        GUI.enabled = canCraft && skillLock == null;
        if (GUILayout.Button("Craft", GUILayout.MinHeight(60)))
        {
            CraftExecution.Start(player!, craftingData!, station, ingredients.ToList(), result, Close);
            GUIUtility.ExitGUI();
        }
        GUI.enabled = true;
        if (skillLock != null) GUILayout.Label("Locked: " + skillLock);
        else if (!canCraft && result.resultItem != null) GUILayout.Label(craftError);
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
                RecipeRegistry.IsIngredientName(station, item.name) && !selected.Contains(item.name) &&
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

    private void DrawRecipeBook()
    {
        GUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label($"{station.Label()} recipes — choose one to fill the slots. Nothing is consumed until you craft.");
        if (GUILayout.Button("Back", GUILayout.Width(100))) { browsingRecipes = false; GUIUtility.ExitGUI(); }
        GUILayout.EndHorizontal();
        GUILayout.Label("Search by result, ingredient, or pack ID");
        recipeSearch = GUILayout.TextField(recipeSearch);
        if (bookRevision != RecipeRegistry.Revision || lastRecipeSearch != recipeSearch)
        {
            bookEntries = RecipeRegistry.BrowseBenchRecipes(recipeSearch, station);
            lastRecipeSearch = recipeSearch; bookRevision = RecipeRegistry.Revision; bookScroll = Vector2.zero;
        }
        if (bookEntries.Count == 0) { GUILayout.Label("No matching workbench recipes in enabled packs."); return; }
        var choice = VirtualItemList.Draw(bookEntries, ref bookScroll, r => r.ResultAmount + " × " + r.ResultName + "  •  " + r.WorkMinutes +
            " min\n" + string.Join(", ", r.RequiredItems.Select(p => $"{p.Value} × {p.Key}")), lockedReason: BookLock);
        if (choice == null) return;
        int i = 0;
        foreach (var entry in choice.RequiredItems)
        {
            selections[i].ItemName = entry.Key;
            selections[i++].Quantity = entry.Value;
        }
        for (; i < selections.Count; i++) { selections[i].ItemName = ""; selections[i].Quantity = 0; }
        browsingRecipes = false;
        dirty = true;
        GUIUtility.ExitGUI();
    }

    private void Close()
    {
        open = false;
        browsingRecipes = false;
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
        bookLocks.Clear();
        pickerSlot = -1;
    }
    private void OnDisable() => Close();
    private void OnDestroy() { Close(); if (instance == this) instance = null; }
}
