using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace GlassBoard;

public partial class DockWindow : GlassWindow
{
    private readonly BoardLibrary library;
    private Launchpad? panel;
    private Point? press;
    private DockItem? pressedItem;
    private bool panelPressed;
    private int clickCount;

    public DockWindow() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                            "GlassBoard", "board.json")) { }

    internal DockWindow(string file)
    {
        library = new(file);
        InitializeComponent();
        Rect area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - Height - 12;
        ContentRendered += LoadBoard;
        LocationChanged += (_, _) => { if (panel?.IsVisible == true) AnchorPanel(); };
        SizeChanged += (_, _) => { if (panel?.IsVisible == true) AnchorPanel(); };
    }

    private void LoadBoard(object? sender, EventArgs e)
    {
        ContentRendered -= LoadBoard;
        _ = new DockTaskbar(this, ShowPanel);
        try { library.Load(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, $"无法读取配置。请检查配置文件后重新启动。\n{ex.Message}", Title);
            Close();
            return;
        }
        library.Changed += RefreshDock;
        RefreshDock();
        if (library.State is { LockedCenter: double center, LockedTop: double top })
        {
            double left = Math.Clamp(center - Width / 2, SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
            Left = left;
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
        }
    }

    private void RefreshDock()
    {
        SetGlassTransparency(library.State.Transparency);
        var items = library.State.Items.Select(i => library.Icon(i.Target)).OfType<DockItem>()
            .Take(library.State.DockCount!.Value).ToList();
        DroppedItems.ItemsSource = items;
        DroppedItems.Width = 76d * items.Count;
        double oldWidth = Width;
        Width = Math.Min(SystemParameters.WorkArea.Width, 76 + 76d * items.Count);
        Left += (oldWidth - Width) / 2;
    }

    private void PrepareMenu(object sender, RoutedEventArgs e) => AttachMenu((Button)sender);

    internal void AttachMenu(Button tile)
    {
        var menu = new GlassContextMenu(this);
        void Add(string name, Action<DockItem> action)
        {
            var command = new MenuItem { Header = name };
            command.Click += (_, _) => { if (tile.DataContext is DockItem item) action(item); };
            menu.Items.Add(command);
        }
        Add("打开", Launch);
        if (tile.DataContext is DockItem { ClassId: null })
            Add("打开文件所在位置", item =>
            {
                var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "explorer.exe"), $"/select,\u0022{item.Target}\u0022") { UseShellExecute = true };
                try { using var process = Process.Start(start); }
                catch (Win32Exception ex) { MessageBox.Show(this, ex.Message, "无法打开文件位置"); }
            });
        Add("移到最前", item => Edit(() => library.Move(item.Target, library.State.Items[0].Target, false)));
        menu.Items.Add(new Separator());
        Add("从 GlassBoard 移除", item => Edit(() => library.Remove(item.Target)));
        tile.ContextMenu = menu;
    }

    internal static DragDropEffects ImportEffect(DragDropEffects allowed) =>
        (allowed & DragDropEffects.Link) != 0 ? DragDropEffects.Link : allowed & DragDropEffects.Copy;

    private void CheckDrop(object sender, DragEventArgs e)
    {
        e.Effects = ShellDrop.Supports(e.Data) ? ImportEffect(e.AllowedEffects) : DragDropEffects.None;
        e.Handled = true;
    }

    private void ImportDrop(object sender, DragEventArgs e) => Import(e, true, null);

    internal void Import(DragEventArgs e, bool pin, string? category)
    {
        var effect = ImportEffect(e.AllowedEffects);
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (effect == DragDropEffects.None) return;
        try
        {
            var items = ShellDrop.Read(e.Data);
            if (items.Count > 0 && Edit(() => library.Import(items, pin, category))) e.Effects = effect;
        }
        catch (COMException) { /* Explorer can release the source during a drop. */ }
    }

    internal bool Edit(Action change)
    {
        try { change(); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(panel is { IsVisible: true } ? panel : this, ex.Message, "无法保存 GlassBoard 配置");
            return false;
        }
    }

    internal void Launch(DockItem item)
    {
        var start = new ProcessStartInfo { FileName = item.Target, UseShellExecute = true };
        if (item.ClassId is Guid id)
        {
            start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            start.ArgumentList.Add($"shell:::{id:B}");
        }
        else start.WorkingDirectory = Path.GetDirectoryName(item.Target)!;
        try { using var process = Process.Start(start); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        catch (Win32Exception ex) { MessageBox.Show(this, $"无法打开“{item.Name}”。\n{ex.Message}", Title); }
    }

    private void PressDock(object sender, MouseButtonEventArgs e)
    {
        panelPressed = false;
        pressedItem = null;
        for (var node = e.OriginalSource as DependencyObject; node is Visual; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ScrollBar) return;
            if (node == PanelHandle) panelPressed = true;
            if (node is FrameworkElement { DataContext: DockItem item }) pressedItem = item;
        }
        clickCount = e.ClickCount;
        press = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    private void MoveDock(object sender, MouseEventArgs e)
    {
        if (press is not Point origin || e.LeftButton != MouseButtonState.Pressed) return;
        Vector delta = e.GetPosition(this) - origin;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        press = null;
        ReleaseMouseCapture();
        if (!library.State.PositionLocked) DragMove();
        e.Handled = true;
    }

    private void ReleaseDock(object sender, MouseButtonEventArgs e)
    {
        if (press is null) return;
        press = null;
        ReleaseMouseCapture();
        if (panelPressed && clickCount == 1)
        {
            if (panel is { IsVisible: true } && !panel.IsDismissing) panel.Dismiss();
            else ShowPanel();
        }
        else if (!panelPressed)
        {
            panel?.Dismiss();
            if (clickCount == 2 && pressedItem != null) Launch(pressedItem);
        }
        e.Handled = true;
    }

    private void ShowPanel()
    {
        if (panel == null)
        {
            panel = new Launchpad(this, library);
            panel.Closed += (_, _) => panel = null;
        }
        panel.CancelDismiss();
        AnchorPanel();
        panel.Show();
        panel.Activate();
        panel.FocusSearch();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public int Flags; }
    [DllImport("user32")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);

    private void AnchorPanel()
    {
        if (panel == null) return;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetWindowRect(hwnd, out var dockRect) || !GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor)) return;
        var work = monitor.Work;
        var dpi = VisualTreeHelper.GetDpi(this);
        int gap = (int)Math.Round(10 * dpi.DpiScaleY);
        int width = Math.Min(work.Right - work.Left, (int)Math.Round(780 * dpi.DpiScaleX));
        int height = Math.Min(work.Bottom - work.Top, (int)Math.Round(458 * dpi.DpiScaleY));
        int above = dockRect.Top - gap - work.Top, below = work.Bottom - dockRect.Bottom - gap;
        bool placeAbove = above >= height || (below < height && above >= below);
        height = Math.Min(height, Math.Max(1, placeAbove ? above : below));
        int x = Math.Clamp((dockRect.Left + dockRect.Right - width) / 2, work.Left, work.Right - width);
        int y = placeAbove ? dockRect.Top - gap - height : dockRect.Bottom + gap;
        var panelHwnd = new System.Windows.Interop.WindowInteropHelper(panel).EnsureHandle();
        SetWindowPos(panelHwnd, 0, x, Math.Clamp(y, work.Top, work.Bottom - height), width, height, 0x14);
    }

    private void CancelGesture(object sender, MouseEventArgs e) => press = null;

    private void ItemKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is FrameworkElement { DataContext: DockItem item })
        {
            Launch(item);
            e.Handled = true;
        }
    }

}
