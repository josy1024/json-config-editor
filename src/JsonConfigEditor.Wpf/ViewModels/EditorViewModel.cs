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
    private bool _hasVisibleFields;

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
    public int Count => Fields.Count(f => !f.Field.Hidden || f.Field.IsFavorite);

    public bool HasVisibleFields
    {
        get => _hasVisibleFields;
        set => Set(ref _hasVisibleFields, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    internal void RaiseCounts()
    {
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(Count));
    }
}

public sealed class ChipViewModel(string label, int count, SectionViewModel? section) : ObservableObject
{
    private bool _isSelected;
    private bool _isVisible = true;
    private int _count = count;
    public string Label { get; } = label;
    public int Count
    {
        get => _count;
        private set => Set(ref _count, value);
    }
    public SectionViewModel? Section { get; } = section;
    public Action<ChipViewModel>? Selected { get; set; }
    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value) && value) Selected?.Invoke(this);
        }
    }

    internal void UpdateCount(int count) => Count = count;
}

/// <summary>Nav-Eintrag links für einen Abschnitt.</summary>
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
    private bool _favoritesOnly;
    private bool _showAdvanced;
    private ChipViewModel? _chip;
    private string? _info;
    private int _columns = 2;

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
        RestoreLastGoodCommand = new RelayCommand(RestoreLastGood, () => CanRestoreLastGood);
        ReloadCommand = new RelayCommand(ReloadFromDisk);
        ClearSearchCommand = new RelayCommand(() => SearchText = "");

        OpenOtherCommand = new RelayCommand(() => OpenOtherRequested?.Invoke(this, EventArgs.Empty));
        ToggleDebugCommand = new RelayCommand(() => ShowDebug = !ShowDebug);
        CopyDebugCommand = new RelayCommand(CopyDebug);
        OpenLogCommand = new RelayCommand(OpenLog, () => Log.Enabled);
        OpenRepoCommand = new RelayCommand(() => { if (!AppInfo.OpenUrl(AppInfo.RepoUrl)) Info("Browser startet nicht: " + AppInfo.RepoUrl); });

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
    public string Version => AppInfo.Version;
    public string VersionText => AppInfo.VersionText;
    public string RepoUrl => AppInfo.RepoUrl;
    public string VersionTooltip => $"{AppInfo.RepoUrl}  ·  F3: Debug-Infos";

    // ---------------------------------------------------------------- F3 Debug

    private bool _showDebug;
    private string _debugText = "";

    /// <summary>F3: Aufruf, Version, verwendete JSON-Dateien. Wird beim Öffnen frisch aufgebaut.</summary>
    public bool ShowDebug
    {
        get => _showDebug;
        set
        {
            if (value) DebugText = DebugInfo.Build(this);
            Set(ref _showDebug, value);
        }
    }

    public string DebugText
    {
        get => _debugText;
        private set => Set(ref _debugText, value);
    }

    public ICommand ToggleDebugCommand { get; }
    public ICommand CopyDebugCommand { get; }
    public ICommand OpenLogCommand { get; }
    public ICommand OpenRepoCommand { get; }

    private void CopyDebug()
    {
        try
        {
            System.Windows.Clipboard.SetText(DebugInfo.Build(this));
            Info("Debug-Infos in der Zwischenablage");
        }
        catch (System.Runtime.InteropServices.ExternalException ex) { Info("Zwischenablage gerade belegt: " + ex.Message); }
    }

    private void OpenLog()
    {
        var file = Log.CurrentFile;
        try
        {
            if (File.Exists(file)) Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            else if (Directory.Exists(Log.Directory)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Directory}\"") { UseShellExecute = true });
            else Info("Noch kein Log da: " + file);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true }); }
            catch (Exception ex2) when (ex2 is System.ComponentModel.Win32Exception or InvalidOperationException) { Info("Log nicht zu öffnen: " + ex.Message); }
        }
    }

    public ObservableCollection<SectionViewModel> Sections { get; } = [];
    public ObservableCollection<FieldViewModel> Favorites { get; } = [];
    public ObservableCollection<ChipViewModel> Chips { get; } = [];
    public ObservableCollection<NavItem> NavItems { get; } = [];
    public IReadOnlyList<string> Warnings => Document?.Warnings ?? [];
    public bool HasWarnings => Warnings.Count > 0;
    public string WarningsTitle => Warnings.Count == 1 ? "1 Hinweis zur Konfiguration" : $"{Warnings.Count} Hinweise zur Konfiguration";

    public string? UserFileError => Document?.UserFileError;
    public bool HasUserFileError => UserFileError != null;

    /// <summary>Kaputte Datei und gültiger letzter Stand (.bak) da: zurückholen statt bei null anfangen.</summary>
    public bool CanRestoreLastGood => HasUserFileError && !IsReadOnly && LastGoodTime != null;
    private DateTime? LastGoodTime => Document != null ? ConfigWriter.LastGoodTime(Document) : null;
    public string RestoreLastGoodLabel => LastGoodTime is { } t ? $"Letzten guten Stand wiederherstellen ({t:dd.MM. HH:mm})" : "Letzten guten Stand wiederherstellen";
    public ICommand RestoreLastGoodCommand { get; }

    /// <summary>Felder, deren Wert noch nicht in der Datei ist (Speichern schlug fehl).</summary>
    public IReadOnlyList<FieldViewModel> UnsavedFields => _all.Where(f => f.HasUnsaved).ToList();
    public string? RootHint => Document?.RootHint;
    public bool HasRootHint => !string.IsNullOrWhiteSpace(RootHint);

    public bool HasFavorites => Favorites.Count > 0;
    public int TotalCount => _all.Count(f => !f.Field.Hidden || f.Field.IsFavorite);
    public int FilteredCount => _all.Count(f => f.IsVisible);
    public int FavoriteCount => Favorites.Count;
    public int ChangedCount => _all.Count(f => f.IsChanged);
    public string SearchPlaceholder => $"{TotalCount} Einstellungen durchsuchen";
    public string SearchResultLabel => $"{FilteredCount} Treffer";
    public string ChangedLabel => $"Geändert · {ChangedCount}";
    public bool NothingFound => !HasLoadError && Sections.All(s => !s.IsVisible);

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
            if (!Set(ref _changedOnly, value)) return;
            if (value && _favoritesOnly)
            {
                _favoritesOnly = false;
                OnPropertyChanged(nameof(ShowFavoritesOnly));
            }
            OnPropertyChanged(nameof(ShowAll));
            ApplyFilter();
        }
    }

    public bool ShowFavoritesOnly
    {
        get => _favoritesOnly;
        set
        {
            if (!Set(ref _favoritesOnly, value)) return;
            if (value && _changedOnly)
            {
                _changedOnly = false;
                OnPropertyChanged(nameof(ShowChangedOnly));
            }
            OnPropertyChanged(nameof(ShowAll));
            ApplyFilter();
        }
    }

    public bool ShowAll
    {
        get => !_changedOnly && !_favoritesOnly;
        set
        {
            if (!value) return;
            ShowChangedOnly = false;
            ShowFavoritesOnly = false;
        }
    }

    public bool ShowAdvanced
    {
        get => _showAdvanced;
        set { if (Set(ref _showAdvanced, value)) ApplyFilter(); }
    }

    public bool HasAdvanced => _all.Any(f => f.Field.Advanced && !f.Field.Hidden && !f.Field.IsFavorite);

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

    public void ToggleFavorite(FieldViewModel field)
    {
        if (!field.CanToggleFavorite) return;

        var adding = !field.IsFavorite;
        var keys = Favorites.Select(f => f.Key).ToList();
        if (!adding)
            keys.RemoveAll(k => string.Equals(k, field.Key, StringComparison.OrdinalIgnoreCase));
        else
            keys.Add(field.Key);

        try
        {
            ConfigWriter.SetFavorites(Document, keys);
            RefreshFavoriteViews();
            Info(adding ? $"Favorit gespeichert: {field.Label}" : $"Favorit entfernt: {field.Label}");
        }
        catch (Exception ex) when (ex is ConfigEditorException or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or KeyNotFoundException)
        {
            Log.Warn($"Favorit für {field.Key} nicht gespeichert ({Document.Paths.UserFile}): {ex.Message}");
            Info("Favorit nicht gespeichert: " + ex.Message);
        }
    }

    private void RefreshFavoriteViews()
    {
        var byKey = _all.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
        Favorites.Clear();
        foreach (var field in Document.Favorites)
            if (byKey.TryGetValue(field.Key, out var viewModel)) Favorites.Add(viewModel);

        foreach (var field in _all) field.RefreshFavoriteState();
        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(FavoriteCount));
        ApplyFilter();
    }

    public void ApplyFilter()
    {
        var search = _search.Trim();
        foreach (var f in _all)
        {
            f.IsVisible = (!f.Field.Hidden || f.Field.IsFavorite)
                          && (_showAdvanced || !f.Field.Advanced || f.Field.IsFavorite)
                          && (!_changedOnly || f.IsChanged)
                          && (!_favoritesOnly || f.IsFavorite)
                          && (search.Length == 0 || f.Matches(search));
        }
        var chipSection = _chip?.Section;
        foreach (var s in Sections)
        {
            s.HasVisibleFields = s.Fields.Any(f => f.IsVisible);
            s.IsVisible = (chipSection == null || chipSection == s) && s.HasVisibleFields;
            s.RaiseCounts();
            var chip = Chips.FirstOrDefault(c => c.Section == s);
            if (chip != null)
            {
                chip.IsVisible = s.HasVisibleFields;
                chip.UpdateCount(s.VisibleCount);
            }
        }
        var allChip = Chips.FirstOrDefault(c => c.Section == null);
        if (allChip != null) allChip.UpdateCount(FilteredCount);
        SyncNavItems();
        OnPropertyChanged(nameof(NothingFound));
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(SearchResultLabel));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(HasAdvanced));
    }

    private void SyncNavItems()
    {
        var visibleSections = Sections.Where(s => s.HasVisibleFields).ToList();
        for (var i = NavItems.Count - 1; i >= 0; i--)
        {
            if (NavItems[i].Target is not SectionViewModel section || !visibleSections.Contains(section))
                NavItems.RemoveAt(i);
        }

        for (var i = 0; i < visibleSections.Count; i++)
        {
            var section = visibleSections[i];
            var currentIndex = -1;
            for (var j = 0; j < NavItems.Count; j++)
            {
                if (ReferenceEquals(NavItems[j].Target, section))
                {
                    currentIndex = j;
                    break;
                }
            }

            if (currentIndex < 0)
                NavItems.Insert(i, new NavItem(section.Label, section.Glyph, section));
            else if (currentIndex != i)
                NavItems.Move(currentIndex, i);
        }
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

    /// <summary>Sofort neu laden, z. B. wenn eine Liste beim Verschieben schon anders in der Datei stand.</summary>
    internal void ReloadNow()
    {
        _reloadTimer.Stop();
        ReloadFromDisk();
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
            RefreshFavoriteViews();
            _lastWritten = text;
            RaiseUserFileState();
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

    internal void Info(string message)
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
            RaiseUserFileState();
            RefreshFavoriteViews();
            Info($"Sicherung angelegt: {Path.GetFileName(backup)}. Du startest mit den Standardwerten.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ConfigEditorException)
        {
            Info("Sicherung fehlgeschlagen: " + ex.Message);
        }
    }

    private void RestoreLastGood()
    {
        try
        {
            var broken = ConfigWriter.RestoreLastGood(Document);
            foreach (var f in _all) f.LoadFromModel();
            RaiseUserFileState();
            RefreshFavoriteViews();
            Info("Letzter guter Stand ist zurück" + (broken != null ? $", die kaputte Fassung liegt als {Path.GetFileName(broken)} daneben." : "."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ConfigEditorException or InvalidOperationException)
        {
            Info("Wiederherstellen fehlgeschlagen: " + ex.Message);
        }
    }

    private void RaiseUserFileState()
    {
        OnPropertyChanged(nameof(UserFileError));
        OnPropertyChanged(nameof(HasUserFileError));
        OnPropertyChanged(nameof(CanRestoreLastGood));
        OnPropertyChanged(nameof(RestoreLastGoodLabel));
        OnPropertyChanged(nameof(ChangedCount));
        OnPropertyChanged(nameof(ChangedLabel));
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
