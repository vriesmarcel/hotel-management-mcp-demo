# Hotel management MCP demos — Visual Studio Live! Orlando 2026

These are the demos from Marcel de Vries's **Opening Up Your App to AI Agents: A Practical Guide to MCP with .NET**, presented at **Visual Studio Live! in Orlando, November 2026**.

The presentation starts with an existing .NET Framework 4.8 Windows Forms hotel application. It adds MCP without first rewriting the application, then extracts shared business services into a separate .NET 10 host. A booking skill guides the conversation, and the finale lets the guest choose and book a room inside the chat using MCP Apps, followed by an automatic confirmation in the conversation.

## Choose a demo

Each branch is a runnable checkpoint with its own setup instructions. `main` preserves the original application and serves as the guide to the demo series; the complete implementation is on `demo-04-mcp-apps-ui`.

The branch numbers include the original “no hotel connection” introduction. They therefore differ from the four working demos below.

| Working demo | Branch | What to show | Setup and walkthrough |
|---|---|---|---|
| 1. MCP inside WinForms | [`demo-02-no-refactor`](https://github.com/vriesmarcel/hotel-management-mcp-demo/tree/demo-02-no-refactor) | `GetAvailableRooms` runs inside the original desktop process, using the existing data-access code. | [Quick start](https://github.com/vriesmarcel/hotel-management-mcp-demo/blob/demo-02-no-refactor/DEMO-1-QUICKSTART.md), [README](https://github.com/vriesmarcel/hotel-management-mcp-demo/blob/demo-02-no-refactor/README.md), [walkthrough](https://github.com/vriesmarcel/hotel-management-mcp-demo/blob/demo-02-no-refactor/DEMO.md) |
| 2. Shared services and three endpoints | [`demo-03-dotnet10-contexts`](https://github.com/vriesmarcel/hotel-management-mcp-demo/tree/demo-03-dotnet10-contexts) | WinForms and an independent .NET 10 MCP host share business services. Reservations, Stays and Billing have separate tool catalogs. | [README](https://github.com/vriesmarcel/hotel-management-mcp-demo/blob/demo-03-dotnet10-contexts/README.md), [walkthrough](https://github.com/vriesmarcel/hotel-management-mcp-demo/blob/demo-03-dotnet10-contexts/DEMO.md) |
| 3. Booking skill and plugin | [`demo-03a-booking-skill`](https://github.com/vriesmarcel/hotel-management-mcp-demo/tree/demo-03a-booking-skill) | The skill asks the missing questions, interprets room facts and policies, quotes the stay and requests confirmation. | [README](https://github.com/vriesmarcel/hotel-management-mcp-demo/blob/demo-03a-booking-skill/README.md), [skill/plugin walkthrough](https://github.com/vriesmarcel/hotel-management-mcp-demo/blob/demo-03a-booking-skill/DEMO-03A.md), [Copilot Cowork packaging](https://github.com/vriesmarcel/hotel-management-mcp-demo/blob/demo-03a-booking-skill/COWORK.md) |
| 4. Inline MCP Apps finale | [`demo-04-mcp-apps-ui`](https://github.com/vriesmarcel/hotel-management-mcp-demo/tree/demo-04-mcp-apps-ui) | Choose a room and packages, review the quote and cancellation terms, confirm the booking, then receive confirmation in chat. | [README](https://github.com/vriesmarcel/hotel-management-mcp-demo/blob/demo-04-mcp-apps-ui/README.md), [finale walkthrough](https://github.com/vriesmarcel/hotel-management-mcp-demo/blob/demo-04-mcp-apps-ui/DEMO-04.md) |

The [`before-mcp`](https://github.com/vriesmarcel/hotel-management-mcp-demo/tree/before-mcp) tag is the original baseline. The historical [architecture decision](docs/adr/0001-shared-hotel-application-layer-and-mcp.md) explains the shared-layer design; the numbered branches contain its implementation. The branches, rather than the historical tags, are the navigation points for this demo series.

## Prerequisites

- Windows and Visual Studio 2026 with .NET Framework 4.8 desktop development and .NET 10 support.
- SQL Server with the original `Project_HMS` database and its room/package catalog. **The repository does not include the original database schema or a backup.** The SQL files in the later branches are additive migrations, not a complete bootstrap for an empty database.
- Python 3.11+ for the configuration and plugin helpers.
- An MCP client. The presentation used Codex desktop; the inline finale also requires MCP Apps support enabled in the client.
- Node.js 20+ only when rebuilding or testing the UI. Its built HTML is committed, so ordinary F5 execution does not require npm.

Local database settings are deliberately excluded from Git. Supply your own connection settings; do not commit real passwords or guest data. The fixed token `123456` is the public localhost **demo** credential, not a production secret or authentication design.

## Create a checkout for each stage

Run these commands from a parent directory of your choice:

```powershell
git clone https://github.com/vriesmarcel/hotel-management-mcp-demo.git
cd hotel-management-mcp-demo
git worktree add ../hotel-demo-1 origin/demo-02-no-refactor
git worktree add ../hotel-demo-2 origin/demo-03-dotnet10-contexts
git worktree add ../hotel-demo-3 origin/demo-03a-booking-skill
git worktree add ../hotel-demo-4 origin/demo-04-mcp-apps-ui
```

These commands create detached demonstration checkouts of the published branches. Add `-b <your-branch-name>` to a worktree command if you want to develop your own changes there.

### Configure the database once

In `hotel-demo-1`, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Configure-Database.ps1
```

The default is `localhost,1433` with SQL user `sa`; the script prompts for the password. Use its `-Server` and `-UserName` parameters, or supply `HMS_CONNECTION_STRING` securely, for another local setup. This writes the ignored `Project_HMS/ConnectionStrings.local.config`. It does not create the original database.

In `hotel-demo-2`, initialize a separate synthetic rehearsal database:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Initialize-Demo03.ps1 -SourceConfig ..\hotel-demo-1\Project_HMS\ConnectionStrings.local.config
```

The SQL account needs permission to create the rehearsal database. The script copies the original schema and room/package catalog, applies migrations, and creates the demo desktop account **`demo-manager` / `123456`**. It does not copy original guests or bookings. It writes connection files for both WinForms and the host.

Copy those two ignored files to demos 3 and 4 so the follow-up demos share the same rehearsal bookings. From `hotel-demo-2`:

```powershell
foreach ($demo in @('../hotel-demo-3', '../hotel-demo-4')) {
    Copy-Item ./Project_HMS/ConnectionStrings.local.config "$demo/Project_HMS/ConnectionStrings.local.config"
    Copy-Item ./Hotel.Mcp.Host/appsettings.Local.json "$demo/Hotel.Mcp.Host/appsettings.Local.json"
}
```

For an existing rehearsal database created before cancellation terms were added, run `scripts/Update-DemoDatabase.ps1` from a later demo. This migration is additive and repeatable. **Do not rerun initialization for an ordinary demo switch**: it creates a new rehearsal database and changes your local connections.

## Run the presentation flow

Only one hotel host can use port **5057** at a time. Stop the old host before starting the next checkpoint. Every MCP connection uses the HTTP header `Authorization: Bearer 123456`; no token environment variable is required.

### Demo 1 — the embedded tool

Open the demo 1 solution, select `Project_HMS` as startup project and press F5. The login window must remain open; MCP does not require a desktop login. Connect Reservations to:

```text
http://127.0.0.1:5057/mcp/
```

Keep the trailing slash. Disable Stays, Billing and the hotel plugin for this stage. Set a breakpoint in `Project_HMS/Mcp/AvailabilityTools.cs`, then ask:

> Use GetAvailableRooms for November 18–21, 2026. Show room IDs, categories and nightly rates. Do not book anything.

Those are the presentation's example dates; use a suitable future stay when replaying later. Room availability changes as bookings are made.

### Demo 2 — the separate host

Open the demo 2 solution and start both `Project_HMS` and `Hotel.Mcp.Host`, or debug the host alone. Set a breakpoint in `Hotel.Mcp.Host/ReservationTools.cs` and step into the shared application service.

| Context | URL | Tools |
|---|---|---|
| Reservations | `http://127.0.0.1:5057/mcp/reservations` | `GetAvailableRooms`, `GetPackages`, `CalculateQuote`, `CreateReservation`, `GetReservation` |
| Stays | `http://127.0.0.1:5057/mcp/stays` | `GetArrivals`, `CheckIn` |
| Billing | `http://127.0.0.1:5057/mcp/billing` | `GetBill`, `RecordPayment`, `CompleteCheckout` |

Find a room, request a quote without booking, then confirm the quote with synthetic guest details to create a reservation. Follow the walkthrough for the check-in and billing examples. Closing WinForms while leaving the host running demonstrates the independent lifecycle.

### Demo 3 — the skill and plugin

Keep the .NET host running. Follow [DEMO-03A.md](https://github.com/vriesmarcel/hotel-management-mcp-demo/blob/demo-03a-booking-skill/DEMO-03A.md) to install the standalone skill or stage the plugin for GUI installation. Try:

> $book-hotel-room Help me book a room for November 18–21, 2026.

The skill supplies the booking workflow; the plugin packages the skill and the Reservations connection. If you enable the plugin connection, disable the duplicate manual Reservations connection. A separately installed standalone skill remains installed when the plugin is disabled. Stop at the quote if you want to save the booking for the finale.

The [Cowork guide](https://github.com/vriesmarcel/hotel-management-mcp-demo/blob/demo-03a-booking-skill/COWORK.md) covers the skill package and connector builder. A working remote connector still needs a reachable HTTPS endpoint and OAuth configuration; the local Codex configuration is not a universal plugin package.

### Demo 4 — book inside the chat

Stop the previous host and start `Hotel.Mcp.Host` from demo 4. Select **Hotel MCP (.NET 10)** for the inline presentation; **Hotel MCP Apps (.NET 10)** opens the separate browser preview instead. The endpoint URLs remain unchanged.

Reconnect the client and request a fresh result:

> Use GetAvailableRooms for November 18–21, 2026, and show the interactive MCP Apps room picker inline. Don't book anything yet.

Select a room and packages, enter synthetic guest details, review the server-priced quote and cancellation terms, then explicitly confirm and book. The picker displays the receipt and sends it back to the conversation for automatic assistant confirmation. Open WinForms to show the saved reservation.

## Switching connections reliably

- Changing a connection's enabled state does not change its URL or clear tool filters.
- The embedded server has one tool. The later Reservations endpoint needs **all five** tools. A leftover `enabled_tools = ["GetAvailableRooms"]` lets the picker open but blocks `GetPackages`, quoting and booking. For UI-only switching, leave tool filters absent.
- Use either the manual Reservations registration or the plugin registration, avoiding duplicate connections.
- Start the intended host before reconnecting the client. Use a fresh chat after switching connections, and a fresh picker after rebuilding the UI host.
- The later branches' `python scripts/Configure-Codex.py --demo 3` selects the three .NET routes, clears stale filters and disables duplicate hotel plugin registrations. `--demo 2` restores the embedded-host registration and its one-tool filter. These numbers refer to the branch checkpoints, not the four working-demo labels above.
- Demo 1's `scripts/Setup-WinFormsDemo.ps1` builds and configures the embedded demo; its `Configure-WinFormsCodex.py --probe` checks discovery using the Codex-compatible protocol revision.
- Set `tool_timeout_sec = 300` when demonstrating breakpoints, and resume execution within that time.

For the Windows Store version of Codex used in the presentation, [restart-gpt.ps1](restart-gpt.ps1) closes the desktop app's process tree and launches it again. Run it from an **external PowerShell window** after active tasks finish:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\restart-gpt.ps1
```

Use `-WhatIf` for a dry run. Keep Visual Studio and the selected hotel host running separately.

## Room context and cancellation

The demos publish room information and cancellation policy as MCP resources, with equivalent facts in availability results for clients without resource access. Family capacity is five. Single/king beds are 200 cm long; queen beds are 190 cm. Individual room-to-bed configurations are unspecified.

The demo policy allows free cancellation until 48 elapsed hours before **15:00 Europe/Berlin** check-in, then charges one room night excluding packages. Quotes contain the exact deadline and fee, and bookings preserve the accepted terms. There is no cancellation-execution tool. Quotes expire after 15 minutes and do not hold inventory. Demo payment tools record SQL entries; no real money moves.

## Validation and scope

The branch walkthroughs list their verification commands. `Test-Mcp.ps1` checks the embedded server. `Test-Demo03.ps1` exercises shared business rules, SQL, desktop handlers and MCP route separation. `Test-Demo04.ps1` adds UI tests using an owned temporary database. Skill/plugin tests use mocked booking operations. The SQL integration tests require the original schema source and permission to create a disposable test database.

This is teaching code: loopback hosting, a shared demo token and database, and simplified hotel/payment behavior are deliberate. OAuth, delegated authorization, per-reservation access policies and production deployment were presentation topics, not guarantees implemented by the localhost demo.
