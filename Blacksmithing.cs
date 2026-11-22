#nullable enable
using System;
using HarmonyLib;
using UnityEngine;

namespace SilverpineMods.CustomCraftingRecipes;

// Player.InteractWith enumerates all active IInteractionHandlers and offers
// their actions in a radial menu. Attach lazily to the *placed* object, not
// the resource template, so existing saves and freshly placed benches agree.
[HarmonyPatch(typeof(Player), "InteractWith", typeof(GameObject))]
internal static class MetalworkingBenchPatch
{
    internal static bool IsMetalworkingBench(string prefab) =>
        string.Equals(prefab, "prefab_furniture_workbench", StringComparison.Ordinal);

    private static void Prefix(GameObject __0)
    {
        if (!RecipeRegistry.HasStation(CraftingStation.Blacksmithing) || !__0.TryGetComponent<Workbench>(out _) ||
            !IsMetalworkingBench(SerializationManager.GetPrefabName(__0)) || __0.TryGetComponent<SmithingInteraction>(out _)) return;
        __0.AddComponent<SmithingInteraction>();
    }
}

internal sealed class SmithingInteraction : MonoBehaviour, IInteractionHandler
{
    public string InteractionName => "Blacksmithing";
    public bool Interactable => RecipeRegistry.HasStation(CraftingStation.Blacksmithing);
    public bool ProcessTurnOnInteract => false;
    public void OnInteract(GameObject sender)
    {
        if (Interactable) QuantityCraftingUI.Open(new CustomCraftingUIData(GetComponent<Workbench>()), CraftingStation.Blacksmithing);
    }
}
