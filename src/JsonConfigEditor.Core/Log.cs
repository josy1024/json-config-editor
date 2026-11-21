namespace JsonConfigEditor.Core;

/// <summary>
/// Kleines Tageslog unter %LOCALAPPDATA%\JsonConfigEditor\logs, 7 Tage. Standardmäßig aus,
/// die EXE schaltet es ein (eine App, die die DLL einbettet, loggt selbst).
/// Geloggt werden Keys und Pfade, keine Werte. Fehler beim Loggen werden verschluckt.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();

    public static bool Enabled { get; set; }

    public static string Directory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JsonConfigEditor", "logs");

    public static int KeepDays { get; set; } = 7;

    public static string CurrentFile => Path.Combine(Directory, $"editor-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO ", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN ", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    /// <summary>Logs älter als <see cref="KeepDays"/> löschen.</summary>
    public static void Cleanup()
    {
        if (!Enabled) return;
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return;
            var limit = DateTime.Now.Date.AddDays(-KeepDays);
            foreach (var f in System.IO.Directory.EnumerateFiles(Directory, "editor-*.log"))
            {
                try { if (File.GetLastWriteTime(f) < limit) File.Delete(f); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void Write(string level, string message, Exception? ex)
    {
        if (!Enabled) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} [{Environment.ProcessId}] {message}";
        if (ex != null) line += Environment.NewLine + "    " + ex.ToString().Replace(Environment.NewLine, Environment.NewLine + "    ");
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                // mehrere Editor-Prozesse gleichzeitig: kurz nochmal versuchen
                for (var i = 0; i < 3; i++)
                {
                    try
                    {
                        using var fs = new FileStream(CurrentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                        using var w = new StreamWriter(fs);
                        w.WriteLine(line);
                        return;
                    }
                    catch (IOException) { Thread.Sleep(20); }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }
}
