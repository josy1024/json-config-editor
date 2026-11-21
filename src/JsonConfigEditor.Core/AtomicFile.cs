using System.Collections.Concurrent;
using System.Text;

namespace JsonConfigEditor.Core;

/// <summary>
/// Erst tmp schreiben und auf die Platte zwingen, dann umbenennen. Leser (die App mit FileSystemWatcher)
/// sehen nie eine halbe Datei, und nach Stromausfall liegt entweder die alte oder die neue Fassung da.
/// Hat gerade jemand die Datei offen (Virenscanner, App liest), ein paar Mal mit kurzer Pause nochmal.
/// </summary>
public static class AtomicFile
{
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static void WriteAllText(string path, string content, int attempts = 10)
    {
        var gate = Locks.GetOrAdd(Path.GetFullPath(path), _ => new object());
        lock (gate)
        {
            var tmp = $"{path}.{Environment.ProcessId}.{Environment.CurrentManagedThreadId}.tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(Utf8NoBom.GetBytes(content));
                    fs.Flush(flushToDisk: true); // sonst kann nach Stromausfall eine leere Datei übrig bleiben
                }
                for (var i = 1; ; i++)
                {
                    try
                    {
                        File.Move(tmp, path, overwrite: true);
                        return;
                    }
                    catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && i < attempts)
                    {
                        Thread.Sleep(20 * i);
                    }
                }
            }
            catch
            {
                // auch bei voller Platte oder fehlendem Recht keine halbe tmp liegen lassen
                try { File.Delete(tmp); } catch (Exception) { /* egal */ }
                throw;
            }
        }
    }

    /// <summary>
    /// Übrig gebliebene tmp-Dateien von abgestürzten Läufen neben <paramref name="path"/> löschen
    /// (eigene Muster, älter als <paramref name="minAge"/>). Fehler werden ignoriert.
    /// </summary>
    /// <returns>Anzahl gelöschter Dateien.</returns>
    public static int CleanupStale(string path, TimeSpan? minAge = null)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
        var cutoff = DateTime.UtcNow - (minAge ?? TimeSpan.FromDays(1));
        var count = 0;
        try
        {
            var stale = Directory.EnumerateFiles(dir, Path.GetFileName(path) + ".*.tmp")
                .Concat(Directory.EnumerateFiles(dir, ".jce-*.tmp"));
            foreach (var file in stale)
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) > cutoff) continue; // läuft evtl. gerade
                    File.Delete(file);
                    count++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* gesperrt, nächstes Mal */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Ordner nicht lesbar */ }
        if (count > 0) Log.Info($"{count} alte tmp-Datei(en) in {dir} gelöscht");
        return count;
    }
}
