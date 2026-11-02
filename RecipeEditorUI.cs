#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Silverpine.ModdingTools;
using Newtonsoft.Json;
using UnityEngine;

namespace SilverpineMods.CustomCraftingRecipes;

internal sealed class RecipeEditorUI : ModToolBehaviour
{
    private const float DesignWidth = 1920f;
    private const float DesignHeight = 1080f;

    private enum PickerTarget
    {
        None,
        Ingredient,
        Result
    }

    private static RecipeEditorUI? instance;

    private readonly List<RecipePackDocument> documents = new();
    private RecipePackDocument? document;
    private int selectedRecipe = -1;
    private int ingredientPickerIndex = -1;
    private PickerTarget pickerTarget;
    private Vector2 packScroll;
    private Vector2 recipeScroll;
    private Vector2 detailsScroll;
    private Vector2 pickerScroll;
    private string itemSearch = "";
    private string workMinutesText = "10";
    private string status = "";
    private bool customItemsOnly;
    private bool open;
    private string savedSnapshot = "";
    private Action? pendingChange;
    private bool frameworkHandlesEscape;
    private List<Item> pickerItems = new();
    private string cachedSearch = "";
    private bool cachedCustomOnly;
    private int cachedCatalogRevision = -1;

    internal static void Open(ModToolSession session)
    {
        if (instance == null)
        {
            GameObject root = new("Custom Recipe Editor IMGUI");
            instance = root.AddComponent<RecipeEditorUI>();
        }
        else
        {
            instance.gameObject.SetActive(true);
        }

        instance.AttachSession(session);
        session.Closed += instance.SessionClosed;
        var escapeProperty = typeof(ModToolSession).GetProperty("EscapeHandler");
        instance.frameworkHandlesEscape = escapeProperty != null;
        escapeProperty?.SetValue(session, (Func<bool>)instance.HandleEscape);
        ItemCatalog.Refresh(force: true);
        instance.open = true;
        instance.RefreshDocuments();
    }

    private RecipeDefinition? CurrentRecipe =>
        document?.Pack.recipes != null &&
        selectedRecipe >= 0 &&
        selectedRecipe < document.Pack.recipes.Count
            ? document.Pack.recipes[selectedRecipe]
            : null;

    private void Update()
    {
        if (!open) return;
        ItemCatalog.Refresh();
        if (!frameworkHandlesEscape && Input.GetKeyDown(KeyCode.Escape)) HandleEscape();
    }

    private bool HandleEscape()
    {
        if (pendingChange != null) pendingChange = null;
        else if (pickerTarget != PickerTarget.None) ClosePicker();
        else RequestChange(Close);
        return true;
    }

    private string Snapshot() => document == null ? "" : JsonConvert.SerializeObject(document.Pack);
    private void RequestChange(Action action)
    {
        if (document != null && (Snapshot() != savedSnapshot ||
            workMinutesText != (CurrentRecipe?.workMinutes.ToString() ?? "10")))
            pendingChange = action;
        else action();
    }

    private void DrawUnsavedPrompt()
    {
        GUILayout.BeginArea(new Rect(560, 390, 800, 260), GUI.skin.box);
        GUILayout.Label("This pack has unsaved changes.");
        GUILayout.Label("Save the pack, discard the changes, or return to editing.");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save and continue"))
        {
            Action next = pendingChange!;
            if (SaveCurrentPack()) { pendingChange = null; next(); }
            else pendingChange = null;
            GUIUtility.ExitGUI();
        }
        if (GUILayout.Button("Discard"))
        {
            Action next = pendingChange!;
            if (document != null && savedSnapshot.Length > 0)
            {
                var original = JsonConvert.DeserializeObject<RecipePackDefinition>(savedSnapshot)!;
                int index = documents.IndexOf(document);
                if (index >= 0) documents[index] = new RecipePackDocument(document.Path, original, document.SourceText);
            }
            else if (document != null) documents.Remove(document);
            pendingChange = null;
            next();
            GUIUtility.ExitGUI();
        }
        if (GUILayout.Button("Cancel")) { pendingChange = null; GUIUtility.ExitGUI(); }
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void OnGUI()
    {
        if (!open)
            return;

        using ModGuiScope scope = ModGui.BeginScaled(DesignWidth, DesignHeight);
        GUI.enabled = true;
        GUI.color = Color.white;
        GUI.backgroundColor = Color.white;
        GUI.depth = -1000;
        if (pendingChange != null) { DrawUnsavedPrompt(); return; }
        Color old = GUI.color;
        GUI.color = new Color(0.055f, 0.075f, 0.095f, 0.99f);
        GUI.DrawTexture(
            new Rect(0, 0, DesignWidth, DesignHeight),
            Texture2D.whiteTexture);
        GUI.color = old;

        GUILayout.BeginArea(
            new Rect(12, 10, DesignWidth - 24, DesignHeight - 20));
        DrawHeader();
        GUILayout.BeginHorizontal();
        DrawPackPanel();
        DrawRecipePanel();
        if (pickerTarget == PickerTarget.None)
            DrawDetails();
        else
            DrawItemPicker();
        GUILayout.EndHorizontal();
        if (!string.IsNullOrWhiteSpace(status))
            GUILayout.Label(status, GUI.skin.box);
        GUILayout.EndArea();
    }

    private void DrawHeader()
    {
        GUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label("Custom Recipe Editor", GUILayout.Width(260));
        GUILayout.Label(
            $"{documents.Count} pack(s) • " +
            $"{documents.Sum(value => value.Pack.recipes?.Count ?? 0)} " +
            $"recipe(s) • {CountCustomItems()} custom item(s) loaded");
        if (GUILayout.Button("Refresh Packs", GUILayout.Width(130)))
            { RequestChange(RefreshDocuments); GUIUtility.ExitGUI(); }
        GUI.enabled = document != null;
        if (GUILayout.Button("Save Pack", GUILayout.Width(110)))
            { SaveCurrentPack(); GUIUtility.ExitGUI(); }
        GUI.enabled = true;
        if (GUILayout.Button("Close", GUILayout.Width(90)))
            { RequestChange(Close); GUIUtility.ExitGUI(); }
        GUILayout.EndHorizontal();
    }

    private void DrawPackPanel()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(330));
        GUILayout.Label("Recipe packs");
        if (GUILayout.Button("New Pack"))
            { RequestChange(CreatePack); GUIUtility.ExitGUI(); }

        packScroll = GUILayout.BeginScrollView(packScroll);
        foreach (RecipePackDocument candidate in documents)
        {
            string label = string.IsNullOrWhiteSpace(candidate.Pack.displayName)
                ? candidate.Pack.packId
                : candidate.Pack.displayName;
            if (!candidate.Pack.enabled)
                label += " (disabled)";
            Color old = GUI.backgroundColor;
            if (ReferenceEquals(candidate, document))
                GUI.backgroundColor = new Color(0.55f, 0.75f, 0.92f);
            if (GUILayout.Button(label, GUILayout.MinHeight(44)))
            {
                if (!ReferenceEquals(candidate, document))
                    RequestChange(() => SelectDocument(documents.FirstOrDefault(value => value.Path == candidate.Path) ?? candidate));
                GUIUtility.ExitGUI();
            }
            GUI.backgroundColor = old;
        }
        GUILayout.EndScrollView();

        if (document != null)
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = !document.Pack.enabled;
            if (GUILayout.Button("Enable Pack"))
                SetPackEnabled(true);
            GUI.enabled = document.Pack.enabled;
            if (GUILayout.Button("Disable Pack"))
                SetPackEnabled(false);
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Label(document.Pack.enabled
                ? "Enabled — recipes are active."
                : "Disabled — recipes are ignored.");
            GUILayout.Label("Pack ID");
            document.Pack.packId = GUILayout.TextField(
                document.Pack.packId ?? "");
            GUILayout.Label("Saving updates the folder to match Pack ID.");
            GUILayout.Label("Display name");
            document.Pack.displayName = GUILayout.TextField(
                document.Pack.displayName ?? "");
            if (!string.IsNullOrWhiteSpace(document.Path))
            {
                GUILayout.Label(
                    Path.GetFileName(
                        Path.GetDirectoryName(document.Path) ?? ""));
            }
        }
        GUILayout.EndVertical();
    }

    private void DrawRecipePanel()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(380));
        GUILayout.Label("Recipes");
        GUI.enabled = document != null;
        if (GUILayout.Button("New Recipe"))
            CreateRecipe();

        recipeScroll = GUILayout.BeginScrollView(recipeScroll);
        if (document?.Pack.recipes != null)
        {
            for (int index = 0;
                 index < document.Pack.recipes.Count;
                 index++)
            {
                RecipeDefinition recipe = document.Pack.recipes[index] ??= new RecipeDefinition { enabled = false };
                string name = string.IsNullOrWhiteSpace(recipe.id)
                    ? $"Recipe {index + 1}"
                    : recipe.id;
                if (!recipe.enabled)
                    name += " (disabled)";
                string stations = recipe.stations == null
                    ? ""
                    : string.Join(", ", recipe.stations);
                Color old = GUI.backgroundColor;
                if (index == selectedRecipe)
                    GUI.backgroundColor = new Color(0.55f, 0.75f, 0.92f);
                if (GUILayout.Button(
                        string.IsNullOrWhiteSpace(stations)
                            ? name
                            : name + "\n" + stations,
                        GUILayout.MinHeight(48)))
                    SelectRecipe(index);
                GUI.backgroundColor = old;
            }
        }
        GUILayout.EndScrollView();

        GUI.enabled = CurrentRecipe != null;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Duplicate"))
            DuplicateRecipe();
        if (GUILayout.Button("Delete"))
            DeleteRecipe();
        GUILayout.EndHorizontal();
        GUI.enabled = true;
        GUILayout.EndVertical();
    }

    private void DrawDetails()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        RecipeDefinition? recipe = CurrentRecipe;
        if (recipe == null)
        {
            GUILayout.Label("Create or select a recipe.");
            GUILayout.EndVertical();
            return;
        }

        detailsScroll = GUILayout.BeginScrollView(detailsScroll);
        recipe.enabled = GUILayout.Toggle(recipe.enabled, "Recipe enabled");

        GUILayout.Label("Recipe ID");
        recipe.id = GUILayout.TextField(recipe.id ?? "");
        string qualifiedId = string.IsNullOrWhiteSpace(document?.Pack.packId)
            ? recipe.id
            : document.Pack.packId + ":" + recipe.id;
        GUILayout.Label("Qualified ID: " + qualifiedId);

        GUILayout.Space(8);
        GUILayout.Label("Crafting stations");
        long ingredientTotal = IngredientTotal(recipe);
        int distinctIngredients = DistinctIngredientCount(recipe);
        recipe.stations ??= new List<string>();
        foreach (CraftingStation station in Enum.GetValues(
                     typeof(CraftingStation)))
        {
            string stationName = station.ToString();
            bool selected = recipe.stations.Any(value =>
                string.Equals(value?.Trim(), stationName, StringComparison.OrdinalIgnoreCase));
            bool fits = ingredientTotal <= int.MaxValue && station.Fits(distinctIngredients, (int)ingredientTotal);
            GUI.enabled = fits || selected;
            bool next = GUILayout.Toggle(
                selected,
                station == CraftingStation.CustomCrafting
                    ? $"{stationName} ({station.Capacity()} quantity slots)"
                    : $"{stationName} ({station.Capacity()} slots)");
            GUI.enabled = true;
            if (next && !selected)
                recipe.stations.Add(stationName);
            else if (!next && selected)
            {
                recipe.stations.RemoveAll(value =>
                    string.Equals(value?.Trim(), stationName, StringComparison.OrdinalIgnoreCase));
            }
            if (!fits)
            {
                GUILayout.Label(
                    station == CraftingStation.CustomCrafting
                        ? $"Requires {distinctIngredients} distinct ingredients; " +
                          $"{stationName} has {station.Capacity()} quantity slots."
                        : $"Requires {ingredientTotal} total items; {stationName} " +
                          $"has {station.Capacity()} slots.",
                    GUI.skin.box);
            }
        }

        GUILayout.Space(8);
        GUILayout.Label(
            $"Ingredients ({DistinctIngredientCount(recipe)}/{CraftingStation.CustomCrafting.Capacity()} types, " +
            $"{ingredientTotal} total items)");
        recipe.ingredients ??= new List<IngredientDefinition>();
        int removeIndex = -1;
        for (int index = 0; index < recipe.ingredients.Count; index++)
        {
            IngredientDefinition ingredient = recipe.ingredients[index] ??= new IngredientDefinition();
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"{index + 1}.", GUILayout.Width(28));
            ingredient.item = GUILayout.TextField(
                ingredient.item ?? "",
                GUILayout.MinWidth(230));
            if (GUILayout.Button("Browse", GUILayout.Width(78)))
                OpenPicker(PickerTarget.Ingredient, index);
            GUILayout.Label("Qty", GUILayout.Width(30));
            GUI.enabled = ingredient.amount > 1;
            if (GUILayout.Button("-", GUILayout.Width(30)))
                ingredient.amount--;
            GUI.enabled = ingredient.amount < 999;
            if (GUILayout.Button("+", GUILayout.Width(30)))
                ingredient.amount++;
            GUI.enabled = true;
            GUILayout.Label(
                Math.Max(1, ingredient.amount).ToString(),
                GUILayout.Width(25));
            if (GUILayout.Button("Remove", GUILayout.Width(72)))
                removeIndex = index;
            GUILayout.EndHorizontal();
            // Invalid imported quantities stay visible until explicitly corrected.
        }
        if (removeIndex >= 0)
            recipe.ingredients.RemoveAt(removeIndex);
        GUI.enabled = recipe.ingredients.Count <
                      CraftingStation.CustomCrafting.Capacity();
        if (GUILayout.Button("Add Ingredient", GUILayout.Width(180)))
            recipe.ingredients.Add(new IngredientDefinition());
        GUI.enabled = true;

        GUILayout.Space(8);
        GUILayout.Label("Result item");
        GUILayout.BeginHorizontal();
        recipe.result = GUILayout.TextField(
            recipe.result ?? "",
            GUILayout.MinWidth(330));
        if (GUILayout.Button("Browse Items", GUILayout.Width(120)))
            OpenPicker(PickerTarget.Result, -1);
        GUILayout.EndHorizontal();
        GUILayout.Label(
            "The browser includes native and live Custom Item Loader items. " +
            "Quality|Item Name may be entered manually.");

        GUILayout.Space(8);
        GUILayout.Label("Work time (in-game minutes, 1–1440)");
        workMinutesText = GUILayout.TextField(
            workMinutesText,
            GUILayout.Width(180));
        if (int.TryParse(workMinutesText, out int minutes))
            recipe.workMinutes = minutes;

        GUILayout.Label("Result text / crafting description");
        recipe.description = GUILayout.TextArea(
            recipe.description ?? "",
            GUILayout.MinHeight(100));

        GUILayout.Space(10);
        List<string> problems = ValidateRecipe(recipe);
        if (!int.TryParse(workMinutesText, out _)) problems.Add("Work time must be a whole number.");
        GUILayout.Label(
            problems.Count == 0
                ? "Recipe is valid."
                : "Fix before enabling/saving:\n• " +
                  string.Join("\n• ", problems),
            GUI.skin.box);
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void DrawItemPicker()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            pickerTarget == PickerTarget.Result
                ? "Choose result item"
                : "Choose ingredient",
            GUILayout.Width(210));
        GUILayout.Label(
            "Custom Item Loader entries come from the live ItemLibrary.");
        if (GUILayout.Button("Cancel", GUILayout.Width(90)))
            ClosePicker();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Search", GUILayout.Width(60));
        itemSearch = GUILayout.TextField(itemSearch);
        customItemsOnly = GUILayout.Toggle(
            customItemsOnly,
            "Custom only",
            GUILayout.Width(120));
        if (GUILayout.Button("Clear", GUILayout.Width(70)))
            itemSearch = "";
        GUILayout.EndHorizontal();

        if (cachedCatalogRevision != ItemCatalog.Revision || cachedSearch != itemSearch || cachedCustomOnly != customItemsOnly)
        {
            cachedCatalogRevision = ItemCatalog.Revision;
            cachedSearch = itemSearch;
            cachedCustomOnly = customItemsOnly;
            pickerItems = ItemCatalog.Items
            .Where(item =>
                (!customItemsOnly || IsCustomItem(item)) &&
                (string.IsNullOrWhiteSpace(itemSearch) ||
                 item.name.IndexOf(
                     itemSearch,
                     StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();
        }
        List<Item> items = pickerItems;

        GUILayout.Label($"{items.Count} matching item(s)");
        Item? chosen = VirtualItemList.Draw(items, ref pickerScroll,
            item => (IsCustomItem(item) ? "[Custom] " : "[Native] ") + item.name + "  •  " + item.itemCategory);
        if (chosen != null) { ChooseItem(chosen.name); GUIUtility.ExitGUI(); }
        GUILayout.EndVertical();
    }

    private void RefreshDocuments()
    {
        string previousPath = document?.Path ?? "";
        List<RecipePackDocument> loaded;
        try { loaded = RecipeRegistry.LoadEditablePacks(); }
        catch (Exception exception)
        {
            status = "Could not refresh packs: " + exception.Message;
            Plugin.Log.LogError(status);
            return;
        }
        documents.Clear();
        documents.AddRange(loaded);
        RecipePackDocument? restored = documents.FirstOrDefault(value =>
            value.Path.Equals(
                previousPath,
                StringComparison.OrdinalIgnoreCase));
        if (restored != null)
            SelectDocument(restored);
        else if (documents.Count > 0)
            SelectDocument(documents[0]);
        else
        {
            document = null;
            selectedRecipe = -1;
            SyncSelectedFields();
        }
        status = $"Discovered {documents.Count} recipe pack(s).";
    }

    private void SelectDocument(RecipePackDocument value)
    {
        document = value;
        document.Pack.recipes ??= new List<RecipeDefinition>();
        savedSnapshot = Snapshot();
        selectedRecipe = document.Pack.recipes.Count == 0 ? -1 : 0;
        pickerTarget = PickerTarget.None;
        SyncSelectedFields();
        status = "Loaded " + value.Pack.displayName + ".";
    }

    private void CreatePack()
    {
        RecipePackDefinition pack = new()
        {
            packId = NextPackId(),
            displayName = "New Recipe Pack",
            enabled = true,
            recipes = new List<RecipeDefinition>
            {
                NewRecipe("new-recipe-1")
            }
        };
        document = new RecipePackDocument("", pack);
        selectedRecipe = 0;
        SyncSelectedFields();
        documents.Add(document);
        savedSnapshot = "";
        status = "New pack created. Save it to create its .recipe.pack folder.";
    }

    private bool SaveCurrentPack(bool validateRecipes = true)
    {
        if (document == null)
            return false;

        try
        {
            if (CurrentRecipe != null && !int.TryParse(workMinutesText, out _))
                throw new InvalidDataException("Work time must be a whole number.");
            ValidatePackMetadata(document.Pack);
            if (validateRecipes && document.Pack.enabled)
            {
                List<string> problems = ValidatePack(document.Pack);
                if (problems.Count > 0)
                    throw new InvalidDataException(string.Join(" ", problems));
            }

            RecipeRegistry.SavePack(document);
            string savedPath = document.Path;
            string selectedId = CurrentRecipe?.id ?? "";
            documents.Clear();
            documents.AddRange(RecipeRegistry.LoadEditablePacks());
            RecipePackDocument? saved = documents.FirstOrDefault(value =>
                value.Path.Equals(
                    savedPath,
                    StringComparison.OrdinalIgnoreCase));
            if (saved != null)
            {
                SelectDocument(saved);
                int restored = saved.Pack.recipes.FindIndex(recipe => recipe?.id == selectedId);
                if (restored >= 0) { selectedRecipe = restored; SyncSelectedFields(); }
            }
            status = $"Saved {Path.GetFileName(Path.GetDirectoryName(savedPath))} " +
                     "and reloaded all recipe packs.";
            return true;
        }
        catch (Exception exception)
        {
            status = "Save failed: " + exception.Message;
            Plugin.Log.LogError("Recipe pack save failed: " + exception);
            return false;
        }
    }

    private void SetPackEnabled(bool enabled)
    {
        if (document == null || document.Pack.enabled == enabled)
            return;
        bool previous = document.Pack.enabled;
        document.Pack.enabled = enabled;
        if (!SaveCurrentPack(validateRecipes: enabled))
        {
            document.Pack.enabled = previous;
            return;
        }
        status = enabled
            ? "Pack enabled and recipes reloaded."
            : "Pack disabled and its recipes were unloaded.";
    }

    private List<string> ValidatePack(RecipePackDefinition pack)
    {
        List<string> problems = new();
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> signatures =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (RecipePackDocument other in documents.Where(value => !ReferenceEquals(value, document) && value.Pack.enabled))
            foreach (RecipeDefinition? existing in other.Pack.recipes ?? new List<RecipeDefinition>())
            {
                if (existing == null || !existing.enabled || RecipeRules.Validate(existing).Count != 0) continue;
                string signature = IngredientSignature(existing);
                foreach (string station in existing.stations)
                    if (RecipeRules.TryStation(station, out CraftingStation parsed)) signatures.Add(parsed + ":" + signature);
            }
        for (int index = 0; index < pack.recipes.Count; index++)
        {
            RecipeDefinition? recipe = pack.recipes[index];
            if (recipe == null) { problems.Add($"Recipe {index + 1} is null."); continue; }
            if (!ids.Add(recipe.id?.Trim() ?? ""))
                problems.Add($"Duplicate recipe ID '{recipe.id}'.");
            if (!recipe.enabled)
                continue;
            List<string> recipeProblems = ValidateRecipe(recipe);
            if (recipeProblems.Count > 0)
            {
                problems.Add(
                    $"Recipe {index + 1}: {string.Join("; ", recipeProblems)}");
                continue;
            }
            string ingredientSignature = IngredientSignature(recipe);
            foreach (string station in recipe.stations.Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                RecipeRules.TryStation(station, out CraftingStation parsed);
                if (!signatures.Add(parsed + ":" + ingredientSignature))
                {
                    problems.Add(
                        $"Recipe {index + 1} duplicates another {station} " +
                        "ingredient combination in an enabled pack.");
                }
            }
        }
        return problems;
    }

    private void ValidatePackMetadata(RecipePackDefinition pack)
    {
        RecipePackStore.FolderName(pack.packId); // Validate before changing any files.
        RecipePackDocument? duplicate = documents.FirstOrDefault(value =>
            !ReferenceEquals(value, document) &&
            string.Equals(value.Pack.packId?.Trim(), pack.packId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (duplicate != null)
            throw new InvalidDataException(
                $"Pack ID '{pack.packId}' is already in use.");
        if (string.IsNullOrWhiteSpace(pack.displayName))
            pack.displayName = pack.packId.Trim();
        pack.packId = pack.packId.Trim();
        pack.recipes ??= new List<RecipeDefinition>();
    }

    internal static List<string> ValidateRecipe(RecipeDefinition recipe)
    {
        List<string> problems = RecipeRules.Validate(recipe);
        foreach (IngredientDefinition? ingredient in recipe.ingredients ?? new List<IngredientDefinition>())
            if (ingredient != null && !string.IsNullOrWhiteSpace(ingredient.item) && !ItemCatalog.Contains(ingredient.item, false))
                problems.Add($"Ingredient '{ingredient.item}' is not loaded.");
        if (!string.IsNullOrWhiteSpace(recipe.result) && !ItemCatalog.Contains(recipe.result, true))
            problems.Add($"Result '{recipe.result}' is not loaded or its quality prefix is invalid.");
        return problems;
    }

    private static string IngredientSignature(RecipeDefinition recipe) => RecipeRules.Signature(RecipeRules.Counts(recipe));
    private static long IngredientTotal(RecipeDefinition recipe) =>
        recipe.ingredients?.Sum(ingredient => (long)Math.Max(0, ingredient?.amount ?? 0)) ?? 0;
    private static int DistinctIngredientCount(RecipeDefinition recipe) =>
        recipe.ingredients?.Where(ingredient => ingredient != null && !string.IsNullOrWhiteSpace(ingredient.item))
            .Select(ingredient => ingredient.item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() ?? 0;

    private void CreateRecipe()
    {
        if (document == null)
            return;
        document.Pack.recipes ??= new List<RecipeDefinition>();
        int suffix = 1;
        HashSet<string> ids = document.Pack.recipes
            .Select(recipe => recipe?.id ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string id;
        do
        {
            id = "new-recipe-" + suffix++;
        }
        while (ids.Contains(id) ||
            Directory.Exists(Path.Combine(RecipeRegistry.DefinitionDirectory, RecipePackStore.FolderName(id))) ||
            File.Exists(Path.Combine(RecipeRegistry.DefinitionDirectory, RecipePackStore.FolderName(id))));
        document.Pack.recipes.Add(NewRecipe(id));
        SelectRecipe(document.Pack.recipes.Count - 1);
        status = "New recipe added to the pack draft.";
    }

    private static RecipeDefinition NewRecipe(string id) => new()
    {
        id = id,
        enabled = true,
        stations = new List<string>
        {
            nameof(CraftingStation.CustomCrafting)
        },
        ingredients = new List<IngredientDefinition>
        {
            new()
        },
        result = "",
        workMinutes = 10,
        description = ""
    };

    private void DuplicateRecipe()
    {
        RecipeDefinition? source = CurrentRecipe;
        if (source == null || document == null)
            return;
        RecipeDefinition copy = new()
        {
            id = (source.id ?? "recipe") + "-copy",
            enabled = source.enabled,
            stations = source.stations?.ToList() ?? new List<string>(),
            ingredients = source.ingredients?
                .Where(ingredient => ingredient != null)
                .Select(ingredient => new IngredientDefinition
                {
                    item = ingredient.item,
                    amount = ingredient.amount
                })
                .ToList() ?? new List<IngredientDefinition>(),
            result = source.result,
            workMinutes = source.workMinutes,
            description = source.description
        };
        document.Pack.recipes.Add(copy);
        SelectRecipe(document.Pack.recipes.Count - 1);
        status = "Recipe duplicated. Change its ID or ingredients.";
    }

    private void DeleteRecipe()
    {
        if (CurrentRecipe == null || document == null)
            return;
        document.Pack.recipes.RemoveAt(selectedRecipe);
        selectedRecipe = Math.Min(
            selectedRecipe,
            document.Pack.recipes.Count - 1);
        SyncSelectedFields();
        status = "Recipe removed from the draft. Save to apply.";
    }

    private void SelectRecipe(int index)
    {
        if (CurrentRecipe != null && !int.TryParse(workMinutesText, out _))
        { status = "Enter a valid work time before changing recipes."; return; }
        selectedRecipe = index;
        ClosePicker();
        detailsScroll = Vector2.zero;
        SyncSelectedFields();
    }

    private void SyncSelectedFields()
    {
        workMinutesText = CurrentRecipe?.workMinutes.ToString() ?? "10";
    }

    private string NextPackId()
    {
        int suffix = 1;
        HashSet<string> ids = documents
            .Select(value => value.Pack.packId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string id;
        do
        {
            id = "my.recipe.pack" + suffix++;
        }
        while (ids.Contains(id));
        return id;
    }

    private void OpenPicker(PickerTarget target, int ingredientIndex)
    {
        pickerTarget = target;
        ingredientPickerIndex = ingredientIndex;
        itemSearch = "";
        customItemsOnly = false;
        pickerScroll = Vector2.zero;
    }

    private void ClosePicker()
    {
        pickerTarget = PickerTarget.None;
        ingredientPickerIndex = -1;
    }

    private void ChooseItem(string name)
    {
        RecipeDefinition? recipe = CurrentRecipe;
        if (recipe == null)
            return;
        if (pickerTarget == PickerTarget.Result)
            recipe.result = name;
        else if (ingredientPickerIndex >= 0 &&
                 ingredientPickerIndex < recipe.ingredients.Count)
            recipe.ingredients[ingredientPickerIndex].item = name;
        ClosePicker();
    }

    private static bool IsCustomItem(Item item) =>
        item.spriteName?.StartsWith(
            "custom:",
            StringComparison.OrdinalIgnoreCase) == true;

    private static int CountCustomItems() => ItemCatalog.CustomCount;

    private void SessionClosed()
    {
        open = false;
        pendingChange = null;
        pickerTarget = PickerTarget.None;
        document = null;
        documents.Clear();
    }

    private void Close()
    {
        open = false;
        pendingChange = null;
        pickerTarget = PickerTarget.None;
        document = null;
        documents.Clear();
        gameObject.SetActive(false);
        ReleaseSession();
    }
}
