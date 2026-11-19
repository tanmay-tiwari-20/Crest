using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Crest.Features;

public sealed partial class LocalHub
{
    public void SetShelfPinned(string id, bool pinned)
    {
        lock (_gate)
        {
            if (_disposed) return;
            int index = _shelf.FindIndex(x => x.Id == id);
            if (index < 0 || _shelf[index].Pinned == pinned) return;
            _shelf[index] = _shelf[index] with { Pinned = pinned };
            PersistShelf();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string? RelinkShelf(string id, string replacement)
    {
        string path;
        bool directory;
        try
        {
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(replacement));
            directory = Directory.Exists(path);
            if (!directory && !File.Exists(path)) return "Choose an existing file or folder to restore this reference.";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { return "Windows could not read that location. Choose an existing file or folder."; }
        lock (_gate)
        {
            if (_disposed) return "The shelf is no longer available.";
            int index = _shelf.FindIndex(x => x.Id == id);
            if (index < 0) return "This reference has been removed.";
            if (_shelf[index].IsDirectory != directory) return _shelf[index].IsDirectory ? "Choose a folder for this folder reference." : "Choose a file for this file reference.";
            if (_shelf.Any(x => x.Id != id && string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase))) return "That item is already on the shelf. Remove the missing reference or choose another location.";
            string name = Path.GetFileName(path);
            _shelf[index] = _shelf[index] with { Path = path, Name = string.IsNullOrEmpty(name) ? path : name, Available = true };
            PersistShelf();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <summary>Return the closest existing ancestor so recovery never scans or guesses a user's files.</summary>
    public string? ShelfRecoveryFolder(string id)
    {
        ShelfItem? item;
        lock (_gate) item = _shelf.FirstOrDefault(x => x.Id == id);
        if (item is null) return null;
        try
        {
            string? parent = Path.GetDirectoryName(item.Path);
            while (!string.IsNullOrEmpty(parent))
            {
                if (Directory.Exists(parent)) return parent;
                parent = Path.GetDirectoryName(parent);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { }
        return null;
    }

    private void PersistShelf()
    {
        var pinned = _shelf.Where(x => x.Pinned).ToList();
        if (pinned.Count == 0) { _store.DeleteState("shelf.json"); return; }
        _store.WriteState("shelf.json", new ShelfState { Items = pinned });
    }
    private void RestoreShelf()
    {
        var saved = _store.ReadState<ShelfState>("shelf.json");
        if (saved?.SchemaVersion != 1) return;
        foreach (var item in (saved.Items ?? new()).Take(20))
        {
            if (item is null || !item.Pinned || string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 64 || string.IsNullOrWhiteSpace(item.Path) || item.Path.Length > 32767 || _shelf.Any(x => x.Id == item.Id)) continue;
            try
            {
                if (!Path.IsPathFullyQualified(item.Path)) continue;
                string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(item.Path));
                if (_shelf.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                string name = Path.GetFileName(path);
                _shelf.Add(item with { Path = path, Name = string.IsNullOrEmpty(name) ? path : name, Available = item.IsDirectory ? Directory.Exists(path) : File.Exists(path) });
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { }
        }
        PersistShelf();
    }
    public sealed class ShelfState { public int SchemaVersion { get; set; } = 1; public List<ShelfItem>? Items { get; set; } = new(); }
}
