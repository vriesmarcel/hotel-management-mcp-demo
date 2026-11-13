# Demo 04 — interactive reservations with MCP Apps

Branch: `demo-04-mcp-apps-ui`, based on `demo-03a-booking-skill`.

Worktree: `C:\source\hotel-management-mcp-demo-worktrees\demo-04-mcp-apps-ui`.

## The finale in Visual Studio

1. Stop any earlier hotel host using port 5057. Open this worktree's `Project_HMS.sln` in Visual Studio 2026.
2. Set **Hotel.Mcp.Host** as the startup project. Choose the **Hotel MCP Apps (.NET 10)** launch profile.
3. Put breakpoints in `Hotel.Mcp.Host/ReservationTools.cs` at `GetAvailableRooms`, `CalculateQuote` and `CreateReservation`. Add one in `Hotel.Application/ReservationsService.cs` to show the shared application layer.
4. Press **F5**. The browser opens `http://127.0.0.1:5057/demo/reservations`. The initial availability request hits the first breakpoint. Continue to show the available rooms for November 18–21, 2026.
5. Select an available **Family** room. Choose **Spa**, leave food at **No package**, and advance at **0**. Package prices do not specify how many people are covered; do not claim the legacy Spa package is priced per person.
6. Enter fictional guest details, e.g. `Finale Guest`, `Demo Street`, `DEMO-PHONE`, `DEMO-FINALE`. Click **Review my stay**. Step through `CalculateQuote`: the application service prices the stay and saves a 15-minute quote. Nothing has been booked yet.
7. Show the quote. Tick **I confirm…**, then click **Confirm & book**. Step into `CreateReservation`, then continue. The hotel confirms the reservation and the UI displays its ID. This writes to your configured rehearsal database.
8. Show the booking in WinForms (start `Project_HMS` separately if needed), or ask Codex to call `GetReservation` with the displayed ID. Closing the browser does not stop the host.

The UI is bundled and committed. **F5 does not need Node.js, npm, environment variables or a new token.** Both ignored local configuration files have been copied from the previous demo so this checkout uses the same rehearsal database. Existing bookings remain in place, including room 16's earlier reservation. Repeated finales consume inventory: choose another room or another date window rather than resetting the database during the talk.

On a new machine, copy the two ignored local configuration files from your working Demo 03 checkout, or use `scripts/Initialize-Demo03.ps1` to create a fresh rehearsal database. That setup creates a new database; it does not preserve your previous rehearsal bookings.

## What to say on stage

“The skill guides the conversation. MCP Apps adds a view to a tool result. The model finds rooms; the guest makes precise choices in the interface. Both paths call the same application services.”

“Notice the breakpoint: clicking Book is another MCP tool call. There is no second reservation API and no booking logic in JavaScript.”

“Reservations owns this UI and these tools. Check-in and payments still belong to their own contexts.”

Expand **Behind the demo** below the frame to show the tool names called during the interaction. Guest details and credentials are not displayed in that list.

## Embedded clients and the local reference host

`GetAvailableRooms` advertises `_meta.ui.resourceUri: ui://hotel/reservations/v1`. An MCP Apps-capable client reads that resource and displays the app. Standard MCP clients continue receiving the room data as text and structured content. The existing booking skill/plugin can still use all five Reservations tools without a UI.

**Codex desktop inline rendering and read-only interaction were verified on this machine (26.1106.51428).** Enable the experimental `enable_mcp_apps` feature and keep the hotel host running when Codex connects. If the host was offline during discovery, use Settings → MCP servers → Restart. All three hotel servers should then appear natively in the chat.

For the chat finale, use the **Hotel MCP (.NET 10)** launch profile (no browser launch), press F5, and ask Codex: “Use GetAvailableRooms for November 18–21, 2026, and show the interactive room picker. Don't book anything yet.”

The availability count is the total across all categories. Each card selects a specific room; it is not a category filter. Selecting it opens the package and guest-details step. **Change room** returns to the results and invalidates any quote. The app requests a stable inline height and scrolls within the host's frame, avoiding repeated document/iframe resizing. The selection/layout regression is tested offline with the official AppBridge; it performs no SQL writes.

After UI edits, stop debugging, rebuild and press F5. Ask for a **new** GetAvailableRooms result; an already-open widget retains its old HTML. The 4.0.1 layout change still needs a fresh native-card check after restarting the host. Native inline booking has not yet been exercised; browser booking was verified separately against a disposable test database.

The browser page remains a **local reference host** built with the official AppBridge. It fetches the same resource and forwards its calls to the same Reservations endpoint. Use it as a fallback or expand **Behind the demo** there to inspect tool names.

## Code tour

| File | Explain |
|---|---|
| `Hotel.Mcp.Host/ReservationTools.cs` | `[McpAppUi]` on `GetAvailableRooms`; structured result; existing quote and booking tools. |
| `Hotel.Mcp.Host/ContextCatalogs.cs` | Apply the Apps attributes to each route's catalog; register the UI only in Reservations. |
| `Hotel.Mcp.Host/ReservationApp.cs` | Embedded HTML resource, MIME `text/html;profile=mcp-app`, CSP metadata. |
| `Hotel.Mcp.Host/Ui/reservations.js` | Official `App` client: tool result in, `callServerTool` out, confirmation and retry state. |
| `Hotel.Mcp.Host/Ui/preview.js` | The local reference host; official `AppBridge`, HTTP MCP forwarding and scoped tool allowlist. |
| `Hotel.Mcp.Host/Program.cs` | SDK transport plus one loopback-only preview page; MCP requests still require the demo token. |

```mermaid
sequenceDiagram
    participant H as MCP Apps host
    participant M as Reservations MCP endpoint
    participant U as Sandboxed hotel UI
    participant A as Application services + SQL
    H->>M: tools/call GetAvailableRooms
    M-->>H: Rooms + tool metadata points to UI
    H->>M: resources/read ui://hotel/reservations/v1
    M-->>H: HTML + MIME + CSP metadata
    H->>U: Initialize bridge; send tool input and result
    U->>H: callServerTool CalculateQuote
    H->>M: tools/call CalculateQuote
    M->>A: Price and persist quote
    M-->>H: Quote
    H-->>U: Show quote for confirmation
    Note over U: Guest explicitly confirms
    U->>H: callServerTool CreateReservation (UUID, confirmed)
    H->>M: tools/call CreateReservation
    M->>A: Validate quote + inventory; atomic booking
    M-->>H: Reservation summary
    H-->>U: Confirmation receipt
```

The UI has no database connection or bearer token and cannot make direct network requests. Its text content is escaped, and it uses no CDN assets. The local parent page contains the intentionally fixed demo token; loopback binding, Host/Origin checks and iframe isolation remain in place. This is demonstration authentication, not a production deployment.

Changing room, dates, packages or advance invalidates the quote. Changing guest details clears confirmation. Expired quotes cannot be submitted. A double click cannot issue two concurrent bookings. If a response is interrupted, the UI freezes the original payload and permits one retry with the same UUID. If that remains uncertain, stop and check the hotel's records; **do not refresh and start another booking**. Retry state is in memory and does not survive a reload. No guest identity is included in the optional model-context receipt, although the host necessarily receives the booking tool arguments.

The C# Apps extension currently carries experimental API diagnostic `MCPEXP003`; this project suppresses that specific diagnostic. Business validation and SQL transactions remain in the existing application layer.

## Build and verify

To change the UI, use Node.js 20+:

```powershell
cd Hotel.Mcp.Host/Ui
npm ci --ignore-scripts
npm run build
```

Commit the source, lockfile and regenerated `dist/*.html` together. HTML is embedded in the .NET assembly, so rebuild/restart the host after UI changes.

The full check requires installed Microsoft Edge, Visual Studio build tools, Node.js and the original schema source:

```powershell
.\scripts\Test-Demo04.ps1 -SourceConfig 'C:\source\hotel-management-mcp-demo-worktrees\demo-02-no-refactor\Project_HMS\ConnectionStrings.local.config'
```

The runner verifies a UUID ownership marker, creates a disposable test database, exercises the existing business/SQL/WinForms and HTTP tests, then runs Playwright against its own host on port 5059. The browser test creates a synthetic reservation and verifies it through MCP, including an identical retry. Separate mocked tool responses exercise expiry, rejection, interrupted responses, safe text and responsive layout. Only the owned test host/database are cleaned up. The original source database fingerprint is checked for changes. Screenshots and failure traces are under the ignored `Hotel.Mcp.Host/Ui/test-results` directory.

## References

- [Official C# SDK MCP Apps extension](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/apps/apps.md)
- [MCP Apps quickstart and TypeScript bridge](https://apps.extensions.modelcontextprotocol.io/api/documents/quickstart.html)
- [OpenAI embedded UI documentation](https://developers.openai.com/plugins/build/chatgpt-ui)


## Resource-aware room picker (4.1.0)

Run `scripts/Update-DemoDatabase.ps1` once for your existing synthetic rehearsal database, restart the host, and request a fresh GetAvailableRooms card. Old cards contain old bundled HTML.

The picker reads `hotel://room-types` and `hotel://policies/cancellation/v1` through `App.readServerResource` when the host advertises resource support. Otherwise it consumes identical reference facts returned by availability. Open “Room information and cancellation policy”, select Family (maximum five guests), and review the bed-length explanation. Specific room bed configurations remain unknown.

The quote shows the exact server-computed deadline and late fee. For November 18, 2026 check-in, cancellation is free through November 16, 15:00 Europe/Berlin (+01:00); later the fee is one room night excluding packages. The confirmation checkbox includes cancellation terms. Missing quote terms block booking. The receipt uses the reservation's stored snapshot. No cancellation execution or actual payment has been added.


## Complete the conversation after booking (picker 4.2.0)

After a successful Confirm & book click, the picker calls `App.sendMessage` (`ui/message`, role `user`) with the public receipt and asks the assistant to confirm the completed reservation in chat. `updateModelContext` alone only supplies context to a future turn, so it did not finish the conversation automatically. No guest name, address, phone or national ID is included in the chat handoff.

Only successful bookings send the follow-up. Quote-only, rejected and uncertain bookings do not. A chat delivery error cannot retry or undo the completed booking, and it does not trigger an automatic message retry. Hosts without `message.text` retain the receipt and show a manual chat-verification hint. The local reference host displays the outgoing message under Behind the demo / Chat handoff; it does not simulate an assistant answer.

Restart the host and request a fresh picker card to load this bundle. In Codex, rehearse the final user-clicked confirmation and check that the assistant replies with the saved reservation and cancellation terms. SDK reference: https://apps.extensions.modelcontextprotocol.io/api/classes/app.App.html#sendMessage
