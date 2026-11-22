# Village Foundry 1.6.0

For Silverpine 1.7.3 with BepInEx, ModdingTools and Custom Item Loader (tested with 3.0.9). With the game closed, extract the ZIP into the game directory containing `BepInEx`. The ZIP includes the updated Custom Crafting Recipes DLL, two new Foundry packs, and optional mod-author documentation/source. It does not replace Village Trades or Village Workshops. Back up edited Foundry packs before reinstalling.

To begin, use a native Metalworking Bench → **Blacksmithing → Browse recipes → Smelting Furnace**, or build the furnace at an ordinary CustomCrafting station. It costs 12 Rock, 3 Plank, 2 Nails and 3 Iron Ore. Place it to open Smelting. Use Custom Recipe Editor in the Mod Tools menu to add your own recipes to either station.

- [Foundry recipes, pack-author example and save notes](BepInEx/config/CustomCraftingRecipes/silverpine.village-foundry.recipe.pack/GUIDE.md)
- [Skills API](CRAFTING-API.md) and [integration example](Examples/SkillBridge/SmithSkills.cs). No skills mod is installed or required.
- [Furnace and materials art / prompts](BepInEx/config/CustomItemLoader/silverpine.village-foundry.item.pack/ART.md)
