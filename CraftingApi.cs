#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SilverpineMods.CustomCraftingRecipes;

/// <summary>Immutable facts captured from the actual craft, not its preview.</summary>
public sealed class CraftContext
{
    public Player Player { get; }
    public CraftingStation Station { get; }
    /// <summary>Pack-qualified id, or null for a native cooking/alchemy result.</summary>
    public string? RecipeId { get; }
    public string ResultName { get; }
    public string ResultSpriteKey { get; }
    public ItemCategory Category { get; }
    public int BaseQuantity { get; }
    public int WorkMinutes { get; }
    public bool SupportsQuality { get; }
    public bool SupportsDurability { get; }
    public IReadOnlyDictionary<string,int> Ingredients { get; }

    internal CraftContext(Player player, CraftingStation station, ResultData result, IReadOnlyList<Item> inputs, int quantity)
    {
        Player = player; Station = station; RecipeId = (result as BatchResult)?.RecipeId;
        Item item = result.resultItem;
        ResultName = item.name; ResultSpriteKey = item.spriteName ?? ""; Category = item.itemCategory;
        BaseQuantity = quantity; WorkMinutes = result.workMinutes;
        SupportsQuality = item.GetItemComponent<IQualityItemComponent>() != null;
        SupportsDurability = item.GetItemComponent<ItemComponent_Durability>() != null;
        Ingredients = new ReadOnlyDictionary<string,int>(inputs.GroupBy(i => i.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>Proposed per-craft bonuses. default means no change. Percentages are additive.</summary>
public readonly struct CraftBonus
{
    public int ExtraItems { get; }
    public int QualitySteps { get; }
    public int DurabilityPercent { get; }
    public int ValuePercent { get; }

    public CraftBonus(int extraItems = 0, int qualitySteps = 0, int durabilityPercent = 0, int valuePercent = 0)
    {
        ExtraItems = Check(extraItems, CraftingApi.MaxExtraItems, nameof(extraItems));
        QualitySteps = Check(qualitySteps, (int)Quality.Legendary, nameof(qualitySteps));
        DurabilityPercent = Check(durabilityPercent, 100, nameof(durabilityPercent));
        ValuePercent = Check(valuePercent, 100, nameof(valuePercent));
    }
    private static int Check(int value, int max, string name) => value >= 0 && value <= max
        ? value : throw new ArgumentOutOfRangeException(name, $"Expected 0 through {max}.");

    internal CraftBonus Plus(CraftBonus b) => new(
        Math.Min(CraftingApi.MaxExtraItems,ExtraItems+b.ExtraItems), Math.Min((int)Quality.Legendary,QualitySteps+b.QualitySteps),
        Math.Min(100,DurabilityPercent+b.DurabilityPercent), Math.Min(100,ValuePercent+b.ValuePercent));
}

public sealed class CraftReceipt
{
    public CraftContext Craft { get; }
    /// <summary>Exact result instances accepted by the game, never inventory templates.</summary>
    public IReadOnlyList<Item> Outputs { get; }
    public int ExtraItems => Outputs.Count - Craft.BaseQuantity;
    internal CraftReceipt(CraftContext craft, IReadOnlyList<Item> outputs)
    { Craft = craft; Outputs = Array.AsReadOnly(outputs.ToArray()); }
}

/// <summary>Versioned, optional skills integration. Callbacks execute on the game's main thread.</summary>
public static partial class CraftingApi
{
    public const int ApiVersion = 2;
    public const int MaxExtraItems = 12;
    private static readonly object gate = new();
    private static volatile Registration[] registrations = Array.Empty<Registration>();

    /// <summary>Register a unique id. Ascending priority, then ordinal id. Dispose to unregister.</summary>
    public static IDisposable Register(string id, Func<CraftContext,CraftBonus>? modify = null,
        Action<CraftReceipt>? completed = null, int priority = 0)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A unique registration id is required.", nameof(id));
        if (modify == null && completed == null) throw new ArgumentException("Supply a modifier or a completion observer.");
        id = id.Trim();
        lock (gate)
        {
            if (registrations.Any(r => r.Id.Equals(id,StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Duplicate crafting registration: " + id);
            var entry = new Registration(id,priority,modify,completed);
            registrations = registrations.Concat(new[]{entry}).OrderBy(r=>r.Priority).ThenBy(r=>r.Id,StringComparer.Ordinal).ToArray();
            return entry;
        }
    }

    private sealed class Registration : IDisposable
    {
        internal readonly string Id;
        internal readonly int Priority;
        internal readonly Func<CraftContext,CraftBonus>? Modify;
        internal readonly Action<CraftReceipt>? Completed;
        internal Registration(string id,int priority,Func<CraftContext,CraftBonus>? modify,Action<CraftReceipt>? completed)
        { Id=id; Priority=priority; Modify=modify; Completed=completed; }
        public void Dispose() { lock(gate) registrations = registrations.Where(r => !ReferenceEquals(r,this)).ToArray(); }
    }

    internal static bool TryDeliver(Player player, CraftingStation station, ResultData result, Inventory inventory,
        IReadOnlyList<Item> ingredients, out IReadOnlyList<Item> units, out string error,
        Func<Item,Item>? clone = null, Action<Item>? remove = null, Func<Item,bool>? add = null)
    {
        units = BatchResult.Units(result);
        string? denied = LockReason(player, station, result);
        if (denied != null) { error = denied; return false; }
        var hooks = registrations;
        if (hooks.Length == 0) return CraftTransaction.TryComplete(inventory, ingredients, units, out error, remove, add);
        if (!CraftTransaction.CanComplete(inventory, ingredients, units, out error)) return false;
        var ctx = new CraftContext(player,station,result,ingredients,units.Count);
        CraftBonus bonus = default;
        foreach (var hook in hooks)
        {
            if (hook.Modify == null) continue;
            try { bonus = bonus.Plus(hook.Modify(ctx)); }
            catch (Exception ex) { Plugin.Log?.LogError($"Ignoring crafting modifier '{hook.Id}': {ex}"); }
        }
        int extras = bonus.ExtraItems > 0 && units.All(RecipeResultFactory.CanDuplicate) ? bonus.ExtraItems : 0;
        bool stats = (bonus.QualitySteps > 0 && ctx.SupportsQuality) ||
            (bonus.DurabilityPercent > 0 && ctx.SupportsDurability) || bonus.ValuePercent > 0;
        if (extras > 0 || stats)
        {
            clone ??= i => i.DeepClone();
            var expanded = new List<Item>(units.Count + extras);
            try
            {
                foreach (Item unit in units) expanded.Add(stats ? clone(unit) : unit);
                for (int i = 0; i < extras; ++i) expanded.Add(clone(units[0]));
                // Never mutate preview or catalog objects. Validate the full batch before touching inventory.
                if (!CraftTransaction.CanComplete(inventory, ingredients, expanded, out error)) return false;
                if (stats) foreach (Item item in expanded) ApplyStats(item,bonus);
                units = expanded;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError("Could not prepare crafting bonuses: " + ex);
                error = "Craft cancelled: could not prepare the modified reward. Ingredients were not used.";
                return false;
            }
        }
        if (!CraftTransaction.TryComplete(inventory, ingredients, units, out error, remove, add)) return false;
        var receipt = new CraftReceipt(ctx,units);
        foreach (var hook in hooks)
        {
            try { hook.Completed?.Invoke(receipt); }
            catch (Exception ex) { Plugin.Log?.LogError($"Craft was delivered; observer '{hook.Id}' failed: {ex}"); }
        }
        return true;
    }

    private static void ApplyStats(Item item, CraftBonus bonus)
    {
        if (bonus.QualitySteps > 0)
            foreach (var quality in item.GetItemComponents<IQualityItemComponent>())
                quality.Quality = (Quality)Math.Min((int)Quality.Legendary,(int)quality.Quality + bonus.QualitySteps);
        if (bonus.DurabilityPercent > 0 && item.GetItemComponent<ItemComponent_Durability>() is { } d)
        {
            double factor = 1.0 + bonus.DurabilityPercent/100.0;
            d.maxDurability = (float)Math.Min(float.MaxValue,d.maxDurability*factor);
            d.Durability = (float)Math.Min(d.maxDurability,d.Durability*factor);
        }
        if (bonus.ValuePercent > 0)
            item.value = (int)Math.Min(int.MaxValue,((long)item.value*(100+bonus.ValuePercent)+50)/100);
    }
}
