using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace JsonConfigEditor.Core;

/// <summary>
/// Alle Ebenen geladen und zu Feldern zusammengeführt: Base &lt; Machine &lt; User &lt; Machine:Locked.
/// Geschrieben wird nur über <see cref="ConfigWriter"/> in die User-Datei.
/// </summary>
public sealed partial class ConfigDocument
{
    internal const string EditorMetadataKey = "_jsonConfigEditor";

    public ConfigEditorOptions Options { get; }
    public ConfigPaths Paths { get; }
    public EditorDescriptor? Descriptor { get; }

    public string Title { get; private set; } = "Einstellungen";
    public IReadOnlyList<ConfigSection> Sections => _sections;
    public IReadOnlyList<ConfigField> Fields => _fields;
    public IReadOnlyList<ConfigField> Favorites => _favorites;
    /// <summary>Hinweise zu Descriptor und Dateien, die das Arbeiten nicht verhindern.</summary>
    public IReadOnlyList<string> Warnings => _warnings;
    /// <summary>User-Datei kaputt: dann wird nie geschrieben.</summary>
    public string? UserFileError { get; private set; }
    public string? MachineFileError { get; private set; }
    /// <summary>_hinweis auf oberster Ebene.</summary>
    public string? RootHint { get; private set; }

    /// <summary>User-Datei kann nicht geschrieben werden (Ordner gesperrt, schreibgeschützt …), siehe <see cref="CheckWritable"/>.</summary>
    public string? WriteBlockedReason { get; private set; }

    public bool ReadOnly => Options.ReadOnly || WriteBlockedReason != null;

    private readonly List<ConfigSection> _sections = [];
    private readonly List<ConfigField> _fields = [];
    private readonly List<ConfigField> _favorites = [];
    private readonly List<string> _warnings = [];
    private readonly Dictionary<string, ConfigField> _byKey = new(StringComparer.OrdinalIgnoreCase);

    private ConfigDocument(ConfigEditorOptions options, ConfigPaths paths, EditorDescriptor? descriptor)
    {
        Options = options;
        Paths = paths;
        Descriptor = descriptor;
    }

    /// <summary>Lädt alles. Wirft <see cref="ConfigEditorException"/> bei fehlender/kaputter Basisdatei oder falschen Parametern.</summary>
    public static ConfigDocument Load(ConfigEditorOptions options)
    {
        var (paths, descriptor) = ConfigPaths.Resolve(options);
        var doc = new ConfigDocument(options, paths, descriptor);
        doc.Build();
        return doc;
    }

    public ConfigField? Find(string key) => _byKey.GetValueOrDefault(key);

    public ConfigSection? FindSection(string name) =>
        _sections.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(s.Label, name, StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------- Laden

    private sealed class Layer
    {
        public readonly Dictionary<string, JsonNode?> Values = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Order = [];
        public readonly Dictionary<string, string> Hints = new(StringComparer.OrdinalIgnoreCase);
        public List<string>? Favorites;
        public string? FavoritesWarning;
    }

    private sealed record Layers(Layer Base, Layer Machine, Layer Locked, Layer User, Dictionary<string, string> Comments, List<string> Notes);

    private Layers ReadLayers()
    {
        if (!File.Exists(Paths.BaseFile))
            throw new ConfigEditorException($"Basisdatei nicht gefunden: {Paths.BaseFile}", ExitCodes.BrokenFile);
        var notes = new List<string>();
        var baseText = ReadText(Paths.BaseFile, "Basisdatei", notes);
        var baseObj = JsonText.ParseObject(baseText, $"Basisdatei {Paths.BaseFile}");
        var comments = CommentScanner.Scan(baseText);

        var machine = new Layer();
        var locked = new Layer();
        MachineFileError = null;
        if (Paths.MachineFile != null && File.Exists(Paths.MachineFile))
        {
            try
            {
                var m = JsonText.ParseObject(ReadText(Paths.MachineFile, "Machine-Datei", notes), $"Machine-Datei {Paths.MachineFile}");
                if (m["Locked"] is JsonObject l) Flatten(l, "", locked);
                Flatten(m, "", machine, skipTopLevel: "Locked");
            }
            catch (Exception ex) when (ex is ConfigEditorException or IOException or UnauthorizedAccessException)
            {
                MachineFileError = ex.Message;
            }
        }

        var user = new Layer();
        UserFileError = null;
        if (File.Exists(Paths.UserFile))
        {
            try
            {
                var userRoot = JsonText.ParseObject(ReadText(Paths.UserFile, "User-Datei", notes), $"User-Datei {Paths.UserFile}");
                ReadUserFavorites(userRoot, user);
                Flatten(userRoot, "", user);
            }
            catch (Exception ex) when (ex is ConfigEditorException or IOException or UnauthorizedAccessException)
            {
                UserFileError = ex.Message;
            }
        }

        var b = new Layer();
        Flatten(baseObj, "", b);
        return new Layers(b, machine, locked, user, comments, notes);
    }

    private static void ReadUserFavorites(JsonObject root, Layer user)
    {
        if (!root.TryGetPropertyValue(EditorMetadataKey, out var metadataNode)) return;
        if (metadataNode is not JsonObject metadata)
        {
            user.FavoritesWarning = $"User-Datei: {EditorMetadataKey} muss ein Objekt sein; gespeicherte Favoriten werden ignoriert.";
            return;
        }
        if (!metadata.TryGetPropertyValue("favorites", out var favoritesNode)) return;
        if (favoritesNode is not JsonArray favorites)
        {
            user.FavoritesWarning = $"User-Datei: {EditorMetadataKey}.favorites muss eine Liste sein; gespeicherte Favoriten werden ignoriert.";
            return;
        }

        user.Favorites = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in favorites)
        {
            if (item is JsonValue value && value.TryGetValue<string>(out var key) &&
                !string.IsNullOrWhiteSpace(key) && seen.Add(key))
            {
                user.Favorites.Add(key);
            }
            else
            {
                user.FavoritesWarning = $"User-Datei: ungültiger Eintrag in {EditorMetadataKey}.favorites wurde ignoriert.";
            }
        }
    }

    private void Build()
    {
        var layers = ReadLayers();
        var d = Descriptor;
        if (layers.User.FavoritesWarning is { } favoritesWarning) _warnings.Add(favoritesWarning);
        _warnings.AddRange(layers.Notes);
        if (d != null) _warnings.AddRange(d.Warnings);

        Title = d?.App?.Title ?? (Paths.AppName != null ? $"{Paths.AppName} Einstellungen" : "Einstellungen");
        if (MachineFileError != null) _warnings.Add($"Machine-Datei wird ignoriert: {MachineFileError}");
        if (d != null && d.FormatVersion > 1)
            _warnings.Add($"Descriptor hat formatVersion {d.FormatVersion}, dieser Editor kennt 1. Neuere Attribute werden ignoriert.");
        if (d?.Extra is { Count: > 0 } extraRoot)
            _warnings.Add($"Descriptor: unbekannte Einträge {string.Join(", ", extraRoot.Keys)}");

        // Reihenfolge: Base, dann Descriptor-Felder, dann was nur in Machine/Locked/User steht
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddKeys(IEnumerable<string> ks) { foreach (var k in ks) if (seen.Add(k)) keys.Add(k); }
        AddKeys(layers.Base.Order);
        if (d != null)
        {
            foreach (var k in d.Fields.Keys.Where(k => !seen.Contains(k)))
            {
                var known = layers.Machine.Values.ContainsKey(k) || layers.Locked.Values.ContainsKey(k) || layers.User.Values.ContainsKey(k);
                if (!known) _warnings.Add($"Descriptor: Feld {k} gibt's in keiner Config-Datei.");
            }
            AddKeys(d.Fields.Keys);
        }
        AddKeys(layers.Machine.Order);
        AddKeys(layers.Locked.Order);
        AddKeys(layers.User.Order);

        var sections = new Dictionary<string, ConfigSection>(StringComparer.OrdinalIgnoreCase);
        var appearance = 0;
        ConfigSection SectionFor(string name)
        {
            if (sections.TryGetValue(name, out var s)) return s;
            s = new ConfigSection { Name = name };
            s.Label = name switch
            {
                ConfigSection.GeneralName => "Allgemein",
                ConfigSection.OtherName => "Sonstige",
                _ => name,
            };
            s.Order = name switch
            {
                ConfigSection.GeneralName => -1,
                ConfigSection.OtherName => int.MaxValue,
                _ => 1000 + appearance++,
            };
            s.Description = layers.Comments.GetValueOrDefault(name);
            s.Hint = layers.User.Hints.GetValueOrDefault(name) ?? layers.Machine.Hints.GetValueOrDefault(name) ?? layers.Base.Hints.GetValueOrDefault(name);
            if (d != null && d.Sections.TryGetValue(name, out var sd))
            {
                s.Label = sd.Label ?? s.Label;
                s.Order = sd.Order ?? s.Order;
                s.Icon = sd.Icon;
                s.Description = sd.Description ?? s.Description;
                s.Hidden = sd.Hidden ?? false;
            }
            sections[name] = s;
            return s;
        }
        RootHint = layers.User.Hints.GetValueOrDefault("") ?? layers.Machine.Hints.GetValueOrDefault("") ?? layers.Base.Hints.GetValueOrDefault("");

        foreach (var key in keys)
        {
            var inBase = layers.Base.Values.ContainsKey(key);
            var fd = d?.Fields.GetValueOrDefault(key);
            var top = key.Contains(':') ? key[..key.IndexOf(':')] : ConfigSection.GeneralName;
            var sectionName = inBase || fd != null ? top : ConfigSection.OtherName;

            var field = new ConfigField { Key = key, Section = sectionName, InBase = inBase };
            SetValues(field, layers);
            var sample = field.HasBase ? field.BaseValue : field.EffectiveValue;
            field.Kind = Detect(key, sample);
            if (key.EndsWith("File", StringComparison.OrdinalIgnoreCase)) field.PathKind = PathKind.File;
            field.Label = MakeLabel(key, sectionName);
            field.Description = layers.Comments.GetValueOrDefault(key);
            if (fd != null) Apply(field, fd);
            if (field.Kind == FieldKind.Object) continue;

            var section = SectionFor(sectionName);
            section.Fields.Add(field);
            _fields.Add(field);
            _byKey[key] = field;
        }

        // Abschnitte aus dem Descriptor oder mit _hinweis, die (noch) keine Felder haben, braucht keiner
        _sections.AddRange(sections.Values.OrderBy(s => s.Order).ThenBy(s => s.Label, StringComparer.CurrentCultureIgnoreCase));

        var favoriteKeys = layers.User.Favorites ?? d?.Favorites ?? [];
        foreach (var key in favoriteKeys)
        {
            if (!_byKey.TryGetValue(key, out var f))
            {
                var source = layers.User.Favorites != null ? "User-Datei" : "Descriptor";
                _warnings.Add($"{source}: Favorit {key} gibt's nicht.");
                continue;
            }
            if (f.Hidden || f.Advanced)
                _warnings.Add($"Favorit {key} ist hidden/advanced, Favorit gewinnt.");
        }
        SetFavorites(favoriteKeys);

        if (d != null)
        {
            foreach (var s in d.Sections.Keys.Where(k => !sections.ContainsKey(k)))
                _warnings.Add($"Descriptor: Abschnitt {s} gibt's nicht.");
        }
    }

    /// <summary>
    /// Dateien neu lesen (z. B. nach externer Änderung) und Werte der bekannten Felder aktualisieren.
    /// Neue Keys tauchen erst nach neuem Öffnen auf.
    /// </summary>
    /// <returns>true, wenn sich die Menge der Keys geändert hat.</returns>
    public bool Reload()
    {
        var layers = ReadLayers();
        foreach (var f in _fields) SetValues(f, layers);
        SetFavorites(layers.User.Favorites ?? Descriptor?.Favorites ?? []);
        var all = layers.Base.Order.Concat(layers.Machine.Order).Concat(layers.Locked.Order).Concat(layers.User.Order);
        return all.Any(k => !_byKey.ContainsKey(k));
    }

    internal void SetFavorites(IEnumerable<string> keys)
    {
        _favorites.Clear();
        foreach (var field in _fields) field.IsFavorite = false;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (seen.Add(key) && _byKey.TryGetValue(key, out var field))
            {
                field.IsFavorite = true;
                _favorites.Add(field);
            }
        }
    }

    private static void SetValues(ConfigField f, Layers l)
    {
        f.HasBase = l.Base.Values.TryGetValue(f.Key, out var b);
        f.BaseValue = b?.DeepClone();
        f.HasMachine = l.Machine.Values.TryGetValue(f.Key, out var m);
        f.MachineValue = m?.DeepClone();
        f.HasLocked = l.Locked.Values.TryGetValue(f.Key, out var lk);
        f.LockedValue = lk?.DeepClone();
        f.HasUser = l.User.Values.TryGetValue(f.Key, out var u);
        f.UserValue = u?.DeepClone();
    }

    private void Apply(ConfigField f, FieldDescriptor fd)
    {
        f.Label = fd.Label ?? f.Label;
        f.Description = fd.Description ?? f.Description;
        if (fd.Type != null)
        {
            f.Kind = fd.Type.ToLowerInvariant() switch
            {
                "bool" or "boolean" => FieldKind.Bool,
                "int" or "integer" => FieldKind.Int,
                "number" or "double" => FieldKind.Number,
                "string" or "text" => FieldKind.String,
                "path" => FieldKind.Path,
                _ => Warn(f.Kind, $"Descriptor: {f.Key} hat unbekannten Typ \"{fd.Type}\"."),
            };
        }
        f.ReadOnly = fd.ReadOnly ?? f.ReadOnly;
        f.Hidden = fd.Hidden ?? f.Hidden;
        f.Advanced = fd.Advanced ?? f.Advanced;
        f.Min = fd.Min ?? f.Min;
        f.Max = fd.Max ?? f.Max;
        f.Unit = fd.Unit ?? f.Unit;
        f.ZeroMeans = fd.ZeroMeans ?? f.ZeroMeans;
        f.EmptyMeans = fd.EmptyMeans ?? f.EmptyMeans;
        f.RestartRequired = fd.RestartRequired ?? f.RestartRequired;
        f.MustExist = fd.MustExist ?? f.MustExist;
        f.ExpandEnv = fd.ExpandEnv ?? f.ExpandEnv;
        f.WarnCloudFolder = fd.WarnCloudFolder ?? f.WarnCloudFolder;
        if (fd.PathKind != null)
        {
            f.PathKind = fd.PathKind.Equals("file", StringComparison.OrdinalIgnoreCase) ? PathKind.File : PathKind.Folder;
            if (f.Kind == FieldKind.String) f.Kind = FieldKind.Path;
        }
        if (fd.RelativeTo != null) f.RelativeToBase = fd.RelativeTo.Equals("base", StringComparison.OrdinalIgnoreCase);

        if (fd.Extra is { Count: > 0 })
        {
            foreach (var name in fd.Extra.Keys)
            {
                _warnings.Add(FieldDescriptor.Reserved.Contains(name, StringComparer.OrdinalIgnoreCase)
                    ? $"Descriptor: {f.Key}.{name} ist für später reserviert und wird noch ignoriert."
                    : $"Descriptor: {f.Key}.{name} ist unbekannt.");
            }
        }
    }

    private FieldKind Warn(FieldKind keep, string message)
    {
        _warnings.Add(message);
        return keep;
    }

    // ---------------------------------------------------------------- Erkennung

    internal static FieldKind Detect(string key, JsonNode? v)
    {
        if (v is JsonArray) return FieldKind.Array;
        if (v is JsonObject) return FieldKind.Object;
        if (v is null) return LooksLikePathKey(key) ? FieldKind.Path : FieldKind.String;
        return v.GetValueKind() switch
        {
            JsonValueKind.True or JsonValueKind.False => FieldKind.Bool,
            JsonValueKind.Number => v.ToJsonString().IndexOfAny(['.', 'e', 'E']) >= 0 ? FieldKind.Number : FieldKind.Int,
            JsonValueKind.String => LooksLikePath(key, v.GetValue<string>()) ? FieldKind.Path : FieldKind.String,
            _ => FieldKind.String,
        };
    }

    internal static bool LooksLikePathKey(string key)
    {
        var leaf = key[(key.LastIndexOf(':') + 1)..];
        return PathKeySuffix().IsMatch(leaf);
    }

    internal static bool LooksLikePath(string key, string value) =>
        LooksLikePathKey(key)
        || value.Contains('\\') || value.Contains('/')
        || EnvVar().IsMatch(value)
        || DriveLetter().IsMatch(value);

    [GeneratedRegex("(Path|Dir|Directory|Folder|File)$", RegexOptions.IgnoreCase)]
    private static partial Regex PathKeySuffix();

    [GeneratedRegex("%[A-Za-z_][A-Za-z0-9_()]*%")]
    private static partial Regex EnvVar();

    [GeneratedRegex(@"^[A-Za-z]:([\\/]|$)")]
    private static partial Regex DriveLetter();

    internal static string MakeLabel(string key, string section)
    {
        var parts = key.Split(':');
        var rest = section is ConfigSection.GeneralName or ConfigSection.OtherName || parts.Length == 1
            ? parts
            : parts[1..];
        return string.Join(" › ", rest.Select(SplitCamel));
    }

    internal static string SplitCamel(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1]) && char.IsUpper(s[i - 1]))))
                sb.Append(' ');
            sb.Append(c == '_' ? ' ' : c);
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- Helfer

    private static void Flatten(JsonObject obj, string prefix, Layer layer, string? skipTopLevel = null)
    {
        foreach (var (name, value) in obj)
        {
            if (prefix.Length == 0 && skipTopLevel != null && string.Equals(name, skipTopLevel, StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.StartsWith('_'))
            {
                if (name.Equals("_hinweis", StringComparison.OrdinalIgnoreCase) && value is JsonValue h && h.GetValueKind() == JsonValueKind.String)
                    layer.Hints[prefix] = h.GetValue<string>();
                continue;
            }
            var key = prefix.Length == 0 ? name : prefix + ":" + name;
            if (value is JsonObject child)
            {
                Flatten(child, key, layer);
                continue;
            }
            if (layer.Values.TryAdd(key, value)) layer.Order.Add(key);
        }
    }

    internal static string ReadText(string path, string what = "Datei") => SafeFile.ReadText(path, what);

    private static string ReadText(string path, string what, List<string> notes)
    {
        var text = SafeFile.ReadText(path, what, out var wasAnsi);
        if (wasAnsi)
        {
            notes.Add(what == "User-Datei"
                ? $"{what} war nicht UTF-8 (ANSI/Windows-1252), Umlaute wurden richtig gelesen. Beim nächsten Speichern wird sie als UTF-8 geschrieben."
                : $"{what} ist nicht UTF-8 (ANSI/Windows-1252): {path}. Die App liest sie evtl. mit falschen Umlauten, bitte als UTF-8 speichern.");
            Log.Warn($"{what} nicht UTF-8: {path}");
        }
        return text;
    }

    /// <summary>
    /// Prüft, ob die User-Datei geschrieben werden kann. Wenn nicht: <see cref="WriteBlockedReason"/> gesetzt,
    /// Dokument ist dann read-only. Nicht in <see cref="Load"/>, weil es den Ordner anlegt (bei --validate unerwünscht).
    /// </summary>
    public void CheckWritable()
    {
        WriteBlockedReason = Options.ReadOnly ? null : SafeFile.CheckWritable(Paths.UserFile);
        if (!ReadOnly) AtomicFile.CleanupStale(Paths.UserFile);
    }

    /// <summary>Letzter gültiger Stand der User-Datei, den der Editor vor seinem letzten Speichern weggelegt hat.</summary>
    public string LastGoodFile => Paths.UserFile + ".bak";
}
