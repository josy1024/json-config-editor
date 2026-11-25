using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace JsonConfigEditor.Core;

/// <summary>Inhalt einer <c>appsettings.editor.json</c>. Alles optional.</summary>
public sealed class EditorDescriptor
{
    public int FormatVersion { get; set; } = 1;
    public AppDescriptor? App { get; set; }
    public List<string> Favorites { get; set; } = [];
    public Dictionary<string, SectionDescriptor> Sections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, FieldDescriptor> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static EditorDescriptor Load(string path)
    {
        var text = SafeFile.ReadText(path, "Descriptor", ExitCodes.BadArguments);
        return Parse(text, $"Descriptor {path}");
    }

    /// <summary>
    /// Tolerant: Nur kaputtes JSON (Syntax) ist ein Fehler. Falscher Typ an einer Stelle ("min": "5",
    /// "fields": [] …) kostet nur diesen Eintrag und landet in <see cref="Warnings"/>, der Editor geht trotzdem auf.
    /// </summary>
    public static EditorDescriptor Parse(string text, string what = "Descriptor")
    {
        var d = new EditorDescriptor();
        try
        {
            var node = JsonNode.Parse(text, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (node is null) return d;
            if (node is not JsonObject root)
                throw new ConfigEditorException($"{what} ist kein JSON-Objekt.", ExitCodes.BrokenFile);

            foreach (var (name, value) in root)
            {
                switch (name.ToLowerInvariant())
                {
                    case "formatversion":
                        if (value is JsonValue v && v.TryGetValue<int>(out var fv)) d.FormatVersion = fv;
                        else d.Warn($"formatVersion muss eine Zahl sein ({Short(value)}), 1 angenommen.");
                        break;
                    case "app":
                        d.App = Part<AppDescriptor>(value, "app", d);
                        break;
                    case "favorites":
                        if (value is JsonArray favs)
                        {
                            foreach (var item in favs)
                            {
                                if (item is JsonValue fvv && fvv.TryGetValue<string>(out var key) && !string.IsNullOrWhiteSpace(key)) d.Favorites.Add(key);
                                else d.Warn($"favorites: Eintrag {Short(item)} ist kein Key, ignoriert.");
                            }
                        }
                        else if (value != null) d.Warn("favorites muss eine Liste von Keys sein, ignoriert.");
                        break;
                    case "sections":
                        ReadMap(value, "sections", d.Sections, d);
                        break;
                    case "fields":
                        ReadMap(value, "fields", d.Fields, d);
                        break;
                    default:
                        (d.Extra ??= new())[name] = JsonSerializer.SerializeToElement(value);
                        break;
                }
            }
        }
        catch (ConfigEditorException) { throw; }
        catch (JsonException ex)
        {
            throw new ConfigEditorException($"{what} ist kein gültiges JSON: {ex.Message}", ExitCodes.BrokenFile, ex);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // z. B. doppelter Key auf gleicher Ebene
            throw new ConfigEditorException($"{what} ist ungültig: {ex.Message}", ExitCodes.BrokenFile, ex);
        }
        return d;
    }

    /// <summary>Hinweise zu Einträgen, die beim Lesen ignoriert wurden.</summary>
    [JsonIgnore] public List<string> Warnings { get; } = [];

    private void Warn(string message) => Warnings.Add("Descriptor: " + message);

    private static void ReadMap<T>(JsonNode? value, string where, Dictionary<string, T> target, EditorDescriptor d) where T : class, new()
    {
        if (value is null) return;
        if (value is not JsonObject map)
        {
            d.Warn($"{where} muss ein Objekt sein, ignoriert.");
            return;
        }
        foreach (var (key, item) in map)
        {
            if (Part<T>(item, $"{where}.{key}", d) is not { } part) continue;
            if (!target.TryAdd(key, part)) d.Warn($"{where}.{key} ist doppelt (Groß-/Kleinschreibung zählt nicht), der erste gilt.");
        }
    }

    /// <summary>Objekt lesen; geht das nicht, Eigenschaft für Eigenschaft und nur die kaputten weglassen.</summary>
    private static T? Part<T>(JsonNode? node, string where, EditorDescriptor d) where T : class, new()
    {
        if (node is null) return null;
        if (node is not JsonObject obj)
        {
            d.Warn($"{where} muss ein Objekt sein ({Short(node)}), ignoriert.");
            return null;
        }
        try { return JsonSerializer.Deserialize<T>(obj, ReadOptions) ?? new T(); }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or NotSupportedException) { }

        var clean = new JsonObject();
        foreach (var (name, value) in obj)
        {
            var single = new JsonObject { [name] = value?.DeepClone() };
            try
            {
                JsonSerializer.Deserialize<T>(single, ReadOptions);
                clean[name] = value?.DeepClone();
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                d.Warn($"{where}.{name} hat einen ungültigen Wert ({Short(value)}), ignoriert.");
            }
        }
        try { return JsonSerializer.Deserialize<T>(clean, ReadOptions) ?? new T(); }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or NotSupportedException) { return new T(); }
    }

    private static string Short(JsonNode? n)
    {
        var s = n?.ToJsonString() ?? "null";
        return s.Length > 40 ? s[..40] + "…" : s;
    }
}

public sealed class AppDescriptor
{
    public string? Name { get; set; }
    public string? Title { get; set; }
    /// <summary>Basisdatei, relativ zum Ordner des Descriptors. Default appsettings.json.</summary>
    public string? Base { get; set; }
    public string? UserFile { get; set; }
    public string? MachineFile { get; set; }
}

public sealed class SectionDescriptor
{
    public string? Label { get; set; }
    public int? Order { get; set; }
    public string? Icon { get; set; }
    public string? Description { get; set; }
    public bool? Hidden { get; set; }
}

public sealed class FieldDescriptor
{
    public string? Label { get; set; }
    public string? Description { get; set; }
    /// <summary>bool, int, number, string, path</summary>
    public string? Type { get; set; }
    public bool? ReadOnly { get; set; }
    public bool? Hidden { get; set; }
    public bool? Advanced { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public string? Unit { get; set; }
    public string? ZeroMeans { get; set; }
    public string? EmptyMeans { get; set; }
    public bool? RestartRequired { get; set; }
    /// <summary>file oder folder</summary>
    public string? PathKind { get; set; }
    public bool? MustExist { get; set; }
    public bool? ExpandEnv { get; set; }
    /// <summary>"base" = relativ zum Ordner der Basisdatei auflösen.</summary>
    public string? RelativeTo { get; set; }
    public bool? WarnCloudFolder { get; set; }
    /// <summary>Nur bei Arrays: Umsortieren und Zeilentext.</summary>
    public ListDescriptor? List { get; set; }

    /// <summary>Unbekannte oder für später reservierte Attribute (options, provider …).</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    internal static readonly string[] Reserved = ["options", "optionsFromGlob", "provider"];
}

public sealed class ListDescriptor
{
    /// <summary>▲ ▼ anbieten. Default true.</summary>
    public bool? Reorder { get; set; }
    /// <summary>Eigenschaft für den Zeilentext. Default: erste String-Eigenschaft.</summary>
    public string? ItemLabel { get; set; }
    /// <summary>Eigenschaften, die klein unter dem Zeilentext stehen.</summary>
    public List<string>? ItemDetail { get; set; }
}
