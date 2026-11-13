import { test, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';

// Entirely offline: all hotel responses are synthetic and no SQL host is contacted.
async function setup(page, mode = 'success') {
  const ui = await readFile(new URL('../dist/reservations.html', import.meta.url), 'utf8');
  let source = (await readFile(new URL('../preview.js', import.meta.url), 'utf8')).replaceAll('\r\n', '\n');
  if (mode === 'unsupported') {
    source = source.replace('message: { text: {} }, ', '');
    source = source.replace(/  bridge.onmessage = async[\s\S]*?\n  };\n/, '');
  }
  if (mode === 'rejected') source = source.replace("$('connection').textContent = 'Chat confirmation requested';\n    return {};", "$('connection').textContent = 'Chat confirmation rejected';\n    return { isError: true };");
  const bundle = await build({ stdin: { contents: source, resolveDir: fileURLToPath(new URL('..', import.meta.url)), loader: 'js' }, bundle: true, write: false, format: 'iife', target: 'es2022' });
  const template = await readFile(new URL('../preview.html', import.meta.url), 'utf8');
  const host = template.replace('/* APP_JS */', () => bundle.outputFiles[0].text.replaceAll('</script', '<\\/script'));
  const calls = [];
  const cancellation = { policyUri: 'hotel://policies/cancellation/v1', policyVersion: 'v1', hotelTimeZone: 'Europe/Berlin', checkInAt: '2026-11-18T15:00:00+01:00', freeCancellationUntil: '2026-11-16T15:00:00+01:00', lateCancellationFee: 3000 };
  await page.route('http://localhost:18888/**', async route => {
    if (route.request().method() === 'GET') return route.fulfill({ contentType: 'text/html', body: host });
    const { id, method, params } = route.request().postDataJSON();
    let result = {};
    if (method === 'tools/list') result = { tools: [{ name: 'GetAvailableRooms', _meta: { ui: { resourceUri: 'ui://hotel/reservations/v1' } } }] };
    if (method === 'resources/read') {
      const data = params.uri === 'hotel://room-types' ? { roomTypes: [], beds: [], note: 'Test facts' } : { description: 'Demo policy' };
      result = { contents: [{ uri: params.uri, mimeType: params.uri.startsWith('ui:') ? 'text/html;profile=mcp-app' : 'application/json', text: params.uri.startsWith('ui:') ? ui : JSON.stringify(data) }] };
    }
    if (method === 'tools/call') {
      calls.push(params);
      let data;
      if (params.name === 'GetPackages') data = { food: [], services: [] };
      else if (params.name === 'GetAvailableRooms') data = { ...params.arguments, nights: 3, rooms: [{ roomId: 2, category: 'Double', nightlyRate: 3000 }] };
      else if (params.name === 'CalculateQuote') data = { quoteId: 'test-quote', stay: params.arguments, days: 3, total: 9000, remaining: 9000, cancellation, expiresUtc: new Date(Date.now()+900000).toISOString() };
      else if (params.name === 'CreateReservation') {
        if (mode === 'uncertain-booking') return route.abort('connectionreset');
        if (mode === 'rejected-booking') return route.fulfill({ json: { jsonrpc: '2.0', id, result: { isError: true, content: [{ type: 'text', text: 'Room no longer available.' }] } } });
        data = { reservationId: 42, roomId: 2, checkIn: '2026-11-18', checkOut: '2026-11-21', status: 'Reserved', total: 9000, paid: 0, remaining: 9000, cancellation: { ...cancellation, unexpectedPrivateField: 'PRIVATE-POLICY-FIELD' }, guestName: 'PRIVATE-GUEST', address: 'PRIVATE-ADDRESS', phone: 'PRIVATE-PHONE', nationalId: 'PRIVATE-ID' };
      } else throw new Error(`Unexpected tool ${params.name}`);
      result = { structuredContent: data, content: [] };
    }
    return route.fulfill({ json: { jsonrpc: '2.0', id, result } });
  });
  await page.goto('http://localhost:18888/demo/reservations');
  const app = page.frameLocator('#app');
  await expect(app.locator('.room')).toHaveCount(1);
  await app.locator('.room').click();
  for (const id of ['guestName', 'address', 'phone', 'nationalId']) await app.locator(`#${id}`).fill('Synthetic guest');
  await expect(app.locator('#food option')).not.toHaveCount(0);
  await app.locator('#quoteButton').click();
  await expect(app.locator('#review')).toBeVisible();
  expect(calls.filter(x=>x.name==='CreateReservation')).toHaveLength(0);
  await expect(page.locator('#chatMessages li')).toHaveCount(0);
  await app.locator('#confirm').check();
  return { app, calls };
}

test('one confirmed booking asks chat to close the conversation with a private-data-free receipt', async ({ page }) => {
  const { app, calls } = await setup(page);
  await app.locator('#book').dblclick();
  await expect(app.locator('#status')).toHaveText('Reservation confirmed. Confirmation requested in chat.');
  await expect(page.locator('#chatMessages li')).toHaveCount(1);
  const message = await page.locator('#chatMessages li').textContent();
  expect(message).toContain('do not create another reservation');
  const summary = JSON.parse(message.slice(message.indexOf('\n') + 1));
  expect(summary).toMatchObject({ reservationId: 42, roomId: 2, total: 9000, paid: 0, remaining: 9000, cancellation: { policyVersion: 'v1', lateCancellationFee: 3000 } });
  expect(message).not.toContain('PRIVATE-');
  expect(message).not.toContain('Synthetic guest');
  expect(calls.filter(x=>x.name==='CreateReservation')).toHaveLength(1);
  await expect(app.locator('#receipt')).toBeVisible();
  await expect(app.locator('#book')).toBeDisabled();
});

for (const mode of ['rejected', 'unsupported']) test(`${mode} chat delivery preserves the completed booking`, async ({ page }) => {
  const { app, calls } = await setup(page, mode);
  await app.locator('#book').click();
  await expect(app.locator('#receipt')).toBeVisible();
  await expect(app.locator('#status')).toContainText('Reservation 42 is confirmed');
  await expect(app.locator('#status')).toContainText(mode === 'rejected' ? 'could not be delivered' : 'cannot request a chat reply automatically');
  await expect(app.locator('#book')).toBeDisabled();
  expect(calls.filter(x=>x.name==='CreateReservation')).toHaveLength(1);
  await expect(page.locator('#chatMessages li')).toHaveCount(mode === 'rejected' ? 1 : 0);
});

for (const mode of ['rejected-booking', 'uncertain-booking']) test(`${mode} never announces a successful reservation to chat`, async ({ page }) => {
  const { app, calls } = await setup(page, mode);
  await app.locator('#book').click();
  await expect(app.locator('#status')).toContainText(mode === 'rejected-booking' ? 'no longer available' : 'result is uncertain');
  await expect(page.locator('#chatMessages li')).toHaveCount(0);
  await expect(app.locator('#receipt')).toBeHidden();
  expect(calls.filter(x=>x.name==='CreateReservation')).toHaveLength(1);
});
