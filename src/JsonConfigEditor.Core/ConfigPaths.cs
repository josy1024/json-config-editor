namespace JsonConfigEditor.Core;

/// <summary>Aufgelöste Dateipfade (Umgebungsvariablen expandiert, absolut).</summary>
public sealed record ConfigPaths(string BaseFile, string UserFile, string? MachineFile, string? DescriptorFile, string? AppName)
{
    public const string DefaultUserFileName = "appsettings.local.json";
    public const string DefaultMachineFileName = "appsettings.json";

    /// <summary>Descriptor lesen (falls angegeben oder neben der Basisdatei gefunden) und Pfade auflösen.</summary>
    public static (ConfigPaths Paths, EditorDescriptor? Descriptor) Resolve(ConfigEditorOptions o)
    {
        string? descriptorPath = SafeFile.FullPath(o.DescriptorPath, what: "Descriptor-Pfad");
        var explicitBase = SafeFile.FullPath(o.BasePath, what: "Basisdatei");
        if (descriptorPath == null && explicitBase != null)
        {
            // appsettings.json → appsettings.editor.json daneben
            var sibling = Path.Combine(Path.GetDirectoryName(explicitBase)!, Path.GetFileNameWithoutExtension(explicitBase) + ".editor.json");
            if (File.Exists(sibling)) descriptorPath = sibling;
        }
        if (o.DescriptorPath != null)
        {
            if (Directory.Exists(descriptorPath))
                throw new ConfigEditorException($"Descriptor-Pfad ist ein Ordner: {descriptorPath}", ExitCodes.BadArguments);
            if (!File.Exists(descriptorPath))
                throw new ConfigEditorException($"Descriptor nicht gefunden: {descriptorPath}", ExitCodes.BadArguments);
        }

        var descriptor = descriptorPath != null ? EditorDescriptor.Load(descriptorPath) : null;
        var app = descriptor?.App;
        var appName = Clean(o.AppName) ?? Clean(app?.Name);
        if (appName != null && appName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ConfigEditorException($"App-Name \"{appName}\" taugt nicht als Ordnername.", ExitCodes.BadArguments);

        string? basePath = explicitBase;
        if (basePath == null)
        {
            if (descriptorPath == null)
                throw new ConfigEditorException("Keine Basisdatei: Descriptor-Pfad oder --base angeben.", ExitCodes.BadArguments);
            basePath = SafeFile.FullPath(app?.Base ?? "appsettings.json", Path.GetDirectoryName(descriptorPath), "app.base im Descriptor")!;
        }

        var userPath = SafeFile.FullPath(o.UserPath, what: "User-Datei") ?? SafeFile.FullPath(app?.UserFile, what: "app.userFile im Descriptor")
            ?? (appName != null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName, DefaultUserFileName)
                : null)
            ?? throw new ConfigEditorException("User-Datei unbekannt: --user oder --app angeben (oder app.userFile im Descriptor).", ExitCodes.BadArguments);

        var machinePath = SafeFile.FullPath(o.MachinePath, what: "Machine-Datei") ?? SafeFile.FullPath(app?.MachineFile, what: "app.machineFile im Descriptor")
            ?? (appName != null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), appName, DefaultMachineFileName)
                : null);

        if (Same(userPath, basePath))
            throw new ConfigEditorException("User-Datei und Basisdatei sind dieselbe Datei, die Basis wird nie geschrieben.", ExitCodes.BadArguments);
        if (Same(userPath, machinePath))
            throw new ConfigEditorException("User-Datei und Machine-Datei sind dieselbe Datei.", ExitCodes.BadArguments);
        if (Same(userPath, descriptorPath))
            throw new ConfigEditorException("User-Datei und Descriptor sind dieselbe Datei.", ExitCodes.BadArguments);
        if (Same(basePath, descriptorPath))
            throw new ConfigEditorException("Basisdatei und Descriptor sind dieselbe Datei. Im Descriptor zeigt app.base auf die appsettings.json.", ExitCodes.BadArguments);

        return (new ConfigPaths(basePath, userPath, machinePath, descriptorPath, appName), descriptor);
    }

    private static bool Same(string? a, string? b) => a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
