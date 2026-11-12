# Demo 03a — from individual prompts to a booking skill

The branch `demo-03a-booking-skill` starts at the validated .NET 10 host demo. The MCP server and business logic are unchanged. This step adds the conversation workflow in a client skill.

The source is `plugins/hotel-booking/skills/book-hotel-room/SKILL.md`. It is also the skill bundled in the plugin, so there is one maintained definition.

## Rehearse the standalone skill

1. Keep the Demo 03 host running in Visual Studio on port 5057. You can use its existing checkout and database; there is no need to start a second host or reset any bookings.
2. From this branch, run `python scripts/Install-BookingSkill.py`. It installs the skill into your personal Codex skills directory. Python 3.11+ is required for the helper/setup scripts.
3. Open a new chat or restart Codex if needed to refresh skill discovery.
4. Enter: **`$book-hotel-room Help me book a room for November 18–21, 2026.`**
5. Choose a returned available room. The skill asks for missing packages, advance and guest details, calculates a quote, and asks for confirmation. Choose no packages/no advance and supply synthetic guest values for the demo.
6. Confirm the displayed quote. The skill creates the request UUID, calls the booking tool, and presents the reservation. It does not check in or call Billing.

Room 16 was already booked in the preceding rehearsal; do not assume it is still available. The skill always uses the tool's current result.

If you give all details at once, the skill should skip redundant questions and go directly to quoting. If you ask for a quote only, it stops there. A quote expiring or losing availability returns to the appropriate decision; it does not silently book a different room.

Breakpoints remain in `ReservationTools.CalculateQuote` and `ReservationTools.CreateReservation` in the running host. This makes a useful comparison: the tool contract and C# services did not change, but the client now knows the booking sequence.

## Package the skill and tools as one plugin

`plugins/hotel-booking` is a Codex compatibility plugin bundle containing:

```text
.codex-plugin/plugin.json           Identity, skill directory and MCP wiring
.mcp.json                          Reservations connection + demo token
skills/book-hotel-room/SKILL.md     Booking conversation
skills/book-hotel-room/agents/openai.yaml
skills/book-hotel-room/scripts/hotel_mcp.py
```

The bundle connects to the separately running local host. It does not contain the .NET application, SQL credentials or database. Its literal token `123456` is only the deliberately shared localhost demo credential.

Export it with `python scripts/Package-BookingPlugin.py <output.zip>`. The packager includes only the five explicit plugin files. The repository marketplace at `.agents/plugins/marketplace.json` exposes the plugin under **Hotel MCP Demos** for supported local clients.

To stage the plugin in your personal marketplace, run `python scripts/Install-BookingSkill.py --mode plugin`. This preserves unrelated marketplace entries and backs up existing files. Restart Codex, select that local marketplace in Plugins, and install **Hotel Booking Demo**. Registration is not installation; the script does not claim it is enabled. Do not publish it to a workspace or public directory for this demo.

Use one presentation mode at a time. The standalone skill uses the existing `hotel_reservations` registration. After installing and verifying the plugin, disable that duplicate manual Reservations registration and remove the standalone skill copy if you want a clean plugin-only demonstration. Leave Stays and Billing available for their later segments. These changes are not performed automatically by the setup helper.

The compatibility MCP file uses Codex's `http_headers` key and a 300-second timeout. It is intentionally a local Codex package, not a claim of portable cloud execution or public-directory readiness. Official references: [packaging and local marketplaces](https://developers.openai.com/plugins/build/plugins), [native MCP configuration parser](https://github.com/openai/codex/blob/main/codex-rs/codex-mcp/src/plugin_config.rs).

## Verify

Run `python -m unittest discover -s tests/booking-skill -v`. This checks the helper's no-unconfirmed-write behavior, quote-only behavior, exact request preservation, uncertainty handling, MCP decoding, package contents and isolated installation. Booking tests use mocked transport and create no reservations.

For a read-only check against the running server, run `python plugins/hotel-booking/skills/book-hotel-room/scripts/hotel_mcp.py catalog` and `... policy`. Native MCP tools are preferred; the bundled helper is a fallback for chats that have not exposed them yet.

Conversational rehearsal cases (these are manual acceptance cases, not automated model evaluations):

| User input / situation | Expected behavior |
|---|---|
| “Help me book a room” | Ask for dates, then find rooms; no guessed dates or booking |
| Dates supplied, several rooms returned | Ask for a room or apply an explicitly given selection rule |
| Room selected, packages/advance unspecified | Offer choices and ask; do not silently choose amounts |
| All details supplied, quote requested only | Calculate and display quote; no reservation |
| Current quote explicitly confirmed with complete guest details | Book without asking for confirmation again |
| Guest details supplied without confirmation | Ask to confirm the concrete quote before booking |
| A new room/date/package choice | Requote and confirm the updated details |
| Quote expired or price changed | Explain, requote, and obtain confirmation |
| Booking response lost | At most one identical retry with the same UUID; no new booking key |
| “Thanks” after success | Acknowledge; do not create another reservation |

Teaching point: **MCP exposes what the application can do. The skill teaches the client how to complete the booking conversation. The plugin distributes them together.** Confirmation in a skill improves the interaction, but is not a substitute for authorization and transaction rules in the application.
