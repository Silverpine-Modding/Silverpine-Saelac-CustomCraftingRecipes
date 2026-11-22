using Mono.Cecil;
using Mono.Cecil.Cil;
using SilverpineMods.CustomCraftingRecipes;

internal static class VillageTradesChecks
{
    // Read literal item registrations directly from the installed game's IL.
    // Initializing the real ItemLibrary in a console would require Unity Resources.
    internal static Dictionary<string, (int Price, string Sprite)> NativeGoods()
    {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(Item).Assembly.Location);
        var code = assembly.MainModule.GetType("ItemLibrary").Methods.Single(m => m.Name == ".cctor").Body.Instructions;
        Dictionary<string, (int, string)> prices = new(StringComparer.Ordinal);
        for (int i = 0; i + 3 < code.Count; i++)
        {
            if (code[i].OpCode != OpCodes.Ldstr || code[i + 1].OpCode != OpCodes.Ldstr ||
                code[i + 2].OpCode != OpCodes.Ldstr || !((string)code[i + 1].Operand).StartsWith("sprite_item_")) continue;
            var n = code[i + 3];
            int price = n.OpCode.Code switch
            {
                >= Code.Ldc_I4_0 and <= Code.Ldc_I4_8 => (int)n.OpCode.Code - (int)Code.Ldc_I4_0,
                Code.Ldc_I4_S or Code.Ldc_I4 => Convert.ToInt32(n.Operand),
                _ => -1
            };
            if (price >= 0) prices.Add((string)code[i].Operand, (price, (string)code[i + 1].Operand));
        }
        return prices;
    }

    internal static void Run(Action<bool,string> check)
    {
        var native = NativeGoods();
        var root = Directory.CreateTempSubdirectory("village-trades-check-");
        try
        {
            RecipeRegistry.Initialize(root.FullName);
            var folder = root.CreateSubdirectory("silverpine.village-trades.recipe.pack");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "VillageTrades", "recipes.json"), Path.Combine(folder.FullName, "recipes.json"));
            RecipeRegistry.Reload();
            var doc = RecipeRegistry.LoadEditablePacks().Single(d => d.Pack.packId == "silverpine.village-trades");
            var definitions = doc.Pack.recipes.ToDictionary(r => r.result, StringComparer.Ordinal);
            check(definitions.Count >= 30 && RecipeRegistry.Count == definitions.Count + 1, "Village Trades loads every distinct output alongside the Starter recipe");
            double Cheapest(string name, HashSet<string> path)
            {
                if (!path.Add(name)) throw new Exception("Production cycle at " + name);
                double cost = native[name].Price;
                if (definitions.TryGetValue(name, out var source))
                    cost = Math.Min(cost, source.ingredients.Sum(i => i.amount * Cheapest(i.item, path)) / source.resultAmount);
                path.Remove(name);
                return cost;
            }
            foreach (var r in doc.Pack.recipes)
            {
                var (price, sprite) = native[r.result]; // Unknown names fail, including incorrect capitalization.
                int cost = r.ingredients.Sum(i => native[i.item].Price * i.amount);
                int gross = price * r.resultAmount;
                double chainCost = r.ingredients.Sum(i => i.amount * Cheapest(i.item, new(StringComparer.Ordinal)));
                var station = Enum.Parse<CraftingStation>(r.stations.Single());
                check(RecipeRegistry.TryFindRecipe(station, RecipeRules.Counts(r), out var found) &&
                    found!.Id == doc.Pack.packId + ":" + r.id && found.ResultAmount == r.resultAmount &&
                    gross > cost && gross <= cost * 1.55 && Math.Round(price * 0.5, MidpointRounding.ToEven) * r.resultAmount <= chainCost &&
                    !sprite.Contains("herb", StringComparison.OrdinalIgnoreCase) && !sprite.Contains("ore", StringComparison.OrdinalIgnoreCase) &&
                    r.result != "Turnip" && r.workMinutes >= 10, $"Native value and station verified: {r.resultAmount} × {r.result} ({cost} to {gross}, {r.workMinutes} min)");
            }
            check(RecipeRegistry.BrowseBenchRecipes("village-trades").Count == definitions.Values.Count(r => r.stations.Contains("CustomCrafting")) &&
                RecipeRegistry.BrowseBenchRecipes("gold ore").Any(r => r.ResultName == "Gold Ring") &&
                !RecipeRegistry.BrowseBenchRecipes("Loaf of Bread").Any(), "workbench book searches enabled recipes and excludes other stations");
            doc.Pack.enabled = false; RecipeRegistry.SavePack(doc);
            check(!RecipeRegistry.BrowseBenchRecipes("village-trades").Any() && RecipeRegistry.Count == 1, "disabling Village Trades leaves only the Starter recipe");
            var starter = RecipeRegistry.LoadEditablePacks().Single(d => d.Pack.packId != doc.Pack.packId);
            check(starter.Pack.schemaVersion == 1 && starter.Pack.recipes.Single().resultAmount == 1, "v1 pack still yields one item");
            var recipe = starter.Pack.recipes.Single();
            foreach (int n in new[] {0, -1, RecipeRules.MaxYield + 1, int.MaxValue}) {
                recipe.resultAmount = n; check(RecipeRules.Validate(recipe).Count != 0, "invalid result amount rejected: " + n);
            }
            recipe.resultAmount = 2; RecipeRegistry.SavePack(starter);
            check(RecipeRegistry.ReadPack(starter.Path).schemaVersion == 2 && RecipeRegistry.ReadPack(starter.Path).recipes[0].resultAmount == 2,
                "editing a single-result pack upgrades its schema only when needed");
        }
        finally
        {
            if (root.Parent?.FullName == Path.TrimEndingDirectorySeparator(Path.GetTempPath()) && root.Name.StartsWith("village-trades-check-"))
                root.Delete(true);
        }

        var bread = new Item { name = "Loaf of Bread", bulk = 2 };
        var bag = new Inventory { maxItems = 2, maxBulk = 4 };
        var flour = new Item { name = "Flour", bulk = 4 }; bag.TryAddItem(flour);
        var inputs = new[] { flour };
        var compiled = new CompiledRecipe("loaves", new() { ["Flour"] = 1 }, new() { CraftingStation.Cooking }, bread.name, 70, "Bake.", 2);
        var preview = RecipeResultFactory.Build(compiled, new[] { bread }, _ => new Item {name = bread.name, bulk = 2}, bag, e => throw e);
        var units = BatchResult.Units(preview);
        check(units.Count == 2 && units.Distinct().Count() == 2 && units.All(i => !bag.ContainsItem(i)) && preview.resultText.Contains("2 ×"),
            "batch preview lists the yield without granting or sharing any units");
        var effectItem = new Item("Coins", "", "", 0, ItemCategory.Miscellaneous, ItemSound.None, 0f, new ItemComponent_Gold(1, 1));
        var unsafeBatch = RecipeResultFactory.Build(compiled, new[] { bread }, _ => effectItem, bag, _ => {});
        check(unsafeBatch.resultItem == null && unsafeBatch.resultText.Contains("inventory callbacks"), "pickup-effect outputs cannot be batched");
        void Remove(Item item) { bag.items.Remove(item); item.OnRemovedFromInventory(); }
        bag.maxItems = 1;
        check(!CraftTransaction.TryComplete(bag, inputs, units, out _, Remove) && bag.ContainsItem(flour), "batch rejects insufficient item slots before consuming inputs");
        bag.maxItems = 2; bag.maxBulk = 3;
        check(!CraftTransaction.TryComplete(bag, inputs, units, out _, Remove) && bag.ContainsItem(flour), "batch checks the combined output bulk");
        bag.maxBulk = 4;
        check(!CraftTransaction.TryComplete(bag, inputs, units, out _, Remove, i => i == units[0] && bag.TryAddItem(i)) &&
            bag.items.SequenceEqual(inputs), "partial batch insertion rolls back exact output and restores original flour");
        check(CraftTransaction.TryComplete(bag, inputs, units, out _, Remove) && bag.items.SequenceEqual(units) &&
            units.All(i => i.inventory == bag), "one set of inputs delivers the full batch after freeing slots and bulk");
        check(!CraftTransaction.TryComplete(bag, inputs, units, out _, Remove) && bag.items.Count == 2, "a completed batch cannot deliver twice");
        var ores = new Inventory();
        Item Ore(Quality grade) => new("Gold Ore", "", "", 10, ItemCategory.Ore, ItemSound.Rock, 1f, new ItemComponent_Ore {Quality = grade});
        var premium = Ore(Quality.Pristine); var ordinary = Ore(Quality.Normal);
        ores.TryAddItem(premium); ores.TryAddItem(ordinary);
        check(CraftTransaction.Select(ores, new Dictionary<string,int>{["Gold Ore"] = 1}).Single() == ordinary,
            "automatic workbench selection preserves higher-grade stock when ordinary ore is available");
    }
}
