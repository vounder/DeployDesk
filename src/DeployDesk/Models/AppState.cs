namespace DeployDesk.Models;

public sealed class AppState
{
    public List<string> ProjectFiles { get; init; } = [];
    public Dictionary<string, ProjectState> Projects { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public ApplicationSettings Settings { get; set; } = new();
}

public sealed class ApplicationSettings
{
    public string Language { get; set; } = "en";
    public bool AutoRefreshEnabled { get; set; } = true;
    public int AutoRefreshIntervalSeconds { get; set; } = 5;
    public bool ConfirmBeforeDeploy { get; set; } = true;
    public bool ClearLogBeforeDeploy { get; set; } = true;
    public bool AutoScrollLog { get; set; } = true;
}

public sealed class ProjectState
{
    public string? LastDeployedCommit { get; set; }
    public DateTimeOffset? LastDeployedAt { get; set; }
    public string? TrustedDeploymentHash { get; set; }
}
