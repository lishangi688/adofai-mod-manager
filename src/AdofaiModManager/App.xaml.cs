using System.IO;
using System.Windows;
using AdofaiModManager.Services;

namespace AdofaiModManager;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
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

        // 界面语言（必须在建窗口之前设好，「跟随系统」也是在这里判定）
        Loc.Instance.SetLanguage(AppServices.Settings.Settings.Language, notify: false);

        var theme = AppServices.Settings.Settings.Theme;

        // 先按主题铺好资源（向导也要用）
        ThemeService.Apply(theme);

        // 首次使用：先走一遍向导（每步都可跳过）
        if (!AppServices.Settings.Settings.HasCompletedSetup)
        {
            // 向导是此时唯一的窗口，先改成"显式退出"，
            // 否则它一关闭就会触发"最后一个窗口关闭 → 退出程序"
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var wizard = new Views.Dialogs.FirstRunWizard();
            wizard.ShowDialog();

            AppServices.Settings.Settings.HasCompletedSetup = true;
            AppServices.Settings.Save();
        }

        var window = new MainWindow();
        MainWindow = window;

        // 应用主题（并在「跟随系统」时监听系统主题变化）
        ThemeService.Apply(theme, window);

        window.Show();

        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    private static void HandleException(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            File.AppendAllText(
                AppPaths.CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // 忽略
        }
    }
}
