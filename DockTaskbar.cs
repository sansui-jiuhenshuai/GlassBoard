using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace GlassBoard;

internal sealed class DockTaskbar
{
    private const int Callback = 0x8000 + 72;
    private readonly uint taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    private readonly Action openPanel;
    private NotifyIconData data;

    internal DockTaskbar(DockWindow dock, Action openPanel)
    {
        this.openPanel = openPanel;
        var hwnd = new WindowInteropHelper(dock).Handle;
        // Borrow the small icon installed by WPF on the Dock window.
        var icon = SendMessageW(hwnd, 0x007f, 0, 0);
        if (icon == 0) icon = SendMessageW(hwnd, 0x007f, 1, 0);
        data = new NotifyIconData { Size = Marshal.SizeOf<NotifyIconData>(), Window = hwnd,
            Id = 1, Flags = 7, Message = Callback, Icon = icon, Tip = "GlassBoard" };
        HwndSource.FromHwnd(hwnd)!.AddHook(Message);
        Register();
        dock.Closed += (_, _) => Shell_NotifyIconW(2, ref data);
    }

    private void Register()
    {
        if (Shell_NotifyIconW(0, ref data))
        {
            data.Version = 4;
            Shell_NotifyIconW(4, ref data);
        }
    }

    private nint Message(nint hwnd, int message, nint w, nint l, ref bool handled)
    {
        if (message == taskbarCreated) Register();
        if (message == Callback && ((int)l & 0xffff) is 0x400 or 0x401)
        {
            openPanel();
            handled = true;
        }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public nint Window;
        public uint Id, Flags, Message;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string? Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string? InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SendMessageW(nint hwnd, int msg, nint w, nint l);
}
