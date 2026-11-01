#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace SilverpineMods.CustomCraftingRecipes;

internal static class CraftTransaction
{
    internal static bool CanComplete(Inventory inventory, IReadOnlyList<Item> ingredients, Item result, out string error)
    {
        error = "";
        HashSet<Item> owned = new(inventory.items);
        if (ingredients.Count == 0 || ingredients.Any(item => item == null || !owned.Contains(item)) ||
            new HashSet<Item>(ingredients).Count != ingredients.Count)
            error = "You no longer have the required ingredients.";
        else if (owned.Contains(result))
            error = "This craft has already completed.";
        else
        {
            double bulk = inventory.GetBulk() - ingredients.Sum(item => (double)item.bulk) + result.bulk;
            long count = (long)inventory.GetItemCount() - ingredients.Count + 1;
            if ((!inventory.allowEncumbered && bulk > inventory.maxBulk + 0.0001) || count > inventory.maxItems)
                error = "Your inventory cannot hold the result after using the ingredients.";
        }
        return error.Length == 0;
    }

    internal static bool TryComplete(Inventory inventory, IReadOnlyList<Item> ingredients, Item result,
        out string error, Action<Item>? remove = null, Func<Item, bool>? add = null)
    {
        if (!CanComplete(inventory, ingredients, result, out error)) return false;
        remove ??= inventory.RemoveItem;
        add ??= inventory.TryAddItem;
        try
        {
            foreach (Item item in ingredients) remove(item);
            if (add(result)) return true;
            error = "Craft cancelled because the result could not be added. Ingredients restored.";
        }
        catch (Exception exception)
        {
            Plugin.Log?.LogError("An inventory callback failed during crafting: " + exception);
            // Native TryAddItem can throw from a listener after delivery succeeded.
            // Restoring ingredients in that case would duplicate the craft.
            if (inventory.ContainsItem(result)) return true;
            error = "Craft cancelled: " + exception.Message + ". Ingredients restored.";
        }
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
        Dictionary<string, int> remaining = counts.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        List<Item> selected = new();
        foreach (Item item in inventory.items)
        {
            if (item != null && remaining.TryGetValue(item.name, out int count) && count > 0)
            { selected.Add(item); remaining[item.name] = count - 1; }
        }
        return remaining.Values.Any(count => count != 0) ? new List<Item>() : selected;
    }
}
