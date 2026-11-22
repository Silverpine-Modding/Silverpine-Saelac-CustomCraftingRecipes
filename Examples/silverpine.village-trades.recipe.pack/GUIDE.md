# Village Trades

35 recipes using only native Silverpine 1.7.3 items. Requires CustomCraftingRecipes 1.4.0 or newer; no item packs or new workstation prefabs required.

## Using the pack

Place the entire `silverpine.village-trades.recipe.pack` folder inside `BepInEx/config/CustomCraftingRecipes/`. Start the game, or press F6 and reopen the station. Look for **Village Trades** in the ModTools Recipe Editor to disable it or adjust a recipe. It can coexist with the default Wooden Door recipe. Other packs that register the same combination at the same station compete under the loader’s usual first-valid-match rule.

At a CustomCrafting bench, **Browse recipes** searches by result, ingredient, or pack ID and fills in the required quantities. It does not start work. At Cooking and Alchemy stations, use the table below to fill the existing slots in any order (one item per slot). Standard cooking requires a lit fire. Yield is the **total** received per craft, not an amount per slot. The full batch must fit after the ingredients are consumed.

## What labor is worth

The value columns are the sum of the native template prices for the materials and the finished batch, in coins. All 35 crafts add value. No item has a modded price, and a crafted shirt is the same shirt a shop sells. A roll worth 30 becomes four shirts worth 40 after 150 minutes of tailoring; flour worth 20 becomes two loaves worth 28 after 70 minutes of baking.

These are **goods values, not guaranteed cash proceeds**. At the default vendor rate, a trader pays about half the final item value, rounds each unit separately, and may refuse a category or lack funds. One-coin utensils are for use, not reliable trade goods. Gathered materials have an opportunity cost: high-quality ore or herbs may be worth more sold raw, especially when the market pays well. The bench uses lower-quality copies first; store prized materials separately if you never want them consumed. No recipe produces market herbs, ore, turnips, gold piles, keys, or other progression rewards. No cycle returns its own raw materials.

## CustomCrafting workbench

| Result (batch) | Materials consumed | Work (min) | Materials → goods |
| --- | --- | ---: | ---: |
| 3 × Wooden Spoon | 2 × Branch | 20 | 2 → 3 |
| 4 × Wooden Bowl | 3 × Branch | 30 | 3 → 4 |
| 2 × Wooden Plate | 1 × Plank | 15 | 5 → 6 |
| 1 × Basket | 5 × Branch | 35 | 5 → 6 |
| 1 × Cast Iron Pot | 1 × Iron Ore, 2 × Branch | 60 | 5 → 6 |
| 1 × Bucket | 2 × Iron Ore, 2 × Branch | 40 | 8 → 10 |
| 1 × Nails | 3 × Iron Ore | 45 | 9 → 10 |
| 1 × Candlestick | 2 × Copper Ore, 2 × Branch | 50 | 12 → 15 |
| 1 × Silver Necklace | 1 × Silver Ore | 60 | 8 → 10 |
| 1 × Gold Ring | 1 × Gold Ore, 1 × Copper Ore, 1 × Branch | 75 | 16 → 20 |
| 1 × Gold Necklace | 3 × Gold Ore, 1 × Branch | 120 | 31 → 40 |
| 1 × Hourglass | 3 × Glass Vial, 1 × Plank, 1 × Rock | 65 | 11 → 15 |
| 1 × Portable Oil Lantern | 1 × Lantern Oil, 2 × Glass Vial, 1 × Copper Ore, 1 × Iron Ore | 80 | 27 → 35 |
| 1 × Spool of Thread | 5 × Branch, 1 × Burdock Root | 25 | 9 → 10 |
| 1 × Roll of Cloth | 2 × Spool of Thread, 2 × Branch | 85 | 22 → 30 |
| 2 × Towel | 1 × Spool of Thread | 45 | 10 → 12 |
| 4 × Linen Shirt | 1 × Roll of Cloth | 150 | 30 → 40 |
| 5 × Linen Pants | 1 × Roll of Cloth, 1 × Spool of Thread | 180 | 40 → 50 |
| 4 × Linen Robe | 1 × Roll of Cloth, 1 × Branch | 180 | 31 → 40 |
| 3 × Gambeson | 2 × Roll of Cloth, 2 × Branch | 200 | 62 → 75 |
| 4 × Winter Coat | 2 × Roll of Cloth, 2 × Spool of Thread | 240 | 80 → 100 |
| 1 × Hammer | 3 × Iron Ore, 1 × Plank, 1 × Branch | 55 | 15 → 20 |
| 1 × Shovel | 2 × Iron Ore, 2 × Plank | 60 | 16 → 20 |
| 1 × Hand Saw | 4 × Iron Ore, 1 × Plank, 1 × Branch | 70 | 18 → 20 |
| 1 × Pickaxe | 3 × Iron Ore, 1 × Plank, 2 × Branch | 75 | 16 → 20 |
| 1 × Sickle | 4 × Iron Ore, 4 × Branch | 60 | 16 → 20 |

## Cooking

| Result (batch) | Materials consumed | Work (min) | Materials → goods |
| --- | --- | ---: | ---: |
| 2 × Loaf of Bread | 1 × Flour | 70 | 20 → 28 |
| 6 × Porridge | 1 × Flour, 1 × Bottle of Milk | 45 | 25 → 30 |
| 7 × Vegetable Flatbread | 1 × Flour, 1 × Wild Garlic, 1 × Turnip | 75 | 34 → 42 |
| 6 × Muffin | 1 × Flour, 1 × Bottle of Milk, 1 × Juniper Berries | 65 | 29 → 36 |
| 5 × Sweetroll | 1 × Flour, 1 × Vesilberry | 60 | 25 → 30 |
| 2 × Beef Stew | 1 × Raw Large Animal Meat, 1 × Winter Chanterelle, 1 × Wild Garlic | 45 | 14 → 20 |
| 2 × Dried sausage | 1 × Raw Large Animal Meat, 1 × Juniper Berries | 90 | 9 → 10 |
| 2 × Cup of Tea | 1 × Rosehips, 1 × Lavender | 20 | 8 → 10 |

## Alchemy

| Result (batch) | Materials consumed | Work (min) | Materials → goods |
| --- | --- | ---: | ---: |
| 1 × Lantern Oil | 2 × Raw Large Animal Meat, 1 × Glass Vial | 45 | 12 → 15 |
