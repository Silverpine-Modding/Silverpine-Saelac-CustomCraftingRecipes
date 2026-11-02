using Newtonsoft.Json;
using SilverpineMods.CustomCraftingRecipes;

internal static class PackRenameChecks
{
    internal static void Run(Action<bool, string> check)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "rename-fixtures-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            bool Refuses(Action action)
            {
                try { action(); return false; }
                catch (IOException) { return true; }
                catch (InvalidDataException) { return true; }
            }
            string Target(string id) => Path.Combine(root, RecipePackStore.FolderName(id));
            RecipePackDocument Draft(string id) => new("", new RecipePackDefinition
            {
                packId = id, displayName = "Recipe rename test", recipes = new()
                {
                    new RecipeDefinition { id = "door", ingredients = new() { new IngredientDefinition { item = "Plank", amount = 1 } }, result = "Wooden Door" }
                }
            });
            RecipeRegistry.Initialize(root);
            var starter = RecipeRegistry.LoadEditablePacks().Single();
            string starterPath = starter.Path, starterId = starter.Pack.packId;
            starter.Pack.enabled = false;
            RecipeRegistry.SavePack(starter);
            check(starter.Path == Path.Combine(Target(starterId), "recipes.json") && !File.Exists(starterPath) && starter.Pack.packId == starterId,
                "saving repairs an existing default recipe folder without changing its pack ID");

            var doc = Draft("my.recipe.pack");
            doc.Pack.packId = "herb.seeds";
            RecipeRegistry.SavePack(doc);
            check(doc.Path == Path.Combine(Target("herb.seeds"), "recipes.json") && !Directory.Exists(Target("my.recipe.pack")),
                "a new recipe pack uses its final ID on its first save");
            check(doc.SourceText == File.ReadAllText(doc.Path), "successful saves track the exact disk text for conflict detection");
            string oldPath = doc.Path, oldFolder = Path.GetDirectoryName(oldPath)!;
            string oldText = File.ReadAllText(oldPath);
            string recipes = JsonConvert.SerializeObject(doc.Pack.recipes);
            Directory.CreateDirectory(Path.Combine(oldFolder, "notes"));
            File.WriteAllText(Path.Combine(oldFolder, "notes", "instructions.txt"), "keep together");
            File.WriteAllBytes(Path.Combine(oldFolder, "icon.png"), new byte[] { 3, 1, 4 });
            doc.Pack.packId = "garden.recipe.pack";
            RecipeRegistry.SavePack(doc);
            check(Path.GetFileName(Path.GetDirectoryName(doc.Path)) == "garden.recipe.pack" && !Directory.Exists(oldFolder),
                "changing a saved Pack ID moves its folder and does not duplicate the required suffix");
            check(File.ReadAllText(doc.Path + ".bak") == oldText && File.ReadAllText(Path.Combine(Target("garden.recipe.pack"), "notes", "instructions.txt")) == "keep together" &&
                File.ReadAllBytes(Path.Combine(Target("garden.recipe.pack"), "icon.png")).SequenceEqual(new byte[] { 3, 1, 4 }),
                "recipe-folder renaming preserves previous JSON, companion files, and nested contents");
            check(JsonConvert.SerializeObject(RecipeRegistry.ReadPack(doc.Path).recipes) == recipes,
                "renaming leaves recipe IDs, ingredients, quantities, results, and station restrictions unchanged");
            var counts = RecipeRules.Counts(doc.Pack.recipes[0]);
            check(RecipeRegistry.TryFindRecipe(CraftingStation.CustomCrafting, counts, out var recipe) && recipe!.Id == "garden.recipe.pack:door" && RecipeRegistry.Count == 1,
                "renamed recipes reload once under the new qualified ID");
            doc.Pack.enabled = false; RecipeRegistry.SavePack(doc);
            check(!RecipeRegistry.TryFindRecipe(CraftingStation.CustomCrafting, counts, out _), "disabling a renamed pack unloads its recipes");
            doc.Pack.enabled = true; RecipeRegistry.SavePack(doc);
            check(RecipeRegistry.TryFindRecipe(CraftingStation.CustomCrafting, counts, out _), "enabling a renamed pack reloads its recipes");
            string samePath = doc.Path;
            doc.Pack.displayName = "A new display label";
            RecipeRegistry.SavePack(doc);
            check(doc.Path == samePath, "changing only a display name does not change the pack folder");

            doc.Pack.packId = "GARDEN.RECIPE.PACK";
            RecipeRegistry.SavePack(doc);
            check(Directory.EnumerateDirectories(root).Any(p => Path.GetFileName(p) == "GARDEN.RECIPE.PACK") &&
                !Directory.EnumerateDirectories(root, ".recipes-rename-*").Any(),
                "case-only recipe folder renames update the real directory casing without leftovers");
            oldPath = doc.Path; oldText = File.ReadAllText(oldPath);
            doc.Pack.packId = "rollback";
            check(Refuses(() => RecipePackStore.Save(root, doc, "never committed", (_, _, _) => throw new IOException("simulated write failure"))) &&
                doc.Path == oldPath && doc.SourceText == oldText && File.ReadAllText(oldPath) == oldText && !Directory.Exists(Target("rollback")),
                "failed saves roll a renamed folder back and retain the editor's original path and snapshot");
            doc.Pack.packId = "garden.recipe.pack";
            check(Refuses(() => RecipePackStore.Save(root, doc, "never committed", (_, _, _) => throw new IOException("simulated case write failure"))) &&
                doc.Path == oldPath && Directory.EnumerateDirectories(root).Any(p => Path.GetFileName(p) == "GARDEN.RECIPE.PACK") &&
                !Directory.EnumerateDirectories(root, ".recipes-rename-*").Any(),
                "case-only recipe renames also roll back safely after a write failure");

            Directory.CreateDirectory(Target("taken"));
            File.WriteAllText(Path.Combine(Target("taken"), "sentinel"), "untouched");
            doc.Pack.packId = "taken";
            check(Refuses(() => RecipeRegistry.SavePack(doc)) && doc.Path == oldPath && File.ReadAllText(oldPath) == oldText &&
                File.ReadAllText(Path.Combine(Target("taken"), "sentinel")) == "untouched", "an occupied recipe destination is never merged or overwritten");
            Directory.CreateDirectory(Target("empty")); doc.Pack.packId = "empty";
            check(Refuses(() => RecipeRegistry.SavePack(doc)), "even an empty existing recipe destination blocks renaming");
            File.WriteAllText(Target("occupied-file"), "not a directory"); doc.Pack.packId = "occupied-file";
            check(Refuses(() => RecipeRegistry.SavePack(doc)) && File.ReadAllText(Target("occupied-file")) == "not a directory",
                "a file occupying a recipe folder's target name blocks renaming");
            var duplicate = Draft("GARDEN.RECIPE.PACK");
            check(Refuses(() => RecipeRegistry.SavePack(duplicate)) && duplicate.Path == "" && duplicate.SourceText == null && File.ReadAllText(oldPath) == oldText,
                "a new document cannot overwrite a saved recipe pack");

            doc.Pack.packId = "external-edit";
            File.WriteAllText(oldPath, oldText + " ");
            check(Refuses(() => RecipeRegistry.SavePack(doc)) && doc.Path == oldPath && File.ReadAllText(oldPath) == oldText + " " && !Directory.Exists(Target("external-edit")),
                "external recipe edits are detected before any folder is moved");
            var discarded = new RecipePackDocument(doc.Path, RecipeRegistry.ReadPack(doc.Path), doc.SourceText);
            check(Refuses(() => RecipeRegistry.SavePack(discarded)), "discarding an editor draft cannot reset the stale-file protection");
            doc = RecipeRegistry.LoadEditablePacks().Single(p => p.Path.Equals(oldPath, StringComparison.OrdinalIgnoreCase));
            doc.Pack.packId = "reloaded"; RecipeRegistry.SavePack(doc);
            check(File.Exists(doc.Path) && Path.GetDirectoryName(doc.Path) == Target("reloaded"), "reloading an external change allows a subsequent rename");

            oldPath = doc.Path; oldText = File.ReadAllText(oldPath); oldFolder = Path.GetDirectoryName(oldPath)!;
            string unrelated = Path.Combine(oldFolder, "notes", "other.json");
            File.WriteAllText(unrelated, "another document"); doc.Pack.packId = "shared-folder";
            check(Refuses(() => RecipeRegistry.SavePack(doc)) && File.ReadAllText(unrelated) == "another document" && !Directory.Exists(Target("shared-folder")),
                "folders containing another JSON document are not moved automatically");
            File.Delete(unrelated);
            check(Refuses(() => RecipePackStore.Save(Path.Combine(root, "restricted"), doc, oldText)), "recipe saves cannot move a source outside their configured root");
            var rootDocument = new RecipePackDocument(Path.Combine(root, "recipes.json"), new RecipePackDefinition { packId = "root-move" }, "root pack");
            File.WriteAllText(rootDocument.Path, "root pack");
            check(Refuses(() => RecipePackStore.Save(root, rootDocument, "changed")) && File.ReadAllText(rootDocument.Path) == "root pack",
                "the shared recipe-pack root can never be renamed");
            foreach (string id in new[] { "", ".", "..", "../outside", "a/b", "a\\b", "bad:name", "trailing.", "has spaces", "CON", "nul.pack", "COM1", "LPT9.foo", "COM¹" })
                check(Refuses(() => RecipePackStore.FolderName(id)), "unsafe recipe folder ID rejected: " + id);
            check(RecipePackStore.FolderName(" spaced ") == "spaced.recipe.pack" && RecipePackStore.FolderName("name.recipe.pack") == "name.recipe.pack",
                "recipe folder names trim surrounding whitespace and append the suffix only when needed");

            var diskFailure = Draft("disk-failure"); RecipeRegistry.SavePack(diskFailure);
            string failurePath = diskFailure.Path, failureText = File.ReadAllText(failurePath);
            Directory.CreateDirectory(failurePath + ".bak"); diskFailure.Pack.packId = "disk-failure-renamed";
            check(Refuses(() => RecipeRegistry.SavePack(diskFailure)) && diskFailure.Path == failurePath && File.ReadAllText(failurePath) == failureText &&
                !Directory.Exists(Target("disk-failure-renamed")), "a real atomic-write failure rolls the recipe folder back without changing its JSON");

            var racing = Draft("race");
            check(Refuses(() => RecipePackStore.Save(root, racing, "new recipe", (path, text, expected) =>
            {
                RecipePackStore.WriteAtomic(path, text, expected);
                Directory.CreateDirectory(Target("race"));
                File.WriteAllText(Path.Combine(Target("race"), "sentinel"), "external owner");
            })) && racing.Path == "" && racing.SourceText == null && !File.Exists(Path.Combine(Target("race"), "recipes.json")) &&
                File.ReadAllText(Path.Combine(Target("race"), "sentinel")) == "external owner" && !Directory.EnumerateDirectories(root, ".recipes-new-*").Any(),
                "a destination created during first save is not merged, and the draft remains retryable");
            racing.Pack.packId = "retry"; RecipeRegistry.SavePack(racing);
            check(File.Exists(racing.Path), "a failed first save can be retried with a different pack ID");

            doc.Pack.packId = "blocked-rollback";
            string recoveryError = "";
            try
            {
                RecipePackStore.Save(root, doc, "not committed", (_, _, _) =>
                {
                    Directory.CreateDirectory(oldFolder);
                    File.WriteAllText(Path.Combine(oldFolder, "sentinel"), "new owner");
                    throw new IOException("simulated rollback collision");
                });
            }
            catch (IOException error) { recoveryError = error.Message; }
            check(doc.Path == Path.Combine(Target("blocked-rollback"), "recipes.json") && recoveryError.Contains(Target("blocked-rollback")) &&
                File.ReadAllText(doc.Path) == oldText && File.ReadAllText(Path.Combine(oldFolder, "sentinel")) == "new owner",
                "a blocked rollback preserves both owners' files and reports the recoverable pack location");
        }
        finally
        {
            // This directory is uniquely created by this test under its output.
            if (Path.GetDirectoryName(root) == Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)) Directory.Delete(root, true);
        }
    }
}
