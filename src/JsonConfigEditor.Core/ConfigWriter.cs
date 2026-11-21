using System.Text.Json.Nodes;

namespace JsonConfigEditor.Core;

/// <summary>
/// Schreibt Feldwerte, die vom Default abweichen, und persönliche Favoriten in die User-Datei.
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

        var (root, before) = ReadUser(doc);
        var isDefault = field.HasDefault && JsonText.ValuesEqual(value, field.DefaultValue);
        if (isDefault)
            JsonText.RemovePath(root, field.Key);
        else
            JsonText.SetPath(root, field.Key, value?.DeepClone());
        Save(doc, root, before);

        field.HasUser = !isDefault;
        field.UserValue = isDefault ? null : value?.DeepClone();
    }

    /// <summary>Key aus der User-Datei nehmen, dann gilt wieder der Wert darunter.</summary>
    public static void Reset(ConfigDocument doc, string key)
    {
        var field = doc.Find(key) ?? throw new KeyNotFoundException($"Feld {key} gibt's nicht.");
        EnsureWritable(doc, field);
        var (root, before) = ReadUser(doc);
        if (JsonText.RemovePath(root, field.Key)) Save(doc, root, before);
        field.HasUser = false;
        field.UserValue = null;
    }

    /// <summary>Schreibt die persönlichen Favoriten als Metadaten in die User-Datei.</summary>
    public static void SetFavorites(ConfigDocument doc, IEnumerable<string> keys)
    {
        if (doc.ReadOnly) throw new InvalidOperationException("Nur Ansicht: " + (doc.WriteBlockedReason ?? "--readonly"));
        if (doc.UserFileError != null)
            throw new ConfigEditorException($"User-Datei ist kaputt, es wird nichts geschrieben: {doc.UserFileError}", ExitCodes.BrokenFile);

        var favorites = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Favoriten-Keys dürfen nicht leer sein.", nameof(keys));
            if (doc.Find(key) == null) throw new KeyNotFoundException($"Favoriten-Feld {key} gibt's nicht.");
            if (seen.Add(key)) favorites.Add(key);
        }

        var (root, before) = ReadUser(doc);
        // _jsonConfigEditor gehört dem Editor: ist es kaputt (von Hand "favorites": "x" o. ä.), wird es ersetzt.
        // Beim Lesen wurde es schon mit Hinweis ignoriert, hier also kein Grund, das Speichern scheitern zu lassen.
        if (!root.TryGetPropertyValue(ConfigDocument.EditorMetadataKey, out var metadataNode) || metadataNode is not JsonObject metadata)
        {
            if (metadataNode != null) Log.Warn($"{ConfigDocument.EditorMetadataKey} in {doc.Paths.UserFile} war kein Objekt, wird ersetzt");
            metadata = JsonText.NewObject();
            root[ConfigDocument.EditorMetadataKey] = metadata;
        }

        var array = new JsonArray();
        foreach (var key in favorites) array.Add(JsonValue.Create(key));
        metadata["favorites"] = array;
        Save(doc, root, before);
        doc.SetFavorites(favorites);
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

    /// <summary>
    /// Gibt es einen gültigen letzten Stand (<see cref="ConfigDocument.LastGoodFile"/>)? Dann dessen Zeitpunkt.
    /// Die .bak wird nur aus einer Datei geschrieben, die sich gerade fehlerfrei lesen ließ.
    /// </summary>
    public static DateTime? LastGoodTime(ConfigDocument doc)
    {
        var bak = doc.LastGoodFile;
        try
        {
            if (!File.Exists(bak)) return null;
            JsonText.ParseObject(SafeFile.ReadText(bak, "Sicherung"), bak);
            return File.GetLastWriteTime(bak);
        }
        catch (Exception ex) when (ex is ConfigEditorException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Kaputte User-Datei wegsichern (.kaputt-Datum) und den letzten gültigen Stand zurückholen.
    /// Die eigenen Werte bleiben so bis auf die letzte Änderung erhalten.
    /// </summary>
    /// <returns>Pfad der weggesicherten kaputten Datei, null wenn keine da war.</returns>
    public static string? RestoreLastGood(ConfigDocument doc)
    {
        if (doc.ReadOnly) throw new InvalidOperationException("Nur Ansicht: " + (doc.WriteBlockedReason ?? "--readonly"));
        var bak = doc.LastGoodFile;
        var text = SafeFile.ReadText(bak, "Sicherung");
        JsonText.ParseObject(text, $"Sicherung {bak}"); // nur zurückholen, was auch gültig ist

        var path = doc.Paths.UserFile;
        string? broken = null;
        if (File.Exists(path))
        {
            broken = $"{path}.kaputt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(path, broken);
        }
        AtomicFile.WriteAllText(path, text);
        Written?.Invoke(path, text);
        doc.Reload();
        Log.Info($"User-Datei aus {bak} wiederhergestellt" + (broken != null ? $", kaputte Fassung: {broken}" : ""));
        return broken;
    }

    private static void EnsureWritable(ConfigDocument doc, ConfigField field)
    {
        if (doc.WriteBlockedReason != null) throw new InvalidOperationException("Nur Ansicht: " + doc.WriteBlockedReason);
        if (doc.ReadOnly) throw new InvalidOperationException("Editor läuft read-only.");
        if (field.IsLocked) throw new FieldLockedException(field.Key);
        if (field.ReadOnly) throw new InvalidOperationException($"{field.Key} ist read-only.");
        if (field.Kind is FieldKind.Array or FieldKind.Object)
            throw new InvalidOperationException($"{field.Key}: Listen und Objekte bitte im Texteditor bearbeiten.");
        if (doc.UserFileError != null)
            throw new ConfigEditorException($"User-Datei ist kaputt, es wird nichts geschrieben: {doc.UserFileError}", ExitCodes.BrokenFile);
    }

    /// <summary>User-Datei frisch von Platte: was die App inzwischen reingeschrieben hat, bleibt. Text = Stand vorher (gültig), null wenn neu.</summary>
    private static (JsonObject Root, string? Text) ReadUser(ConfigDocument doc)
    {
        var path = doc.Paths.UserFile;
        if (!File.Exists(path)) return (JsonText.NewObject(), null);
        var text = ConfigDocument.ReadText(path, "User-Datei");
        return (JsonText.ParseObject(text, $"User-Datei {path}"), text);
    }

    private static void Save(ConfigDocument doc, JsonObject root, string? before)
    {
        var path = doc.Paths.UserFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = JsonText.Serialize(root);
        if (!string.IsNullOrWhiteSpace(before) && before != text)
        {
            // letzter gültiger Stand, für "Wiederherstellen", falls die Datei später kaputtgeht (Handarbeit, halbes Schreiben der App)
            try { AtomicFile.WriteAllText(doc.LastGoodFile, before); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Sicherung {doc.LastGoodFile} nicht geschrieben: {ex.Message}"); // Speichern selbst soll daran nicht scheitern
            }
        }
        AtomicFile.WriteAllText(path, text);
        Written?.Invoke(path, text);
    }
}

public sealed class FieldLockedException(string key)
    : InvalidOperationException($"{key} ist von der IT gesperrt.")
{
    public string Key { get; } = key;
}
