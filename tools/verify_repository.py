"""Verify a public repository and its install ZIP without GitHub authentication."""
import argparse
import hashlib
import io
import json
from urllib.parse import urlparse
from urllib.request import Request, urlopen

from generate_manifest import MAX_ARCHIVE_BYTES, validate_archive, validate_manifest
from release_metadata import read_release_metadata, version_parts

MAX_MANIFEST_BYTES = 2 * 1024 * 1024


def fetch_bytes(url, limit):
    if urlparse(url).scheme != "https":
        raise ValueError("Public distribution URLs must use HTTPS")
    request = Request(url, headers={"User-Agent": "CollectionImageGenerator-PublicVerifier", "Accept": "*/*"})
    # Deliberately no gh CLI, token, cookies, or Authorization header.
    with urlopen(request, timeout=30) as response:
        if urlparse(response.geturl()).scheme != "https":
            raise ValueError("Refusing a non-HTTPS redirect")
        data = response.read(limit + 1)
    if len(data) > limit:
        raise ValueError("Public download exceeds its size limit")
    return data


def verify_manifest_url(manifest_url, version, download=fetch_bytes):
    metadata = read_release_metadata()
    version_parts(version, 4)
    manifest = json.loads(download(manifest_url, MAX_MANIFEST_BYTES))
    versions = validate_manifest(manifest, metadata)
    matches = [entry for entry in versions if entry["version"] == version]
    if len(matches) != 1:
        raise ValueError(f"Expected published version {version} was not found")
    entry = matches[0]
    if version != metadata["assembly_version"] or entry["targetAbi"] != metadata["target_abi"]:
        raise ValueError("Published release version/ABI differs from this checkout")
    archive = download(entry["sourceUrl"], MAX_ARCHIVE_BYTES)
    checksum = hashlib.md5(archive, usedforsecurity=False).hexdigest()
    if checksum.lower() != entry["checksum"].lower():
        raise ValueError("Public ZIP bytes do not match Jellyfin's manifest checksum")
    validate_archive(io.BytesIO(archive))
    return dict(name=manifest[0]["name"], version=version, targetAbi=entry["targetAbi"],
                sourceUrl=entry["sourceUrl"], checksum=checksum,
                sha256=hashlib.sha256(archive).hexdigest(), bytes=len(archive))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest-url", required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    print(json.dumps(verify_manifest_url(args.manifest_url, args.version), indent=2))
