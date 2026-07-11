using System.Text.Json.Serialization;

namespace DeployDesk.Models;

public sealed class DeployLink
{
    public int SchemaVersion { get; init; }
    public ProjectDefinition Project { get; init; } = new();
    public RepositoryDefinition Repository { get; init; } = new();
    public RunnerDefinition Runner { get; init; } = new();
    public List<DeployOptionDefinition> Options { get; init; } = [];
    public List<ProjectLinkDefinition> Links { get; init; } = [];

    [JsonIgnore]
    public string SourcePath { get; set; } = string.Empty;

    [JsonIgnore]
    public string RepositoryRoot { get; set; } = string.Empty;

    [JsonIgnore]
    public string RunnerPath { get; set; } = string.Empty;
}

public sealed class ProjectDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public string AccentColor { get; init; } = "#35D079";
}

public sealed class RepositoryDefinition
{
    public string Remote { get; init; } = "origin";
    public string Branch { get; init; } = "main";
}

public sealed class RunnerDefinition
{
    public string Type { get; init; } = "powershell";
    public string File { get; init; } = string.Empty;
    public string Protocol { get; init; } = "deploydesk-jsonl-v1";
    public List<string> Arguments { get; init; } = [];
}

public sealed class DeployOptionDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Type { get; init; } = "boolean";
    public bool Default { get; init; }
    public string Argument { get; init; } = string.Empty;
}

public sealed class ProjectLinkDefinition
{
    public string Label { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}
