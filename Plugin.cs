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
    BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency(
    "renegadex.silverpine.customitemloader",
    BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid =
        "renegadex.silverpine.customcraftingrecipes";
    public const string PluginName = "Custom Crafting Recipes";
    public const string PluginVersion = "1.2.1";

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

        RecipeRegistry.Initialize(Path.Combine(
            Paths.ConfigPath,
            "CustomCraftingRecipes"));

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

        RecipeRegistry.Reload();
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
        station == CraftingStation.CustomCrafting
            ? distinctIngredients <= station.Capacity()
            : totalItems <= station.Capacity();
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
    private static string definitionDirectory = "";

    internal static int Count => recipes.Count;
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
            for (int index = 0; index < pack.recipes.Count; index++)
            {
                RecipeDefinition? definition = pack.recipes[index];
                if (definition == null || !definition.enabled)
                    continue;
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
        string temporaryPath = document.Path + ".tmp";
        File.WriteAllText(temporaryPath, json + Environment.NewLine);
        File.Copy(temporaryPath, document.Path, overwrite: true);
        File.Delete(temporaryPath);
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
        List<ItemSelectionFieldInfo> fields = new(5);
        for (int index = 0; index < 5; index++)
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
                original(item) || IsIngredient(station, item);
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
        int selectedCount = selectedCounts.Values.Sum();
        if (selectedCount == 0)
        {
            result = ResultData.None;
            return false;
        }
        CompiledRecipe? recipe = recipes.FirstOrDefault(candidate =>
            candidate.Stations.Contains(station) &&
            candidate.Matches(selectedCounts, selectedCount));
        if (recipe == null)
        {
            result = ResultData.None;
            return false;
        }

        result = BuildResult(recipe);
        return true;
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
                Item cooked = ItemLibrary.GetItemByName(
                    cookable.resultItem,
                    logInvalid: false);
                return new ResultData(
                    cooked.GetFinalDescription(),
                    cooked,
                    10);
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
        if (items == null || items.Any(item => item == null))
            return true;
        if (items.Count < 4)
            return true;

        ItemComponent_AlchemyIngredient? component;
        bool validBase = items[0].TryGetItemComponent(out component) &&
            component != null &&
            component.alchemyEffectTypes.Contains(AlchemyEffectType.Base);
        bool validFirst = items[1].TryGetItemComponent(out component) &&
            component != null &&
            component.alchemyEffectTypes.Any(type => type != AlchemyEffectType.Base);
        bool validSecond = items[2].TryGetItemComponent(out component) &&
            component != null &&
            component.alchemyEffectTypes.Any(type => type != AlchemyEffectType.Base);
        return validBase && validFirst && validSecond &&
               items[3].name == "Glass Vial";
    }

    private static bool IsIngredient(CraftingStation station, Item? item) =>
        item != null && IngredientNames[station].Contains(item.name);

    private static ResultData BuildResult(CompiledRecipe recipe)
    {
        string lookupName = recipe.ResultName;
        int separatorIndex = lookupName.LastIndexOf('|');
        string baseName = separatorIndex >= 0
            ? lookupName.Substring(separatorIndex + 1)
            : lookupName;
        Item? template = ItemLibrary.Items.FirstOrDefault(item =>
            string.Equals(
                item.name,
                baseName,
                StringComparison.OrdinalIgnoreCase));
        if (template == null)
        {
            if (LoggedMissingResults.Add(recipe.ResultName))
            {
                Plugin.Log.LogError(
                    $"Recipe '{recipe.Id}' refers to unknown result item " +
                    $"'{recipe.ResultName}'.");
            }
            return new ResultData(
                $"Recipe '{recipe.Id}' has an unknown result item: " +
                $"<color=\"yellow\">{recipe.ResultName}</color>.",
                null,
                0);
        }

        Item result = ItemLibrary.GetItemByName(
            recipe.ResultName,
            logInvalid: false);
        string text = string.IsNullOrWhiteSpace(recipe.Description)
            ? $"Creates <color=\"yellow\">{result.GetFinalName()}</color>."
            : recipe.Description;
        return new ResultData(text, result, recipe.WorkMinutes);
    }

    private static bool TryCompile(
        string packId,
        RecipeDefinition definition,
        int index,
        HashSet<string> stationSignatures,
        out CompiledRecipe? compiled)
    {
        compiled = null;
        string localId = string.IsNullOrWhiteSpace(definition.id)
            ? $"recipe-{index + 1}"
            : definition.id.Trim();
        string id = packId + ":" + localId;
        if (definition.ingredients == null ||
            definition.ingredients.Count == 0)
        {
            Warn(id, "has no ingredients and was skipped.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(definition.result))
        {
            Warn(id, "has no result item and was skipped.");
            return false;
        }

        Dictionary<string, int> required =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (IngredientDefinition? ingredient in definition.ingredients)
        {
            if (ingredient == null || string.IsNullOrWhiteSpace(ingredient.item))
            {
                Warn(id, "contains an ingredient with no item name.");
                return false;
            }
            string itemName = ingredient.item.Trim();
            int amount = ingredient.amount <= 0 ? 1 : ingredient.amount;
            required.TryGetValue(itemName, out int existing);
            required[itemName] = existing + amount;
        }

        int ingredientCount = required.Values.Sum();
        if (required.Count > CraftingStation.CustomCrafting.Capacity())
        {
            Warn(id, "requires more than ten distinct ingredients and was skipped.");
            return false;
        }

        List<string> configuredStations = definition.stations == null ||
                                          definition.stations.Count == 0
            ? new List<string> { nameof(CraftingStation.CustomCrafting) }
            : definition.stations;
        HashSet<CraftingStation> validStations = new();
        string signature = string.Join(
            "|",
            required.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair =>
                    pair.Key.ToLowerInvariant() + "=" + pair.Value));

        foreach (string? stationName in configuredStations)
        {
            if (!Enum.TryParse(
                    stationName?.Trim(),
                    ignoreCase: true,
                    out CraftingStation station))
            {
                Warn(id, $"uses unknown station '{stationName}'.");
                continue;
            }
            if (!station.Fits(required.Count, ingredientCount))
            {
                Warn(
                    id,
                    station == CraftingStation.CustomCrafting
                        ? $"cannot use {station}: it needs {required.Count} " +
                          $"distinct ingredients but that UI has " +
                          $"{station.Capacity()} quantity slots."
                        : $"cannot use {station}: it needs {ingredientCount} " +
                          $"items but that UI has {station.Capacity()} slots.");
                continue;
            }
            string stationSignature = station + ":" + signature;
            if (!stationSignatures.Add(stationSignature))
            {
                Warn(
                    id,
                    $"duplicates another {station} ingredient combination; " +
                    "that station was skipped.");
                continue;
            }
            validStations.Add(station);
        }

        if (validStations.Count == 0)
        {
            Warn(id, "has no valid crafting stations and was skipped.");
            return false;
        }

        int workMinutes = definition.workMinutes <= 0
            ? 10
            : Math.Min(definition.workMinutes, 1440);
        compiled = new CompiledRecipe(
            id,
            required,
            validStations,
            definition.result.Trim(),
            workMinutes,
            definition.description?.Trim() ?? "");
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
