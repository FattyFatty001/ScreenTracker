using Microsoft.Win32;
using Velopack;

namespace LucasScreentime;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnFirstRun(_ => RegisterStartup())
            .OnBeforeUninstallFastCallback(_ => RemoveStartup())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    internal static void RegisterStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            var exe = Environment.ProcessPath
                ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            key?.SetValue("LucasScreentime", $"\"{exe}\"");
        }
        catch { }
    }

    internal static void RemoveStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue("LucasScreentime", throwOnMissingValue: false);
        }
        catch { }
    }
}
