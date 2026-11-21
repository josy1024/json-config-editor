using System.Collections.Concurrent;
using System.Text;

namespace JsonConfigEditor.Core;

/// <summary>
/// Erst tmp schreiben, dann umbenennen. Leser (die App mit FileSystemWatcher) sehen nie eine halbe Datei.
/// Hat gerade jemand die Datei offen (Virenscanner, App liest), ein paar Mal mit kurzer Pause nochmal.
/// </summary>
public static class AtomicFile
{
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);

    public static void WriteAllText(string path, string content, int attempts = 10)
    {
        var gate = Locks.GetOrAdd(Path.GetFullPath(path), _ => new object());
        lock (gate)
        {
            var tmp = $"{path}.{Environment.ProcessId}.{Environment.CurrentManagedThreadId}.tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
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
                catch
                {
                    try { File.Delete(tmp); } catch (Exception) { /* egal */ }
                    throw;
                }
            }
        }
    }
}
