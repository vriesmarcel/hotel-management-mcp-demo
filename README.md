> Part of Marcel de Vries's **Opening Up Your App to AI Agents: A Practical Guide to MCP with .NET**, Visual Studio Live! Orlando 2026.
> [All demos and prerequisites](https://github.com/vriesmarcel/hotel-management-mcp-demo/blob/main/README.md) · [This checkpoint's walkthrough](DEMO-1-QUICKSTART.md)

# Hotel MCP demo — no refactoring required

This is `demo-02-no-refactor`, the first MCP demonstration for
**Opening Up Your App to AI Agents: A Practical Guide to MCP with .NET**.

The original .NET Framework 4.8 WinForms application and its inline SQL stay
in place. `GetAvailableRooms` uses the existing `DataAccess` directly. The
official MCP C# SDK runs inside the same process through a Framework HTTP
adapter. There are no extracted service projects or companion processes.

Open `Project_HMS.sln`, select Debug, set `Project_HMS` as the startup project,
and press **F5**. On the presenter's machine the local database settings are
already configured. On another machine run `scripts\Configure-Database.ps1`
once; it writes a Git-ignored `ConnectionStrings.local.config`.

Endpoint: `http://127.0.0.1:5057/mcp/`. Public demo token: **123456**.
Codex uses the static `Authorization: Bearer 123456` header from
`config/codex-mcp.example.toml`. No MCP environment variables are required.
This token is a presentation shortcut; real authentication comes later.

See [DEMO.md](DEMO.md) for the breakpoint walkthrough and verification.

| Branch | Presentation checkpoint |
| --- | --- |
| `demo-01-baseline` | Original application without MCP |
| `demo-02-no-refactor` | Add one MCP tool directly to the existing application |
| `demo-02-get-available-rooms` | Preserved alternative with shared application services |

A modern ASP.NET Core MCP host and stronger authorization can be introduced
as later improvements. They are not prerequisites for this first demonstration.
