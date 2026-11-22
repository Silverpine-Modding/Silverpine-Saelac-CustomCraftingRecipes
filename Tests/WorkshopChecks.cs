using Newtonsoft.Json.Linq;
using SilverpineMods.CustomCraftingRecipes;
using System.Buffers.Binary;
using System.Reflection;

internal static class WorkshopChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var root = Directory.CreateTempSubdirectory("village-workshops-check-");
        try
        {
            string samples = AppContext.BaseDirectory;
            RecipeRegistry.Initialize(root.FullName);
            foreach (string name in new[] {"WorkshopRecipes", "VillageTrades"})
                File.Copy(Path.Combine(samples, name, "recipes.json"), Path.Combine(root.CreateSubdirectory(name + ".recipe.pack").FullName, "recipes.json"));
            RecipeRegistry.Reload();
            var docs = RecipeRegistry.LoadEditablePacks();
            var book = docs.Single(d => d.Pack.packId == WorkshopFurniture.Pack);
            var other = docs.Single(d => d.Pack.packId == "silverpine.village-trades");
            var catalog = VillageTradesChecks.NativeGoods().ToDictionary(k => k.Key, k => k.Value.Price, StringComparer.Ordinal);
            foreach (var f in JObject.Parse(File.ReadAllText(Path.Combine(samples, "WorkshopFurniturePrices.json"))))
                catalog.Add(f.Key, (int)f.Value!);
            string items = Path.Combine(samples,"WorkshopItems");
            var pack = JObject.Parse(File.ReadAllText(Path.Combine(items,"items.json")));
            check((string?)pack["packId"] == book.Pack.packId && (bool)pack["enabled"]!, "matched furniture and recipe packs");
            string loader = Path.Combine(samples,"WorkshopIntegration","CustomItemLoader.dll");
            if (File.Exists(loader))
            {
                var asm = Assembly.LoadFrom(loader);
                const string ns = "SilverpineMods.CustomItemLoader.";
                var adapter = asm.GetType(ns+"CustomItemWorkbenchAdapter",true)!;
                var builder = asm.GetType(ns+"AssetFiles",true)!.GetMethod("FurnitureName",BindingFlags.Static | BindingFlags.NonPublic)!;
                var prefab = (Func<string,string,string>)builder.CreateDelegate(typeof(Func<string,string,string>));
                var names = WorkshopFurniture.Stations.Select(s => prefab(WorkshopFurniture.Pack,s.Id)).ToArray();
                check(names.Distinct().Count() == 3 && !names.Contains(prefab("Skeleton.Pack","mod_skelebones")) &&
                    adapter.GetMethod("OnInteract",new[]{typeof(UnityEngine.GameObject)}) != null &&
                    adapter.GetProperty("InteractionName")?.PropertyType == typeof(string),"installed loader: distinct prefab keys, interaction and label hooks");
                var packType = asm.GetType(ns+"ItemPackDefinition",true)!;
                var parsed = pack.ToObject(packType)!;
                packType.GetMethod("Validate",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(parsed,new object[]{items});
                foreach (object def in (System.Collections.IEnumerable)packType.GetField("items")!.GetValue(parsed)!)
                    def.GetType().GetMethod("Validate",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(def,null);
                check(true, "installed Custom Item Loader accepts all three shipping furniture definitions");
            }
            foreach (var (id,station) in WorkshopFurniture.Stations)
            {
                var item = pack["items"]!.Single(i => (string?)i["id"] == id);
                string label = (string)item["name"]!;
                catalog.Add(label,(int)item["value"]!);
                var png = File.ReadAllBytes(Path.Combine(items,(string)item["image"]!));
                int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
                check(png.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) && png[25] == 6 &&
                    (width,height) == (station == CraftingStation.Joinery ? (32,24) : station == CraftingStation.Tailoring ? (32,32) : (44,42)) &&
                    station.Label() == label && (string?)item["workbench"] == "CustomCrafting" && (string?)item["placement"] == "GeneratedPrefab" &&
                    (string?)item["category"] == "Furniture" && (float)item["placementScale"]! == 1 && !(bool)item["furniture"]!["turnable"]!,
                    "Physical station has native-sized RGBA furniture art and a matching menu: " + label);
                check(station.Fits(10,99) && !station.Fits(11,11) && !CraftingStation.Cooking.Fits(2,4), "specialist vs native slot limits: " + label);
                check(RecipeRegistry.BrowseBenchRecipes("",station).Count == book.Pack.recipes.Count(r => r.stations.Contains(station.ToString())),
                    "station book is isolated: " + label);
            }
            var sources = docs.SelectMany(d => d.Pack.recipes).ToLookup(r => r.result);
            double Cheapest(string item, HashSet<string> visited)
            {
                if (!visited.Add(item)) throw new Exception("Ingredient cycle: " + item);
                double value = catalog[item];
                foreach (var r in sources[item])
                    value = Math.Min(value,r.ingredients.Sum(i => i.amount * Cheapest(i.item,visited))/r.resultAmount);
                visited.Remove(item);
                return value;
            }
            foreach (var r in book.Pack.recipes)
            {
                int inputs = r.ingredients.Sum(i => catalog[i.item]*i.amount), output = catalog[r.result]*r.resultAmount;
                var minCost = r.ingredients.Sum(i => i.amount * Cheapest(i.item,new(StringComparer.Ordinal)));
                check(output > inputs && output <= inputs*1.55 && Math.Round(catalog[r.result]*0.5)*r.resultAmount <= minCost &&
                    RecipeRules.Validate(r).Count == 0 && r.workMinutes >= 10, $"Workshop economy: {r.result}, {inputs} → {output} ({minCost:F1} minimum cost)");
                foreach (var s in r.stations.Select(Enum.Parse<CraftingStation>))
                    check(RecipeRegistry.TryFindRecipe(s,RecipeRules.Counts(r),out var found) && found!.Id == book.Pack.packId + ":" + r.id,
                        $"Recipe resolves alongside both example packs: {r.id} at {s}");
            }
            Item Furniture(string packId,string id) => new() { spriteName = "custom:" + packId + ":" + id, itemCategory = ItemCategory.Furniture };
            check(!WorkshopFurniture.IsStarter(Furniture("lookalike.pack","joiners-bench")) && WorkshopFurniture.IsStarter(Furniture(WorkshopFurniture.Pack,"joiners-bench")) &&
                !WorkshopFurniture.IsStarter(Furniture(WorkshopFurniture.Pack,"bake-oven")), "only this pack's starter can be stocked by Aldric");
            var newNative = book.Pack.recipes.Select(r => r.result).Except(other.Pack.recipes.Select(r => r.result)).Except(WorkshopFurniture.Stations.Select(s => s.Station.Label()));
            check(newNative.Count() >= 18, "at least eighteen additional native craftable outputs");
            book.Pack.enabled = false; RecipeRegistry.SavePack(book);
            check(!RecipeRegistry.BrowseBenchRecipes("",CraftingStation.Joinery).Any() && RecipeRegistry.BrowseBenchRecipes("village-trades").Count > 0,
                "disabling Workshops clears specialists but preserves the existing bench pack");
        }
        finally { root.Delete(true); }
    }
}
