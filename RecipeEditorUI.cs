#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Silverpine.ModdingTools;
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
        if (!open || !Input.GetKeyDown(KeyCode.Escape))
            return;
        if (pickerTarget != PickerTarget.None)
            ClosePicker();
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
        GUI.depth = -1000;

        using ModGuiScope scope = ModGui.BeginScaled(
            DesignWidth,
            DesignHeight);
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
            RefreshDocuments();
        GUI.enabled = document != null;
        if (GUILayout.Button("Save Pack", GUILayout.Width(110)))
            SaveCurrentPack();
        GUI.enabled = true;
        if (GUILayout.Button("Close", GUILayout.Width(90)))
            Close();
        GUILayout.EndHorizontal();
    }

    private void DrawPackPanel()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(330));
        GUILayout.Label("Recipe packs");
        if (GUILayout.Button("New Pack"))
            CreatePack();

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
                SelectDocument(candidate);
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
                RecipeDefinition recipe = document.Pack.recipes[index];
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
        int ingredientTotal = IngredientTotal(recipe);
        int distinctIngredients = DistinctIngredientCount(recipe);
        recipe.stations ??= new List<string>();
        foreach (CraftingStation station in Enum.GetValues(
                     typeof(CraftingStation)))
        {
            string stationName = station.ToString();
            bool selected = recipe.stations.Any(value =>
                value.Equals(stationName, StringComparison.OrdinalIgnoreCase));
            bool fits = station.Fits(distinctIngredients, ingredientTotal);
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
                    value.Equals(
                        stationName,
                        StringComparison.OrdinalIgnoreCase));
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
            $"Ingredients ({recipe.ingredients?.Count ?? 0}/5 types, " +
            $"{ingredientTotal} total items)");
        recipe.ingredients ??= new List<IngredientDefinition>();
        int removeIndex = -1;
        for (int index = 0; index < recipe.ingredients.Count; index++)
        {
            IngredientDefinition ingredient = recipe.ingredients[index];
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
            ingredient.amount = Math.Max(1, ingredient.amount);
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
            recipe.workMinutes = Mathf.Clamp(minutes, 1, 1440);

        GUILayout.Label("Result text / crafting description");
        recipe.description = GUILayout.TextArea(
            recipe.description ?? "",
            GUILayout.MinHeight(100));

        GUILayout.Space(10);
        List<string> problems = ValidateRecipe(recipe);
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

        List<Item> items = ItemLibrary.Items
            .Where(item => item != null)
            .GroupBy(item => item.name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(item =>
                (!customItemsOnly || IsCustomItem(item)) &&
                (string.IsNullOrWhiteSpace(itemSearch) ||
                 item.name.IndexOf(
                     itemSearch,
                     StringComparison.OrdinalIgnoreCase) >= 0))
            .OrderBy(item => IsCustomItem(item) ? 0 : 1)
            .ThenBy(item => item.name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        GUILayout.Label($"{items.Count} matching item(s)");
        pickerScroll = GUILayout.BeginScrollView(pickerScroll);
        foreach (Item item in items)
        {
            string prefix = IsCustomItem(item) ? "[Custom] " : "[Native] ";
            if (GUILayout.Button(
                    prefix + item.name + "  •  " + item.itemCategory,
                    GUILayout.MinHeight(34)))
                ChooseItem(item.name);
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void RefreshDocuments()
    {
        string previousPath = document?.Path ?? "";
        documents.Clear();
        documents.AddRange(RecipeRegistry.LoadEditablePacks());
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
        status = "New pack created. Save it to create its .recipe.pack folder.";
    }

    private bool SaveCurrentPack(bool validateRecipes = true)
    {
        if (document == null)
            return false;

        try
        {
            ValidatePackMetadata(document.Pack);
            if (validateRecipes)
            {
                List<string> problems = ValidatePack(document.Pack);
                if (problems.Count > 0)
                    throw new InvalidDataException(string.Join(" ", problems));
            }

            RecipeRegistry.SavePack(document);
            string savedPath = document.Path;
            documents.Clear();
            documents.AddRange(RecipeRegistry.LoadEditablePacks());
            RecipePackDocument? saved = documents.FirstOrDefault(value =>
                value.Path.Equals(
                    savedPath,
                    StringComparison.OrdinalIgnoreCase));
            if (saved != null)
                SelectDocument(saved);
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
        for (int index = 0; index < pack.recipes.Count; index++)
        {
            RecipeDefinition recipe = pack.recipes[index];
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
            foreach (string station in recipe.stations)
            {
                if (!signatures.Add(station + ":" + ingredientSignature))
                {
                    problems.Add(
                        $"Recipe {index + 1} duplicates another {station} " +
                        "ingredient combination in this pack.");
                }
            }
        }
        return problems;
    }

    private void ValidatePackMetadata(RecipePackDefinition pack)
    {
        if (string.IsNullOrWhiteSpace(pack.packId))
            throw new InvalidDataException("Pack ID is required.");
        RecipePackDocument? duplicate = documents.FirstOrDefault(value =>
            !ReferenceEquals(value, document) &&
            value.Pack.packId.Equals(
                pack.packId,
                StringComparison.OrdinalIgnoreCase));
        if (duplicate != null)
            throw new InvalidDataException(
                $"Pack ID '{pack.packId}' is already in use.");
        if (string.IsNullOrWhiteSpace(pack.displayName))
            pack.displayName = pack.packId.Trim();
        pack.recipes ??= new List<RecipeDefinition>();
    }

    private static List<string> ValidateRecipe(RecipeDefinition recipe)
    {
        List<string> problems = new();
        if (string.IsNullOrWhiteSpace(recipe.id))
            problems.Add("Recipe ID is required.");
        if (recipe.ingredients == null || recipe.ingredients.Count == 0)
            problems.Add("At least one ingredient is required.");
        else
        {
            if (recipe.ingredients.Any(ingredient =>
                    string.IsNullOrWhiteSpace(ingredient.item)))
                problems.Add("Every ingredient needs an item.");
            foreach (IngredientDefinition ingredient in recipe.ingredients
                         .Where(ingredient =>
                             !string.IsNullOrWhiteSpace(ingredient.item)))
            {
                if (!ItemExists(ingredient.item))
                    problems.Add(
                        $"Ingredient '{ingredient.item}' is not loaded.");
            }
            if (recipe.ingredients.Count >
                CraftingStation.CustomCrafting.Capacity())
                problems.Add("Recipes may use at most ten ingredient types.");
            int duplicateTypes = recipe.ingredients
                .Where(ingredient =>
                    !string.IsNullOrWhiteSpace(ingredient.item))
                .GroupBy(
                    ingredient => ingredient.item.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Count(group => group.Count() > 1);
            if (duplicateTypes > 0)
                problems.Add(
                    "Combine duplicate ingredient rows by increasing quantity.");
        }
        if (recipe.stations == null || recipe.stations.Count == 0)
            problems.Add("Select at least one crafting station.");
        else
        {
            int total = IngredientTotal(recipe);
            int distinct = DistinctIngredientCount(recipe);
            foreach (string value in recipe.stations)
            {
                if (!Enum.TryParse(
                        value,
                        ignoreCase: true,
                        out CraftingStation station))
                    problems.Add($"Unknown station '{value}'.");
                else if (!station.Fits(distinct, total))
                    problems.Add(
                        station == CraftingStation.CustomCrafting
                            ? $"{station} has only {station.Capacity()} " +
                              "distinct ingredient slots."
                            : $"{station} has only {station.Capacity()} slots.");
            }
        }
        if (string.IsNullOrWhiteSpace(recipe.result))
            problems.Add("A result item is required.");
        else if (!ItemExists(recipe.result))
            problems.Add($"Result '{recipe.result}' is not loaded.");
        if (recipe.workMinutes < 1 || recipe.workMinutes > 1440)
            problems.Add("Work time must be between 1 and 1440 minutes.");
        return problems;
    }

    private static bool ItemExists(string configuredName)
    {
        int separator = configuredName.LastIndexOf('|');
        string name = separator >= 0
            ? configuredName.Substring(separator + 1)
            : configuredName;
        return ItemLibrary.Items.Any(item =>
            string.Equals(item.name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string IngredientSignature(RecipeDefinition recipe) =>
        string.Join(
            "|",
            recipe.ingredients
                .Where(ingredient =>
                    !string.IsNullOrWhiteSpace(ingredient.item))
                .GroupBy(
                    ingredient => ingredient.item.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group.Key.ToLowerInvariant() + "=" +
                    group.Sum(ingredient => Math.Max(1, ingredient.amount))));

    private static int IngredientTotal(RecipeDefinition recipe) =>
        recipe.ingredients?.Sum(ingredient =>
            Math.Max(1, ingredient?.amount ?? 1)) ?? 0;

    private static int DistinctIngredientCount(RecipeDefinition recipe) =>
        recipe.ingredients?
            .Where(ingredient =>
                ingredient != null &&
                !string.IsNullOrWhiteSpace(ingredient.item))
            .Select(ingredient => ingredient.item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() ?? 0;

    private void CreateRecipe()
    {
        if (document == null)
            return;
        document.Pack.recipes ??= new List<RecipeDefinition>();
        int suffix = 1;
        HashSet<string> ids = document.Pack.recipes
            .Select(recipe => recipe.id ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string id;
        do
        {
            id = "new-recipe-" + suffix++;
        }
        while (ids.Contains(id));
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
        selectedRecipe = index;
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

    private static int CountCustomItems() =>
        ItemLibrary.Items.Count(IsCustomItem);

    private void Close()
    {
        open = false;
        pickerTarget = PickerTarget.None;
        document = null;
        documents.Clear();
        gameObject.SetActive(false);
        ReleaseSession();
    }
}
