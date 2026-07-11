using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace DeployDesk;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeployDesk",
        "startup.log");

    public App()
    {
        DispatcherUnhandledException += HandleDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            WriteException("AppDomain", eventArgs.ExceptionObject as Exception ?? new Exception(eventArgs.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            WriteException("TaskScheduler", eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            ResetLog();
            WriteMessage("DeployDesk startet.");
            var window = new MainWindow();
            MainWindow = window;
            window.Show();

            if (e.Args.FirstOrDefault() is { } path &&
                path.EndsWith(".deploylink", StringComparison.OrdinalIgnoreCase))
            {
                _ = window.ImportFromCommandLineAsync(path);
            }
        }
        catch (Exception exception)
        {
            WriteException("OnStartup", exception);
            ShowStartupError(exception);
            Shutdown(1);
        }
    }

    private void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        WriteException("Dispatcher", eventArgs.Exception);
        eventArgs.Handled = true;
        ShowStartupError(eventArgs.Exception);
        Shutdown(1);
    }

    private static void ShowStartupError(Exception exception)
    {
        MessageBox.Show(
            $"DeployDesk konnte nicht gestartet werden.\n\n{exception.Message}\n\nDetails wurden gespeichert unter:\n{LogPath}",
            "DeployDesk – Startfehler",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static void WriteMessage(string message)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}", Encoding.UTF8);
    }

    private static void ResetLog()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        File.WriteAllText(LogPath, string.Empty, Encoding.UTF8);
    }

    private static void WriteException(string source, Exception exception)
    {
        try
        {
            WriteMessage($"[{source}] {exception}");
        }
        catch
        {
            // Die eigentliche Ausnahme darf nicht von einem Logfehler verdeckt werden.
        }
    }
}
