using System.Globalization;
using JsonConfigEditor.Core;

namespace JsonConfigEditor.Tests;

public class ValidatorAndPathTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-AT");

    private static ConfigField Field(FieldKind kind, double? min = null, double? max = null) =>
        new() { Key = "A:B", Section = "A", Kind = kind, Min = min, Max = max };

    [Theory]
    [InlineData("7", true, "7")]
    [InlineData(" 12 ", true, "12")]
    [InlineData("7,5", false, null)]
    [InlineData("", false, null)]
    [InlineData("-1", false, null)]
    [InlineData("101", false, null)]
    public void Int_mit_Grenzen(string input, bool ok, string? json)
    {
        var good = FieldValidator.TryParse(Field(FieldKind.Int, 0, 100), input, De, out var v, out var err);
        Assert.Equal(ok, good);
        if (ok) Assert.Equal(json, v!.ToJsonString());
        else Assert.NotNull(err);
    }

    [Theory]
    [InlineData("7,5", "7.5")]
    [InlineData("7.5", "7.5")]
    [InlineData("-40", "-40")]
    [InlineData("1,0", "1")]
    public void Number_Komma_und_Punkt(string input, string json)
    {
        Assert.True(FieldValidator.TryParse(Field(FieldKind.Number), input, De, out var v, out _));
        Assert.Equal(json, v!.ToJsonString());
    }

    [Fact]
    public void Number_Unsinn_wird_abgelehnt()
    {
        Assert.False(FieldValidator.TryParse(Field(FieldKind.Number), "abc", De, out _, out var err));
        Assert.Equal("Zahl erwartet", err);
    }

    [Fact]
    public void Pfad_Ordner_existiert_fehlt_mustExist_Datei()
    {
        using var t = new TempDir();
        var r = PathChecker.Check(t.Root, PathKind.Folder, false, true, false, null);
        Assert.Equal(PathSeverity.Ok, r.Severity);

        r = PathChecker.Check(Path.Combine(t.Root, "neu"), PathKind.Folder, false, true, false, null);
        Assert.Equal(PathSeverity.Warning, r.Severity);
        Assert.Contains("angelegt", r.Message);

        r = PathChecker.Check(Path.Combine(t.Root, "neu"), PathKind.Folder, true, true, false, null);
        Assert.Equal(PathSeverity.Error, r.Severity);

        r = PathChecker.Check(t.Root, PathKind.File, false, true, false, null);
        Assert.Equal(PathSeverity.Error, r.Severity);

        r = PathChecker.Check(@"C:\a|b", PathKind.Folder, false, true, false, null);
        Assert.Equal(PathSeverity.Error, r.Severity);
    }

    [Fact]
    public void Pfad_Umgebungsvariablen_und_relativ()
    {
        using var t = new TempDir();
        File.WriteAllText(Path.Combine(t.Root, "vocab.txt"), "x");
        var r = PathChecker.Check("vocab.txt", PathKind.File, true, true, false, t.Root);
        Assert.Equal(PathSeverity.Ok, r.Severity);
        Assert.Equal(Path.Combine(t.Root, "vocab.txt"), r.Expanded);

        r = PathChecker.Check("%GIBTS_NICHT_JCE%\\x", PathKind.Folder, false, true, false, null);
        Assert.Equal(PathSeverity.Warning, r.Severity);
        Assert.Contains("GIBTS_NICHT_JCE", r.Message);

        r = PathChecker.Check("", PathKind.Folder, false, true, false, null);
        Assert.Equal(PathSeverity.Info, r.Severity);
    }

    [Fact]
    public void Pfad_in_OneDrive_wird_gewarnt()
    {
        using var t = new TempDir();
        var cloud = Path.Combine(t.Root, "OneDrive - Praxis");
        Directory.CreateDirectory(Path.Combine(cloud, "Dokumente"));
        var old = CloudFolders.Provider;
        CloudFolders.Provider = () => [cloud];
        try
        {
            var r = PathChecker.Check(Path.Combine(cloud, "Dokumente"), PathKind.Folder, false, true, true, null);
            Assert.Equal(PathSeverity.Warning, r.Severity);
            Assert.Contains("OneDrive", r.Message);

            r = PathChecker.Check(Path.Combine(cloud, "Dokumente"), PathKind.Folder, false, true, false, null);
            Assert.Equal(PathSeverity.Ok, r.Severity);

            // gleicher Präfix, anderer Ordner
            Assert.Null(CloudFolders.Find(cloud + "2"));
        }
        finally { CloudFolders.Provider = old; }
    }
}
