"""Generate Jellyfin's repository format from the exact ZIP being released."""
import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
import re
import zipfile

from release_metadata import read_release_metadata, version_parts

REQUIRED_FILES = {"CollectionImageGenerator.Plugin.dll", "CollectionImageGenerator.Core.dll", "LICENSE", "THIRD-PARTY-NOTICES.txt"}
MAX_ARCHIVE_BYTES = 64 * 1024 * 1024


def validate_archive(source):
    with zipfile.ZipFile(source) as archive:
        entries = archive.infolist()
        if len(entries) != len(REQUIRED_FILES) or {entry.filename for entry in entries} != REQUIRED_FILES:
            raise ValueError("Archive must contain only the two plugin assemblies and required licenses")
        if any(entry.file_size == 0 or entry.flag_bits & 1 for entry in entries) or sum(entry.file_size for entry in entries) > MAX_ARCHIVE_BYTES:
            raise ValueError("Archive contains empty, encrypted, or oversized entries")
        if archive.testzip() is not None:
            raise ValueError("Archive CRC validation failed")


def validate_manifest(manifest, metadata, allow_empty=False):
    if not isinstance(manifest, list) or len(manifest) > 1 or (not manifest and not allow_empty):
        raise ValueError("Expected a single-plugin repository manifest")
    if not manifest:
        return []
    package = manifest[0]
    if not isinstance(package, dict) or package.get("guid") != metadata["guid"] or package.get("name") != metadata["name"]:
        raise ValueError("Repository plugin identity differs from the project")
    for key in ("overview", "description", "owner", "category"):
        if not isinstance(package.get(key), str) or not package[key].strip():
            raise ValueError(f"Manifest requires {key}")
    versions = package.get("versions")
    if not isinstance(versions, list) or not versions:
        raise ValueError("Manifest must have release versions")
    seen = set()
    for entry in versions:
        if not isinstance(entry, dict):
            raise ValueError("Invalid version entry")
        parts = version_parts(entry.get("version"), 4)
        version_parts(entry.get("targetAbi"), 4)
        if parts[3] != 0 or entry["version"] in seen:
            raise ValueError("Duplicate version or nonzero release revision")
        seen.add(entry["version"])
        short_version = ".".join(map(str, parts[:3]))
        expected_url = f"https://github.com/{metadata['repository']}/releases/download/v{short_version}/collection-image-generator-{short_version}.zip"
        if entry.get("sourceUrl") != expected_url:
            raise ValueError("Source URL must identify the immutable versioned release asset")
        if not isinstance(entry.get("checksum"), str) or not re.fullmatch(r"[0-9a-fA-F]{32}", entry["checksum"]):
            raise ValueError("Jellyfin requires an MD5 checksum")
        timestamp = entry.get("timestamp")
        if not isinstance(timestamp, str) or datetime.fromisoformat(timestamp).tzinfo is None:
            raise ValueError("Release timestamp must include a timezone")
    return versions


def build_manifest(archive, tag, timestamp=None, previous=None, changelog=None):
    metadata = read_release_metadata()
    if tag != "v" + metadata["version"]:
        raise ValueError("Release tag must match the plugin project version exactly")
    archive = Path(archive)
    if archive.name != metadata["archive_name"] or archive.stat().st_size > MAX_ARCHIVE_BYTES:
        raise ValueError("Unexpected archive filename or size")
    validate_archive(archive)
    package = {key: metadata[key] for key in ("guid", "name", "overview", "description", "owner", "category")}
    package["versions"] = [{
        "version": metadata["assembly_version"],
        "targetAbi": metadata["target_abi"],
        "sourceUrl": f"https://github.com/{metadata['repository']}/releases/download/{tag}/{archive.name}",
        # Jellyfin 12.4 checks MD5 (InstallationManager), not the SHA-256 sidecar.
        "checksum": hashlib.md5(archive.read_bytes(), usedforsecurity=False).hexdigest(),
        "timestamp": timestamp or datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }]
    if changelog:
        package["versions"][0]["changelog"] = changelog
    historic = validate_manifest([] if previous is None else previous, metadata, allow_empty=True)
    current = package["versions"][0]
    validate_manifest([package], metadata)
    if any(version_parts(version["version"], 4) > version_parts(current["version"], 4) for version in historic):
        raise ValueError("A release version must not move backwards")
    matches = [version for version in historic if version["version"] == current["version"]]
    if matches and any(matches[0][key] != current[key] for key in ("sourceUrl", "checksum", "targetAbi")):
        raise ValueError("Published release assets and ABI are immutable; publish a new version")
    package["versions"] = (matches or [current]) + [version for version in historic if version["version"] != current["version"]]
    package["versions"].sort(key=lambda version: tuple(map(int, version["version"].split("."))), reverse=True)
    return [package]


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", type=Path, required=True)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--previous", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--changelog")
    args = parser.parse_args()
    previous = json.loads(args.previous.read_text(encoding="utf-8")) if args.previous else None
    manifest = build_manifest(args.archive, args.tag, previous=previous, changelog=args.changelog)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    pending = args.output.with_suffix(args.output.suffix + ".tmp")
    try:
        pending.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        pending.replace(args.output)
    finally:
        pending.unlink(missing_ok=True)
    print(args.output)
