# DeployLink v2 Specification

This document defines the public repository contract consumed by DeployDesk `0.3.x`. The
machine-readable schema is [`deploylink-v2.schema.json`](deploylink-v2.schema.json).

Use this document when authoring a project link or compatible runner. Use the
[integration guide](DEPLOYDESK_AI_INTEGRATION.md) for a safe end-to-end adoption workflow.

## Scope

A `*.deploylink` file describes:

- the project shown in DeployDesk;
- the expected Git remote and branch;
- one non-secret deployment target;
- the repository-local PowerShell runner;
- optional boolean runner controls; and
- optional operator links.

It is not a credentials file, server provisioning manifest, shell script, or substitute for the
repository runner.

## File placement and naming

- Place the file in the Git repository root.
- Use the `.deploylink` extension.
- Prefer `<project-id>.deploylink`, for example `storefront-production.deploylink`.
- Create a separate file and unique project ID for each target environment.
- Encode the file as UTF-8 JSON.

DeployDesk can technically locate the enclosing Git root when the link is below it, but root
placement keeps manual runner discovery and repository review predictable.

## Complete example

```json
{
  "schemaVersion": 2,
  "project": {
    "id": "storefront-production",
    "name": "Storefront",
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
    "remotePath": "/srv/apps/storefront",
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
      "description": "Load optional seed data after the application starts.",
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

The optional `$schema` property is informational in the current application. DeployDesk does not
resolve it or run a general-purpose JSON Schema validator. If a repository includes `$schema`, use
a stable URL that actually serves the checked-in schema; do not depend on the historical
`deploydesk.local` placeholder.

## Top-level object

| Property | Required | Type | Description |
|---|---:|---|---|
| `$schema` | No | string | Optional editor/schema hint. Not resolved by DeployDesk. |
| `schemaVersion` | Yes | integer | Must be exactly `2`. |
| `project` | Yes | object | Display identity and metadata. |
| `repository` | Yes | object | Expected Git remote and branch. |
| `server` | Yes | object | One non-secret target and health-check definition. |
| `runner` | Yes | object | Repository-local runner and protocol. |
| `options` | No | array | Boolean operator choices mapped to runner switches. |
| `links` | No | array | External project links. |

The JSON Schema rejects unknown properties through `additionalProperties: false`. The runtime also
rejects unmapped members and case-insensitive duplicate JSON properties. Authors should still run a
Draft 2020-12 schema validator because the application uses explicit C# checks rather than a
general-purpose JSON Schema engine.

## `project`

| Property | Required | Constraints | Runtime use |
|---|---:|---|---|
| `id` | Yes | `^[a-z0-9][a-z0-9_-]{1,63}$` | Key for local trust and deploy history. Keep it globally distinct. |
| `name` | Yes | 1–120 characters without control characters | Project name in the workspace. |
| `description` | No | Up to 2,000 characters without control characters | Project description; local repository path is the UI fallback. |
| `icon` | No | String | Reserved project metadata; the current workspace does not load it. |
| `accentColor` | No | `#RRGGBB` | Reserved project metadata; the current workspace uses its global theme. |

Example:

```json
{
  "id": "billing-staging",
  "name": "Billing API",
  "description": "Staging environment"
}
```

Project IDs are used as keys in local state and must be unique on an operator device. DeployDesk
rejects importing a second active project with the same ID.

## `repository`

| Property | Required | Constraints | Meaning |
|---|---:|---|---|
| `remote` | Yes | `^[A-Za-z0-9][A-Za-z0-9._/-]*$` | Git remote name used by status comparison and the runner. Usually `origin`. |
| `branch` | Yes | Up to 255 safe ref characters; no `..` or `.lock` suffix | Deployment branch, usually `main`. |

The runtime accepts a conservative subset of Git ref names. The runner should additionally verify:

```powershell
git check-ref-format --branch $branch
```

DeployDesk does not change branches or push in its `GitService`. It requires the current branch to
exactly match `repository.branch` before committing or starting the runner. The runner should still
verify the selected commit and push to the configured destination safely.

## `server`

The server block is visible in the trust prompt. It is target metadata, not secret storage.

| Property | Required | Constraints | Meaning |
|---|---:|---|---|
| `name` | No | 1–40 characters; defaults to `Production` | Environment label. |
| `host` | Yes | 1–253 characters, letters/digits/`.`/`_`/`:`/`-` | DNS name, IP address, or deliberate SSH alias; no scheme or user prefix. |
| `user` | Yes | `^[A-Za-z_][A-Za-z0-9._-]{0,31}$` | SSH user. Prefer an unprivileged deploy account. |
| `sshPort` | Yes | Integer 1–65535 | SSH port. |
| `remotePath` | Yes | Absolute Linux path, maximum 1024 characters, no `.` or `..` segments | Remote application directory. |
| `healthCheck` | Yes | Object | Health condition the runner must verify. |

Allowed remote paths match `^/[A-Za-z0-9._/-]+$`. This intentionally excludes whitespace and
shell metacharacters. The runner must still treat all configuration values as data and quote them
for the destination command language.

### `server.healthCheck`

| Property | Required | Constraints | Default/model value |
|---|---:|---|---:|
| `port` | Yes | Integer 1–65535 | None |
| `path` | Yes | Begins with `/`; safe URL-path characters; maximum 2048 | `/` |
| `expectedStatus` | No | Integer 100–599 | `200` |
| `attempts` | No | Integer 1–120 | `20` |
| `intervalSeconds` | No | Integer 1–60 | `2` |

The health definition is consumed by the runner, not checked directly by DeployDesk. A compliant
runner returns success only after the expected status is observed within the bounded retry policy.

## `runner`

| Property | Required | Supported value / meaning |
|---|---:|---|
| `type` | Yes | Must be `powershell`. |
| `file` | Yes | Safe repository-relative `.ps1` path, 4–1,024 characters, with no drive/root, `.`/`..` segment, colon, or control character. |
| `protocol` | Yes | Must be `deploydesk-jsonl-v1`. |
| `arguments` | No | Up to 64 non-empty trusted static arguments, each at most 1,024 characters without control characters. |

DeployDesk caps the link at 1 MiB and the runner at 16 MiB. It resolves `file` canonically,
including filesystem links and junctions, and rejects it if it is missing or resolves outside the
repository. The runner and link file are included in the trust fingerprint.

Every entry in `runner.arguments` is passed as a discrete process argument. The following arguments
are reserved and must not be present, including `-Name:value` and `-Name=value` forms:

- `-DeployLinkPath`
- `-NonInteractive`
- `-SkipLocalGit`
- `-ValidateOnly`
- `-OutputFormat`

Static arguments can change runner behavior and are part of the trusted link content. Keep them
minimal. Do not place secrets in them; command lines may be visible to local diagnostic tools.

## `options`

Each option exposes one boolean choice in the deployment area.

| Property | Required | Constraints | Meaning |
|---|---:|---|---|
| `id` | Yes | `^[a-z0-9][a-z0-9_-]{0,63}$`; unique case-insensitively | Stable option identifier. |
| `label` | Yes | 1–120 characters without control characters | Operator-facing label. |
| `description` | No | Up to 1,000 characters without control characters | Tooltip/help text explaining consequences. |
| `type` | Yes | Must be `boolean`. | Only supported option type. |
| `default` | No | Boolean, default `false` | Initial selection for the project. |
| `argument` | Yes | `^-[A-Za-z][A-Za-z0-9-]{0,63}$`; not reserved | Runner switch appended only when selected. |

Use unique IDs and arguments. Choose conservative defaults, especially for destructive or
irreversible work. Operations such as seed loading or migrations should default to `false` unless
the repository's deployment contract makes them universally safe.

The runtime enforces the same safe switch shape, rejects reserved DeployDesk arguments, and rejects
duplicate option IDs.

## `links`

| Property | Required | Meaning |
|---|---:|---|
| `label` | Yes | 1–120 character operator-facing name without control characters. |
| `url` | Yes | HTTP or HTTPS absolute URL with a host and no embedded user information. Prefer HTTPS. |

At most 32 links are supported. The **Website** button prefers a link whose label equals `Website`
case-insensitively, then falls back to the first valid configured link. Only HTTP and HTTPS links
are accepted and opened through the Windows shell. Repository authors should prefer reviewed HTTPS
links.

## Runner process contract

DeployDesk starts the runner from the repository root with this shape:

```text
powershell.exe
  -NoProfile
  -ExecutionPolicy Bypass
  -File <absolute-runner-path>
  <runner.arguments...>
  -DeployLinkPath <absolute-link-path>
  -NonInteractive
  -SkipLocalGit
  -OutputFormat JsonLines
  <selected-option-arguments...>
```

The arguments are supplied individually through the process API, not joined into a shell string.

### Required runner parameters

A recommended minimum signature is:

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

Project option switches are added to this signature. `-ValidateOnly` and `-CommitMessage` support a
useful manual workflow but are not currently passed by the DeployDesk UI.

### Behavioral requirements

- `-NonInteractive` must prohibit `Read-Host`, modal prompts, and other input waits.
- `-SkipLocalGit` must skip local staging and commit logic because DeployDesk owns that step.
- `-DeployLinkPath` is the canonical selected link and must be loaded and validated again.
- `-OutputFormat JsonLines` enables the structured line protocol.
- A normal manual call may retain human-readable text output.
- Exit code `0` is reserved for a fully successful deployment after health verification.
- Every failure, cancellation, or incomplete deployment returns a non-zero exit code.

## JSON Lines protocol

When output format is `JsonLines`, each structured event is one complete JSON object on one stdout
line. Do not pretty-print an event across lines.

```json
{"type":"step","message":"Checking prerequisites"}
{"type":"success","message":"Prerequisites are available"}
{"type":"warning","message":"Seed data was skipped"}
{"type":"error","message":"Health check failed"}
{"type":"completed","message":"Deployment completed"}
```

Supported event types:

| Type | Meaning |
|---|---|
| `step` | A new operation or progress state. |
| `success` | A successful intermediate operation. |
| `warning` | A non-fatal condition requiring attention. |
| `error` | A failed operation. Must be followed by a non-zero process exit for critical failure. |
| `completed` | The complete deployment, including health verification, succeeded. |

Use `message` for the display text. The current parser also accepts `label` as a fallback. Ordinary
tool output is allowed in addition to structured events and remains visible unchanged.

DeployDesk determines final success from all three terminal conditions: process exit code `0`, no
observed structured `error` event, and at least one structured `completed` event. A runner must keep
its process exit and event stream consistent; a contradictory or incomplete result is rejected.

## Validation responsibilities

DeployDesk validation is not the runner's security boundary. The runner must revalidate at least:

- the canonical link and runner remain inside the repository root;
- schema version and required objects;
- host, user, ports, remote path, and health path;
- Git remote and branch;
- bounded health attempts and delay;
- supported options; and
- all values before use in a local or remote command context.

Do not concatenate configuration into shell code. Prefer fixed scripts and structured data
transfer. Where a command language is unavoidable, use strict allowlists and context-correct
quoting.

## Secret policy

Never store or emit:

- passwords, access tokens, or private SSH keys;
- `.env` contents;
- database or message-broker URLs containing credentials;
- cloud, registry, or package-feed credentials; or
- unredacted command output that contains those values.

The hostname and remote path are not cryptographic secrets. They become public metadata when a
link file is committed to a public repository. If publishing them is undesirable, use an
appropriate deployment hostname or SSH alias, understanding that an alias is obscurity rather
than a credential.

## Compatibility checklist

- [ ] The file parses as UTF-8 JSON and validates against `deploylink-v2.schema.json`.
- [ ] `schemaVersion` is `2`.
- [ ] The project ID is unique and stable.
- [ ] The target contains no credentials.
- [ ] The runner exists inside the repository.
- [ ] The runner accepts all reserved DeployDesk parameters.
- [ ] Static runner arguments do not duplicate reserved parameters.
- [ ] Every option has a matching PowerShell switch and safe default.
- [ ] JSONL mode writes one object per line and never prompts.
- [ ] The runner returns non-zero on every failure.
- [ ] `completed` and exit code `0` occur only after a successful health check.
- [ ] Normal output and exception messages do not expose secrets.
- [ ] The repository documents recovery and any one-time server preparation.
