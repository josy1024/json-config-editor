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
        Closing += (_, _) => View.ViewModel.FlushAll();
        Closed += (_, _) => View.Dispose();
    }

    public ConfigEditorView View { get; }

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
