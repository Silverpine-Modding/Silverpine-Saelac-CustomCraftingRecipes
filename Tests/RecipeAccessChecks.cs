using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using SilverpineMods.CustomCraftingRecipes;

internal static class RecipeAccessChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var root = Directory.CreateTempSubdirectory("silverpine-unlocks-");
        try
        {
            RecipeRegistry.Initialize(root.FullName);
            var owner = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            var guest = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            var station = CraftingStation.Blacksmithing;
            var r = new RecipeDefinition { id="tool",result="Superior|Pickaxe",resultAmount=2,description="Tool",stations=new(){station.ToString()},
                ingredients=new(){new(){item="Ingot",amount=1}},skillRequirements=new(){new(){skill="test.smith",minLevel=5},new(){skill="test.mining",minLevel=2}} };
            var doc = new RecipePackDocument("",new(){packId="test.levels",recipes=new(){r}});
            RecipeRegistry.SavePack(doc);
            var saved = RecipeRegistry.ReadPack(doc.Path);
            check(saved.schemaVersion==3 && saved.recipes.Single().skillRequirements![0].minLevel==5, "editor save round-trips multiple level requirements and upgrades the pack to schema 3");
            var wrongVersion=Path.Combine(root.FullName,"wrong.json");
            saved.schemaVersion=2; File.WriteAllText(wrongVersion,JsonConvert.SerializeObject(saved));
            bool rejected=false; try { RecipeRegistry.ReadPack(wrongVersion); } catch (InvalidDataException) {rejected=true;}
            check(rejected,"handwritten older schema cannot silently ignore level requirements");
            var bad= new RecipeDefinition {id="bad",result="X",ingredients=new(){new(){item="Y"}},skillRequirements=new(){
                null!,new(){skill="a b",minLevel=-1},new(){skill="smith",minLevel=1},new(){skill=" SMITH ",minLevel=1}}};
            check(RecipeRules.Validate(bad).Count>=4,"malformed, duplicate and invalid-level skill requirements are rejected");
            var compiled=RecipeRegistry.BrowseBenchRecipes("test.levels",station).Single();
            var bag=new Inventory {maxItems=4,maxBulk=20};
            var ore=new Item {name="Ingot"}; bag.TryAddItem(ore);
            var inputs=new[]{ore};
            var template=new Item {name="Pickaxe",bulk=1};
            var result=RecipeResultFactory.Build(compiled,new[]{template},_=>new Item {name="Pickaxe",bulk=1},bag,e=>throw e);
            check(CraftingApi.LockReason(owner,station,compiled)?.Contains("provider not loaded")==true &&
                CraftingApi.LockReason(owner,station,result)==CraftingApi.LockReason(owner,station,compiled), "missing skill locks book and manual match identically");
            int level=4; using var smith=CraftingApi.RegisterSkill("TEST.smith","Blacksmithing",p=>ReferenceEquals(p,owner)?level:1);
            using var mining=CraftingApi.RegisterSkill("test.mining","Mining",_=>2);
            check(CraftingApi.LockReason(owner,station,compiled)=="Requires Blacksmithing level 5 (current: 4).","player sees required and current levels");
            check(!CraftExecution.CanStart(owner,station,result,bag,inputs,out var startError) && startError.Contains("level 5") &&
                bag.items.SequenceEqual(inputs),"manually selected ingredients cannot start locked work");
            var rev=CraftingApi.AccessRevision; level=5; CraftingApi.NotifyRecipeAccessChanged();
            check(rev!=CraftingApi.AccessRevision && CraftingApi.LockReason(owner,station,result)==null && CraftingApi.LockReason(guest,station,result)!=null,
                "crossing the exact threshold unlocks the same preview for only that player");
            check(CraftExecution.CanStart(owner,station,result,bag,inputs,out _),"unlocked manual selection can start with sufficient materials and capacity");
            int rewards=0,xp=0;
            using var hooks=CraftingApi.Register("test.access-reward",_=>{rewards++;return default;},_=>xp++);
            bool Deliver(out string error) => CraftingApi.TryDeliver(owner,station,result,bag,inputs,out _,out error,
                remove:i=>{bag.items.Remove(i);i.OnRemovedFromInventory();});
            level=4;
            check(!Deliver(out var reason) && reason.Contains("level 5") && bag.items.SequenceEqual(inputs) && rewards==0 && xp==0,
                "level loss after start denies completion before rolls, consumption, output or XP");
            level=6;
            using var gate=CraftingApi.RegisterRecipeGate("test.gate", c =>
                c.Station==station && c.ResultName=="Pickaxe" && c.RecipeId=="test.levels:tool" ? "Requires guild training." : null);
            check(CraftingApi.LockReason(owner,station,compiled)=="Requires guild training." && !Deliver(out reason) && reason=="Requires guild training.",
                "code gate ANDs with declared levels and receives the same unqualified output name in book and delivery");
            var native=new ResultData("Native",new Item {name="Porridge"},5);
            using var nativeGate=CraftingApi.RegisterRecipeGate("test.native",c=>c.RecipeId==null && c.Station==CraftingStation.Cooking && c.ResultName=="Porridge"?"Cooking 3":null);
            check(CraftingApi.LockReason(owner,CraftingStation.Cooking,native)=="Cooking 3" && CraftingApi.LockReason(owner,CraftingStation.Alchemy,native)==null,
                "native Cooking/Alchemy can be gated separately without a pack ID");
            using var faulty=CraftingApi.RegisterRecipeGate("test.broken",_=>throw new Exception("offline"));
            check(CraftingApi.LockReason(owner,station,result)?.Contains("Cannot verify")==true,"throwing gate fails closed");
            faulty.Dispose(); gate.Dispose();
            mining.Dispose();
            using var other=CraftingApi.RegisterSkill("test.mining","Mining",_=>throw new Exception("offline"));
            mining.Dispose();
            check(CraftingApi.LockReason(owner,station,compiled)?.Contains("Cannot verify")==true,"throwing level provider fails closed; stale handle does not remove its replacement");
            other.Dispose();
            using var restored=CraftingApi.RegisterSkill("test.mining","Mining",_=>1);
            check(CraftingApi.LockReason(owner,station,result)?.Contains("Mining level 2")==true,"all listed skill thresholds are mandatory");
            r.skillRequirements![0].minLevel=8; r.skillRequirements.RemoveAt(1);
            RecipeRegistry.SavePack(doc);
            check(!Deliver(out reason) && reason.Contains("level 8") && rewards==0 && xp==0,"pack reload during work enforces the new requirement on existing result");
            level=8;
            check(Deliver(out _) && rewards==1 && xp==1 && bag.items.Count==2 && !bag.ContainsItem(ore),"unlocked recipe completes normally with one reward roll and one XP callback");
            var livePreview=RecipeResultFactory.Build(RecipeRegistry.BrowseBenchRecipes("test.levels",station).Single(),new[]{template},_=>new Item{name="Pickaxe"},bag,e=>throw e);
            doc.Pack.enabled=false; RecipeRegistry.SavePack(doc);
            check(CraftingApi.LockReason(owner,station,livePreview)?.Contains("disabled")==true,"disabling a pack invalidates outstanding crafts");
            check(CraftingApi.LockReason(owner,CraftingStation.Alchemy,native)==null,"unrelated recipes stay unlocked");
        }
        finally {root.Delete(true);}
    }
}
