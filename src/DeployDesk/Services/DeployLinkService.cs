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
        if (config.SchemaVersion != 1)
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

        if (!config.Runner.Type.Equals("powershell", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Version 1 unterstützt ausschließlich PowerShell-Runner.");
        }

        if (!config.Runner.Protocol.Equals("deploydesk-jsonl-v1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Nicht unterstütztes Runner-Protokoll.");
        }

        if (config.Options.Any(option =>
                !option.Type.Equals("boolean", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(option.Id) ||
                string.IsNullOrWhiteSpace(option.Argument)))
        {
            throw new InvalidDataException("Version 1 unterstützt ausschließlich boolesche Optionen mit Argument.");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectIdRegex();
}
