#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SilverpineMods.CustomCraftingRecipes;

internal static class VirtualItemList
{
    // Fixed-height rows allow large custom-item libraries without constructing
    // thousands of off-screen IMGUI controls on every event.
    internal static T? Draw<T>(IReadOnlyList<T> items, ref Vector2 scroll, Func<T, string> label, float height = 650,
        Func<T,string?>? lockedReason = null) where T : class
    {
        const float rowHeight = 44;
        Rect viewport = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
        Rect content = new(0, 0, Math.Max(1, viewport.width - 22), items.Count * rowHeight);
        scroll = GUI.BeginScrollView(viewport, scroll, content);
        int first = Math.Max(0, (int)(scroll.y / rowHeight));
        int last = Math.Min(items.Count, first + (int)Math.Ceiling(viewport.height / rowHeight) + 1);
        T? selected = null;
        bool enabled = GUI.enabled;
        for (int index = first; index < last; index++)
        {
            var item = items[index];
            string caption = label(item);
            string? reason = lockedReason?.Invoke(item);
            if (reason != null)
            {
                int line = caption.IndexOf('\n');
                caption = "Locked: " + (line < 0 ? caption : caption.Substring(0,line)) + "\n" + reason;
            }
            GUI.enabled = enabled && reason == null;
            if (GUI.Button(new Rect(0, index * rowHeight, content.width, rowHeight - 4), caption)) selected = item;
        }
        GUI.enabled = enabled;
        GUI.EndScrollView();
        return selected;
    }
}
