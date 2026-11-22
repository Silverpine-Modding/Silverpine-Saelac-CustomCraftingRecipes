#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SilverpineMods.CustomCraftingRecipes;

// ResultData is the native UI contract. Keep its first item for rendering and
// carry independently-created additional units only for configured batches.
internal sealed class BatchResult : ResultData
{
    private readonly Item[] units;
    internal CompiledRecipe Recipe { get; }
    internal int Revision { get; } = RecipeRegistry.Revision;
    internal string RecipeId => Recipe.Id;
    internal BatchResult(string text, Item[] items, int minutes, CompiledRecipe recipe) : base(text, items[0], minutes)
    { units = items; Recipe = recipe; }
    internal static IReadOnlyList<Item> Units(ResultData data) => data is BatchResult batch ? batch.units :
        data.resultItem == null ? Array.Empty<Item>() : new[] { data.resultItem };
}

internal static class RecipeResultFactory
{
    internal static bool CanDuplicate(Item item) => item.GetType() == typeof(Item) &&
        !item.GetItemComponents<ItemComponent>().Any(component =>
            component.GetType().GetMethod(nameof(ItemComponent.OnAddedToInventory))?.DeclaringType != typeof(ItemComponent) ||
            component.GetType().GetMethod(nameof(ItemComponent.OnRemovedFromInventory))?.DeclaringType != typeof(ItemComponent));

    internal static Item CreatePreview(string name, IReadOnlyList<Item> catalog, Func<string, Item> create, Inventory? destination)
    {
        string baseName = name;
        if (name.Contains('|'))
        {
            string[] parts = name.Split('|');
            if (parts.Length != 2 || !Enum.TryParse(parts[0], true, out Quality quality) || !Enum.IsDefined(typeof(Quality), quality))
                throw new InvalidDataException("Invalid result quality prefix.");
            baseName = parts[1];
            name = quality + "|" + baseName;
        }
        if (!catalog.Any(item => item != null && string.Equals(item.name, baseName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Result item '{baseName}' is not loaded.");
        Item item = create(name) ?? throw new InvalidDataException("The item factory returned no result.");
        return CraftingResultPreview.Prepare(item, destination);
    }

    internal static ResultData Build(CompiledRecipe recipe, IReadOnlyList<Item> catalog,
        Func<string, Item> create, Inventory? destination, Action<Exception> report)
    {
        try
        {
            Item result = CreatePreview(recipe.ResultName, catalog, create, destination);
            string text = string.IsNullOrWhiteSpace(recipe.Description)
                ? $"Creates <color=\"yellow\">{result.GetFinalName()}</color>." : recipe.Description;
            if (recipe.ResultAmount > 1)
            {
                // Pickup effects (e.g. gold converting itself to currency) cannot
                // be undone if a later unit fails. Keep such outputs single-only.
                if (!CanDuplicate(result))
                    throw new InvalidDataException("Items with special inventory callbacks must use a result quantity of one.");
                var units = new Item[recipe.ResultAmount];
                units[0] = result;
                for (int i = 1; i < units.Length; i++) units[i] = CreatePreview(recipe.ResultName, catalog, create, destination);
                return new BatchResult($"Yield: {units.Length} × {result.name}\n{text}", units, recipe.WorkMinutes, recipe);
            }
            return new BatchResult(text, new[] { result }, recipe.WorkMinutes, recipe);
        }
        catch (Exception exception)
        {
            report(exception);
            return new ResultData($"Could not create output '{recipe.ResultName}': {exception.Message}", null, 0);
        }
    }
}
