namespace JsonConfigEditor.Wpf;

/// <summary>Glyphen aus "Segoe Fluent Icons" (Win11), Fallback "Segoe MDL2 Assets" (Win10).</summary>
public static class Icons
{
    public const string FontFamily = "Segoe Fluent Icons, Segoe MDL2 Assets";

    public const string Settings = "";
    public const string Star = "";
    public const string StarFill = "\uE735";
    public const string Lock = "";
    public const string Restart = "";
    public const string Reset = "";
    public const string Folder = "";
    public const string Search = "";
    public const string Warning = "";
    public const string Info = "";
    public const string Ok = "";
    public const string Error = "";
    public const string Edit = "";
    public const string List = "";

    private static readonly Dictionary<string, string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["settings"] = Settings,
        ["star"] = Star,
        ["mic"] = "",
        ["microphone"] = "",
        ["audio"] = "",
        ["folder"] = Folder,
        ["output"] = Folder,
        ["record"] = "",
        ["recording"] = "",
        ["shield"] = "",
        ["privacy"] = "",
        ["retention"] = "",
        ["delete"] = "",
        ["model"] = "",
        ["models"] = "",
        ["chip"] = "",
        ["waveform"] = "",
        ["live"] = "",
        ["quality"] = "",
        ["text"] = "",
        ["archive"] = "",
        ["tools"] = "",
        ["clock"] = "",
        ["globe"] = "",
        ["network"] = "",
        ["user"] = "",
        ["display"] = "",
    };

    /// <summary>Icon aus dem Descriptor, sonst nach Abschnittsname raten, sonst Zahnrad.</summary>
    public static string For(string? icon, string? sectionName)
    {
        if (icon != null)
        {
            if (Named.TryGetValue(icon, out var g)) return g;
            if (icon.Length == 1) return icon;
        }
        if (sectionName != null && Named.TryGetValue(sectionName, out var s)) return s;
        return Settings;
    }
}
