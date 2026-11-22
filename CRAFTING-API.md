# Crafting API v2 (Custom Crafting Recipes 1.7.0+)

Reference `CustomCraftingRecipes.dll` and the game's `Assembly-CSharp.dll`; don't ship copies of them in a skills mod. Use the namespace `SilverpineMods.CustomCraftingRecipes` and BepInEx dependency `[BepInDependency("renegadex.silverpine.customcraftingrecipes", "1.7.0")]`. The [SkillBridge example](Examples/SkillBridge/SmithSkills.cs) connects your own per-player level and XP store. It is sample code, not an installed skills system. The v1 reward/XP registration signature is unchanged.

## Level-gated recipes

Publish a stable skill ID once, after your skill store is ready. Keep the returned handle until deliberate unload:

```csharp
var smithing = CraftingApi.RegisterSkill("my.skills.smithing", "Blacksmithing",
    player => GetSmithingLevel(player)); // your lookup, for the supplied player
```

In **Mod Tools → Custom Recipe Editor → Add Skill Requirement**, enter `my.skills.smithing` and minimum level `5`. Save the pack. Alternatively, use pack-level `"schemaVersion": 3` and add this field to a recipe:

```json
"skillRequirements": [{ "skill": "my.skills.smithing", "minLevel": 5 }]
```

All listed skills must meet their minimum, inclusively. The editor upgrades the schema when needed. Old packs require no changes. If a required skill provider is absent, the recipe stays visible but locked and tells the player why. Other recipes are unaffected.

To gate an existing recipe **without editing its pack**, register a rule instead:

```csharp
var pickaxeRule = CraftingApi.RegisterRecipeGate("my.skills.pickaxe", recipe =>
    recipe.RecipeId == "silverpine.village-foundry:forge-pickaxe" && GetSmithingLevel(recipe.Player) < 5
        ? "Requires Blacksmithing level 5." : null);
```

Rules can inspect `Player`, `Station`, `RecipeId`, and `ResultName` (base name without a quality prefix). For native Cooking/Alchemy, `RecipeId` is null: match station and result name instead. Return null/empty for recipes you don't restrict. All checks must allow the craft; the first denial is shown. Failed providers/rules deny access and log their exception once per registration. Dispose a rule to remove it. Disposing a skill provider leaves recipes that explicitly require it locked.

Access checks run during browsing, so keep them cheap and side-effect-free: no RNG, XP, or inventory changes. Open menus refresh within half a second; call `CraftingApi.NotifyRecipeAccessChanged()` after a level change for a refresh on the next UI update. Starting work and delivering the result always check live. If the player no longer qualifies at completion, work time has elapsed but no ingredients are taken, output granted, bonus rolled, or XP callback sent.

## Reward and completion hooks

```csharp
IDisposable handle = CraftingApi.Register("my.skills.smith", modify: craft =>
{
    if (craft.Station != CraftingStation.Blacksmithing) return default;
    bool bonus = UnityEngine.Random.value < 0.10f; // one roll per batch
    return new CraftBonus(extraItems: bonus ? 1 : 0, qualitySteps: 1, durabilityPercent: 10);
}, completed: receipt =>
{
    if (receipt.Craft.Station == CraftingStation.Blacksmithing)
        AwardSmithingXp(receipt.Craft.Player, receipt.Craft.WorkMinutes); // your API
});
```

- **Scope.** Covers configured recipes at all eight stations and native Cooking/Alchemy crafts that pass through this plugin. Native Repair, item combination, loot and other mods' independent crafting systems do not trigger these hooks. Native non-pack recipes have a null `RecipeId`; configured recipes retain the qualified `packId:recipeId` from the preview.
- **Timing.** Modifiers run on the Unity main thread when work finishes, after checking the original ingredients and base capacity. Never on preview, on interruption or when the base craft is already invalid. Use `modify` to return a proposal, not to award XP, mutate inventory, or save progression. There is no built-in roll: your mod owns its chance curve and RNG.
- **Context.** Read the player, station, recipe ID, result name/sprite key/category, base quantity, work time, supported quality/durability, and an immutable count of the inputs. `CraftingApi.ApiVersion == 2`. Reward hook order is ascending priority and then ordinal registration ID. Duplicate IDs (case-insensitive) are rejected. Hold onto the disposable process-lifetime registration; dispose on deliberate unload. The reward hook snapshot for a craft is fixed at delivery.
- **Bonuses.** Up to 12 extra independent copies per craft in addition to the base yield (up to 24 total). Items with special inventory callbacks can't be duplicated. Native quality advances by 0–6 steps, capped at Legendary, only on components implementing `IQualityItemComponent`. Durability and base value increases are additive percentages from 0–100 each, with the same caps across all providers. Durability scales current and maximum together. Native quality is Silverpine's starred rarity; no synthetic rarity field is added to plain goods. Equipment uses its own native quality multipliers. Changes apply only to these output instances and use the game's normal save fields.
- **Delivery.** If the whole modified batch can't fit or insertion fails, no completion hook fires and ingredients are retained/restored. The work time has already elapsed. A throwing modifier is logged and its proposal ignored. A throwing completion observer cannot undo delivery or stop other observers. No handlers means the ordinary crafting transaction runs unchanged.
- **Completion.** `receipt.Craft` provides the context, `receipt.ExtraItems` the actual bonus count, and `receipt.Outputs` a read-only list of the actual item instances accepted by the game. Use this stage for XP or integrating your own persistent per-instance affix system. Native pickup effects can transform an accepted output; acceptance is not a promise it still occupies an inventory slot. Don't reinsert items or grant yield here.
