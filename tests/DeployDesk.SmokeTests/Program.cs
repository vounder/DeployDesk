using DeployDesk.Services;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;

if (args is ["--ui-animation"])
{
    return RunUiAnimationSmokeTest();
}

if (args is ["--security-validation"])
{
    return await RunSecurityValidationTestsAsync();
}

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: DeployDesk.SmokeTests <project.deploylink>");
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
        throw new InvalidDataException("The schema-v2 server configuration was not loaded.");
    }
    var branch = await gitService.GetBranchAsync(config.RepositoryRoot);
    var changes = await gitService.GetChangesAsync(config.RepositoryRoot);
    var commits = await gitService.GetCommitsAsync(config.RepositoryRoot);

    Console.WriteLine($"OK: {config.Project.Name}");
    Console.WriteLine($"Repository: {config.RepositoryRoot}");
    Console.WriteLine($"Branch: {branch}");
    Console.WriteLine($"Target: {config.Server.User}@{config.Server.Host}:{config.Server.SshPort}{config.Server.RemotePath}");
    Console.WriteLine($"Changes: {changes.Count}");
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

            var expectedVersion = typeof(DeployDesk.MainWindow).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion.Split('+', 2)[0];
            if (window.ApplicationVersion != $"v{expectedVersion}")
            {
                throw new InvalidOperationException("The visible application version does not match the assembly version.");
            }

            var toggleSettings = typeof(DeployDesk.MainWindow).GetMethod(
                "ToggleSettings",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException("ToggleSettings is missing.");
            toggleSettings.Invoke(window, null);
            if (window.SettingsVisibility != System.Windows.Visibility.Visible)
            {
                throw new InvalidOperationException("The settings drawer did not open.");
            }

            var setBusy = typeof(DeployDesk.MainWindow).GetMethod("SetBusy", BindingFlags.Instance | BindingFlags.NonPublic)
                          ?? throw new MissingMethodException("SetBusy is missing.");
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
        Console.Error.WriteLine("The UI animation test timed out.");
        return 1;
    }

    if (capturedException is not null)
    {
        Console.Error.WriteLine(capturedException);
        return 1;
    }

    Console.WriteLine("UI animation test passed.");
    return 0;
}

static async Task<int> RunSecurityValidationTestsAsync()
{
    var localization = new LocalizationService();
    localization.SetLanguage("de");
    if (localization["Settings"] != "Einstellungen")
    {
        Console.Error.WriteLine("German localization did not load.");
        return 1;
    }
    localization.SetLanguage("en");
    if (localization["Settings"] != "Settings")
    {
        Console.Error.WriteLine("English localization did not load.");
        return 1;
    }

    var secretPathCheck = typeof(DeployDesk.MainWindow).GetMethod(
        "IsPotentialSecretChange",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("IsPotentialSecretChange is missing.");
    if (secretPathCheck.Invoke(null, ["?? .env"]) is not true ||
        secretPathCheck.Invoke(null, ["?? deploy/client.key"]) is not true ||
        secretPathCheck.Invoke(null, ["?? .env.example"]) is not false)
    {
        Console.Error.WriteLine("Potential-secret path detection failed.");
        return 1;
    }

    var root = Path.Combine(Path.GetTempPath(), "DeployDesk.SecurityTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var processService = new ProcessService();
        var git = ExecutableLocator.FindGitExecutable();
        var initResult = await processService.RunAsync(git, ["init", "-b", "main"], root);
        if (initResult.ExitCode != 0)
        {
            throw new InvalidOperationException(initResult.StandardError);
        }

        var runnerPath = Path.Combine(root, "deploy.ps1");
        var linkPath = Path.Combine(root, "security-test.deploylink");
        await File.WriteAllTextAsync(runnerPath, "param()\nexit 0\n");

        var fixtureGitService = new GitService(processService);
        var service = new DeployLinkService(fixtureGitService);

        await File.WriteAllTextAsync(linkPath, CreateLink("deploy.ps1", "https://deploy.example.com"));
        var valid = await service.LoadAsync(linkPath);
        if (valid.Schema != "https://deploydesk.local/schema/deploylink-v2.json")
        {
            throw new InvalidDataException("The documented $schema property was not accepted.");
        }
        if (!Path.GetFullPath(valid.RunnerPath).Equals(Path.GetFullPath(runnerPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The valid runner did not resolve as expected.");
        }
        if ((await fixtureGitService.GetChangesAsync(root)).Count < 2)
        {
            throw new InvalidDataException("Hardened Git status did not return the disposable fixture files.");
        }

        await ExpectRejectedAsync(async () =>
        {
            await File.WriteAllTextAsync(linkPath, CreateLink("deploy.ps1", "file:///C:/Windows/System32/calc.exe"));
            await service.LoadAsync(linkPath);
        }, "non-web URI schemes");

        await ExpectRejectedAsync(async () =>
        {
            await File.WriteAllTextAsync(linkPath, CreateLink("deploy.ps1", "https://user:pass@deploy.example.com"));
            await service.LoadAsync(linkPath);
        }, "URI user information");

        await ExpectRejectedAsync(async () =>
        {
            var json = CreateLink("deploy.ps1", "https://deploy.example.com");
            var closingBrace = json.LastIndexOf('}');
            await File.WriteAllTextAsync(linkPath, json[..closingBrace] + ",\n  \"unexpected\": true\n}\n");
            await service.LoadAsync(linkPath);
        }, "unknown JSON properties");

        await ExpectRejectedAsync(async () =>
        {
            var json = CreateLink("deploy.ps1", "https://deploy.example.com");
            await File.WriteAllTextAsync(linkPath, json.Replace(
                "\"schemaVersion\": 2,",
                "\"schemaVersion\": 2,\n  \"schemaVersion\": 2,"));
            await service.LoadAsync(linkPath);
        }, "duplicate JSON properties");

        await ExpectRejectedAsync(async () =>
        {
            await File.WriteAllTextAsync(linkPath, CreateLink("../outside.ps1", "https://deploy.example.com"));
            await service.LoadAsync(linkPath);
        }, "runner traversal");

        Console.WriteLine("Security validation tests passed.");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception);
        return 1;
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ExpectRejectedAsync(Func<Task> action, string scenario)
{
    try
    {
        await action();
    }
    catch (Exception exception) when (exception is InvalidDataException or JsonException)
    {
        return;
    }

    throw new InvalidDataException($"Validation accepted {scenario}.");
}

static string CreateLink(string runnerFile, string websiteUrl) => $$"""
{
  "$schema": "https://deploydesk.local/schema/deploylink-v2.json",
  "schemaVersion": 2,
  "project": {
    "id": "security-test",
    "name": "Security Test",
    "description": "Temporary validation fixture"
  },
  "repository": {
    "remote": "origin",
    "branch": "main"
  },
  "server": {
    "name": "Test",
    "host": "deploy.example.com",
    "user": "deploy",
    "sshPort": 22,
    "remotePath": "/srv/security-test",
    "healthCheck": {
      "port": 8080,
      "path": "/health"
    }
  },
  "runner": {
    "type": "powershell",
    "file": "{{runnerFile}}",
    "protocol": "deploydesk-jsonl-v1",
    "arguments": []
  },
  "links": [
    {
      "label": "Website",
      "url": "{{websiteUrl}}"
    }
  ]
}
""";
