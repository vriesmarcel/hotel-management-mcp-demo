# First MCP demo: WinForms hosted endpoint

This is the first successful MCP call in the presentation. Its existing Git branch is `demo-02-no-refactor`. The earlier baseline only demonstrates the missing connection. Keep these names distinct when selecting a worktree.

## Repeatable setup on the presenter's machine

Close any running Hotel.Mcp.Host or Project_HMS demo. Then run in PowerShell:

```powershell
Set-Location C:\source\hotel-management-mcp-demo-worktrees\demo-02-no-refactor
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Setup-WinFormsDemo.ps1
```

This builds Debug and configures the current Codex user's `config.toml` (or the `CODEX_HOME` override). It enables `hotel_reservations` at **http://127.0.0.1:5057/mcp/** with the public demo header `Authorization: Bearer 123456`, exposes only GetAvailableRooms, and sets a five-minute tool timeout. It removes stale bearer-environment-variable settings for that entry and disables separately configured hotel Stays/Billing servers and hotel-booking plugin registrations. Other settings remain intact. A timestamped backup is written only when a change is needed. Repeating setup does not duplicate entries or backups.

Python 3.11+ and Visual Studio with .NET Framework 4.8 build tools are required. On this machine the ignored database config already exists. On another machine run `scripts/Configure-Database.ps1` first. No MCP token environment variables are required.

## Start in Visual Studio and verify

1. Open `Project_HMS.sln` from this exact worktree. Set **Project_HMS** as the startup project and select **Debug**.
2. Set a breakpoint at `var arrival = ParseDate(checkIn);` in `Project_HMS/Mcp/AvailabilityTools.cs`.
3. Press **F5**. Leave the login window open. Its title shows `/mcp/` and the process ID. No desktop login is needed for MCP calls.
4. Run the discovery check while the debugger is running, not paused:

```powershell
python .\scripts\Configure-WinFormsCodex.py --probe
```

Expected: **PASS: hotel-reservations-net48 accepted Codex protocol 2025-06-18 and exposes only GetAvailableRooms.** This verifies the host identity and exact catalog without querying SQL or hitting the tool breakpoint.

5. Restart MCP servers in Codex **after** the WinForms app is listening, or fully quit and relaunch Codex. Start a fresh chat and inspect `/mcp`. The hotel server should be connected and GetAvailableRooms available. If a trusted project's `.codex/config.toml` overrides the user settings, remove the conflicting hotel override; this script edits only the user configuration.
6. Send:

> Use GetAvailableRooms for November 18–21, 2026. Show room IDs, categories and nightly rates. Do not book anything.

7. Visual Studio pauses on the breakpoint. Inspect `checkIn` and `checkOut`, then press **F5** to complete the call. Stay within five minutes. Codex should show the SQL-backed result. Inventory can change between rehearsals.

## Independent checks

```powershell
# Check Codex config without changing it
python .\scripts\Configure-WinFormsCodex.py --check

# Call the tool outside Codex; DOES hit the same breakpoint and reads SQL
python .\scripts\Configure-WinFormsCodex.py --probe --availability

# Offline setup-script checks, no host or database needed
python .\scripts\Test-WinFormsCodex.py
```

An enabled entry in `codex mcp list` only confirms configuration, not a successful handshake. If the probe succeeds but Codex has no tools, inspect its startup error and restart the connection after the host is listening; also check project/plugin overrides. If the probe cannot connect, run it in your own PowerShell with the app started and resumed (a sandbox may not be allowed to reach desktop loopback). HTTP 401 indicates authentication trouble; HTTP 404 or an unexpected server identity suggests the wrong endpoint/host. `/mcp/reservations` belongs to the later .NET 10 demo.

The setup does not stop processes, change firewall/URL permissions, alter database configuration or restart Codex. If startup reports URL access denied, use the URL ACL guidance in `DEMO.md`; a local URL reservation is different from a firewall rule. A browser GET is not an MCP connectivity test.

The probe uses 2025-06-18, the revision requested by the installed Codex desktop client, and initializes without a version header just like Codex. The adapter also supports 2025-11-25 and 2026-07-28. An older binary rejects the Codex revision with `Unsupported protocol version`; stop debugging, rebuild this branch and press F5 before reconnecting Codex. Closing the login window stops its embedded endpoint. When returning to later demos, restore their endpoint, full tool catalog and plugin settings deliberately; the backup contains the prior user configuration and may also contain credentials, so do not publish it.
