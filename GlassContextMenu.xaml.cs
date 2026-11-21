using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace GlassBoard;

public partial class GlassContextMenu : ContextMenu
{
    private readonly GlassWindow owner;
    private RoundedBackdrop? backdrop;
    private HwndSource? popup;

    internal GlassContextMenu(GlassWindow owner)
    {
        this.owner = owner;
        InitializeComponent();
        Opened += (_, _) =>
        {
            popup = PresentationSource.FromVisual(this) as HwndSource;
            if (popup == null) return;
            int percent = owner.GlassTransparency;
            if (percent < 100 && owner.HasGlass)
            {
                try { backdrop = new RoundedBackdrop(); }
                catch (COMException) { }
            }
            Background = GlassWindow.SurfaceBrush(percent, backdrop != null);
            backdrop?.SetTransparency(percent);
            Sync();
            popup.AddHook(PopupMessage);
        };
        Closed += (_, _) =>
        {
            if (popup?.IsDisposed == false) popup.RemoveHook(PopupMessage);
            popup = null;
            backdrop?.Dispose();
            backdrop = null;
        };
    }

    private nint PopupMessage(nint hwnd, int msg, nint w, nint l, ref bool handled)
    {
        if (msg == 0x0047) Sync();
        return 0;
    }

    private void Sync()
    {
        if (backdrop == null || popup == null) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        backdrop.Resize((int)Math.Ceiling(ActualWidth * dpi.DpiScaleX),
                        (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY), (float)(11 * dpi.DpiScaleX));
        backdrop.Follow(popup.Handle);
    }
}
