using System.Diagnostics;
using System.IO;
using System.Text;

namespace DeployDesk.Services;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class ProcessService
{
    private readonly object _gate = new();
    private Process? _activeProcess;

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        Action<string>? onOutput = null,
        Action<string>? onError = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Prozess konnte nicht gestartet werden: {fileName}");
        }

        lock (_gate)
        {
            _activeProcess = process;
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        async Task ReadAsync(StreamReader reader, StringBuilder target, Action<string>? callback)
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                target.AppendLine(line);
                callback?.Invoke(line);
            }
        }

        try
        {
            var outputTask = ReadAsync(process.StandardOutput, stdout, onOutput);
            var errorTask = ReadAsync(process.StandardError, stderr, onError);
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(outputTask, errorTask);
            return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeProcess, process))
                {
                    _activeProcess = null;
                }
            }
        }
    }

    public void CancelActive()
    {
        lock (_gate)
        {
            if (_activeProcess is { HasExited: false } process)
            {
                TryKill(process);
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Der Prozess ist zwischen Prüfung und Abbruch bereits beendet worden.
        }
    }
}
