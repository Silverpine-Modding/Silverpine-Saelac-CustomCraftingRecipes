#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SilverpineMods.CustomCraftingRecipes;

internal static class VirtualItemList
{
    // Fixed-height rows allow large custom-item libraries without constructing
    // thousands of off-screen IMGUI controls on every event.
    internal static Item? Draw(IReadOnlyList<Item> items, ref Vector2 scroll, Func<Item, string> label, float height = 650)
    {
        const float rowHeight = 44;
        Rect viewport = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
        Rect content = new(0, 0, Math.Max(1, viewport.width - 22), items.Count * rowHeight);
        scroll = GUI.BeginScrollView(viewport, scroll, content);
        int first = Math.Max(0, (int)(scroll.y / rowHeight));
        int last = Math.Min(items.Count, first + (int)Math.Ceiling(viewport.height / rowHeight) + 1);
        Item? selected = null;
        for (int index = first; index < last; index++)
            if (GUI.Button(new Rect(0, index * rowHeight, content.width, rowHeight - 4), label(items[index]))) selected = items[index];
        GUI.EndScrollView();
        return selected;
    }
}
