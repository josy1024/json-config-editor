using System.Text.Json;
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
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigEditorException($"Descriptor {path} nicht lesbar: {ex.Message}", ExitCodes.BadArguments, ex);
        }
        return Parse(text, path);
    }

    public static EditorDescriptor Parse(string text, string what = "Descriptor")
    {
        EditorDescriptor? d;
        try { d = JsonSerializer.Deserialize<EditorDescriptor>(text, ReadOptions); }
        catch (JsonException ex)
        {
            throw new ConfigEditorException($"{what} ist kein gültiges JSON: {ex.Message}", ExitCodes.BrokenFile, ex);
        }
        d ??= new EditorDescriptor();
        // Dictionaries kommen vom Serializer case-sensitive, Keys sollen wie bei IConfiguration egal sein
        d.Sections = new Dictionary<string, SectionDescriptor>(d.Sections ?? [], StringComparer.OrdinalIgnoreCase);
        d.Fields = new Dictionary<string, FieldDescriptor>(d.Fields ?? [], StringComparer.OrdinalIgnoreCase);
        d.Favorites ??= [];
        return d;
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

    /// <summary>Unbekannte oder für später reservierte Attribute (options, list …).</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    internal static readonly string[] Reserved = ["options", "optionsFromGlob", "list", "provider"];
}
