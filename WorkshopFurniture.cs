#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SilverpineMods.CustomCraftingRecipes;

// CIL owns placement, pickup and save restoration. Resolve its *own* stable
// prefab names, never a display name or the folder where the pack is installed.
internal static class WorkshopFurniture
{
    internal const string Pack = "silverpine.village-workshops";
    internal const string FoundryPack = "silverpine.village-foundry";
    internal static readonly (string Id, CraftingStation Station)[] Stations = {
        ("joiners-bench", CraftingStation.Joinery), ("tailors-frame", CraftingStation.Tailoring),
        ("bake-oven", CraftingStation.Baking)
    };
    private static readonly Dictionary<string, CraftingStation> prefabs = new(StringComparer.OrdinalIgnoreCase);

    internal static void Install(Harmony harmony)
    {
        const string ns = "SilverpineMods.CustomItemLoader.";
        var adapter = AccessTools.TypeByName(ns + "CustomItemWorkbenchAdapter");
        if (adapter == null) return; // No dependency for regular crafting packs.
        try
        {
            var asm = adapter.Assembly;
            var builder = asm.GetType(ns + "AssetFiles")?.GetMethod("FurnitureName", BindingFlags.NonPublic | BindingFlags.Static);
            var interact = adapter.GetMethod("OnInteract", new[] { typeof(GameObject) });
            var label = adapter.GetProperty("InteractionName")?.GetGetMethod();
            if (builder == null || interact == null || label == null)
                throw new MissingMethodException("Custom Item Loader furniture hooks do not match this version.");
            var name = (Func<string,string,string>)builder.CreateDelegate(typeof(Func<string,string,string>));
            foreach (var entry in Stations) prefabs.Add(name(Pack, entry.Id), entry.Station);
            prefabs.Add(name(FoundryPack, "smelting-furnace"), CraftingStation.Smelting);
            harmony.Patch(interact, prefix: new HarmonyMethod(typeof(WorkshopFurniture), nameof(Interact)));
            harmony.Patch(label, postfix: new HarmonyMethod(typeof(WorkshopFurniture), nameof(Label)));
            harmony.Patch(AccessTools.Method(typeof(LootTable), nameof(LootTable.GetPredefined)),
                postfix: new HarmonyMethod(typeof(WorkshopFurniture), nameof(OfferStarterBench)));
        }
        catch (Exception e)
        {
            prefabs.Clear();
            Plugin.Log.LogError("Village Workshops furniture integration unavailable: " + e);
        }
    }

    private static bool Interact(Component __instance)
    {
        if (!prefabs.TryGetValue(SerializationManager.GetPrefabName(__instance.gameObject), out var station)) return true;
        QuantityCraftingUI.Open(new CustomCraftingUIData(null!), station);
        return false;
    }

    private static void Label(Component __instance, ref string __result)
    {
        if (prefabs.TryGetValue(SerializationManager.GetPrefabName(__instance.gameObject), out var station))
            __result = "Use " + station.Label();
    }

    // CIL's initial registration can predate our Awake. Resolve at restock time;
    // never mutate its shared item templates or replace native furniture rolls.
    private static void OfferStarterBench(PredefinedLootTableType predefinedLootTableType, List<Item> __result)
    {
        if (prefabs.Count == 0 || predefinedLootTableType != PredefinedLootTableType.Aldric || __result.Exists(IsStarter)) return;
        var item = ItemLibrary.Items.Find(IsStarter);
        if (item != null && UnityEngine.Random.value < 0.25f)
            __result.Add(item.DeepClone());
    }

    internal static bool IsStarter(Item item) =>
        item != null && item.itemCategory == ItemCategory.Furniture &&
        string.Equals(item.spriteName, "custom:" + Pack + ":joiners-bench", StringComparison.OrdinalIgnoreCase);
}
