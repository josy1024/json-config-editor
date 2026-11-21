using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonConfigEditor.Core;

public enum FieldKind { Bool, Int, Number, String, Path, Array, Object }

public enum PathKind { Folder, File }

/// <summary>Woher der wirksame Wert kommt.</summary>
public enum ValueOrigin
{
    /// <summary>Werks-Default aus der Basisdatei.</summary>
    Default,
    /// <summary>IT-Vorgabe aus der Machine-Datei, User darf ändern.</summary>
    Machine,
    /// <summary>Eigener Wert aus der User-Datei.</summary>
    User,
    /// <summary>Von der IT gesperrt (Machine:Locked).</summary>
    Locked,
    /// <summary>Nirgends gesetzt.</summary>
    None,
}

/// <summary>Ein editierbarer Wert, Key im Format "Abschnitt:Key".</summary>
public sealed class ConfigField
{
    public required string Key { get; init; }
    public required string Section { get; init; }
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    public FieldKind Kind { get; set; }

    public bool ReadOnly { get; set; }
    public bool Hidden { get; set; }
    public bool Advanced { get; set; }
    public bool IsFavorite { get; set; }
    public bool RestartRequired { get; set; }

    public double? Min { get; set; }
    public double? Max { get; set; }
    public string? Unit { get; set; }
    public string? ZeroMeans { get; set; }
    public string? EmptyMeans { get; set; }

    public PathKind PathKind { get; set; } = PathKind.Folder;
    public bool MustExist { get; set; }
    public bool ExpandEnv { get; set; } = true;
    public bool RelativeToBase { get; set; }
    public bool WarnCloudFolder { get; set; }

    /// <summary>Steht der Key in der Basisdatei?</summary>
    public bool InBase { get; set; }

    public JsonNode? BaseValue { get; internal set; }
    public JsonNode? MachineValue { get; internal set; }
    public JsonNode? LockedValue { get; internal set; }
    public JsonNode? UserValue { get; internal set; }

    public bool HasBase { get; internal set; }
    public bool HasMachine { get; internal set; }
    public bool HasLocked { get; internal set; }
    public bool HasUser { get; internal set; }

    public bool IsLocked => HasLocked;
    public bool IsEditable => !IsLocked && !ReadOnly && Kind is not (FieldKind.Array or FieldKind.Object);

    /// <summary>Was ohne User-Datei gälte (Machine vor Base). Damit vergleicht das Sparse-Schreiben.</summary>
    public JsonNode? DefaultValue => HasMachine ? MachineValue : BaseValue;
    public bool HasDefault => HasMachine || HasBase;

    public JsonNode? EffectiveValue =>
        HasLocked ? LockedValue : HasUser ? UserValue : HasMachine ? MachineValue : BaseValue;

    public ValueOrigin Origin =>
        HasLocked ? ValueOrigin.Locked
        : HasUser ? ValueOrigin.User
        : HasMachine ? ValueOrigin.Machine
        : HasBase ? ValueOrigin.Default
        : ValueOrigin.None;

    /// <summary>Eigener Wert, der wirklich vom Default abweicht.</summary>
    public bool IsChanged => HasUser && !(HasDefault && JsonText.ValuesEqual(UserValue, DefaultValue));

    public int ArrayCount => EffectiveValue is JsonArray a ? a.Count : 0;

    public bool? BoolValue => EffectiveValue is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False
        ? v.GetValue<bool>() : null;

    public string? StringValue => EffectiveValue is JsonValue v && v.GetValueKind() == JsonValueKind.String
        ? v.GetValue<string>() : EffectiveValue?.ToJsonString();

    public double? NumberValue => EffectiveValue is JsonValue v && v.GetValueKind() == JsonValueKind.Number
        ? double.Parse(v.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture) : null;

    public override string ToString() => $"{Key} = {EffectiveValue?.ToJsonString() ?? "null"} ({Origin})";
}

public sealed class ConfigSection
{
    public required string Name { get; init; }
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>_hinweis aus User-, Machine- oder Basisdatei.</summary>
    public string? Hint { get; set; }
    public string? Icon { get; set; }
    public int Order { get; set; }
    public bool Hidden { get; set; }
    public List<ConfigField> Fields { get; } = [];

    public const string GeneralName = "_general";
    public const string OtherName = "_other";
}
