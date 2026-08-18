# Custom Crafting Recipes

A BepInEx/Harmony plugin for Silverpine 1.7.3 that completes the dormant
`CustomCrafting` workbench with configurable, recipe-based crafting.

## What it does

- Replaces the unfinished `CustomCraftingUIData` behavior and extends the
  existing Alchemy and Cooking UIs with optional configured recipes.
- Gives the CustomCrafting UI ten ingredient-type slots with per-slot `-` and
  `+` quantity controls.
- Matches selected ingredients by item name, in any order.
- Supports repeated ingredients through `amount`; quantities consume one
  CustomCrafting slot per distinct item type.
- Allows recipes in CustomCrafting (ten slots), Alchemy (four slots), and
  Cooking (three slots), while preserving each UI's native recipes.
- Resolves outputs from `ItemLibrary`, including items registered there by
  Custom Item Loader.
- Uses Silverpine's native crafting flow for work time, ingredient removal,
  inventory-capacity checks, result delivery, sounds, and workbench callbacks.
- Reloads recipes with **F6**. Close and reopen a crafting menu afterward so it
  can rebuild its ingredient choices.
- Discovers independent `*.recipe.pack` folders with pack IDs, display names,
  and enabled states.
- Adds **Custom Recipe Editor** to the Modding Tools main menu. It provides
  pack creation, Enable Pack and Disable Pack buttons, recipe editing, and a
  live item browser that includes and labels Custom Item Loader items.

Native Alchemy and Cooking recipes remain available. Repair and
combination-item crafting are not changed.

## Recipe file

On first launch, the plugin creates a starter pack at:

`BepInEx/config/CustomCraftingRecipes/Starter.recipe.pack/recipes.json`

Additional packs use the same layout:

`BepInEx/config/CustomCraftingRecipes/<Name>.recipe.pack/recipes.json`

The included default recipe uses two quantity slots and only verified native
items:

```json
{
  "schemaVersion": 1,
  "packId": "silverpine.starter-recipes",
  "displayName": "Starter Recipes",
  "enabled": true,
  "recipes": [
    {
      "id": "wooden-door",
      "enabled": true,
      "stations": [ "CustomCrafting" ],
      "ingredients": [
        { "item": "Plank", "amount": 4 },
        { "item": "Nails", "amount": 1 }
      ],
      "result": "Wooden Door",
      "workMinutes": 20,
      "description": "Fit the planks together and fasten them with nails."
    }
  ]
}
```

`item` and `result` are case-insensitive display names from `ItemLibrary`.
Quality-prefixed native results such as `Superior|Iron Sword` are supported.
Custom Item Loader outputs should use their unique display names. If two
registered items share a display name, the first `ItemLibrary` match wins.

`packId` must be unique. Runtime/log identities use the qualified form
`packId:recipeId`. Setting the pack-level `enabled` field to `false`, or using
the editor's **Disable Pack** button, unloads every recipe in that pack. The
button saves the state and reloads packs immediately.

If the old single-file `CustomCraftingRecipes/recipes.json` layout is found and
no pack folders exist, it is automatically copied into
`Migrated.recipe.pack/recipes.json`; the legacy file is retained but ignored.

`stations` may contain `CustomCrafting`, `Alchemy`, and/or `Cooking`.
CustomCrafting accepts up to ten distinct ingredient types and lets each type
have its own quantity. Alchemy and Cooking retain one item per UI field, so
their total ingredient amounts may not exceed four and three, respectively.

Recipes with the same ingredient multiset in the same station are ambiguous,
so the editor blocks them and the runtime keeps the first valid definition. A
recipe may use at most ten distinct ingredient types. The editor currently
allows a quantity of 1-999 for each type.

## Build

```powershell
dotnet build -c Release
```

The DLL is written to `bin/Release/CustomCraftingRecipes.dll`.

## Credits

Created by **Saelac and ChatGPT**.
