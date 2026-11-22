#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace SilverpineMods.CustomCraftingRecipes;

internal static class CraftTransaction
{
    internal static bool CanComplete(Inventory inventory, IReadOnlyList<Item> ingredients, Item result, out string error)
        => CanComplete(inventory, ingredients, new[] { result }, out error);

    internal static bool CanComplete(Inventory inventory, IReadOnlyList<Item> ingredients, IReadOnlyList<Item> results, out string error)
    {
        error = "";
        HashSet<Item> owned = new(inventory.items);
        if (ingredients.Count == 0 || ingredients.Any(item => item == null || !owned.Contains(item)) ||
            new HashSet<Item>(ingredients).Count != ingredients.Count)
            error = "You no longer have the required ingredients.";
        else if (results.Count < 1 || results.Count > RecipeRules.MaxYield + CraftingApi.MaxExtraItems ||
            results.Any(item => item == null || owned.Contains(item)) || new HashSet<Item>(results).Count != results.Count)
            error = "The craft does not have a valid, undelivered result.";
        else
        {
            double bulk = inventory.GetBulk() - ingredients.Sum(item => (double)item.bulk) + results.Sum(item => (double)item.bulk);
            long count = (long)inventory.GetItemCount() - ingredients.Count + results.Count;
            if ((!inventory.allowEncumbered && bulk > inventory.maxBulk + 0.0001) || count > inventory.maxItems)
                error = "Your inventory cannot hold the result after using the ingredients.";
        }
        return error.Length == 0;
    }

    internal static bool TryComplete(Inventory inventory, IReadOnlyList<Item> ingredients, Item result,
        out string error, Action<Item>? remove = null, Func<Item, bool>? add = null)
        => TryComplete(inventory, ingredients, new[] { result }, out error, remove, add);

    internal static bool TryComplete(Inventory inventory, IReadOnlyList<Item> ingredients, IReadOnlyList<Item> results,
        out string error, Action<Item>? remove = null, Func<Item, bool>? add = null)
    {
        if (!CanComplete(inventory, ingredients, results, out error)) return false;
        remove ??= inventory.RemoveItem;
        add ??= inventory.TryAddItem;
        try
        {
            foreach (Item item in ingredients) remove(item);
            foreach (Item unit in results)
                if (!add(unit))
                    throw new InvalidOperationException("The result could not be added.");
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Log?.LogError("An inventory callback failed during crafting: " + exception);
            // Native TryAddItem can throw from a listener after delivery succeeded.
            // Restoring ingredients in that case would duplicate the craft.
            if (results.All(inventory.ContainsItem)) return true;
            error = "Craft cancelled: " + exception.Message + ". Ingredients restored.";
        }
        // Only passive outputs are allowed in batches. Undo exactly the units
        // inserted by this attempt, never other inventory items of the same name.
        foreach (Item unit in results)
            if (inventory.items.Remove(unit)) unit.inventory = null;
        // Rollback must not fail because a listener changed capacity during removal.
        foreach (Item item in ingredients)
        {
            if (inventory.ContainsItem(item)) continue;
            inventory.items.Add(item);
            item.inventory = inventory;
            try { item.OnAddedToInventory(); }
            catch (Exception exception) { Plugin.Log?.LogError("Ingredient restoration callback failed: " + exception); }
        }
        try { inventory.UpdateEncumbered(); }
        catch (Exception exception) { Plugin.Log?.LogError("Inventory refresh failed after restoring ingredients: " + exception); }
        return false;
    }

    internal static List<Item> Select(Inventory inventory, IReadOnlyDictionary<string, int> counts)
    {
        if (counts.Count == 0) return new List<Item>();
        var requested = counts.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var candidates = inventory.items.Where(i => i != null && requested.ContainsKey(i.name))
            .ToLookup(i => i.name, StringComparer.OrdinalIgnoreCase);
        List<Item> selected = new();
        foreach (var request in requested)
        {
            var matching = candidates[request.Key];
            if (request.Value < 1 || matching.Count() < request.Value) return new List<Item>();
            // Stable for ties; do not spend premium ore or herbs just because
            // they happened to enter the inventory before common ones.
            selected.AddRange(matching.OrderBy(i => i.GetItemComponent<IQualityItemComponent>()?.Quality ?? Quality.Normal)
                .Take(request.Value));
        }
        return selected;
    }
}
