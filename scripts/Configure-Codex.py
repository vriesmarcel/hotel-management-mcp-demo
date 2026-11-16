"""Switch hotel demos safely; requires Python 3.11+. Does not start or stop hosts."""
import argparse
import copy
from datetime import datetime
import json
import os
from pathlib import Path
import re
import tempfile
import tomllib

DEMO = '3'

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
    replacements = {}
    for name, route in (('hotel_reservations', 'reservations'), ('hotel_stays', 'stays'), ('hotel_billing', 'billing')):
        settings = {'enabled': DEMO == '3' or route == 'reservations',
                    'url': 'http://127.0.0.1:5057' + ('/mcp/' if DEMO == '2' and route == 'reservations' else '/mcp/' + route),
                    'http_headers': {'Authorization': 'Bearer 123456'},
                    'startup_timeout_sec': 30, 'tool_timeout_sec': 300}
        if DEMO == '2' and route == 'reservations':
            settings['enabled_tools'] = ['GetAvailableRooms']
        servers[name] = settings
        replacements[('mcp_servers', name)] = settings
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
        backup = path.with_name(path.name + '.before-hotel-switch-' + datetime.now().strftime('%Y%m%d-%H%M%S-%f') + '.bak')
        with backup.open('xb') as file:
            file.write(raw)
        print('Configuration backup:', backup)
    fd, temp = tempfile.mkstemp(prefix='hotel-switch-', suffix='.toml', dir=path.parent)
    try:
        with os.fdopen(fd, 'wb') as file:
            file.write(data)
        if (path.read_bytes() if path.exists() else b'') != raw:
            raise ValueError('Codex config changed during setup; retry after closing Settings.')
        os.replace(temp, path)
    finally:
        if os.path.exists(temp):
            os.unlink(temp)
    print('PASS: configured hotel MCP registrations for repository Demo', DEMO)
    print('Later hotel plugins disabled; unrelated settings preserved. Reconnect Codex after the host starts.')


def main():
    global DEMO
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--demo', choices=('2', '3'), default='3', help='Repository checkpoint: 2 = WinForms, 3 = separate host and three contexts')
    parser.add_argument('--config', type=Path, default=Path(os.environ.get('CODEX_HOME', Path.home()/'.codex'))/'config.toml')
    args = parser.parse_args()
    DEMO = args.demo
    try:
        configure(args.config)
    except (ValueError, OSError) as error:
        parser.exit(1, str(error) + '\n')


if __name__ == '__main__':
    main()
