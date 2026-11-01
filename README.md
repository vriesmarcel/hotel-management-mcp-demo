# Hotel management MCP demo

This private repository demonstrates adding Model Context Protocol (MCP)
capabilities to an existing C# Windows Forms hotel management application.

## Architecture

See the [Architecture Decision Records](docs/adr/README.md), starting with
[ADR 0001: Shared hotel application layer for Windows Forms and MCP](docs/adr/0001-shared-hotel-application-layer-and-mcp.md).
The first middle-layer extraction is now implemented in `Hotel.Contracts`,
`Hotel.Application`, and `Hotel.Sql`, with all desktop forms using typed
services. See [refactoring, configuration, schema and verification details](docs/middle-layer-refactoring.md).
MCP, OAuth, and future date-range booking capabilities remain unimplemented.

## Presentation versions

| Tag | Meaning |
| --- | --- |
| `before-mcp` | Original application before MCP implementation, with the database password replaced by a placeholder. |
| `adr-before-refactoring` | Accepted ADR documentation-only milestone, before application refactoring. |
| `after-refactoring` | Reserved for the verified middle-layer refactoring. |
| `after-mcp` | Reserved for the completed, verified MCP-enabled version. This tag does not exist yet. |

The `before-mcp` tag is the permanent baseline. The `after-mcp` tag will only be
created after implementation and verification, not as another name for the
baseline.

To view the baseline in a separate checkout:

```powershell
git clone --branch before-mcp https://github.com/vriesmarcel/hotel-management-mcp-demo.git hotel-before
```

Once the completed version is published:

```powershell
git clone --branch after-mcp https://github.com/vriesmarcel/hotel-management-mcp-demo.git hotel-after
git diff before-mcp after-mcp
```

## Running the refactored desktop application

Use the .NET Framework 4.8 Windows desktop development workload.
Supply `HMS_CONNECTION_STRING` securely outside source control and explicitly
apply `database\001-id-sequences.sql` to the intended database before creating
records. The migration has only been exercised in isolated validation databases,
not applied to the shared live database. See the
[configuration and migration instructions](docs/middle-layer-refactoring.md#local-configuration-and-migration).

Run `scripts\Test-Refactoring.ps1` for boundary, application, isolated SQL,
and automated desktop workflow validation. It never writes hotel data to the
shared database.

## Baseline prerequisites (`before-mcp` only)

Open `Project_HMS.sln` in Visual Studio with .NET Framework 4.8 development
support and the Windows desktop development workload.

The original application expects a local SQL Server database named
`Project_HMS`. The supplied source folder does not include a database schema,
seed data, or database backup; those must be supplied separately before the
baseline can run against a database.

The password in `Project_HMS\DataAccess.cs` is
`REPLACE_WITH_LOCAL_PASSWORD`. Supply a local development connection before
running the baseline, and never commit a real password.

## Import boundaries

Application source, project files, resources, and the solution are preserved.
The only application-source change in the baseline is password redaction.
This README and `.gitignore` are added for the demonstration.

IDE caches, build outputs, and the original `SnapShots` folder are excluded.
The original working copy is not modified.

## Intended MCP capabilities

The implementation will provide date-range availability retrieval and room
booking. Retrieval requires read authorization; booking requires additional
server-enforced booking rights.

Authentication must keep passwords out of model-visible messages and tool
arguments. An MCP App can provide an interactive sign-in entry point where
supported; the precise authentication flow and host-specific behavior for
Claude, OpenAI clients, and Copilot still need to be established.
