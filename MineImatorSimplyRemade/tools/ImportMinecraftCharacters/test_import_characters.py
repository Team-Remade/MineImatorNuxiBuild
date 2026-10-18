import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from zipfile import ZipFile

from import_characters import import_assets


class ImportAssetsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.version = self.root / "26.3"
        self.archive = self.root / "26.3.zip"
        self.metadata = self.root / "26.3.midata"
        self.metadata.write_text(json.dumps({
            "version": "26.3", "swatches": [],
            "characters": [{"name": "steve", "file": "steve.mimodel"}],
            "equipment": [{"name": "elytra", "file": "elytra.mimodel"}],
            "special_blocks": [{"name": "chest", "file": "chest.mimodel"}],
        }), encoding="utf-8")
        with ZipFile(self.archive, "w") as archive:
            for folder, name in (
                ("character", "steve"), ("equipment", "elytra"), ("special_block", "chest"),
            ):
                archive.writestr(f"assets/minecraft/models/{folder}/{name}.mimodel", json.dumps({
                    "name": name, "texture": "entity/test", "parts": [],
                }))
            archive.writestr("assets/minecraft/textures/entity/test.png", b"texture")
            archive.writestr("assets/minecraft/models/character/loops/steve_walk.miframes",
                             '{"format":24,"keyframes":[]}')

    def run_import(self, **options):
        with contextlib.redirect_stdout(io.StringIO()):
            import_assets(self.archive, self.metadata, self.version, **options)

    def snapshot(self):
        return {path.relative_to(self.version): path.read_bytes()
                for path in self.version.rglob("*") if path.is_file()}

    def test_imports_all_supported_types_and_animation_bytes(self):
        self.run_import()
        for folder, name in (("characters", "steve"), ("equipment", "elytra"), ("props", "chest")):
            model = self.version / folder / "vanilla" / name / f"{name}.mimodel"
            data = json.loads(model.read_text())
            self.assertTrue((model.parent / data["texture"]).exists())
            self.assertTrue((model.parent / "textures.nux").exists())
        loop = self.version / "animations" / "vanilla" / "character" / "loops" / "steve_walk.miframes"
        self.assertEqual(loop.read_bytes(), b'{"format":24,"keyframes":[]}')

    def test_existing_assets_are_untouched_by_default(self):
        self.run_import()
        for path in self.version.rglob("*"):
            if path.is_file():
                path.write_bytes(b"user customization")
        before = self.snapshot()
        self.run_import()
        self.assertEqual(self.snapshot(), before)

    def test_replace_existing_refreshes_models_manifests_textures_and_loops(self):
        self.run_import()
        original = self.snapshot()
        for path in self.version.rglob("*"):
            if path.is_file():
                path.write_bytes(b"user customization")
        self.run_import(replace_existing=True)
        self.assertEqual(self.snapshot(), original)

    def test_existing_manifest_is_preserved_when_model_is_missing(self):
        directory = self.version / "equipment" / "vanilla" / "elytra"
        directory.mkdir(parents=True)
        manifest = directory / "textures.nux"
        manifest.write_bytes(b"custom manifest")
        self.run_import()
        self.assertEqual(manifest.read_bytes(), b"custom manifest")
        self.assertTrue((directory / "elytra.mimodel").exists())

    def test_strict_check_detects_customized_assets_without_overwriting(self):
        self.run_import()
        model = next(self.version.rglob("*.mimodel"))
        model.write_bytes(b"custom model")
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "Missing or outdated"):
            self.run_import(check=True, replace_existing=True)
        self.assertEqual(self.snapshot(), before)

    def test_check_is_read_only_and_detects_missing_assets(self):
        self.run_import()
        before = self.snapshot()
        self.run_import(check=True, replace_existing=True)
        self.assertEqual(self.snapshot(), before)
        loop = next(self.version.rglob("*.miframes"))
        loop.unlink()
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "Missing or outdated"):
            self.run_import(check=True)
        self.assertEqual(self.snapshot(), before)

    def test_missing_texture_model_is_retained_as_source_only(self):
        with ZipFile(self.archive, "a") as archive:
            archive.writestr("assets/minecraft/models/special_block/bed.mimodel",
                             '{"name":"bed","texture":"entity/bed/missing","parts":[]}')
        self.run_import()
        self.assertTrue((self.version / "source_models" / "special_block_bed.mimodel").exists())
        self.assertFalse((self.version / "props" / "vanilla" / "bed").exists())

    def test_unsafe_archive_paths_are_rejected_before_writing(self):
        with ZipFile(self.archive, "a") as archive:
            archive.writestr("assets/minecraft/models/character/../escape.miframes", "{}")
        with self.assertRaisesRegex(ValueError, "Invalid archive animation path"):
            self.run_import()
        self.assertFalse(self.version.exists())


if __name__ == "__main__":
    unittest.main()
