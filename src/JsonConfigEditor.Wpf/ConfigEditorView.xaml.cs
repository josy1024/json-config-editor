using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using JsonConfigEditor.Core;
using JsonConfigEditor.Wpf.ViewModels;

namespace JsonConfigEditor.Wpf;

/// <summary>
/// Der Editor als UserControl zum Einbetten. Bringt sein Fluent-Theme selbst mit,
/// wenn das Host-Fenster keins hat.
/// </summary>
public partial class ConfigEditorView : UserControl, IDisposable
{
    private const string FluentDictionary = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml";
    private const double NavActivationOffset = 56;
    private bool _scrolledInitially;
    private bool _syncingNavFromScroll;

    public ConfigEditorView(ConfigEditorOptions options) : this(new EditorViewModel(options)) { }

    public ConfigEditorView(EditorViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        viewModel.Saved += (s, e) => Saved?.Invoke(this, e);
        Loaded += OnLoaded;
    }

    public EditorViewModel ViewModel { get; }

    /// <summary>Nach jedem gespeicherten Wert.</summary>
    public event EventHandler<SavedEventArgs>? Saved;

    /// <summary>Zum Abschnitt springen (Name oder Label).</summary>
    public void ShowSection(string? name)
    {
        var section = ViewModel.FindSection(name);
        if (section == null) return;
        Nav.SelectedItem = ViewModel.NavItems.FirstOrDefault(n => n.Target == section);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Host ohne Fluent-Theme: Farben fehlen, dann selbst einbinden
        if (TryFindResource("AccentFillColorDefaultBrush") == null)
        {
            try { Resources.MergedDictionaries.Insert(0, new ResourceDictionary { Source = new Uri(FluentDictionary) }); }
            catch (Exception) { /* dann eben Standard-WPF-Optik */ }
        }
        if (!_scrolledInitially)
        {
            _scrolledInitially = true;
            if (ViewModel.Options.InitialSection is { } s) Dispatcher.BeginInvoke(() => ShowSection(s), System.Windows.Threading.DispatcherPriority.Loaded);
            else Search.Focus();
        }
    }

    private void OnNavSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingNavFromScroll) return;
        if (Nav.SelectedItem is not NavItem item) return;
        if (item.Target is SectionViewModel section)
        {
            // Filter-Chip gilt evtl. für einen anderen Abschnitt: dann auf "Alle", sonst ist das Ziel unsichtbar
            var chip = ViewModel.Chips.FirstOrDefault(c => c.IsSelected);
            if (chip?.Section != null && chip.Section != section) ViewModel.Chips[0].IsSelected = true;
            UpdateLayout();
            if (SectionList.ItemContainerGenerator.ContainerFromItem(section) is FrameworkElement el)
            {
                ScrollTo(el);
                PlaySectionMotion(el);
            }
        }
        else
        {
            var chip = ViewModel.Chips.FirstOrDefault(c => c.IsSelected);
            if (chip?.Section != null) ViewModel.Chips[0].IsSelected = true;
            UpdateLayout();
            ScrollTo(FavoritesBlock);
        }
    }

    private void OnScrollerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Scroller.ViewportHeight <= 0) return;

        SectionViewModel? activeSection = null;
        SectionViewModel? firstVisibleSection = null;
        SectionViewModel? lastVisibleSection = null;
        foreach (var section in ViewModel.Sections.Where(s => s.IsVisible))
        {
            if (SectionList.ItemContainerGenerator.ContainerFromItem(section) is not FrameworkElement element ||
                element.Visibility != Visibility.Visible)
                continue;

            firstVisibleSection ??= section;
            lastVisibleSection = section;
            var top = GetTopInScroller(element);
            if (top is <= NavActivationOffset) activeSection = section;
        }

        if (Scroller.ScrollableHeight > 0 && Scroller.VerticalOffset >= Scroller.ScrollableHeight - 1)
            activeSection = lastVisibleSection;

        object? target = activeSection;
        if (target == null)
        {
            var favoritesTop = FavoritesBlock.Visibility == Visibility.Visible ? GetTopInScroller(FavoritesBlock) : null;
            var favoritesAtAnchor = favoritesTop <= NavActivationOffset &&
                                    favoritesTop + FavoritesBlock.ActualHeight > NavActivationOffset;
            target = favoritesAtAnchor ? "favorites" : firstVisibleSection;
        }
        var navItem = ViewModel.NavItems.FirstOrDefault(n => Equals(n.Target, target));
        if (navItem == null || ReferenceEquals(Nav.SelectedItem, navItem)) return;

        _syncingNavFromScroll = true;
        try { Nav.SelectedItem = navItem; }
        finally { _syncingNavFromScroll = false; }
    }

    private double? GetTopInScroller(FrameworkElement element)
    {
        try { return element.TransformToAncestor(Scroller).Transform(new Point(0, 0)).Y; }
        catch (InvalidOperationException) { return null; }
    }

    private static void PlaySectionMotion(FrameworkElement element)
    {
        var transform = new TranslateTransform();
        element.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void ScrollTo(FrameworkElement element)
    {
        if (element.Visibility != Visibility.Visible) return;
        try
        {
            var pos = element.TransformToAncestor(CardsPanel).Transform(new Point(0, 0));
            Scroller.ScrollToVerticalOffset(Math.Max(0, pos.Y - 4));
        }
        catch (InvalidOperationException) { element.BringIntoView(); }
    }

    private void OnScrollerSizeChanged(object sender, SizeChangedEventArgs e) =>
        ViewModel.Columns = e.NewSize.Width >= 860 ? 2 : 1;

    private void OnFieldLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FieldViewModel f) f.FlushPending();
    }

    private void OnFieldKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if ((sender as FrameworkElement)?.DataContext is FieldViewModel f) f.FlushPending();
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // F3 = Debug-Infos, Esc schließt sie
        if (e.Key == Key.F3)
        {
            ViewModel.ShowDebug = !ViewModel.ShowDebug;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && ViewModel.ShowDebug)
        {
            ViewModel.ShowDebug = false;
            e.Handled = true;
        }
        // Strg+F = Suche, Esc in der Suche = leeren
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Search.Focus();
            Search.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Search.IsKeyboardFocusWithin && ViewModel.HasSearch)
        {
            ViewModel.SearchText = "";
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    public void Dispose()
    {
        ViewModel.FlushAll();
        ViewModel.Dispose();
    }

    internal static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            if (FindChild<T>(child) is { } found) return found;
        }
        return null;
    }
}
