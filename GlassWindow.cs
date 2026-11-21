using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace GlassBoard;

public class GlassWindow : Window
{
    private RoundedBackdrop? backdrop;
    private bool effectsEnabled, materialReady;
    private int transparency = 56;
    public double GlassRadius { get; set; } = 24;
    internal int GlassTransparency => transparency;
    internal bool HasGlass => backdrop != null;

    internal static Brush SurfaceBrush(int value, bool hasBlur)
    {
        if (value == 100) return Brushes.Transparent;
        if (SystemParameters.HighContrast) return SystemColors.WindowBrush;
        byte alpha = (byte)Math.Round(255 * (100 - value) / 100d);
        return hasBlur ? new LinearGradientBrush(Color.FromArgb(alpha, 51, 63, 75),
                                                  Color.FromArgb(alpha, 17, 24, 33), 75)
                       : new SolidColorBrush(Color.FromArgb(alpha, 34, 37, 43));
    }

    public GlassWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = Brushes.White;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) ReleaseBackdrop();
            else Dispatcher.InvokeAsync(() => SetGlassTransparency(transparency), System.Windows.Threading.DispatcherPriority.ContextIdle);
        };
    }

    internal void SetGlassTransparency(int value)
    {
        transparency = value;
        if (Content is not Border surface) return;
        if (materialReady && effectsEnabled && IsVisible && value is > 0 and < 100)
        {
            if (backdrop == null)
            {
                try { backdrop = new RoundedBackdrop(); }
                catch (COMException) { }
                RoundWindow();
                backdrop?.Follow(new WindowInteropHelper(this).Handle);
            }
        }
        else ReleaseBackdrop();
        backdrop?.SetTransparency(value);
        surface.Background = SurfaceBrush(value, backdrop != null);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd)!;
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            SetAttribute(hwnd, 20, 1); // DWMWA_USE_IMMERSIVE_DARK_MODE
            SetAttribute(hwnd, 33, 1); // Use our own rounded region.
            SetAttribute(hwnd, 34, unchecked((int)0xfffffffe)); // DWMWA_COLOR_NONE
        }
        const int extendedStyle = -20;
        SetWindowLongW(hwnd, extendedStyle, (GetWindowLongW(hwnd, extendedStyle) | 0x80) & ~0x40000);
        ConfigureBackdrop(hwnd);
        source.AddHook(OnWindowMessage);
    }

    private void ConfigureBackdrop(nint hwnd)
    {
        effectsEnabled = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && !SystemParameters.HighContrast &&
            Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                              "EnableTransparency", 1) is not 0;
        Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brushes.White;
        Dispatcher.InvokeAsync(() =>
        {
            if (HwndSource.FromHwnd(hwnd) == null) return;
            materialReady = true;
            SetGlassTransparency(transparency);
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        RoundWindow();
        backdrop?.Follow(hwnd);
    }

    protected void ReleaseBackdrop()
    {
        backdrop?.Dispose();
        backdrop = null;
    }

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0047) backdrop?.Follow(hwnd); // WM_WINDOWPOSCHANGED
        if (message is 0x001A or 0x031A)
            Dispatcher.InvokeAsync(() => { if (HwndSource.FromHwnd(hwnd) != null) ConfigureBackdrop(hwnd); });
        return 0;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        RoundWindow();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.InvokeAsync(RoundWindow);
    }

    private void RoundWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        float radius = (float)(GlassRadius * dpi.DpiScaleX);
        backdrop?.Resize(width, height, radius);
        int diameter = (int)Math.Round(radius * 2);
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
        if (region != 0 && SetWindowRgn(hwnd, region, true) == 0) DeleteObject(region);
    }

    protected override void OnClosed(EventArgs e)
    {
        backdrop?.Dispose();
        backdrop = null;
        base.OnClosed(e);
    }

    private static int SetAttribute(nint hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    [DllImport("user32.dll")] private static extern int GetWindowLongW(nint h, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLongW(nint h, int index, int value);

    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int x, int y, int right, int bottom, int w, int h);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
