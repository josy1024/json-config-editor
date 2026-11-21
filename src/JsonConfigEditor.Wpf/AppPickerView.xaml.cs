using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JsonConfigEditor.Core;
using JsonConfigEditor.Wpf.ViewModels;

namespace JsonConfigEditor.Wpf;

/// <summary>Startseite: bekannte Apps, "Andere Datei öffnen…", Drag &amp; Drop.</summary>
public partial class AppPickerView : UserControl
{
    private const string FluentDictionary = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml";

    public AppPickerView(CatalogLocations? locations = null) : this(new AppPickerViewModel(locations)) { }

    public AppPickerView(AppPickerViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    public AppPickerViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (TryFindResource("AccentFillColorDefaultBrush") == null)
        {
            try { Resources.MergedDictionaries.Insert(0, new ResourceDictionary { Source = new Uri(FluentDictionary) }); }
            catch (Exception) { /* Standard-WPF-Optik */ }
        }
        if (List.SelectedItem != null)
        {
            List.UpdateLayout();
            (List.ItemContainerGenerator.ContainerFromItem(List.SelectedItem) as ListBoxItem)?.Focus();
        }
        else List.Focus();
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // nur Doppelklick auf eine Zeile, nicht auf die Scrollbar
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(List, d) is ListBoxItem)
            ViewModel.OpenSelected();
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.OpenSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && ViewModel.Selected is { CanForget: true } s)
        {
            ViewModel.ForgetCommand.Execute(s);
            e.Handled = true;
        }
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            ViewModel.OpenPath(files[0]);
        e.Handled = true;
    }
}
