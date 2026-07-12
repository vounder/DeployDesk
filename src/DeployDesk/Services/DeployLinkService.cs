using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DeployDesk.Models;

namespace DeployDesk.Services;

public sealed partial class DeployLinkService(GitService gitService)
{
    private const long MaximumDeployLinkLength = 1_048_576;
    private const long MaximumRunnerLength = 16_777_216;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public string LanguageCode { get; set; } = "en";

    public async Task<DeployLink> LoadAsync(string sourcePath)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath) || !fullSourcePath.EndsWith(".deploylink", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(Message("Select an existing .deploylink file.", "W\u00E4hle eine vorhandene .deploylink-Datei aus."));
        }

        if (new FileInfo(fullSourcePath).Length > MaximumDeployLinkLength)
        {
            throw new InvalidDataException(Message("The project link is larger than 1 MiB.", "Die Projektverkn\u00FCpfung ist gr\u00F6\u00DFer als 1 MiB."));
        }

        DeployLink config;
        try
        {
            await using var stream = File.OpenRead(fullSourcePath);
            using (var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions
                   {
                       AllowTrailingCommas = true,
                       CommentHandling = JsonCommentHandling.Skip,
                       MaxDepth = 32
                   }))
            {
                EnsureNoDuplicateProperties(document.RootElement);
            }
            stream.Position = 0;

            config = await JsonSerializer.DeserializeAsync<DeployLink>(stream, JsonOptions)
                     ?? throw new InvalidDataException(Message("The project link is empty or invalid.", "Die Projektverkn\u00FCpfung ist leer oder ung\u00FCltig."));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                Message("The project link contains invalid or unsupported JSON.", "Die Projektverkn\u00FCpfung enth\u00E4lt ung\u00FCltiges oder nicht unterst\u00FCtztes JSON."),
                exception);
        }

        Validate(config);

        var repositoryRoot = Path.GetFullPath(await gitService.FindRootAsync(Path.GetDirectoryName(fullSourcePath)!));
        var runnerPath = Path.GetFullPath(Path.Combine(repositoryRoot, config.Runner.File));
        var rootWithSeparator = repositoryRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!runnerPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) || !File.Exists(runnerPath))
        {
            throw new InvalidDataException(Message("The configured deployment runner is missing or outside the repository.", "Der konfigurierte Deployment-Runner fehlt oder liegt au\u00DFerhalb des Repositories."));
        }

        if (new FileInfo(runnerPath).Length > MaximumRunnerLength)
        {
            throw new InvalidDataException(Message("The deployment runner is larger than 16 MiB.", "Der Deployment-Runner ist gr\u00F6\u00DFer als 16 MiB."));
        }

        var resolvedRepositoryRoot = ResolveExistingPath(repositoryRoot);
        var resolvedRunnerPath = ResolveExistingPath(runnerPath);
        var relativeResolvedRunner = Path.GetRelativePath(resolvedRepositoryRoot, resolvedRunnerPath);
        if (Path.IsPathRooted(relativeResolvedRunner) || relativeResolvedRunner == ".." ||
            relativeResolvedRunner.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativeResolvedRunner.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException(Message("The deployment runner resolves outside the repository through a link or junction.", "Der Deployment-Runner verweist \u00FCber einen Link oder eine Junction aus dem Repository heraus."));
        }

        config.SourcePath = fullSourcePath;
        config.RepositoryRoot = repositoryRoot;
        config.RunnerPath = resolvedRunnerPath;
        return config;
    }

    private void Validate(DeployLink config)
    {
        if (config.Project is null || config.Repository is null || config.Server is null ||
            config.Server.HealthCheck is null || config.Runner is null || config.Options is null ||
            config.Links is null || config.Runner.Arguments is null)
        {
            throw new InvalidDataException(Message("The project link contains incomplete or null values.", "Die Projektverkn\u00FCpfung enth\u00E4lt unvollst\u00E4ndige oder leere Werte."));
        }

        if (config.SchemaVersion != 2)
        {
            throw new InvalidDataException($"{Message("Unsupported schemaVersion", "Nicht unterst\u00FCtzte schemaVersion")}: {config.SchemaVersion}");
        }

        if (!ProjectIdRegex().IsMatch(config.Project.Id ?? string.Empty))
        {
            throw new InvalidDataException(Message("project.id may contain only lowercase letters, numbers, hyphens, and underscores.", "project.id darf nur Kleinbuchstaben, Zahlen, Bindestriche und Unterstriche enthalten."));
        }

        if (!IsSafeDisplayText(config.Project.Name, 120) ||
            !IsSafeDisplayText(config.Project.Description, 2_000, allowEmpty: true) ||
            !ProjectColorRegex().IsMatch(config.Project.AccentColor ?? string.Empty) ||
            string.IsNullOrWhiteSpace(config.Runner.File))
        {
            throw new InvalidDataException(Message("Project metadata or the runner file is invalid.", "Projektmetadaten oder Runner-Datei sind ung\u00FCltig."));
        }

        if (!IsSafeDisplayText(config.Server.Name, 40) ||
            !IsSafeDisplayText(config.Server.Host, 253) ||
            !ServerHostRegex().IsMatch(config.Server.Host ?? string.Empty) ||
            !ServerUserRegex().IsMatch(config.Server.User ?? string.Empty))
        {
            throw new InvalidDataException(Message("server.host or server.user is invalid.", "server.host oder server.user ist ung\u00FCltig."));
        }

        if (config.Server.SshPort is < 1 or > 65535 || config.Server.HealthCheck.Port is < 1 or > 65535)
        {
            throw new InvalidDataException(Message("SSH and health-check ports must be between 1 and 65535.", "SSH- und Health-Check-Ports m\u00FCssen zwischen 1 und 65535 liegen."));
        }

        if (config.Server.RemotePath is null || config.Server.RemotePath.Length > 1_024 ||
            !RemotePathRegex().IsMatch(config.Server.RemotePath) ||
            config.Server.RemotePath.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException(Message("server.remotePath must be a safe, absolute Linux path.", "server.remotePath muss ein sicherer absoluter Linux-Pfad sein."));
        }

        var healthCheck = config.Server.HealthCheck;
        if (healthCheck.Path is null || healthCheck.Path.Length > 2_048 ||
            !HealthPathRegex().IsMatch(healthCheck.Path) ||
            healthCheck.ExpectedStatus is < 100 or > 599 ||
            healthCheck.Attempts is < 1 or > 120 ||
            healthCheck.IntervalSeconds is < 1 or > 60)
        {
            throw new InvalidDataException(Message("server.healthCheck contains invalid values.", "server.healthCheck enth\u00E4lt ung\u00FCltige Werte."));
        }

        if (config.Repository.Remote is not { } remote || config.Repository.Branch is not { } branch ||
            !GitRemoteRegex().IsMatch(remote) ||
            !GitBranchRegex().IsMatch(branch) ||
            branch.Contains("..", StringComparison.Ordinal) ||
            branch.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(Message("repository.remote or repository.branch is invalid.", "repository.remote oder repository.branch ist ung\u00FCltig."));
        }

        if (!string.Equals(config.Runner.Type, "powershell", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(Message("Schema 2 supports PowerShell runners only.", "Schema 2 unterst\u00FCtzt ausschlie\u00DFlich PowerShell-Runner."));
        }

        var runnerSegments = config.Runner.File.Replace('\\', '/').Split('/');
        if (Path.IsPathRooted(config.Runner.File) ||
            config.Runner.File.Length > 1_024 ||
            !config.Runner.File.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ||
            config.Runner.File.Any(character => char.IsControl(character) || character == ':') ||
            runnerSegments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new InvalidDataException(Message("runner.file must be a safe, relative PowerShell script path.", "runner.file muss ein sicherer relativer PowerShell-Skriptpfad sein."));
        }

        if (!string.Equals(config.Runner.Protocol, "deploydesk-jsonl-v1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(Message("Unsupported runner protocol.", "Nicht unterst\u00FCtztes Runner-Protokoll."));
        }

        var reservedArguments = new[] { "-DeployLinkPath", "-NonInteractive", "-SkipLocalGit", "-ValidateOnly", "-OutputFormat" };
        if (config.Runner.Arguments.Count > 64 ||
            config.Runner.Arguments.Any(argument => string.IsNullOrWhiteSpace(argument) ||
                argument.Length > 1_024 || argument.Any(char.IsControl) ||
                reservedArguments.Any(reserved =>
                    argument.Equals(reserved, StringComparison.OrdinalIgnoreCase) ||
                    argument.StartsWith(reserved + ":", StringComparison.OrdinalIgnoreCase) ||
                    argument.StartsWith(reserved + "=", StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidDataException(Message("runner.arguments contains invalid or reserved parameters.", "runner.arguments enth\u00E4lt ung\u00FCltige oder reservierte Parameter."));
        }

        if (config.Options.Count > 64 ||
            config.Options.Any(option => option is null ||
                !string.Equals(option.Type, "boolean", StringComparison.OrdinalIgnoreCase) ||
                !OptionIdRegex().IsMatch(option.Id ?? string.Empty) ||
                !IsSafeDisplayText(option.Label, 120) ||
                !IsSafeDisplayText(option.Description, 1_000, allowEmpty: true) ||
                !OptionArgumentRegex().IsMatch(option.Argument ?? string.Empty) ||
                reservedArguments.Contains(option.Argument, StringComparer.OrdinalIgnoreCase)) ||
            config.Options.Select(option => option?.Id ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.Options.Count)
        {
            throw new InvalidDataException(Message("Options must use unique, safe boolean parameters.", "Optionen m\u00FCssen eindeutige, sichere boolesche Parameter verwenden."));
        }

        if (config.Links.Count > 32 || config.Links.Any(link =>
                link is null || !IsSafeDisplayText(link.Label, 120) || !IsSafeWebUri(link.Url)))
        {
            throw new InvalidDataException(Message("links may only contain safe HTTP or HTTPS addresses.", "links darf ausschlie\u00DFlich sichere HTTP- oder HTTPS-Adressen enthalten."));
        }
    }

    private static bool IsSafeDisplayText(string? value, int maximumLength, bool allowEmpty = false) =>
        value is not null && value.Length <= maximumLength && !value.Any(char.IsControl) &&
        (allowEmpty || !string.IsNullOrWhiteSpace(value));

    private static bool IsSafeWebUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
         uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) &&
        !string.IsNullOrWhiteSpace(uri.Host) &&
        string.IsNullOrEmpty(uri.UserInfo);

    private void EnsureNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException($"{Message("Duplicate JSON property", "Doppelte JSON-Eigenschaft")}: {property.Name}");
                }
                EnsureNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                EnsureNoDuplicateProperties(item);
            }
        }
    }

    private string ResolveExistingPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
                   ?? throw new InvalidDataException(Message("The configured path has no filesystem root.", "Der konfigurierte Pfad besitzt kein Dateisystem-Stammverzeichnis."));
        FileSystemInfo current = new DirectoryInfo(root);
        var segments = fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < segments.Length; index++)
        {
            var candidatePath = Path.Combine(current.FullName, segments[index]);
            current = index == segments.Length - 1 && File.Exists(candidatePath)
                ? new FileInfo(candidatePath)
                : new DirectoryInfo(candidatePath);
            current = current.ResolveLinkTarget(returnFinalTarget: true) ?? current;
        }

        return Path.GetFullPath(current.FullName);
    }

    private string Message(string english, string german) =>
        string.Equals(LanguageCode, "de", StringComparison.OrdinalIgnoreCase) ? german : english;

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectIdRegex();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectColorRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex OptionIdRegex();

    [GeneratedRegex("^-[A-Za-z][A-Za-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex OptionArgumentRegex();

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
