using System.Text.Json;
using System.Text.Json.Serialization;

namespace JsonConfigEditor.Core;

/// <summary>Woher ein Eintrag der Startseite kommt. Reihenfolge = Vorrang bei Doppelten.</summary>
public enum AppSource
{
    /// <summary>%ProgramData%\JsonConfigEditor\apps.json, verteilt von der IT.</summary>
    Machine,
    /// <summary>apps.json neben der EXE (Firmen- oder Portable-Paket).</summary>
    Bundled,
    /// <summary>%APPDATA%\JsonConfigEditor\apps.json, eigene Liste.</summary>
    User,
    /// <summary>appsettings.editor.json neben der EXE oder im aktuellen Ordner.</summary>
    Found,
    /// <summary>Zuletzt geöffnet (recent.json, automatisch).</summary>
    Recent,
}

/// <summary>Eine App, deren Einstellungen der Editor öffnen kann.</summary>
public sealed record AppEntry
{
    public required string Name { get; init; }
    public string? Descriptor { get; init; }
    public string? Base { get; init; }
    public string? User { get; init; }
    public string? Machine { get; init; }
    public AppSource Source { get; init; }
    public DateTime? LastOpened { get; init; }
    /// <summary>Warum der Eintrag gerade nicht geht (Datei fehlt), sonst null.</summary>
    public string? Problem { get; init; }

    /// <summary>Descriptor, sonst Basisdatei.</summary>
    public string Target => Descriptor ?? Base ?? "";
    public bool IsAvailable => Problem == null;

    public ConfigEditorOptions ToOptions() => new()
    {
        DescriptorPath = Descriptor,
        BasePath = Base,
        UserPath = User,
        MachinePath = Machine,
        // ohne Descriptor bestimmt der Name die Default-Pfade unter %APPDATA% und %ProgramData%
        AppName = Descriptor == null && User == null ? Name : null,
    };

    internal string Key => $"{Target}|{User}".ToUpperInvariant();
}

/// <summary>Wo der Katalog sucht. Für Tests austauschbar.</summary>
public sealed record CatalogLocations(string? ExeDir, string? CurrentDir, string UserDir, string MachineDir)
{
    public static CatalogLocations Default => new(
        AppContext.BaseDirectory,
        SafeCurrentDirectory(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppCatalog.FolderName),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppCatalog.FolderName));

    private static string? SafeCurrentDirectory()
    {
        try { return Environment.CurrentDirectory; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}

/// <summary>
/// Startseite ohne Parameter: vorgegebene Apps (IT, mitgeliefert, eigene Liste), gefundene
/// appsettings.editor.json und zuletzt geöffnete. Kaputte Listen werden mit Hinweis übersprungen.
/// </summary>
public sealed class AppCatalog
{
    public const string FolderName = "JsonConfigEditor";
    public const string ListFileName = "apps.json";
    public const string RecentFileName = "recent.json";
    public const string DescriptorFileName = "appsettings.editor.json";
    public const int MaxRecent = 10;

    public IReadOnlyList<AppEntry> Entries { get; }
    public IReadOnlyList<string> Warnings { get; }
    public CatalogLocations Locations { get; }

    /// <summary>Eigene Liste, wird bei "Liste bearbeiten" mit Vorlage angelegt.</summary>
    public string UserListFile => Path.Combine(Locations.UserDir, ListFileName);

    private AppCatalog(IReadOnlyList<AppEntry> entries, IReadOnlyList<string> warnings, CatalogLocations locations)
    {
        Entries = entries;
        Warnings = warnings;
        Locations = locations;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppCatalog Load(CatalogLocations? locations = null)
    {
        var loc = locations ?? CatalogLocations.Default;
        var warnings = new List<string>();
        var all = new List<AppEntry>();

        ReadList(Path.Combine(loc.MachineDir, ListFileName), AppSource.Machine, all, warnings);
        if (loc.ExeDir != null) ReadList(Path.Combine(loc.ExeDir, ListFileName), AppSource.Bundled, all, warnings);
        ReadList(Path.Combine(loc.UserDir, ListFileName), AppSource.User, all, warnings);
        foreach (var d in FindNearby(loc.ExeDir, loc.CurrentDir))
            all.Add(new AppEntry { Name = NameFor(d, null, isDescriptor: true), Descriptor = d, Source = AppSource.Found });
        foreach (var r in ReadRecent(loc, warnings))
            all.Add(r);

        // Doppelte zusammenlegen: höhere Quelle gewinnt, "zuletzt geöffnet" wandert mit
        var merged = new List<AppEntry>();
        var byKey = new Dictionary<string, int>();
        foreach (var e in all)
        {
            if (byKey.TryGetValue(e.Key, out var i))
            {
                if (e.LastOpened is { } t && (merged[i].LastOpened == null || merged[i].LastOpened < t))
                    merged[i] = merged[i] with { LastOpened = t };
                continue;
            }
            byKey[e.Key] = merged.Count;
            merged.Add(e with { Problem = e.Problem ?? ProblemOf(e) });
        }

        var sorted = merged
            .Select((e, i) => (e, i))
            .OrderByDescending(x => x.e.LastOpened.HasValue)
            .ThenByDescending(x => x.e.LastOpened)
            .ThenBy(x => x.i)
            .Select(x => x.e)
            .ToList();
        return new AppCatalog(sorted, warnings, loc);
    }

    /// <summary>appsettings.editor.json in diesen Ordnern (ohne Doppelte, nur existierende).</summary>
    public static IReadOnlyList<string> FindNearby(params string?[] dirs)
    {
        var found = new List<string>();
        foreach (var dir in dirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var p = Path.GetFullPath(Path.Combine(dir, DescriptorFileName));
                if (File.Exists(p) && !found.Contains(p, StringComparer.OrdinalIgnoreCase)) found.Add(p);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { }
        }
        return found;
    }

    /// <summary>Nach erfolgreichem Öffnen in die "Zuletzt"-Liste. Fehler werden nur geloggt.</summary>
    public static void RememberRecent(ConfigEditorOptions options, string? name, CatalogLocations? locations = null)
    {
        var loc = locations ?? CatalogLocations.Default;
        try
        {
            var entry = FromOptions(options, name);
            if (entry == null) return;
            var list = ReadRecentDtos(loc);
            list.RemoveAll(d => ToEntry(d, AppSource.Recent, null)?.Key == entry.Key);
            list.Insert(0, new EntryDto
            {
                Name = entry.Name, Descriptor = entry.Descriptor, Base = entry.Base,
                User = entry.User, Machine = entry.Machine, LastOpened = DateTime.Now,
            });
            if (list.Count > MaxRecent) list.RemoveRange(MaxRecent, list.Count - MaxRecent);
            WriteRecent(loc, list);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ConfigEditorException or JsonException)
        {
            Log.Warn("Zuletzt-Liste nicht gespeichert", ex);
        }
    }

    /// <summary>Eintrag aus der "Zuletzt"-Liste nehmen (Listen von IT/User bleiben unberührt).</summary>
    public static void ForgetRecent(AppEntry entry, CatalogLocations? locations = null)
    {
        var loc = locations ?? CatalogLocations.Default;
        try
        {
            var list = ReadRecentDtos(loc);
            if (list.RemoveAll(d => ToEntry(d, AppSource.Recent, null)?.Key == entry.Key) > 0) WriteRecent(loc, list);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ConfigEditorException or JsonException)
        {
            Log.Warn("Zuletzt-Liste nicht gespeichert", ex);
        }
    }

    /// <summary>Eigene apps.json mit Beispiel anlegen, falls sie fehlt. Gibt den Pfad zurück.</summary>
    public string EnsureUserList()
    {
        var path = UserListFile;
        if (File.Exists(path)) return path;
        Directory.CreateDirectory(Locations.UserDir);
        AtomicFile.WriteAllText(path, """
            {
              // Eigene Apps für die Startseite von json-config-editor.
              // descriptor = appsettings.editor.json der App (empfohlen)
              // oder base = appsettings.json, dazu optional user/machine. %VARIABLEN% gehen, relative Pfade gelten ab diesem Ordner.
              "apps": [
                // { "name": "PraxisRecorder", "descriptor": "%ProgramFiles%\\PraxisRecorder\\appsettings.editor.json" },
                // { "name": "MeineApp", "base": "C:\\Tools\\MeineApp\\appsettings.json", "user": "%APPDATA%\\MeineApp\\appsettings.local.json" }
              ]
            }

            """);
        return path;
    }

    // ---------------------------------------------------------------- intern

    private sealed class EntryDto
    {
        public string? Name { get; set; }
        public string? Descriptor { get; set; }
        public string? Base { get; set; }
        public string? User { get; set; }
        public string? Machine { get; set; }
        public DateTime? LastOpened { get; set; }
    }

    private sealed class ListDto
    {
        public List<EntryDto>? Apps { get; set; }
        public List<EntryDto>? Recent { get; set; }
    }

    private static void ReadList(string file, AppSource source, List<AppEntry> into, List<string> warnings)
    {
        if (!File.Exists(file)) return;
        ListDto? dto;
        try { dto = JsonSerializer.Deserialize<ListDto>(SafeFile.ReadText(file, "App-Liste"), JsonOptions); }
        catch (ConfigEditorException ex) { warnings.Add(ex.Message); return; }
        catch (JsonException ex) { warnings.Add($"App-Liste {file} ist kein gültiges JSON: {ex.Message}"); return; }

        var dir = Path.GetDirectoryName(file);
        var n = 0;
        foreach (var d in dto?.Apps ?? [])
        {
            n++;
            try
            {
                var e = ToEntry(d, source, dir);
                if (e == null) warnings.Add($"App-Liste {file}, Eintrag {n}: \"descriptor\" oder \"base\" fehlt.");
                else into.Add(e);
            }
            catch (ConfigEditorException ex) { warnings.Add($"App-Liste {file}, Eintrag {n}: {ex.Message}"); }
        }
    }

    private static IEnumerable<AppEntry> ReadRecent(CatalogLocations loc, List<string> warnings)
    {
        List<EntryDto> list;
        try { list = ReadRecentDtos(loc); }
        catch (Exception ex) when (ex is ConfigEditorException or JsonException)
        {
            warnings.Add($"Zuletzt-Liste wird ignoriert: {ex.Message}");
            yield break;
        }
        foreach (var d in list)
        {
            AppEntry? e;
            try { e = ToEntry(d, AppSource.Recent, null); }
            catch (ConfigEditorException) { continue; }
            if (e != null) yield return e;
        }
    }

    private static List<EntryDto> ReadRecentDtos(CatalogLocations loc)
    {
        var file = Path.Combine(loc.UserDir, RecentFileName);
        if (!File.Exists(file)) return [];
        var dto = JsonSerializer.Deserialize<ListDto>(SafeFile.ReadText(file, "Zuletzt-Liste"), JsonOptions);
        return dto?.Recent?.Where(r => r != null).ToList() ?? [];
    }

    private static void WriteRecent(CatalogLocations loc, List<EntryDto> list)
    {
        Directory.CreateDirectory(loc.UserDir);
        AtomicFile.WriteAllText(Path.Combine(loc.UserDir, RecentFileName),
            JsonSerializer.Serialize(new ListDto { Recent = list }, JsonOptions));
    }

    private static AppEntry? ToEntry(EntryDto d, AppSource source, string? relativeTo)
    {
        var descriptor = SafeFile.FullPath(d.Descriptor, relativeTo, "descriptor");
        var basePath = SafeFile.FullPath(d.Base, relativeTo, "base");
        if (descriptor == null && basePath == null) return null;
        return new AppEntry
        {
            Name = string.IsNullOrWhiteSpace(d.Name) ? NameFor(descriptor ?? basePath!, null, descriptor != null) : d.Name.Trim(),
            Descriptor = descriptor,
            Base = basePath,
            User = SafeFile.FullPath(d.User, relativeTo, "user"),
            Machine = SafeFile.FullPath(d.Machine, relativeTo, "machine"),
            Source = source,
            LastOpened = d.LastOpened,
        };
    }

    private static AppEntry? FromOptions(ConfigEditorOptions o, string? name)
    {
        var descriptor = SafeFile.FullPath(o.DescriptorPath);
        var basePath = SafeFile.FullPath(o.BasePath);
        if (descriptor == null && basePath == null) return null;
        return new AppEntry
        {
            Name = !string.IsNullOrWhiteSpace(name) ? name : NameFor(descriptor ?? basePath!, o.AppName, descriptor != null),
            Descriptor = descriptor,
            Base = basePath,
            User = SafeFile.FullPath(o.UserPath),
            Machine = SafeFile.FullPath(o.MachinePath),
            Source = AppSource.Recent,
        };
    }

    private static string? ProblemOf(AppEntry e)
    {
        try
        {
            return File.Exists(e.Target) ? null : "Datei fehlt";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
    }

    /// <summary>Anzeigename: app.name im Descriptor, sonst Ordnername.</summary>
    internal static string NameFor(string path, string? appName, bool isDescriptor)
    {
        if (!string.IsNullOrWhiteSpace(appName)) return appName;
        if (isDescriptor && File.Exists(path))
        {
            try
            {
                var d = EditorDescriptor.Load(path);
                if (!string.IsNullOrWhiteSpace(d.App?.Name)) return d.App.Name!;
            }
            catch (ConfigEditorException) { /* dann Ordnername */ }
        }
        return OpenTarget.AppNameFromFolder(path);
    }
}

/// <summary>Eine Datei oder ein Ordner, den der User übergibt oder auswählt, richtig zuordnen.</summary>
public static class OpenTarget
{
    private static readonly string[] BuildFolders = ["bin", "obj", "debug", "release", "publish", "x64", "x86", "win-x64", "app"];

    /// <summary>
    /// Descriptor, Basisdatei, User-Datei (<c>*.local.json</c>) oder Ordner erkennen und in
    /// <paramref name="o"/> eintragen. Schon gesetzte Optionen bleiben. Wirft <see cref="ConfigEditorException"/> (Exit 3).
    /// </summary>
    public static void Apply(ConfigEditorOptions o, string pathOrFolder)
    {
        var full = SafeFile.FullPath(pathOrFolder, what: "Datei")
                   ?? throw new ConfigEditorException("Leerer Pfad.", ExitCodes.BadArguments);

        if (Directory.Exists(full))
        {
            var d = Path.Combine(full, AppCatalog.DescriptorFileName);
            var b = Path.Combine(full, "appsettings.json");
            if (File.Exists(d)) { o.DescriptorPath ??= d; return; }
            if (!File.Exists(b))
                throw new ConfigEditorException($"In {full} liegt weder {AppCatalog.DescriptorFileName} noch appsettings.json.", ExitCodes.BadArguments);
            full = b;
        }
        if (!File.Exists(full)) throw new ConfigEditorException($"Datei nicht gefunden: {full}", ExitCodes.BadArguments);
        if (!full.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ConfigEditorException($"{Path.GetFileName(full)} ist keine .json-Datei.", ExitCodes.BadArguments);

        if (IsDescriptor(full))
        {
            o.DescriptorPath ??= full;
            return;
        }

        const string local = ".local.json";
        if (full.EndsWith(local, StringComparison.OrdinalIgnoreCase))
        {
            // User-Datei gewählt: Basis daneben suchen, die User-Datei bleibt das Schreibziel
            var baseFile = full[..^local.Length] + ".json";
            if (!File.Exists(baseFile))
                throw new ConfigEditorException(
                    $"{Path.GetFileName(full)} ist eine User-Datei, daneben fehlt {Path.GetFileName(baseFile)}. Bitte die appsettings.json der App wählen.",
                    ExitCodes.BadArguments);
            o.BasePath ??= baseFile;
            o.UserPath ??= full;
            return;
        }

        o.BasePath ??= full;
        var sibling = Path.Combine(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full) + ".editor.json");
        if (o.AppName == null && o.UserPath == null && !File.Exists(sibling))
            o.AppName = AppNameFromFolder(full);
    }

    /// <summary><c>*.editor.json</c> oder JSON mit <c>formatVersion</c> auf oberster Ebene.</summary>
    public static bool IsDescriptor(string path)
    {
        if (path.EndsWith(".editor.json", StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            var root = JsonText.ParseObject(SafeFile.ReadText(path, "Datei"), path);
            return root.ContainsKey("formatVersion");
        }
        catch (ConfigEditorException) { return false; }
    }

    /// <summary>Ordnername als App-Name, Build-Ordner (bin, Release, net10.0 …) überspringen.</summary>
    public static string AppNameFromFolder(string file)
    {
        var dir = Path.GetDirectoryName(file);
        while (!string.IsNullOrEmpty(dir))
        {
            var name = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name)) break;
            var lower = name.ToLowerInvariant();
            if (!BuildFolders.Contains(lower) && !System.Text.RegularExpressions.Regex.IsMatch(lower, @"^net(\d|standard|coreapp)"))
                return name;
            dir = Path.GetDirectoryName(dir);
        }
        return "App";
    }
}
