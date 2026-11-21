using System.Windows;
using JsonConfigEditor.Core;
using JsonConfigEditor.Wpf.ViewModels;

namespace JsonConfigEditor.Wpf;

/// <summary>Fertiges Fenster mit Fluent-Theme (hell/dunkel nach Windows).</summary>
public sealed class ConfigEditorWindow : Window
{
    public ConfigEditorWindow(ConfigEditorOptions options)
    {
        ThemeMode = ThemeMode.System;
        if (AppInfo.Icon is { } icon) Icon = icon; // Host kann's danach überschreiben
        View = new ConfigEditorView(options);
        Content = View;
        Title = View.ViewModel.Title;
        Width = 1180;
        Height = 820;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        View.Saved += (s, e) => Saved?.Invoke(this, e);
        Closing += OnClosing;
        Closed += (_, _) => View.Dispose();
    }

    public ConfigEditorView View { get; }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var vm = View.ViewModel;
        vm.FlushAll(); // versucht auch nicht gespeicherte Werte nochmal
        var unsaved = vm.UnsavedFields;
        if (unsaved.Count == 0) return;

        var names = string.Join(Environment.NewLine, unsaved.Take(5).Select(f => "• " + f.Label));
        var answer = MessageBox.Show(this,
            $"Diese Werte sind noch nicht gespeichert, die Datei ist gerade gesperrt:{Environment.NewLine}{names}{Environment.NewLine}{Environment.NewLine}"
            + "Trotzdem schließen? Dann gehen sie verloren.",
            Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer == MessageBoxResult.No) e.Cancel = true;
        else Log.Warn($"Geschlossen mit {unsaved.Count} nicht gespeicherten Werten: {string.Join(", ", unsaved.Select(f => f.Key))}");
    }

    /// <summary>Nach jedem gespeicherten Wert.</summary>
    public event EventHandler<SavedEventArgs>? Saved;

    /// <summary>Nicht-modal zeigen.</summary>
    public static ConfigEditorWindow Show(ConfigEditorOptions options, Window? owner = null)
    {
        var w = Create(options, owner);
        w.Show();
        return w;
    }

    /// <summary>Modal zeigen, kehrt nach dem Schließen zurück.</summary>
    public static ConfigEditorWindow ShowDialog(ConfigEditorOptions options, Window? owner = null)
    {
        var w = Create(options, owner);
        w.ShowDialog();
        return w;
    }

    private static ConfigEditorWindow Create(ConfigEditorOptions options, Window? owner)
    {
        var w = new ConfigEditorWindow(options);
        if (owner != null) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w;
    }

    /// <summary>Fenster nach vorne holen, auch wenn es minimiert ist.</summary>
    public void Activate(string? section)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        if (section != null) View.ShowSection(section);
    }
}
