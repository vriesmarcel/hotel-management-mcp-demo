import { test, expect } from '@playwright/test';

// The PowerShell runner checks a UUID database marker before starting the host.
if (process.env.HMS_UI_TEST_OWNED !== '5059') throw new Error('Run scripts/Test-Demo04.ps1; never point browser booking tests at the rehearsal database.');
const base = 'http://127.0.0.1:5059';
const fakeRooms = { checkIn: '2026-11-18', checkOut: '2026-11-21', nights: 3, rooms: [
  { roomId: 3, category: 'Family', nightlyRate: 5000 }, { roomId: 8, category: 'Family', nightlyRate: 5000 },
  { roomId: 1, category: 'Single', nightlyRate: 2000 }, { roomId: 4, category: 'Single', nightlyRate: 2000 },
  { roomId: 7, category: 'Double King', nightlyRate: 3000 }, { roomId: 9, category: 'Family', nightlyRate: 5000 }
] };
const food = [{ fId: 0, fName: '', fPackageCost: 0 }, { fId: 14, fName: 'Breakfast', fPackageCost: 200 }];
const services = [{ sId: 0, sName: '', sPackageCost: 0 }, { sId: 21, sName: 'Spa', sPackageCost: 1000 }];
function decode(result) { return result.structuredContent ?? JSON.parse(result.content[0].text); }
async function rpc(request, method, params) {
  const response = await request.post(`${base}/mcp/reservations`, { headers: { Authorization: 'Bearer 123456', Accept: 'application/json, text/event-stream', 'MCP-Protocol-Version': '2025-11-25' }, data: { jsonrpc: '2.0', id: 1, method, params } });
  expect(response.ok()).toBeTruthy(); const text = await response.text();
  const result = text.startsWith('event:') || text.startsWith('data:') ? JSON.parse(text.split('\n').find(x => x.startsWith('data:')).slice(5)) : JSON.parse(text);
  expect(result.error).toBeUndefined(); return result.result;
}
const frame = page => page.frameLocator('#app');
async function ready(page) {
  await page.goto(`${base}/demo/reservations`);
  await expect(frame(page).locator('.room').first()).toBeVisible();
  await expect(frame(page).locator('#food option')).not.toHaveCount(0);
}
async function guest(page) {
  for (const [id, value] of Object.entries({ guestName: 'UI Demo Guest', address: 'Demo Street', phone: 'DEMO-PHONE', nationalId: 'DEMO-UI' })) await frame(page).locator(`#${id}`).fill(value);
}
async function mock(page, overrides = {}) {
  const calls = [];
  await page.route('**/mcp/reservations', async route => {
    const body = route.request().postDataJSON();
    if (body.method !== 'tools/call') return route.continue();
    const { name, arguments: args } = body.params; calls.push({ name, args });
    if (overrides[name]) return overrides[name](route, body, calls);
    let data;
    if (name === 'GetAvailableRooms') data = { ...fakeRooms, ...args };
    if (name === 'GetPackages') data = { food, services };
    if (name === 'CalculateQuote') data = { quoteId: 'mock-quote', stay: args, days: 3, total: 16000, remaining: 16000 - args.advance, expiresUtc: new Date(Date.now() + 900000).toISOString() };
    if (name === 'CreateReservation') data = { reservationId: 42, roomId: 3, checkIn: fakeRooms.checkIn, checkOut: fakeRooms.checkOut, total: 16000, paid: args.advance, remaining: 16000 - args.advance, status: 'Reserved' };
    await route.fulfill({ json: { jsonrpc: '2.0', id: body.id, result: { content: [{ type: 'text', text: JSON.stringify(data) }] } } });
  }); return calls;
}

test('real SDK resource, isolated catalogs and iframe booking reach SQL once', async ({ page, request }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  const catalog = await rpc(request, 'tools/list', {});
  const tool = catalog.tools.find(x => x.name === 'GetAvailableRooms');
  expect(tool._meta.ui.resourceUri).toBe('ui://hotel/reservations/v1');
  const resource = await rpc(request, 'resources/read', { uri: tool._meta.ui.resourceUri });
  expect(resource.contents[0].mimeType).toBe('text/html;profile=mcp-app');
  expect(resource.contents[0]._meta.ui.csp.connectDomains).toEqual([]);
  expect(resource.contents[0].text).not.toContain('Bearer 123456');
  const posts = [];
  page.on('request', r => { if (r.method() === 'POST') { const b = r.postDataJSON(); if (b.method === 'tools/call') posts.push(b.params); } });
  await ready(page);
  await frame(page).locator('.room').first().click(); await guest(page);
  const serviceId = await frame(page).locator('#service option').last().getAttribute('value');
  await frame(page).locator('#service').selectOption(serviceId);
  await frame(page).locator('#advance').fill('10');
  await frame(page).getByRole('button', { name: 'Review my stay' }).click();
  await expect(frame(page).locator('#review')).toBeVisible();
  await expect(frame(page).locator('#book')).toBeDisabled();
  expect(posts.filter(x => x.name === 'CreateReservation')).toHaveLength(0);
  await frame(page).locator('#confirm').check();
  await frame(page).locator('#book').dblclick();
  await expect(frame(page).getByText('Your room is reserved.')).toBeVisible();
  const bookings = posts.filter(x => x.name === 'CreateReservation'); expect(bookings).toHaveLength(1);
  expect(bookings[0].arguments.confirmed).toBe(true);
  expect(bookings[0].arguments.requestId).toMatch(/^[0-9a-f-]{36}$/);
  const receiptText = await frame(page).locator('#receiptDetails').textContent();
  const id = +receiptText.match(/Reservation (\d+)/)[1];
  const reservation = decode(await rpc(request, 'tools/call', { name: 'GetReservation', arguments: { reservationId: id } }));
  expect(reservation.paid).toBe(10); expect(reservation.remaining).toBe(reservation.total - 10);
  const retry = decode(await rpc(request, 'tools/call', bookings[0])); expect(retry.reservationId).toBe(id);
  const availability = decode(await rpc(request, 'tools/call', { name: 'GetAvailableRooms', arguments: { checkIn: '2026-11-18', checkOut: '2026-11-21' } }));
  expect(availability.rooms.some(x => x.roomId === reservation.roomId)).toBe(false);
  expect(errors).toEqual([]);
});

test('quote invalidation, explicit confirmation, layout and safe text rendering', async ({ page }, testInfo) => {
  const calls = await mock(page); await ready(page);
  await page.screenshot({ path: testInfo.outputPath('room-picker-desktop.png'), fullPage: true });
  await frame(page).locator('.room').first().click(); await guest(page);
  await frame(page).locator('#service').selectOption('21');
  await frame(page).locator('#quoteButton').click();
  await expect(frame(page).locator('#review')).toBeVisible();
  await frame(page).locator('#confirm').check();
  await frame(page).locator('#guestName').fill('A different guest'); await expect(frame(page).locator('#book')).toBeDisabled();
  await frame(page).locator('#service').selectOption('0'); await expect(frame(page).locator('#review')).toBeHidden();
  await frame(page).locator('#service').selectOption('21'); await frame(page).locator('#quoteButton').click();
  await expect(frame(page).locator('#review')).toBeVisible();
  await frame(page).locator('#review').screenshot({ path: testInfo.outputPath('quote-desktop.png') });
  await page.setViewportSize({ width: 390, height: 844 });
  await frame(page).locator('#review').screenshot({ path: testInfo.outputPath('quote-mobile.png') });
  const child = page.frames().find(x => x.parentFrame());
  expect(await child.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await frame(page).locator('#checkOut').fill('2026-11-22');
  await expect(frame(page).locator('#review')).toBeHidden();
  expect(calls.filter(x => x.name === 'CreateReservation')).toHaveLength(0);
});

test('lost response retries identical UUID and payload, then stops on uncertainty', async ({ page }) => {
  const calls = await mock(page, { CreateReservation: route => route.abort('connectionreset') });
  await ready(page); await frame(page).locator('.room').first().click(); await guest(page);
  await frame(page).locator('#quoteButton').click(); await frame(page).locator('#confirm').check();
  await frame(page).locator('#book').click();
  await expect(frame(page).locator('#status')).toContainText('result is uncertain');
  await expect(frame(page).locator('#guestName')).toBeDisabled();
  await frame(page).locator('#book').click();
  await expect(frame(page).locator('#status')).toContainText('still uncertain');
  await expect(frame(page).locator('#book')).toBeDisabled();
  const bookings = calls.filter(x => x.name === 'CreateReservation'); expect(bookings).toHaveLength(2); expect(bookings[0].args).toEqual(bookings[1].args);
});

test('expired quotes cannot book', async ({ page }) => {
  const calls = await mock(page, { CalculateQuote: (route, body) => route.fulfill({ json: { jsonrpc: '2.0', id: body.id, result: { content: [{ type: 'text', text: JSON.stringify({ quoteId: 'expired', stay: body.params.arguments, days: 3, total: 15000, remaining: 15000, expiresUtc: '2000-01-01T00:00:00Z' }) }] } } }) });
  await ready(page); await frame(page).locator('.room').first().click(); await guest(page);
  await frame(page).locator('#quoteButton').click(); await frame(page).locator('#confirm').check();
  await expect(frame(page).locator('#expiry')).toContainText('expired'); await expect(frame(page).locator('#book')).toBeDisabled();
  expect(calls.filter(x => x.name === 'CreateReservation')).toHaveLength(0);
});

test('hotel rejection invalidates the quote and allows reselection', async ({ page }) => {
  const calls = await mock(page, { CreateReservation: (route, body) => route.fulfill({ json: { jsonrpc: '2.0', id: body.id, result: { isError: true, content: [{ type: 'text', text: 'The room is no longer available.' }] } } }) });
  await ready(page); await frame(page).locator('.room').first().click(); await guest(page);
  await frame(page).locator('#quoteButton').click(); await frame(page).locator('#confirm').check();
  await frame(page).locator('#book').click();
  await expect(frame(page).locator('#status')).toContainText('no longer available');
  await expect(frame(page).locator('#review')).toBeHidden();
  await expect(frame(page).locator('#find')).toBeEnabled();
  expect(calls.filter(x => x.name === 'CreateReservation')).toHaveLength(1);
});

test('availability is escaped and empty/error results stay actionable', async ({ page }) => {
  let mode = 'unsafe';
  await mock(page, { GetAvailableRooms: (route, body) => route.fulfill({ json: { jsonrpc: '2.0', id: body.id, result: mode === 'error' ? { isError: true, content: [{ type: 'text', text: 'Invalid stay dates.' }] } : { structuredContent: { ...fakeRooms, rooms: mode === 'empty' ? [] : [{ roomId: 1, category: '<img src=x onerror=alert(1)>', nightlyRate: 1 }] }, content: [] } } }) });
  await ready(page); await expect(frame(page).locator('.room strong')).toHaveText('<img src=x onerror=alert(1)>');
  await expect(frame(page).locator('.room img')).toHaveCount(0);
  mode = 'empty'; await frame(page).locator('#find').click(); await expect(frame(page).locator('#empty')).toBeVisible();
  mode = 'error'; await frame(page).locator('#find').click(); await expect(frame(page).locator('#status')).toContainText('Invalid stay dates.');
  await expect(frame(page).locator('#find')).toBeEnabled();
});
