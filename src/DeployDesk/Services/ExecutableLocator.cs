using System.IO;

namespace DeployDesk.Services;

public static class ExecutableLocator
{
    public static string FindGitExecutable()
    {
        var candidates = new List<string?>
        {
            Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"),
            Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "git.exe"),
            Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe"),
            Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "cmd", "git.exe")
        };

        candidates.AddRange(GetPathCandidates("git.exe"));
        return FindExistingExecutable(candidates, "Git for Windows was not found in a trusted absolute location.");
    }

    public static string FindWindowsPowerShellExecutable()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return FindExistingExecutable(
            [Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")],
            "Windows PowerShell was not found in the Windows system directory.");
    }

    private static IEnumerable<string> GetPathCandidates(string executableName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var expanded = Environment.ExpandEnvironmentVariables(entry.Trim('"'));
            if (!Path.IsPathRooted(expanded))
            {
                continue;
            }

            yield return Path.Combine(expanded, executableName);
        }
    }

    private static string FindExistingExecutable(IEnumerable<string?> candidates, string errorMessage)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(errorMessage);
    }

    private static string? Combine(string root, params string[] segments) =>
        string.IsNullOrWhiteSpace(root) ? null : Path.Combine([root, .. segments]);
}
