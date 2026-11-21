using JsonConfigEditor.Core;
using Xunit;

namespace JsonConfigEditor.Tests;

public class AppCatalogTests
{
    private sealed class Env : IDisposable
    {
        public TempDir T { get; } = new();
        public string Exe => Dir("exe");
        public string Cwd => Dir("cwd");
        public string UserDir => Path.Combine(T.Root, "appdata");
        public string MachineDir => Path.Combine(T.Root, "programdata");
        public CatalogLocations Loc => new(Exe, Cwd, UserDir, MachineDir);

        public string Dir(string name)
        {
            var d = Path.Combine(T.Root, name);
            Directory.CreateDirectory(d);
            return d;
        }

        /// <summary>App-Ordner mit appsettings.json und Descriptor.</summary>
        public string App(string name, string? folder = null)
        {
            var d = Dir(folder ?? name);
            File.WriteAllText(Path.Combine(d, "appsettings.json"), """{ "A": { "B": true } }""");
            var desc = Path.Combine(d, "appsettings.editor.json");
            File.WriteAllText(desc, $$"""{ "app": { "name": "{{name}}", "userFile": "{{Path.Combine(T.Root, "u-" + name + ".json").Replace("\\", "\\\\")}}" } }""");
            return desc;
        }

        public void Write(string dir, string file, string json)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, file), json);
        }

        public void Dispose() => T.Dispose();
    }

    [Fact]
    public void Lists_from_it_bundle_and_user_with_relative_paths_and_missing_files()
    {
        using var e = new Env();
        var praxis = e.App("Praxis");
        e.Write(e.MachineDir, "apps.json", $$"""
            {
              // IT-Vorgabe
              "apps": [
                { "name": "Praxis (IT)", "descriptor": "{{praxis.Replace("\\", "\\\\")}}" },
                { "name": "Fehlt", "descriptor": "C:\\gibtsnicht\\appsettings.editor.json" },
              ]
            }
            """);
        e.App("Gebündelt", Path.Combine("exe", "bundled"));
        e.Write(e.Exe, "apps.json", """{ "apps": [ { "descriptor": "bundled\\appsettings.editor.json" } ] }""");
        e.Write(e.UserDir, "apps.json", """{ "apps": [ { "name": "Ohne Pfad" } ] }""");

        var c = AppCatalog.Load(e.Loc);

        var it = Assert.Single(c.Entries, x => x.Name == "Praxis (IT)");
        Assert.Equal(AppSource.Machine, it.Source);
        Assert.True(it.IsAvailable);
        var missing = Assert.Single(c.Entries, x => x.Name == "Fehlt");
        Assert.False(missing.IsAvailable);
        var bundled = Assert.Single(c.Entries, x => x.Source == AppSource.Bundled);
        Assert.True(bundled.IsAvailable); // relativ zur apps.json aufgelöst
        Assert.Equal("Gebündelt", bundled.Name); // ohne "name" in der Liste: app.name aus dem Descriptor
        Assert.Contains(c.Warnings, w => w.Contains("Eintrag 1") && w.Contains("descriptor"));
    }

    [Fact]
    public void Broken_list_is_skipped_with_warning()
    {
        using var e = new Env();
        e.Write(e.UserDir, "apps.json", "{ kaputt");
        var c = AppCatalog.Load(e.Loc);
        Assert.Empty(c.Entries);
        Assert.Contains(c.Warnings, w => w.Contains("kein gültiges JSON"));
    }

    [Fact]
    public void Descriptor_next_to_exe_or_in_current_folder_is_found_once()
    {
        using var e = new Env();
        File.WriteAllText(Path.Combine(e.Exe, "appsettings.editor.json"), """{ "app": { "name": "Neben" } }""");
        Assert.Single(AppCatalog.FindNearby(e.Exe, e.Exe + "\\", e.Cwd));
        var c = AppCatalog.Load(e.Loc);
        var found = Assert.Single(c.Entries);
        Assert.Equal(AppSource.Found, found.Source);
        Assert.Equal("Neben", found.Name);
    }

    [Fact]
    public void Recent_is_remembered_deduplicated_trimmed_and_forgettable()
    {
        using var e = new Env();
        var a = e.App("Alpha");
        var b = e.App("Beta");

        AppCatalog.RememberRecent(new ConfigEditorOptions { DescriptorPath = a }, "Alpha", e.Loc);
        AppCatalog.RememberRecent(new ConfigEditorOptions { DescriptorPath = b }, "Beta", e.Loc);
        AppCatalog.RememberRecent(new ConfigEditorOptions { DescriptorPath = a }, "Alpha", e.Loc);

        var c = AppCatalog.Load(e.Loc);
        Assert.Equal(["Alpha", "Beta"], c.Entries.Select(x => x.Name));
        Assert.All(c.Entries, x => Assert.Equal(AppSource.Recent, x.Source));

        AppCatalog.ForgetRecent(c.Entries[0], e.Loc);
        Assert.Equal(["Beta"], AppCatalog.Load(e.Loc).Entries.Select(x => x.Name));

        for (var i = 0; i < AppCatalog.MaxRecent + 3; i++)
            AppCatalog.RememberRecent(new ConfigEditorOptions { BasePath = Path.Combine(e.T.Root, $"x{i}", "appsettings.json"), AppName = $"X{i}" }, null, e.Loc);
        Assert.Equal(AppCatalog.MaxRecent, AppCatalog.Load(e.Loc).Entries.Count);
    }

    [Fact]
    public void Recent_entry_that_is_also_in_it_list_shows_once_with_it_source_and_date()
    {
        using var e = new Env();
        var a = e.App("Alpha");
        e.Write(e.MachineDir, "apps.json", $$"""{ "apps": [ { "descriptor": "{{a.Replace("\\", "\\\\")}}" } ] }""");
        AppCatalog.RememberRecent(new ConfigEditorOptions { DescriptorPath = a }, "Alpha", e.Loc);

        var x = Assert.Single(AppCatalog.Load(e.Loc).Entries);
        Assert.Equal(AppSource.Machine, x.Source);
        Assert.NotNull(x.LastOpened);
    }

    [Fact]
    public void Base_entry_derives_app_name_for_default_user_path()
    {
        var entry = new AppEntry { Name = "MeineApp", Base = @"C:\Tools\MeineApp\appsettings.json" };
        Assert.Equal("MeineApp", entry.ToOptions().AppName);
        Assert.Null(new AppEntry { Name = "X", Descriptor = @"C:\a\appsettings.editor.json" }.ToOptions().AppName);
    }

    // ---------------------------------------------------------------- OpenTarget

    [Fact]
    public void Open_descriptor_by_name_or_folder()
    {
        using var e = new Env();
        var d = e.App("Praxis");
        var o1 = new ConfigEditorOptions();
        OpenTarget.Apply(o1, d);
        Assert.Equal(d, o1.DescriptorPath);

        var o2 = new ConfigEditorOptions();
        OpenTarget.Apply(o2, Path.GetDirectoryName(d)!);
        Assert.Equal(d, o2.DescriptorPath);
    }

    [Fact]
    public void Open_base_with_sibling_descriptor_keeps_descriptor_name()
    {
        using var e = new Env();
        var d = e.App("Praxis");
        var o = new ConfigEditorOptions();
        OpenTarget.Apply(o, Path.Combine(Path.GetDirectoryName(d)!, "appsettings.json"));
        Assert.NotNull(o.BasePath);
        Assert.Null(o.AppName); // Descriptor daneben liefert den Namen
        Assert.Equal("Praxis", ConfigPaths.Resolve(o).Paths.AppName);
    }

    [Fact]
    public void Open_plain_base_takes_app_name_from_folder_skipping_build_folders()
    {
        using var e = new Env();
        var dir = e.Dir(Path.Combine("MeineApp", "bin", "Release", "net10.0-windows"));
        var b = Path.Combine(dir, "appsettings.json");
        File.WriteAllText(b, "{}");
        var o = new ConfigEditorOptions();
        OpenTarget.Apply(o, b);
        Assert.Equal("MeineApp", o.AppName);
        Assert.Equal("Netzwerk", OpenTarget.AppNameFromFolder(@"C:\Netzwerk\appsettings.json"));
    }

    [Fact]
    public void Open_local_json_uses_sibling_base_and_writes_into_picked_file()
    {
        using var e = new Env();
        var dir = e.Dir("Legacy");
        File.WriteAllText(Path.Combine(dir, "appsettings.json"), "{}");
        var local = Path.Combine(dir, "appsettings.local.json");
        File.WriteAllText(local, "{}");
        var o = new ConfigEditorOptions();
        OpenTarget.Apply(o, local);
        Assert.Equal(Path.Combine(dir, "appsettings.json"), o.BasePath);
        Assert.Equal(local, o.UserPath);

        File.Delete(Path.Combine(dir, "appsettings.json"));
        Assert.Throws<ConfigEditorException>(() => OpenTarget.Apply(new ConfigEditorOptions(), local));
    }

    [Fact]
    public void Open_wrong_things_gives_parameter_errors()
    {
        using var e = new Env();
        var txt = Path.Combine(e.T.Root, "readme.txt");
        File.WriteAllText(txt, "hi");
        Assert.Equal(ExitCodes.BadArguments, Assert.Throws<ConfigEditorException>(() => OpenTarget.Apply(new ConfigEditorOptions(), txt)).ExitCode);
        Assert.Throws<ConfigEditorException>(() => OpenTarget.Apply(new ConfigEditorOptions(), Path.Combine(e.T.Root, "fehlt.json")));
        Assert.Throws<ConfigEditorException>(() => OpenTarget.Apply(new ConfigEditorOptions(), e.Dir("leer")));
    }

    [Fact]
    public void Json_with_formatVersion_is_a_descriptor_even_with_other_name()
    {
        using var e = new Env();
        var f = Path.Combine(e.Dir("x"), "praxis-editor.json");
        File.WriteAllText(f, """{ "formatVersion": 1 }""");
        Assert.True(OpenTarget.IsDescriptor(f));
    }
}
