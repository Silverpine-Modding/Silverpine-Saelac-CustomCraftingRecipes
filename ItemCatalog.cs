#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace SilverpineMods.CustomCraftingRecipes;

internal static class ItemCatalog
{
    private static readonly Dictionary<string, Item> byName = new(StringComparer.OrdinalIgnoreCase);
    private static (Item Item, string Name, string Sprite)[] snapshot = Array.Empty<(Item, string, string)>();
    private static long nextCheck;
    internal static int Revision { get; private set; }
    internal static IReadOnlyList<Item> Items { get; private set; } = Array.Empty<Item>();
    internal static int CustomCount { get; private set; }
    internal static bool IsCustom(Item item) => item.spriteName?.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) == true;

    internal static void Refresh(bool force = false)
    {
        long now = DateTime.UtcNow.Ticks;
        if (!force && now < nextCheck && ItemLibrary.Items.Count == snapshot.Length) return;
        nextCheck = now + TimeSpan.TicksPerSecond / 2;
        RefreshSource(ItemLibrary.Items, force);
    }

    internal static void RefreshSource(IReadOnlyList<Item> source, bool force = false)
    {
        bool changed = source.Count != snapshot.Length;
        if (!changed)
            for (int i = 0; i < source.Count; i++)
                if (!ReferenceEquals(source[i], snapshot[i].Item) || (source[i]?.name ?? "") != snapshot[i].Name || (source[i]?.spriteName ?? "") != snapshot[i].Sprite)
                { changed = true; break; }
        if (!changed && !force) return;
        snapshot = source.Select(item => (item, item?.name ?? "", item?.spriteName ?? "")).ToArray();
        byName.Clear();
        foreach (Item item in source)
            if (item != null && !string.IsNullOrWhiteSpace(item.name) && !byName.ContainsKey(item.name)) byName.Add(item.name, item);
        Items = byName.Values.OrderBy(item => IsCustom(item) ? 0 : 1)
            .ThenBy(item => item.name, StringComparer.OrdinalIgnoreCase).ToArray();
        CustomCount = Items.Count(IsCustom);
        Revision++;
    }

    internal static bool Contains(string? name, bool allowQuality)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string trimmed = name.Trim();
        if (allowQuality && trimmed.Contains('|'))
        {
            string[] parts = trimmed.Split('|');
            if (parts.Length != 2 || !Enum.TryParse(parts[0], true, out Quality quality) || !Enum.IsDefined(typeof(Quality), quality)) return false;
            trimmed = parts[1];
        }
        return byName.ContainsKey(trimmed);
    }
}
