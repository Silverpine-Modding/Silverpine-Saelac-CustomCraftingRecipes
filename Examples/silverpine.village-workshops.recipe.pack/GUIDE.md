# Village Workshops

At an ordinary CustomCrafting station (including Mr Bones), select **Browse recipes → Joiner's Bench**. Bring 6 Planks, 2 Nails and 3 Branches; the work takes 180 in-game minutes. Alternatively, Aldric has a 25% chance to stock one completed bench when his inventory is generated. It does not replace any of his normal stock. The native Metalworking Bench remains a repair station.

Place the bench using the furniture item's normal placement action. **Use Joiner's Bench** opens its own recipe book. Build the other two stations there, or at an ordinary CustomCrafting station.

| Station | Examples | Construction |
| --- | --- | --- |
| Joiner's Bench | Chairs, tables, a room divider, a bookcase, beds and the other two stations | 6 Planks, 2 Nails, 3 Branches |
| Tailor's Frame | Three rugs, scarves and upholstery; a Chair becomes a Fancy Armchair and a Wooden Bench becomes a Fancy Couch | 5 Planks, 1 Nails, 2 Spools of Thread, 1 Branch |
| Village Bake Oven | Pretzels, croissants, apple strudel, bread, flatbread and muffins | 12 Rocks, 4 Planks, 7 Iron Ore, 2 Nails |

The oven charges branch fuel **per batch**; it has no separate ignition or fire timer. Each specialist has ten quantity slots and a searchable, station-specific recipe book. Input order does not matter. The three station sprites are fixed-facing. Furniture outputs use the original game items, including their native placement behavior.

Across the 26 recipes, 3 outputs are the new stations and 23 are native items. Every recipe adds labor value at normal base prices, including the oven's fuel. At ordinary merchant rates a player cannot profit by buying all the inputs at base price and immediately selling the output, even when using the cheaper intermediates from Village Trades. Gathering ingredients can pay; scarcity, market events and item quality can change actual returns. No native item prices or merchant prices are changed.

## Install and packs

- Custom Crafting Recipes 1.5.0 needs ModdingTools. The stations also need Custom Item Loader (tested: 3.0.9).
- Put `silverpine.village-workshops.item.pack` in `BepInEx/config/CustomItemLoader/` and `silverpine.village-workshops.recipe.pack` in `BepInEx/config/CustomCraftingRecipes/`. Restart after changing the DLL or item pack. Village Trades is complementary, not required.
- The Recipe Editor can enable or disable the recipes without removing the furniture. To retire the item pack, back up the save and remove placed stations and inventory copies first. Keep the item pack enabled while a save references it.
- To check placement in a save: place the three stations beside a table, open each, check the menu title and book, close, save/reload, and pick one up and place it again. An unrelated CustomCrafting or repair bench should still open its own UI.
