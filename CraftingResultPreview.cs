#nullable enable

namespace SilverpineMods.CustomCraftingRecipes;

internal static class CraftingResultPreview
{
    internal static Item Prepare(Item item, Inventory? destination)
    {
        if (item.inventory != null)
            return item;

        // Native name/description formatting requires an owning inventory even
        // before the craft completes. Keep the preview separate from live items
        // and do not invoke pickup callbacks; TryAddItem assigns real ownership
        // when crafting actually delivers the result.
        Inventory preview = new() { owner = destination?.owner };
        preview.items.Add(item);
        item.inventory = preview;
        return item;
    }
}
