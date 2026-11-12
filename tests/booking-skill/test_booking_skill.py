import contextlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import urllib.error
import uuid
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PLUGIN = ROOT / "plugins/hotel-booking"
spec = importlib.util.spec_from_file_location("hotel_mcp", PLUGIN / "skills/book-hotel-room/scripts/hotel_mcp.py")
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


class BookingSkillTests(unittest.TestCase):
    def test_reference_commands_only_read_resources(self):
        for command, uri in [("room-info", "hotel://room-types"), ("cancellation-policy", "hotel://policies/cancellation/v1")]:
            with self.subTest(command=command), patch.object(helper, "rpc", side_effect=[{}, {"contents": []}]) as network, contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(helper.main([command]), 0)
                self.assertEqual(network.call_args_list[-1].args, ("resources/read", {"uri": uri}, 2))
                self.assertEqual(network.call_count, 2)
    def booking(self):
        return dict(quoteId=str(uuid.uuid4()), guestName="Synthetic Guest", address="Demo Street",
                    phone="DEMO-PHONE", nationalId="DEMO-ID", advance=0, requestId=str(uuid.uuid4()), confirmed=True)

    def test_json_and_sse_results(self):
        expected = {"content": [{"type": "text", "text": "{}"}]}
        raw = json.dumps({"jsonrpc": "2.0", "id": 2, "result": expected})
        self.assertEqual(helper.decode_response(raw, 2), expected)
        self.assertEqual(helper.decode_response(": ping\r\n\r\nevent: message\r\ndata: " + raw + "\r\n\r\n", 2), expected)
        with self.assertRaises(ValueError):
            helper.decode_response(raw, 3)

    def test_unconfirmed_booking_never_reaches_transport(self):
        args = self.booking()
        args["confirmed"] = False
        with patch.object(helper, "rpc") as network, self.assertRaises(ValueError):
            helper.invoke("CreateReservation", args)
        network.assert_not_called()

    def test_wrong_context_rejected(self):
        with patch.object(helper, "rpc") as network, self.assertRaises(ValueError):
            helper.invoke("RecordPayment", {})
        network.assert_not_called()

    def test_id_and_argument_validation(self):
        args = self.booking()
        for key, value in [("requestId", "bad"), ("quoteId", "bad"), ("confirmed", "true")]:
            with self.subTest(key=key), self.assertRaises(ValueError):
                helper.validate("CreateReservation", {**args, key: value})
        with self.assertRaises(ValueError):
            helper.validate("CalculateQuote", {"roomId": 1, "checkIn": "2026-11-18", "checkOut": "2026-11-21", "total": 1})

    def test_transport_preserves_booking_request_exactly(self):
        args = self.booking()
        response = {"structuredContent": {"reservationId": 42, "status": "Reserved"}}
        with patch.object(helper, "rpc", return_value=response) as network:
            self.assertEqual(helper.invoke("CreateReservation", args), response["structuredContent"])
        network.assert_called_once_with("tools/call", {"name": "CreateReservation", "arguments": args}, 2)

    def test_unknown_booking_outcome_preserves_id_without_retry(self):
        args = self.booking()
        output = io.StringIO()
        with patch.object(helper, "rpc", side_effect=[{}, urllib.error.URLError("timeout")]) as network, \
             patch("sys.stdin", io.StringIO(json.dumps(args))), contextlib.redirect_stdout(output):
            self.assertEqual(helper.main(["CreateReservation"]), 1)
        result = json.loads(output.getvalue())
        self.assertEqual(result["outcome"], "unknown")
        self.assertEqual(result["requestId"], args["requestId"])
        self.assertEqual(network.call_count, 2)  # Initialize and one booking attempt only.
        self.assertNotIn("DEMO-ID", output.getvalue())

    def test_quote_does_not_book(self):
        arguments = {"roomId": 16, "checkIn": "2026-11-18", "checkOut": "2026-11-21", "foodPackageId": 0, "servicePackageId": 0, "advance": 0}
        with patch.object(helper, "rpc", return_value={"content": [{"type": "text", "text": '{"total":9000}'}]}) as network:
            self.assertEqual(helper.invoke("CalculateQuote", arguments)["total"], 9000)
        network.assert_called_once_with("tools/call", {"name": "CalculateQuote", "arguments": arguments}, 2)

    def test_expired_quote_is_error_not_rebooking(self):
        with patch.object(helper, "rpc", return_value={"isError": True, "content": [{"type": "text", "text": "Quote expired"}]}) as network, self.assertRaises(helper.ToolFailure):
            helper.invoke("CreateReservation", self.booking())
        self.assertEqual(network.call_count, 1)

    def test_explicit_quote_rejection_is_distinct_from_timeout(self):
        output = io.StringIO()
        response = {"isError": True, "content": [{"type": "text", "text": "Quote expired"}]}
        with patch.object(helper, "rpc", side_effect=[{}, response]) as network, \
             patch("sys.stdin", io.StringIO(json.dumps(self.booking()))), contextlib.redirect_stdout(output):
            self.assertEqual(helper.main(["CreateReservation"]), 1)
        self.assertEqual(json.loads(output.getvalue())["outcome"], "rejected")
        self.assertEqual(network.call_count, 2)

    def test_plugin_and_skill_reference_same_endpoint(self):
        manifest = json.loads((PLUGIN / ".codex-plugin/plugin.json").read_text())
        server_file = (PLUGIN / manifest["mcpServers"]).resolve()
        self.assertTrue(server_file.is_relative_to(PLUGIN.resolve()))
        servers = json.loads(server_file.read_text())["mcpServers"]
        self.assertEqual(list(servers), ["hotel_reservations"])
        self.assertEqual(servers["hotel_reservations"]["url"], helper.ENDPOINT)
        self.assertEqual(servers["hotel_reservations"]["http_headers"]["Authorization"], helper.HEADERS["Authorization"])
        marketplace = json.loads((ROOT / ".agents/plugins/marketplace.json").read_text())
        self.assertEqual((ROOT / marketplace["plugins"][0]["source"]["path"]).resolve(), PLUGIN.resolve())

    def test_install_and_package_in_isolated_home(self):
        with tempfile.TemporaryDirectory() as directory:
            home = Path(directory)
            market = home / ".agents/plugins/marketplace.json"
            market.parent.mkdir(parents=True)
            market.write_text(json.dumps({"name": "existing", "plugins": [{"name": "keep-me", "source": {"source": "local", "path": "./existing"}}]}))
            for mode in ("skill", "plugin", "plugin"):
                subprocess.run([sys.executable, str(ROOT / "scripts/Install-BookingSkill.py"), "--mode", mode, "--home", str(home)], check=True, capture_output=True)
            self.assertEqual((home / ".codex/skills/book-hotel-room/SKILL.md").read_bytes(), (PLUGIN / "skills/book-hotel-room/SKILL.md").read_bytes())
            after = json.loads(market.read_text())
            self.assertEqual(after["name"], "existing")
            self.assertEqual([p["name"] for p in after["plugins"]], ["keep-me", "hotel-booking"])
            output = home / "bundle.zip"
            subprocess.run([sys.executable, str(ROOT / "scripts/Package-BookingPlugin.py"), str(output)], check=True, capture_output=True)
            with zipfile.ZipFile(output) as archive:
                names = archive.namelist()
                self.assertEqual(len(names), 5)
                self.assertIn(".codex-plugin/plugin.json", names)
                self.assertTrue(all(".." not in Path(name).parts for name in names))
                self.assertFalse(any("local.config" in name or "appsettings" in name or ".dll" in name for name in names))


if __name__ == "__main__":
    unittest.main()
