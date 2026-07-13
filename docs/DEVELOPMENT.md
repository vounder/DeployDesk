# DeployDesk Development Guide

This guide covers local development, verification, publishing, and installer creation for the
current Windows application.

## Prerequisites

- Windows 10 or Windows 11, x64;
- .NET SDK 8.0.422 (pinned by `global.json`, embedding runtime 8.0.28);
- Git for Windows;
- Windows PowerShell;
- Windows OpenSSH client when exercising a runner that uses SSH;
- Inno Setup 6 only when building the installer.

Verify the main toolchain:

```powershell
dotnet --info
git --version
powershell.exe -NoProfile -Command '$PSVersionTable.PSVersion'
```

## Repository layout

| Path | Purpose |
|---|---|
| `src/DeployDesk` | WPF application, models, and services. |
| `tests/DeployDesk.SmokeTests` | UI animation, security-validation, and compatible-repository smoke harness. |
| `.github/workflows/ci.yml` | Windows build, UI smoke, security-validation, and dependency-audit workflow. |
| `global.json` | Reproducible, patched .NET SDK selection. |
| `docs` | User, architecture, protocol, integration, and development documentation. |
| `scripts/publish.ps1` | Self-contained publish helper. |
| `scripts/smoke-start.ps1` | Published-application GUI startup check. |
| `installer/DeployDesk.iss` | Per-user Inno Setup package definition and file association. |
| `artifacts` | Generated output; ignored by Git. |

The application has no third-party NuGet package references in the current project files.

## Restore and build

From the repository root:

```powershell
dotnet restore DeployDesk.sln
dotnet build DeployDesk.sln
```

For a release-configuration compile:

```powershell
dotnet build DeployDesk.sln -c Release
```

## Run the application

```powershell
dotnet run --project src/DeployDesk/DeployDesk.csproj
```

You can pass one link file to exercise the command-line import path:

```powershell
dotnet run --project src/DeployDesk/DeployDesk.csproj -- C:\path\to\project.deploylink
```

This opens the normal trust prompt; it does not deploy automatically.

## Verification

### WPF UI smoke test

```powershell
dotnet run --project tests/DeployDesk.SmokeTests/DeployDesk.SmokeTests.csproj -- --ui-animation
```

This starts a WPF application in an STA thread, shows `MainWindow`, switches its deployment busy
state through reflection, lets the animation run briefly, and closes the window. It verifies a
focused startup and resource path, not every user interaction.

### Compatible-repository smoke test

```powershell
dotnet run --project tests/DeployDesk.SmokeTests/DeployDesk.SmokeTests.csproj -- C:\path\to\project.deploylink
```

This test:

- loads and validates the selected schema-v2 link;
- resolves its repository and runner;
- reads branch, worktree changes, and commits through Git; and
- prints non-secret project and target metadata.

It does **not** start the deployment runner, connect over SSH, push, build remotely, or run a health
check.

### Security-validation smoke test

```powershell
dotnet run --project tests/DeployDesk.SmokeTests/DeployDesk.SmokeTests.csproj -- --security-validation
```

This creates a disposable local Git repository and verifies rejection of unsafe URI schemes, URI
userinfo, unknown and duplicate JSON properties, and runner path traversal. It performs no network
or deployment operation.

### Manual application checks

Until automated UI coverage expands, verify changes proportionally:

1. Start with no saved state and confirm English is the default.
2. Open settings and verify immediate English/German switching.
3. Verify each refresh interval and the automatic-refresh toggle.
4. Import a valid link through file picker, drag/drop, and command line as applicable.
5. Reject and accept the trust prompt deliberately.
6. Change the runner or link and confirm trust is invalidated.
7. Exercise clean and dirty worktrees without deploying to production.
8. Use a safe fixture runner to emit every JSONL type, stdout, and stderr.
9. Verify deployment confirmation, log clearing, output following, copy, and cancellation settings.
10. Restart and confirm project, trust, deploy history, and settings persistence.

Use a disposable local repository and fixture runner for destructive or commit-related tests.

### Documentation checks

Check whitespace and Markdown links before submitting:

```powershell
git diff --check

$markdownFiles = Get-ChildItem -Recurse -File -Include *.md
$markdownFiles | ForEach-Object {
    Select-String -Path $_.FullName -Pattern '\]\((?!https?://|#)([^)]+\.md)(?:#[^)]+)?\)' |
        ForEach-Object { $_ }
}
```

The second command is an inspection aid, not a complete Markdown link validator.

## Publish a self-contained build

Run:

```powershell
.\scripts\publish.ps1
```

The script calls `dotnet publish` in `Release` configuration for `win-x64`, self-contained, with
single-file publishing and compression enabled. Output is written to:

```text
artifacts\publish
```

Although the main application is configured for single-file publish, WPF can emit native runtime
libraries beside `DeployDesk.exe`. Treat the complete publish directory as the portable package.
Do not distribute only the executable unless that exact isolated layout has been tested on a clean
supported Windows machine.

The helper accepts another runtime identifier, but the application and installer are currently
documented and configured for Windows x64:

```powershell
.\scripts\publish.ps1 -Runtime win-x64
```

## Smoke-test the published application

After publishing:

```powershell
.\scripts\smoke-start.ps1
```

The script starts `artifacts\publish\DeployDesk.exe`, waits up to ten seconds for a responsive main
window, reports success, and terminates the process. It does not import a project or test a runner.

## Build the installer

The Inno Setup definition creates a lowest-privilege, per-user install under
`%LOCALAPPDATA%\Programs\DeployDesk`. It adds a Start menu shortcut, offers a desktop shortcut,
and registers `.deploylink` for the current user.

1. Publish the application.
2. Compile the installer with Inno Setup 6:

```powershell
.\scripts\publish.ps1
ISCC.exe .\installer\DeployDesk.iss
```

Output is written to `artifacts\installer`.

Before distribution, install on a clean test account and verify:

- application startup;
- optional desktop and Start menu shortcuts;
- `.deploylink` double-click import;
- uninstall cleanup of application files and registration;
- user state remains scoped to `%LOCALAPPDATA%\DeployDesk`; and
- the complete WPF runtime payload is present.

The current repository does not sign the installer or executable. Do not describe locally built
artifacts as signed or verified.

## Public release checklist

Before making the repository or a binary release public:

1. Select and add the intended project license; public visibility alone is not a license.
2. Scan the tracked tree, reachable history, ignored artifacts, filenames, and image metadata for
   credentials or private infrastructure data.
3. Review author/committer email addresses in Git history and switch to a GitHub noreply address if
   personal disclosure is not intended.
4. Build with the pinned SDK and verify that the self-contained executable embeds runtime 8.0.28
   or a newer supported security patch.
5. Authenticode-sign and timestamp the executable and installer through protected CI secrets.
6. Publish SHA-256 checksums, an SBOM, and provenance alongside the complete installer/payload.
7. Install and smoke-test the signed artifact in a clean Windows environment before publishing it.

## Versioning

The current version is declared independently in:

- `src/DeployDesk/DeployDesk.csproj` (`Version`);
- `installer/DeployDesk.iss` (`AppVersion`); and
- `README.md` and version-specific compatibility documentation.

The custom title bar and settings drawer read the application version from the built assembly, so
they do not require a separate manual update. Keep the remaining declarations aligned for a version
change and update documentation badges and status text in the same change.

## Change guidelines

### UI and localization

- Keep English as the default language.
- Add every user-facing string to both English and German resources.
- Verify live switching with the settings drawer open and closed.
- Preserve keyboard focus, readable contrast, window resizing, and high-DPI behavior.
- Respect the output-follow setting rather than forcing log scrolling.

### Configuration contract

When changing `*.deploylink` behavior, update together:

1. `docs/deploylink-v2.schema.json` or introduce a new versioned schema;
2. `DeployLinkService` runtime validation;
3. models;
4. [DeployLink specification](DEPLOYLINK_SPEC.md);
5. [AI integration guide](DEPLOYDESK_AI_INTEGRATION.md);
6. README example; and
7. smoke fixtures/checks.

Do not silently reinterpret a versioned field. Add a schema version for breaking behavior.

### Process execution

- Continue to pass untrusted or configured values through `ProcessStartInfo.ArgumentList`.
- Do not build shell command strings from link values.
- Resolve security-sensitive executables to absolute trusted paths.
- Preserve the disabled Git hook/filesystem-monitor boundary for DeployDesk-owned operations.
- Preserve stdout/stderr draining and cancellation of the full process tree.
- Keep secrets out of application-generated output.
- Treat the PowerShell runner as an explicit trust boundary.

### Persistence

- Use safe defaults when a setting is absent in an older state file.
- Do not add credentials or runner output to `state.json`.
- Preserve atomic writes and recovery from malformed state.
- Consider path and deployment metadata privacy when adding diagnostics.

## Pull-request verification checklist

- [ ] Scope is focused and existing user changes are preserved.
- [ ] `dotnet build DeployDesk.sln` succeeds.
- [ ] UI smoke test succeeds for WPF changes.
- [ ] Compatible-repository smoke test succeeds for link/Git changes.
- [ ] Security-validation smoke test succeeds for parsing, path, process, or URL changes.
- [ ] `dotnet list DeployDesk.sln package --vulnerable --include-transitive` is clean.
- [ ] Manual checks cover the changed workflow and both languages.
- [ ] No real deployment or server mutation was performed without explicit authorization.
- [ ] No key, token, private endpoint, personal path, or credential-bearing fixture is committed.
- [ ] Documentation and examples match actual behavior.
- [ ] `git diff --check` succeeds.

See [CONTRIBUTING.md](../CONTRIBUTING.md) for contribution workflow and
[SECURITY.md](../SECURITY.md) for vulnerability reporting and secure design expectations.

## Current project limitations

- CI covers Windows compilation and focused smoke/security checks, not comprehensive unit or
  end-to-end coverage.
- There are no official release artifacts or automatic updates.
- Executables and installers are not code-signed by this repository.
- The repository currently has no license file; do not make licensing claims in package metadata
  or documentation until the owner adds one.
