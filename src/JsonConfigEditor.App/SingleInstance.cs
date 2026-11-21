using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace JsonConfigEditor.App;

/// <summary>
/// Ein Fenster pro User-Datei. Zweiter Aufruf schickt dem ersten per Named Pipe den gewünschten
/// Abschnitt, der holt sich dann nach vorne.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstance(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    /// <summary>null, wenn schon ein Editor für diese Datei läuft (dann wurde er schon benachrichtigt).</summary>
    public static SingleInstance? TryAcquire(string userFile, string? section)
    {
        var id = Id(userFile);
        var mutex = new Mutex(initiallyOwned: true, $@"Local\JsonConfigEditor-{id}", out var created);
        if (created) return new SingleInstance(mutex, $"JsonConfigEditor-{id}");

        mutex.Dispose();
        try
        {
            using var client = new NamedPipeClientStream(".", $"JsonConfigEditor-{id}", PipeDirection.Out);
            client.Connect(2000);
            using var w = new StreamWriter(client, new UTF8Encoding(false));
            w.WriteLine(section ?? "");
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            // erster Editor hängt oder schließt gerade: dann eben nicht nach vorne holen
        }
        return null;
    }

    /// <summary>Auf Aufrufe weiterer Instanzen hören. Callback kommt auf einem Hintergrund-Thread.</summary>
    public void Listen(Action<string?> activate)
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_cts.Token);
                    using var r = new StreamReader(server, Encoding.UTF8);
                    var line = await r.ReadLineAsync(_cts.Token);
                    activate(string.IsNullOrWhiteSpace(line) ? null : line.Trim());
                }
                catch (OperationCanceledException) { return; }
                catch (IOException) { /* Client weg, weiter hören */ }
            }
        });
    }

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
