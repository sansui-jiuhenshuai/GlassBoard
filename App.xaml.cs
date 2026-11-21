using System;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace GlassBoard;

public partial class App : Application
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private Mutex? instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        instance = new Mutex(false, @"Local\GlassBoard.Desktop.Instance", out bool first);
        if (!first) { Shutdown(); return; }
        base.OnStartup(e);
        MainWindow = new DockWindow();
        MainWindow.Show();
    }

    internal static bool StartupEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("GlassBoard") is string;
        }
    }

    internal static void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (!enable) { key.DeleteValue("GlassBoard", false); return; }
        string executable = Environment.ProcessPath!;
        string command = $"\u0022{executable}\u0022";
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            command += $" \u0022{typeof(App).Assembly.Location}\u0022";
        key.SetValue("GlassBoard", command, RegistryValueKind.String);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
