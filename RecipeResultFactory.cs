#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SilverpineMods.CustomCraftingRecipes;

internal static class RecipeResultFactory
{
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
            return new ResultData(text, result, recipe.WorkMinutes);
        }
        catch (Exception exception)
        {
            report(exception);
            return new ResultData($"Could not create output '{recipe.ResultName}': {exception.Message}", null, 0);
        }
    }
}
