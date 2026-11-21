using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace JsonConfigEditor.Core;

/// <summary>
/// Ein Fenster pro User-Datei. Zweiter Aufruf schickt dem ersten per Named Pipe den gewünschten
/// Abschnitt, der holt sich nach vorne und antwortet "ok". Kommt keine Antwort, hängt der erste Editor:
/// dann sagt der zweite das, statt sich still zu beenden.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string Ok = "ok";
    private const string Busy = "busy";

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstance(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    /// <summary>Wie lange der zweite Aufruf auf den ersten wartet (Verbinden und Antwort je).</summary>
    public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// null, wenn schon ein Editor für diese Datei läuft. <paramref name="problem"/> ist dann null, wenn er
    /// sich nach vorne geholt hat, sonst der Grund (hängt, reagiert nicht).
    /// </summary>
    public static SingleInstance? TryAcquire(string userFile, string? section, out string? problem)
    {
        problem = null;
        var id = Id(userFile);
        var mutex = new Mutex(initiallyOwned: true, $@"Local\JsonConfigEditor-{id}", out var created);
        if (created) return new SingleInstance(mutex, PipeName(id));

        mutex.Dispose();
        problem = Notify(PipeName(id), section);
        return null;
    }

    /// <summary>Ersten Editor benachrichtigen. null = hat sich gemeldet, sonst Fehlertext.</summary>
    private static string? Notify(string pipeName, string? section)
    {
        const string hangs = "Für diese Einstellungen ist schon ein Editor offen, er reagiert aber nicht. "
                           + "Kurz warten und nochmal versuchen, sonst \"JsonConfigEditor\" im Task-Manager beenden.";
        var ms = (int)Timeout.TotalMilliseconds;
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            client.Connect(ms);
            // Schreiben und Lesen mit Abbruch: hängt der erste Editor, blockiert sonst schon das Schreiben
            // (Pipe ohne Puffer wartet, bis die Gegenseite liest)
            using var cts = new CancellationTokenSource(Timeout);
            var request = new UTF8Encoding(false).GetBytes((section ?? "") + "\n");
            client.WriteAsync(request, cts.Token).AsTask().GetAwaiter().GetResult();
            using var r = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            var answer = r.ReadLineAsync(cts.Token).AsTask().GetAwaiter().GetResult();
            return answer == Ok ? null : hangs;
        }
        catch (UnauthorizedAccessException)
        {
            // älterer Editor (≤ 0.3) hört nur in eine Richtung: wie früher nur schicken, ohne Antwort
            return NotifyOneWay(pipeName, section, ms) ? null : hangs;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException)
        {
            Log.Warn($"Erster Editor ({pipeName}) antwortet nicht: {ex.Message}");
            return hangs;
        }
    }

    private static bool NotifyOneWay(string pipeName, string? section, int ms)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(ms);
            using var w = new StreamWriter(client, new UTF8Encoding(false));
            w.WriteLine(section ?? "");
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Auf Aufrufe weiterer Instanzen hören. <paramref name="activate"/> kommt auf einem Hintergrund-Thread
    /// und gibt zurück, ob das Fenster reagiert hat. Fehler im Callback beenden das Zuhören nicht.
    /// </summary>
    public void Listen(Func<string?, bool> activate)
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_cts.Token);
                    using var r = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                    var line = await r.ReadLineAsync(_cts.Token);

                    bool ok;
                    try { ok = activate(string.IsNullOrWhiteSpace(line) ? null : line.Trim()); }
                    catch (Exception ex)
                    {
                        Log.Warn("Fenster nach vorne holen fehlgeschlagen", ex);
                        ok = false;
                    }

                    await using var w = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true);
                    await w.WriteLineAsync(ok ? Ok : Busy);
                    await w.FlushAsync(_cts.Token);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    // Client weg, Pipe kaputt …: weiter hören, aber nicht im Kreis drehen
                    if (ex is not IOException) Log.Warn("Single-Instance-Pipe", ex);
                    try { await Task.Delay(200, _cts.Token); } catch (OperationCanceledException) { return; }
                }
            }
        });
    }

    private static string PipeName(string id) => $"JsonConfigEditor-{id}";

    private static string Id(string userFile)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(userFile).ToUpperInvariant()));
        return Convert.ToHexString(bytes, 0, 8);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* nicht unser Thread */ }
        _mutex.Dispose();
    }
}
