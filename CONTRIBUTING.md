# Contributing to DeployDesk

Thank you for helping improve DeployDesk. The project values focused changes, explicit security
boundaries, accurate documentation, and a calm operator experience.

## Before you start

1. Read the [README](README.md), [architecture](docs/ARCHITECTURE.md), and
   [security policy](SECURITY.md).
2. Search existing issues and pull requests before proposing the same change.
3. For a large feature, public contract change, or major visual redesign, open an issue first so
   scope and compatibility can be discussed.
4. Never use production infrastructure for development or testing without explicit authorization.

Security vulnerabilities must not be reported in a public issue. Follow
[SECURITY.md](SECURITY.md).

## Development setup

DeployDesk development requires Windows x64 and the pinned .NET SDK 8.0.422.

```powershell
git clone <your-fork-url>
cd DeployDesk
dotnet restore DeployDesk.sln
dotnet build DeployDesk.sln
dotnet run --project src/DeployDesk/DeployDesk.csproj
```

See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for smoke tests, packaging, and detailed checks.

## Choose a focused change

Good contributions include:

- bug fixes with a clear reproduction;
- safer configuration, trust, path, or process handling;
- focused accessibility and localization improvements;
- tests for current behavior and edge cases;
- protocol documentation that matches implementation;
- operator workflow improvements that preserve repository ownership; and
- packaging or release improvements with verifiable behavior.

Avoid combining unrelated cleanup, style changes, features, and documentation rewrites in one pull
request. Preserve unrelated working-tree changes.

## Implementation principles

### Keep the responsibility boundary clear

DeployDesk presents, validates, approves, invokes, and observes. Repository runners push, connect,
build, migrate, verify health, and recover. A new feature should live on the correct side of that
boundary and be documented accordingly.

### Prefer safe process APIs

Pass values through `ProcessStartInfo.ArgumentList`. Do not interpolate configuration into shell
command strings. Keep runner confinement, cancellation, and stdout/stderr draining intact.

### Keep secrets out

Do not commit real hosts when they are considered private, personal paths, keys, tokens, `.env`
content, credential-bearing URLs, or captured production output. Use `example.com`, disposable
repositories, and synthetic fixtures.

### Maintain both languages

English is the default interface language and German is supported. Every user-visible string must
be available in both languages. Test the immediate live switch and layout in each language.

### Preserve compatibility

The `*.deploylink` schema and JSONL runner protocol are public contracts. When changing them:

- prefer additive changes;
- keep schema versions explicit;
- update runtime validation, models, schema, documentation, examples, and tests together; and
- do not silently assign a new meaning to an existing field or event.

## Test your change

At minimum:

```powershell
dotnet build DeployDesk.sln
dotnet run --project tests/DeployDesk.SmokeTests/DeployDesk.SmokeTests.csproj -- --ui-animation
dotnet run --project tests/DeployDesk.SmokeTests/DeployDesk.SmokeTests.csproj -- --security-validation
git diff --check
```

For link, Git, trust, or runner changes, also use a disposable compatible repository:

```powershell
dotnet run --project tests/DeployDesk.SmokeTests/DeployDesk.SmokeTests.csproj -- C:\path\to\fixture.deploylink
```

The repository smoke command does not deploy. Do not turn a validation task into a real SSH,
server, or production test without explicit authorization.

Manually exercise the changed workflow. For UI/settings work, verify English and German, restart
persistence, keyboard and mouse behavior, relevant window sizes, and the default settings.

## Documentation expectations

Documentation is part of the change when behavior changes. Relevant locations include:

- `README.md` for public positioning and quick start;
- `docs/USER_GUIDE.md` for operator workflows;
- `docs/ARCHITECTURE.md` for responsibility and data-flow changes;
- `docs/DEPLOYLINK_SPEC.md` for the public repository contract;
- `docs/DEPLOYDESK_AI_INTEGRATION.md` for adoption instructions;
- `docs/DEVELOPMENT.md` for contributor and release workflows; and
- `SECURITY.md` for trust or threat-model changes.

Use examples that can be copied safely. Do not claim releases, CI, signing, licensing, or platform
support that the repository does not provide.

## Commit guidance

Use concise, imperative commits that explain the reason for a change. Conventional prefixes such
as `feat:`, `fix:`, `docs:`, `test:`, and `chore:` are welcome but not required.

Before committing:

```powershell
git status --short
git diff --check
git diff
```

Confirm that generated `bin`, `obj`, and `artifacts` content is not staged.

## Pull-request checklist

Include in the pull-request description:

- problem and user impact;
- approach and important tradeoffs;
- security-boundary changes;
- files or protocol fields affected;
- verification commands and results;
- screenshots for visual changes, with private data removed; and
- remaining limitations or follow-up work.

Before requesting review:

- [ ] The change is focused and unrelated user work is preserved.
- [ ] Build and relevant smoke checks pass.
- [ ] Both supported languages are complete for UI changes.
- [ ] No real secret, private target, or personal path is included.
- [ ] Public contracts and documentation match the implementation.
- [ ] Failure and cancellation behavior is tested where relevant.
- [ ] No real deployment occurred without explicit authorization.
- [ ] `git diff --check` passes.

## Review priorities

Reviewers will prioritize:

1. target and credential safety;
2. trust, validation, and process boundaries;
3. failure and cancellation semantics;
4. backward compatibility;
5. operator clarity and accessibility;
6. test evidence; and
7. maintainability.

A reviewer may ask for a smaller change or additional fixture coverage before discussing aesthetic
preferences.

## Licensing status

This repository currently does not contain a license file. Public visibility alone does not grant
general permission to copy, redistribute, or create derivative works. Before investing in a
substantial contribution, confirm the intended contribution and licensing terms with the project
owner. Do not add license headers or assert a project license without owner approval.
