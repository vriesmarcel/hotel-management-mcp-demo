"""Offline packaging checks. No hotel tools or tenant operations are invoked."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location('packager', Path(__file__).with_name('Package-CoworkPlugin.py'))
packager = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packager)


class PackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.output = Path(self.temp.name) / 'plugin.zip'

    def test_skill_archive_is_allowlisted_and_has_no_local_connection(self):
        packager.package(self.output)
        with zipfile.ZipFile(self.output) as archive:
            self.assertEqual(set(archive.namelist()), {'.claude-plugin/plugin.json',
                'skills/book-hotel-room/SKILL.md', 'color.png', 'outline.png'})
            for name in archive.namelist():
                if name.endswith(('.json', '.md')):
                    self.assertNotIn(b'Bearer 123456', archive.read(name))
            for name, size in [('color.png', 192), ('outline.png', 32)]:
                raw = archive.read(name)
                self.assertEqual(packager.struct.unpack('!2I', raw[16:24]), (size, size))

    def test_oauth_package_uses_vault_reference(self):
        packager.package(self.output, mcp_url='https://hotel.example.com/mcp/reservations',
            oauth_reference_id='test-registration-reference', privacy_url='https://hotel.example.com/privacy',
            terms_url='https://hotel.example.com/terms', version='1.1.0')
        with zipfile.ZipFile(self.output) as archive:
            manifest = json.loads(archive.read('manifest.json'))
            self.assertNotIn('.claude-plugin/plugin.json', archive.namelist())
            self.assertEqual(manifest['manifestVersion'], '1.29')
            server = manifest['agentConnectors'][0]['toolSource']['remoteMcpServer']
            self.assertEqual(server['authorization'], {'type': 'OAuthPluginVault', 'referenceId': 'test-registration-reference'})

    def test_dcr_omits_authorization_instead_of_claiming_anonymous_access(self):
        packager.package(self.output, mcp_url='https://hotel.example.com/mcp/reservations',
            oauth_dcr=True, privacy_url='https://hotel.example.com/privacy', terms_url='https://hotel.example.com/terms')
        with zipfile.ZipFile(self.output) as archive:
            server = json.loads(archive.read('manifest.json'))['agentConnectors'][0]['toolSource']['remoteMcpServer']
            self.assertNotIn('authorization', server)

    def test_invalid_or_incomplete_config_does_not_create_a_package(self):
        cases = [dict(mcp_url='http://127.0.0.1:5057/mcp/reservations'),
                 dict(mcp_url='https://127.0.0.1/mcp'),
                 dict(mcp_url='https://hotel.example.com/mcp'),
                 dict(mcp_url='https://hotel.example.com/mcp', oauth_dcr=True),
                 dict(oauth_reference_id='unused'), dict(version='not-a-version'),
                 dict(mcp_url='https://hotel.example.com/mcp?token=secret', oauth_dcr=True)]
        for args in cases:
            with self.subTest(args=args):
                with self.assertRaises(ValueError):
                    packager.package(self.output, **args)
                self.assertFalse(self.output.exists())

    def test_existing_archive_is_preserved(self):
        self.output.write_bytes(b'existing-user-file')
        with self.assertRaises(FileExistsError):
            packager.package(self.output)
        self.assertEqual(self.output.read_bytes(), b'existing-user-file')


if __name__ == '__main__':
    unittest.main()
