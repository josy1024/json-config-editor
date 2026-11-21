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
