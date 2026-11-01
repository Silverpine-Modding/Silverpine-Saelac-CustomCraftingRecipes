using System.Diagnostics;
using System.Reflection;
using BepInEx.Logging;
using Newtonsoft.Json;
using SilverpineMods.CustomCraftingRecipes;

static class AuditChecks
{
    static RecipeDefinition Recipe(string id = "test", int types = 2, int amount = 1) => new()
    {
        id = id, result = "Bag of Morning Dew Seeds", workMinutes = 10,
        ingredients = Enumerable.Range(0, types).Select(i => new IngredientDefinition { item = "Ingredient " + i, amount = amount }).ToList()
    };

    public static void Run(Action<bool, string> check)
    {
        typeof(Plugin).GetProperty("Log", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, new ManualLogSource("Regression"));
        Rules(check);
        InventoryChecks(check);
        CatalogChecks(check);
        ResultChecks(check);
        PackChecks(check);
        PatchContracts(check);
    }

    static void Rules(Action<bool, string> check)
    {
        check(RecipeRules.Validate(Recipe(types: 10, amount: 999)).Count == 0, "ten custom types with quantities up to 999 are valid");
        check(RecipeRules.Validate(Recipe(types: 11)).Count > 0, "eleven custom types are rejected");
        foreach (int amount in new[] { int.MinValue, -1, 0, 1000, int.MaxValue })
            check(RecipeRules.Validate(Recipe(amount: amount)).Count > 0, "invalid quantity rejected without overflow: " + amount);
        var r = Recipe(types: 1, amount: 500);
        r.ingredients.Add(new IngredientDefinition { item = " ingredient 0 ", amount = 500 });
        check(RecipeRules.Validate(r).Count > 0, "duplicate ingredient rows cannot exceed the combined quantity limit");
        r = Recipe(types: 1, amount: 2);
        r.ingredients.Add(new IngredientDefinition { item = " ingredient 0 ", amount = 1 });
        check(RecipeRules.Counts(r)["INGREDIENT 0"] == 3, "duplicate rows normalize by trimmed case-insensitive name");
        foreach (var station in new[] { CraftingStation.Cooking, CraftingStation.Alchemy })
        {
            r = Recipe(types: 1, amount: station.Capacity()); r.stations = new() { station.ToString() };
            check(RecipeRules.Validate(r).Count == 0, station + " permits its exact total-item capacity");
            r.ingredients[0].amount++;
            check(RecipeRules.Validate(r).Count > 0, station + " rejects excess total quantities");
        }
        r = Recipe(); r.stations = new() { "99" };
        check(RecipeRules.Validate(r).Count > 0, "undefined numeric stations are rejected");
        r.stations = new() { null! };
        check(RecipeRules.Validate(r).Count > 0, "null station entries are rejected safely");
        r.stations.Clear();
        check(RecipeRules.Validate(r).Count > 0, "an explicitly empty station list is invalid");
        r = Recipe(); r.ingredients.Add(null!);
        check(RecipeRules.Validate(r).Count > 0, "null ingredient rows do not crash validation");
        r = Recipe(); r.ingredients[0].item = "Superior|Iron Sword";
        check(RecipeRules.Validate(r).Count > 0, "unsupported quality-specific ingredient names are rejected");
        foreach (int time in new[] { 0, -1, 1441, int.MaxValue })
        { r = Recipe(); r.workMinutes = time; check(RecipeRules.Validate(r).Count > 0, "invalid work time rejected: " + time); }
        r = Recipe();
        var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        check(RecipeRegistry.TryCompile("pack", r, 0, signatures, out var compiled), "valid recipe compiles through the production loader");
        var shuffled = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["ingredient 1"] = 1, ["INGREDIENT 0"] = 1 };
        check(compiled!.Matches(shuffled, 2), "ingredient order and case do not affect matching");
        shuffled["ingredient 1"] = 2;
        check(!compiled.Matches(shuffled, 3), "wrong quantities prevent a match");
        shuffled["extra"] = 1;
        check(!compiled.Matches(shuffled, 4), "extra ingredients prevent a match");
        check(!RecipeRegistry.TryCompile("other", r, 0, signatures, out _), "duplicate station combinations are rejected across packs");
        check(RecipeRegistry.CreateCustomCraftingFields().Count == 10, "native CustomCrafting data also exposes ten fields");
        var fields = new List<ItemSelectionFieldInfo> { new("", item => item.name == "Fish") };
        CookingFieldsPatch.ExpandCookingFields(fields);
        CookingFieldsPatch.ExpandCookingFields(fields);
        check(fields.Count == 3 && fields.All(f => f.predicate(new Item { name = "Fish" })), "cooking expansion is idempotent and preserves native predicates");
        check(!RecipeRegistry.IsSafeForNativeAlchemy(new()) && !RecipeRegistry.IsSafeForNativeAlchemy(null), "short/null alchemy lists cannot reach native indexing");
        check(RecipeRegistry.IsSafeForNativeAlchemy(new() { null!, null!, null!, null! }), "empty native alchemy fields remain supported");
        check(!RecipeRegistry.IsSafeForNativeAlchemy(new() { new(), new(), new(), new() }), "non-alchemy custom items cannot crash native fallback");
        var a = new Dictionary<string, int> { ["A=1|B"] = 2 };
        var b = new Dictionary<string, int> { ["A"] = 1, ["B"] = 2 };
        check(RecipeRules.Signature(a) != RecipeRules.Signature(b), "ingredient signature separators cannot create ambiguous matches");
    }

    static void InventoryChecks(Action<bool, string> check)
    {
        var inventory = new Inventory { maxItems = 2, maxBulk = 5 };
        var first = new Item { name = "Plank", bulk = 2 };
        var second = new Item { name = "Plank", bulk = 2 };
        inventory.TryAddItem(first); inventory.TryAddItem(second);
        var result = new Item { name = "Result", bulk = 3 };
        var inputs = new List<Item> { first, second };
        check(!inventory.CanAddItem(result) && CraftTransaction.CanComplete(inventory, inputs, result, out _), "full inventory may craft when consuming ingredients frees space");
        check(!CraftTransaction.CanComplete(inventory, new[] { first, first }, result, out _), "same item instance cannot count as two ingredients");
        check(!CraftTransaction.CanComplete(inventory, new[] { new Item() }, result, out _), "stale ingredient instances are rejected");
        result.bulk = 6;
        check(!CraftTransaction.CanComplete(inventory, inputs, result, out _), "result exceeding post-consumption capacity is rejected");
        result.bulk = 3;
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["plank"] = 2 };
        check(CraftTransaction.Select(inventory, counts).SequenceEqual(inputs), "quantity selection captures the actual distinct inventory instances");
        counts["plank"] = 3;
        check(CraftTransaction.Select(inventory, counts).Count == 0, "insufficient quantities cannot create a partial craft");
        // Native RemoveItem requires Player.Instance. Use equivalent removal of
        // these unequippable fixtures while testing the real transaction logic.
        void Remove(Item item) { inventory.items.Remove(item); item.OnRemovedFromInventory(); }
        check(!CraftTransaction.TryComplete(inventory, inputs, result, out _, Remove, _ => false), "rejected result insertion reports failure");
        check(inventory.ContainsItem(first) && inventory.ContainsItem(second) && !inventory.ContainsItem(result), "failed insertion restores all original ingredients");
        int removed = 0;
        void ThrowOnRemove(Item item) { Remove(item); if (++removed == 1) throw new InvalidOperationException("test listener"); }
        check(!CraftTransaction.TryComplete(inventory, inputs, result, out _, ThrowOnRemove), "removal listener exceptions are contained");
        check(inventory.GetItemCount() == 2 && inventory.ContainsItem(first) && inventory.ContainsItem(second), "partial removal failure restores ingredients without duplication");
        check(CraftTransaction.TryComplete(inventory, inputs, result, out _, Remove), "successful craft delivers through the native Inventory.TryAddItem");
        check(inventory.GetItemCount() == 1 && inventory.ContainsItem(result) && result.inventory == inventory, "successful craft consumes exact quantities and assigns real ownership");
        check(!CraftTransaction.TryComplete(inventory, inputs, result, out _, Remove), "a repeated completion cannot duplicate its output");

        inventory = new Inventory(); first = new Item(); result = new Item(); inputs = new() { first };
        inventory.TryAddItem(first);
        bool AddThenThrow(Item item) { inventory.TryAddItem(item); throw new Exception("late inventory listener"); }
        check(CraftTransaction.TryComplete(inventory, inputs, result, out _, Remove, AddThenThrow), "post-delivery listener exception does not roll back a completed craft");
        check(inventory.ContainsItem(result) && !inventory.ContainsItem(first), "post-delivery exception cannot duplicate ingredients");
    }

    static void CatalogChecks(Action<bool, string> check)
    {
        var seed = new Item { name = "Bag of Morning Dew Seeds", spriteName = "custom:herbs:seeds" };
        var native = new Item { name = "Plank", spriteName = "native" };
        var list = new List<Item> { native, seed };
        ItemCatalog.RefreshSource(list);
        check(ItemCatalog.CustomCount == 1 && ReferenceEquals(ItemCatalog.Items[0], seed), "catalog includes and labels custom items ahead of native items");
        int revision = ItemCatalog.Revision;
        ItemCatalog.RefreshSource(list);
        check(ItemCatalog.Revision == revision, "unchanged catalog does not rebuild the sorted item list");
        list[1] = new Item { name = "New Seeds", spriteName = "custom:replacement" };
        ItemCatalog.RefreshSource(list);
        check(ItemCatalog.Revision > revision && ItemCatalog.Contains(" new seeds ", false) && !ItemCatalog.Contains(seed.name, false), "same-count item replacement invalidates the catalog");
        list[1].name = "Renamed Seeds";
        ItemCatalog.RefreshSource(list);
        check(ItemCatalog.Contains("renamed seeds", false), "in-place item renames refresh the catalog");
        check(!ItemCatalog.Contains("99|Plank", true), "invalid numeric result quality is rejected");
        check(ItemCatalog.Contains("Superior|Plank", true) && !ItemCatalog.Contains("Superior|Plank", false), "quality prefixes are parsed only for result names");
        var valid = Recipe(types: 1); valid.ingredients[0].item = "Plank"; valid.result = "Renamed Seeds";
        check(RecipeEditorUI.ValidateRecipe(valid).Count == 0, "editor validation accepts registered custom item outputs");
        valid.ingredients[0].amount = int.MaxValue;
        check(RecipeEditorUI.ValidateRecipe(valid).Count > 0, "editor shares runtime quantity validation");
    }

    static void PackChecks(Action<bool, string> check)
    {
        string fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "audit-fixtures-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(fixture);
        try
        {
            RecipeRegistry.Initialize(fixture);
            check(RecipeRegistry.PackCount == 1 && RecipeRegistry.Count == 1, "first run creates and loads the starter pack");
            var starter = RecipeRegistry.LoadEditablePacks().Single();
            starter.Pack.enabled = false; RecipeRegistry.SavePack(starter);
            check(RecipeRegistry.Count == 0 && File.Exists(starter.Path + ".bak"), "disabling a pack unloads recipes and keeps an atomic-save backup");
            var pack = new RecipePackDefinition { packId = "test.recipes", displayName = "Tests", recipes = new() { Recipe("valid") } };
            var doc = new RecipePackDocument("", pack);
            RecipeRegistry.SavePack(doc);
            var counts = RecipeRules.Counts(pack.recipes[0]);
            check(RecipeRegistry.TryFindRecipe(CraftingStation.CustomCrafting, counts, out var match) && match!.Id == "test.recipes:valid", "saved pack is available in the indexed registry");
            check(!RecipeRegistry.TryFindRecipe(CraftingStation.Cooking, counts, out _), "recipe index enforces station restrictions");
            pack.recipes.Add(Recipe("bad", amount: int.MaxValue));
            pack.recipes.Add(null!);
            pack.recipes.Add(Recipe("valid", types: 3));
            RecipeRegistry.SavePack(doc);
            check(RecipeRegistry.Count == 1, "malformed entries and duplicate recipe IDs do not stop valid recipes loading");
            pack.enabled = false; RecipeRegistry.SavePack(doc);
            check(!RecipeRegistry.TryFindRecipe(CraftingStation.CustomCrafting, counts, out _) && !RecipeRegistry.IsIngredientName(CraftingStation.CustomCrafting, "Ingredient 0"), "disabled packs clear both matches and allowed ingredients");
            pack.enabled = true;
            pack.recipes = Enumerable.Range(0, 2000).Select(i => { var r = Recipe("r" + i, types: 1); r.ingredients[0].item = "Item " + i; return r; }).ToList();
            RecipeRegistry.SavePack(doc);
            check(RecipeRegistry.Count == 2000, "registry loads 2,000 independent recipe combinations");
            counts = new(StringComparer.OrdinalIgnoreCase) { ["item 1999"] = 1 };
            RecipeRegistry.TryFindRecipe(CraftingStation.CustomCrafting, counts, out _);
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < 10000; i++)
                if (!RecipeRegistry.TryFindRecipe(CraftingStation.CustomCrafting, counts, out _)) throw new Exception("Indexed lookup failed");
            timer.Stop();
            Console.WriteLine($"BENCHMARK: 10,000 indexed lookups among 2,000 recipes: {timer.Elapsed.TotalMilliseconds:F1} ms (headless .NET, not Unity frame timing).");
            var linear = pack.recipes.Select(r => new CompiledRecipe(r.id, RecipeRules.Counts(r), new() { CraftingStation.CustomCrafting }, r.result, r.workMinutes, r.description)).ToList();
            timer.Restart();
            for (int i = 0; i < 10000; i++)
                if (linear.FirstOrDefault(r => r.Matches(counts, 1)) == null) throw new Exception("Linear baseline failed");
            timer.Stop();
            Console.WriteLine($"BENCHMARK: equivalent former linear scan: {timer.Elapsed.TotalMilliseconds:F1} ms.");
            check(true, "all 10,000 repeated indexed lookups return the correct recipe");
            string badFolder = Path.Combine(fixture, "bad.recipe.pack"); Directory.CreateDirectory(badFolder);
            File.WriteAllText(Path.Combine(badFolder, "recipes.json"), "{broken json");
            RecipeRegistry.Reload();
            check(RecipeRegistry.Count == 2000, "a malformed JSON pack does not block healthy packs");
            File.WriteAllText(Path.Combine(badFolder, "recipes.json"), "{\"schemaVersion\":99}");
            RecipeRegistry.Reload();
            check(RecipeRegistry.Count == 2000, "unsupported future schemas are isolated instead of misread");
            int revision = RecipeRegistry.Revision;
            string moved = fixture + "-moved";
            Directory.Move(fixture, moved);
            try
            {
                try { RecipeRegistry.Reload(); } catch (DirectoryNotFoundException) { }
                check(RecipeRegistry.Count == 2000 && RecipeRegistry.Revision == revision, "failed directory discovery leaves the previous registry intact");
            }
            finally { Directory.Move(moved, fixture); }
        }
        finally
        {
            // Delete only this test's unique directory underneath its build output.
            if (Path.GetDirectoryName(fixture) == Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)) Directory.Delete(fixture, true);
        }
    }

    static void PatchContracts(Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var native = typeof(CraftingUI);
        check(native.GetMethod("PollFields", flags) != null, "native crafting patch target exists in the installed game assembly");
        foreach (string field in new[] { "currentCraftingUIData", "resultTextMesh", "resultField", "craftButton" })
            check(native.GetField(field, flags) != null, "Harmony field injection target exists: " + field);
        check(typeof(Player).GetMethods().Any(m => m.Name == "StartLongInteraction" && m.GetParameters().Length == 4), "timed crafting callback API matches the installed game");
    }

    static void ResultChecks(Action<bool, string> check)
    {
        var template = new Item { name = "Bag of Morning Dew Seeds" };
        var catalog = new List<Item> { template };
        var inventory = new Inventory();
        var recipe = new CompiledRecipe("seeds", new(StringComparer.OrdinalIgnoreCase) { ["Morning Dew"] = 1 },
            new() { CraftingStation.CustomCrafting }, template.name, 10, "");
        int clones = 0, errors = 0;
        Item Create(string name) { clones++; return new Item { name = template.name }; }
        var result = RecipeResultFactory.Build(recipe, catalog, Create, inventory, _ => errors++);
        check(result.resultItem != null && result.resultText.Contains(template.name), "default recipe text safely formats a custom seed-bag output");
        check(clones == 1 && errors == 0 && template.inventory == null && inventory.GetItemCount() == 0,
            "result construction does not mutate the library template or player inventory");
        var second = RecipeResultFactory.Build(recipe, catalog, Create, inventory, _ => errors++);
        check(!ReferenceEquals(result.resultItem, second.resultItem), "separate crafts receive independent result instances");
        var missing = RecipeResultFactory.Build(recipe, Array.Empty<Item>(), Create, inventory, _ => errors++);
        check(missing.resultItem == null && missing.workMinutes == 0 && missing.resultText.Contains("not loaded"), "missing result returns a non-craftable explanatory preview");
        check(clones == 2 && errors == 1, "missing item cannot fall back to a Strange Object clone");
        var broken = RecipeResultFactory.Build(recipe, catalog, _ => throw new Exception("bad item component"), inventory, _ => errors++);
        check(broken.resultItem == null && broken.resultText.Contains("bad item component"), "item cloning failures become a non-craftable preview");
        var nullResult = RecipeResultFactory.Build(recipe, catalog, _ => null!, inventory, _ => errors++);
        check(nullResult.resultItem == null && errors == 3, "null factory outputs are contained");
        string normalized = "";
        RecipeResultFactory.CreatePreview("superior|" + template.name, catalog, value => { normalized = value; return new Item(); }, inventory);
        check(normalized.StartsWith("Superior|"), "quality prefixes normalize to the native parser's enum spelling");
        bool invalidQuality = false;
        try { RecipeResultFactory.CreatePreview("999|" + template.name, catalog, Create, inventory); }
        catch (InvalidDataException) { invalidQuality = true; }
        check(invalidQuality, "undefined result quality never silently becomes a normal item");
        recipe = new CompiledRecipe("seeds", new() { ["Morning Dew"] = 1 }, new() { CraftingStation.Cooking }, template.name, 20, "Pack-authored description.");
        result = RecipeResultFactory.Build(recipe, catalog, Create, inventory, _ => errors++);
        check(result.resultText == "Pack-authored description." && result.workMinutes == 20, "pack-authored description and work time remain intact");
    }
}
