using System.IO;
using System.Text.Json;
using DeployDesk.Models;

namespace DeployDesk.Services;

public sealed class StateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _statePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeployDesk",
        "state.json");
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public async Task<AppState> LoadAsync()
    {
        if (!File.Exists(_statePath))
        {
            return new AppState();
        }

        try
        {
            await using var stream = File.OpenRead(_statePath);
            return await JsonSerializer.DeserializeAsync<AppState>(stream, JsonOptions) ?? new AppState();
        }
        catch (JsonException)
        {
            return new AppState();
        }
    }

    public async Task SaveAsync(AppState state)
    {
        await _saveGate.WaitAsync();
        var temporaryPath = $"{_statePath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions);
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(temporaryPath, _statePath, overwrite: true);
                    break;
                }
                catch (Exception exception) when (
                    attempt < 4 && exception is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(50 * (attempt + 1));
                }
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
            _saveGate.Release();
        }
    }
}
