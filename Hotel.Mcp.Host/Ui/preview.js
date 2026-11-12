import { AppBridge, PostMessageTransport } from '@modelcontextprotocol/ext-apps/app-bridge';
const $ = id => document.getElementById(id);
let sequence = 0;
const allowed = new Set(['GetAvailableRooms', 'GetPackages', 'CalculateQuote', 'CreateReservation', 'GetReservation']);
async function rpc(method, params) {
  const id = ++sequence;
  const response = await fetch('/mcp/reservations', { method: 'POST', headers: {
    'Content-Type': 'application/json', Accept: 'application/json, text/event-stream',
    Authorization: 'Bearer 123456', 'MCP-Protocol-Version': '2025-11-25'
  }, body: JSON.stringify({ jsonrpc: '2.0', id, method, params }), signal: AbortSignal.timeout(300000) });
  if (!response.ok) throw new Error(`Hotel endpoint returned HTTP ${response.status}.`);
  const text = await response.text();
  const reply = response.headers.get('content-type')?.includes('text/event-stream')
    ? text.split('\n').filter(x => x.startsWith('data:')).map(x => JSON.parse(x.slice(5))).find(x => x.id === id)
    : JSON.parse(text);
  if (!reply || reply.error) throw new Error(reply?.error?.message || 'No response from the hotel.');
  return reply.result;
}
async function tool(params) {
  if (!allowed.has(params.name)) throw new Error('This app can only use the Reservations tools.');
  const entry = document.createElement('li'); entry.textContent = params.name; $('calls').append(entry);
  return rpc('tools/call', params);
}
async function main() {
  await rpc('initialize', { protocolVersion: '2025-11-25', capabilities: { extensions: { 'io.modelcontextprotocol/ui': { mimeTypes: ['text/html;profile=mcp-app'] } } }, clientInfo: { name: 'hotel-local-mcp-apps-host', version: '4.0.0' } });
  const catalog = await rpc('tools/list', {});
  const uri = catalog.tools.find(x => x.name === 'GetAvailableRooms')?._meta?.ui?.resourceUri;
  if (!uri) throw new Error('This host needs Demo 04 with MCP Apps UI metadata.');
  const resource = await rpc('resources/read', { uri });
  const content = resource.contents.find(x => x.uri === uri && x.mimeType === 'text/html;profile=mcp-app');
  if (!content?.text) throw new Error('The hotel UI resource is missing.');
  const frame = $('app');
  const bridge = new AppBridge(null, { name: 'Hotel local demo host', version: '4.0.0' }, { serverTools: {}, serverResources: {}, updateModelContext: { text: {} } }, { hostContext: { theme: 'light', displayMode: 'inline' } });
  bridge.oncalltool = params => tool(params);
  bridge.onreadresource = params => {
    if (!['hotel://room-types', 'hotel://policies/cancellation/v1'].includes(params.uri)) throw new Error('This app may only read its hotel reference resources.');
    return rpc('resources/read', params);
  };
  bridge.onupdatemodelcontext = async () => { $('connection').textContent = 'Reservation confirmed'; return {}; };
  bridge.onsizechange = ({ height }) => { if (Number.isFinite(height)) frame.style.height = `${Math.max(500, Math.min(height, 12000))}px`; };
  bridge.oninitialized = async () => {
    try {
      const args = { checkIn: '2026-11-18', checkOut: '2026-11-21' };
      await bridge.sendToolInput({ arguments: args });
      await bridge.sendToolResult(await tool({ name: 'GetAvailableRooms', arguments: args }));
      $('connection').textContent = 'Connected to Reservations';
    } catch (error) { $('error').textContent = error.message; }
  };
  // Install the listener before the iframe app sends its initialization message.
  await bridge.connect(new PostMessageTransport(frame.contentWindow, frame.contentWindow));
  frame.srcdoc = content.text;
}
main().catch(error => { $('connection').textContent = 'Connection failed'; $('error').textContent = error.message; });
