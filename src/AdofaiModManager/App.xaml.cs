using System.IO;
using System.Windows;
using AdofaiModManager.Services;

namespace AdofaiModManager;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly string CrashLogPath =
        Path.Combine(AppContext.BaseDirectory, "crash.log");

    public App()
    {
        DispatcherUnhandledException += (_, args) => HandleException(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            HandleException(args.ExceptionObject as Exception);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppServices.Initialize();

        var window = new MainWindow();
        MainWindow = window;

        // 应用主题（并在「跟随系统」时监听系统主题变化）
        ThemeService.Apply(AppServices.Settings.Settings.Theme, window);

        window.Show();
    }

    private static void HandleException(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // 忽略
        }
    }
}
