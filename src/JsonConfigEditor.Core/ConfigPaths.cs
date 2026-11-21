namespace JsonConfigEditor.Core;

/// <summary>Aufgelöste Dateipfade (Umgebungsvariablen expandiert, absolut).</summary>
public sealed record ConfigPaths(string BaseFile, string UserFile, string? MachineFile, string? DescriptorFile, string? AppName)
{
    public const string DefaultUserFileName = "appsettings.local.json";
    public const string DefaultMachineFileName = "appsettings.json";

    /// <summary>Descriptor lesen (falls angegeben oder neben der Basisdatei gefunden) und Pfade auflösen.</summary>
    public static (ConfigPaths Paths, EditorDescriptor? Descriptor) Resolve(ConfigEditorOptions o)
    {
        string? descriptorPath = Full(o.DescriptorPath);
        if (descriptorPath == null && o.BasePath != null)
        {
            // appsettings.json → appsettings.editor.json daneben
            var b = Full(o.BasePath)!;
            var sibling = Path.Combine(Path.GetDirectoryName(b)!, Path.GetFileNameWithoutExtension(b) + ".editor.json");
            if (File.Exists(sibling)) descriptorPath = sibling;
        }
        if (o.DescriptorPath != null && !File.Exists(descriptorPath))
            throw new ConfigEditorException($"Descriptor nicht gefunden: {descriptorPath}", ExitCodes.BadArguments);

        var descriptor = descriptorPath != null ? EditorDescriptor.Load(descriptorPath) : null;
        var app = descriptor?.App;
        var appName = o.AppName ?? app?.Name;

        string? basePath = Full(o.BasePath);
        if (basePath == null)
        {
            if (descriptorPath == null)
                throw new ConfigEditorException("Keine Basisdatei: Descriptor-Pfad oder --base angeben.", ExitCodes.BadArguments);
            var rel = Environment.ExpandEnvironmentVariables(app?.Base ?? "appsettings.json");
            basePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(descriptorPath)!, rel));
        }

        var userPath = Full(o.UserPath) ?? Full(app?.UserFile)
            ?? (appName != null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName, DefaultUserFileName)
                : null)
            ?? throw new ConfigEditorException("User-Datei unbekannt: --user oder --app angeben (oder app.userFile im Descriptor).", ExitCodes.BadArguments);

        var machinePath = Full(o.MachinePath) ?? Full(app?.MachineFile)
            ?? (appName != null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), appName, DefaultMachineFileName)
                : null);

        if (string.Equals(userPath, basePath, StringComparison.OrdinalIgnoreCase))
            throw new ConfigEditorException("User-Datei und Basisdatei sind dieselbe Datei, die Basis wird nie geschrieben.", ExitCodes.BadArguments);

        return (new ConfigPaths(basePath, userPath, machinePath, descriptorPath, appName), descriptor);
    }

    private static string? Full(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
}
