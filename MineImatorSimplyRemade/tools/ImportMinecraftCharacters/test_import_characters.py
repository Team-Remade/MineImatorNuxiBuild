import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from zipfile import ZipFile

from import_characters import import_assets, nodes


MATERIALS = ("leather", "chainmail", "copper", "iron", "gold", "diamond", "netherite")


def armor_metadata():
    states = {"variant": [{"value": "armor"},
                          {"value": "armor_baby", "file": "armor_baby.mimodel"}]}
    for piece in ("helmet", "chestplate", "leggings", "boots"):
        folder = "humanoid_leggings" if piece == "leggings" else "humanoid"
        states[piece] = [{"value": "none", "shape_hide": [piece, piece + "_baby"]}] + [
            {"value": material, "shape_texture": {
                piece: f"entity/equipment/{folder}/{material}",
                piece + "_baby": f"entity/equipment/humanoid_baby/{material}",
            }} for material in MATERIALS + (("turtle_scute",) if piece == "helmet" else ())
        ]
    return {"name": "armor", "file": "armor.mimodel", "states": states,
            "default_state": "variant=armor,helmet=iron,chestplate=iron,leggings=iron,boots=iron"}


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

    def add_armor(self, missing_texture=None, model_type="special_block"):
        metadata = json.loads(self.metadata.read_text())
        # Source .midata files group equipment metadata under special_blocks.
        metadata["special_blocks"].extend(metadata.pop("equipment"))
        metadata["special_blocks"].append(armor_metadata())
        self.metadata.write_text(json.dumps(metadata), encoding="utf-8")
        with ZipFile(self.archive, "a") as archive:
            for name in ("armor", "armor_baby"):
                folder = "humanoid_baby" if name.endswith("_baby") else "humanoid"
                suffix = "_baby" if name.endswith("_baby") else ""
                archive.writestr(f"assets/minecraft/models/{model_type}/{name}.mimodel", json.dumps({
                    "name": name, "texture": f"entity/equipment/{folder}/iron",
                    "parts": [{"name": "body", "shapes": [
                        {"description": piece + suffix, "from": [0, 0, 0], "to": [1, 2, 3]}
                        for piece in ("helmet", "chestplate", "leggings", "boots")
                    ]}],
                }))
            for folder in ("humanoid", "humanoid_leggings", "humanoid_baby"):
                for material in MATERIALS:
                    reference = f"entity/equipment/{folder}/{material}"
                    if reference != missing_texture:
                        archive.writestr(f"assets/minecraft/textures/{reference}.png", b"texture")

    def test_armor_imports_synchronized_material_models_and_preserves_geometry(self):
        self.add_armor()
        self.run_import()
        for name in ("armor", "armor_baby"):
            directory = self.version / "equipment" / "vanilla" / name
            self.assert_material_models(directory, name)
        before = self.snapshot()
        self.run_import(check=True, replace_existing=True)
        self.assertEqual(self.snapshot(), before)
        self.run_import()
        self.assertEqual(self.snapshot(), before)
        self.assertFalse((self.version / "props" / "vanilla" / "armor").exists())

    def test_armor_equipment_archive_layout_is_also_supported(self):
        self.add_armor(model_type="equipment")
        self.run_import()
        for name in ("armor", "armor_baby"):
            self.assert_material_models(self.version / "equipment" / "vanilla" / name, name)

    def test_armor_metadata_in_special_blocks_with_equipment_catalog(self):
        self.add_armor()
        metadata = json.loads(self.metadata.read_text())
        metadata["equipment"] = []
        self.metadata.write_text(json.dumps(metadata), encoding="utf-8")
        self.run_import()
        for name in ("armor", "armor_baby"):
            self.assert_material_models(self.version / "equipment" / "vanilla" / name, name)

    def test_armor_variant_models_are_preserved_checked_and_replaced(self):
        self.add_armor()
        self.run_import()
        model = self.version / "equipment" / "vanilla" / "armor" / "armor_diamond.mimodel"
        original = model.read_bytes()
        model.write_bytes(b"custom variant")
        self.run_import()
        self.assertEqual(model.read_bytes(), b"custom variant")
        with self.assertRaisesRegex(ValueError, "Missing or outdated"):
            self.run_import(check=True, replace_existing=True)
        self.assertEqual(model.read_bytes(), b"custom variant")
        self.run_import(replace_existing=True)
        self.assertEqual(model.read_bytes(), original)

    def assert_material_models(self, directory, name):
        manifest = json.loads((directory / "textures.nux").read_text())
        self.assertEqual([variant["name"] for variant in manifest["variants"]],
                         ["Default", "Leather", "Chainmail", "Copper", "Gold", "Diamond", "Netherite", "Custom"])
        self.assertEqual(manifest["variants"][-1], {"name": "Custom", "custom": True})
        baseline = json.loads((directory / f"{name}.mimodel").read_text())
        self.assertEqual(manifest["default"], baseline["texture"])
        for variant in manifest["variants"][:-1]:
            self.assertNotIn("file", variant)
            model = json.loads((directory / variant["model"]).read_text())
            self.assertEqual(model["name"], name)
            material = "iron" if variant["name"] == "Default" else variant["name"].lower()
            root_folder = "humanoid_baby" if name.endswith("_baby") else "humanoid"
            self.assertEqual(Path(model["texture"].replace("\\", "/")).parts[-2:],
                             (root_folder, material + ".png"))
            for node in nodes(model):
                if "texture" not in node:
                    continue
                reference = Path(node["texture"].replace("\\", "/"))
                self.assertTrue((directory / reference).is_file(), str(reference))
                folder = ("humanoid_baby" if name.endswith("_baby") else
                          "humanoid_leggings" if node.get("description") == "leggings" else "humanoid")
                self.assertEqual(reference.parts[-2:], (folder, material + ".png"))
            geometry = json.loads(json.dumps(model))
            original_geometry = json.loads(json.dumps(baseline))
            for candidate in (geometry, original_geometry):
                for node in nodes(candidate):
                    node.pop("texture", None)
            self.assertEqual(geometry, original_geometry)
        self.assertEqual(len(list(directory.glob("*.mimodel"))), 7)

    def test_missing_armor_variant_texture_does_not_write_partial_set(self):
        self.add_armor(missing_texture="entity/equipment/humanoid_leggings/diamond")
        self.run_import()
        self.assertFalse((self.version / "equipment" / "vanilla" / "armor").exists())
        self.assertTrue((self.version / "source_models" / "equipment_armor.mimodel").is_file())
        self.assertTrue((self.version / "equipment" / "vanilla" / "armor_baby" / "textures.nux").is_file())

    def test_shipped_armor_materials_have_existing_textures_and_unchanged_geometry(self):
        root = Path(__file__).resolve().parents[2] / "data" / "minecraft" / "versions" / "26.3"
        for name in ("armor", "armor_baby"):
            with self.subTest(name=name):
                self.assert_material_models(root / "equipment" / "vanilla" / name, name)


if __name__ == "__main__":
    unittest.main()
