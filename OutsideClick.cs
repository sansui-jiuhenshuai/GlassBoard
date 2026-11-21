using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GlassBoard;

// Observe button-downs only while the launchpad is open. Never capture or swallow
// the click: the window the user clicked must still receive its normal input.
internal sealed class OutsideClick : IDisposable
{
    private readonly Hook callback;
    private nint hook;

    internal OutsideClick(Window panel, Window dock, Action dismiss)
    {
        callback = (code, message, data) =>
        {
            if (code >= 0 && (message == 0x0201 || message == 0x0204 || message == 0x0207))
            {
                var p = Marshal.PtrToStructure<ScreenPoint>(data);
                var root = GetAncestor(WindowFromPoint(p), 2);
                // WPF menus and the settings popup have their own presentation source.
                bool popup = HwndSource.FromHwnd(root)?.RootVisual is { } visual &&
                             visual is not Window && visual.GetType().Name == "PopupRoot";
                if (!popup && !Contains(panel, p) && !Contains(dock, p))
                    panel.Dispatcher.InvokeAsync(dismiss, System.Windows.Threading.DispatcherPriority.Background);
            }
            return CallNextHookEx(0, code, message, data);
        };
        hook = SetWindowsHookExW(14, callback, GetModuleHandleW(null), 0);
        if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static bool Contains(Window window, ScreenPoint p)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle != 0 && GetWindowRect(handle, out var r) &&
            p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
    }

    public void Dispose()
    {
        if (hook != 0) { UnhookWindowsHookEx(hook); hook = 0; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct ScreenPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate nint Hook(int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookExW(int type, Hook callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint msg, nint data);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(ScreenPoint p);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern nint GetModuleHandleW(string? name);
}
