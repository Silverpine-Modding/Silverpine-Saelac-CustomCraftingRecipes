using SilverpineMods.CustomCraftingRecipes;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
    passed++;
}

// Use the real game's Item and Inventory classes, without launching Unity.
var destination = new Inventory();
int changes = 0;
destination.OnInventoryChanged += () => changes++;
var seedBag = new ObservedItem
{
    name = "Bag of Morning Dew Seeds",
    description = "Contains seeds from a Morning Dew."
};
Check(seedBag.inventory == null, "fresh recipe output starts without an inventory");
Item prepared = CraftingResultPreview.Prepare(seedBag, destination);
Check(ReferenceEquals(prepared, seedBag), "preview preserves the result item instance");
Check(seedBag.inventory != null && seedBag.inventory.ContainsItem(seedBag),
    "preview has a valid owning inventory before name and description formatting");
Check(!ReferenceEquals(seedBag.inventory, destination) && destination.GetItemCount() == 0,
    "preview does not grant an item to the player");
Check(changes == 0 && seedBag.Added == 0,
    "preview does not trigger inventory events or pickup callbacks");
Check(seedBag.GetFinalName() == "Bag of Morning Dew Seeds",
    "native result-name formatting succeeds before crafting");
Check(seedBag.GetFinalDescription().Contains(seedBag.description),
    "native result-description formatting succeeds before crafting");

Inventory preview = seedBag.inventory!;
CraftingResultPreview.Prepare(seedBag, destination);
Check(ReferenceEquals(preview, seedBag.inventory) && preview.GetItemCount() == 1,
    "repeated preview preparation does not duplicate or reassign the result");

Check(destination.TryAddItem(seedBag), "native inventory accepts the crafted result");
Check(ReferenceEquals(seedBag.inventory, destination) && destination.ContainsItem(seedBag),
    "native delivery replaces preview ownership with real inventory ownership");
Check(seedBag.Added == 1 && changes > 0,
    "pickup callbacks happen once when the craft delivers the item");
CraftingResultPreview.Prepare(seedBag, null);
Check(ReferenceEquals(seedBag.inventory, destination) && seedBag.Added == 1,
    "preparing an owned item leaves its ownership and callbacks intact");

var other = new ObservedItem { name = "Cooked Fish" };
CraftingResultPreview.Prepare(other, null);
Check(other.inventory != null && other.inventory.ContainsItem(other) && other.Added == 0,
    "preview works without an active player and keeps separate result inventories");
Check(!ReferenceEquals(preview, other.inventory), "unrelated previews do not accumulate together");
AuditChecks.Run(Check);
Console.WriteLine($"{passed} regression checks passed.");

sealed class ObservedItem : Item
{
    public int Added;
    public override void OnAddedToInventory() => Added++;
}
