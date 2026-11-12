# Demo 03 presenter walkthrough

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
