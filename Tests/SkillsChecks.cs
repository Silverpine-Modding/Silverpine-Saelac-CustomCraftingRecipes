using SilverpineMods.CustomCraftingRecipes;

internal static class SkillsChecks
{
    internal static void Run(Action<bool,string> check)
    {
        Item Make() => new("Test tool","skill-test","",20,ItemCategory.Material,ItemSound.Metal,1,
            new ItemComponent_Ore { Quality=Quality.Pristine }, new ItemComponent_Durability(100));
        var inventory = new Inventory {maxItems=5,maxBulk=20};
        var input = new Item {name="Test Ingot",bulk=1};
        inventory.TryAddItem(input);
        var inputs = new[]{input};
        var order = new List<string>();
        CraftReceipt? receipt = null;
        int calls=0;
        using var a = CraftingApi.Register("example.skills", ctx =>
        {
            calls++; order.Add("a");
            if (ctx.Station != CraftingStation.Blacksmithing || ctx.RecipeId != "test:tool" || ctx.BaseQuantity != 1 ||
                !ctx.SupportsQuality || !ctx.SupportsDurability || ctx.Ingredients["test ingot"]!=1) throw new Exception("Bad context");
            return new CraftBonus(extraItems:1,qualitySteps:2,durabilityPercent:25,valuePercent:20);
        }, r => { order.Add("xp"); receipt=r; }, priority:-5);
        using var broken = CraftingApi.Register("bad.mod",_ => throw new Exception("isolated"),_ => throw new Exception("isolated"));
        using var b = CraftingApi.Register("other.skills",_ => { order.Add("b"); return new CraftBonus(qualitySteps:1,durabilityPercent:10); });
        var template=Make();
        var recipe = new CompiledRecipe("test:tool",new(){["Test Ingot"]=1},new(){CraftingStation.Blacksmithing},template.name,30,"Working.");
        var result=RecipeResultFactory.Build(recipe,new[]{template},_=>Make(),inventory,e=>throw e);
        RecipeResultFactory.Build(recipe,new[]{template},_=>Make(),inventory,e=>throw e);
        check(calls==0 && receipt==null,"browsing and repeated previews never call skills");
        bool Deliver(ResultData preview, out string error, Func<Item,bool>? add=null) =>
            CraftingApi.TryDeliver(null!,CraftingStation.Blacksmithing,preview,inventory,inputs,out _,out error,
                remove:i=>{inventory.items.Remove(i);i.OnRemovedFromInventory();},add:add);
        inventory.maxItems=1;
        check(!Deliver(result,out var error) && error.Contains("hold") && inventory.ContainsItem(input) && receipt==null,
            "full bonus batch fails atomically, without XP or consuming ingredients");
        inventory.maxItems=4; int inserted=0;
        check(!Deliver(result,out _,i=>++inserted==1 && inventory.TryAddItem(i)) && inventory.items.SequenceEqual(inputs) && receipt==null,
            "partial bonus delivery rolls back all items and never awards XP");
        order.Clear(); check(Deliver(result,out _) && order.SequenceEqual(new[]{"a","b","xp"}) && receipt?.ExtraItems==1 &&
            receipt.Outputs.Distinct().Count()==2 && receipt.Outputs.All(inventory.ContainsItem),
            "ordered modifiers compose, broken integrations are isolated, and XP follows committed inventory");
        var received=inventory.items.First();
        check(received.GetItemComponent<ItemComponent_Ore>().Quality==Quality.Legendary &&
            received.GetItemComponent<ItemComponent_Durability>().maxDurability==135 && received.GetItemComponent<ItemComponent_Durability>().Durability==135 &&
            received.value==24 && !ReferenceEquals(inventory.items[0],inventory.items[1]) &&
            template.value==20 && result.resultItem.GetItemComponent<ItemComponent_Durability>().maxDurability==100,
            "per-instance stat increases stack; quality caps at Legendary; previews and templates stay untouched");
        var persisted=received.DeepClone(); // round-trip through Silverpine's actual item serializer
        check(persisted.GetItemComponent<ItemComponent_Ore>().Quality==Quality.Legendary &&
            persisted.GetItemComponent<ItemComponent_Durability>().Durability==135 && persisted.value==24,
            "skill-crafted quality, durability and value survive native item serialization");
        check(!Deliver(result,out _) && calls==3,"already-spent inputs cannot roll modifiers or deliver again");
        bool rejected=false; try { using var duplicate=CraftingApi.Register("EXAMPLE.SKILLS",_=>default); } catch(InvalidOperationException) { rejected=true; }
        check(rejected,"duplicate registration ids rejected");
        a.Dispose(); broken.Dispose(); b.Dispose();
        int nativeReceipts=0;
        using var native=CraftingApi.Register("native",_=>new CraftBonus(extraItems:12),r=> { if (r.Craft.RecipeId==null && r.ExtraItems==0) nativeReceipts++; });
        var single = new ObservedItem {name="Callback item",bulk=1};
        inventory.TryAddItem(input);
        check(Deliver(new ResultData("Native",single,5),out _) && nativeReceipts==1 && single.Added==1 && calls==3,
            "native crafts have no pack id; unsafe pickup effects are never duplicated; disposed hooks stay removed");
        bool invalid=false; try { _=new CraftBonus(extraItems:13); } catch(ArgumentOutOfRangeException) { invalid=true; }
        check(invalid,"invalid bonus magnitudes rejected");
    }
}
