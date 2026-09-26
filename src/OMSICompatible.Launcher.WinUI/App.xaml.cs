using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace OMSICompatible.Launcher.WinUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception exception)
        {
            ReportStartupFailure(
                exception);
            throw;
        }
    }

    protected override void OnLaunched(
        LaunchActivatedEventArgs args)
    {
        try
        {
            _window =
                new MainWindow();

            _window.Activate();
        }
        catch (Exception exception)
        {
            ReportStartupFailure(
                exception);
            throw;
        }
    }

    private static void ReportStartupFailure(
        Exception exception)
    {
        try
        {
            var directory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "OMSI-Compatible-Runtime");

            Directory.CreateDirectory(
                directory);

            var path =
                Path.Combine(
                    directory,
                    "launcher-winui.log");

            File.AppendAllText(
                path,
                $"[{DateTimeOffset.Now:O}] {exception}\r\n\r\n");
        }
        catch
        {
            // Startup reporting must never hide the original exception.
        }

        try
        {
            MessageBox(
                IntPtr.Zero,
                "O novo launcher WinUI não conseguiu iniciar. " +
                "O erro foi registrado em %LocalAppData%\\OMSI-Compatible-Runtime\\launcher-winui.log.\r\n\r\n" +
                exception.Message,
                "OMSI Compatible Runtime",
                0x00000010u);
        }
        catch
        {
            // Keep the original startup exception if user32 is unavailable.
        }
    }

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(
        IntPtr hWnd,
        string text,
        string caption,
        uint type);
}
