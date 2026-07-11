using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeployDesk.Models;

namespace DeployDesk.Services;

public sealed partial class DeployLinkService(GitService gitService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public async Task<DeployLink> LoadAsync(string sourcePath)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath) || !fullSourcePath.EndsWith(".deploylink", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Bitte eine vorhandene .deploylink-Datei auswählen.");
        }

        await using var stream = File.OpenRead(fullSourcePath);
        var config = await JsonSerializer.DeserializeAsync<DeployLink>(stream, JsonOptions)
                     ?? throw new InvalidDataException("Die Deploy-Verknüpfung ist leer oder ungültig.");

        Validate(config);

        var repositoryRoot = Path.GetFullPath(await gitService.FindRootAsync(Path.GetDirectoryName(fullSourcePath)!));
        var runnerPath = Path.GetFullPath(Path.Combine(repositoryRoot, config.Runner.File));
        var rootWithSeparator = repositoryRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!runnerPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) || !File.Exists(runnerPath))
        {
            throw new InvalidDataException("Der konfigurierte Deploy-Runner fehlt oder liegt außerhalb des Repositories.");
        }

        config.SourcePath = fullSourcePath;
        config.RepositoryRoot = repositoryRoot;
        config.RunnerPath = runnerPath;
        return config;
    }

    private static void Validate(DeployLink config)
    {
        if (config.SchemaVersion != 2)
        {
            throw new InvalidDataException($"Nicht unterstützte schemaVersion: {config.SchemaVersion}");
        }

        if (!ProjectIdRegex().IsMatch(config.Project.Id))
        {
            throw new InvalidDataException("project.id darf nur Kleinbuchstaben, Zahlen, Bindestriche und Unterstriche enthalten.");
        }

        if (string.IsNullOrWhiteSpace(config.Project.Name) || string.IsNullOrWhiteSpace(config.Runner.File))
        {
            throw new InvalidDataException("Projektname und Runner-Datei sind erforderlich.");
        }

        if (config.Server.Name.Length is < 1 or > 40 ||
            config.Server.Host.Length is < 1 or > 253 ||
            !ServerHostRegex().IsMatch(config.Server.Host) ||
            !ServerUserRegex().IsMatch(config.Server.User))
        {
            throw new InvalidDataException("server.host oder server.user ist ungültig.");
        }

        if (config.Server.SshPort is < 1 or > 65535 || config.Server.HealthCheck.Port is < 1 or > 65535)
        {
            throw new InvalidDataException("SSH- und Health-Check-Port müssen zwischen 1 und 65535 liegen.");
        }

        if (config.Server.RemotePath.Length > 1024 ||
            !RemotePathRegex().IsMatch(config.Server.RemotePath) ||
            config.Server.RemotePath.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException("server.remotePath muss ein sicherer absoluter Linux-Pfad sein.");
        }

        var healthCheck = config.Server.HealthCheck;
        if (healthCheck.Path.Length > 2048 ||
            !HealthPathRegex().IsMatch(healthCheck.Path) ||
            healthCheck.ExpectedStatus is < 100 or > 599 ||
            healthCheck.Attempts is < 1 or > 120 ||
            healthCheck.IntervalSeconds is < 1 or > 60)
        {
            throw new InvalidDataException("server.healthCheck enthält ungültige Werte.");
        }

        if (!GitRemoteRegex().IsMatch(config.Repository.Remote) ||
            !GitBranchRegex().IsMatch(config.Repository.Branch) ||
            config.Repository.Branch.Contains("..", StringComparison.Ordinal) ||
            config.Repository.Branch.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("repository.remote oder repository.branch ist ungültig.");
        }

        if (!config.Runner.Type.Equals("powershell", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Schema 2 unterstützt ausschließlich PowerShell-Runner.");
        }

        if (!config.Runner.Protocol.Equals("deploydesk-jsonl-v1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Nicht unterstütztes Runner-Protokoll.");
        }

        var reservedArguments = new[] { "-DeployLinkPath", "-NonInteractive", "-SkipLocalGit", "-ValidateOnly", "-OutputFormat" };
        if (config.Runner.Arguments.Any(argument => reservedArguments.Any(reserved =>
                argument.Equals(reserved, StringComparison.OrdinalIgnoreCase) ||
                argument.StartsWith(reserved + ":", StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidDataException("runner.arguments darf keine von DeployDesk reservierten Parameter enthalten.");
        }

        if (config.Options.Any(option =>
                !option.Type.Equals("boolean", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(option.Id) ||
                string.IsNullOrWhiteSpace(option.Argument)))
        {
            throw new InvalidDataException("Schema 2 unterstützt ausschließlich boolesche Optionen mit Argument.");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectIdRegex();

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerHostRegex();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9._-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerUserRegex();

    [GeneratedRegex("^/[A-Za-z0-9._/-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RemotePathRegex();

    [GeneratedRegex("^/[A-Za-z0-9._~%/?=&-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex HealthPathRegex();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._/-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex GitRemoteRegex();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._/-]{0,254}$", RegexOptions.CultureInvariant)]
    private static partial Regex GitBranchRegex();
}
