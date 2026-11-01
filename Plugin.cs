#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
using Silverpine.ModdingTools;
using UnityEngine;

namespace SilverpineMods.CustomCraftingRecipes;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(
    Silverpine.ModdingTools.Plugin.PluginGuid,
    "1.3.0")]
[BepInDependency(
    "renegadex.silverpine.customitemloader",
    BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid =
        "renegadex.silverpine.customcraftingrecipes";
    public const string PluginName = "Custom Crafting Recipes";
    public const string PluginVersion = "1.3.0";

    internal static ManualLogSource Log { get; private set; } = null!;

    private ConfigEntry<KeyCode> reloadKey = null!;

    private void Awake()
    {
        Log = Logger;
        reloadKey = Config.Bind(
            "General",
            "ReloadKey",
            KeyCode.F6,
            "Reload recipes.json. Close and reopen an active crafting menu " +
            "after reloading so its ingredient slots are rebuilt.");

        try
        {
            RecipeRegistry.Initialize(Path.Combine(Paths.ConfigPath, "CustomCraftingRecipes"));
        }
        catch (Exception exception)
        {
            Log.LogError("Could not load recipes; fix the pack files and reload: " + exception);
        }

        ModdingToolsMenu.RegisterSession(
            PluginGuid + ".recipe-editor",
            "Custom Recipe Editor",
            (_, session) => RecipeEditorUI.Open(session),
            order: 320);
        Log.LogInfo(
            "Registered Custom Recipe Editor with the Modding Tools main menu.");

        new Harmony(PluginGuid).PatchAll();
        Log.LogInfo($"{PluginName} {PluginVersion} loaded with " +
                    $"{RecipeRegistry.Count} recipe(s).");
    }

    private void Update()
    {
        if (!Input.GetKeyDown(reloadKey.Value))
            return;

        try { RecipeRegistry.Reload(); }
        catch (Exception exception)
        {
            Log.LogError("Recipe reload failed; retaining the previous registry: " + exception);
            return;
        }
        Log.LogInfo("Custom-crafting recipes reloaded. Close and reopen " +
                    "an active crafting menu to refresh its slots.");
    }

}

internal enum CraftingStation
{
    CustomCrafting,
    Alchemy,
    Cooking
}

internal static class CraftingStationInfo
{
    internal static int Capacity(this CraftingStation station) =>
        station switch
        {
            CraftingStation.CustomCrafting => 10,
            CraftingStation.Alchemy => 4,
            CraftingStation.Cooking => 3,
            _ => 0
        };

    internal static bool Fits(
        this CraftingStation station,
        int distinctIngredients,
        int totalItems) =>
        distinctIngredients > 0 && totalItems > 0 && (station == CraftingStation.CustomCrafting
            ? distinctIngredients <= station.Capacity()
            : totalItems <= station.Capacity());
}

[HarmonyPatch(typeof(CraftingUI), nameof(CraftingUI.Open))]
internal static class QuantityCustomCraftingOpenPatch
{
    private static bool Prefix(CraftingUIData craftingUIData)
    {
        if (craftingUIData is not CustomCraftingUIData)
            return true;

        QuantityCraftingUI.Open(craftingUIData);
        return false;
    }
}

[HarmonyPatch(
    typeof(CustomCraftingUIData),
    nameof(CustomCraftingUIData.GetItemSelectionFieldInfos))]
internal static class CustomCraftingFieldsPatch
{
    private static bool Prefix(ref List<ItemSelectionFieldInfo> __result)
    {
        __result = RecipeRegistry.CreateCustomCraftingFields();
        return false;
    }
}

[HarmonyPatch(
    typeof(CustomCraftingUIData),
    nameof(CustomCraftingUIData.GetResultData))]
internal static class CustomCraftingResultPatch
{
    private static bool Prefix(
        List<Item> items,
        ref ResultData __result)
    {
        __result = RecipeRegistry.GetCustomCraftingResult(items);
        return false;
    }
}

[HarmonyPatch(
    typeof(AlchemyCraftingUIData),
    nameof(AlchemyCraftingUIData.GetItemSelectionFieldInfos))]
internal static class AlchemyFieldsPatch
{
    private static void Postfix(ref List<ItemSelectionFieldInfo> __result)
    {
        RecipeRegistry.ExtendPredicates(CraftingStation.Alchemy, __result);
    }
}

[HarmonyPatch(
    typeof(AlchemyCraftingUIData),
    nameof(AlchemyCraftingUIData.GetResultData))]
internal static class AlchemyResultPatch
{
    private static bool Prefix(
        List<Item> items,
        ref ResultData __result)
    {
        if (RecipeRegistry.TryGetConfiguredResult(
                CraftingStation.Alchemy,
                items,
                out __result))
            return false;

        if (!RecipeRegistry.IsSafeForNativeAlchemy(items))
        {
            __result = RecipeRegistry.NoRecipeResult;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(
    typeof(CookingCraftingUIData),
    nameof(CookingCraftingUIData.GetItemSelectionFieldInfos))]
internal static class CookingFieldsPatch
{
    private static void Postfix(ref List<ItemSelectionFieldInfo> __result)
    {
        ExpandCookingFields(__result);
        RecipeRegistry.ExtendPredicates(CraftingStation.Cooking, __result);
    }

    internal static void ExpandCookingFields(
        List<ItemSelectionFieldInfo> fields)
    {
        if (fields == null || fields.Count == 0)
            return;
        Predicate<Item> nativePredicate = fields[0].predicate;
        fields[0].label = "Ingredient 1";
        while (fields.Count < CraftingStation.Cooking.Capacity())
        {
            fields.Add(new ItemSelectionFieldInfo(
                $"Ingredient {fields.Count + 1}",
                nativePredicate));
        }
    }
}

[HarmonyPatch(
    typeof(CookingCraftingUIData),
    nameof(CookingCraftingUIData.GetResultData))]
internal static class CookingResultPatch
{
    private static bool Prefix(
        CookingCraftingUIData __instance,
        List<Item> items,
        ref ResultData __result)
    {
        Workbench? workbench = Traverse.Create(__instance)
            .Field("workbench")
            .GetValue<Workbench>();
        TileLight? fire = workbench != null
            ? workbench.GetComponent<TileLight>()
            : null;
        __result = RecipeRegistry.GetCookingResult(
            items,
            requiresFire: fire != null,
            fireLit: fire == null || fire.Lit);
        return false;
    }
}

// Custom Item Loader uses its own no-fire cooking UI for placed custom
// furniture. Patch it when that optional plugin is present.
[HarmonyPatch]
internal static class CustomItemLoaderCookingFieldsPatch
{
    private static bool Prepare() => TargetMethod() != null;

    private static MethodBase? TargetMethod()
    {
        Type? type = AccessTools.TypeByName(
            "SilverpineMods.CustomItemLoader.CustomItemCookingCraftingUIData");
        return type == null
            ? null
            : AccessTools.Method(type, "GetItemSelectionFieldInfos");
    }

    private static void Postfix(ref List<ItemSelectionFieldInfo> __result)
    {
        CookingFieldsPatch.ExpandCookingFields(__result);
        RecipeRegistry.ExtendPredicates(CraftingStation.Cooking, __result);
    }
}

[HarmonyPatch]
internal static class CustomItemLoaderCookingResultPatch
{
    private static bool Prepare() => TargetMethod() != null;

    private static MethodBase? TargetMethod()
    {
        Type? type = AccessTools.TypeByName(
            "SilverpineMods.CustomItemLoader.CustomItemCookingCraftingUIData");
        return type == null
            ? null
            : AccessTools.Method(type, "GetResultData");
    }

    private static bool Prefix(
        List<Item> items,
        ref ResultData __result)
    {
        __result = RecipeRegistry.GetCookingResult(
            items,
            requiresFire: false,
            fireLit: true);
        return false;
    }
}

internal static class RecipeRegistry
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace
    };

    private const string DefaultPackJson = @"{
  ""schemaVersion"": 1,
  ""packId"": ""silverpine.starter-recipes"",
  ""displayName"": ""Starter Recipes"",
  ""enabled"": true,
  ""recipes"": [
    {
      ""id"": ""wooden-door"",
      ""enabled"": true,
      ""stations"": [ ""CustomCrafting"" ],
      ""ingredients"": [
        { ""item"": ""Plank"", ""amount"": 4 },
        { ""item"": ""Nails"", ""amount"": 1 }
      ],
      ""result"": ""Wooden Door"",
      ""workMinutes"": 20,
      ""description"": ""Fit the planks together and fasten them with nails.""
    }
  ]
}";

    private static readonly Dictionary<CraftingStation, HashSet<string>>
        IngredientNames = Enum.GetValues(typeof(CraftingStation))
            .Cast<CraftingStation>()
            .ToDictionary(
                station => station,
                _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    private static readonly HashSet<string> LoggedMissingResults =
        new(StringComparer.OrdinalIgnoreCase);
    private static List<CompiledRecipe> recipes = new();
    private static readonly Dictionary<string, CompiledRecipe> recipeIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private static string definitionDirectory = "";

    internal static int Count => recipes.Count;
    internal static int Revision { get; private set; }
    internal static int PackCount { get; private set; }
    internal static int EnabledPackCount { get; private set; }
    internal static string DefinitionDirectory => definitionDirectory;
    internal static ResultData NoRecipeResult => new(
        "No configured recipe matches these ingredients.",
        null,
        0);

    internal static void Initialize(string directory)
    {
        definitionDirectory = directory;
        Directory.CreateDirectory(definitionDirectory);
        EnsureInitialPack();
        Reload();
    }

    internal static void Reload()
    {
        List<CompiledRecipe> loaded = new();
        HashSet<string> stationSignatures =
            new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> loadedPackIds =
            new(StringComparer.OrdinalIgnoreCase);
        string[] paths = FindPackFiles();
        int enabledPacks = 0;
        foreach (string path in paths)
        {
            RecipePackDefinition pack;
            try
            {
                pack = ReadPack(path);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError(
                    $"Could not load recipe pack '{path}': {exception}");
                continue;
            }

            if (!pack.enabled)
            {
                Plugin.Log.LogInfo(
                    $"Skipped disabled recipe pack '{pack.packId}'.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(pack.packId))
            {
                Plugin.Log.LogWarning(
                    $"Recipe pack '{path}' has no packId and was skipped.");
                continue;
            }
            string packId = pack.packId.Trim();
            if (!loadedPackIds.Add(packId))
            {
                Plugin.Log.LogWarning(
                    $"Duplicate recipe pack ID '{packId}' in '{path}'; " +
                    "the later pack was skipped.");
                continue;
            }

            enabledPacks++;
            HashSet<string> recipeIds = new(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < pack.recipes.Count; index++)
            {
                RecipeDefinition? definition = pack.recipes[index];
                if (definition == null || !definition.enabled)
                    continue;
                if (!recipeIds.Add(definition.id?.Trim() ?? ""))
                {
                    Warn(packId, $"contains duplicate recipe ID '{definition.id}'; the later recipe was skipped.");
                    continue;
                }
                if (TryCompile(
                        packId,
                        definition,
                        index,
                        stationSignatures,
                        out CompiledRecipe? recipe))
                    loaded.Add(recipe!);
            }
        }

        recipes = loaded;
        recipeIndex.Clear();
        foreach (CompiledRecipe recipe in recipes)
            foreach (CraftingStation station in recipe.Stations)
                recipeIndex[station + ":" + RecipeRules.Signature(recipe.RequiredItems)] = recipe;
        Revision++;
        PackCount = paths.Length;
        EnabledPackCount = enabledPacks;
        LoggedMissingResults.Clear();
        foreach (HashSet<string> names in IngredientNames.Values)
            names.Clear();
        foreach (CompiledRecipe recipe in recipes)
        {
            foreach (CraftingStation station in recipe.Stations)
            {
                foreach (string name in recipe.RequiredItems.Keys)
                    IngredientNames[station].Add(name);
            }
        }

        Plugin.Log.LogInfo(
            $"Loaded {recipes.Count} custom recipe(s) from {enabledPacks} " +
            $"enabled recipe pack(s); discovered {paths.Length} pack(s).");
    }

    internal static List<RecipePackDocument> LoadEditablePacks()
    {
        List<RecipePackDocument> documents = new();
        foreach (string path in FindPackFiles())
        {
            try
            {
                documents.Add(new RecipePackDocument(path, ReadPack(path)));
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError(
                    $"Recipe editor could not read '{path}': {exception}");
            }
        }
        return documents;
    }

    internal static void SavePack(RecipePackDocument document)
    {
        RecipePackDefinition pack = document.Pack;
        pack.schemaVersion = 1;
        pack.recipes ??= new List<RecipeDefinition>();
        if (string.IsNullOrWhiteSpace(document.Path))
        {
            string folderName = SafeFileName(pack.packId) + ".recipe.pack";
            string folder = Path.Combine(definitionDirectory, folderName);
            if (Directory.Exists(folder))
            {
                throw new IOException(
                    $"A recipe-pack folder named '{folderName}' already exists.");
            }
            Directory.CreateDirectory(folder);
            document.Path = Path.Combine(folder, "recipes.json");
        }

        string json = JsonConvert.SerializeObject(pack, Formatting.Indented);
        string temporaryPath = document.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json + Environment.NewLine);
            if (File.Exists(document.Path))
                File.Replace(temporaryPath, document.Path, document.Path + ".bak");
            else
                File.Move(temporaryPath, document.Path);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        Reload();
    }

    internal static RecipePackDefinition ReadPack(string path)
    {
        string json = File.ReadAllText(path);
        RecipePackDefinition? pack =
            JsonConvert.DeserializeObject<RecipePackDefinition>(
                json,
                JsonSettings);
        if (pack == null)
            throw new InvalidDataException("The recipe pack is empty.");
        if (pack.schemaVersion != 1)
            throw new InvalidDataException($"Unsupported recipe schema version {pack.schemaVersion}.");
        pack.packId = pack.packId?.Trim() ?? "";
        pack.displayName ??= "";
        pack.recipes ??= new List<RecipeDefinition>();
        return pack;
    }

    private static string[] FindPackFiles() =>
        Directory.GetDirectories(
                definitionDirectory,
                "*.recipe.pack",
                SearchOption.TopDirectoryOnly)
            .Select(folder => Path.Combine(folder, "recipes.json"))
            .Where(File.Exists)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static void EnsureInitialPack()
    {
        if (FindPackFiles().Length > 0)
            return;

        string legacyPath = Path.Combine(definitionDirectory, "recipes.json");
        string folder = Path.Combine(
            definitionDirectory,
            File.Exists(legacyPath)
                ? "Migrated.recipe.pack"
                : "Starter.recipe.pack");
        Directory.CreateDirectory(folder);
        string destination = Path.Combine(folder, "recipes.json");
        if (File.Exists(legacyPath))
        {
            LegacyRecipeFile? legacy =
                JsonConvert.DeserializeObject<LegacyRecipeFile>(
                    File.ReadAllText(legacyPath),
                    JsonSettings);
            RecipePackDefinition migrated = new()
            {
                packId = "migrated.recipes",
                displayName = "Migrated Recipes",
                enabled = true,
                recipes = legacy?.recipes ?? new List<RecipeDefinition>()
            };
            File.WriteAllText(
                destination,
                JsonConvert.SerializeObject(migrated, Formatting.Indented) +
                Environment.NewLine);
            Plugin.Log.LogInfo(
                $"Migrated legacy recipes.json into '{destination}'. The " +
                "legacy file is retained but no longer loaded.");
        }
        else
        {
            File.WriteAllText(destination, DefaultPackJson);
        }
    }

    private static string SafeFileName(string value)
    {
        string safe = string.IsNullOrWhiteSpace(value)
            ? "new-recipes"
            : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalid, '_');
        return safe.Replace(':', '_');
    }

    internal static List<ItemSelectionFieldInfo> CreateCustomCraftingFields()
    {
        int capacity = CraftingStation.CustomCrafting.Capacity();
        List<ItemSelectionFieldInfo> fields = new(capacity);
        for (int index = 0; index < capacity; index++)
        {
            fields.Add(new ItemSelectionFieldInfo(
                $"Ingredient {index + 1}",
                item => IsIngredient(CraftingStation.CustomCrafting, item)));
        }
        return fields;
    }

    internal static void ExtendPredicates(
        CraftingStation station,
        List<ItemSelectionFieldInfo>? fields)
    {
        if (fields == null)
            return;
        foreach (ItemSelectionFieldInfo field in fields)
        {
            Predicate<Item> original = field.predicate;
            field.predicate = item =>
                item != null && (original(item) || IsIngredient(station, item));
        }
    }

    internal static ResultData GetCustomCraftingResult(List<Item>? items)
    {
        List<Item> selected = NonNullItems(items);
        if (selected.Count == 0)
            return ResultData.None;
        if (TryGetConfiguredResult(
                CraftingStation.CustomCrafting,
                selected,
                out ResultData result))
            return result;

        Dictionary<string, int> counts = CountItems(selected);
        bool couldMatch = recipes.Any(recipe =>
            recipe.Stations.Contains(CraftingStation.CustomCrafting) &&
            recipe.CouldMatch(counts, selected.Count));
        return new ResultData(
            couldMatch
                ? "Add the remaining ingredients."
                : "No custom-crafting recipe matches these ingredients.",
            null,
            0);
    }

    internal static bool TryGetConfiguredResult(
        CraftingStation station,
        List<Item>? items,
        out ResultData result)
    {
        List<Item> selected = NonNullItems(items);
        if (selected.Count == 0)
        {
            result = ResultData.None;
            return false;
        }

        Dictionary<string, int> selectedCounts = CountItems(selected);
        return TryGetConfiguredResult(station, selectedCounts, out result);
    }

    internal static bool TryGetConfiguredResult(
        CraftingStation station,
        IReadOnlyDictionary<string, int> selectedCounts,
        out ResultData result)
    {
        if (!TryFindRecipe(station, selectedCounts, out CompiledRecipe? recipe))
        {
            result = ResultData.None;
            return false;
        }
        result = BuildResult(recipe!);
        return true;
    }

    internal static bool TryFindRecipe(CraftingStation station,
        IReadOnlyDictionary<string, int> selectedCounts, out CompiledRecipe? recipe)
    {
        recipe = null;
        if (selectedCounts.Count == 0 || selectedCounts.Count > CraftingStation.CustomCrafting.Capacity() ||
            selectedCounts.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 1 || p.Value > RecipeRules.MaxQuantity))
            return false;
        return recipeIndex.TryGetValue(station + ":" + RecipeRules.Signature(selectedCounts), out recipe);
    }

    internal static ResultData GetCookingResult(
        List<Item>? items,
        bool requiresFire,
        bool fireLit)
    {
        List<Item> selected = NonNullItems(items);
        if (selected.Count == 0)
            return ResultData.None;
        if (requiresFire && !fireLit)
            return new ResultData("No fire.", null, 0);
        if (TryGetConfiguredResult(
                CraftingStation.Cooking,
                selected,
                out ResultData configured))
            return configured;
        if (selected.Count == 1)
        {
            ItemComponent_Cookable? cookable =
                selected[0].GetItemComponent<ItemComponent_Cookable>();
            if (cookable != null)
            {
                try
                {
                    Item cooked = CreatePreviewItem(cookable.resultItem);
                    return new ResultData(cooked.GetFinalDescription(), cooked, 10);
                }
                catch (Exception exception)
                {
                    if (LoggedMissingResults.Add(cookable.resultItem ?? ""))
                        Plugin.Log.LogError("Could not create cooked result: " + exception);
                    return new ResultData("The cooked result item could not be created. Check its item definition.", null, 0);
                }
            }
        }
        return NoRecipeResult;
    }

    internal static bool IsIngredientName(
        CraftingStation station,
        string? itemName) =>
        !string.IsNullOrWhiteSpace(itemName) &&
        IngredientNames[station].Contains(itemName);

    internal static bool IsSafeForNativeAlchemy(List<Item>? items)
    {
        if (items == null || items.Count != 4)
            return false;
        if (items.Any(item => item == null))
            return true;

        ItemComponent_AlchemyIngredient? component;
        bool validBase = items[0].TryGetItemComponent(out component) &&
            component?.alchemyEffectTypes != null &&
            component.alchemyEffectTypes.Contains(AlchemyEffectType.Base);
        bool validFirst = items[1].TryGetItemComponent(out component) &&
            component?.alchemyEffectTypes != null &&
            component.alchemyEffectTypes.Any(type => type != AlchemyEffectType.Base);
        bool validSecond = items[2].TryGetItemComponent(out component) &&
            component?.alchemyEffectTypes != null &&
            component.alchemyEffectTypes.Any(type => type != AlchemyEffectType.Base);
        return validBase && validFirst && validSecond &&
               items[3].name == "Glass Vial";
    }

    private static bool IsIngredient(CraftingStation station, Item? item) =>
        item != null && IngredientNames[station].Contains(item.name);

    private static ResultData BuildResult(CompiledRecipe recipe) =>
        RecipeResultFactory.Build(recipe, ItemLibrary.Items,
            name => ItemLibrary.GetItemByName(name, logInvalid: false),
            Player.Instance != null ? Player.Instance.worldInventory.inventory : null,
            exception =>
            {
                if (LoggedMissingResults.Add(recipe.ResultName))
                    Plugin.Log.LogError($"Could not create result for '{recipe.Id}': {exception}");
            });

    private static Item CreatePreviewItem(string name) =>
        RecipeResultFactory.CreatePreview(name, ItemLibrary.Items,
            value => ItemLibrary.GetItemByName(value, logInvalid: false),
            Player.Instance != null ? Player.Instance.worldInventory.inventory : null);

    internal static bool TryCompile(string packId, RecipeDefinition definition, int index,
        HashSet<string> stationSignatures, out CompiledRecipe? compiled)
    {
        compiled = null;
        string id = packId + ":" + (definition.id?.Trim() ?? $"recipe-{index + 1}");
        List<string> errors = RecipeRules.Validate(definition);
        if (errors.Count > 0)
        {
            Warn(id, string.Join(" ", errors) + " Recipe skipped.");
            return false;
        }
        Dictionary<string, int> required = RecipeRules.Counts(definition);
        string signature = RecipeRules.Signature(required);
        HashSet<CraftingStation> stations = new();
        foreach (string value in definition.stations)
        {
            RecipeRules.TryStation(value, out CraftingStation station);
            if (stations.Contains(station)) continue;
            if (!stationSignatures.Add(station + ":" + signature))
            {
                Warn(id, $"duplicates another {station} ingredient combination; that station was skipped.");
                continue;
            }
            stations.Add(station);
        }
        if (stations.Count == 0) return false;
        compiled = new CompiledRecipe(id, required, stations, definition.result.Trim(),
            definition.workMinutes, definition.description?.Trim() ?? "");
        return true;
    }

    private static List<Item> NonNullItems(List<Item>? items) =>
        items?.Where(item => item != null).ToList() ?? new List<Item>();

    private static Dictionary<string, int> CountItems(IEnumerable<Item> items)
    {
        Dictionary<string, int> counts =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (Item item in items)
        {
            counts.TryGetValue(item.name, out int existing);
            counts[item.name] = existing + 1;
        }
        return counts;
    }

    private static void Warn(string id, string message)
    {
        Plugin.Log.LogWarning($"Recipe '{id}' {message}");
    }
}

internal sealed class CompiledRecipe
{
    internal CompiledRecipe(
        string id,
        Dictionary<string, int> requiredItems,
        HashSet<CraftingStation> stations,
        string resultName,
        int workMinutes,
        string description)
    {
        Id = id;
        RequiredItems = requiredItems;
        Stations = stations;
        ResultName = resultName;
        WorkMinutes = workMinutes;
        Description = description;
        IngredientCount = requiredItems.Values.Sum();
    }

    internal string Id { get; }
    internal Dictionary<string, int> RequiredItems { get; }
    internal HashSet<CraftingStation> Stations { get; }
    internal string ResultName { get; }
    internal int WorkMinutes { get; }
    internal string Description { get; }
    internal int IngredientCount { get; }

    internal bool Matches(
        IReadOnlyDictionary<string, int> selected,
        int selectedCount)
    {
        if (selectedCount != IngredientCount ||
            selected.Count != RequiredItems.Count)
            return false;
        return RequiredItems.All(pair =>
            selected.TryGetValue(pair.Key, out int count) &&
            count == pair.Value);
    }

    internal bool CouldMatch(
        IReadOnlyDictionary<string, int> selected,
        int selectedCount)
    {
        if (selectedCount > IngredientCount)
            return false;
        return selected.All(pair =>
            RequiredItems.TryGetValue(pair.Key, out int count) &&
            pair.Value <= count);
    }
}

[Serializable]
internal sealed class RecipePackDefinition
{
    public int schemaVersion = 1;
    public string packId = "my.recipe.pack";
    public string displayName = "My Recipe Pack";
    public bool enabled = true;
    public List<RecipeDefinition> recipes = new();
}

[Serializable]
internal sealed class LegacyRecipeFile
{
    public int schemaVersion = 1;
    public List<RecipeDefinition> recipes = new();
}

internal sealed class RecipePackDocument
{
    internal RecipePackDocument(
        string path,
        RecipePackDefinition pack)
    {
        Path = path;
        Pack = pack;
    }

    internal string Path { get; set; }
    internal RecipePackDefinition Pack { get; }
}

[Serializable]
internal sealed class RecipeDefinition
{
    public string id = "";
    public bool enabled = true;
    public List<string> stations = new()
    {
        nameof(CraftingStation.CustomCrafting)
    };
    public List<IngredientDefinition> ingredients = new();
    public string result = "";
    public int workMinutes = 10;
    public string description = "";
}

[Serializable]
internal sealed class IngredientDefinition
{
    public string item = "";
    public int amount = 1;
}
