"""Switch only the hotel MCP registrations; requires Python 3.11+ for TOML validation."""
import argparse
import copy
import re
import shutil
import tomllib
import uuid
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--demo', choices=('2', '3'), default='3')
parser.add_argument('--config', type=Path, default=Path.home() / '.codex' / 'config.toml')
args = parser.parse_args()
path = args.config
original = path.read_text(encoding='utf-8-sig') if path.exists() else ''
before = tomllib.loads(original)
text = original
managed_keys = {'url', 'http_headers', 'bearer_token_env_var', 'tool_timeout_sec', 'startup_timeout_sec', 'enabled'}

for name, route in [('hotel_reservations', 'reservations'), ('hotel_stays', 'stays'), ('hotel_billing', 'billing')]:
    # Refuse uncommon layouts rather than rewriting unrelated or multiline configuration.
    if re.search(r'(?m)^\[mcp_servers\.' + name + r'\.', text):
        raise SystemExit('Nested hotel settings require a manual merge with config/codex-mcp.example.toml.')
    pattern = re.compile(r'(?ms)^\[mcp_servers\.' + name + r'\][ \t]*\r?\n(.*?)(?=^\[|\Z)')
    match = pattern.search(text)
    existing = match.group(1) if match else ''
    kept = []
    for line in existing.splitlines():
        key = re.match(r'^\s*([A-Za-z_]+)\s*=', line)
        if not key or key.group(1) not in managed_keys:
            kept.append(line)
    endpoint = '/mcp/' if args.demo == '2' and route == 'reservations' else '/mcp/' + route
    enabled = args.demo == '3' or route == 'reservations'
    block = f'''[mcp_servers.{name}]
url = "http://127.0.0.1:5057{endpoint}"
http_headers = {{ Authorization = "Bearer 123456" }}
startup_timeout_sec = 30
tool_timeout_sec = 300
enabled = {str(enabled).lower()}
'''
    block += '\n'.join(kept).strip() + '\n\n'
    text = text[:match.start()] + block + text[match.end():] if match else text.rstrip() + '\n\n' + block

after = tomllib.loads(text)
# A semantic comparison proves all unrelated settings survived the edit.
expected = copy.deepcopy(before)
servers = expected.setdefault('mcp_servers', {})
for name in ('hotel_reservations', 'hotel_stays', 'hotel_billing'):
    server = servers.setdefault(name, {})
    for key in managed_keys:
        server.pop(key, None)
        if key in after['mcp_servers'][name]:
            server[key] = after['mcp_servers'][name][key]
if expected != after:
    raise SystemExit('Configuration validation failed; no file was changed.')
path.parent.mkdir(parents=True, exist_ok=True)
if path.exists():
    backup = path.with_name('config.toml.hotel-demo-' + uuid.uuid4().hex[:8] + '.bak')
    shutil.copy2(path, backup)
path.write_text(text, encoding='utf-8')
print(f'Configured hotel MCP registrations for Demo {args.demo}. Existing settings preserved; backup saved alongside config.toml.')
print('Restart/reconnect Codex after starting the selected demo. No token environment variable is needed.')
