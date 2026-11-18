import importlib.util
from pathlib import Path
import tempfile
import tomllib
import unittest

spec = importlib.util.spec_from_file_location('setup', Path(__file__).with_name('Configure-Codex.py'))
setup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(setup)

class SwitchTests(unittest.TestCase):
    def setUp(self):
        setup.DEMO = '3'

    def test_transition_from_quoted_winforms_config(self):
        original = '''model = "unchanged"
["mcp_servers"."hotel_reservations"]
url = "http://127.0.0.1:5057/mcp/"
enabled_tools = ["GetAvailableRooms"]
disabled_tools = ["CalculateQuote"]
bearer_token_env_var = "STALE"
[mcp_servers.hotel_reservations.http_headers]
Authorization = "old"
[mcp_servers.node_repl]
command = "node"
[plugins."hotel-booking@local"]
enabled = true
'''
        result = setup.configured_text(original)
        config = tomllib.loads(result)
        self.assertEqual(config['model'], 'unchanged')
        self.assertEqual(config['mcp_servers']['node_repl'], {'command':'node'})
        for name, route in [('hotel_reservations','reservations'),('hotel_stays','stays'),('hotel_billing','billing')]:
            server = config['mcp_servers'][name]
            self.assertTrue(server['enabled'])
            self.assertEqual(server['url'], 'http://127.0.0.1:5057/mcp/'+route)
            self.assertEqual(server['http_headers'], {'Authorization':'Bearer 123456'})
            self.assertNotIn('enabled_tools', server)
            self.assertNotIn('disabled_tools', server)
            self.assertNotIn('bearer_token_env_var', server)
        self.assertFalse(config['plugins']['hotel-booking@local']['enabled'])
        self.assertEqual(result, setup.configured_text(result))

    def test_round_trip_to_winforms(self):
        contexts = setup.configured_text('')
        setup.DEMO = '2'
        servers = tomllib.loads(setup.configured_text(contexts))['mcp_servers']
        self.assertEqual(servers['hotel_reservations']['url'], 'http://127.0.0.1:5057/mcp/')
        self.assertEqual(servers['hotel_reservations']['enabled_tools'], ['GetAvailableRooms'])
        self.assertFalse(servers['hotel_stays']['enabled'])
        self.assertFalse(servers['hotel_billing']['enabled'])

    def test_backup_and_idempotent_write(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/'config.toml'
            original = b'\xef\xbb\xbfmodel = "unchanged"\r\n'
            path.write_bytes(original)
            setup.configure(path)
            backups = list(path.parent.glob('*.bak'))
            self.assertEqual(len(backups), 1)
            self.assertEqual(backups[0].read_bytes(), original)
            stamp = path.stat().st_mtime_ns
            setup.configure(path)
            self.assertEqual(path.stat().st_mtime_ns, stamp)

    def test_ambiguous_multiline_layout_is_rejected(self):
        with self.assertRaises(ValueError):
            setup.configured_text('message = """\n[mcp_servers.hotel_reservations]\nhello\n"""\n')

if __name__ == '__main__':
    unittest.main()
