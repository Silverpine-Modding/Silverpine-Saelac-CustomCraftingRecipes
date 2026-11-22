#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SilverpineMods.CustomCraftingRecipes;

/// <summary>Available before item selection as well as during crafting. Contains no mutable recipe data.</summary>
public readonly struct RecipeAccessContext
{
    public Player Player { get; }
    public CraftingStation Station { get; }
    public string? RecipeId { get; }
    public string ResultName { get; }
    internal RecipeAccessContext(Player player, CraftingStation station, string? id, string name)
    { Player = player; Station = station; RecipeId = id; ResultName = name; }
}

public static partial class CraftingApi
{
    // Publish new collections only when a mod registers/disposes; queries don't take a lock.
    private static volatile Dictionary<string,AccessProvider> skills = new(StringComparer.OrdinalIgnoreCase);
    private static volatile AccessProvider[] rules = Array.Empty<AccessProvider>();
    private static int accessRevision;
    internal static int AccessRevision => Volatile.Read(ref accessRevision);

    /// <summary>Tell open menus to refresh immediately after a level or permission changes.
    /// Optional: visible requirements are also refreshed periodically. Actual crafts always check live.</summary>
    public static void NotifyRecipeAccessChanged() => Interlocked.Increment(ref accessRevision);

    /// <summary>Provide per-player levels for this ID in schema-3 pack skillRequirements.</summary>
    public static IDisposable RegisterSkill(string skillId, string displayName, Func<Player,int> getLevel)
    {
        skillId = skillId?.Trim() ?? "";
        if (!ValidSkillId(skillId)) throw new ArgumentException("Invalid skill ID.", nameof(skillId));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A display name is required.", nameof(displayName));
        if (getLevel == null) throw new ArgumentNullException(nameof(getLevel));
        lock (gate)
        {
            if (skills.ContainsKey(skillId)) throw new InvalidOperationException("Duplicate skill: " + skillId);
            var provider = new AccessProvider(skillId, displayName.Trim(), getLevel, null);
            skills = new Dictionary<string,AccessProvider>(skills,StringComparer.OrdinalIgnoreCase) { [skillId] = provider };
            NotifyRecipeAccessChanged();
            return provider;
        }
    }

    /// <summary>Return null/empty to allow, or a player-facing reason to lock. Gates are ANDed,
    /// checked in ordinal ID order, and may also apply to native Cooking/Alchemy (RecipeId == null).</summary>
    public static IDisposable RegisterRecipeGate(string id, Func<RecipeAccessContext,string?> lockedReason)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A unique gate ID is required.", nameof(id));
        if (lockedReason == null) throw new ArgumentNullException(nameof(lockedReason));
        id = id.Trim();
        lock (gate)
        {
            if (rules.Any(r => r.Id.Equals(id,StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Duplicate recipe gate: " + id);
            var provider = new AccessProvider(id, id, null, lockedReason);
            rules = rules.Concat(new[] { provider }).OrderBy(r => r.Id,StringComparer.Ordinal).ToArray();
            NotifyRecipeAccessChanged();
            return provider;
        }
    }

    internal static bool ValidSkillId(string? id) => !string.IsNullOrEmpty(id) &&
        id!.All(c => char.IsLetterOrDigit(c) || c is '.' or ':' or '-' or '_');

    private sealed class AccessProvider : IDisposable
    {
        internal readonly string Id, Label;
        internal readonly Func<Player,int>? Level;
        internal readonly Func<RecipeAccessContext,string?>? Rule;
        private int logged;
        internal AccessProvider(string id, string label, Func<Player,int>? level, Func<RecipeAccessContext,string?>? rule)
        { Id=id; Label=label; Level=level; Rule=rule; }
        internal string Failure(Exception ex)
        {
            if (Interlocked.Exchange(ref logged,1)==0) Plugin.Log?.LogError($"Recipe access check '{Id}' failed: {ex}");
            return $"Cannot verify recipe requirement: {Label}.";
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (Level != null)
                {
                    if (!skills.TryGetValue(Id,out var current) || current != this) return;
                    var copy = new Dictionary<string,AccessProvider>(skills,StringComparer.OrdinalIgnoreCase);
                    copy.Remove(Id); skills = copy;
                }
                else
                {
                    if (Array.IndexOf(rules,this)<0) return;
                    rules = rules.Where(r => r != this).ToArray();
                }
                NotifyRecipeAccessChanged();
            }
        }
    }

    internal static string? LockReason(Player player, CraftingStation station, ResultData result)
    {
        if (result.resultItem == null) return null;
        if (result is not BatchResult batch) return Check(player,station,null,result.resultItem.name);
        var recipe = batch.Recipe;
        // A reload during a timed craft must not let an obsolete unlocked preview bypass a new requirement.
        if (batch.Revision != RecipeRegistry.Revision)
        {
            if (!RecipeRegistry.TryFindRecipe(station,recipe.RequiredItems,out var current) || current == null ||
                !string.Equals(current.Id,recipe.Id,StringComparison.OrdinalIgnoreCase) ||
                current.ResultName != recipe.ResultName || current.ResultAmount != recipe.ResultAmount || current.WorkMinutes != recipe.WorkMinutes)
                return "Recipe changed or was disabled. Select the ingredients again.";
            recipe = current;
        }
        return LockReason(player,station,recipe);
    }

    internal static string? LockReason(Player player, CraftingStation station, CompiledRecipe recipe) =>
        Check(player,station,recipe,recipe.AccessResultName);

    private static string? Check(Player player, CraftingStation station, CompiledRecipe? recipe, string name)
    {
        if (recipe != null && recipe.Skills.Length != 0)
        {
            var providers = skills;
            foreach (var (id, minimum) in recipe.Skills)
            {
                if (!providers.TryGetValue(id,out var provider)) return $"Requires {id} level {minimum} (skill provider not loaded).";
                int actual;
                try { actual = provider.Level!(player); }
                catch (Exception ex) { return provider.Failure(ex); }
                if (actual < minimum) return $"Requires {provider.Label} level {minimum} (current: {actual}).";
            }
        }
        var gates = rules;
        if (gates.Length == 0) return null;
        var context = new RecipeAccessContext(player,station,recipe?.Id,name);
        foreach (var provider in gates)
        {
            try
            {
                string? denial = provider.Rule!(context);
                if (!string.IsNullOrWhiteSpace(denial)) return denial;
            }
            catch (Exception ex) { return provider.Failure(ex); }
        }
        return null;
    }
}
