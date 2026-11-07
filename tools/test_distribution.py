"""Distribution contracts; all archives and HTTP responses here are test fixtures."""
import hashlib
import importlib.util
import inspect
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile
from unittest import mock
import io
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ET.parse(ROOT / "src/CollectionImageGenerator.Plugin/CollectionImageGenerator.Plugin.csproj").getroot()
VERSION = PROJECT.findtext(".//Version")
ASSEMBLY_VERSION = PROJECT.findtext(".//AssemblyVersion")
TARGET_ABI = next(p.attrib["Version"] for p in PROJECT.findall(".//PackageReference") if p.attrib["Include"] == "Jellyfin.Controller") + ".0"
TAG = "v" + VERSION
ARCHIVE_NAME = f"collection-image-generator-{VERSION}.zip"


class DistributionTests(unittest.TestCase):
    def tool(self, name):
        path = Path(__file__).with_name(name + ".py")
        self.assertTrue(path.is_file(), f"Missing distribution tool: {name}")
        spec = importlib.util.spec_from_file_location(name, path)
        module = importlib.util.module_from_spec(spec)
        sys.modules[name] = module
        spec.loader.exec_module(module)
        return module

    def archive(self, directory, payload=b"Synthetic DLL fixture, not an assembly"):
        path = Path(directory) / ARCHIVE_NAME
        with zipfile.ZipFile(path, "w") as archive:
            for name in ("CollectionImageGenerator.Plugin.dll", "CollectionImageGenerator.Core.dll", "LICENSE", "THIRD-PARTY-NOTICES.txt"):
                archive.writestr(name, payload)
        return path

    def test_manifest_has_jellyfin_identity_abi_versioned_url_and_md5(self):
        tool = self.tool("generate_manifest")
        with tempfile.TemporaryDirectory() as directory:
            archive = self.archive(directory)
            result = tool.build_manifest(archive, TAG, timestamp="2026-11-07T00:00:00Z")
            self.assertEqual(1, len(result))
            package = result[0]
            self.assertEqual("f8a5df17-f0b4-4eab-9144-a46ea55c2b28", package["guid"])
            self.assertEqual("Collection Image Generator", package["name"])
            version = package["versions"][0]
            self.assertEqual(ASSEMBLY_VERSION, version["version"])
            self.assertEqual(TARGET_ABI, version["targetAbi"])
            self.assertEqual(f"https://github.com/everyonesoftware/collection-image-generator-jellyfin-plugin/releases/download/{TAG}/{ARCHIVE_NAME}", version["sourceUrl"])
            self.assertEqual(hashlib.md5(archive.read_bytes(), usedforsecurity=False).hexdigest(), version["checksum"])
            self.assertEqual("2026-11-07T00:00:00Z", version["timestamp"])

    def test_manifest_rejects_wrong_tag_and_unexpected_archive_contents(self):
        tool = self.tool("generate_manifest")
        with tempfile.TemporaryDirectory() as directory:
            archive = self.archive(directory)
            for tag in ("v9.9.9", VERSION, "v0.1.0-rc1"):
                with self.subTest(tag=tag), self.assertRaises(ValueError):
                    tool.build_manifest(archive, tag)
            with zipfile.ZipFile(archive, "a") as zipped:
                zipped.writestr("../outside.dll", b"synthetic unexpected file")
            with self.assertRaises(ValueError):
                tool.build_manifest(archive, TAG)

    def test_manifest_preserves_history_and_same_version_publication(self):
        tool = self.tool("generate_manifest")
        self.assertIn("previous", inspect.signature(tool.build_manifest).parameters)
        with tempfile.TemporaryDirectory() as directory:
            archive = self.archive(directory)
            old = tool.build_manifest(archive, TAG, timestamp="2026-11-06T00:00:00Z")
            prior = dict(old[0]["versions"][0], version="0.0.9.0", targetAbi="12.0.0.0",
                         checksum="a" * 32, sourceUrl=old[0]["versions"][0]["sourceUrl"].replace(VERSION, "0.0.9"))
            old[0]["versions"].append(prior)
            result = tool.build_manifest(archive, TAG, previous=old, timestamp="2026-11-07T00:00:00Z")
            self.assertEqual(old, result)
            self.assertEqual([ASSEMBLY_VERSION, "0.0.9.0"], [v["version"] for v in result[0]["versions"]])

    def test_manifest_refuses_rewriting_releases_or_losing_untrusted_history(self):
        tool = self.tool("generate_manifest")
        with tempfile.TemporaryDirectory() as directory:
            archive = self.archive(directory)
            initial = tool.build_manifest(archive, TAG)
            variants = []
            for field, value in (("checksum", "f" * 32), ("sourceUrl", "https://example.invalid/replaced.zip"), ("targetAbi", "11.0.0.0"), ("version", "9.0.0.0")):
                changed = json.loads(json.dumps(initial))
                changed[0]["versions"][0][field] = value
                variants.append(changed)
            foreign = json.loads(json.dumps(initial))
            foreign[0]["guid"] = "00000000-0000-0000-0000-000000000001"
            duplicate = json.loads(json.dumps(initial))
            duplicate[0]["versions"] *= 2
            variants += [foreign, duplicate, {}, [None], [{"guid": initial[0]["guid"]}]]
            for previous in variants:
                with self.subTest(previous=previous), self.assertRaises(ValueError):
                    tool.build_manifest(archive, TAG, previous=previous)

    def test_manifest_cli_writes_a_valid_catalog_and_refuses_missing_history(self):
        with tempfile.TemporaryDirectory() as directory:
            archive = self.archive(directory)
            output = Path(directory) / "catalog/manifest.json"
            command = [sys.executable, str(ROOT / "tools/generate_manifest.py"), "--archive", str(archive),
                       "--tag", TAG, "--output", str(output), "--changelog", "Synthetic release notes"]
            completed = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(0, completed.returncode, completed.stderr)
            self.assertTrue(output.is_file(), "Manifest CLI did not produce its promised output")
            manifest = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual("Synthetic release notes", manifest[0]["versions"][0]["changelog"])
            before = output.read_bytes()
            completed = subprocess.run(command + ["--previous", str(Path(directory) / "missing.json")], capture_output=True, text=True)
            self.assertNotEqual(0, completed.returncode)
            self.assertEqual(before, output.read_bytes())

    def test_public_verifier_downloads_manifest_and_exact_zip_bytes(self):
        generator = self.tool("generate_manifest")
        verifier = self.tool("verify_repository")
        with tempfile.TemporaryDirectory() as directory:
            archive = self.archive(directory)
            manifest = generator.build_manifest(archive, TAG)
            url = "https://raw.githubusercontent.com/everyonesoftware/collection-image-generator-jellyfin-plugin/repository/manifest.json"
            responses = {url: json.dumps(manifest).encode(), manifest[0]["versions"][0]["sourceUrl"]: archive.read_bytes()}
            requested = []
            def download(address, limit):
                requested.append(address)
                self.assertLessEqual(len(responses[address]), limit)
                return responses[address]
            result = verifier.verify_manifest_url(url, ASSEMBLY_VERSION, download=download)
            self.assertEqual(list(responses), requested)
            self.assertEqual(ASSEMBLY_VERSION, result["version"])
            self.assertEqual(hashlib.sha256(archive.read_bytes()).hexdigest(), result["sha256"])
            responses[manifest[0]["versions"][0]["sourceUrl"]] = archive.read_bytes() + b"changed bytes"
            with self.assertRaises(ValueError):
                verifier.verify_manifest_url(url, ASSEMBLY_VERSION, download=download)

    def test_package_version_tracks_project_metadata_instead_of_a_literal(self):
        self.tool("release_metadata")
        with mock.patch("release_metadata.read_release_metadata", return_value={"version": "0.2.0"}):
            package = self.tool("package_plugin")
        self.assertEqual("0.2.0", package.VERSION)

    def test_public_fetch_is_anonymous_https_and_bounded(self):
        verifier = self.tool("verify_repository")
        url = "https://example.invalid/public.zip"
        class Response(io.BytesIO):
            def geturl(self):
                return url
        with mock.patch.object(verifier, "urlopen", return_value=Response(b"fixture")) as opened:
            self.assertEqual(b"fixture", verifier.fetch_bytes(url, 7))
            request = opened.call_args.args[0]
            self.assertFalse(request.has_header("Authorization"))
            self.assertFalse(request.has_header("Cookie"))
            self.assertEqual(30, opened.call_args.kwargs["timeout"])
        with mock.patch.object(verifier, "urlopen", return_value=Response(b"too large")):
            with self.assertRaises(ValueError):
                verifier.fetch_bytes(url, 2)
        with self.assertRaises(ValueError):
            verifier.fetch_bytes("http://example.invalid/insecure", 20)

    def test_release_metadata_refuses_identity_or_version_drift(self):
        tool = self.tool("release_metadata")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for relative in ("catalog.json", "src/CollectionImageGenerator.Plugin/CollectionImageGenerator.Plugin.csproj", "src/CollectionImageGenerator.Plugin/Plugin.cs"):
                target = root / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes((ROOT / relative).read_bytes())
            project = root / "src/CollectionImageGenerator.Plugin/CollectionImageGenerator.Plugin.csproj"
            original = project.read_text(encoding="utf-8")
            project.write_text(original.replace(f"<AssemblyVersion>{ASSEMBLY_VERSION}</AssemblyVersion>", "<AssemblyVersion>99.0.0.0</AssemblyVersion>"), encoding="utf-8")
            with self.assertRaises(ValueError):
                tool.read_release_metadata(root)
            project.write_text(original, encoding="utf-8")
            catalog = root / "catalog.json"
            metadata = json.loads(catalog.read_text(encoding="utf-8"))
            metadata["guid"] = "00000000-0000-0000-0000-000000000001"
            catalog.write_text(json.dumps(metadata), encoding="utf-8")
            with self.assertRaises(ValueError):
                tool.read_release_metadata(root)

    def test_verifier_refuses_missing_version_wrong_abi_and_foreign_catalog(self):
        generator = self.tool("generate_manifest")
        verifier = self.tool("verify_repository")
        with tempfile.TemporaryDirectory() as directory:
            archive = self.archive(directory)
            original = generator.build_manifest(archive, TAG)
            for change in ("missing-version", "abi", "identity"):
                manifest = json.loads(json.dumps(original))
                if change == "missing-version":
                    manifest[0]["versions"][0]["version"] = "0.0.1.0"
                    manifest[0]["versions"][0]["sourceUrl"] = manifest[0]["versions"][0]["sourceUrl"].replace(VERSION, "0.0.1")
                elif change == "abi":
                    manifest[0]["versions"][0]["targetAbi"] = "99.0.0.0"
                else:
                    manifest[0]["guid"] = "00000000-0000-0000-0000-000000000001"
                with self.subTest(change=change), self.assertRaises(ValueError):
                    verifier.verify_manifest_url("https://example.invalid/manifest.json", ASSEMBLY_VERSION,
                                                 download=lambda url, limit: json.dumps(manifest).encode())

    def test_metadata_uses_project_versions_and_matches_plugin_identity(self):
        metadata = self.tool("release_metadata").read_release_metadata()
        self.assertEqual(VERSION, metadata["version"])
        self.assertEqual(ASSEMBLY_VERSION, metadata["assembly_version"])
        self.assertEqual(TARGET_ABI, metadata["target_abi"])
        self.assertEqual(ARCHIVE_NAME, metadata["archive_name"])
        self.assertEqual("f8a5df17-f0b4-4eab-9144-a46ea55c2b28", metadata["guid"])
        self.assertEqual("Collection Image Generator", metadata["name"])


if __name__ == "__main__":
    unittest.main()
