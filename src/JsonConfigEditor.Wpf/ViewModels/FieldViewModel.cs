using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Input;
using System.Windows.Threading;
using JsonConfigEditor.Core;
using Microsoft.Win32;

namespace JsonConfigEditor.Wpf.ViewModels;

/// <summary>Eine Karte. Favorit und Abschnitt zeigen dieselbe Instanz, also immer denselben Wert.</summary>
public sealed class FieldViewModel : ObservableObject
{
    private readonly EditorViewModel _owner;
    private readonly DispatcherTimer _commitTimer;
    private string _text = "";
    private string? _error;
    private PathCheckResult? _pathCheck;
    private bool _isVisible = true;
    private bool _pending;

    public FieldViewModel(ConfigField field, EditorViewModel owner)
    {
        Field = field;
        _owner = owner;
        _commitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _commitTimer.Tick += (_, _) => FlushPending();
        ResetCommand = new RelayCommand(Reset, () => CanReset);
        BrowseCommand = new RelayCommand(Browse, () => CanEdit);
        OpenInEditorCommand = new RelayCommand(() => _owner.OpenUserFile());
        OpenFolderCommand = new RelayCommand(OpenFolder, () => ResolvedPath != null);
        CopyPathCommand = new RelayCommand(CopyPath, () => ResolvedPath != null);
        LoadFromModel();
    }

    public ConfigField Field { get; }
    public string Key => Field.Key;
    public string Label => Field.Label;
    public string? Description => Field.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Field.Description);
    public string? Unit => Field.Unit;
    public string SectionLabel => _owner.SectionLabel(Field.Section);

    public bool IsBool => Field.Kind == FieldKind.Bool;
    public bool IsNumber => Field.Kind is FieldKind.Int or FieldKind.Number;
    public bool IsText => Field.Kind == FieldKind.String;
    public bool IsPath => Field.Kind == FieldKind.Path;
    public bool IsArray => Field.Kind is FieldKind.Array or FieldKind.Object;

    public bool IsLocked => Field.IsLocked;
    public bool RestartRequired => Field.RestartRequired;
    public bool CanEdit => Field.IsEditable && !_owner.IsReadOnly;
    public bool IsReadOnlyField => !CanEdit;
    /// <summary>Schloss nur bei IT-Sperre oder read-only, nicht bei Listen (die gehen im Texteditor).</summary>
    public bool ShowLock => Field.IsLocked || Field.ReadOnly || _owner.IsReadOnly;
    public bool IsChanged => Field.IsChanged;
    public bool CanReset => Field.HasUser && CanEdit;

    public string LockToolTip => Field.IsLocked ? "Gesperrt durch die IT" : Field.ReadOnly ? "Nur lesen" : "";

    public string OriginText => Field.Origin switch
    {
        ValueOrigin.Machine => "IT-Vorgabe",
        ValueOrigin.User => Field.IsChanged ? "Eigener Wert" : "= Standard",
        ValueOrigin.Locked => "IT gesperrt",
        _ => "",
    };
    public bool ShowOrigin => OriginText.Length > 0;
    public bool OriginIsUser => Field.Origin == ValueOrigin.User;

    public string DefaultText
    {
        get
        {
            if (!Field.HasDefault) return "";
            var d = Field.DefaultValue;
            var s = d is JsonValue v && v.TryGetValue<string>(out var str) ? str : d?.ToJsonString() ?? "null";
            return s.Length == 0 && Field.EmptyMeans != null ? $"Standard: leer = {Field.EmptyMeans}" : $"Standard: {s}";
        }
    }

    // ---------------------------------------------------------------- Werte

    public bool BoolValue
    {
        get => Field.BoolValue ?? false;
        set
        {
            if (value == BoolValue || !CanEdit) return;
            Commit(JsonValue.Create(value));
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value)) return;
            if (!CanEdit) return;
            if (FieldValidator.TryParse(Field, value, CultureInfo.CurrentCulture, out _, out var err) || IsPath)
            {
                Error = null;
                _pending = true;
                _commitTimer.Stop();
                _commitTimer.Start();
            }
            else
            {
                _pending = false;
                _commitTimer.Stop();
                Error = err;
            }
            OnPropertyChanged(nameof(InfoText));
        }
    }

    public string ArraySummary => Field.ArrayCount == 1 ? "1 Eintrag" : $"{Field.ArrayCount} Einträge";

    public string? Error
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value)) OnPropertyChanged(nameof(HasError));
        }
    }
    public bool HasError => _error != null;

    /// <summary>zeroMeans / emptyMeans / Standardwert als graue Info.</summary>
    public string? InfoText
    {
        get
        {
            if (IsNumber && Field.ZeroMeans != null && double.TryParse(_text, NumberStyles.Float, CultureInfo.CurrentCulture, out var d) && d == 0)
                return $"0 = {Field.ZeroMeans}";
            if ((IsText || IsPath) && Field.EmptyMeans != null && string.IsNullOrWhiteSpace(_text))
                return $"leer = {Field.EmptyMeans}";
            return null;
        }
    }

    public string? PathStatus => _pathCheck?.Message;
    public string? ExpandedPath =>
        _pathCheck?.Expanded is { } e && !string.Equals(e, _text, StringComparison.OrdinalIgnoreCase) ? e : null;
    public bool PathOk => _pathCheck?.Severity == PathSeverity.Ok;
    public bool PathWarning => _pathCheck?.Severity == PathSeverity.Warning;
    public bool PathError => _pathCheck?.Severity == PathSeverity.Error;
    public bool HasPathStatus => IsPath && _pathCheck != null && _pathCheck.Severity != PathSeverity.Info;

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    public ICommand ResetCommand { get; }
    public ICommand BrowseCommand { get; }
    public ICommand OpenInEditorCommand { get; }
    /// <summary>Pfadfeld: Ordner im Explorer öffnen (bei Dateien: Ordner mit markierter Datei).</summary>
    public ICommand OpenFolderCommand { get; }
    public ICommand CopyPathCommand { get; }

    /// <summary>Wirksamer Pfad (Umgebungsvariablen und relativ aufgelöst), null wenn leer.</summary>
    public string? ResolvedPath => !IsPath ? null
        : _pathCheck?.Expanded is { Length: > 0 } e ? e
        : string.IsNullOrWhiteSpace(_text) ? null
        : Environment.ExpandEnvironmentVariables(_text.Trim());

    public string OpenFolderTooltip => Field.PathKind == PathKind.File ? "Ordner öffnen, Datei markieren" : "Ordner öffnen";

    // ---------------------------------------------------------------- Ablauf

    /// <summary>Wert aus dem Modell neu anzeigen (nach Laden, Reset, externer Änderung).</summary>
    public void LoadFromModel()
    {
        _commitTimer.Stop();
        _pending = false;
        _text = FieldValidator.Format(Field, CultureInfo.CurrentCulture);
        _error = null;
        CheckPath();
        OnAllChanged();
    }

    /// <summary>Von außen geändert: übernehmen, außer der User tippt gerade in genau diesem Feld.</summary>
    public void RefreshFromModel()
    {
        if (_pending) return;
        LoadFromModel();
    }

    /// <summary>Beim Verlassen des Felds sofort speichern.</summary>
    public void FlushPending()
    {
        _commitTimer.Stop();
        if (!_pending) return;
        _pending = false;
        if (!FieldValidator.TryParse(Field, _text, CultureInfo.CurrentCulture, out var value, out var err))
        {
            Error = err;
            CheckPath();
            return;
        }
        Commit(value);
    }

    private void Commit(JsonNode? value)
    {
        try
        {
            ConfigWriter.Set(_owner.Document, Key, value);
            Error = null;
            _owner.OnFieldSaved(this);
        }
        catch (Exception ex) when (ex is ConfigEditorException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Error = "Nicht gespeichert: " + ex.Message;
            Log.Warn($"{Key} nicht gespeichert ({_owner.Document.Paths.UserFile}): {ex.Message}");
        }
        CheckPath();
        RaiseState();
    }

    private void Reset()
    {
        try
        {
            ConfigWriter.Reset(_owner.Document, Key);
            _owner.OnFieldSaved(this);
            LoadFromModel();
        }
        catch (Exception ex) when (ex is ConfigEditorException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Error = "Nicht zurückgesetzt: " + ex.Message;
            Log.Warn($"{Key} nicht zurückgesetzt: {ex.Message}");
        }
    }

    private void Browse()
    {
        var current = _pathCheck?.Expanded ?? Environment.ExpandEnvironmentVariables(_text);
        if (Field.PathKind == PathKind.Folder)
        {
            var dlg = new OpenFolderDialog { Title = Label, Multiselect = false };
            if (Directory.Exists(current)) dlg.InitialDirectory = current;
            if (dlg.ShowDialog() == true) ApplyBrowsed(dlg.FolderName);
        }
        else
        {
            var dlg = new OpenFileDialog { Title = Label, CheckFileExists = Field.MustExist };
            var dir = Path.GetDirectoryName(current);
            if (dir != null && Directory.Exists(dir)) dlg.InitialDirectory = dir;
            if (dlg.ShowDialog() == true) ApplyBrowsed(dlg.FileName);
        }
    }

    private void OpenFolder()
    {
        if (ResolvedPath is not { } path) return;
        try
        {
            if (Field.PathKind == PathKind.File && File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return;
            }
            var wanted = Field.PathKind == PathKind.File || File.Exists(path) ? Path.GetDirectoryName(path) : path;
            // Ordner gibt's (noch) nicht: nächsten vorhandenen darüber öffnen
            var dir = wanted;
            while (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) dir = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(dir))
            {
                _owner.Info($"{wanted} gibt's nicht.");
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            if (!string.Equals(dir, wanted, StringComparison.OrdinalIgnoreCase))
                _owner.Info($"{wanted} gibt's noch nicht, geöffnet: {dir}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or ArgumentException or InvalidOperationException)
        {
            _owner.Info("Ordner nicht zu öffnen: " + ex.Message);
        }
    }

    private void CopyPath()
    {
        if (ResolvedPath is not { } path) return;
        try
        {
            System.Windows.Clipboard.SetText(path);
            _owner.Info("Pfad kopiert: " + path);
        }
        catch (System.Runtime.InteropServices.ExternalException ex) { _owner.Info("Zwischenablage gerade belegt: " + ex.Message); }
    }

    private void ApplyBrowsed(string path)
    {
        Text = path;
        FlushPending();
    }

    private void CheckPath()
    {
        if (!IsPath) return;
        var baseDir = Path.GetDirectoryName(_owner.Document.Paths.BaseFile);
        _pathCheck = PathChecker.Check(Field, _text, baseDir);
        OnPropertyChanged(nameof(PathStatus));
        OnPropertyChanged(nameof(ExpandedPath));
        OnPropertyChanged(nameof(PathOk));
        OnPropertyChanged(nameof(PathWarning));
        OnPropertyChanged(nameof(PathError));
        OnPropertyChanged(nameof(HasPathStatus));
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(OriginText));
        OnPropertyChanged(nameof(ShowOrigin));
        OnPropertyChanged(nameof(OriginIsUser));
        OnPropertyChanged(nameof(CanReset));
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(InfoText));
        CommandManager.InvalidateRequerySuggested();
    }

    public bool Matches(string search) =>
        Label.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || Key.Contains(search, StringComparison.OrdinalIgnoreCase)
        || (Description?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || SectionLabel.Contains(search, StringComparison.CurrentCultureIgnoreCase);
}
