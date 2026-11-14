"""Package the booking skill for Cowork, optionally adding an OAuth HTTPS connector."""
import argparse
import ipaddress
import json
from pathlib import Path
import re
import struct
from urllib.parse import urlsplit
import uuid
import zipfile
import zlib

APP_ID = str(uuid.uuid5(uuid.NAMESPACE_URL, 'https://github.com/vriesmarcel/hotel-management-mcp-demo/plugins/hotel-booking-cowork'))
REPO_URL = 'https://github.com/vriesmarcel/hotel-management-mcp-demo'


def remote_url(value):
    parsed = urlsplit(value)
    host = parsed.hostname or ''
    if (parsed.scheme != 'https' or not host or parsed.username or parsed.password
            or parsed.query or parsed.fragment or '.' not in host
            or host.lower().endswith(('.localhost', '.local', '.internal'))):
        raise ValueError('Use a reachable HTTPS URL without credentials, query strings or fragments.')
    try:
        addr = ipaddress.ip_address(host)
    except ValueError:
        pass
    else:
        if not addr.is_global:
            raise ValueError('Loopback/private IP addresses are not remote Cowork endpoints.')
    return value


def icon(size, outline=False):
    """Original geometric hotel icon, encoded as RGBA PNG using the standard library."""
    pixels = bytearray()
    for y in range(size):
        for x in range(size):
            u, v = x / size, y / size
            building = .22 <= u < .78 and .18 <= v < .84
            edge = building and (u < .27 or u >= .73 or v < .23 or v >= .79)
            window = any(a <= u < a+.10 and b <= v < b+.10
                         for a in (.34, .56) for b in (.31, .51))
            door = .44 <= u < .56 and .68 <= v < .84
            if outline:
                color = (255, 255, 255, 255) if edge or window or door else (0, 0, 0, 0)
            else:
                color = (255, 255, 255, 255) if building and not (window or door) else (39, 95, 100, 255)
            pixels.extend(color)
    raw = b''.join(b'\x00' + pixels[y*size*4:(y+1)*size*4] for y in range(size))
    def chunk(kind, data):
        return struct.pack('!I', len(data)) + kind + data + struct.pack('!I', zlib.crc32(kind + data) & 0xffffffff)
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('!2I5B', size, size, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b''))


def package(output, *, mcp_url=None, oauth_reference_id=None, oauth_dcr=False,
            privacy_url=None, terms_url=None, version=None):
    root = Path(__file__).resolve().parents[1] / 'plugins' / 'hotel-booking-cowork'
    paths = ['.claude-plugin/plugin.json', 'skills/book-hotel-room/SKILL.md']
    for path in paths:
        file = root / path
        if not file.is_file() or file.is_symlink():
            raise ValueError('Missing or unsafe source: ' + path)
    metadata = json.loads((root / paths[0]).read_text(encoding='utf-8'))
    if version:
        if not re.fullmatch(r'\d+\.\d+\.\d+', version):
            raise ValueError('Version must be major.minor.patch.')
        metadata['version'] = version
    skill = (root / paths[1]).read_bytes()
    files = {paths[1]: skill, 'color.png': icon(192), 'outline.png': icon(32, True)}
    if mcp_url:
        remote_url(mcp_url)
        if bool(oauth_reference_id) == bool(oauth_dcr):
            raise ValueError('Choose exactly one: --oauth-reference-id or --oauth-dcr.')
        if not privacy_url or not terms_url:
            raise ValueError('Native Microsoft 365 packages need --privacy-url and --terms-url.')
        remote_url(privacy_url)
        remote_url(terms_url)
        server = {'mcpServerUrl': mcp_url}
        if oauth_reference_id:
            if len(oauth_reference_id) > 128 or not oauth_reference_id.strip():
                raise ValueError('OAuth reference ID must be 1–128 characters.')
            server['authorization'] = {'type': 'OAuthPluginVault', 'referenceId': oauth_reference_id}
        # Omitted authorization invokes Cowork's DCR discovery; the server must implement it.
        manifest = {
            '$schema': 'https://developer.microsoft.com/json-schemas/teams/v1.29/MicrosoftTeams.schema.json',
            'manifestVersion': '1.29', 'version': metadata['version'], 'id': APP_ID,
            'developer': {'name': 'Marcel de Vries', 'websiteUrl': REPO_URL,
                          'privacyUrl': privacy_url, 'termsOfUseUrl': terms_url},
            'name': {'short': 'Hotel Booking Demo', 'full': 'Hotel Booking Demo for Copilot Cowork'},
            'description': {'short': 'Guided demo hotel reservations with confirmed quotes',
                            'full': 'Find hotel rooms, choose packages, review pricing and cancellation terms, and book after explicit confirmation using the hotel Reservations connector. Demo use with synthetic guest details only.'},
            'icons': {'color': 'color.png', 'outline': 'outline.png'}, 'accentColor': '#275F64',
            'agentSkills': [{'folder': './skills/book-hotel-room'}],
            'agentConnectors': [{'id': 'hotel-reservations', 'displayName': 'Hotel Reservations',
                'description': 'Room availability, packages, quotes and confirmed reservations.',
                'toolSource': {'remoteMcpServer': server}}]
        }
        files['manifest.json'] = (json.dumps(manifest, indent=2) + '\n').encode('utf-8')
        mode = 'Microsoft 365 package with OAuth connector'
    else:
        if oauth_reference_id or oauth_dcr or privacy_url or terms_url:
            raise ValueError('Connector options require --mcp-url.')
        files[paths[0]] = (json.dumps(metadata, indent=2) + '\n').encode('utf-8')
        mode = 'Compatible skill-only plugin (Cowork converts on upload)'
    output = Path(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'x', zipfile.ZIP_DEFLATED) as archive:
        for name, data in sorted(files.items()):
            info = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, data)
    return mode


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    parser.add_argument('--mcp-url')
    auth = parser.add_mutually_exclusive_group()
    auth.add_argument('--oauth-reference-id', help='Microsoft plugin-vault registration reference, not a secret or Entra client ID')
    auth.add_argument('--oauth-dcr', action='store_true', help='Only for an endpoint implementing OAuth dynamic client registration')
    parser.add_argument('--privacy-url')
    parser.add_argument('--terms-url')
    parser.add_argument('--version')
    args = vars(parser.parse_args())
    try:
        print(package(**args))
    except (ValueError, FileExistsError) as error:
        parser.error(str(error))


if __name__ == '__main__':
    main()
