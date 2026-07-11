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
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var temporaryPath = _statePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions);
        }

        File.Move(temporaryPath, _statePath, overwrite: true);
    }
}
