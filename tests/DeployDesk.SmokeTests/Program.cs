using DeployDesk.Services;
using System.IO;
using System.Reflection;
using System.Windows.Threading;

if (args is ["--ui-animation"])
{
    return RunUiAnimationSmokeTest();
}

if (args.Length != 1)
{
    Console.Error.WriteLine("Aufruf: DeployDesk.SmokeTests <projekt.deploylink>");
    return 2;
}

var processService = new ProcessService();
var gitService = new GitService(processService);
var deployLinkService = new DeployLinkService(gitService);

try
{
    var config = await deployLinkService.LoadAsync(args[0]);
    if (config.SchemaVersion != 2 || string.IsNullOrWhiteSpace(config.Server.Host))
    {
        throw new InvalidDataException("Schema-v2-Serverkonfiguration wurde nicht geladen.");
    }
    var branch = await gitService.GetBranchAsync(config.RepositoryRoot);
    var changes = await gitService.GetChangesAsync(config.RepositoryRoot);
    var commits = await gitService.GetCommitsAsync(config.RepositoryRoot);

    Console.WriteLine($"OK: {config.Project.Name}");
    Console.WriteLine($"Repo: {config.RepositoryRoot}");
    Console.WriteLine($"Branch: {branch}");
    Console.WriteLine($"Ziel: {config.Server.User}@{config.Server.Host}:{config.Server.SshPort}{config.Server.RemotePath}");
    Console.WriteLine($"Änderungen: {changes.Count}");
    Console.WriteLine($"Commits: {commits.Count}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static int RunUiAnimationSmokeTest()
{
    Exception? capturedException = null;
    var thread = new Thread(() =>
    {
        try
        {
            var app = new DeployDesk.App { SuppressWindowStartup = true };
            app.InitializeComponent();
            var window = new DeployDesk.MainWindow();
            window.Show();

            var setBusy = typeof(DeployDesk.MainWindow).GetMethod("SetBusy", BindingFlags.Instance | BindingFlags.NonPublic)
                          ?? throw new MissingMethodException("SetBusy fehlt.");
            setBusy.Invoke(window, [true]);

            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                setBusy.Invoke(window, [false]);
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
            window.Close();
        }
        catch (TargetInvocationException exception)
        {
            capturedException = exception.InnerException ?? exception;
        }
        catch (Exception exception)
        {
            capturedException = exception;
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!thread.Join(TimeSpan.FromSeconds(10)))
    {
        Console.Error.WriteLine("UI-Animationstest hat das Zeitlimit überschritten.");
        return 1;
    }

    if (capturedException is not null)
    {
        Console.Error.WriteLine(capturedException);
        return 1;
    }

    Console.WriteLine("UI-Animationstest erfolgreich.");
    return 0;
}
