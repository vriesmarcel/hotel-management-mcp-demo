"""Install the standalone booking skill, or stage its bundle in a personal marketplace."""
import argparse
import json
import os
from pathlib import Path
import shutil
import uuid

ROOT = Path(__file__).resolve().parents[1]
PLUGIN = ROOT / "plugins" / "hotel-booking"
SKILL = PLUGIN / "skills" / "book-hotel-room"


def copy_package(source, target):
    if target.is_symlink():
        raise ValueError("Installation target must not be a symbolic link.")
    files = [p for p in source.rglob("*") if p.is_file() and "__pycache__" not in p.parts and p.suffix != ".pyc"]
    for path in files:
        if path.is_symlink():
            raise ValueError("Package files must not be symbolic links.")
    if target.exists():
        backup = target.parents[1] / "hotel-demo-backups" / (target.name + "-" + uuid.uuid4().hex[:8])
        backup.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(target, backup)
        print("Preserved the previous installation at", backup)
    for path in files:
        destination = target / path.relative_to(source)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, destination)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mode", choices=["skill", "plugin"], default="skill")
    parser.add_argument("--home", type=Path, default=Path.home(), help="User home; override for installation tests.")
    args = parser.parse_args()
    codex_home = Path(os.environ.get("CODEX_HOME", str(args.home / ".codex"))) if args.home == Path.home() else args.home / ".codex"
    if args.mode == "skill":
        target = codex_home / "skills" / "book-hotel-room"
        copy_package(SKILL, target)
        print("Installed standalone skill:", target)
        print("Open a new chat or restart Codex to refresh skill discovery. Invoke $book-hotel-room.")
        return
    # A personal marketplace resolves paths from user home, so keep its bundle there.
    target = args.home / ".codex" / "plugins" / "hotel-booking"
    copy_package(PLUGIN, target)
    marketplace = args.home / ".agents" / "plugins" / "marketplace.json"
    data = json.loads(marketplace.read_text(encoding="utf-8-sig")) if marketplace.exists() else {
        "name": "hotel-demo", "interface": {"displayName": "Hotel MCP Demos"}, "plugins": []}
    entries = data.setdefault("plugins", [])
    entry = {"name": "hotel-booking", "source": {"source": "local", "path": "./.codex/plugins/hotel-booking"},
             "policy": {"installation": "AVAILABLE", "authentication": "ON_INSTALL"}, "category": "Productivity"}
    matches = [i for i, item in enumerate(entries) if item.get("name") == "hotel-booking"]
    if len(matches) > 1:
        raise ValueError("Duplicate Hotel Booking entries; marketplace was not changed.")
    if matches:
        entries[matches[0]] = entry
    else:
        entries.append(entry)
    marketplace.parent.mkdir(parents=True, exist_ok=True)
    if marketplace.exists():
        shutil.copy2(marketplace, marketplace.with_name("marketplace.json." + uuid.uuid4().hex[:8] + ".bak"))
    marketplace.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    print("Staged plugin in personal marketplace:", data["name"])
    print("Restart Codex, select this local marketplace in Plugins, and install Hotel Booking Demo.")
    print("This registers the source; it does not claim the plugin is installed or disable existing hotel connections.")


if __name__ == "__main__":
    main()
