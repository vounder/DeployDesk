# DeployDesk User Guide

This guide explains how to use DeployDesk safely as a deployment operator. For repository setup,
see the [DeployLink specification](DEPLOYLINK_SPEC.md) and the
[repository integration guide](DEPLOYDESK_AI_INTEGRATION.md).

## Contents

- [What DeployDesk does](#what-deploydesk-does)
- [Requirements](#requirements)
- [Start DeployDesk](#start-deploydesk)
- [Add a project](#add-a-project)
- [Understand the trust prompt](#understand-the-trust-prompt)
- [Read the workspace](#read-the-workspace)
- [Prepare a deployment](#prepare-a-deployment)
- [Run and observe a deployment](#run-and-observe-a-deployment)
- [Settings](#settings)
- [Local data](#local-data)
- [Troubleshooting](#troubleshooting)

## What DeployDesk does

DeployDesk is an operator interface for deployment automation that already lives in a Git
repository. It:

- loads non-secret project and target metadata from a `*.deploylink` file;
- validates the configured PowerShell runner and keeps it inside the selected repository;
- shows the local repository's current branch, changes, recent commits, and deployment history;
- optionally commits all current worktree changes;
- runs the repository's deployment script through a non-interactive contract;
- displays structured JSON Lines and ordinary process output in real time; and
- can cancel the runner and its child processes.

DeployDesk does not implement the repository's Git push, SSH connection, remote build, migration,
or health check. Those actions belong to the trusted runner. This distinction matters when
diagnosing a deployment or reviewing its security.

## Requirements

### Operator machine

- Windows 10 or Windows 11, x64;
- Git for Windows available as `git.exe`;
- Windows PowerShell available as `powershell.exe`;
- any additional command-line tools used by the repository runner, commonly OpenSSH.

A self-contained DeployDesk publish includes the .NET runtime. A source build additionally
requires the .NET 8 SDK.

### Project repository

The repository must contain:

- a schema-v2 `*.deploylink` file, normally in the repository root;
- a PowerShell runner referenced by that link file and located inside the repository;
- a valid local Git worktree; and
- all project-specific prerequisites documented by the repository owner.

Credentials must not be stored in the link file. Configure them through SSH agent, `~/.ssh/config`,
the operating system, or the appropriate server-side secret store.

## Start DeployDesk

You can start a source build with:

```powershell
dotnet run --project src/DeployDesk/DeployDesk.csproj
```

A locally built installer registers the `.deploylink` file extension. After that, double-clicking
a link file opens DeployDesk and starts the import flow. The portable application also supports
the file picker and drag and drop without registration.

## Add a project

Use one of these methods:

1. Select **Add project** and choose a `*.deploylink` file.
2. Drop a `*.deploylink` file onto the DeployDesk window.
3. Double-click a registered `*.deploylink` file in File Explorer.

DeployDesk resolves the file to an absolute path, locates the enclosing Git repository, resolves
the runner relative to the repository root, validates the supported schema and protocol, and
checks that the runner exists inside that repository.

If the same link path is already open, DeployDesk selects the existing project rather than adding
a duplicate.

## Understand the trust prompt

Before a newly imported project can run, DeployDesk displays:

- project name;
- local repository root;
- SSH user, host, and port;
- remote application path;
- absolute PowerShell runner path; and
- a SHA-256 deployment fingerprint.

The fingerprint is calculated from the complete runner file followed by the complete
`*.deploylink` file. Confirm only when the displayed repository, server, path, and runner are the
ones you intended to use.

### What trust protects

If either trusted file changes, its fingerprint changes. DeployDesk refuses to restore or run the
project until it is imported and approved again. This helps prevent an unnoticed target or runner
change.

### What trust does not protect

The fingerprint is not a signature and does not establish the identity of a publisher. It covers
only the selected link and runner files, not:

- every file in the repository;
- modules, scripts, executables, or containers loaded by the runner;
- the state of the remote server; or
- credentials and SSH host keys.

Review repository changes and use normal Git verification practices. Treat the runner as code
that can execute with your user account's permissions.

## Read the workspace

### Project list

The left sidebar contains saved link files. Each project shows a compact repository status. Use
**Remove project** to remove the selected link from DeployDesk; this does not delete the repository,
link file, runner, or remote application. It does remove that project's local trust fingerprint and
deployment-history entry from `state.json`.

### Project header

The header displays the project name, description, environment, and target summary. When supplied
by the link file:

- **Website** opens the link labeled `Website`, or the first configured project link;
- **Repository** opens the local repository in File Explorer; and
- **Sync** refreshes the Git information.

Only open project links you trust. Compatible repositories should use HTTPS URLs.

### Status cards

| Card | Meaning |
|---|---|
| Branch | The result of `git rev-parse --abbrev-ref HEAD`. |
| Worktree | The number of entries returned by `git status --porcelain`. |
| Pending | Commits between the last commit DeployDesk recorded as deployed and `HEAD`; before the first run, the configured remote branch is used when available. |
| Last deploy | The local date and time of the last runner process that exited successfully. |

“Last deploy” is local DeployDesk state. It is not proof that the remote application is still
healthy, and it is not synchronized across operator machines.

### Repository details

The lower repository area lists current worktree changes and up to 15 recent commits. The worktree
lines use Git's porcelain status format. Consult `git status` in the repository if you need a more
detailed review before committing.

## Prepare a deployment

### Review the target

Before every deployment, verify the environment badge and target summary. This is particularly
important when the same application has separate staging and production link files.

### Review local changes

If **Commit changes before deployment** is selected and the worktree is not clean, DeployDesk runs:

```text
git add -A
git commit -m <message>
```

`git add -A` includes all modifications, deletions, and untracked files in the repository. Review
the worktree list and use `git diff`/`git diff --staged` when appropriate. DeployDesk does not offer
partial staging. The checkbox defaults to off. If it is deliberately enabled, DeployDesk blocks
common secret-bearing filenames such as `.env`, private-key formats, credential stores, and publish
profiles. This filename check is not a content-aware secret scanner.

If the checkbox is cleared while changes exist, deployment stops and asks you to commit manually
or enable the option.

### Set the commit message

Enter a commit message when you want an explicit description. If the field is empty, DeployDesk
uses a local timestamp in the form:

```text
deploy: YYYY-MM-DD HH:mm
```

The commit field is cleared after a successful local commit.

### Select project options

Each boolean option comes from the trusted `*.deploylink` file. A selected option adds its
configured argument to the runner process. Examples might include migrations or seed data, but
their exact effect is defined entirely by the repository runner. Read the option tooltip and the
repository documentation before enabling it.

## Run and observe a deployment

Select **Deploy now**. When deployment confirmation is enabled in settings, review and approve the
confirmation before execution.

DeployDesk:

1. recalculates and verifies the trust fingerprint;
2. verifies that the current branch exactly matches the configured deployment branch;
3. checks the worktree and creates the optional local commit;
4. records the current `HEAD` as the candidate deployed commit;
5. starts Windows PowerShell from its absolute system path without a visible console;
6. passes the configured arguments plus DeployDesk's non-interactive protocol arguments;
7. streams stdout and stderr into the activity area; and
8. records the candidate commit and local completion time only if the runner exits with code `0`,
   emits `completed`, and does not emit `error`.

### Activity output

The runner can emit JSON Lines events such as:

```json
{"type":"step","message":"Building the application"}
{"type":"warning","message":"Seed data was skipped"}
{"type":"error","message":"Health check failed"}
```

DeployDesk formats recognized events and uses `step`, `warning`, and `error` messages to update the
current status. Non-JSON output remains visible. Stderr lines are marked with `[STDERR]`.

The in-memory activity buffer is capped at approximately 200,000 characters. Use **Copy log** to
copy the current project, timestamp, status, and visible activity to the clipboard. Check copied
content for secrets before sharing it.

### Cancel a deployment

Select **Cancel deployment** to cancel the current operation. DeployDesk requests cancellation and
kills the runner's process tree. Remote work that already started may continue because DeployDesk
cannot automatically undo commands already sent to another machine. Consult the runner's recovery
documentation before retrying.

### Understand success

DeployDesk requires runner exit code `0`, no observed structured `error` event, and an observed
`completed` event. A compliant runner performs its health check before completion and returns a
non-zero exit code for every failed or incomplete deployment.

## Settings

Open settings from the labeled button at the bottom of the project sidebar, the gear button in the
title bar, or with `Ctrl+,`. Press `Esc` to close the drawer.

| Setting | Behavior | Default |
|---|---|---|
| Language | Switches the live interface between English and German. | English |
| Automatic repository status refresh | Refreshes Git status while enabled. | On |
| Refresh interval | Selects 5, 15, 30, or 60 seconds. | 5 seconds |
| Confirm before deployment | Requires an operator confirmation before starting the runner. | On |
| Clear activity log before deployment | Starts each deployment with an empty activity area. | On |
| Follow runner output | Scrolls the activity view as new output arrives. | On |

Settings are stored under the `settings` section of
`%LOCALAPPDATA%\DeployDesk\state.json`. The selected language takes effect immediately.

## Local data

DeployDesk uses `%LOCALAPPDATA%\DeployDesk`:

| File | Contents |
|---|---|
| `state.json` | Absolute link-file paths, per-project last deployed commit/time, trust fingerprints, and user settings. |
| `startup.log` | Startup and unhandled-exception details from the latest application launch. |

The state file is not intended for credentials, but it contains local paths and deployment
metadata. Protect it with the normal permissions of your Windows account. Runner activity is held
in memory and is not automatically written to a deployment log file.

To reset DeployDesk, close the application and back up or remove `state.json`. This forgets saved
projects, trust records, deployment history, and settings; it does not change any repository or
server.

## Troubleshooting

### “Please select an existing .deploylink file”

The selected path is missing or does not end with `.deploylink`. Verify the file name and location.

### The runner is missing or outside the repository

`runner.file` must resolve to an existing file below the Git repository root. Use a repository-
relative path such as `deploy/deploy.ps1`; do not point to a shared script elsewhere on disk.

### The project disappears after restart

DeployDesk restores only saved projects whose link and runner still match the approved
fingerprint. Re-import the link, review the changed fingerprint and target, and approve it if the
change was expected.

### Git status is unavailable

Confirm that Git for Windows is installed, `git.exe` is on `PATH`, the link file is inside a valid
Git worktree, and the repository is readable. Run these commands from the repository:

```powershell
git status
git log -15 --oneline
```

### Local commit fails

Check the activity status and run `git status`. Common causes include missing Git author identity,
locked files, failing configured clean filters, or no effective change after staging. DeployDesk
disables repository Git hooks for its own Git operations.

### PowerShell runner does not start

DeployDesk resolves Windows `powershell.exe` from its absolute system path; it does not use
PowerShell 7's `pwsh.exe`. Confirm Windows PowerShell is available and that the runner is compatible
with it. The application deliberately
uses `-NoProfile` and `-ExecutionPolicy Bypass`; the trust prompt is the approval boundary for the
runner file.

### Deployment exits successfully but the application is unhealthy

This is a runner-contract defect. The runner must verify the configured health endpoint and return
a non-zero exit code when the check fails. DeployDesk cannot infer remote health independently.

### Startup fails

Review `%LOCALAPPDATA%\DeployDesk\startup.log`. It can contain absolute paths and exception details,
so remove sensitive local information before sharing it.

### Need to report a security issue?

Do not include sensitive details in a public issue. Follow [SECURITY.md](../SECURITY.md).
