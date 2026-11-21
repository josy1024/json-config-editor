using JsonConfigEditor.Core;

namespace JsonConfigEditor.Tests;

/// <summary>Eigener Ordner pro Test mit Base-, Machine- und User-Datei.</summary>
internal sealed class TempDir : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "jce-tests", Guid.NewGuid().ToString("N"));
    public string Base => Path.Combine(Root, "app", "appsettings.json");
    public string Descriptor => Path.Combine(Root, "app", "appsettings.editor.json");
    public string Machine => Path.Combine(Root, "machine", "appsettings.json");
    public string User => Path.Combine(Root, "user", "appsettings.local.json");

    public TempDir()
    {
        Directory.CreateDirectory(Path.Combine(Root, "app"));
        Directory.CreateDirectory(Path.Combine(Root, "machine"));
    }

    public TempDir WithBase(string json) { File.WriteAllText(Base, json); return this; }
    public TempDir WithPraxisBase() => WithBase(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "praxisrecorder.appsettings.json")));
    public TempDir WithMachine(string json) { File.WriteAllText(Machine, json); return this; }
    public TempDir WithDescriptor(string json) { File.WriteAllText(Descriptor, json); return this; }

    public TempDir WithUser(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(User)!);
        File.WriteAllText(User, json);
        return this;
    }

    public ConfigEditorOptions Options => new() { BasePath = Base, UserPath = User, MachinePath = Machine };

    public ConfigDocument Load() => ConfigDocument.Load(Options);

    public string UserText => File.ReadAllText(User);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (Exception) { /* egal */ }
    }
}
