import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("package_plugin", Path(__file__).with_name("package_plugin.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class PackageTests(unittest.TestCase):
    def test_archive_contains_only_our_assemblies_and_license(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            build = root / "build"
            build.mkdir()
            for name in ["CollectionImageGenerator.Plugin.dll", "CollectionImageGenerator.Core.dll", "SkiaSharp.dll", "MediaBrowser.Controller.dll"]:
                (build / name).write_bytes(b"test fixture, not a real assembly")
            (root / "LICENSE").write_text("test license")
            fonts = root / "src/CollectionImageGenerator.Core/Assets"
            fonts.mkdir(parents=True)
            (fonts / "OFL.txt").write_text("test font license")
            archive = module.package_plugin(build, root / "artifacts", root)
            with zipfile.ZipFile(archive) as z:
                names = set(z.namelist())
                self.assertIn("CollectionImageGenerator.Plugin.dll", names)
                self.assertIn("CollectionImageGenerator.Core.dll", names)
                self.assertIn("LICENSE", names)
                self.assertIn("THIRD-PARTY-NOTICES.txt", names)
                self.assertNotIn("SkiaSharp.dll", names)
                self.assertNotIn("MediaBrowser.Controller.dll", names)
    def test_missing_assembly_does_not_produce_archive(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "LICENSE").write_text("test license")
            with self.assertRaises(FileNotFoundError):
                module.package_plugin(root, root / "artifacts", root)

if __name__ == "__main__":
    unittest.main()
