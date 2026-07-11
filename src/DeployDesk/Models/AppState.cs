namespace DeployDesk.Models;

public sealed class AppState
{
    public List<string> ProjectFiles { get; init; } = [];
    public Dictionary<string, ProjectState> Projects { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProjectState
{
    public string? LastDeployedCommit { get; set; }
    public DateTimeOffset? LastDeployedAt { get; set; }
    public string? TrustedDeploymentHash { get; set; }
}
