# Village Foundry (Silverpine 1.7.3)

Use a Metalworking Bench and select **Blacksmithing**, or use any ordinary CustomCrafting station (including Mr Bones). Under **Browse recipes**, select **Smelting Furnace**: 12 Rock, 3 Plank, 2 Nails and 3 Iron Ore. It takes 175 in-game minutes. Place the finished furnace as furniture; it is not a cooking oven. A Joiner's Bench can also build it if Village Workshops is installed. Aldric can stock the native Metalworking Bench; the Foundry pack also lets an ordinary or Joiner's Bench build one once you have ingots.

1. At the furnace, bank **7 Branch → 3 Charcoal**. Charcoal also works as ordinary stove fuel (90 in-game minutes per charge).
2. Choose an ore: **2 Iron, Copper, Silver or Gold Ore + 1 Charcoal → 1 matching Ingot**. One charge is consumed per smelt. There is no separate lighting step or real-time furnace timer.
3. At the Metalworking Bench, select **Blacksmithing**, then **Browse recipes**. For example, **1 Iron Ingot + 1 Charcoal → 2 Nails**. Other outputs include tools, copper candlesticks, silver necklaces and gold jewelry. Repair remains a separate, unchanged action.

There are 18 recipes: 2 station builds, 5 furnace processes, and 11 smithing recipes. Labor increases the base value at each step, including fuel and intermediate costs. These prices are balanced at normal quality and normal market conditions, without optional skill bonuses. Existing packs with direct ore recipes still work.

## Add your own recipes

Open **Mod Tools → Custom Recipe Editor**, create or select your own pack, and add a recipe. Under **Crafting stations**, select **Smelting Furnace** or **Blacksmithing** and clear any other station you don't want. Pick ingredients and an output from the item browser, set amounts, yield and work time, then save and enable the pack. Registered Custom Item Loader items are available here too; restart after changing an item pack.

Alternatively save this as `BepInEx/config/CustomCraftingRecipes/my.metals.recipe.pack/recipes.json`. This example assumes your item pack defines Meteor Ore, Meteor Billet and Meteor Clasp; recipes reference their exact display names, not their item IDs.

```json
{
  "schemaVersion": 2,
  "packId": "my.metals",
  "displayName": "Meteor Metalwork",
  "enabled": true,
  "recipes": [
    {
      "id": "refine-meteor", "stations": ["Smelting"],
      "ingredients": [{"item": "Meteor Ore", "amount": 3}, {"item": "Charcoal", "amount": 1}],
      "result": "Meteor Billet", "resultAmount": 1, "workMinutes": 100
    },
    {
      "id": "meteor-clasps", "stations": ["Blacksmithing"],
      "ingredients": [{"item": "Meteor Billet", "amount": 1}],
      "result": "Meteor Clasp", "resultAmount": 2, "workMinutes": 45
    }
  ]
}
```

Reload JSON packs with F6; reopen the station. Both menus support ten distinct ingredient types, quantities and order-independent matching. Fuel is explicit recipe data, not a hidden mandatory ingredient. The native bench offers Blacksmithing whenever *any* enabled pack has a recipe there. The furnace requires its furniture item pack to be loaded.

## Installation and saves

Requires Custom Crafting Recipes 1.6.0 and its ModdingTools dependency; the Foundry assets also need Custom Item Loader (tested with 3.0.9). The two packs are independent of Village Trades and Village Workshops. Use the Recipe Editor to disable recipes. Keep the item pack installed while a save contains the furnace, charcoal or ingots. No need to replace your existing packs.
