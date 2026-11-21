using System.Text;
using System.Text.Json.Nodes;
using JsonConfigEditor.Core;
using Xunit;

namespace JsonConfigEditor.Tests;

/// <summary>Selbstheilung: letzter guter Stand, ANSI-Dateien, toleranter Descriptor, sauberes atomares Schreiben.</summary>
public class SelfHealingTests
{
    private static readonly Encoding Win1252 = GetWin1252();

    private static Encoding GetWin1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    // ---------------------------------------------------------------- letzter guter Stand

    [Fact]
    public void Saving_keeps_the_previous_valid_file_as_bak()
    {
        using var t = new TempDir().WithBase("""{ "A": 0, "B": 0 }""").WithUser("""{ "A": 1 }""");
        var doc = t.Load();
        ConfigWriter.Set(doc, "B", JsonValue.Create(5));

        Assert.True(File.Exists(doc.LastGoodFile));
        var bak = JsonNode.Parse(File.ReadAllText(doc.LastGoodFile))!.AsObject();
        Assert.Equal(1, (int)bak["A"]!);
        Assert.False(bak.ContainsKey("B"));
    }

    [Fact]
    public void First_save_of_a_new_user_file_writes_no_bak()
    {
        using var t = new TempDir().WithBase("""{ "A": 0 }""");
        var doc = t.Load();
        ConfigWriter.Set(doc, "A", JsonValue.Create(3));
        Assert.False(File.Exists(doc.LastGoodFile));
    }

    [Fact]
    public void Broken_user_file_can_be_restored_from_last_good_state()
    {
        using var t = new TempDir().WithBase("""{ "A": 0, "B": 0 }""").WithUser("""{ "A": 1 }""");
        var doc = t.Load();
        ConfigWriter.Set(doc, "B", JsonValue.Create(5)); // .bak = { "A": 1 }

        File.WriteAllText(t.User, """{ "A": 1, "B": 5, """); // von Hand kaputt gemacht
        doc.Reload();
        Assert.NotNull(doc.UserFileError);
        Assert.NotNull(ConfigWriter.LastGoodTime(doc));

        var broken = ConfigWriter.RestoreLastGood(doc);

        Assert.Null(doc.UserFileError);
        Assert.Equal(1, (int)doc.Find("A")!.UserValue!);
        Assert.NotNull(broken);
        Assert.True(File.Exists(broken));
        Assert.Contains("\"B\": 5,", File.ReadAllText(broken!));
    }

    [Fact]
    public void Broken_bak_is_not_offered_and_not_restored()
    {
        using var t = new TempDir().WithBase("""{ "A": 0 }""").WithUser("{ kaputt");
        var doc = t.Load();
        File.WriteAllText(doc.LastGoodFile, "{ auch kaputt");

        Assert.Null(ConfigWriter.LastGoodTime(doc));
        Assert.Throws<ConfigEditorException>(() => ConfigWriter.RestoreLastGood(doc));
        Assert.Equal("{ kaputt", t.UserText); // nichts angefasst
    }

    // ---------------------------------------------------------------- Kodierung

    [Fact]
    public void Ansi_user_file_is_read_with_correct_umlauts_and_saved_as_utf8()
    {
        using var t = new TempDir().WithBase("""{ "Name": "", "Zahl": 0 }""");
        Directory.CreateDirectory(Path.GetDirectoryName(t.User)!);
        File.WriteAllBytes(t.User, Win1252.GetBytes("""{ "Name": "Ärzte Süd" }"""));

        var doc = t.Load();
        Assert.Equal("Ärzte Süd", doc.Find("Name")!.UserValue!.GetValue<string>());
        Assert.Contains(doc.Warnings, w => w.Contains("UTF-8"));

        ConfigWriter.Set(doc, "Zahl", JsonValue.Create(2));
        var bytes = File.ReadAllBytes(t.User);
        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        Assert.Contains("Ärzte Süd", strict.GetString(bytes)); // wirft, wenn nicht UTF-8
    }

    [Fact]
    public void Utf8_with_bom_and_utf16_are_read()
    {
        Assert.Equal("{\"A\":\"Ä\"}", SafeFile.Decode([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("{\"A\":\"Ä\"}")], out var a1));
        Assert.False(a1);
        Assert.Equal("{\"A\":\"Ä\"}", SafeFile.Decode([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("{\"A\":\"Ä\"}")], out var a2));
        Assert.False(a2);
        Assert.Equal("Ä", SafeFile.Decode([0xC4], out var a3));
        Assert.True(a3);
    }

    [Fact]
    public void Ansi_base_file_gives_a_warning_for_the_app_developer()
    {
        using var t = new TempDir();
        File.WriteAllBytes(t.Base, Win1252.GetBytes("{ // Größe\n \"A\": 1 }"));
        var doc = t.Load();
        Assert.Contains(doc.Warnings, w => w.StartsWith("Basisdatei ist nicht UTF-8"));
    }

    // ---------------------------------------------------------------- Descriptor tolerant

    [Fact]
    public void Wrong_type_in_descriptor_costs_only_that_entry()
    {
        using var t = new TempDir().WithBase("""{ "Live": { "Chunk": 5, "Model": "x" } }""").WithDescriptor("""
            {
              "formatVersion": "eins",
              "favorites": [ 3, "Live:Chunk" ],
              "sections": { "Live": "kein Objekt" },
              "fields": {
                "Live:Chunk": { "label": "Häppchen", "min": "5", "unit": "s" },
                "Live:Model": [ "falsch" ]
              }
            }
            """);
        var doc = ConfigDocument.Load(new ConfigEditorOptions { DescriptorPath = t.Descriptor, UserPath = t.User, MachinePath = t.Machine });

        var chunk = doc.Find("Live:Chunk")!;
        Assert.Equal("Häppchen", chunk.Label);
        Assert.Equal("s", chunk.Unit);
        Assert.Null(chunk.Min);
        Assert.True(chunk.IsFavorite);
        Assert.Contains(doc.Warnings, w => w.Contains("fields.Live:Chunk.min"));
        Assert.Contains(doc.Warnings, w => w.Contains("fields.Live:Model"));
        Assert.Contains(doc.Warnings, w => w.Contains("sections.Live"));
        Assert.Contains(doc.Warnings, w => w.Contains("favorites"));
        Assert.Contains(doc.Warnings, w => w.Contains("formatVersion"));
    }

    [Fact]
    public void Tolerated_descriptor_errors_still_fail_validate()
    {
        using var t = new TempDir().WithBase("""{ "A": 1 }""").WithDescriptor("""{ "fields": { "A": { "max": "zehn" } } }""");
        var r = ConfigValidation.Run(new ConfigEditorOptions { DescriptorPath = t.Descriptor, UserPath = t.User, MachinePath = t.Machine });
        Assert.Equal(ExitCodes.ValidationFailed, r.ExitCode);
        Assert.Contains(r.Problems, p => p.Contains("A.max"));
    }

    [Fact]
    public void Descriptor_with_broken_json_syntax_is_still_an_error()
    {
        var ex = Assert.Throws<ConfigEditorException>(() => EditorDescriptor.Parse("""{ "fields": { "A": { } """));
        Assert.Equal(ExitCodes.BrokenFile, ex.ExitCode);
    }

    [Fact]
    public void Descriptor_field_keys_stay_case_insensitive_and_unknown_attributes_warn()
    {
        var d = EditorDescriptor.Parse("""{ "fields": { "live:chunk": { "label": "x", "foo": 1 } }, "bar": 2 }""");
        Assert.True(d.Fields.ContainsKey("Live:Chunk"));
        Assert.True(d.Fields["Live:Chunk"].Extra!.ContainsKey("foo"));
        Assert.True(d.Extra!.ContainsKey("bar"));
    }

    // ---------------------------------------------------------------- atomares Schreiben

    [Fact]
    public void Atomic_write_leaves_no_tmp_behind()
    {
        using var t = new TempDir();
        var file = Path.Combine(t.Root, "a.json");
        AtomicFile.WriteAllText(file, "{}");
        AtomicFile.WriteAllText(file, """{ "A": 1 }""");
        Assert.Equal("""{ "A": 1 }""", File.ReadAllText(file));
        Assert.Empty(Directory.GetFiles(t.Root, "*.tmp"));
    }

    [Fact]
    public void Failed_atomic_write_deletes_its_tmp()
    {
        using var t = new TempDir();
        var target = Path.Combine(t.Root, "ordner.json");
        Directory.CreateDirectory(target); // Ziel ist ein Ordner: umbenennen geht nie
        Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(target, "{}", attempts: 2));
        Assert.Empty(Directory.GetFiles(t.Root, "*.tmp"));
    }

    [Fact]
    public void Stale_tmp_files_are_cleaned_but_fresh_ones_stay()
    {
        using var t = new TempDir().WithBase("""{ "A": 0 }""");
        Directory.CreateDirectory(Path.GetDirectoryName(t.User)!);
        var dir = Path.GetDirectoryName(t.User)!;
        var old1 = Path.Combine(dir, "appsettings.local.json.123.1.tmp");
        var old2 = Path.Combine(dir, ".jce-probe-1-abc.tmp");
        var fresh = Path.Combine(dir, "appsettings.local.json.456.1.tmp");
        var other = Path.Combine(dir, "fremd.tmp");
        foreach (var f in new[] { old1, old2, fresh, other }) File.WriteAllText(f, "x");
        File.SetLastWriteTimeUtc(old1, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(old2, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddDays(-2));

        var doc = t.Load();
        doc.CheckWritable();

        Assert.False(File.Exists(old1));
        Assert.False(File.Exists(old2));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(other)); // gehört nicht uns
    }
}
