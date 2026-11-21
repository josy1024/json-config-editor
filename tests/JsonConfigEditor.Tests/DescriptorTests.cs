using JsonConfigEditor.Core;

namespace JsonConfigEditor.Tests;

public class DescriptorTests
{
    private const string Descriptor = """
        {
          "app": { "name": "TestApp", "title": "Test Einstellungen", "base": "appsettings.json",
                   "userFile": "USER", "machineFile": "MACHINE" },
          "favorites": ["Recording:SegmentMinSeconds", "Live:ChunkSeconds", "Gibts:Nicht"],
          "sections": { "Live": { "label": "Live-Text", "order": 1 }, "Output": { "label": "Ausgabe", "order": 0 } },
          "fields": {
            "Output:RootPath": { "type": "path", "pathKind": "folder", "emptyMeans": "Dokumente\\X", "warnCloudFolder": true },
            "Recording:SegmentMinSeconds": { "advanced": true, "type": "number", "unit": "s" },
            "Recording:FlushSeconds": { "hidden": true },
            "Recording:MaxMinutes": { "min": 0, "zeroMeans": "unbegrenzt", "options": ["a"] },
            "Models:Backend": { "restartRequired": true, "foo": 1 }
          }
        }
        """;

    private static TempDir Setup()
    {
        var t = new TempDir().WithPraxisBase();
        t.WithDescriptor(Descriptor.Replace("USER", t.User.Replace("\\", "\\\\")).Replace("MACHINE", t.Machine.Replace("\\", "\\\\")));
        return t;
    }

    [Fact]
    public void Nur_Descriptor_Pfad_reicht()
    {
        using var t = Setup();
        var doc = ConfigDocument.Load(new ConfigEditorOptions { DescriptorPath = t.Descriptor });
        Assert.Equal(t.Base, doc.Paths.BaseFile);
        Assert.Equal(t.User, doc.Paths.UserFile);
        Assert.Equal("Test Einstellungen", doc.Title);
        Assert.Equal("TestApp", doc.Paths.AppName);
    }

    [Fact]
    public void Favoriten_in_Reihenfolge_Favorit_schlaegt_advanced()
    {
        using var t = Setup();
        var doc = ConfigDocument.Load(new ConfigEditorOptions { DescriptorPath = t.Descriptor });

        Assert.Equal(["Recording:SegmentMinSeconds", "Live:ChunkSeconds"], doc.Favorites.Select(f => f.Key));
        var seg = doc.Find("Recording:SegmentMinSeconds")!;
        Assert.True(seg.Advanced);
        Assert.True(seg.IsFavorite);
        Assert.Equal(FieldKind.Number, seg.Kind);
        Assert.Equal("s", seg.Unit);
        Assert.Contains(doc.Warnings, w => w.Contains("Gibts:Nicht"));
        Assert.Contains(doc.Warnings, w => w.Contains("SegmentMinSeconds") && w.Contains("Favorit gewinnt"));
    }

    [Fact]
    public void User_Favoriten_ueberschreiben_Descriptor_Favoriten_persistent()
    {
        using var t = Setup();
        var options = new ConfigEditorOptions { DescriptorPath = t.Descriptor };
        var doc = ConfigDocument.Load(options);

        ConfigWriter.SetFavorites(doc, ["Live:ChunkSeconds"]);

        Assert.Equal(["Live:ChunkSeconds"], doc.Favorites.Select(f => f.Key));
        var reloaded = ConfigDocument.Load(options);
        Assert.Equal(["Live:ChunkSeconds"], reloaded.Favorites.Select(f => f.Key));
        Assert.False(reloaded.Find("Recording:SegmentMinSeconds")!.IsFavorite);
    }

    [Fact]
    public void Feldattribute_und_Abschnitte()
    {
        using var t = Setup();
        var doc = ConfigDocument.Load(new ConfigEditorOptions { DescriptorPath = t.Descriptor });

        Assert.True(doc.Find("Recording:FlushSeconds")!.Hidden);
        var max = doc.Find("Recording:MaxMinutes")!;
        Assert.Equal(0, max.Min);
        Assert.Equal("unbegrenzt", max.ZeroMeans);
        Assert.True(doc.Find("Models:Backend")!.RestartRequired);
        var root = doc.Find("Output:RootPath")!;
        Assert.True(root.WarnCloudFolder);
        Assert.Equal("Dokumente\\X", root.EmptyMeans);

        Assert.Equal(["Ausgabe", "Live-Text"], doc.Sections.Take(2).Select(s => s.Label));
        Assert.Contains(doc.Warnings, w => w.Contains("MaxMinutes.options") && w.Contains("reserviert"));
        Assert.Contains(doc.Warnings, w => w.Contains("Backend.foo") && w.Contains("unbekannt"));
    }

    [Fact]
    public void Descriptor_neben_Base_wird_automatisch_gefunden()
    {
        using var t = Setup();
        var doc = ConfigDocument.Load(new ConfigEditorOptions { BasePath = t.Base });
        Assert.Equal(t.Descriptor, doc.Paths.DescriptorFile);
        Assert.NotEmpty(doc.Favorites);
    }

    [Fact]
    public void App_Name_leitet_Default_Pfade_ab()
    {
        using var t = new TempDir().WithBase("{}");
        var (paths, _) = ConfigPaths.Resolve(new ConfigEditorOptions { BasePath = t.Base, AppName = "Foo" });
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Foo", "appsettings.local.json"), paths.UserFile);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Foo", "appsettings.json"), paths.MachineFile);
    }

    [Fact]
    public void Ohne_User_Pfad_und_App_Name_Parameterfehler()
    {
        using var t = new TempDir().WithBase("{}");
        var ex = Assert.Throws<ConfigEditorException>(() => ConfigPaths.Resolve(new ConfigEditorOptions { BasePath = t.Base }));
        Assert.Equal(ExitCodes.BadArguments, ex.ExitCode);
    }

    [Fact]
    public void Validate_Exitcodes()
    {
        using var t = new TempDir().WithBase("""{ "A": { "N": 1, "B": true } }""");
        Assert.Equal(ExitCodes.Ok, ConfigValidation.Run(t.Options).ExitCode);

        t.WithUser("""{ "A": { "N": "eins" } }""");
        var r = ConfigValidation.Run(t.Options);
        Assert.Equal(ExitCodes.ValidationFailed, r.ExitCode);
        Assert.Contains(r.Problems, p => p.Contains("A:N"));

        t.WithUser("{ kaputt");
        Assert.Equal(ExitCodes.BrokenFile, ConfigValidation.Run(t.Options).ExitCode);

        var missing = new ConfigEditorOptions { BasePath = Path.Combine(t.Root, "fehlt.json"), UserPath = t.User };
        Assert.Equal(ExitCodes.BrokenFile, ConfigValidation.Run(missing).ExitCode);
    }
}
