# Security Policy

DeployDesk starts repository-provided deployment code with the current Windows user's permissions.
Security reports are taken seriously, particularly when they affect trust validation, path
confinement, process arguments, secret exposure, or unintended deployment targets.

## Supported versions

There are no official release artifacts yet. Until a release process is established, security
fixes target the latest `main` branch and the current `0.3.x` development line. Older commits are
not maintained as supported releases.

## Report a vulnerability privately

Do not disclose a vulnerability, exploit, credential, private host, or sensitive log in a public
issue or discussion.

1. Prefer GitHub's private vulnerability reporting / Security Advisory flow for this repository
   when it is available.
2. If private reporting is unavailable, use a private contact method published by the repository
   owner or maintainer.
3. If no private contact is available, open a minimal public issue asking for a private reporting
   channel. Do not include technical details that make the issue exploitable.

Include when possible:

- affected commit or version;
- relevant Windows version and architecture;
- prerequisites and minimal reproduction;
- expected and observed behavior;
- security impact and realistic attack path;
- whether user confirmation is required;
- sanitized logs, screenshots, or a proof of concept; and
- any suggested mitigation.

Remove API keys, private keys, tokens, passwords, personal paths, private hostnames, and production
data. Maintainers may ask follow-up questions and coordinate disclosure after a fix is available.

## Security model

DeployDesk provides a local operator boundary around repository-owned automation. It is designed
to reduce accidental execution of an unnoticed target or runner change, not to sandbox or prove
the safety of repository code.

### Protections provided by DeployDesk

- Canonical resolution of the selected link file, Git root, and runner.
- Rejection of a missing, oversized, or link/junction-escaped runner outside the repository root.
- Bounded JSON/runner input and conservative validation of target, Git, health, option, and web-link values.
- Rejection of duplicate and unknown JSON properties.
- An explicit first-import prompt showing repository, target, remote path, runner, and fingerprint.
- A SHA-256 fingerprint of the selected runner and `*.deploylink` content.
- Revalidation of that fingerprint before execution; a change requires re-import and approval.
- Process arguments added individually through `ProcessStartInfo.ArgumentList`.
- Git and Windows PowerShell resolved to absolute executable paths; repository Git hooks and
  filesystem monitors are disabled for DeployDesk-owned Git operations.
- Bounded redirected output, a bounded UI queue, and the ability to cancel the local runner process tree.
- Enforcement of the configured branch plus exit code `0`, no `error` event, and a terminal
  `completed` event before deployment history is updated.
- Automatic commit defaults to off, requires a reviewed confirmation, and blocks common
  secret-bearing filenames as a defense-in-depth heuristic.
- Per-user state stored under `%LOCALAPPDATA%\DeployDesk`.

### Protections not provided by DeployDesk

DeployDesk does not:

- authenticate the repository author or sign its contents;
- fingerprint the entire repository or recursively hash runner dependencies;
- sandbox the runner, imported modules, child scripts, executables, containers, or Git filters;
- manage credentials, private keys, or server-side secrets;
- independently verify Git push, SSH host keys, remote commands, builds, migrations, or health;
- provide rollback, remote locking, or transactional deployment;
- redact secrets from runner stdout/stderr or clipboard content;
- create a durable, tamper-resistant audit log; or
- sign or automatically update published binaries.

The operator's approval means “run this exact link and runner file from this repository,” not
“every dependency and remote effect has been independently verified.”

## Trust fingerprint boundary

The deployment fingerprint is:

```text
SHA-256(runner bytes || 0x00 || deploylink bytes)
```

It changes when either selected file changes. It does not change when another repository file,
PowerShell module, executable, Git hook, container image, remote branch, or server resource changes.

Runner authors should minimize dynamic dependencies and should validate or pin dependencies where
appropriate. Operators should inspect Git changes before approving a modified runner or deploying
a changed repository.

SHA-256 is used for local change detection. The fingerprint is not a publisher signature and has
no external chain of trust.

## Secrets policy

Never place the following in a `*.deploylink`, runner source, committed fixture, application state,
or runner output:

- passwords or access tokens;
- private SSH keys;
- `.env` values;
- credential-bearing database, cache, or message-broker URLs;
- package, container-registry, or cloud credentials; or
- production data.

Use:

- SSH agent or keys protected in the user profile;
- `~/.ssh/config` for non-secret connection aliases and parameters;
- Windows or another operating-system credential store;
- a server-side `.env` protected by file permissions;
- Docker/Kubernetes secrets; or
- a dedicated secret-management service.

The target hostname, user name, port, and remote path are visible during trust review and may be
committed as non-secret metadata. They become public when the repository is public. Use a suitable
deployment hostname or alias if disclosing an infrastructure address is undesirable; an alias is
not itself a credential.

## Safe runner requirements

A compatible runner is executable code and must defend its own boundary. It should:

1. Re-read and validate the selected `-DeployLinkPath`.
2. Confirm canonical link and runner paths remain inside the expected repository.
3. Validate hosts, users, ports, paths, remote names, branches, health parameters, and option values.
4. Treat all configuration as data; never concatenate it into local or remote shell code.
5. Use `BatchMode=yes` for non-interactive SSH and preserve normal `known_hosts` verification.
6. Never default to `StrictHostKeyChecking=no`.
7. Use a least-privileged deployment account.
8. Avoid destructive Git operations such as unapproved `reset --hard`.
9. Use fast-forward-only remote updates and verify every exit code.
10. Run a bounded health check after build/start and before reporting success.
11. Return a non-zero exit code for every failed, cancelled, or incomplete deployment.
12. Emit `completed` only after the health condition succeeds.
13. Avoid printing environment dumps, verbose credential-bearing commands, or secret values.

See the [DeployLink specification](docs/DEPLOYLINK_SPEC.md) and
[integration guide](docs/DEPLOYDESK_AI_INTEGRATION.md).

## Operator safety

- Review the target and remote path on every deployment, especially for production.
- Review worktree changes before using automatic commit. DeployDesk uses `git add -A`, including
  untracked files and deletions.
- Enable deployment confirmation unless a controlled workflow explicitly calls for otherwise.
- Treat project links as repository-controlled input; DeployDesk permits only HTTP/HTTPS and HTTPS
  should be preferred.
- Do not share copied activity logs until they have been checked for secrets and private metadata.
- Understand that cancelling the local process cannot roll back remote commands already accepted.
- Re-import changed trust content only after reviewing the diff.

## Local data and privacy

`%LOCALAPPDATA%\DeployDesk\state.json` stores absolute local link paths, project trust fingerprints,
last deployed commit/time, and application settings. It is not intended to contain credentials,
but paths and deployment metadata may be sensitive in some environments.

`%LOCALAPPDATA%\DeployDesk\startup.log` contains startup and unhandled-exception information and can
include local paths or exception messages. Runner activity is memory-only unless an operator copies
it. Apply normal Windows account permissions and sanitize diagnostics before sharing.

## Dependency and artifact integrity

The current project has no third-party NuGet package references, but it depends on the pinned .NET
8.0.422 SDK / 8.0.28 runtime, WPF native components, Git, Windows PowerShell, and runner-specific
tools. Obtain build tools from trusted sources and review tool versions in sensitive environments.

The repository does not currently provide signed releases, published checksums, an updater, or
build provenance. Its CI workflow verifies source builds and focused checks; it does not make a
locally built executable or installer an official signed artifact.

## Security review checklist

Changes affecting any of these areas deserve explicit security review:

- link parsing, regexes, schema, canonical paths, or repository confinement;
- trust storage, hashing, comparison, or re-import behavior;
- PowerShell, Git, URL, or process invocation;
- deployment confirmation and target presentation;
- activity logging, clipboard, exceptions, or local state;
- settings defaults that reduce operator confirmation or visibility;
- file association and command-line import;
- packaging, signing, updates, or release distribution; and
- runner protocol success/failure semantics.

Run the checks in [the development guide](docs/DEVELOPMENT.md) and use only disposable targets for
security tests unless a real deployment is explicitly authorized.
