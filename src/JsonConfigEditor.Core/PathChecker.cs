using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace JsonConfigEditor.Core;

public enum PathSeverity { Ok, Info, Warning, Error }

public sealed record PathCheckResult(PathSeverity Severity, string Message, string? Expanded);

/// <summary>Pfad prüfen: gültig, existiert, beschreibbar, liegt in OneDrive/SharePoint.</summary>
public static partial class PathChecker
{
    public static PathCheckResult Check(ConfigField f, string? raw, string? baseDir) =>
        Check(raw, f.PathKind, f.MustExist, f.ExpandEnv, f.WarnCloudFolder, f.RelativeToBase ? baseDir : null);

    public static PathCheckResult Check(string? raw, PathKind kind, bool mustExist, bool expandEnv, bool warnCloud, string? relativeTo)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new(PathSeverity.Info, "leer", null);

        var expanded = expandEnv ? Environment.ExpandEnvironmentVariables(raw.Trim()) : raw.Trim();
        if (UnresolvedVar().Match(expanded) is { Success: true } m)
            return new(PathSeverity.Warning, $"Variable {m.Value} ist nicht gesetzt", expanded);
        if (expanded.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || HasBadNameChars(expanded))
            return new(PathSeverity.Error, "ungültige Zeichen im Pfad", expanded);

        string full;
        try
        {
            full = Path.IsPathRooted(expanded) || relativeTo == null
                ? Path.GetFullPath(expanded)
                : Path.GetFullPath(Path.Combine(relativeTo, expanded));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(PathSeverity.Error, "ungültiger Pfad", expanded);
        }
        if (!Path.IsPathRooted(expanded) && relativeTo == null && kind == PathKind.Folder)
            return Cloud(new(PathSeverity.Warning, "relativer Pfad, hängt vom Startordner der App ab", full), full, warnCloud);

        PathCheckResult result;
        if (kind == PathKind.Folder)
        {
            if (File.Exists(full)) return new(PathSeverity.Error, "das ist eine Datei, kein Ordner", full);
            if (Directory.Exists(full))
                result = IsWritable(full)
                    ? new(PathSeverity.Ok, "existiert, beschreibbar", full)
                    : new(PathSeverity.Warning, "existiert, aber nicht beschreibbar", full);
            else
                result = mustExist
                    ? new(PathSeverity.Error, "Ordner existiert nicht", full)
                    : new(PathSeverity.Warning, "fehlt, wird von der App angelegt", full);
        }
        else
        {
            if (Directory.Exists(full)) return new(PathSeverity.Error, "das ist ein Ordner, keine Datei", full);
            result = File.Exists(full)
                ? new(PathSeverity.Ok, "existiert", full)
                : mustExist
                    ? new(PathSeverity.Error, "Datei existiert nicht", full)
                    : new(PathSeverity.Warning, "Datei fehlt", full);
        }
        return Cloud(result, full, warnCloud);
    }

    private static PathCheckResult Cloud(PathCheckResult r, string full, bool warnCloud)
    {
        if (!warnCloud || r.Severity == PathSeverity.Error) return r;
        var root = CloudFolders.Find(full);
        return root == null ? r : r with { Severity = PathSeverity.Warning, Message = $"liegt in OneDrive/SharePoint ({root}), wird in die Cloud synchronisiert" };
    }

    private static bool HasBadNameChars(string path)
    {
        // Laufwerks-Doppelpunkt erlaubt, sonst keine : * ? " < > |
        var rest = path.Length >= 2 && path[1] == ':' ? path[2..] : path;
        if (rest.StartsWith(@"\\?\", StringComparison.Ordinal)) return false;
        return rest.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0;
    }

    private static bool IsWritable(string dir)
    {
        var probe = Path.Combine(dir, $".jce-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [GeneratedRegex("%[A-Za-z_][A-Za-z0-9_()]*%")]
    private static partial Regex UnresolvedVar();
}

/// <summary>OneDrive- und SharePoint-Ordner des Users (Umgebung + Registry).</summary>
public static class CloudFolders
{
    private static readonly Lazy<IReadOnlyList<string>> Default = new(Detect);

    /// <summary>Für Tests austauschbar.</summary>
    internal static Func<IReadOnlyList<string>> Provider { get; set; } = () => Default.Value;

    public static IReadOnlyList<string> Roots => Provider();

    /// <summary>Cloud-Wurzel, in der der Pfad liegt, sonst null.</summary>
    public static string? Find(string fullPath)
    {
        foreach (var root in Roots)
        {
            var r = root.TrimEnd('\\', '/');
            if (fullPath.Equals(r, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return r;
        }
        return null;
    }

    private static IReadOnlyList<string> Detect()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
        {
            var p = Environment.GetEnvironmentVariable(v);
            if (!string.IsNullOrWhiteSpace(p)) roots.Add(p);
        }
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
                foreach (var name in accounts?.GetSubKeyNames() ?? [])
                {
                    using var k = accounts!.OpenSubKey(name);
                    if (k?.GetValue("UserFolder") is string uf && uf.Length > 0) roots.Add(uf);
                }
                // SharePoint-Bibliotheken, die per OneDrive synchronisiert werden
                using var providers = Registry.CurrentUser.OpenSubKey(@"Software\SyncEngines\Providers\OneDrive");
                foreach (var name in providers?.GetSubKeyNames() ?? [])
                {
                    using var k = providers!.OpenSubKey(name);
                    if (k?.GetValue("MountPoint") is string mp && mp.Length > 0) roots.Add(mp);
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Registry nicht lesbar: dann eben nur die Umgebungsvariablen
            }
        }
        return roots.ToList();
    }
}
