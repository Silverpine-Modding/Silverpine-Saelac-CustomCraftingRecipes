using System.Reflection;
using System.Buffers.Binary;
using Newtonsoft.Json.Linq;
using SilverpineMods.CustomCraftingRecipes;

internal static class FoundryChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var baseDir=AppContext.BaseDirectory;
        var root=Directory.CreateTempSubdirectory("silverpine-foundry-");
        try
        {
            RecipeRegistry.Initialize(root.FullName);
            foreach(var pack in new[]{"VillageTrades","WorkshopRecipes","FoundryRecipes"})
                File.Copy(Path.Combine(baseDir,pack,"recipes.json"),Path.Combine(root.CreateSubdirectory(pack+".recipe.pack").FullName,"recipes.json"));
            RecipeRegistry.Reload();
            var docs=RecipeRegistry.LoadEditablePacks();
            var foundry=docs.Single(d=>d.Pack.packId==WorkshopFurniture.FoundryPack).Pack;
            var prices=VillageTradesChecks.NativeGoods().ToDictionary(p=>p.Key,p=>p.Value.Price);
            foreach(var f in JObject.Parse(File.ReadAllText(Path.Combine(baseDir,"WorkshopFurniturePrices.json"))))
                prices.Add(f.Key,(int)f.Value!);
            foreach(var dir in new[]{"WorkshopItems","FoundryItems"})
            {
                var items=JObject.Parse(File.ReadAllText(Path.Combine(baseDir,dir,"items.json")));
                foreach(var item in items["items"]!) prices.Add((string)item["name"]!,(int)item["value"]!);
                if(dir!="FoundryItems") continue;
                check((string?)items["packId"]==foundry.packId && items["items"]!.Count()==6,"furnace, charcoal and four refined ingots are pack-owned");
                foreach(var item in items["items"]!)
                {
                    string name=(string)item["name"]!;
                    var png=File.ReadAllBytes(Path.Combine(baseDir,dir,(string)item["image"]!));
                    var size=(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)),BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
                    check(png.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) && png[25]==6 &&
                        size==(name=="Smelting Furnace"?(40,44):name=="Charcoal"?(24,24):(28,22)),"shipped materials have native-sized RGBA sprites: "+name);
                    if(name=="Smelting Furnace") check((string?)item["id"]=="smelting-furnace" && (string?)item["workbench"]=="CustomCrafting" &&
                        (string?)item["placement"]=="GeneratedPrefab" && CraftingStation.Smelting.Label()==name,"furnace uses the dedicated CIL interaction route");
                }
                var loader=Path.Combine(baseDir,"WorkshopIntegration","CustomItemLoader.dll");
                if(File.Exists(loader))
                {
                    var asm=Assembly.LoadFrom(loader);
                    var type=asm.GetType("SilverpineMods.CustomItemLoader.ItemPackDefinition",true)!;
                    var data=items.ToObject(type)!;
                    type.GetMethod("Validate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(data,new object[]{dir});
                    foreach(object def in (System.Collections.IEnumerable)type.GetField("items")!.GetValue(data)!)
                        def.GetType().GetMethod("Validate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(def,null);
                    check(true,"installed item loader accepts all six Foundry definitions");
                }
            }
            var sources=docs.SelectMany(d=>d.Pack.recipes).ToLookup(r=>r.result);
            double Cheapest(string item, HashSet<string> path)
            {
                if(!path.Add(item)) throw new Exception("Ingredient cycle: "+item);
                double cost=prices[item];
                foreach(var r in sources[item]) cost=Math.Min(cost,r.ingredients.Sum(i=>i.amount*Cheapest(i.item,path))/r.resultAmount);
                path.Remove(item); return cost;
            }
            foreach(var r in foundry.recipes)
            {
                int inputs=r.ingredients.Sum(i=>i.amount*prices[i.item]);
                int gross=r.resultAmount*prices[r.result];
                check(RecipeRules.Validate(r).Count==0 && gross>inputs && gross<=inputs*1.55 &&
                    Math.Round(prices[r.result]*0.5)*r.resultAmount<=r.ingredients.Sum(i=>i.amount*Cheapest(i.item,new())),
                    $"Foundry economy, including intermediate costs: {r.id} ({inputs} → {gross})");
                foreach(var s in r.stations.Select(Enum.Parse<CraftingStation>))
                    check(RecipeRegistry.TryFindRecipe(s,RecipeRules.Counts(r),out var compiled) && compiled!.Id==foundry.packId+":"+r.id,
                        $"Loaded alongside existing packs: {r.id} at {s}");
                if(r.stations.SequenceEqual(new[]{"Blacksmithing"}))
                    check(r.ingredients.Any(i=>i.item.EndsWith(" Ingot")),"finished metal goods use refined stock: "+r.id);
                if(r.result.EndsWith(" Ingot"))
                    check(r.stations.SequenceEqual(new[]{"Smelting"}) && r.ingredients.Any(i=>i.item=="Charcoal") &&
                        r.ingredients.Any(i=>i.item==r.result.Replace(" Ingot"," Ore")),"matched ore and fuel for "+r.result);
            }
            check(MetalworkingBenchPatch.IsMetalworkingBench("prefab_furniture_workbench") &&
                !MetalworkingBenchPatch.IsMetalworkingBench("prefab_furniture_stove") &&
                typeof(Player).GetMethod("InteractWith",BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(UnityEngine.GameObject)},null)!=null,
                "native bench is targeted by stable prefab id at the installed game's radial interaction entry point");
            // Use the very same document/save path as RecipeEditorUI. No reference to
            // the bundled Foundry IDs is required for a third-party pack or item.
            var customItem = new Item{name="Meteor Bloom",spriteName="custom:test:bloom"};
            var customIngot = new Item{name="Meteor Billet",spriteName="custom:test:billet"};
            ItemCatalog.RefreshSource(new[]{customItem,customIngot},force:true);
            check(ItemCatalog.Contains("Meteor Bloom",false) && ItemCatalog.Contains("Meteor Billet",true),"editor catalog resolves third-party ingredients and outputs");
            RecipeDefinition Custom(string id,CraftingStation s,string input,string output) => new() {
                id=id,stations=new(){s.ToString()},ingredients=new(){new IngredientDefinition{item=input,amount=5}},
                result=output,resultAmount=2,description="Mod recipe.",workMinutes=35};
            var custom = new RecipePackDefinition {packId="test.stars",displayName="Stars",recipes=new(){
                Custom("refine",CraftingStation.Smelting,"Meteor Bloom","Meteor Billet"),
                Custom("shape",CraftingStation.Blacksmithing,"Meteor Billet","Meteor Clasp")}};
            var doc=new RecipePackDocument("",custom); RecipeRegistry.SavePack(doc);
            foreach(var r in custom.recipes)
            {
                var s=Enum.Parse<CraftingStation>(r.stations.Single());
                check(RecipeRegistry.IsIngredientName(s,r.ingredients[0].item) &&
                    RecipeRegistry.BrowseBenchRecipes("test.stars",s).Single().ResultName==r.result &&
                    RecipeRegistry.TryFindRecipe(s,RecipeRules.Counts(r),out var recipe) && recipe!.ResultAmount==2 &&
                    !RecipeRegistry.TryFindRecipe(CraftingStation.Cooking,RecipeRules.Counts(r),out _),
                    "editor-saved custom quantity recipe matches and browses only at "+s);
            }
            doc.Pack.enabled=false; RecipeRegistry.SavePack(doc);
            check(!RecipeRegistry.IsIngredientName(CraftingStation.Smelting,"Meteor Bloom") &&
                !RecipeRegistry.BrowseBenchRecipes("test.stars",CraftingStation.Blacksmithing).Any() &&
                RecipeRegistry.HasStation(CraftingStation.Blacksmithing),"disabling a third-party pack leaves other smithing recipes intact");
        }
        finally {root.Delete(true);}
    }
}
