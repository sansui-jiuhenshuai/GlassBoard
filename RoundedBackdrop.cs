using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.System;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace GlassBoard;

internal sealed class RoundedBackdrop : IDisposable
{
    private static Compositor? compositor;
    private static DispatcherQueueController? controller;
    private readonly DesktopWindowTarget target;
    private readonly CompositionRoundedRectangleGeometry geometry;
    private readonly CompositionGeometricClip clip;
    private readonly CompositionBackdropBrush brush;
    private readonly SpriteVisual visual;
    private readonly nint host;

    internal RoundedBackdrop()
    {
        if (DispatcherQueue.GetForCurrentThread() == null)
        {
            var options = new QueueOptions { Size = 12, ThreadType = 2, ApartmentType = 2 };
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out nint queue));
            try { controller = DispatcherQueueController.FromAbi(queue); }
            finally { Marshal.Release(queue); }
        }
        compositor ??= new Compositor();
        var interop = compositor.As<IDesktopInterop>();
        // DirectComposition paints above a window's own WPF surface. Give it a
        // non-activating, non-redirected popup immediately below the interactive HWND.
        const uint ToolWindow = 0x80, NoActivate = 0x08000000, NoRedirection = 0x00200000, Transparent = 0x20;
        host = CreateWindowExW(ToolWindow | NoActivate | NoRedirection | Transparent, "STATIC", "",
                               0x88000000 /* WS_POPUP | WS_DISABLED */, 0, 0, 0, 0, 0, 0, 0, 0);
        if (host == 0) throw new COMException("无法创建玻璃背景窗口。");
        try
        {
            int enabled = 1;
            Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(host, 17, ref enabled, sizeof(int)));
            Marshal.ThrowExceptionForHR(interop.CreateDesktopWindowTarget(host, false, out nint raw));
            try { target = DesktopWindowTarget.FromAbi(raw); }
            finally { Marshal.Release(raw); }

            geometry = compositor.CreateRoundedRectangleGeometry();
            clip = compositor.CreateGeometricClip(geometry);
            brush = compositor.CreateHostBackdropBrush();
            visual = compositor.CreateSpriteVisual();
            visual.Brush = brush;
            visual.Clip = clip;
            target.Root = visual;
        }
        catch { DestroyWindow(host); throw; }
    }

    internal void Follow(nint foreground)
    {
        if (!IsWindowVisible(foreground) || IsIconic(foreground))
        {
            ShowWindow(host, 0);
            return;
        }
        if (GetWindowRect(foreground, out var rect))
            SetWindowPos(host, foreground, rect.Left, rect.Top, rect.Right - rect.Left,
                         rect.Bottom - rect.Top, 0x0250); // NOACTIVATE | SHOWWINDOW | NOOWNERZORDER
    }

    internal void Resize(int width, int height, float radius)
    {
        geometry.Size = visual.Size = new Vector2(width, height);
        geometry.CornerRadius = new Vector2(radius);
    }

    internal void SetTransparency(int percent) => visual.Opacity = 1f - percent / 100f;

    public void Dispose()
    {
        target.Root = null;
        target.Dispose();
        visual.Dispose();
        brush.Dispose();
        clip.Dispose();
        geometry.Dispose();
        DestroyWindow(host);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint CreateWindowExW(uint exStyle, string cls, string title, uint style, int x, int y,
                                               int width, int height, nint parent, nint menu, nint module, nint param);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopInterop
    {
        [PreserveSig] int CreateDesktopWindowTarget(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost, out nint target);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct QueueOptions { internal int Size, ThreadType, ApartmentType; }
    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(QueueOptions options, out nint controller);
}
