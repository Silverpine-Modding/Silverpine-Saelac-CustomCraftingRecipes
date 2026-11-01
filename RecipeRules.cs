#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace SilverpineMods.CustomCraftingRecipes;

internal static class RecipeRules
{
    internal const int MaxQuantity = 999;

    internal static bool TryStation(string? text, out CraftingStation station) =>
        Enum.TryParse(text?.Trim(), true, out station) &&
        Enum.IsDefined(typeof(CraftingStation), station);

    internal static List<string> Validate(RecipeDefinition recipe)
    {
        List<string> errors = new();
        if (string.IsNullOrWhiteSpace(recipe.id)) errors.Add("Recipe ID is required.");
        if (string.IsNullOrWhiteSpace(recipe.result)) errors.Add("A result item is required.");
        if (recipe.workMinutes < 1 || recipe.workMinutes > 1440)
            errors.Add("Work time must be between 1 and 1440 minutes.");
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        if (recipe.ingredients == null || recipe.ingredients.Count == 0)
            errors.Add("At least one ingredient is required.");
        else
            foreach (IngredientDefinition? ingredient in recipe.ingredients)
            {
                if (ingredient == null || string.IsNullOrWhiteSpace(ingredient.item))
                { errors.Add("Every ingredient needs an item."); continue; }
                if (ingredient.item.Contains('|'))
                    errors.Add("Ingredients must use plain item names; quality prefixes are supported only for results.");
                if (ingredient.amount < 1 || ingredient.amount > MaxQuantity)
                { errors.Add($"Ingredient quantities must be between 1 and {MaxQuantity}."); continue; }
                string name = ingredient.item.Trim();
                counts.TryGetValue(name, out int previous);
                // Saturate invalid totals so malformed JSON cannot overflow arithmetic.
                counts[name] = Math.Min(MaxQuantity + 1, previous + ingredient.amount);
            }
        if (counts.Values.Any(n => n > MaxQuantity))
            errors.Add($"Combined quantity per ingredient cannot exceed {MaxQuantity}.");
        if (counts.Count > CraftingStation.CustomCrafting.Capacity())
            errors.Add($"Recipes may use at most {CraftingStation.CustomCrafting.Capacity()} ingredient types.");
        if (recipe.stations == null || recipe.stations.Count == 0)
            errors.Add("Select at least one crafting station.");
        else
        {
            int total = (int)Math.Min(int.MaxValue, counts.Values.Sum(value => (long)value));
            foreach (string? value in recipe.stations)
            {
                if (!TryStation(value, out CraftingStation station))
                    errors.Add($"Unknown station '{value}'.");
                else if (!station.Fits(counts.Count, total))
                    errors.Add($"{station} permits {station.Capacity()} " +
                        (station == CraftingStation.CustomCrafting ? "distinct ingredient types." : "total items."));
            }
        }
        return errors;
    }

    internal static Dictionary<string, int> Counts(RecipeDefinition recipe)
    {
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        foreach (IngredientDefinition ingredient in recipe.ingredients)
        {
            string name = ingredient.item.Trim();
            counts.TryGetValue(name, out int previous);
            counts[name] = checked(previous + ingredient.amount);
        }
        return counts;
    }

    internal static string Signature(IReadOnlyDictionary<string, int> counts) =>
        string.Concat(counts.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.Key.Length + ":" + p.Key.ToUpperInvariant() + ":" + p.Value + ";"));
}
