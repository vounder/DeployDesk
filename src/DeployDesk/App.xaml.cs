using System.Windows;

namespace DeployDesk;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        if (e.Args.FirstOrDefault() is { } path &&
            path.EndsWith(".deploylink", StringComparison.OrdinalIgnoreCase))
        {
            _ = window.ImportFromCommandLineAsync(path);
        }
    }
}
