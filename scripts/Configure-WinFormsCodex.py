"""Configure Codex for the embedded WinForms demo, or probe that host read-only."""
import argparse
import copy
from datetime import datetime
import json
import os
from pathlib import Path
import re
import tempfile
import tomllib
import urllib.error
import urllib.request

URL = 'http://127.0.0.1:5057/mcp/'
SERVER = 'hotel_reservations'
SETTINGS = {'enabled': True, 'url': URL,
            'http_headers': {'Authorization': 'Bearer 123456'},
            'enabled_tools': ['GetAvailableRooms'], 'startup_timeout_sec': 20,
            'tool_timeout_sec': 300}


def toml_value(value):
    if isinstance(value, str):
        return json.dumps(value, ensure_ascii=False)
    if isinstance(value, bool):
        return str(value).lower()
    if isinstance(value, (int, float)):
        return str(value)
    if isinstance(value, list):
        return '[' + ', '.join(toml_value(v) for v in value) + ']'
    if isinstance(value, dict):
        return '{ ' + ', '.join(json.dumps(k) + ' = ' + toml_value(v) for k, v in value.items()) + ' }'
    raise ValueError('Unsupported value in hotel configuration; no changes written.')


def header_path(line):
    # Parse names with TOML itself, including quoted names containing dots.
    if not re.match(r'^\s*\[(?!\[)', line):
        return None
    try:
        node = tomllib.loads(line + '\n__winforms_marker__ = true\n')
        path = []
        while isinstance(node, dict) and '__winforms_marker__' not in node:
            if len(node) != 1:
                return None
            key, node = next(iter(node.items()))
            path.append(key)
        return tuple(path)
    except tomllib.TOMLDecodeError:
        return None


def configured_text(original):
    before = tomllib.loads(original)
    expected = copy.deepcopy(before)
    servers = expected.setdefault('mcp_servers', {})
    servers[SERVER] = copy.deepcopy(SETTINGS)
    replacements = {('mcp_servers', SERVER): servers[SERVER]}
    for name in ('hotel_stays', 'hotel_billing'):
        if name in servers:
            servers[name]['enabled'] = False
            replacements[('mcp_servers', name)] = servers[name]
    for name, plugin in expected.get('plugins', {}).items():
        if name.lower().startswith(('hotel-booking@', 'hotel-booking-cowork@')):
            plugin['enabled'] = False
            replacements[('plugins', name)] = plugin
    if expected == before:
        return original
    kept = []
    skipping = False
    for line in original.splitlines(keepends=True):
        path = header_path(line)
        if path is not None:
            skipping = any(path[:len(key)] == key for key in replacements)
        elif re.match(r'^\s*\[\[', line):
            skipping = False
        if not skipping:
            kept.append(line)
    result = ''.join(kept).rstrip() + '\n\n'
    for path, value in replacements.items():
        result += '[' + '.'.join(json.dumps(k) for k in path) + ']\n'
        result += ''.join(json.dumps(k) + ' = ' + toml_value(v) + '\n' for k, v in value.items()) + '\n'
    if tomllib.loads(result) != expected:
        raise ValueError('Cannot safely preserve this TOML layout. No changes written.')
    return result


def configure(path):
    raw = path.read_bytes() if path.exists() else b''
    original = raw.decode('utf-8-sig')
    result = configured_text(original)
    if result == original:
        print('PASS: Codex is already configured; no file change or extra backup.')
        return
    data = (b'\xef\xbb\xbf' if raw.startswith(b'\xef\xbb\xbf') else b'') + result.encode('utf-8')
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        backup = path.with_name(path.name + '.before-winforms-' + datetime.now().strftime('%Y%m%d-%H%M%S-%f') + '.bak')
        with backup.open('xb') as file:
            file.write(raw)
        print('Configuration backup:', backup)
    fd, temp = tempfile.mkstemp(prefix='winforms-', suffix='.toml', dir=path.parent)
    try:
        with os.fdopen(fd, 'wb') as file:
            file.write(data)
        if (path.read_bytes() if path.exists() else b'') != raw:
            raise ValueError('Codex config changed during setup; retry after closing Settings.')
        os.replace(temp, path)
    finally:
        if os.path.exists(temp):
            os.unlink(temp)
    print('PASS: configured hotel_reservations for', URL)
    print('Other configured hotel contexts/plugins disabled; unrelated settings preserved.')


def check_config(path):
    data = tomllib.loads(path.read_text(encoding='utf-8-sig'))
    if configured_text(path.read_text(encoding='utf-8-sig')) != path.read_text(encoding='utf-8-sig'):
        raise ValueError('Codex configuration does not match this demo. Run setup first.')
    assert data['mcp_servers'][SERVER] == SETTINGS
    print('PASS: exact WinForms URL, fixed header, one enabled tool and 300-second timeout.')


def rpc(method, params, request_id, timeout):
    payload = json.dumps({'jsonrpc': '2.0', 'id': request_id, 'method': method, 'params': params}).encode()
    headers = {'Authorization': 'Bearer 123456', 'Accept': 'application/json, text/event-stream',
               'Content-Type': 'application/json', 'MCP-Protocol-Version': '2025-06-18'}
    # Match Codex: negotiate in the initialize body, then send the agreed header.
    if method == 'initialize':
        headers.pop('MCP-Protocol-Version')
    request = urllib.request.Request(URL, data=payload, headers=headers)
    # A corporate proxy must not intercept the loopback connection.
    with urllib.request.build_opener(urllib.request.ProxyHandler({})).open(request, timeout=timeout) as response:
        body = response.read().decode('utf-8')
        if 'text/event-stream' in response.headers.get('Content-Type', ''):
            messages = []
            for event in body.replace('\r\n', '\n').split('\n\n'):
                lines = [line[5:].lstrip() for line in event.splitlines() if line.startswith('data:')]
                if lines:
                    messages.append(json.loads('\n'.join(lines)))
        else:
            messages = [json.loads(body)]
    message = next((m for m in messages if m.get('id') == request_id), None)
    if not message or 'error' in message or message.get('result', {}).get('isError'):
        raise ValueError('MCP request failed. Inspect the application debugger; response details suppressed.')
    return message['result']


def probe(availability=False, check_in='2026-11-18', check_out='2026-11-21'):
    hello = rpc('initialize', {'protocolVersion': '2025-06-18', 'capabilities': {},
                'clientInfo': {'name': 'winforms-demo-check', 'version': '1.0.0'}}, 1, 10)
    if hello.get('protocolVersion') != '2025-06-18':
        raise ValueError('Host did not accept the protocol revision used by Codex: 2025-06-18.')
    name = hello.get('serverInfo', {}).get('name')
    if name != 'hotel-reservations-net48':
        raise ValueError('Wrong host answered. Stop the other hotel host and start the WinForms demo.')
    # This prepared Framework adapter is stateless and handles each request independently.
    catalog = rpc('tools/list', {}, 2, 10)
    if [t['name'] for t in catalog.get('tools', [])] != ['GetAvailableRooms']:
        raise ValueError('Unexpected tool catalog; this is not the one-tool WinForms demo.')
    print('PASS: hotel-reservations-net48 accepted Codex protocol 2025-06-18 and exposes only GetAvailableRooms.')
    if availability:
        result = rpc('tools/call', {'name': 'GetAvailableRooms', 'arguments': {
            'checkIn': check_in, 'checkOut': check_out}}, 3, 300)
        data = result.get('structuredContent')
        if not data or 'rooms' not in data:
            raise ValueError('No structured availability returned.')
        print('PASS: live SQL availability returned for', check_in, 'to', check_out)
        print(json.dumps({k: data[k] for k in ('checkIn', 'checkOut', 'nights', 'rooms') if k in data}, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config', type=Path, default=Path(os.environ.get('CODEX_HOME', Path.home()/'.codex'))/'config.toml')
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--check', action='store_true')
    mode.add_argument('--probe', action='store_true')
    parser.add_argument('--availability', action='store_true', help='With --probe, call the read-only tool; this can hit your breakpoint')
    parser.add_argument('--check-in', default='2026-11-18')
    parser.add_argument('--check-out', default='2026-11-21')
    args = parser.parse_args()
    if args.availability and not args.probe:
        parser.error('--availability requires --probe')
    try:
        if args.probe:
            probe(args.availability, args.check_in, args.check_out)
        elif args.check:
            check_config(args.config)
        else:
            configure(args.config)
            check_config(args.config)
    except urllib.error.HTTPError as error:
        parser.exit(1, f'HTTP {error.code}: check the fixed token, exact /mcp/ path and running host.\n')
    except (urllib.error.URLError, TimeoutError):
        parser.exit(1, 'Cannot reach the host from this execution environment, or it timed out. Run this probe in your own PowerShell with Project_HMS running and the debugger resumed.\n')
    except (ValueError, OSError, AssertionError) as error:
        parser.exit(1, str(error) + '\n')


if __name__ == '__main__':
    main()
