import { App } from '@modelcontextprotocol/ext-apps';

const app = new App({ name: 'Hotel Reservations', version: '4.0.1' }, {}, { autoResize: false });
const $ = id => document.getElementById(id);
const money = value => Number(value).toLocaleString('en-US', { maximumFractionDigits: 2 });
let rooms = [], selected, quote, busy = false, done = false, pending, attempts = 0, packagesReady = false;
let searchDates;
function status(message, error = false) { $('status').textContent = message; $('status').className = error ? 'error' : ''; }
function unpack(result) {
  if (result.isError) throw new ToolError(result.content?.filter(x => x.type === 'text').map(x => x.text).join('\n') || 'The hotel could not complete this request.');
  if (result.structuredContent) return result.structuredContent;
  const text = result.content?.find(x => x.type === 'text')?.text;
  if (!text) throw new Error('The hotel returned no usable result.');
  return JSON.parse(text);
}
class ToolError extends Error {}
async function call(name, args) { return unpack(await app.callServerTool({ name, arguments: args }, { timeout: 300000 })); }
function lock() {
  $('searchFields').disabled = busy || done || !!pending;
  $('detailFields').disabled = busy || done || !!pending;
  document.querySelectorAll('.room').forEach(x => { x.disabled = busy || done || !!pending; });
  $('changeRoom').disabled = busy || done || !!pending;
  $('confirm').disabled = busy || done || !!pending;
  $('book').disabled = busy || done || (pending ? attempts >= 2 : !quote || !$('confirm').checked || expired());
}
function expired() { return !quote || Date.now() >= Date.parse(quote.expiresUtc); }
function invalidate() { quote = undefined; $('review').hidden = true; $('confirm').checked = false; lock(); }
function sameDates(a, b) { return a?.checkIn === b?.checkIn && a?.checkOut === b?.checkOut; }
const dates = () => ({ checkIn: $('checkIn').value, checkOut: $('checkOut').value });
function renderAvailability(result) {
  if (busy || pending || done) return;
  invalidate(); selected = undefined; $('details').hidden = true;
  searchDates = { checkIn: result.checkIn.slice(0, 10), checkOut: result.checkOut.slice(0, 10) };
  $('checkIn').value = searchDates.checkIn; $('checkOut').value = searchDates.checkOut;
  rooms = result.rooms; $('rooms').replaceChildren(); $('availability').hidden = false;
  $('count').textContent = `${rooms.length} rooms across all categories · ${result.nights} night${result.nights === 1 ? '' : 's'}`;
  $('empty').hidden = rooms.length !== 0;
  for (const room of rooms) {
    const button = document.createElement('button'); button.type = 'button'; button.className = 'room';
    button.dataset.roomId = room.roomId; button.setAttribute('aria-pressed', 'false');
    const id = document.createElement('small'); id.textContent = `Room ${room.roomId}`;
    const category = document.createElement('strong'); category.textContent = room.category;
    const rate = document.createElement('span'); rate.className = 'rate'; rate.textContent = money(room.nightlyRate);
    const per = document.createElement('small'); per.textContent = '/ night'; rate.append(per); button.append(id, category, rate);
    button.addEventListener('click', () => {
      if (busy || pending || done) return;
      selected = room; invalidate();
      document.querySelectorAll('.room').forEach(x => x.setAttribute('aria-pressed', String(x === button)));
      $('selection').textContent = `Room ${room.roomId} · ${room.category} · ${money(room.nightlyRate)} per night`;
      $('availability').hidden = true;
      $('details').hidden = false; status(`Room ${room.roomId} selected. Choose your extras and enter guest details.`);
      $('details').scrollIntoView({ behavior: 'instant', block: 'start' });
    });
    $('rooms').append(button);
  }
  status(rooms.length ? 'Select a room to plan your stay.' : 'Try different dates to find a room.'); lock();
}
$('changeRoom').addEventListener('click', () => {
  if (busy || pending || done) return;
  invalidate(); $('details').hidden = true; $('availability').hidden = false;
  status('Select a room to plan your stay.');
  $('availability').scrollIntoView({ behavior: 'instant', block: 'start' });
});
async function loadPackages() {
  const data = await call('GetPackages', {});
  for (const [id, items, key, name, price, suffix] of [
    ['food', data.food, 'fId', 'fName', 'fPackageCost', ' / night'],
    ['service', data.services, 'sId', 'sName', 'sPackageCost', ' / stay']]) {
    $(id).replaceChildren(new Option('No package', '0'));
    for (const item of items.filter(x => x[key] !== 0)) $(id).add(new Option(`${item[name]} · ${money(item[price])}${suffix}`, String(item[key])));
  }
  packagesReady = true;
}
// Sandboxed Apps hosts need not grant allow-forms. Use buttons and client validation,
// never native navigation/submission; Enter still activates the relevant action.
for (const [form, button] of [['search', 'find'], ['details', 'quoteButton']]) {
  $(form).addEventListener('submit', e => e.preventDefault());
  $(form).addEventListener('keydown', e => { if (e.key === 'Enter' && e.target.tagName === 'INPUT') { e.preventDefault(); $(button).click(); } });
}
$('find').addEventListener('click', async e => {
  e.preventDefault(); if (busy || pending || done) return;
  if (!$('search').reportValidity()) return;
  if (dates().checkOut <= dates().checkIn) { status('Check-out must be after check-in.', true); return; }
  busy = true; invalidate(); $('details').hidden = true; $('availability').hidden = true; lock(); status('Checking availability…');
  try { if (!packagesReady) await loadPackages(); const result = await call('GetAvailableRooms', dates()); busy = false; renderAvailability(result); $('availability').scrollIntoView({ behavior: 'instant', block: 'start' }); }
  catch (error) { status(error.message, true); }
  finally { busy = false; lock(); }
});
for (const id of ['checkIn', 'checkOut']) $(id).addEventListener('input', () => {
  invalidate(); $('availability').hidden = true; $('details').hidden = true; selected = undefined;
});
for (const id of ['food', 'service', 'advance']) $(id).addEventListener('input', invalidate);
for (const id of ['guestName', 'address', 'phone', 'nationalId']) $(id).addEventListener('input', () => { $('confirm').checked = false; lock(); });
$('quoteButton').addEventListener('click', async e => {
  e.preventDefault(); if (busy || done || pending || !selected || !sameDates(dates(), searchDates)) return;
  if (!$('details').reportValidity()) return;
  if (!packagesReady) { status('Packages could not be loaded. Find rooms again to retry.', true); return; }
  busy = true; invalidate(); lock(); status('Getting your quote from the hotel…');
  try {
    quote = await call('CalculateQuote', { roomId: selected.roomId, ...dates(), foodPackageId: +$('food').value, servicePackageId: +$('service').value, advance: +$('advance').value });
    $('quoteStay').textContent = `Room ${selected.roomId} · ${selected.category} · ${quote.stay.checkIn.slice(0,10)} to ${quote.stay.checkOut.slice(0,10)} · ${quote.days} nights`;
    $('quotePackages').textContent = `Food: ${$('food').selectedOptions[0].text}. Service: ${$('service').selectedOptions[0].text}.`;
    $('totals').replaceChildren();
    for (const [label, amount] of [['Total stay', quote.total], ['Advance recorded', quote.total - quote.remaining], ['Remaining', quote.remaining]]) {
      const row = document.createElement('div'), dt = document.createElement('dt'), dd = document.createElement('dd'); dt.textContent = label; dd.textContent = money(amount); row.append(dt, dd); $('totals').append(row);
    }
    $('review').hidden = false; $('book').textContent = 'Confirm & book'; $('confirm').checked = false; tick();
    status('Review your stay and confirm when you’re ready.'); $('review').scrollIntoView({ behavior: 'instant', block: 'nearest' });
  } catch (error) { status(error.message + ' Check availability again if the room has changed.', true); }
  finally { busy = false; lock(); }
});
$('confirm').addEventListener('change', lock);
function tick() {
  if (!quote || done || pending) return;
  $('expiry').textContent = expired() ? 'This quote has expired. Select “Review my stay” for a new quote.' : `Quote valid for ${Math.ceil((Date.parse(quote.expiresUtc) - Date.now()) / 60000)} more minutes. Currency not specified.`;
  lock();
}
setInterval(tick, 1000);
$('book').addEventListener('click', async () => {
  if (busy || done || attempts >= 2 || (!pending && (!quote || expired() || !$('confirm').checked || !$('details').reportValidity()))) return;
  if (!pending) {
    pending = { quoteId: quote.quoteId, guestName: $('guestName').value.trim(), address: $('address').value.trim(), phone: $('phone').value.trim(), nationalId: $('nationalId').value.trim(), advance: +$('advance').value, requestId: crypto.randomUUID(), confirmed: true };
  }
  busy = true; attempts++; lock(); status('Confirming your reservation…');
  try {
    const receipt = await call('CreateReservation', pending);
    done = true; pending = undefined; $('review').hidden = true; $('details').hidden = true; $('availability').hidden = true;
    $('receipt').hidden = false; $('receiptDetails').textContent = `Reservation ${receipt.reservationId} · Room ${receipt.roomId} · ${receipt.checkIn.slice(0,10)} to ${receipt.checkOut.slice(0,10)}`;
    $('receiptTotal').textContent = `Total ${money(receipt.total)} · Recorded paid ${money(receipt.paid)} · Remaining ${money(receipt.remaining)}`;
    status('Reservation confirmed by the hotel.');
    // No guest identity is added to the conversation context.
    app.updateModelContext({ content: [{ type: 'text', text: `Hotel reservation confirmed: ${JSON.stringify(receipt)}` }] }).catch(() => {});
  } catch (error) {
    if (error instanceof ToolError && attempts === 1) {
      pending = undefined; attempts = 0; invalidate(); status(error.message + ' Review your stay again before booking.', true);
    } else {
      // An interrupted response may already have committed. Retain the identical payload and UUID.
      $('book').textContent = 'Retry the same booking';
      status(attempts < 2 ? 'The result is uncertain. Retry the same booking once; the same request ID prevents a duplicate.' : 'The result is still uncertain. Stop here and ask the hotel to check the reservation before starting another booking.', true);
    }
  } finally { busy = false; lock(); }
});
app.ontoolinput = ({ arguments: args }) => { if (!busy && !pending && !done && args?.checkIn && args?.checkOut) { $('checkIn').value = args.checkIn; $('checkOut').value = args.checkOut; } };
app.ontoolresult = result => { try { renderAvailability(unpack(result)); } catch (error) { status(error.message, true); } };
app.onhostcontextchanged = () => {}; // The resource keeps an explicit, accessible light palette.
// Request a stable inline height once; content scrolls inside the app.
// Automatic document sizing can feed a host's iframe ResizeObserver loop.
app.connect().then(async () => {
  app.sendSizeChanged({ height: 640 }).catch(() => {});
  try { await loadPackages(); lock(); } catch (error) { status(error.message, true); $('searchFields').disabled = false; } }).catch(error => status(`Unable to connect to an MCP Apps host: ${error.message}`, true));
