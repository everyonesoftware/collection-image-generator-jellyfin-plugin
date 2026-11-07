"""Build a minimal install archive; Jellyfin supplies shared dependencies."""
import argparse
import hashlib
from pathlib import Path
import zipfile

from release_metadata import read_release_metadata

VERSION = read_release_metadata()["version"]
ASSEMBLIES = ("CollectionImageGenerator.Plugin.dll", "CollectionImageGenerator.Core.dll")


def package_plugin(build_dir, output_dir, root):
    build_dir, output_dir, root = map(Path, (build_dir, output_dir, root))
    for name in ASSEMBLIES:
        if not (build_dir / name).is_file():
            raise FileNotFoundError(f"Build the Release plugin first: {build_dir / name}")
    license_file = root / "LICENSE"
    font_licenses = sorted((root / "src/CollectionImageGenerator.Core").rglob("OFL.txt"))
    if not font_licenses:
        raise FileNotFoundError("Bundled font OFL.txt license is required")
    notices = "Bundled font license\n\n" + "\n\n".join(p.read_text(encoding="utf-8") for p in font_licenses)
    output_dir.mkdir(parents=True, exist_ok=True)
    target = output_dir / f"collection-image-generator-{VERSION}.zip"
    pending = target.with_suffix(".zip.tmp")
    try:
        with zipfile.ZipFile(pending, "w", zipfile.ZIP_DEFLATED) as archive:
            for name in ASSEMBLIES:
                archive.write(build_dir / name, name)
            archive.write(license_file, "LICENSE")
            archive.writestr("THIRD-PARTY-NOTICES.txt", notices)
        pending.replace(target)
    finally:
        pending.unlink(missing_ok=True)
    checksum = hashlib.sha256(target.read_bytes()).hexdigest()
    target.with_suffix(".zip.sha256").write_text(f"{checksum}  {target.name}\n", encoding="utf-8")
    return target


if __name__ == "__main__":
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build-dir", type=Path, default=root / "src/CollectionImageGenerator.Plugin/bin/Release/net10.0")
    parser.add_argument("--output", type=Path, default=root / "artifacts")
    args = parser.parse_args()
    print(package_plugin(args.build_dir, args.output, root))
