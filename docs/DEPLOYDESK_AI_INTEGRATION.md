# AI Agent Instructions: Integrate a Repository with DeployDesk

This is a normative work instruction for an AI coding agent adapting an existing Git repository to
DeployDesk. The expected result is one `*.deploylink` file per target and a non-interactive
PowerShell runner owned by that repository.

For field-level constraints, read the [DeployLink v2 specification](DEPLOYLINK_SPEC.md) and the
[machine-readable schema](deploylink-v2.schema.json). For DeployDesk's trust boundary, read
[SECURITY.md](../SECURITY.md).

## Objective and authority boundary

Integrate with the repository's real build and deployment workflow. Do not replace it with a
generic Docker, SSH, or copy script without understanding the application.

The agent must:

- read all applicable repository instructions before editing, including `AGENTS.md`, README files,
  CI configuration, Dockerfiles, Compose files, package manifests, and existing deployment scripts;
- preserve unrelated user changes and avoid destructive Git operations;
- determine the actual build, validation, test, runtime, and deployment commands;
- read Git and server targets exclusively from the selected link file;
- provide both a manual text mode and the DeployDesk JSON Lines mode;
- use secure, non-interactive behavior under DeployDesk; and
- run safe local checks, but never perform a real deployment, push, SSH mutation, server
  provisioning, migration, seed, or production health test without explicit authorization.

Missing values such as host, SSH user, port, remote path, branch, or health endpoint must not be
invented. Discover them from authoritative repository context or ask the user. A plausible value
is not an authorized target.

## Definition of the integration boundary

DeployDesk:

- displays and locally validates the selected link;
- asks the operator to trust the exact link and runner files;
- optionally stages and commits all local changes;
- invokes the runner with a fixed control contract;
- shows its output; and
- accepts success only for exit code `0`, no `error` event, and a `completed` event.

The repository runner:

- revalidates the link as its own security boundary;
- checks local prerequisites and Git state;
- pushes to the configured remote and branch;
- connects to the configured server;
- performs the application-specific remote update/build/start flow;
- applies optional migrations or seeds only when explicitly selected;
- verifies the configured health condition; and
- returns correct output and exit codes.

The trust hash covers only the link and runner, not helper scripts, modules, images, or other files
loaded by the runner. Keep dependencies reviewable and make that limitation clear in repository
documentation.

## 1. Analyze the repository

Before implementing anything, identify and briefly document:

| Area | Values to establish |
|---|---|
| Instructions | Applicable `AGENTS.md`, contribution rules, and user constraints |
| Git | Repository root, expected remote name, deployment branch, branch protection assumptions |
| Application | Runtime, frontend/backend composition, build, test, and start commands |
| Containers | Compose file, services, ports, networks, volumes, unique project name |
| Server | Approved host or SSH alias, user, SSH port, absolute application path |
| Health | Server-local port, HTTP path, expected status, retry and timeout policy |
| Operations | Migrations, seeds, maintenance mode, cache work, rollback/recovery |
| Links | Public website and health or dashboard URLs |
| Secrets | Existing server `.env`, secret store, SSH agent/config, deploy keys |
| Validation | Safe commands that prove syntax/config without a production change |

Prefer extending a proven deployment path. If CI already produces an artifact, the runner should
usually consume that design rather than invent a second build. If an existing script is interactive,
add an explicit non-interactive mode instead of simulating keyboard input.

### Recommended Git-based sequence

Unless the repository documents another approved strategy:

1. validate local prerequisites and configuration;
2. verify the intended local branch and worktree assumptions;
3. push the exact `HEAD` to the configured remote and target branch;
4. connect with non-interactive SSH and normal host-key verification;
5. update the remote checkout fast-forward-only;
6. run the repository's application-specific build/start flow;
7. run optional migrations or seeds only when explicitly enabled;
8. run a bounded health check; and
9. report completion and exit `0` only after health succeeds.

## 2. Create one link file per target

Place `<project-id>.deploylink` in the repository root. Use a distinct `project.id` for every
environment so local trust and deploy history do not collide.

Example:

```json
{
  "schemaVersion": 2,
  "project": {
    "id": "my-app-production",
    "name": "My App",
    "description": "Production deployment",
    "accentColor": "#89D7A7"
  },
  "repository": {
    "remote": "origin",
    "branch": "main"
  },
  "server": {
    "name": "Production",
    "host": "deploy.example.com",
    "user": "deploy",
    "sshPort": 22,
    "remotePath": "/srv/apps/my-app",
    "healthCheck": {
      "port": 3000,
      "path": "/api/health",
      "expectedStatus": 200,
      "attempts": 20,
      "intervalSeconds": 2
    }
  },
  "runner": {
    "type": "powershell",
    "file": "deploy/deploy.ps1",
    "protocol": "deploydesk-jsonl-v1",
    "arguments": []
  },
  "options": [
    {
      "id": "seed",
      "label": "Run seed data",
      "description": "Load optional seed data after deployment.",
      "type": "boolean",
      "default": false,
      "argument": "-Seed"
    }
  ],
  "links": [
    { "label": "Website", "url": "https://example.com" },
    { "label": "Health", "url": "https://example.com/api/health" }
  ]
}
```

Do not copy the example values into a real integration. Replace every value with reviewed,
authorized repository context.

### Target metadata rules

- `name` is the operator-facing environment, such as `Production` or `Staging`.
- `host` is a DNS name, IP address, or intentional SSH alias. It has no `https://` scheme and no
  `user@` prefix.
- `user` should be a least-privileged deployment account, not `root` by default.
- `sshPort` is an integer from 1 to 65535. Remember that OpenSSH uses `-p` and SCP uses `-P`.
- `remotePath` is an absolute Linux path with no `.` or `..` segments.
- `healthCheck.port` is normally the port checked from the server through `127.0.0.1`.
- `healthCheck.path` begins with `/`.
- retry counts and delays are bounded and reflect realistic startup time.

Hostnames and paths committed to a public repository are public metadata. If disclosure is
undesirable, use an appropriate deployment hostname or SSH alias after confirming it works in the
operator environment. An alias is not a secret and does not replace authentication.

### Schema hint

The `$schema` property is optional. DeployDesk does not resolve it at runtime. Add it only when the
repository has a stable, resolvable URL for this schema; do not publish the historical
`deploydesk.local` placeholder as if it were a working public endpoint.

## 3. Implement the PowerShell runner

The recommended path is `deploy/deploy.ps1`.

Minimum signature:

```powershell
param(
    [string]$DeployLinkPath,
    [switch]$NonInteractive,
    [switch]$SkipLocalGit,
    [switch]$ValidateOnly,
    [string]$CommitMessage,
    [ValidateSet("Text", "JsonLines")]
    [string]$OutputFormat = "Text"
)
```

Add one switch parameter for each boolean option. DeployDesk reserves:

- `-DeployLinkPath`
- `-NonInteractive`
- `-SkipLocalGit`
- `-ValidateOnly`
- `-OutputFormat`

Do not duplicate those names in `runner.arguments`. DeployDesk passes the selected link as a
canonical absolute path. For a manual call, the runner may auto-discover exactly one `*.deploylink`
in the repository root; if zero or multiple files exist, stop with a clear error and require
`-DeployLinkPath`.

`-ValidateOnly` is strongly recommended. It loads and validates the entire configuration and exits
before staging, committing, pushing, connecting, building, migrating, seeding, or changing any
server state.

### PowerShell compatibility

DeployDesk currently invokes `powershell.exe` with `-NoProfile` and `-ExecutionPolicy Bypass`, not
PowerShell 7 `pwsh.exe`. Write compatible syntax or explicitly document why the target repository
cannot support the current client.

Use strict error behavior:

```powershell
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
```

Check `$LASTEXITCODE` after every native process call. PowerShell does not automatically convert a
non-zero native exit code into a terminating exception in all supported environments.

## 4. Load and validate the link again

DeployDesk's UI validation is not a runner security boundary. The runner must load and validate the
file before using any value.

```powershell
$resolvedLink = (Resolve-Path -LiteralPath $DeployLinkPath -ErrorAction Stop).Path
$json = [System.IO.File]::ReadAllText($resolvedLink, [System.Text.Encoding]::UTF8)
$config = $json | ConvertFrom-Json

if ($config.schemaVersion -ne 2) {
    throw "schemaVersion 2 is required."
}

$serverUser = [string]$config.server.user
$serverHost = [string]$config.server.host
$sshPort = [int]$config.server.sshPort
$remotePath = [string]$config.server.remotePath
$gitRemote = [string]$config.repository.remote
$gitBranch = [string]$config.repository.branch
$healthPort = [int]$config.server.healthCheck.port
$healthPath = [string]$config.server.healthCheck.path
```

At minimum, validate:

- the selected link and runner are below the canonical repository root;
- schema version and required objects exist;
- host has no scheme, whitespace, control characters, or option prefix;
- user contains only the safe SSH user characters defined by the schema;
- ports are integers from 1 to 65535;
- remote path is absolute, contains no `.`/`..` segment, and uses an allowed character set;
- health path begins with `/` and contains no CR/LF or shell metacharacters;
- health status, attempts, and delay remain within schema bounds;
- Git remote does not begin with `-` and uses the schema character set;
- branch passes `git check-ref-format --branch`;
- all option IDs and switches are known; and
- no duplicate or conflicting target values are hidden in static arguments.

Never use unchecked configuration as shell code. Prefer sending structured data to a fixed remote
script. If values must appear in a shell command, combine strict allowlists with quoting appropriate
to the exact destination shell. PowerShell quoting does not automatically make a value safe for
Bash, SSH, Docker Compose, SQL, or another interpreter.

## 5. Implement the output protocol

Keep text output for manual use. Under `-OutputFormat JsonLines`, write one compact JSON object per
stdout line:

```json
{"type":"step","message":"Checking the server connection"}
{"type":"success","message":"Server is reachable"}
{"type":"warning","message":"Seed data was skipped"}
{"type":"error","message":"Health check failed"}
{"type":"completed","message":"Deployment completed"}
```

Allowed event types:

- `step`
- `success`
- `warning`
- `error`
- `completed`

A helper avoids invalid escaping:

```powershell
function Write-DeployEvent {
    param(
        [ValidateSet("step", "success", "warning", "error", "completed")]
        [string]$Type,
        [string]$Message
    )

    if ($OutputFormat -eq "JsonLines") {
        [pscustomobject]@{ type = $Type; message = $Message } |
            ConvertTo-Json -Compress |
            Write-Output
    } else {
        Write-Host "[$($Type.ToUpperInvariant())] $Message"
    }
}
```

Protocol rules:

- `-NonInteractive` must never call `Read-Host`, open a dialog, or wait for input.
- `-SkipLocalGit` skips local staging and commit because DeployDesk owns that step.
- Ordinary tool output may also appear, but must not reveal secrets.
- Critical failure emits an `error` event before exiting non-zero when practical.
- Exit code `0` is reserved for a genuinely complete deployment.
- `completed` is emitted only after a successful health check.

DeployDesk requires exit code `0`, no observed `error` event, and a `completed` event. The runner
must keep all three signals consistent and must not emit completion before health verification.

## 6. Build a safe deployment sequence

### 6.1 Prerequisites

Verify required executables with clear messages. Typical dependencies are `git`, `ssh`, Docker,
Docker Compose, and application-specific tooling. In non-interactive mode, fail rather than prompt
to install or authenticate.

### 6.2 SSH

Use the normal SSH configuration and host-key database. A typical connectivity check includes:

- `BatchMode=yes` so missing authentication fails instead of prompting;
- the configured port;
- a short connection timeout; and
- an inert fixed command such as `true`.

Never make `StrictHostKeyChecking=no` the default. Do not auto-accept an unknown production host
key. Document the authorized, out-of-band host-key enrollment process.

### 6.3 Local Git

Under DeployDesk, `-SkipLocalGit` must prevent local staging and commit. Still verify the repository,
current branch, and selected `HEAD`. Push explicitly and inspect the exit code:

```text
git push <remote> HEAD:<branch>
```

Do not infer that a successful local commit means a successful push.

### 6.4 Remote update

Use a fixed remote script or carefully quoted fixed commands. Verify the configured directory and
repository before changing it. Prefer:

```text
git fetch <remote>
git merge --ff-only <remote>/<branch>
```

Do not use `reset --hard`, `clean -fd`, delete/recreate, or force push unless that destructive
strategy is explicitly authorized and documented.

### 6.5 Application build and start

Reuse the repository's real commands. For Docker Compose:

- select a unique Compose project name;
- avoid global `container_name` collisions;
- review ports, networks, volumes, and service dependencies;
- run `docker compose config` before an authorized start when practical;
- check the exit code of build and `up` separately; and
- do not hide a failed build or start as a later health warning.

An SCP fallback must use a complete, reproducible artifact with reviewed exclusions. Do not copy a
hand-maintained partial file list that can silently omit a runtime dependency.

### 6.6 Migrations and seeds

Expose optional, consequential work as explicit safe switches. Defaults should normally be false.
Document idempotency, ordering, backups, failure behavior, and whether retrying is safe.

Do not run a migration or seed in validation mode. Do not infer authorization for production data
changes from authorization to edit repository files.

### 6.7 Health check

Run the configured check from the location whose reachability is meaningful. For an internal
service, this is commonly an HTTP request from the server to `127.0.0.1:<port><path>`.

Use:

- the configured expected status;
- a per-request timeout;
- the configured bounded attempt count and delay;
- useful progress events; and
- a final non-zero exit when the condition is never satisfied.

Only after health succeeds may the runner emit `completed` and exit `0`.

## 7. Keep secrets and provisioning out of the link

Never store in Git, `*.deploylink`, logs, or process arguments:

- passwords or tokens;
- private SSH keys;
- `.env` contents;
- credential-bearing database URLs;
- registry, cloud, or package-feed credentials; or
- production data.

SSH credentials belong in the SSH agent, protected user-profile files, or an appropriate OS store.
Application secrets belong in a protected server-side `.env`, Docker/Kubernetes secrets, or a
dedicated secret-management service.

One-time server preparation must be separate from every deployment and explicitly documented:

- install Docker/Compose or required runtimes;
- create the least-privileged deploy user and permissions;
- configure repository access and deploy keys;
- enroll and verify SSH host keys;
- create server-side environment variables/secrets;
- configure DNS, reverse proxy, TLS, firewall, and backups; and
- document recovery and rollback.

Do not add automatic privileged provisioning to the regular DeployDesk runner merely because the
server is new.

## 8. Verify without deploying

Follow all repository-specific checks first. Then perform the integration checks below without
connecting to or mutating production.

### Parse JSON

```powershell
Get-Content -Raw .\my-app-production.deploylink | ConvertFrom-Json | Out-Null
```

Validate against `docs/deploylink-v2.schema.json` using an available Draft 2020-12 validator. A
successful `ConvertFrom-Json` call proves only syntax, not schema compliance.

### Parse PowerShell syntax

```powershell
$tokens = $null
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path .\deploy\deploy.ps1),
    [ref]$tokens,
    [ref]$errors
) | Out-Null

if ($errors.Count) {
    $errors | Format-List
    exit 1
}
```

### Run validation-only mode

```powershell
.\deploy\deploy.ps1 `
    -DeployLinkPath .\my-app-production.deploylink `
    -ValidateOnly
```

Confirm from code and output that this path cannot push, connect, build, migrate, seed, start, stop,
or perform a real health request.

### Exercise JSONL locally

If the runner supports a fixture or dry-run mode, verify that every emitted structured line parses
independently:

```powershell
$lines = .\deploy\deploy.ps1 `
    -DeployLinkPath .\my-app-production.deploylink `
    -ValidateOnly `
    -NonInteractive `
    -OutputFormat JsonLines

$lines | ForEach-Object { $_ | ConvertFrom-Json | Out-Null }
```

Do not add a fake dry-run mode that claims remote success. Validation output should describe only
what was actually checked.

### Static integration review

- [ ] Link has `schemaVersion: 2` and passes the checked-in schema.
- [ ] Exactly one reviewed server target is represented by the file.
- [ ] Project ID is stable and unique for that target.
- [ ] Runner path exists and remains inside the repository.
- [ ] Every `options[].argument` has a matching PowerShell parameter.
- [ ] Host, user, ports, paths, remote, and branch are read from the link, not duplicated in code.
- [ ] `runner.arguments` contains no reserved parameters or hidden target values.
- [ ] No credentials or private data appear in the link, runner, fixtures, or output.
- [ ] JSONL events are one object per line.
- [ ] Exit codes are checked after native commands and propagated correctly.
- [ ] `completed` and exit `0` happen only after health success.
- [ ] SSH uses batch mode and preserves host-key verification.
- [ ] Remote Git update is fast-forward-only unless another strategy is explicitly approved.
- [ ] Optional migrations/seeds have deliberate safe defaults.
- [ ] `git diff --check` passes.
- [ ] Unrelated user changes are not staged or overwritten.
- [ ] No real deployment or server mutation occurred.

### DeployDesk smoke check

After local validation, import the link into DeployDesk. Confirm the trust prompt shows the exact
repository, runner, SSH target, and remote path. The import is a local trust action and must not
start the runner.

The DeployDesk repository also contains a compatible-repository smoke harness:

```powershell
dotnet run --project tests/DeployDesk.SmokeTests/DeployDesk.SmokeTests.csproj -- `
    C:\path\to\my-app-production.deploylink
```

This reads configuration and local Git status only. Reconfirm that behavior from the current code
before using it in a sensitive environment.

## 9. Request authorization for a real deployment

Repository-editing authority is not deployment authority. Before any real push, SSH command,
server build, migration, seed, restart, or health request, obtain explicit authorization that names
the target.

State clearly:

- project and environment;
- host/alias, SSH port, user, and remote path;
- Git remote, branch, and exact commit;
- optional migrations/seeds selected;
- expected service impact;
- validation already completed; and
- rollback or recovery plan.

If authorization is not granted, stop after local validation and report the unexecuted deployment
steps. Do not treat silence, a request to “finish the integration,” or access to credentials as
authorization to deploy.

## Definition of done

- A valid schema-v2 link exists in the repository root for each intended target.
- Every target uses a unique project ID and can define its own host, user, SSH port, and remote path.
- Target and Git values come from the link, not hardcoded runner values.
- The runner supports `-DeployLinkPath`, text mode, JSONL mode, non-interactive mode, skip-local-Git,
  and validation-only behavior.
- Runner and link stay inside the repository and contain no secrets.
- Options have safe defaults and matching runner switches.
- Native command failures propagate to a non-zero runner exit.
- Health failure prevents `completed` and exit code `0`.
- SSH host-key checking is preserved.
- One-time server preparation and recovery are documented separately.
- Repository build, test, syntax, schema, and integration checks pass.
- Documentation explains manual use and DeployDesk use.
- No real deployment or server mutation occurred without explicit authorization.

## Final report template

The AI's completion report should include:

1. changed files and their purpose;
2. the non-secret target summary;
3. runner sequence and optional controls;
4. every command/check executed and its result;
5. checks not run and why;
6. one-time server preparation still required;
7. known risks, recovery requirements, and trust-hash dependency limits; and
8. an explicit statement that no real deployment occurred, unless a separately authorized run was
   actually completed.
