using System.Text.Json.Nodes;
using JsonConfigEditor.Core;

namespace JsonConfigEditor.Tests;

public class LayerAndWriterTests
{
    private const string Base = """
        {
          // Live-Text an
          "Live": { "Enabled": true, "ChunkSeconds": 7, "Model": "base.bin" },
          "Output": { "RootPath": "", "HistoryCount": 10 }
        }
        """;

    [Fact]
    public void Ebenen_Base_Machine_User_Locked()
    {
        using var t = new TempDir().WithBase(Base)
            .WithMachine("""{ "Output": { "HistoryCount": 20 }, "Live": { "Model": "it.bin" }, "Locked": { "Output": { "RootPath": "D:\\Aufnahmen" } } }""")
            .WithUser("""{ "Live": { "Model": "user.bin" }, "Output": { "RootPath": "C:\\x" } }""");
        var doc = t.Load();

        Assert.Equal(ValueOrigin.Default, doc.Find("Live:Enabled")!.Origin);
        Assert.Equal(ValueOrigin.Machine, doc.Find("Output:HistoryCount")!.Origin);
        Assert.Equal(20, (int)doc.Find("Output:HistoryCount")!.NumberValue!);
        Assert.Equal(ValueOrigin.User, doc.Find("Live:Model")!.Origin);
        Assert.Equal("user.bin", doc.Find("Live:Model")!.StringValue);
        var root = doc.Find("Output:RootPath")!;
        Assert.Equal(ValueOrigin.Locked, root.Origin);
        Assert.Equal(@"D:\Aufnahmen", root.StringValue);
        Assert.True(root.IsLocked);
        Assert.False(root.IsEditable);
    }

    [Fact]
    public void Sparse_nur_Abweichung_landet_in_User_Datei_zurueckstellen_entfernt_Key()
    {
        using var t = new TempDir().WithBase(Base);
        var doc = t.Load();

        ConfigWriter.Set(doc, "Live:Enabled", JsonValue.Create(false));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "Live": { "Enabled": false } }"""), JsonNode.Parse(t.UserText)));
        Assert.Equal(ValueOrigin.User, doc.Find("Live:Enabled")!.Origin);
        Assert.True(doc.Find("Live:Enabled")!.IsChanged);

        ConfigWriter.Set(doc, "Live:Enabled", JsonValue.Create(true));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("{}"), JsonNode.Parse(t.UserText)));
        Assert.Equal(ValueOrigin.Default, doc.Find("Live:Enabled")!.Origin);
    }

    [Fact]
    public void Zahl_gleich_Default_auch_als_7_0()
    {
        using var t = new TempDir().WithBase(Base).WithUser("""{ "Live": { "ChunkSeconds": 9 } }""");
        var doc = t.Load();
        ConfigWriter.Set(doc, "Live:ChunkSeconds", JsonValue.Create(7.0));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("{}"), JsonNode.Parse(t.UserText)));
    }

    [Fact]
    public void Default_ist_Machine_Wert_nicht_Base()
    {
        using var t = new TempDir().WithBase(Base).WithMachine("""{ "Output": { "HistoryCount": 20 } }""");
        var doc = t.Load();
        ConfigWriter.Set(doc, "Output:HistoryCount", JsonValue.Create(10));
        // 10 ist Werks-Default, aber IT sagt 20: also eigener Wert
        Assert.Contains("\"HistoryCount\": 10", t.UserText);
        ConfigWriter.Set(doc, "Output:HistoryCount", JsonValue.Create(20));
        Assert.DoesNotContain("HistoryCount", t.UserText);
    }

    [Fact]
    public void Vorhandene_Metadaten_Arrays_und_unbekannte_Keys_bleiben()
    {
        var user = """
            {
              "Audio": {
                "_hinweis": "Reihenfolge = Priorität.",
                "PreferredDevices": [ { "Name": "Jabra", "Id": "{0.0.1}", "LastSeen": "2026-11-14 17:42" } ]
              },
              "Models": { "_verfuegbar": [ "a.bin", "b.bin" ], "Backend": "auto" },
              "Fremd": { "X": 1 }
            }
            """;
        using var t = new TempDir().WithBase(Base).WithUser(user);
        var doc = t.Load();

        ConfigWriter.Set(doc, "Live:Enabled", JsonValue.Create(false));

        var after = JsonNode.Parse(t.UserText)!.AsObject();
        var expected = JsonNode.Parse(user)!.AsObject();
        expected["Live"] = new JsonObject { ["Enabled"] = false };
        Assert.True(JsonNode.DeepEquals(expected, after));
        // Reihenfolge: Live kommt hinten dazu
        Assert.Equal(["Audio", "Models", "Fremd", "Live"], after.Select(p => p.Key));
    }

    [Theory]
    // keine eigene Liste: Descriptor
    [InlineData(null, null, "A,B,C", "A,B,C")]
    // alte Datei ohne Merker: neuer App-Favorit A kommt an seine Stelle (vorne)
    [InlineData("B,C", null, "A,B,C", "A,B,C")]
    // Merker kennt A, B, C: User hat A abgewählt, bleibt weg. Neues D kommt hinter C
    [InlineData("C,B", "A,B,C", "A,B,C,D", "C,D,B")]
    // eigene Favoriten bleiben in eigener Reihenfolge, nichts Neues
    [InlineData("X,B", "A,B", "A,B", "X,B")]
    public void Neue_App_Favoriten_kommen_auch_bei_eigener_Liste_dazu(string? user, string? known, string descriptor, string expected)
    {
        static List<string>? L(string? s) => s?.Split(',').ToList();
        Assert.Equal(L(expected), ConfigDocument.EffectiveFavorites(L(user), L(known), L(descriptor)!));
    }

    [Fact]
    public void Eigene_Favoriten_merken_sich_die_App_Favoriten()
    {
        using var t = new TempDir().WithBase(Base)
            .WithDescriptor("""{ "favorites": ["Output:RootPath", "Live:Model"] }""")
            .WithUser("""{ "_jsonConfigEditor": { "favorites": ["Live:Model"] } }""");
        var doc = t.Load();
        // alte Datei ohne Merker: RootPath (neu in der App) kommt dazu
        Assert.Equal(["Output:RootPath", "Live:Model"], doc.Favorites.Select(f => f.Key));

        // User wählt RootPath ab: Merker wird geschrieben, beim nächsten Laden bleibt es weg
        ConfigWriter.SetFavorites(doc, ["Live:Model"]);
        var meta = JsonNode.Parse(t.UserText)!["_jsonConfigEditor"]!;
        Assert.Equal(["Output:RootPath", "Live:Model"], meta["descriptorFavorites"]!.AsArray().Select(v => (string)v!).ToArray());
        Assert.Equal(["Live:Model"], t.Load().Favorites.Select(f => f.Key));

        // App nimmt Live:Enabled neu dazu: taucht auf, RootPath bleibt abgewählt
        t.WithDescriptor("""{ "favorites": ["Output:RootPath", "Live:Enabled", "Live:Model"] }""");
        Assert.Equal(["Live:Enabled", "Live:Model"], t.Load().Favorites.Select(f => f.Key));
    }

    [Fact]
    public void Favoriten_werden_in_User_Datei_gespeichert_und_beim_Reload_gelesen()
    {
        var user = """
            {
              "Live": { "Model": "user.bin" },
              "_jsonConfigEditor": { "favorites": ["Live:Enabled"], "other": "keep" },
              "_otherAppMetadata": { "keep": true }
            }
            """;
        using var t = new TempDir().WithBase(Base).WithUser(user);
        var doc = t.Load();

        Assert.Equal(["Live:Enabled"], doc.Favorites.Select(f => f.Key));
        ConfigWriter.SetFavorites(doc, ["Live:ChunkSeconds", "Output:RootPath"]);

        var saved = JsonNode.Parse(t.UserText)!.AsObject();
        Assert.Equal("user.bin", (string)saved["Live"]!["Model"]!);
        Assert.True((bool)saved["_otherAppMetadata"]!["keep"]!);
        Assert.Equal(["Live:ChunkSeconds", "Output:RootPath"],
            saved["_jsonConfigEditor"]!["favorites"]!.AsArray().Select(v => (string)v!).ToArray());
        Assert.Equal("keep", (string)saved["_jsonConfigEditor"]!["other"]!);
        Assert.Equal(["Live:ChunkSeconds", "Output:RootPath"], doc.Favorites.Select(f => f.Key));

        Assert.False(doc.Reload());
        Assert.Equal(["Live:ChunkSeconds", "Output:RootPath"], doc.Favorites.Select(f => f.Key));
        t.WithUser("""{ "_jsonConfigEditor": { "favorites": [] } }""");
        Assert.False(doc.Reload());
        Assert.Empty(doc.Favorites);
    }

    [Fact]
    public void Reset_entfernt_Key_und_leeren_Abschnitt()
    {
        using var t = new TempDir().WithBase(Base).WithUser("""{ "Live": { "Model": "x.bin" }, "Output": { "HistoryCount": 3 } }""");
        var doc = t.Load();
        ConfigWriter.Reset(doc, "Live:Model");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "Output": { "HistoryCount": 3 } }"""), JsonNode.Parse(t.UserText)));
        Assert.Equal("base.bin", doc.Find("Live:Model")!.StringValue);
    }

    [Fact]
    public void Gesperrtes_Feld_kann_nicht_geschrieben_werden()
    {
        using var t = new TempDir().WithBase(Base)
            .WithMachine("""{ "Locked": { "Live": { "Enabled": false } } }""")
            .WithUser("""{ "Output": { "HistoryCount": 3 } }""");
        var doc = t.Load();
        var before = t.UserText;
        Assert.Throws<FieldLockedException>(() => ConfigWriter.Set(doc, "Live:Enabled", JsonValue.Create(true)));
        Assert.Equal(before, t.UserText);
    }

    [Fact]
    public void Kaputte_User_Datei_wird_nie_ueberschrieben()
    {
        using var t = new TempDir().WithBase(Base).WithUser("""{ "Live": { "Enabled": fals """);
        var doc = t.Load();
        Assert.NotNull(doc.UserFileError);
        var before = t.UserText;
        Assert.Throws<ConfigEditorException>(() => ConfigWriter.Set(doc, "Live:Enabled", JsonValue.Create(false)));
        Assert.Throws<ConfigEditorException>(() => ConfigWriter.SetFavorites(doc, []));
        Assert.Equal(before, t.UserText);

        var backup = ConfigWriter.BackupBrokenUserFile(doc);
        Assert.True(File.Exists(backup));
        Assert.Null(doc.UserFileError);
        ConfigWriter.Set(doc, "Live:Enabled", JsonValue.Create(false));
        Assert.Contains("\"Enabled\": false", t.UserText);
    }

    [Fact]
    public void Externe_Aenderung_wird_mit_Reload_uebernommen()
    {
        using var t = new TempDir().WithBase(Base);
        var doc = t.Load();
        t.WithUser("""{ "Live": { "Model": "turbo.bin" } }""");
        Assert.False(doc.Reload());
        Assert.Equal("turbo.bin", doc.Find("Live:Model")!.StringValue);
        t.WithUser("""{ "Neu": { "X": 1 } }""");
        Assert.True(doc.Reload());
    }

    [Fact]
    public void User_Ordner_wird_angelegt_Schreiben_ist_case_insensitive()
    {
        using var t = new TempDir().WithBase(Base).WithUser("""{ "live": { "enabled": true, "Model": "m" } }""");
        var doc = t.Load();
        Assert.Equal(ValueOrigin.User, doc.Find("Live:Enabled")!.Origin);
        ConfigWriter.Set(doc, "Live:Enabled", JsonValue.Create(false));
        var after = JsonNode.Parse(t.UserText)!.AsObject();
        Assert.Single(after);
        Assert.False((bool)after["live"]!["enabled"]!);
    }

    [Fact]
    public void ReadOnly_Editor_schreibt_nicht()
    {
        using var t = new TempDir().WithBase(Base);
        var o = t.Options;
        o.ReadOnly = true;
        var doc = ConfigDocument.Load(o);
        Assert.Throws<InvalidOperationException>(() => ConfigWriter.Set(doc, "Live:Enabled", JsonValue.Create(false)));
        Assert.Throws<InvalidOperationException>(() => ConfigWriter.SetFavorites(doc, ["Live:Enabled"]));
        Assert.False(File.Exists(t.User));
    }
}
