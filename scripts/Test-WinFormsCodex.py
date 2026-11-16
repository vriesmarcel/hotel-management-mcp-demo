import importlib.util
from pathlib import Path
import tempfile
import tomllib
import unittest

spec = importlib.util.spec_from_file_location('setup', Path(__file__).with_name('Configure-WinFormsCodex.py'))
setup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(setup)


class ConfigurationTests(unittest.TestCase):
    def test_preserves_other_settings_and_is_idempotent(self):
        original = '''# retain this comment
model = "example-model"
[mcp_servers.other]
command = "some-command"
[mcp_servers.hotel_reservations]
url = "http://127.0.0.1:5057/mcp/reservations"
bearer_token_env_var = "OLD_TOKEN"
disabled_tools = ["GetAvailableRooms"]
[mcp_servers.hotel_reservations.http_headers]
Authorization = "old-value"
[mcp_servers.hotel_stays]
url = "http://127.0.0.1:5057/mcp/stays"
enabled = true
[plugins."hotel-booking@local.marketplace"]
enabled = true
[plugins."documents@example"]
enabled = true
'''
        updated = setup.configured_text(original)
        before, after = tomllib.loads(original), tomllib.loads(updated)
        self.assertIn('# retain this comment', updated)
        self.assertEqual(before['mcp_servers']['other'], after['mcp_servers']['other'])
        self.assertEqual(before['plugins']['documents@example'], after['plugins']['documents@example'])
        self.assertFalse(after['mcp_servers']['hotel_stays']['enabled'])
        self.assertFalse(after['plugins']['hotel-booking@local.marketplace']['enabled'])
        self.assertEqual(after['mcp_servers']['hotel_reservations'], setup.SETTINGS)
        self.assertEqual(setup.configured_text(updated), updated)

    def test_adds_missing_hotel_without_replacing_other_servers(self):
        updated = setup.configured_text('[mcp_servers.node_repl]\ncommand="node"\n')
        self.assertEqual(set(tomllib.loads(updated)['mcp_servers']), {'node_repl', 'hotel_reservations'})

    def test_backup_contains_exact_original_and_second_run_does_not_write(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/'config.toml'
            original = b'\xef\xbb\xbfmodel = "unchanged"\r\n'
            path.write_bytes(original)
            setup.configure(path)
            backups = list(path.parent.glob('*.bak'))
            self.assertEqual(len(backups), 1)
            self.assertEqual(backups[0].read_bytes(), original)
            self.assertTrue(path.read_bytes().startswith(b'\xef\xbb\xbf'))
            stamp = path.stat().st_mtime_ns
            setup.configure(path)
            self.assertEqual(path.stat().st_mtime_ns, stamp)
            self.assertEqual(len(list(path.parent.glob('*.bak'))), 1)

    def test_rejects_ambiguous_layout_instead_of_corrupting_it(self):
        # A table-looking line inside a multiline string must not be removed.
        original = 'message = """\n[mcp_servers.hotel_reservations]\nhello\n"""\n'
        with self.assertRaises(ValueError):
            setup.configured_text(original)


if __name__ == '__main__':
    unittest.main()
