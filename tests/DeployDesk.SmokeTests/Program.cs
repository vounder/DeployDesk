using DeployDesk.Services;

if (args.Length != 1)
{
    Console.Error.WriteLine("Aufruf: DeployDesk.SmokeTests <projekt.deploylink>");
    return 2;
}

var processService = new ProcessService();
var gitService = new GitService(processService);
var deployLinkService = new DeployLinkService(gitService);

try
{
    var config = await deployLinkService.LoadAsync(args[0]);
    var branch = await gitService.GetBranchAsync(config.RepositoryRoot);
    var changes = await gitService.GetChangesAsync(config.RepositoryRoot);
    var commits = await gitService.GetCommitsAsync(config.RepositoryRoot);

    Console.WriteLine($"OK: {config.Project.Name}");
    Console.WriteLine($"Repo: {config.RepositoryRoot}");
    Console.WriteLine($"Branch: {branch}");
    Console.WriteLine($"Änderungen: {changes.Count}");
    Console.WriteLine($"Commits: {commits.Count}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
