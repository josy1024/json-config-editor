using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using JsonConfigEditor.Core;

namespace JsonConfigEditor.Wpf.ViewModels;

public sealed class SavedEventArgs(IReadOnlyList<string> changedKeys, bool restartRequired) : EventArgs
{
    /// <summary>Alle seit dem Öffnen gespeicherten Keys.</summary>
    public IReadOnlyList<string> ChangedKeys { get; } = changedKeys;
    /// <summary>Mindestens ein gespeicherter Key gilt erst nach Neustart der App.</summary>
    public bool RestartRequired { get; } = restartRequired;
    /// <summary>Der Key, der gerade gespeichert wurde.</summary>
    public string? LastKey { get; init; }
}

public sealed class SectionViewModel : ObservableObject
{
    private bool _isVisible = true;

    public SectionViewModel(ConfigSection section, IEnumerable<FieldViewModel> fields)
    {
        Section = section;
        Fields = fields.ToList();
    }

    public ConfigSection Section { get; }
    public string Name => Section.Name;
    public string Label => Section.Label;
    public string? Description => Section.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Section.Description);
    public string? Hint => Section.Hint;
    public bool HasHint => !string.IsNullOrWhiteSpace(Section.Hint);
    public string Glyph => Icons.For(Section.Icon, Section.Name);
    public IReadOnlyList<FieldViewModel> Fields { get; }
    public int VisibleCount => Fields.Count(f => f.IsVisible);
    public int Count => Fields.Count(f => !f.Field.Hidden);

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    internal void RaiseCounts() => OnPropertyChanged(nameof(VisibleCount));
}

public sealed class ChipViewModel(string label, int count, SectionViewModel? section) : ObservableObject
{
    private bool _isSelected;
    public string Label { get; } = label;
    public int Count { get; } = count;
    public SectionViewModel? Section { get; } = section;
    public Action<ChipViewModel>? Selected { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value) && value) Selected?.Invoke(this);
        }
    }
}

/// <summary>Nav-Eintrag links: Favoriten oder ein Abschnitt.</summary>
public sealed record NavItem(string Label, string Glyph, object Target);

/// <summary>Ganzer Editor: Dokument, Filter, Banner, Dateiüberwachung.</summary>
public sealed class EditorViewModel : ObservableObject, IDisposable
{
    private readonly List<FieldViewModel> _all = [];
    private readonly HashSet<string> _savedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _reloadTimer;
    private readonly DispatcherTimer _infoTimer;
    private FileSystemWatcher? _watcher;
    private string? _lastWritten;
    private string _search = "";
    private bool _changedOnly;
    private bool _showAdvanced;
    private ChipViewModel? _chip;
    private string? _info;
    private int _columns = 2;
    private bool _favoritesVisible;

    public EditorViewModel(ConfigEditorOptions options)
    {
        Options = options;
        _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); ReloadFromDisk(); };
        _infoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _infoTimer.Tick += (_, _) => { _infoTimer.Stop(); InfoMessage = null; };

        OpenUserFileCommand = new RelayCommand(OpenUserFile);
        OpenUserFolderCommand = new RelayCommand(OpenUserFolder);
        BackupBrokenCommand = new RelayCommand(BackupBroken);
        ReloadCommand = new RelayCommand(ReloadFromDisk);
        ClearSearchCommand = new RelayCommand(() => SearchText = "");

        OpenOtherCommand = new RelayCommand(() => OpenOtherRequested?.Invoke(this, EventArgs.Empty));

        try
        {
            Document = ConfigDocument.Load(options);
        }
        catch (ConfigEditorException ex)
        {
            LoadError = ex.Message;
            LoadErrorCode = ex.ExitCode;
            Log.Warn("Laden fehlgeschlagen: " + ex.Message);
            return;
        }
        Document.CheckWritable();
        Log.Info($"Geöffnet: {Document.Paths.UserFile} (Basis {Document.Paths.BaseFile})"
                 + (Document.WriteBlockedReason != null ? " – nur Ansicht: " + Document.WriteBlockedReason : ""));
        Build();
        StartWatching();
        ConfigWriter.Written += OnWritten;
    }

    public ConfigEditorOptions Options { get; }
    public ConfigDocument Document { get; private set; } = null!;
    public string? LoadError { get; }
    public int LoadErrorCode { get; }
    public bool HasLoadError => LoadError != null;
    public bool IsReadOnly => Document?.ReadOnly ?? true;
    /// <summary>User-Datei nicht beschreibbar: Editor zeigt nur an.</summary>
    public string? WriteBlockedReason => Document?.WriteBlockedReason;
    public bool HasWriteBlocked => WriteBlockedReason != null;
    /// <summary>Mit --readonly gestartet.</summary>
    public bool IsReadOnlyByOption => Options.ReadOnly && !HasLoadError;

    /// <summary>Host kann "Andere Einstellungen öffnen…" anbieten (die EXE tut das).</summary>
    public bool CanOpenOther { get => _canOpenOther; set => Set(ref _canOpenOther, value); }
    private bool _canOpenOther;
    public ICommand OpenOtherCommand { get; }
    public event EventHandler? OpenOtherRequested;

    public string Title => Document?.Title ?? "Einstellungen";
    public string AppName => Document?.Paths.AppName ?? "Einstellungen";
    public string UserFilePath => Document?.Paths.UserFile ?? "";
    public string Version => typeof(EditorViewModel).Assembly.GetName().Version?.ToString(3) ?? "";

    public ObservableCollection<SectionViewModel> Sections { get; } = [];
    public ObservableCollection<FieldViewModel> Favorites { get; } = [];
    public ObservableCollection<ChipViewModel> Chips { get; } = [];
    public ObservableCollection<NavItem> NavItems { get; } = [];
    public IReadOnlyList<string> Warnings => Document?.Warnings ?? [];
    public bool HasWarnings => Warnings.Count > 0;
    public string WarningsTitle => Warnings.Count == 1 ? "1 Hinweis zur Konfiguration" : $"{Warnings.Count} Hinweise zur Konfiguration";

    public string? UserFileError => Document?.UserFileError;
    public bool HasUserFileError => UserFileError != null;
    public string? RootHint => Document?.RootHint;
    public bool HasRootHint => !string.IsNullOrWhiteSpace(RootHint);

    public bool HasFavorites => Favorites.Count > 0;
    public bool FavoritesVisible
    {
        get => _favoritesVisible;
        private set => Set(ref _favoritesVisible, value);
    }

    public int TotalCount => _all.Count(f => !f.Field.Hidden);
    public int ChangedCount => _all.Count(f => f.IsChanged);
    public string SearchPlaceholder => $"{TotalCount} Einstellungen durchsuchen";
    public string ChangedLabel => $"Geändert · {ChangedCount}";
    public bool NothingFound => !HasLoadError && Sections.All(s => !s.IsVisible) && !FavoritesVisible;

    public int Columns
    {
        get => _columns;
        set => Set(ref _columns, value);
    }

    public string SearchText
    {
        get => _search;
        set
        {
            if (Set(ref _search, value ?? "")) { OnPropertyChanged(nameof(HasSearch)); ApplyFilter(); }
        }
    }
    public bool HasSearch => _search.Length > 0;

    public bool ShowChangedOnly
    {
        get => _changedOnly;
        set
        {
            if (Set(ref _changedOnly, value)) { OnPropertyChanged(nameof(ShowAll)); ApplyFilter(); }
        }
    }

    public bool ShowAll
    {
        get => !_changedOnly;
        set => ShowChangedOnly = !value;
    }

    public bool ShowAdvanced
    {
        get => _showAdvanced;
        set { if (Set(ref _showAdvanced, value)) ApplyFilter(); }
    }

    public bool HasAdvanced => _all.Any(f => f.Field.Advanced && !f.Field.Hidden);

    public string? InfoMessage
    {
        get => _info;
        private set { if (Set(ref _info, value)) OnPropertyChanged(nameof(HasInfo)); }
    }
    public bool HasInfo => _info != null;

    public ICommand OpenUserFileCommand { get; }
    public ICommand OpenUserFolderCommand { get; }
    public ICommand BackupBrokenCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand ClearSearchCommand { get; }

    /// <summary>Nach jedem erfolgreichen Speichern.</summary>
    public event EventHandler<SavedEventArgs>? Saved;

    public SectionViewModel? FindSection(string? name) =>
        name == null ? null : Sections.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Label, name, StringComparison.OrdinalIgnoreCase));

    internal string SectionLabel(string name) => Document?.FindSection(name)?.Label ?? name;

    // ---------------------------------------------------------------- Aufbau

    private void Build()
    {
        var byKey = new Dictionary<string, FieldViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Document.Fields)
        {
            var vm = new FieldViewModel(f, this);
            byKey[f.Key] = vm;
            _all.Add(vm);
        }
        foreach (var f in Document.Favorites) Favorites.Add(byKey[f.Key]);
        foreach (var s in Document.Sections.Where(s => !s.Hidden))
        {
            var svm = new SectionViewModel(s, s.Fields.Select(f => byKey[f.Key]));
            if (svm.Count > 0) Sections.Add(svm);
        }

        if (HasFavorites) NavItems.Add(new NavItem("Favoriten", Icons.Star, "favorites"));
        foreach (var s in Sections) NavItems.Add(new NavItem(s.Label, s.Glyph, s));

        var all = new ChipViewModel("Alle", TotalCount, null) { Selected = OnChip };
        Chips.Add(all);
        foreach (var s in Sections) Chips.Add(new ChipViewModel(s.Label, s.Count, s) { Selected = OnChip });
        _chip = all;
        all.IsSelected = true;
        ApplyFilter();
    }

    private void OnChip(ChipViewModel chip)
    {
        _chip = chip;
        foreach (var c in Chips.Where(c => c != chip)) c.IsSelected = false;
        ApplyFilter();
    }

    public void ApplyFilter()
    {
        var search = _search.Trim();
        foreach (var f in _all)
        {
            f.IsVisible = !f.Field.Hidden
                          && (_showAdvanced || !f.Field.Advanced || f.Field.IsFavorite)
                          && (!_changedOnly || f.IsChanged)
                          && (search.Length == 0 || f.Matches(search));
        }
        var chipSection = _chip?.Section;
        foreach (var s in Sections)
        {
            s.IsVisible = (chipSection == null || chipSection == s) && s.Fields.Any(f => f.IsVisible);
            s.RaiseCounts();
        }
        FavoritesVisible = chipSection == null && Favorites.Any(f => f.IsVisible);
        OnPropertyChanged(nameof(NothingFound));
    }

    // ---------------------------------------------------------------- Speichern / Neu laden

    internal void OnFieldSaved(FieldViewModel field)
    {
        _savedKeys.Add(field.Key);
        OnPropertyChanged(nameof(ChangedCount));
        OnPropertyChanged(nameof(ChangedLabel));
        if (_changedOnly) ApplyFilter();
        var restart = _all.Any(f => f.RestartRequired && _savedKeys.Contains(f.Key));
        if (field.RestartRequired) Info($"„{field.Label}“ gilt erst nach einem Neustart der App.");
        Saved?.Invoke(this, new SavedEventArgs(_savedKeys.ToList(), restart) { LastKey = field.Key });
    }

    private void OnWritten(string path, string text)
    {
        if (Document != null && string.Equals(path, Document.Paths.UserFile, StringComparison.OrdinalIgnoreCase))
            _lastWritten = text;
    }

    private void StartWatching()
    {
        var dir = Path.GetDirectoryName(Document.Paths.UserFile)!;
        try
        {
            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(Document.Paths.UserFile))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            // Events kommen auf einem Threadpool-Thread, Reload läuft entprellt auf dem UI-Thread
            FileSystemEventHandler kick = (_, _) => KickReloadFromAnyThread();
            _watcher.Changed += kick;
            _watcher.Created += kick;
            _watcher.Deleted += kick;
            _watcher.Renamed += (_, _) => KickReloadFromAnyThread();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // ohne Überwachung geht's auch, dann eben kein Auto-Reload
        }
    }

    private Dispatcher? _uiDispatcher = Dispatcher.CurrentDispatcher;

    private void KickReloadFromAnyThread() => _uiDispatcher?.BeginInvoke(KickReload);

    private void KickReload()
    {
        _reloadTimer.Stop();
        _reloadTimer.Start();
    }

    private void ReloadFromDisk()
    {
        if (Document == null) return;
        string? text = null;
        try { text = File.Exists(Document.Paths.UserFile) ? SafeFile.ReadText(Document.Paths.UserFile, "User-Datei") : null; }
        catch (ConfigEditorException ex)
        {
            // gesperrt/kein Zugriff: SafeFile hat schon ein paar Mal probiert, nicht endlos weiter
            Info("Neu laden fehlgeschlagen: " + ex.Message);
            return;
        }
        if (text != null && text == _lastWritten) return;

        var hadError = Document.UserFileError != null;
        try
        {
            var newKeys = Document.Reload();
            foreach (var f in _all) f.RefreshFromModel();
            _lastWritten = text;
            OnPropertyChanged(nameof(UserFileError));
            OnPropertyChanged(nameof(HasUserFileError));
            OnPropertyChanged(nameof(ChangedCount));
            OnPropertyChanged(nameof(ChangedLabel));
            ApplyFilter();
            if (Document.UserFileError != null) return;
            Info(hadError
                ? "Datei ist wieder in Ordnung, neu geladen."
                : newKeys
                    ? "Datei wurde von außen geändert, neu geladen. Neue Einträge zeigt der Editor nach erneutem Öffnen."
                    : "Datei wurde von außen geändert, neu geladen.");
        }
        catch (ConfigEditorException ex)
        {
            Info("Neu laden fehlgeschlagen: " + ex.Message);
        }
    }

    private void Info(string message)
    {
        InfoMessage = message;
        _infoTimer.Stop();
        _infoTimer.Start();
    }

    // ---------------------------------------------------------------- Befehle

    public void OpenUserFile()
    {
        if (Document == null) return;
        var path = Document.Paths.UserFile;
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                AtomicFile.WriteAllText(path, "{\n}\n");
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true }); }
            catch (Exception ex) { Info("Konnte die Datei nicht öffnen: " + ex.Message); }
        }
    }

    private void OpenUserFolder()
    {
        if (Document == null) return;
        var dir = Path.GetDirectoryName(Document.Paths.UserFile)!;
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Info("Konnte den Ordner nicht öffnen: " + ex.Message); }
    }

    private void BackupBroken()
    {
        try
        {
            var backup = ConfigWriter.BackupBrokenUserFile(Document);
            foreach (var f in _all) f.LoadFromModel();
            OnPropertyChanged(nameof(UserFileError));
            OnPropertyChanged(nameof(HasUserFileError));
            OnPropertyChanged(nameof(ChangedCount));
            OnPropertyChanged(nameof(ChangedLabel));
            ApplyFilter();
            Info($"Sicherung angelegt: {Path.GetFileName(backup)}. Du startest mit den Standardwerten.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ConfigEditorException)
        {
            Info("Sicherung fehlgeschlagen: " + ex.Message);
        }
    }

    /// <summary>Offene Eingaben sofort speichern (Fenster schließt).</summary>
    public void FlushAll()
    {
        foreach (var f in _all) f.FlushPending();
    }

    public void Dispose()
    {
        ConfigWriter.Written -= OnWritten;
        _watcher?.Dispose();
        _watcher = null;
        _reloadTimer.Stop();
        _infoTimer.Stop();
        _uiDispatcher = null;
    }
}
