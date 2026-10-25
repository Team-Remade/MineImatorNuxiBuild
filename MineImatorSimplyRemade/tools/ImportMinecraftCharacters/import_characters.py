"""Import Mine-imator models, textures, and animation loops from Minecraft data."""

import argparse
import json
import os
from pathlib import Path, PurePosixPath
from zipfile import ZipFile


MODEL_PREFIX = "assets/minecraft/models/"
TEXTURE_PREFIX = "assets/minecraft/textures/"
MODEL_TYPES = {
    "character": ("characters", "characters"),
    "equipment": ("equipment", "equipment"),
    "special_block": ("props", "special_blocks"),
}


def nodes(node):
    yield node
    for key in ("parts", "shapes"):
        for child in node.get(key, []):
            yield from nodes(child)


def model_files(character):
    result = {character["file"]} if "file" in character else set()
    for values in character.get("states", {}).values():
        result.update(value["file"] for value in values if "file" in value)
    return result


def selected_states(character, filename):
    defaults = dict(
        pair.split("=", 1)
        for pair in character.get("default_state", "").split(",")
        if pair
    )
    selected = []
    for key, values in character.get("states", {}).items():
        matches = [value for value in values if value.get("file") == filename]
        if not matches:
            matches = [
                value for value in values
                if value.get("file", character.get("file")) == filename
            ]
        if not matches:
            matches = values
        preferred = (Path(filename).stem, defaults.get(key))
        selected.append(next(
            (value for name in preferred for value in matches if value["value"] == name),
            matches[0],
        ))
    return selected


def apply_states(model, states, colors):
    for state in states:
        texture = state.get("texture")
        if isinstance(texture, str):
            model["texture"] = texture
        for node in nodes(model):
            if isinstance(texture, dict) and node.get("name") in texture:
                node["texture"] = texture[node["name"]]
            if node.get("name") in state.get("hide", []):
                node["visible"] = False
            description = node.get("description")
            if description in state.get("shape_hide", []):
                node["visible"] = False
            if description in state.get("shape_texture", {}):
                node["texture"] = state["shape_texture"][description]
            if description in state.get("shape_color", {}):
                color = state["shape_color"][description]
                node["color_blend"] = colors.get(color, color)


def armor_material_models(model, character, filename, colors):
    if character.get("name") != "armor":
        return {}
    groups = [
        values for values in character.get("states", {}).values()
        if any("shape_texture" in value for value in values)
    ]
    if not groups:
        return {}
    materials = [
        value["value"] for value in groups[0] if "shape_texture" in value
        and all(any(option["value"] == value["value"] and "shape_texture" in option
                    for option in values) for values in groups[1:])
    ]
    defaults = selected_states(character, filename)
    result = {}
    for material in materials:
        # Synchronize the piece states, not their Cartesian product.
        states = [
            next(value for value in values if value["value"] == material)
            if values in groups else default
            for values, default in zip(character["states"].values(), defaults)
        ]
        candidate = json.loads(json.dumps(model))
        apply_states(candidate, states, colors)
        textures = {key: reference for state in states
                    for key, reference in state.get("shape_texture", {}).items()}
        candidate["texture"] = next(
            (textures[node["description"]] for node in nodes(candidate)
             if node.get("description") in textures), candidate["texture"],
        )
        result[material] = candidate
    return result


def import_assets(archive_path, metadata_path, version_root, check=False, replace_existing=False):
    metadata = json.loads(metadata_path.read_text(encoding="utf-8-sig"))
    if metadata["version"] != version_root.name:
        raise ValueError("Metadata version must match the destination version directory.")
    colors = {
        f"{swatch['name']}:{color['name']}": color["value"]
        for swatch in metadata["swatches"] for color in swatch["colors"]
    }
    generated = {}
    counts = {folder: 0 for folder, _ in MODEL_TYPES.values()}
    skipped = variant_count = loop_count = source_only = 0

    with ZipFile(archive_path) as archive:
        def texture_path(reference, model_dir):
            relative = PurePosixPath(reference)
            if relative.is_absolute() or ".." in relative.parts or "\\" in reference or ":" in reference:
                raise ValueError(f"Invalid archive texture reference: {reference}")
            relative = relative.with_suffix(".png")
            archive_name = TEXTURE_PREFIX + str(relative)
            target = version_root / "textures" / Path(*relative.parts)
            if replace_existing or not target.exists():
                generated[target] = archive.read(archive_name)
            return os.path.relpath(target, model_dir)

        for entry in sorted(archive.namelist()):
            if not entry.startswith(MODEL_PREFIX) or not entry.endswith(".mimodel"):
                continue
            relative = PurePosixPath(entry.removeprefix(MODEL_PREFIX))
            if relative.is_absolute() or ".." in relative.parts or "\\" in entry or ":" in entry:
                raise ValueError(f"Invalid archive model path: {entry}")
            model_type = relative.parts[0]
            if model_type not in MODEL_TYPES:
                continue
            filename = PurePosixPath(entry).name
            if model_type == "special_block" and filename in ("armor.mimodel", "armor_baby.mimodel"):
                model_type = "equipment"
            folder, metadata_key = MODEL_TYPES[model_type]
            model_dir = version_root / folder / "vanilla" / Path(filename).stem
            model_path = model_dir / filename
            if model_path.exists() and not replace_existing:
                skipped += 1
                continue
            entries = metadata.get(metadata_key, [])
            if model_type == "equipment":
                entries = entries + metadata.get("special_blocks", [])
            matches = [c for c in entries if filename in model_files(c)]
            if len(matches) > 1:
                raise ValueError(f"Ambiguous metadata for {entry}")
            character = matches[0] if matches else {}
            model = json.loads(archive.read(entry))
            states = selected_states(character, filename)
            apply_states(model, states, colors)
            materials = (armor_material_models(model, character, filename, colors)
                         if model_type == "equipment" else {})
            models = {filename: model}
            variants = [{"name": "Default", "model": filename}] if materials else []
            for material, candidate in materials.items():
                if candidate == model:
                    continue
                variant_filename = f"{Path(filename).stem}_{material}.mimodel"
                models[variant_filename] = candidate
                variants.append({"name": material.replace("_", " ").title(),
                                 "model": variant_filename})
            references = [
                reference for candidate in models.values() for node in nodes(candidate)
                for reference in (
                    *(node.get(key) for key in ("texture", "texture_material", "texture_normal")),
                    *node.get("textures", {}).values(),
                ) if reference
            ]
            missing = [
                reference for reference in references
                if TEXTURE_PREFIX + str(PurePosixPath(reference).with_suffix(".png")) not in archive.namelist()
                and not (version_root / "textures" / (reference + ".png")).exists()
            ]
            if missing:
                source_path = version_root / "source_models" / f"{model_type}_{filename}"
                if replace_existing or not source_path.exists():
                    generated[source_path] = archive.read(entry)
                print(f"Source-only {filename}: missing texture(s): {', '.join(sorted(set(missing)))}")
                source_only += 1
                continue
            for candidate in models.values():
                for node in nodes(candidate):
                    for key in ("texture", "texture_material", "texture_normal"):
                        if node.get(key):
                            node[key] = texture_path(node[key], model_dir)
                    for key, reference in node.get("textures", {}).items():
                        node["textures"][key] = texture_path(reference, model_dir)

            if not materials:
                variants = [{"name": "Default", "file": model["texture"]}]
            for values in (() if materials else character.get("states", {}).values()):
                for value in values:
                    if not isinstance(value.get("texture"), str):
                        continue
                    if any("file" in option for option in values) and value.get("file", character.get("file")) != filename:
                        continue
                    # Texture dropdowns cannot switch geometry or accessory states.
                    candidate = json.loads(archive.read(entry))
                    candidate_states = [
                        value if state in values else state for state in states
                    ]
                    apply_states(candidate, candidate_states, colors)
                    baseline = json.loads(archive.read(entry))
                    apply_states(baseline, states, colors)
                    candidate.pop("texture", None)
                    baseline.pop("texture", None)
                    if candidate != baseline:
                        continue
                    path = texture_path(value["texture"], model_dir)
                    if any(variant["file"] == path for variant in variants):
                        continue
                    variants.append({
                        "name": value["value"].replace("_", " ").title(),
                        "file": path,
                    })
            variants.append({"name": "Custom", "custom": True})
            manifest = {"default": model["texture"], "variants": variants}
            for name, value in (*models.items(), ("textures.nux", manifest)):
                generated[model_dir / name] = (
                    json.dumps(value, indent=2, ensure_ascii=True) + "\n"
                ).encode("utf-8")
            counts[folder] += 1
            variant_count += len(variants) - 1

        for entry in sorted(archive.namelist()):
            if not entry.startswith(MODEL_PREFIX) or not entry.endswith(".miframes"):
                continue
            relative = PurePosixPath(entry.removeprefix(MODEL_PREFIX))
            if relative.is_absolute() or ".." in relative.parts or "\\" in entry or ":" in entry:
                raise ValueError(f"Invalid archive animation path: {entry}")
            target = version_root / "animations" / "vanilla" / Path(*relative.parts)
            if target.exists() and not replace_existing:
                skipped += 1
                continue
            data = archive.read(entry)
            json.loads(data)
            generated[target] = data
            loop_count += 1

    for path, data in generated.items():
        if check:
            if not path.exists() or path.read_bytes() != data:
                raise ValueError(f"Missing or outdated imported asset: {path}")
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            if path.exists() and not replace_existing:
                continue
            if not path.exists() or path.read_bytes() != data:
                path.write_bytes(data)
    print(f"{'Verified' if check else 'Imported'} "
          f"{', '.join(f'{count} {folder}' for folder, count in counts.items())}, "
          f"{loop_count} animation loops, {variant_count} fixed texture choices; "
          f"{skipped} existing assets skipped, {source_only} source-only models.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--metadata", type=Path)
    parser.add_argument("--version-root", type=Path)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--replace-existing", action="store_true",
                        help="Replace existing models, manifests, shared textures, and loops.")
    args = parser.parse_args()
    root = args.version_root or (
        Path(__file__).resolve().parents[2]
        / "data" / "minecraft" / "versions" / args.archive.stem
    )
    import_assets(
        args.archive, args.metadata or args.archive.with_suffix(".midata"),
        root, args.check, args.replace_existing,
    )
