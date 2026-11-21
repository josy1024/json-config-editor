using System.Windows;
using JsonConfigEditor.Core;

namespace JsonConfigEditor.Wpf;

/// <summary>Startseite als Fenster. <see cref="Choose"/> gibt die gewählte App zurück oder null (abgebrochen).</summary>
public sealed class AppPickerWindow : Window
{
    public AppPickerWindow(CatalogLocations? locations = null)
    {
        ThemeMode = ThemeMode.System;
        if (AppInfo.Icon is { } icon) Icon = icon;
        View = new AppPickerView(locations);
        Content = View;
        Title = "json-config-editor";
        Width = 860;
        Height = 620;
        MinWidth = 520;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        View.ViewModel.Chosen += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
    }

    public AppPickerView View { get; }

    public ConfigEditorOptions? Result => View.ViewModel.Result;

    /// <summary>Modal zeigen. null = Fenster ohne Auswahl geschlossen.</summary>
    public static ConfigEditorOptions? Choose(Window? owner = null, CatalogLocations? locations = null)
    {
        var w = new AppPickerWindow(locations);
        if (owner != null)
        {
            w.Owner = owner;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        return w.ShowDialog() == true ? w.Result : null;
    }
}
