# Demo 03 presenter walkthrough

## Transition from the first live demo

Presentation demo 2 is repository checkpoint `demo-03-dotnet10-contexts`; the switch command therefore uses `--demo 3`.

1. Stop the first demo in Visual Studio (Shift+F5) to release port 5057.
2. In PowerShell, run:

   ```powershell
   Set-Location C:\source\hotel-management-mcp-demo-worktrees\demo-03-dotnet10-contexts
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Update-DemoDatabase.ps1
   python .\scripts\Configure-Codex.py --demo 3
   ```

   On this presenter machine both ignored database configuration files already exist. The update is additive and idempotent. Do not run `Initialize-Demo03.ps1` for a routine transition: that creates another fresh rehearsal database.
3. Open this checkout's `Project_HMS.sln`. Select Debug and configure multiple startup projects: `Project_HMS` and `Hotel.Mcp.Host`, both Start. Set a breakpoint in `Hotel.Mcp.Host/ReservationTools.cs`, at the start of `GetAvailableRooms`. Press F5 and wait for the host to listen on port 5057.
4. Fully quit and reopen Codex after the host starts; use a fresh chat. Inspect `/mcp` for the three hotel servers. Enabled configuration alone does not prove discovery; the catalogs should expose five Reservations tools, two Stay Operations tools and three Billing tools.
5. Ask: "Use GetAvailableRooms for November 18–21, 2026. Show room IDs, categories and nightly rates. Do not book anything." Inspect the arguments at the breakpoint and press F5 to return the result.

`Configure-Codex.py` removes the first demo's tool allowlist and stale authentication settings, supports quoted and nested TOML tables, disables later hotel plugins, and backs up changed configuration. Unrelated settings are preserved. It honors `CODEX_HOME`; `--config` can target another configuration explicitly. Regression checks: `python -B scripts/Test-ContextCodex.py`.

To demonstrate process independence, close only the WinForms window, leaving the host running, and repeat the availability request. If your Visual Studio launch profile ends both processes together, debug the host alone and launch WinForms separately. Desktop demo login: `demo-manager` / `123456`.

## Prepare

1. Keep the `demo-02-no-refactor` checkout available for the first demo.
2. Open this branch's `Project_HMS.sln` in Visual Studio 2026. Its working folder is `C:\source\hotel-management-mcp-demo-worktrees\demo-03-dotnet10-contexts`.
3. Run `scripts/Initialize-Demo03.ps1` once as described in README, unless the two local configuration files already exist.
4. Set `Project_HMS` and `Hotel.Mcp.Host` as multiple startup projects. Stop Demo 02 before F5.
5. Configure the three MCP registrations from `config/codex-mcp.example.toml`. Demo authentication is `Bearer 123456` on all routes; no token environment variable is needed.
6. Break in `ReservationTools.GetAvailableRooms`, `ReservationsService.CreateReservation`, `StaysService.CheckIn`, or `BillingService.RecordPayment` depending on the segment. Tool timeout is 300 seconds.

## Tell the refactoring story

Start with the working Demo 02 code: it already speaks MCP. Explain that extraction is now a choice to support a second frontend and independent process lifetime.

Show the solution projects. WinForms targets .NET Framework 4.8; Contracts and Application target .NET Standard 2.0; SQL targets net48 and net10.0; MCP Host targets .NET 10. Neither frontend contains business SQL. `Program.cs` composes the desktop services. The host uses `AddMcpServer().WithHttpTransport(...)` and three `MapMcp` routes.

Show `ContextCatalogs.cs`: each route receives only its own tools, resources and prompts. These are logical bounded contexts sharing one host/database for the demo, not independently deployed services or authorization scopes.

## Availability and reservation

In Codex:

> Use GetAvailableRooms to check rooms available from November 18 to November 21, 2026. Show room IDs, categories and nightly rates.

At the breakpoint, inspect `checkIn`/`checkOut`, then step into ReservationsService and the SQL adapter. Continue to return results.

> Read the reservation policy and list packages. Calculate a quote for room [ID] for November 18–21, with no food or service package and no advance. Do not book yet.

Show the quote ID, authoritative total and 15-minute expiry. Demonstrate how `hotel://reservation-policy` describes data/rules and `PlanReservation` provides a reusable planning prompt. A client's resource/prompt UI may differ; the HTTP tests exercise both directly.

> I confirm that quoted stay and total. Book it for Demo Guest, address Demo Street, phone DEMO-PHONE, national ID DEMO-GUEST, with no advance. Use a new request UUID and confirmed=true.

Show the reservation ID and `Reserved` status. Open WinForms using `demo-manager` / `123456` and inspect Customer Details or Bill History. The room remains physically unoccupied until check-in.

## Stay Operations

The November 18 reservation cannot be checked in before November 18. For this segment, create a separate reservation **starting today and ending tomorrow**, using an available room and the same synthetic guest process.

> Use the Stay Operations endpoint to list today's arrivals. Check in reservation [ID] after my confirmation.

Confirm explicitly when prompted. Show `CheckIn` is in Stay Operations, and does not appear in Reservations. In WinForms, the dashboard's **Arrivals / Check-in** button uses the same service. Refresh room management to show occupied status.

## Billing

> Use Billing to show the bill for reservation [ID]. Do not record a payment yet.

> I confirm a demo payment of [AMOUNT LESS THAN BALANCE]. Record it using a new request UUID. No real money should move.

Show the SQL operation receipt and reduced balance. Retry with the same UUID and identical details to show that the balance does not change again.

> I confirm checkout for reservation [ID], paying its remaining balance now. Use a new request UUID.

Show `CheckedOut`, zero balance and the room becoming free. Explain that future payment-provider integration belongs inside Billing, behind the same application boundary.

## Process independence

Close WinForms while leaving the host running and ask for availability again. If Visual Studio's Stop button terminates both startup projects, instead run the host alone under F5 and start WinForms separately. Closing WinForms then affects only its own process.

## Reset for rehearsal

Stop both processes, rerun `Initialize-Demo03.ps1` with the original Demo 02 local config, rebuild, and F5. It provisions a fresh demo database and changes only the ignored connection files. Old rehearsal databases remain available for manual inspection/removal.

The next presentation step can add MCP Apps UI to the Reservations endpoint. This branch prepares the quote/confirm/book workflow but does not implement that UI yet.

To switch Codex registrations automatically (Python 3.11+): `python scripts/Configure-Codex.py --demo 3`. Use `--demo 2` to restore the original Reservations URL and disable the other two registrations. The helper backs up the config and validates that unrelated settings are unchanged. Static headers and timeouts follow the [official Codex MCP configuration](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).


## Shared room resources and cancellation terms

Reservations exposes `hotel://room-types`, the template `hotel://room-types/{category}`, and `hotel://policies/cancellation/v1`. Family capacity is five; single/king bed lengths are 200 cm and queen 190 cm. Other capacities and individual room bed configurations are unspecified. Availability includes the same reference data for clients without resource reads. Resources are application-controlled context, not automatically read by the model.

Quotes now return `cancellation`: policy URI/version, 15:00 Europe/Berlin check-in, exact free-cancellation deadline and one-night room fee excluding packages. Free cancellation includes the deadline exactly; 48 hours are elapsed hours, accounting for DST. Example arrival 2026-11-18: free through 2026-11-16 15:00 +01:00. Booking confirmation includes these terms, and SQL preserves the accepted snapshot. Existing bookings have no invented/backfilled policy. No cancellation tool or money transfer is implemented.

Before starting this updated host against an existing synthetic rehearsal database, run `powershell -File scripts/Update-DemoDatabase.ps1`. This only adds the terms table after checking the demo database marker. Fresh initialization and isolated tests include migration 003 automatically. Do not rerun Initialize-Demo03 just to upgrade an existing demo database. The same migration is idempotent across Demo 03, 03a and 04.
