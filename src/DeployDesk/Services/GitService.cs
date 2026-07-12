using System.IO;
using DeployDesk.Models;

namespace DeployDesk.Services;

public sealed class GitService(ProcessService processService)
{
    private readonly string _gitExecutable = ExecutableLocator.FindGitExecutable();
    private readonly string _emptyHooksDirectory = CreateEmptyHooksDirectory();

    public async Task<string> FindRootAsync(string directory)
    {
        var result = await RunAsync(directory, "rev-parse", "--show-toplevel");
        return result.StandardOutput.Trim();
    }

    public async Task<string> GetBranchAsync(string root)
    {
        var result = await RunAsync(root, "rev-parse", "--abbrev-ref", "HEAD");
        return result.StandardOutput.Trim();
    }

    public async Task<string> GetHeadAsync(string root)
    {
        var result = await RunAsync(root, "rev-parse", "HEAD");
        return result.StandardOutput.Trim();
    }

    public async Task<IReadOnlyList<string>> GetChangesAsync(string root)
    {
        var result = await RunAsync(root, "status", "--porcelain");
        return SplitLines(result.StandardOutput);
    }

    public async Task<IReadOnlyList<CommitItem>> GetCommitsAsync(string root)
    {
        var result = await RunAsync(root, "log", "-15", "--format=%h%x09%s%x09%cr");
        return SplitLines(result.StandardOutput)
            .Select(line => line.Split('\t'))
            .Where(parts => parts.Length >= 3)
            .Select(parts => new CommitItem(parts[0], parts[1], parts[2]))
            .ToList();
    }

    public async Task<int> GetUndeployedCountAsync(
        string root,
        string? lastDeployedCommit,
        string remote,
        string branch)
    {
        var reference = !string.IsNullOrWhiteSpace(lastDeployedCommit)
            ? lastDeployedCommit
            : $"{remote}/{branch}";

        var result = await RunAllowFailureAsync(root, "rev-list", "--count", $"{reference}..HEAD");
        return result.ExitCode == 0 && int.TryParse(result.StandardOutput.Trim(), out var count) ? count : 0;
    }

    public async Task CommitAllAsync(string root, string message)
    {
        await RunAsync(root, "add", "-A");
        await RunAsync(root, "commit", "-m", message);
    }

    private async Task<ProcessResult> RunAsync(string root, params string[] arguments)
    {
        var result = await processService.RunAsync(
            _gitExecutable,
            ["-c", "core.fsmonitor=false", "-c", $"core.hooksPath={_emptyHooksDirectory}", "-C", root, .. arguments],
            root);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError)
                ? processService.Message("Git command failed.", "Git-Befehl fehlgeschlagen.")
                : result.StandardError.Trim());
        }

        return result;
    }

    private Task<ProcessResult> RunAllowFailureAsync(string root, params string[] arguments) =>
        processService.RunAsync(
            _gitExecutable,
            ["-c", "core.fsmonitor=false", "-c", $"core.hooksPath={_emptyHooksDirectory}", "-C", root, .. arguments],
            root);

    private static string CreateEmptyHooksDirectory()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeployDesk",
            "empty-git-hooks");
        Directory.CreateDirectory(path);
        return path;
    }

    private static IReadOnlyList<string> SplitLines(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
