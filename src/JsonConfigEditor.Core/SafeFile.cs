using System.Text;

namespace JsonConfigEditor.Core;

/// <summary>
/// Dateizugriffe mit klaren Fehlermeldungen statt Absturz: Ordner statt Datei, zu groß,
/// binär, gerade gesperrt (kurz nochmal versuchen), kein Zugriff.
/// </summary>
public static class SafeFile
{
    /// <summary>Config-Dateien sind ein paar KB. Alles über 5 MB ist fast sicher die falsche Datei.</summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    private static readonly char[] InvalidChars = [.. Path.GetInvalidPathChars(), '<', '>', '"', '|', '?', '*'];

    /// <summary>Text lesen, auch wenn die App die Datei gerade offen hat. Wirft <see cref="ConfigEditorException"/>.</summary>
    public static string ReadText(string path, string what, int exitCode = ExitCodes.BrokenFile)
    {
        try
        {
            if (Directory.Exists(path))
                throw new ConfigEditorException($"{what} {path} ist ein Ordner, keine Datei.", exitCode);
            var info = new FileInfo(path);
            if (!info.Exists)
                throw new ConfigEditorException($"{what} nicht gefunden: {path}", exitCode);
            if (info.Length > MaxBytes)
                throw new ConfigEditorException(
                    $"{what} {path} ist {info.Length / 1024.0 / 1024.0:0.#} MB groß, erlaubt sind {MaxBytes / 1024 / 1024} MB. Falsche Datei?", exitCode);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    // ReadWrite|Delete: die App darf die Datei gleichzeitig offen haben oder ersetzen
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    var text = reader.ReadToEnd();
                    if (text.Contains('\0'))
                        throw new ConfigEditorException($"{what} {path} ist keine Textdatei.", exitCode);
                    return text;
                }
                catch (IOException) when (attempt < 4 && File.Exists(path))
                {
                    Thread.Sleep(50 * attempt); // gerade gesperrt (Virenscanner, App schreibt)
                }
            }
        }
        catch (ConfigEditorException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            throw new ConfigEditorException($"{what} {path} nicht lesbar: {ex.Message}", exitCode, ex);
        }
    }

    /// <summary>
    /// Kann die User-Datei geschrieben werden? null = ja, sonst der Grund.
    /// Legt den Ordner an, wenn er fehlt (das täte der erste Speichervorgang sowieso).
    /// </summary>
    public static string? CheckWritable(string file)
    {
        try
        {
            if (Directory.Exists(file)) return $"{file} ist ein Ordner, keine Datei.";
            var dir = Path.GetDirectoryName(file);
            if (string.IsNullOrEmpty(dir)) return $"{file}: kein Ordner im Pfad.";
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"Ordner {dir} kann nicht angelegt werden: {ex.Message}";
            }

            if (File.Exists(file) && File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly))
                return $"{file} ist schreibgeschützt.";

            // Probe: tmp anlegen geht = atomares Speichern (tmp + umbenennen) geht
            var probe = Path.Combine(dir, $".jce-probe-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return $"In {Path.GetDirectoryName(file) ?? file} kann nicht geschrieben werden: {ex.Message}";
        }
    }

    /// <summary>Pfad expandieren (%VAR%) und absolut machen. null bei leer, <see cref="ConfigEditorException"/> (Exit 3) bei ungültig.</summary>
    public static string? FullPath(string? path, string? relativeTo = null, string what = "Pfad")
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var raw = path.Trim().Trim('"').Trim();
        // vor dem Expandieren prüfen: ExpandEnvironmentVariables schneidet bei \0 einfach ab
        var p = raw.IndexOfAny(InvalidChars) >= 0 ? raw : Environment.ExpandEnvironmentVariables(raw);
        if (p.Length == 0) return null;
        // .NET prüft Zeichen nicht mehr selbst: sonst endet "C:\a<b" später als "nicht gefunden" oder IOException
        if (p.IndexOfAny(InvalidChars) >= 0 || p.IndexOf(':', p.StartsWith(@"\\?\", StringComparison.Ordinal) ? 6 : 2) >= 0)
            throw new ConfigEditorException($"{what} ungültig: \"{path}\" enthält unerlaubte Zeichen (< > \" | ? * : oder Steuerzeichen).", ExitCodes.BadArguments);
        try
        {
            return relativeTo != null && !Path.IsPathFullyQualified(p)
                ? Path.GetFullPath(Path.Combine(relativeTo, p))
                : Path.GetFullPath(p);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            throw new ConfigEditorException($"{what} ungültig: \"{path}\" ({ex.Message})", ExitCodes.BadArguments, ex);
        }
    }
}
