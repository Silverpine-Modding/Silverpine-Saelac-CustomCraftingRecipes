# CustomCraftingRecipes audit — 2026-10-31

Scope: all plugin source, recipe pack storage and compilation, Recipe Editor,
CustomCrafting overlay, Cooking/Alchemy integration, inventory delivery, and
the prior 1.2.2 seed-bag preview fix. Audited release: 1.3.0.

## Findings and repairs

| Area | Finding | Repair |
| --- | --- | --- |
| Item delivery | Output insertion was unchecked after consuming ingredients; inventory changes during the timed action could lose materials or grant a result for stale selections. | Capture exact item instances; revalidate before completing; account for consumed bulk/slots; restore ingredients on failed delivery. Do not restore ingredients after an output was already delivered. |
| Result previews | Missing result definitions returned a null result that the overlay then dereferenced. Invalid qualities and clone failures were also unsafe. | Central result factory returns an explanatory non-craftable preview. Quality spelling is normalized for the native parser. The seed-bag preview inventory fix is retained. |
| Validation | Runtime silently repaired some invalid values while the editor used different rules. Quantity arithmetic could overflow; undefined numeric enums were accepted. | Shared validation enforces positive quantities up to 999 per type, 10 distinct CustomCrafting types, Cooking 3 total items, Alchemy 4 total items, defined stations, and work times 1–1440. All selected stations must fit. |
| Pack identity | Duplicate recipe IDs were not rejected by the runtime. Delimiter-based ingredient signatures could be ambiguous. | Reject later enabled duplicate IDs; use length-delimited, order-independent signatures. Detect editor conflicts across enabled packs. |
| Slot metadata | The legacy field factory and editor counter still contained five-slot values. | Both derive their capacity from the ten-slot station definition. |
| Save robustness | Copying a temporary file over a live pack was not an atomic replacement. | Atomic replace/move with a previous-version backup. Invalid JSON and future schemas are isolated from healthy packs. Failed directory discovery preserves the current registry. |
| Editor drafts | Refreshing, closing, or changing packs could discard edits; an unsaved new pack was not listed. | Save/discard/cancel prompt, retain new drafts in the pack list, preserve the selected recipe after save, and coordinate Escape with supported ModTools sessions. |
| UI lifetime | Persistent gameplay overlay did not release input on disable/destroy/scene changes and could retain the old player/inventory. | Track the input-block owner; unsubscribe and close on teardown, player/inventory replacement, and scene changes. |
| Shared GUI state | GUI globals were changed before entering the restoring scope. | Enter the GUI scope first, then change globals. |
| Native station flow | Native Alchemy fallback assumed four slots; native completion inherited unchecked delivery. | Guard malformed inputs, preserve native result rules, and use checked completion for the stations extended by this plugin. Native button sounds are retained. |
| Performance | Result items were deep-cloned during repeated GUI events; inventory copies and item grouping/sorting were repeated for each row/draw. | Cache previews and ingredient counts until relevant state changes; refresh catalog only on changes; draw only visible item-browser rows. |
| Recipe lookup | Every match scanned all recipes. | Index by station and canonical ingredient signature. |

## Verification

- Release build: zero warnings and errors.
- 97 automated regression checks passed using the compiled plugin and the
  installed Silverpine Item/Inventory assemblies.
- Coverage includes quantities, slot capacities, case/order matching, null
  entries, integer extremes, duplicate recipes, pack enable/disable, atomic
  saving and backups, malformed JSON, schema versions, missing outputs,
  output-clone failures, independent output instances, native preview formatting,
  inventory rollback, full-inventory crafting, stale selections, custom catalog
  changes, and installed-game patch field/method contracts.
- Tests use real native inventory insertion and formatting. Ingredient removal
  is injected for transaction tests because native removal requires a live
  Player object; Unity rendering and live Harmony execution are not simulated.
- Existing local Starter recipe was inspected and remains compatible. No user
  recipe packs or save files were changed by the audit tests; pack tests run in
  isolated temporary fixture directories under the test build output.

Commands:

```powershell
dotnet build Tests/RegressionTests.csproj -c Release --no-restore
dotnet run --project Tests/RegressionTests.csproj -c Release --no-build --no-restore
git diff --check
```

## Performance measurement

One measured run of 10,000 lookups for the last entry in a 2,000-recipe registry:

| Implementation | Time |
| --- | ---: |
| Indexed matching | 13.6 ms |
| Equivalent former linear scan | 1,113.6 ms |

This is a headless .NET benchmark of lookup work, not a Unity frame-rate claim.
Timings vary by machine and runtime. Preview caching and visible-row rendering
were verified by code inspection; live Unity allocation/frame profiling remains
outside this automated run.

## Remaining in-game smoke test

The game was not launched during this audit. These integration checks remain:

1. Open Recipe Editor; browse native/custom items; save, toggle packs, and test
   save/discard/cancel plus Escape within a picker.
2. Open Mr Bones/another CustomCrafting station; verify ten rows, quantities,
   scrolling, wrong-quantity rejection, a three-item recipe, and seed-bag output.
3. Cook one native item with two fields empty, then a three-item custom recipe;
   verify fire requirements. Test one native and one custom Alchemy recipe.
4. Complete a craft when ingredient consumption frees space; interrupt another
   craft; verify materials/output and workbench sound cleanup.
5. Close/reopen the overlay and transition scenes or load a save while it is
   open; verify player controls recover and the next screen uses the new inventory.

The automated results do not establish compatibility with every third-party
Harmony patch or arbitrary item callback. The new native crafting hook is
limited to the three supported Cooking/Alchemy data types; Repair and unrelated
crafting UIs retain their original path.
