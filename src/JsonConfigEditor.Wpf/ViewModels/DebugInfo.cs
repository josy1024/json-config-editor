using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using JsonConfigEditor.Core;

namespace JsonConfigEditor.Wpf.ViewModels;

/// <summary>
/// Text für das F3-Fenster: Aufruf, Version, welche JSON-Dateien wirklich benutzt werden, Sperren, Warnungen.
/// Gedacht zum Kopieren in ein Ticket. Werte aus der User-Datei stehen drin (die sieht der User eh), sonst keine.
/// </summary>
internal static class DebugInfo
{
    private const int MaxUserFileChars = 8000;

    public static string Build(EditorViewModel vm)
    {
        var sb = new StringBuilder();
        void Line(string label, string? value) => sb.Append(label.PadRight(14)).AppendLine(value ?? "–");

        sb.AppendLine("== json-config-editor ==");
        Line("Version", AppInfo.Version);
        Line("Projekt", AppInfo.RepoUrl);
        Line(".NET", RuntimeInformation.FrameworkDescription);
        Line("Windows", $"{Environment.OSVersion.VersionString} ({RuntimeInformation.OSArchitecture})");
        Line("Prozess", $"{Environment.ProcessId} {Environment.ProcessPath}");
        Line("Zeit", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        sb.AppendLine();

        sb.AppendLine("== Aufruf ==");
        sb.AppendLine(Environment.CommandLine);
        var o = vm.Options;
        Line("Descriptor", o.DescriptorPath);
        if (o.BasePath != null) Line("--base", o.BasePath);
        if (o.UserPath != null) Line("--user", o.UserPath);
        if (o.MachinePath != null) Line("--machine", o.MachinePath);
        if (o.AppName != null) Line("--app", o.AppName);
        if (o.InitialSection != null) Line("--section", o.InitialSection);
        if (o.ReadOnly) Line("--readonly", "ja");
        Line("Arbeitsordner", SafeCurrentDirectory());
        sb.AppendLine();

        sb.AppendLine("== JSON-Dateien (unten gewinnt) ==");
        ConfigPaths? paths = vm.HasLoadError ? TryResolve(o) : vm.Document.Paths;
        if (paths == null)
        {
            sb.AppendLine("Pfade nicht auflösbar.");
        }
        else
        {
            Line("App", paths.AppName);
            Line("Descriptor", Describe(paths.DescriptorFile));
            Line("1 Basis", Describe(paths.BaseFile));
            Line("2 Machine", Describe(paths.MachineFile));
            Line("3 User", Describe(paths.UserFile));
            Line("4 Locked", "Abschnitt \"Locked\" in der Machine-Datei");
        }
        sb.AppendLine();

        sb.AppendLine("== Zustand ==");
        if (vm.HasLoadError)
        {
            Line("Ladefehler", $"{vm.LoadError} (Exit {vm.LoadErrorCode})");
        }
        else
        {
            var d = vm.Document;
            Line("Felder", $"{d.Fields.Count} in {d.Sections.Count} Abschnitten, {d.Favorites.Count} Favoriten");
            Line("Geändert", $"{d.Fields.Count(f => f.Origin == ValueOrigin.User)} eigene Werte");
            var locked = d.Fields.Where(f => f.IsLocked).Select(f => f.Key).ToList();
            Line("IT gesperrt", locked.Count == 0 ? "keine" : string.Join(", ", locked));
            var it = d.Fields.Count(f => f.Origin == ValueOrigin.Machine);
            if (it > 0) Line("IT-Vorgaben", $"{it} Werte aus der Machine-Datei");
            Line("Schreiben", d.ReadOnly ? "nur Ansicht: " + (d.WriteBlockedReason ?? "--readonly") : "ok");
            if (d.UserFileError != null) Line("User-Datei", "KAPUTT: " + d.UserFileError);
            if (d.MachineFileError != null) Line("Machine", "ignoriert: " + d.MachineFileError);
            foreach (var w in d.Warnings) Line("Hinweis", w);
        }
        Line("Log", Log.Enabled ? Log.CurrentFile : "aus (eingebettete DLL)");
        sb.AppendLine();

        if (paths != null && File.Exists(paths.UserFile))
        {
            sb.AppendLine($"== Inhalt {Path.GetFileName(paths.UserFile)} ==");
            try
            {
                var text = SafeFile.ReadText(paths.UserFile, "User-Datei");
                sb.AppendLine(text.Length > MaxUserFileChars ? text[..MaxUserFileChars] + $"\n… ({text.Length - MaxUserFileChars} Zeichen mehr)" : text.TrimEnd());
            }
            catch (ConfigEditorException ex) { sb.AppendLine(ex.Message); }
        }
        return sb.ToString().TrimEnd();
    }

    private static ConfigPaths? TryResolve(ConfigEditorOptions o)
    {
        try { return ConfigPaths.Resolve(o).Paths; }
        catch (ConfigEditorException) { return null; }
    }

    private static string Describe(string? path)
    {
        if (path == null) return "–";
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists
                ? $"{path}  ({fi.Length:N0} Bytes, {fi.LastWriteTime:yyyy-MM-dd HH:mm})"
                : $"{path}  (fehlt)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return $"{path}  ({ex.Message})";
        }
    }

    private static string? SafeCurrentDirectory()
    {
        try { return Environment.CurrentDirectory; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
