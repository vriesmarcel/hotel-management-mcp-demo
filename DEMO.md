# Demo 02 — add MCP without refactoring

Branch: `demo-02-no-refactor`.
Local solution: `C:\source\hotel-management-mcp-demo-worktrees\demo-02-no-refactor\Project_HMS.sln`.

## Before presenting

1. Close any other hotel demo so port 5057 is free.
2. Open this solution in Visual Studio normally. Set `Project_HMS` as the
   startup project and choose Debug.
3. On this machine the ignored database configuration is already prepared.
   On another machine run `scripts\Configure-Database.ps1` once. It asks for
   the SQL password and saves only `Project_HMS\ConnectionStrings.local.config`.
   Rebuild after creating/changing it. The file is copied beside the executable.
4. Set a breakpoint at `var arrival = ParseDate(checkIn);` inside
   `Project_HMS\Mcp\AvailabilityTools.cs`, then press F5.
5. The login window title shows the endpoint and PID. No WinForms login is
   required for the availability demonstration.

The MCP host always starts on this branch. It does not read `HMS_MCP_ENABLED`
or `HMS_MCP_TOKEN`. SQL can still use `HMS_CONNECTION_STRING` as an optional
override, but normally reads the local config file. No special launch shell
is required for Visual Studio or Codex.

## Codex configuration

The presenter's Codex configuration has already been updated. For another
installation, merge the following section into the Codex config, preserving
other settings. Remove `bearer_token_env_var` from this server if present:

```toml
[mcp_servers.hotel_reservations]
url = "http://127.0.0.1:5057/mcp/"
http_headers = { Authorization = "Bearer 123456" }
enabled_tools = ["GetAvailableRooms"]
startup_timeout_sec = 20
tool_timeout_sec = 300
```

Restart the hotel MCP connection after changing configuration. If the current
chat retains an older tool catalog, begin a new chat after the connection is
ready. A full desktop relaunch no longer needs to inherit any token variable.
Static HTTP headers are supported by the [official Codex MCP configuration](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).

## On stage

1. Start with `demo-01-baseline` and the hotel MCP connection disabled. Show
   the application's direct `DataAccess` usage and inline SQL. In a separate
   presentation chat without repository/SQL access, ask for real hotel availability.
2. Close the baseline. Open this solution and show the small addition in
   `Program.cs`: construct one tool, start the embedded host, run the existing
   login form. The existing forms and database access architecture are unchanged.
3. Press F5 and enable/restart the hotel MCP connection in Codex.
4. Ask: **Use GetAvailableRooms for November 18–21, 2026. Show room IDs,
   categories and nightly rates. Do not book anything.**
5. At the breakpoint inspect `checkIn` and `checkOut`. Point to
   `[McpServerTool]` and the parameter descriptions, then show
   `new DataAccess()` and the parameterized SQL query.
6. Say: **“We added an entry point. We did not need to refactor the application
   or upgrade .NET Framework to make it speak MCP.”**
7. Press F5 and return to Codex to show the live results.

Both Codex's tool timeout and the server timeout while a debugger is attached
are five minutes. The normal server deadline is 30 seconds. Explain the code
at the first breakpoint, before SQL executes. Pausing stops the application
process; the UI will pause too while the debugger is stopped.

## What changed from the baseline

- Added `ModelContextProtocol.Core` 2.4.0, bindings, and two files under `Mcp`.
- `Program.cs` starts/stops the MCP host inside the existing WinForms process.
- `AvailabilityTools.GetAvailableRooms` directly uses the original `DataAccess`.
- `DataAccess` gains a local configuration fallback for convenient F5 startup.
- No existing form, business workflow, database schema, or service layer was
  rewritten. The solution still contains only the original application project.

The HTTP adapter is prebuilt infrastructure for the presentation. The official
SDK handles tool discovery, schemas, dispatch and results. Moving to modern
ASP.NET Core later can replace that adapter with the ASP.NET Core MCP package;
extracting services can then be presented as a design improvement.

## Deliberate demo choices

The hard-coded token `123456` is public demonstration data, not real security.
The listener stays at `127.0.0.1` and exposes only read-only availability.
Real identity, authorization and remote hosting belong in the later section.

Dates are hotel-local, arrival-inclusive/departure-exclusive, for 1–365 nights.
Bookings overlap when `CheckIn < departure AND CheckOut > arrival`. Adjacent
stays are permitted. Invalid booking intervals or inconsistent active occupancy
conservatively exclude rooms. The schema records no cancellation status or
currency, and the tool returns no guest details. Availability is not a hold.

## Verification

Run `scripts\Test-Mcp.ps1`. It builds an independent smoke-test executable,
uses the official client against both supported protocol versions, checks date
validation and token rejection, and performs read-only queries against the
configured database. It then starts the real app with all HMS environment
variables cleared, calls the endpoint, closes the login normally, and checks
that the listener stops. It does not modify hotel records.

Verified on 2026-11-12: 20 smoke assertions and real application startup,
availability and shutdown passed. The pre-existing unused `width` compiler
warning in `BillHistory.cs` remains in this deliberately unchanged baseline.

If startup reports URL access denied, a Windows administrator can reserve
`http://127.0.0.1:5057/mcp/` for the presenting account with `netsh http add urlacl`.
No firewall rule is needed for the ordinary local demo. A browser GET is not
an MCP call and will return an authentication/method error.
