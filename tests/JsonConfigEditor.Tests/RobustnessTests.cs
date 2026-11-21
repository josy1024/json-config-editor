using System.Text.Json.Nodes;
using JsonConfigEditor.Core;
using Xunit;

namespace JsonConfigEditor.Tests;

public class RobustnessTests
{
    [Fact]
    public void Directory_instead_of_file_gives_clear_error()
    {
        using var t = new TempDir();
        var ex = Assert.Throws<ConfigEditorException>(() => SafeFile.ReadText(Path.Combine(t.Root, "app"), "Basisdatei"));
        Assert.Contains("Ordner", ex.Message);
    }

    [Fact]
    public void Too_large_file_is_rejected()
    {
        using var t = new TempDir();
        var big = Path.Combine(t.Root, "big.json");
        using (var fs = File.Create(big)) fs.SetLength(SafeFile.MaxBytes + 1);
        var ex = Assert.Throws<ConfigEditorException>(() => SafeFile.ReadText(big, "Basisdatei"));
        Assert.Contains("MB", ex.Message);
        Assert.Equal(ExitCodes.BrokenFile, ex.ExitCode);
    }

    [Fact]
    public void Binary_file_is_rejected()
    {
        using var t = new TempDir();
        var bin = Path.Combine(t.Root, "x.json");
        File.WriteAllBytes(bin, [0x7B, 0x00, 0x01, 0x7D]);
        var ex = Assert.Throws<ConfigEditorException>(() => SafeFile.ReadText(bin, "Datei"));
        Assert.Contains("keine Textdatei", ex.Message);
    }

    [Fact]
    public void File_open_for_writing_by_the_app_can_still_be_read()
    {
        using var t = new TempDir().WithBase("""{ "A": 1 }""");
        using var writer = new FileStream(t.Base, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Contains("\"A\"", SafeFile.ReadText(t.Base, "Basisdatei"));
    }

    [Fact]
    public void Invalid_path_is_a_parameter_error_not_a_crash()
    {
        var ex = Assert.Throws<ConfigEditorException>(() => SafeFile.FullPath("C:\\a\0b.json"));
        Assert.Equal(ExitCodes.BadArguments, ex.ExitCode);
        var ex2 = Assert.Throws<ConfigEditorException>(() => ConfigDocument.Load(new ConfigEditorOptions { BasePath = "C:\\x\0y\\appsettings.json", AppName = "X" }));
        Assert.Equal(ExitCodes.BadArguments, ex2.ExitCode);
    }

    [Fact]
    public void Duplicate_keys_differing_in_case_are_reported_as_broken_file()
    {
        var ex = Assert.Throws<ConfigEditorException>(() => JsonText.ParseObject("""{ "Live": { "Model": "a", "model": "b" } }""", "Basisdatei"));
        Assert.Equal(ExitCodes.BrokenFile, ex.ExitCode);
    }

    [Fact]
    public void Writable_check_ok_for_normal_folder_and_creates_it()
    {
        using var t = new TempDir();
        Assert.Null(SafeFile.CheckWritable(t.User));
        Assert.True(Directory.Exists(Path.GetDirectoryName(t.User)));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(t.User)!)); // Probe wieder weg
    }

    [Fact]
    public void Readonly_user_file_makes_document_view_only()
    {
        using var t = new TempDir().WithBase("""{ "Live": { "Enabled": true } }""").WithUser("{}");
        File.SetAttributes(t.User, FileAttributes.ReadOnly);
        try
        {
            var doc = t.Load();
            doc.CheckWritable();
            Assert.NotNull(doc.WriteBlockedReason);
            Assert.True(doc.ReadOnly);
            var ex = Assert.Throws<InvalidOperationException>(() => ConfigWriter.Set(doc, "Live:Enabled", JsonValue.Create(false)));
            Assert.Contains("Nur Ansicht", ex.Message);
        }
        finally { File.SetAttributes(t.User, FileAttributes.Normal); }
    }

    [Fact]
    public void User_path_that_is_a_folder_is_not_writable()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(t.User);
        Assert.Contains("Ordner", SafeFile.CheckWritable(t.User));
    }

    [Fact]
    public void User_file_equal_to_machine_file_is_rejected()
    {
        using var t = new TempDir().WithBase("{}");
        var ex = Assert.Throws<ConfigEditorException>(() => ConfigPaths.Resolve(new ConfigEditorOptions { BasePath = t.Base, UserPath = t.Machine, MachinePath = t.Machine }));
        Assert.Equal(ExitCodes.BadArguments, ex.ExitCode);
    }

    [Fact]
    public void Descriptor_whose_base_points_to_itself_is_rejected()
    {
        using var t = new TempDir().WithDescriptor("""{ "app": { "name": "X", "base": "appsettings.editor.json" } }""");
        var ex = Assert.Throws<ConfigEditorException>(() => ConfigPaths.Resolve(new ConfigEditorOptions { DescriptorPath = t.Descriptor }));
        Assert.Contains("app.base", ex.Message);
    }

    [Fact]
    public void Descriptor_path_that_is_a_folder_is_rejected()
    {
        using var t = new TempDir();
        var ex = Assert.Throws<ConfigEditorException>(() => ConfigPaths.Resolve(new ConfigEditorOptions { DescriptorPath = Path.Combine(t.Root, "app") }));
        Assert.Equal(ExitCodes.BadArguments, ex.ExitCode);
    }

    [Fact]
    public void Log_writes_only_when_enabled()
    {
        using var t = new TempDir();
        var oldDir = Log.Directory;
        var oldEnabled = Log.Enabled;
        try
        {
            Log.Directory = Path.Combine(t.Root, "logs");
            Log.Enabled = false;
            Log.Info("aus");
            Assert.False(Directory.Exists(Log.Directory));
            Log.Enabled = true;
            Log.Warn("an", new InvalidOperationException("boom"));
            var text = File.ReadAllText(Log.CurrentFile);
            Assert.Contains("WARN", text);
            Assert.Contains("boom", text);
        }
        finally
        {
            Log.Directory = oldDir;
            Log.Enabled = oldEnabled;
        }
    }
}
