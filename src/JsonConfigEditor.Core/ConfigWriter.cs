using System.Text.Json.Nodes;

namespace JsonConfigEditor.Core;

/// <summary>
/// Schreibt in die User-Datei, und nur das, was vom Default (Machine vor Base) abweicht.
/// Die Datei wird jedes Mal frisch gelesen und nur an der Stelle gepatcht: unbekannte Keys,
/// _-Metadaten, Arrays und Reihenfolge bleiben. Kommentare in der User-Datei gehen verloren.
/// </summary>
public static class ConfigWriter
{
    /// <summary>Wird nach jedem erfolgreichen Schreiben gesetzt, damit ein FileSystemWatcher das eigene Schreiben erkennt.</summary>
    public static event Action<string, string>? Written;

    public static void Set(ConfigDocument doc, string key, JsonNode? value)
    {
        var field = doc.Find(key) ?? throw new KeyNotFoundException($"Feld {key} gibt's nicht.");
        EnsureWritable(doc, field);

        var root = ReadUser(doc);
        var isDefault = field.HasDefault && JsonText.ValuesEqual(value, field.DefaultValue);
        if (isDefault)
            JsonText.RemovePath(root, field.Key);
        else
            JsonText.SetPath(root, field.Key, value?.DeepClone());
        Save(doc, root);

        field.HasUser = !isDefault;
        field.UserValue = isDefault ? null : value?.DeepClone();
    }

    /// <summary>Key aus der User-Datei nehmen, dann gilt wieder der Wert darunter.</summary>
    public static void Reset(ConfigDocument doc, string key)
    {
        var field = doc.Find(key) ?? throw new KeyNotFoundException($"Feld {key} gibt's nicht.");
        EnsureWritable(doc, field);
        var root = ReadUser(doc);
        if (JsonText.RemovePath(root, field.Key)) Save(doc, root);
        field.HasUser = false;
        field.UserValue = null;
    }

    /// <summary>Kaputte User-Datei wegsichern (.kaputt-Datum) und mit leerer Datei neu beginnen.</summary>
    public static string BackupBrokenUserFile(ConfigDocument doc)
    {
        var path = doc.Paths.UserFile;
        var backup = $"{path}.kaputt-{DateTime.Now:yyyyMMdd-HHmmss}";
        File.Move(path, backup);
        doc.Reload();
        return backup;
    }

    private static void EnsureWritable(ConfigDocument doc, ConfigField field)
    {
        if (doc.ReadOnly) throw new InvalidOperationException("Editor läuft read-only.");
        if (field.IsLocked) throw new FieldLockedException(field.Key);
        if (field.ReadOnly) throw new InvalidOperationException($"{field.Key} ist read-only.");
        if (field.Kind is FieldKind.Array or FieldKind.Object)
            throw new InvalidOperationException($"{field.Key}: Listen und Objekte bitte im Texteditor bearbeiten.");
        if (doc.UserFileError != null)
            throw new ConfigEditorException($"User-Datei ist kaputt, es wird nichts geschrieben: {doc.UserFileError}", ExitCodes.BrokenFile);
    }

    private static JsonObject ReadUser(ConfigDocument doc)
    {
        var path = doc.Paths.UserFile;
        if (!File.Exists(path)) return JsonText.NewObject();
        // frisch von Platte: was die App inzwischen reingeschrieben hat, bleibt
        return JsonText.ParseObject(ConfigDocument.ReadText(path), $"User-Datei {path}");
    }

    private static void Save(ConfigDocument doc, JsonObject root)
    {
        var path = doc.Paths.UserFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = JsonText.Serialize(root);
        AtomicFile.WriteAllText(path, text);
        Written?.Invoke(path, text);
    }
}

public sealed class FieldLockedException(string key)
    : InvalidOperationException($"{key} ist von der IT gesperrt.")
{
    public string Key { get; } = key;
}
