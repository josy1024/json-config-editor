using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using JsonConfigEditor.Core;

namespace JsonConfigEditor.Wpf.ViewModels;

/// <summary>Eine Zeile auf der Startseite.</summary>
public sealed class AppEntryViewModel(AppEntry entry)
{
    public AppEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string Path => Entry.Target;
    public bool IsAvailable => Entry.IsAvailable;
    public string? Problem => Entry.Problem;
    public bool HasProblem => Entry.Problem != null;
    public bool CanForget => Entry.Source == AppSource.Recent;

    public string SourceLabel => Entry.Source switch
    {
        AppSource.Machine => "IT",
        AppSource.Bundled => "Mitgeliefert",
        AppSource.User => "Meine Liste",
        AppSource.Found => "Gefunden",
        _ => "Zuletzt",
    };

    public string SourceTooltip => Entry.Source switch
    {
        AppSource.Machine => @"Vorgabe der IT: %ProgramData%\JsonConfigEditor\apps.json",
        AppSource.Bundled => "apps.json neben JsonConfigEditor.exe",
        AppSource.User => @"Eigene Liste: %APPDATA%\JsonConfigEditor\apps.json",
        AppSource.Found => "appsettings.editor.json neben der EXE oder im aktuellen Ordner",
        _ => "Zuletzt geöffnet",
    };

    public string? LastOpenedText => Entry.LastOpened is { } t
        ? "zuletzt " + t.ToString("g", CultureInfo.CurrentCulture)
        : null;

    public bool HasLastOpened => Entry.LastOpened != null;
    public string Glyph => Entry.Descriptor != null ? "" : ""; // Zahnrad bzw. Dokument
}

/// <summary>
/// Startseite ohne Parameter: bekannte Apps zum Auswählen, "Andere Datei öffnen…", Drag &amp; Drop.
/// Ergebnis steht in <see cref="Result"/>, <see cref="Chosen"/> meldet die Auswahl.
/// </summary>
public sealed class AppPickerViewModel : ObservableObject
{
    private readonly CatalogLocations? _locations;
    private AppCatalog _catalog;
    private AppEntryViewModel? _selected;
    private string? _error;

    public AppPickerViewModel(CatalogLocations? locations = null)
    {
        _locations = locations;
        _catalog = AppCatalog.Load(locations);
        OpenCommand = new RelayCommand(OpenSelected, () => CanOpen);
        BrowseCommand = new RelayCommand(Browse);
        EditListCommand = new RelayCommand(EditList);
        RefreshCommand = new RelayCommand(Refresh);
        ForgetCommand = new RelayCommand(p => Forget(p as AppEntryViewModel));
        Fill();
    }

    public ObservableCollection<AppEntryViewModel> Entries { get; } = [];
    public IReadOnlyList<string> Warnings => _catalog.Warnings;
    public bool HasWarnings => Warnings.Count > 0;
    public bool IsEmpty => Entries.Count == 0;
    public string Version => AppInfo.Version;
    public string VersionText => AppInfo.VersionText;
    public string RepoUrl => AppInfo.RepoUrl;
    public ICommand OpenRepoCommand { get; } = new RelayCommand(() => AppInfo.OpenUrl(AppInfo.RepoUrl));
    public string UserListFile => _catalog.UserListFile;

    public AppEntryViewModel? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value)) OnPropertyChanged(nameof(CanOpen));
        }
    }

    public bool CanOpen => Selected?.IsAvailable == true;

    public string? Error
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => Error != null;

    /// <summary>Gewählte App bzw. Datei, null solange nichts gewählt.</summary>
    public ConfigEditorOptions? Result { get; private set; }

    public event EventHandler? Chosen;

    public ICommand OpenCommand { get; }
    public ICommand BrowseCommand { get; }
    public ICommand EditListCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ForgetCommand { get; }

    /// <summary>Datei oder Ordner (Durchsuchen, Drag &amp; Drop) öffnen. false + <see cref="Error"/> wenn's nicht passt.</summary>
    public bool OpenPath(string path)
    {
        var o = new ConfigEditorOptions();
        try
        {
            OpenTarget.Apply(o, path);
            ConfigPaths.Resolve(o); // Pfadfehler gleich hier zeigen statt im Editor
        }
        catch (ConfigEditorException ex)
        {
            Error = ex.Message;
            Log.Warn($"Startseite: {path} nicht geöffnet: {ex.Message}");
            return false;
        }
        Choose(o);
        return true;
    }

    public void OpenSelected()
    {
        if (Selected is not { } s) return;
        if (!s.IsAvailable)
        {
            Error = $"{s.Problem}: {s.Path}";
            return;
        }
        Choose(s.Entry.ToOptions());
    }

    private void Choose(ConfigEditorOptions o)
    {
        Error = null;
        Result = o;
        Chosen?.Invoke(this, EventArgs.Empty);
    }

    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Einstellungen öffnen",
            Filter = "Einstellungen (*.json)|*.json|Alle Dateien (*.*)|*.*",
            CheckFileExists = true,
        };
        if (Selected?.Path is { Length: > 0 } p && System.IO.Path.GetDirectoryName(p) is { } dir && Directory.Exists(dir))
            dlg.InitialDirectory = dir;
        if (dlg.ShowDialog() == true) OpenPath(dlg.FileName);
    }

    private void EditList()
    {
        try
        {
            var path = _catalog.EnsureUserList();
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception) { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true }); }
            Error = null;
        }
        catch (Exception ex)
        {
            Error = "Liste konnte nicht geöffnet werden: " + ex.Message;
        }
    }

    private void Refresh()
    {
        _catalog = AppCatalog.Load(_locations);
        Error = null;
        Fill();
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(HasWarnings));
    }

    private void Forget(AppEntryViewModel? e)
    {
        if (e == null || !e.CanForget) return;
        AppCatalog.ForgetRecent(e.Entry, _locations);
        Refresh();
    }

    private void Fill()
    {
        Entries.Clear();
        foreach (var e in _catalog.Entries) Entries.Add(new AppEntryViewModel(e));
        Selected = Entries.FirstOrDefault(e => e.IsAvailable);
        OnPropertyChanged(nameof(IsEmpty));
    }
}
