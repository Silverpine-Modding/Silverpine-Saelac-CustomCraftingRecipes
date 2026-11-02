#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace SilverpineMods.CustomCraftingRecipes;

// Recipe packs own one top-level folder. Rename that folder, never copy or merge
// it, so backups and any accompanying files stay together.
internal static class RecipePackStore
{
    internal const string Suffix = ".recipe.pack";

    internal static string FolderName(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Pack ID is required.");
        id = id.Trim();
        if (id == "." || id == ".." || id.EndsWith(".", StringComparison.Ordinal) ||
            id.Any(c => !char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-'))
            throw new InvalidDataException("Pack IDs may contain only letters, numbers, dots, underscores, and hyphens, and cannot end in a dot.");
        string device = id.Split('.')[0].ToUpperInvariant();
        if (device == "CON" || device == "PRN" || device == "AUX" || device == "NUL" ||
            (device.Length == 4 && (device.StartsWith("COM", StringComparison.Ordinal) || device.StartsWith("LPT", StringComparison.Ordinal)) &&
             "123456789¹²³".IndexOf(device[3]) >= 0))
            throw new InvalidDataException("Choose a pack ID that is not a reserved Windows device name.");
        return id.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase) ? id : id + Suffix;
    }

    internal static void Save(string root, RecipePackDocument document, string text,
        Action<string, string, string?>? write = null)
    {
        root = Full(root);
        string targetFolder = Path.Combine(root, FolderName(document.Pack.packId));
        string destination = Path.Combine(targetFolder, "recipes.json");
        CheckLocation(root, targetFolder);
        if (!Directory.Exists(root)) throw new IOException("The recipe-pack directory was moved or removed. Reload it before saving.");
        string sourcePath = string.IsNullOrEmpty(document.Path) ? "" : Path.GetFullPath(document.Path);
        string sourceFolder = sourcePath.Length == 0 ? "" : Path.GetDirectoryName(sourcePath)!;
        if (sourcePath.Length != 0)
        {
            CheckLocation(root, sourceFolder);
            if (!Path.GetFileName(sourcePath).Equals("recipes.json", StringComparison.OrdinalIgnoreCase) ||
                !sourceFolder.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Only a dedicated .recipe.pack folder can be saved or renamed.");
            CheckSource(sourcePath, document.SourceText);
        }
        if (!Same(sourceFolder, targetFolder) && (Directory.Exists(targetFolder) || File.Exists(targetFolder)))
            throw new IOException("The destination recipe-pack folder already exists. Choose a different pack ID; folders are never merged or overwritten.");

        write ??= WriteAtomic;
        if (sourcePath.Length == 0)
        {
            SaveNew(root, document, text, targetFolder, destination, write);
            return;
        }

        bool rename = !string.Equals(sourceFolder, targetFolder, StringComparison.Ordinal);
        if (rename) CheckDedicatedFolder(sourceFolder, sourcePath);
        string currentFolder = sourceFolder;
        try
        {
            if (rename) MoveFolder(root, ref currentFolder, targetFolder);
            write(destination, text, document.SourceText);
        }
        catch (Exception error)
        {
            if (!string.Equals(currentFolder, sourceFolder, StringComparison.Ordinal))
            {
                try { MoveFolder(root, ref currentFolder, sourceFolder); }
                catch (Exception rollback)
                {
                    // Do not leave the editor pointing at another folder's new owner.
                    document.Path = Path.Combine(currentFolder, "recipes.json");
                    throw new IOException("Save failed and the folder could not be moved back. Your files remain at '" +
                        currentFolder + "'. Reload or recover that folder before retrying. " + rollback.Message, error);
                }
            }
            throw;
        }
        document.Path = destination;
        document.SourceText = text;
    }

    private static void SaveNew(string root, RecipePackDocument document, string text,
        string targetFolder, string destination, Action<string, string, string?> write)
    {
        // Stage first: Directory.Move refuses a destination created concurrently,
        // even when it is empty. A failed new save never becomes an existing edit.
        string draft = Path.Combine(root, ".recipes-new-" + Guid.NewGuid().ToString("N"));
        CheckLocation(root, draft);
        Directory.CreateDirectory(draft);
        string draftPath = Path.Combine(draft, "recipes.json");
        try
        {
            write(draftPath, text, null);
            CheckLocation(root, targetFolder);
            Directory.Move(draft, targetFolder);
            document.Path = destination;
            document.SourceText = text;
        }
        finally
        {
            if (Directory.Exists(draft))
            {
                CheckLocation(root, draft);
                // Remove only our exact staged file, never an external edit or
                // unknown contents. No recursive cleanup of a computed path.
                if (File.Exists(draftPath) && (File.GetAttributes(draftPath) & FileAttributes.ReparsePoint) == 0 &&
                    File.ReadAllText(draftPath) == text) File.Delete(draftPath);
                if (!Directory.EnumerateFileSystemEntries(draft).Any()) Directory.Delete(draft);
            }
        }
    }

    internal static void WriteAtomic(string path, string text, string? expectedText)
    {
        CheckFile(path);
        CheckFile(path + ".bak");
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, ".recipes-save-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(text);
            if (expectedText == null) File.Move(temporary, path);
            else
            {
                CheckSource(path, expectedText);
                File.Replace(temporary, path, path + ".bak");
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void MoveFolder(string root, ref string current, string destination)
    {
        CheckLocation(root, current);
        CheckLocation(root, destination);
        if (Same(current, destination))
        {
            // Windows needs an intermediate name for case-only renames.
            string temporary = Path.Combine(root, ".recipes-rename-" + Guid.NewGuid().ToString("N"));
            CheckLocation(root, temporary);
            Directory.Move(current, temporary);
            current = temporary;
        }
        Directory.Move(current, destination);
        current = destination;
    }

    private static void CheckSource(string path, string? expectedText)
    {
        CheckFile(path);
        if (expectedText == null || !File.Exists(path) || !string.Equals(File.ReadAllText(path), expectedText, StringComparison.Ordinal))
            throw new IOException("The pack changed on disk or was moved. Your edits are still open; reload or reconcile the other changes before saving.");
    }

    private static void CheckFile(string path)
    {
        if (Directory.Exists(path) || (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("Recipe files and their backups cannot be directories or links.");
    }

    private static void CheckDedicatedFolder(string folder, string sourcePath)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Pack folders containing links cannot be renamed automatically.");
            if ((attributes & FileAttributes.Directory) != 0) CheckDedicatedFolder(entry, sourcePath);
            else if (Path.GetExtension(entry).Equals(".json", StringComparison.OrdinalIgnoreCase) && !Same(entry, sourcePath))
                throw new IOException("This folder contains another JSON document. Give the recipe pack a dedicated folder before renaming it.");
        }
    }

    private static void CheckLocation(string root, string folder)
    {
        if (!Same(Path.GetDirectoryName(Full(folder)) ?? "", root))
            throw new IOException("The recipe-pack folder must be directly inside the configured recipe directory; the root itself cannot be renamed.");
        // Check ancestors too: a linked root or parent must not redirect moves.
        for (string? current = Full(folder); current != null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked pack folders cannot be edited or renamed automatically.");
    }

    private static string Full(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private static bool Same(string a, string b) => a.Length != 0 && b.Length != 0 && string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);
}
