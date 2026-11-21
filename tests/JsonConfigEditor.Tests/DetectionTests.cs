using JsonConfigEditor.Core;

namespace JsonConfigEditor.Tests;

public class DetectionTests
{
    [Fact]
    public void PraxisRecorder_ohne_Descriptor_alle_Abschnitte_und_Typen()
    {
        using var t = new TempDir().WithPraxisBase();
        var doc = t.Load();

        Assert.Equal(["Audio", "Output", "Recording", "Models", "Live", "Quality", "Archive"], doc.Sections.Select(s => s.Name));
        Assert.Equal(FieldKind.Bool, doc.Find("Live:Enabled")!.Kind);
        Assert.Equal(FieldKind.Int, doc.Find("Audio:MaxGapFillSeconds")!.Kind);
        Assert.Equal(FieldKind.Int, doc.Find("Recording:SilenceThresholdDb")!.Kind);
        Assert.Equal(FieldKind.String, doc.Find("Models:Backend")!.Kind);
        Assert.Equal(FieldKind.String, doc.Find("Live:Model")!.Kind);
        Assert.Equal(FieldKind.Path, doc.Find("Output:RootPath")!.Kind);
        Assert.Equal(FieldKind.Path, doc.Find("Models:Directory")!.Kind);
        Assert.Equal(FieldKind.Path, doc.Find("Models:VocabularyFile")!.Kind);
        Assert.Equal(PathKind.File, doc.Find("Models:VocabularyFile")!.PathKind);
        Assert.Equal(FieldKind.Array, doc.Find("Audio:PreferredDevices")!.Kind);
        Assert.Empty(doc.Favorites);
    }

    [Fact]
    public void Kommentare_werden_Beschreibung()
    {
        using var t = new TempDir().WithPraxisBase();
        var doc = t.Load();

        Assert.Contains("Lücke beim Mikrowechsel", doc.Find("Audio:MaxGapFillSeconds")!.Description);
        Assert.Contains("Desktop\\PraxisRecorder", doc.Find("Output:RootPath")!.Description);
        Assert.Contains("Auto-Stop", doc.Find("Recording:MaxMinutes")!.Description);
        // zweizeiliger Kommentar vor dem Key
        Assert.Contains("vorausgefüllt", doc.Find("Audio:PreferredDevices")!.Description);
        // Kommentar vor einem Abschnitt beschreibt den Abschnitt
        Assert.Contains("Modelle", doc.FindSection("Live")!.Description);
        Assert.Null(doc.Find("Recording:DiskWarnGb")!.Description);
    }

    [Fact]
    public void Kommentar_hinter_Wert_gehoert_zum_Key_davor()
    {
        var map = CommentScanner.Scan("""
            {
              "A": 1, // zu A
              // zu B
              "B": { "C": true /* zu C */ },
              "D": [ 1, // im Array
                2 ]
            }
            """);
        Assert.Equal("zu A", map["A"]);
        Assert.Equal("zu B", map["B"]);
        Assert.Equal("zu C", map["B:C"]);
        Assert.False(map.ContainsKey("D"));
    }

    [Theory]
    [InlineData("Output:RootPath", "", true)]
    [InlineData("X:Name", @"C:\temp", true)]
    [InlineData("X:Name", "%LOCALAPPDATA%", true)]
    [InlineData("X:Name", "a/b", true)]
    [InlineData("X:Name", "ggml-base-q5_1.bin", false)]
    [InlineData("X:Name", "opus", false)]
    [InlineData("X:ProfileName", "opus", false)]
    public void Pfaderkennung(string key, string value, bool isPath) =>
        Assert.Equal(isPath, ConfigDocument.LooksLikePath(key, value));

    [Fact]
    public void Labels_aus_CamelCase() =>
        Assert.Equal("Max Gap Fill Seconds", ConfigDocument.MakeLabel("Audio:MaxGapFillSeconds", "Audio"));

    [Fact]
    public void Metadaten_mit_Unterstrich_sind_keine_Felder_hinweis_wird_angezeigt()
    {
        using var t = new TempDir()
            .WithBase("""{ "Audio": { "_hinweis": "Basis-Hinweis", "X": 1 } }""")
            .WithUser("""{ "Audio": { "_hinweis": "aus der App" }, "Models": { "_verfuegbar": ["a.bin"] } }""");
        var doc = t.Load();
        Assert.Single(doc.Fields);
        Assert.Equal("aus der App", doc.FindSection("Audio")!.Hint);
    }

    [Fact]
    public void Keys_nur_in_User_Datei_landen_unter_Sonstige()
    {
        using var t = new TempDir()
            .WithBase("""{ "Live": { "Model": "a" } }""")
            .WithUser("""{ "Late": { "Model": "b" } }""");
        var doc = t.Load();
        var f = doc.Find("Late:Model")!;
        Assert.Equal(ConfigSection.OtherName, f.Section);
        Assert.Equal("Sonstige", doc.Sections[^1].Label);
        Assert.Equal("Late › Model", f.Label);
    }
}
