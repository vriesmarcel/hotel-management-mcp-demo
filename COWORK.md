# Hotel booking in Microsoft Copilot Cowork

The skill-only ZIP uses the Claude-compatible plugin layout that Cowork converts during upload. It adds the guided booking workflow. It does **not** register a hotel connector or grant access to the local database. Without connected Reservations tools, the skill explains the missing connection instead of fabricating inventory.

## Visible installation demo

1. Open Cowork, select **+ > Customize > Plugins > Upload plugin**.
2. Choose `hotel-booking-cowork-1.0.0.zip` and let Cowork convert the package.
3. Choose **Only you**, then **Apply**. Tenant policy can control upload availability.
4. Start a conversation: “Help me book a room for four people, November 18–21, 2026.”

The skill activates from the request; no Codex `$skill` syntax is required. GUI upload has not been tested in this tenant. The workflow retains room capacities, server-provided cancellation terms, exact-quote confirmation and idempotent retries. It has no local Python fallback. MCP Apps and automatic UI-to-chat messages remain host-dependent and are not promised for Cowork.

## Rebuild the skill-only ZIP

From the repository root with Python 3.11 or later (standard library only):

```powershell
python scripts/Package-CoworkPlugin.py C:/Temp/hotel-booking-cowork-1.0.0.zip
```

The script writes a new archive and refuses to overwrite an existing file. It includes only the manifest, skill and original geometric icons. It excludes SQL settings, guest data, local helpers and the demo bearer token.

## Add the connector later

Cowork needs a remotely reachable HTTPS Streamable HTTP endpoint. The current `127.0.0.1:5057` endpoint and static `Bearer 123456` header are not a Cowork connection. A tunnel supplies reachability/TLS, but does not implement OAuth. API-key authentication is not currently supported by Cowork. No host or authentication changes are made by this packager.

After configuring a remote OAuth endpoint, register its OAuth connection in the Microsoft plugin vault and use the returned reference ID. Also provide your actual published privacy and terms URLs, which the Microsoft 365 manifest requires. The reference ID is not a client secret and is not simply an Entra app/client ID.

```powershell
python scripts/Package-CoworkPlugin.py C:/Temp/hotel-booking-cowork-connected-1.1.0.zip `
  --version 1.1.0 `
  --mcp-url https://YOUR-HOTEL-HOST/mcp/reservations `
  --oauth-reference-id YOUR-PLUGIN-VAULT-REFERENCE `
  --privacy-url https://YOUR-WEBSITE/privacy `
  --terms-url https://YOUR-WEBSITE/terms
```

This mode creates a native Microsoft 365 v1.29 package with `agentSkills` and `agentConnectors`. For a server that implements OAuth dynamic client registration, replace `--oauth-reference-id ...` with `--oauth-dcr`. The latter deliberately omits `authorization` so Cowork performs DCR. Neither option enables OAuth on the hotel server. Re-uploading a different package format may create a separate plugin identity; remove/disable the skill-only import to avoid duplicate skills.

The builder checks configuration and archive contents; it cannot prove remote reachability, OAuth registration, tenant acceptance or live tool behavior. Validate the connected package with the Microsoft tooling and test read-only availability before attempting a confirmed booking. A live booking test consumes inventory.

## Sources

- [Cowork upload and conversion](https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugins)
- [Cowork package and connector requirements](https://learn.microsoft.com/en-us/copilot/cowork/cowork-plugin-development)
- [Microsoft 365 manifest v1.29 schema](https://developer.microsoft.com/json-schemas/teams/v1.29/MicrosoftTeams.schema.json)
- [Compatible plugin layout](https://code.claude.com/docs/en/plugins-reference)

Checked November 14, 2026. This package is an additional presentation artifact for demos 03a and 04; the Codex plugin and server behavior stay as they are.
