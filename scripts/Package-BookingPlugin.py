"""Export only the plugin files, never the host, SQL settings, or rehearsal data."""
import argparse
from pathlib import Path
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("output", type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1] / "plugins" / "hotel-booking"
files = [root / ".codex-plugin/plugin.json", root / ".mcp.json",
         root / "skills/book-hotel-room/SKILL.md", root / "skills/book-hotel-room/agents/openai.yaml",
         root / "skills/book-hotel-room/scripts/hotel_mcp.py"]
for file in files:
    if not file.is_file() or file.is_symlink():
        raise SystemExit("Missing or unsafe package file: " + str(file.relative_to(root)))
args.output.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(args.output, "w", zipfile.ZIP_DEFLATED) as archive:
    for file in files:
        archive.write(file, file.relative_to(root).as_posix())
print("Packaged", len(files), "files in", args.output)
