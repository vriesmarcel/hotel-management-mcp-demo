# Demo 01 — the application an agent cannot reach

Branch: `demo-01-baseline`.

Run `scripts\Start-Demo.ps1`. It builds the original .NET Framework 4.8
Windows Forms application and asks for the local SQL password without echoing
or saving it. If `HMS_CONNECTION_STRING` is already set, it uses that instead.

Show the hotel application, its direct `DataAccess` dependency and inline SQL.
In Codex, with the hotel MCP server disconnected, ask:

> Which rooms at our hotel are available from November 18 to November 21, 2026?
> Use actual hotel inventory. If no connected tool can answer, say so.

The client has no hotel tool on this branch. Do not give it shell access to the
repository/database for this presentation step: that would demonstrate a
different access path. Use a separate presentation chat without repository
access, and inspect the tool trace rather than relying on a scripted refusal.

Close the hotel window, open the `demo-02-get-available-rooms` checkout, and run
its `scripts\Start-Demo.ps1` from the same PowerShell window.

The application architecture is unchanged from `before-mcp`. The only code
change here replaces the checked-in password placeholder with environment
configuration so credentials do not need to be edited into source control.
