"""Read release identity and versions from the plugin project, never from a tag."""
import argparse
import json
from pathlib import Path
import re
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PROJECT = Path("src/CollectionImageGenerator.Plugin/CollectionImageGenerator.Plugin.csproj")


def version_parts(value, length):
    if not isinstance(value, str) or not re.fullmatch(r"(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*)){" + str(length - 1) + r"}", value):
        raise ValueError(f"Expected a {length}-component numeric version: {value!r}")
    parts = tuple(map(int, value.split(".")))
    if any(part > 65535 for part in parts):
        raise ValueError("Version component exceeds the assembly version limit")
    return parts


def read_release_metadata(root=ROOT):
    root = Path(root)
    metadata = json.loads((root / "catalog.json").read_text(encoding="utf-8"))
    for key in ("guid", "name", "overview", "description", "owner", "category", "repository"):
        if not isinstance(metadata.get(key), str) or not metadata[key].strip():
            raise ValueError(f"Catalog metadata requires {key}")
    if str(uuid.UUID(metadata["guid"])) != metadata["guid"]:
        raise ValueError("Catalog GUID must be canonical")
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", metadata["repository"]):
        raise ValueError("Expected a GitHub owner/repository")
    project = ET.parse(root / PROJECT).getroot()
    version = project.findtext(".//Version")
    version_parts(version, 3)
    assembly = project.findtext(".//AssemblyVersion")
    version_parts(assembly, 4)
    if assembly != version + ".0" or project.findtext(".//FileVersion") != assembly:
        raise ValueError("Version, AssemblyVersion, and FileVersion must agree")
    references = {p.attrib["Include"]: p.attrib.get("Version") for p in project.findall(".//PackageReference")}
    abi = references.get("Jellyfin.Controller")
    version_parts(abi, 3)
    if any(references.get(name) != abi for name in ("Jellyfin.Model", "Jellyfin.Common")):
        raise ValueError("Jellyfin package versions must agree")
    # Fail safely if the literal identity changes without updating catalog metadata.
    source = (root / "src/CollectionImageGenerator.Plugin/Plugin.cs").read_text(encoding="utf-8")
    name_match = re.search(r'public override string Name\s*=>\s*"([^"\\]+)"', source)
    guid_match = re.search(r'public override Guid Id\s*=>\s*new\("([^"]+)"\)', source)
    if not name_match or name_match[1] != metadata["name"] or not guid_match or guid_match[1] != metadata["guid"]:
        raise ValueError("Catalog identity must match the plugin's declared name and GUID")
    return dict(metadata, version=version, assembly_version=assembly, target_abi=abi + ".0",
                archive_name=f"collection-image-generator-{version}.zip")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--field", choices=("version", "assembly_version", "target_abi", "archive_name", "repository"))
    args = parser.parse_args()
    metadata = read_release_metadata()
    print(metadata[args.field] if args.field else json.dumps(metadata, indent=2))
