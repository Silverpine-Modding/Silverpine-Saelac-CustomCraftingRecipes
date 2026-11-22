using System;
using SilverpineMods.CustomCraftingRecipes;

namespace ExampleSkills;

// Call once after your skill store is ready; keep/dispose the returned handle.
// Level and XP are owned by your mod (and by the appropriate player/save).
public static class SmithSkills
{
    // Lets any recipe pack use {"skill":"example.skills.smithing","minLevel":5}.
    public static IDisposable PublishSmithingLevels(Func<Player,int> smithingLevel) =>
        CraftingApi.RegisterSkill("example.skills.smithing", "Blacksmithing", smithingLevel);

    // Alternatively, lock a recipe in an existing pack without editing its JSON.
    public static IDisposable LockPickaxe(Func<Player,int> smithingLevel) =>
        CraftingApi.RegisterRecipeGate("example.skills.unlock-pickaxe", context =>
            string.Equals(context.RecipeId, "silverpine.village-foundry:forge-pickaxe", StringComparison.OrdinalIgnoreCase)
            && smithingLevel(context.Player) < 5 ? "Requires Blacksmithing level 5." : null);

    public static IDisposable Connect(Func<Player,CraftingStation,int> levelOf,
        Action<Player,CraftingStation,int> awardXp, Func<double> nextRoll)
    {
        if (levelOf == null || awardXp == null || nextRoll == null) throw new ArgumentNullException();
        return CraftingApi.Register("example.skills.metals", modify: craft =>
        {
            if (craft.Station != CraftingStation.Blacksmithing && craft.Station != CraftingStation.Smelting) return default;
            int level = Math.Max(0, Math.Min(10, levelOf(craft.Player, craft.Station)));
            // One random sample for the entire batch (including all its units).
            double roll = nextRoll(); // e.g. System.Random.NextDouble: [0,1)
            if (craft.Station == CraftingStation.Smelting)
                return new CraftBonus(extraItems: craft.ResultName.EndsWith(" Ingot",StringComparison.Ordinal) && roll < 0.02*level ? 1 : 0);
            return new CraftBonus(qualitySteps: craft.SupportsQuality && roll < 0.03*level ? 1 : 0,
                durabilityPercent: craft.SupportsDurability ? level*2 : 0);
        }, completed: receipt =>
        {
            var craft = receipt.Craft;
            if (craft.Station == CraftingStation.Blacksmithing || craft.Station == CraftingStation.Smelting)
                awardXp(craft.Player, craft.Station, Math.Max(1, craft.WorkMinutes/10));
        });
    }
}
