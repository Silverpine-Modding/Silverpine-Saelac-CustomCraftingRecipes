# Custom Crafting Recipes 1.7.0: skill unlocks

This update adds recipe access checks and editor support. With the game closed, extract the ZIP into the Silverpine directory containing `BepInEx`. It replaces the crafting DLL, not your recipes or saves. Requires the same BepInEx and ModdingTools setup as 1.6.0. Village Foundry and other optional content packs are not duplicated in this update.

A skills mod supplies the player's actual levels. In **Mod Tools → Custom Recipe Editor**, select a recipe, **Add Skill Requirement**, enter its published skill ID and minimum level, and save. Recipes with no requirements continue to work without a skills mod. Do not downgrade a pack with requirements below schema 3.

See the included `CRAFTING-API.md` for JSON and API examples, or `Examples/SkillBridge/SmithSkills.cs` for working integration code. The ZIP does not install a skills progression mod or impose levels on existing recipes.
