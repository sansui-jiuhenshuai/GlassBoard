using System;
using System.Linq;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GlassBoard;

public partial class Launchpad : GlassWindow
{
    private const string All = "全部", ItemFormat = "GlassBoard.Target";
    private const int PageSize = 18;
    private readonly DockWindow dock;
    private readonly BoardLibrary library;
    private string selected = All;
    private int page;
    private Point press;
    private Button? dragSource;
    private OutsideClick? outsideClick;
    private readonly Stopwatch collapse = new();
    private Rect expanded;
    private Border? expandedSurface;
    private SilkFold? fold;
    internal bool IsDismissing => collapse.IsRunning;
    private readonly DispatcherTimer saveGlass = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer turnPage = new() { Interval = TimeSpan.FromMilliseconds(550) };

    internal Launchpad(DockWindow dock, BoardLibrary library)
    {
        this.dock = dock;
        this.library = library;
        InitializeComponent();
        Owner = dock;
        GlassSlider.Value = library.State.Transparency;
        GlassPercent.Text = $"{library.State.Transparency}%";
        SetGlassTransparency(library.State.Transparency);
        GlassSlider.ValueChanged += PreviewTransparency;
        saveGlass.Tick += (_, _) => CommitGlass();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Refresh();
                try { outsideClick = new OutsideClick(this, dock, Dismiss); }
                catch (Win32Exception) { Deactivated += DismissOnDeactivate; }
            }
            else
            {
                outsideClick?.Dispose(); outsideClick = null;
                Deactivated -= DismissOnDeactivate;
                CancelDismiss();
                CommitGlass();
                turnPage.Stop();
                Apps.ItemsSource = null;
                library.ReleasePanelIcons();
            }
        };
        foreach (var arrow in new[] { Previous, Next })
        {
            arrow.DragEnter += (_, e) =>
            {
                if (e.Data.GetDataPresent(ItemFormat, false)) { turnPage.Tag = arrow; turnPage.Start(); }
            };
            arrow.DragLeave += (_, _) => turnPage.Stop();
        }
        turnPage.Tick += (_, _) =>
        {
            turnPage.Stop();
            if (turnPage.Tag is Button { IsEnabled: true } arrow)
            { page += arrow == Next ? 1 : -1; Filter(); }
        };
        StartAtLogin.IsChecked = App.StartupEnabled;
        StartAtLogin.Checked += ChangeStartup;
        StartAtLogin.Unchecked += ChangeStartup;
        LockPosition.IsChecked = library.State.PositionLocked;
        LockPosition.Checked += ChangeLock;
        LockPosition.Unchecked += ChangeLock;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        library.Changed += Refresh;
        Closed += (_, _) => { library.Changed -= Refresh; outsideClick?.Dispose(); CompositionTarget.Rendering -= CollapseFrame; };
        Refresh();
    }

    internal void FocusSearch() => Search.Focus();

    private void DismissOnDeactivate(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() => { if (!IsActive && !dock.IsActive && !Settings.IsOpen) Dismiss(); });

    internal void Dismiss()
    {
        if (!IsVisible || IsDismissing) return;
        Settings.IsOpen = false;
        if (!SystemParameters.ClientAreaAnimation) { Hide(); return; }
        expanded = new Rect(Left, Top, ActualWidth, ActualHeight);
        var local = PointFromScreen(dock.PointToScreen(new Point(dock.ActualWidth / 2, dock.ActualHeight / 2)));
        var destination = new Point(Left + local.X, Top + local.Y);
        var canvas = expanded;
        canvas.Union(destination);
        expandedSurface = (Border)Content;
        fold = new SilkFold(expandedSurface,
            new Rect(expanded.Left - canvas.Left, expanded.Top - canvas.Top, expanded.Width, expanded.Height),
            new Point(destination.X - canvas.Left, destination.Y - canvas.Top));
        ReleaseBackdrop();
        Content = fold;
        Left = canvas.Left; Top = canvas.Top; Width = canvas.Width; Height = canvas.Height;
        IsHitTestVisible = false;
        collapse.Restart();
        CompositionTarget.Rendering += CollapseFrame;
    }

    private void CollapseFrame(object? sender, EventArgs e)
    {
        double time = Math.Min(1, collapse.Elapsed.TotalMilliseconds / 360);
        if (time >= 1) { Hide(); return; }
        fold?.Advance(time);
        Opacity = 1 - Math.Clamp((time - 0.8) / 0.2, 0, 1);
    }

    internal void CancelDismiss()
    {
        if (!IsDismissing) return;
        collapse.Reset();
        CompositionTarget.Rendering -= CollapseFrame;
        Content = expandedSurface;
        expandedSurface = null;
        fold = null;
        Opacity = 1;
        Width = expanded.Width; Height = expanded.Height; Left = expanded.Left; Top = expanded.Top;
        IsHitTestVisible = true;
        SetGlassTransparency((int)Math.Round(GlassSlider.Value));
    }

    private void ChangeLock(object sender, RoutedEventArgs e)
    {
        bool locked = LockPosition.IsChecked == true;
        if (locked != library.State.PositionLocked &&
            !dock.Edit(() => library.SetDockPosition(locked ? dock.Left + dock.Width / 2 : null, locked ? dock.Top : null)))
            LockPosition.IsChecked = library.State.PositionLocked;
    }

    private void ExitBoard(object sender, RoutedEventArgs e) => dock.Close();

    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        CountInput.Text = library.State.DockCount.ToString();
        Settings.IsOpen = true;
        CountInput.Focus();
        CountInput.SelectAll();
    }

    private void ApplyCount(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CountInput.Text, out int count) || count < 0)
        { CountInput.Text = library.State.DockCount.ToString(); CountInput.SelectAll(); return; }
        if (dock.Edit(() => library.SetDockCount(count))) Settings.IsOpen = false;
    }

    private void PreviewTransparency(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int percent = (int)Math.Round(e.NewValue);
        GlassPercent.Text = $"{percent}%";
        SetGlassTransparency(percent);
        dock.SetGlassTransparency(percent);
        saveGlass.Stop();
        saveGlass.Start();
    }

    private void CommitGlass()
    {
        if (!saveGlass.IsEnabled) return;
        saveGlass.Stop();
        if (!dock.Edit(() => library.SetTransparency((int)Math.Round(GlassSlider.Value))))
            GlassSlider.Value = library.State.Transparency;
    }

    private void PrepareMenu(object sender, RoutedEventArgs e) => dock.AttachMenu((Button)sender);

    private void OverTile(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(ItemFormat, false)) return;
        var tile = (Button)sender;
        tile.BorderThickness = e.GetPosition(tile).X < tile.ActualWidth / 2
            ? new Thickness(2,0,0,0) : new Thickness(0,0,2,0);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void LeaveTile(object sender, DragEventArgs e) => ((Button)sender).ClearValue(BorderThicknessProperty);

    private void DropOnTile(object sender, DragEventArgs e)
    {
        var tile = (Button)sender;
        tile.ClearValue(BorderThicknessProperty);
        if (tile.DataContext is DockItem anchor)
            Reorder(e, anchor, e.GetPosition(tile).X >= tile.ActualWidth / 2);
    }

    private void DropOnGrid(object sender, DragEventArgs e)
    {
        if (Apps.Items.OfType<DockItem>().LastOrDefault() is { } anchor) Reorder(e, anchor, true);
    }

    private void Reorder(DragEventArgs e, DockItem anchor, bool after)
    {
        if (e.Data.GetData(ItemFormat, false) is not string target) return;
        e.Effects = dock.Edit(() => library.Move(target, anchor.Target, after)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void ChangeStartup(object sender, RoutedEventArgs e)
    {
        if (!dock.Edit(() => App.SetStartup(StartAtLogin.IsChecked == true)))
        {
            StartAtLogin.Checked -= ChangeStartup;
            StartAtLogin.Unchecked -= ChangeStartup;
            try { StartAtLogin.IsChecked = App.StartupEnabled; }
            finally
            {
                StartAtLogin.Checked += ChangeStartup;
                StartAtLogin.Unchecked += ChangeStartup;
            }
        }
    }

    private void Refresh()
    {
        if (!IsVisible) return;
        if (selected != All && !library.State.Categories.Contains(selected))
        { selected = All; page = 0; }
        Tabs.Children.Clear();
        foreach (string category in new[] { All }.Concat(library.State.Categories))
        {
            var tab = new Button { Content = category, Tag = category, AllowDrop = category != All };
            if (category == selected) tab.Background = new SolidColorBrush(Color.FromRgb(64, 93, 140));
            tab.Click += (_, _) => { selected = category; page = 0; Refresh(); };
            if (category != All) tab.Drop += ReceiveDrop;
            if (category != All)
            {
                var remove = new MenuItem { Header = "删除分类", ToolTip = "其中的应用仍显示在全部中" };
                remove.Click += (_, _) => dock.Edit(() => library.RemoveCategory(category));
                tab.ContextMenu = new GlassContextMenu(this);
                tab.ContextMenu.Items.Add(remove);
            }
            Tabs.Children.Add(tab);
        }
        Filter();
    }

    private void Filter()
    {
        if (Apps == null) return;
        var items = library.State.Items
            .Where(i => selected == All || i.Category == selected)
            .Select(i => library.Icon(i.Target)).OfType<DockItem>()
            .Where(i => i.Name.Contains(Search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList();
        int pages = Math.Max(1, (items.Count + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 0, pages - 1);
        Apps.ItemsSource = items.Skip(page * PageSize).Take(PageSize).ToList();
        PageNumber.Text = $"{page + 1} / {pages}";
        Previous.IsEnabled = page > 0;
        Next.IsEnabled = page + 1 < pages;
        Empty.Text = items.Count != 0 ? "" : Search.Text.Length != 0 ? "没有匹配的应用" : "拖入应用以添加到此分类";
    }

    private void CreateCategory(object sender, RoutedEventArgs e)
    {
        string name = CategoryName.Text.Trim();
        if (name.Length == 0) { CategoryName.Focus(); return; }
        if (!dock.Edit(() => library.AddCategory(name))) return;
        selected = library.State.Categories.FirstOrDefault(c => c.Equals(name, StringComparison.CurrentCultureIgnoreCase)) ?? name;
        CategoryName.Clear();
        page = 0;
        Refresh();
    }

    private void PressApp(object sender, MouseButtonEventArgs e)
    {
        dragSource = (Button)sender;
        press = e.GetPosition(this);
        if (e.ClickCount == 2 && dragSource.DataContext is DockItem item)
        {
            dragSource = null;
            dock.Launch(item);
            e.Handled = true;
        }
    }

    private void DragApp(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender != dragSource ||
            dragSource?.DataContext is not DockItem item) return;
        var delta = e.GetPosition(this) - press;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragSource = null;
        DragDrop.DoDragDrop((Button)sender, new DataObject(ItemFormat, item.Target), DragDropEffects.Move);
    }

    private void CheckDrop(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(ItemFormat, false) ? e.AllowedEffects & DragDropEffects.Move :
            ShellDrop.Supports(e.Data) ? DockWindow.ImportEffect(e.AllowedEffects) : DragDropEffects.None;
        e.Handled = true;
    }

    private void ReceiveDrop(object sender, DragEventArgs e)
    {
        string category = (sender as Button)?.Tag as string ?? selected;
        string? destination = category == All ? null : category;
        if (e.Data.GetData(ItemFormat, false) is string target)
        {
            e.Effects = category != All && dock.Edit(() => library.Categorize(target, destination))
                ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }
        else dock.Import(e, false, destination);
    }

    private void AppKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is Button { DataContext: DockItem item }) dock.Launch(item);
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { page = 0; Filter(); }
    private void PreviousPage(object sender, RoutedEventArgs e) { page--; Filter(); }
    private void NextPage(object sender, RoutedEventArgs e) { page++; Filter(); }
    private void HidePanel(object sender, RoutedEventArgs e) => Dismiss();
    private void PanelKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; }
        if (e.Key == Key.Enter && CategoryName.IsKeyboardFocusWithin) { CreateCategory(sender, e); e.Handled = true; }
    }
}
