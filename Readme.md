# Mine Imator Nuxi

For all your Minecraft animation needs!

## Dev environment

Requirements

- Dotnet 10.0+
- A C++ compiler (MSVC, GCC, or Clang)
- OpenGL 3.3+ compatible graphics driver

## Setup

1. Clone the repo
2. Run `cd ./MineImatorNuxiBuild`
3. Run `dotnet restore`
4. Run `dotnet build`
5. Run `cd ./MineImatorSimplyRemade`
6. Run `dotnet run`

## Biome colors

Preset climate and color values are based on the
[Java 26.3 biome data](https://github.com/misode/mcmeta/blob/26.3-summary/data/worldgen/biome/data.json)
and the bundled grass/foliage colormaps.

## Procedural 3D effects

Open **Spawn > Effects** and choose **Lightning**, **Ring / Portal**, or
**Procedural Effect**. These are editable scene objects, not baked animations.
The presets share a path -> fractal noise -> branches -> tube geometry pipeline.

- In **Properties > Procedural Effect**, choose a **Line**, **Ring**, or **Helix**
  path. Line and helix endpoints are local-space offsets from the object's origin;
  rings lie in local XY. Move, rotate, scale, or parent the object normally.
- Change tube width, taper, path resolution, noise detail, seed, and branching.
  Set noise and branches to zero for clean beams, rings, or coils.
- **Refresh Rate** controls shape changes per timeline second; zero freezes the
  shape. **Smooth Motion** blends between shapes instead of snapping.
  Playback, backward scrubbing, previews, camera feeds, and export use timeline
  time rather than frame-to-frame random simulation.
- Animate **Path Reveal** from 0 to 1 to grow the effect. All effect controls can
  be keyframed using auto-key or their right-click menus. Seed, shape, resolution,
  branch count, and motion mode use stepped interpolation.
- Use the existing **Materials** controls for color, emission, transparency,
  textures, and blend modes. Enable the scene's **Glow** setting for a halo.
  Effects start emissive and do not cast shadows by default.
- Save custom configurations to the existing object library or export as
  `.nxObj`. Settings and keyframes survive project saves, duplication, and undo.

The initial system is a parameter-based path generator, not a node graph or a
physics simulation. It does not yet support surface-following, collision,
object-target endpoints, arbitrary drawn splines, or motion-history trails.
Geometry is bounded to 128 path segments, 12 tube sides, and 16 single-level
branches (at most 84,168 vertices per effect). Unchanged evaluations reuse their
uploaded mesh; animated changes use the existing mesh-upload pipeline.
The experimental Veldrid renderer can draw the geometry, but retains its existing
lighting/post-processing limitations described below.

### Research behind the approach

- [Conn Buranicz: Procedural Lightning](https://www.connburanicz.com/procedural-lightning)
  describes controllable source/target paths, randomized offsets, branching,
  and procedural strand geometry. Camera-facing ribbons/quads are economical;
  tubes cost more geometry but remain volumetric from every camera angle.
- [Jettelly: Draw Lightning Generators](https://jettelly.com/blog/draw-lightning-generators-procedural-lightning-with-geometry-nodes-in-blender)
  describes curve-driven lightning and the reuse of generated curves for other
  procedural structures. This motivates separating path generation from tube
  construction rather than introducing a lightning-only renderer.

This implementation uses original, seed-hashed multiscale displacement, bounded
branching, and parallel-transport tube frames. It does not embed either source's
code or assets. Stateless timeline evaluation makes every export frame repeatable.

Regression checks (custom executable test harness; no test adapter required):

```powershell
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --texture-sheets
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --texture-sheets-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --backups
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --modifiers
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --effects-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --particles-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --emissive-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --block-rotation-graphics
```

The `--backups` command checks preference defaults/persistence, timed snapshots,
retention, failure safety, and preservation of unsaved edits without a GPU.

The `--texture-sheets` command checks import validation, exact rectangular tile
pixels and alpha, project/object persistence, undo, ImGui controls, and live tile-cut overlays.
The `--texture-sheets-graphics` command requires OpenGL 3.3 and checks uploaded
tile dimensions, pixels, alpha, the existing material orientation rules, and
the dialog's image preview, orientation, transparency, and texture cleanup.

The `--effects-graphics` command requires an OpenGL 3.3 desktop session and checks real rendered
pixels, animation/seek reproducibility, emission, and empty-mesh rebuilding.
The `--particles-graphics` command also requires OpenGL 3.3 and checks particle sprite image orientation,
textures, animation frames, opacity, camera-facing rotation, and block particles.
The `--emissive-graphics` command requires OpenGL 3.3 and checks auto-emissive source positions in modern
and legacy schematics (including the terrain format used by world imports).
The `--block-rotation-graphics` command requires OpenGL 3.3 and checks button mounting/facing (including legacy
stone and wooden buttons), trapdoors, both door halves, multipart rotations, and
consistent geometry after conversion to animatable blocks, rebuilding, and
resource-pack reapplication.

## Experimental Veldrid scene renderer

OpenGL remains the default renderer. To render the main scene through Veldrid,
run `dotnet run -- --renderer=vulkan` or, on Windows,
`dotnet run -- --renderer=d3d11` from `MineImatorSimplyRemade`.
Use `--renderer=opengl` to explicitly select the default path.

This is an incremental migration, not a GL-free renderer. Veldrid renders the
main viewport's geometry into an offscreen target and submits real GPU draw
commands. Its output is read back and uploaded into the existing OpenGL UI.
The window, asset uploads, editor overlays, camera previews, and exports still
use OpenGL. Advanced scene lighting and post-processing are not yet ported;
editor overlays do not have the Veldrid scene depth buffer. The bridge incurs
GPU synchronization and CPU-transfer overhead and is not a performance upgrade.
The viewport labels the experimental backend, and initialization or rendering
failures are logged before falling back to OpenGL.

## Mine-imator Minecraft assets

The bundled 26.3 catalog includes 129 additional character models from the
Mine-imator Minecraft data archive.
Equipment models and special-block/prop models are available as standalone
objects in the **Equipment** and **Props** spawn categories, with model
previews and compatible texture variants. Equipment is not automatically
attached to a character yet.

To import assets, use Python 3.9+ and the matching ZIP and `.midata`
files from a local Mine-imator installation:

```powershell
python .\MineImatorSimplyRemade\tools\ImportMinecraftCharacters\import_characters.py "D:\path\to\Data\Minecraft\26.3.zip"
```

Existing models (including their texture manifests), textures, and animation
loops are skipped by default. This preserves customized or manually moved
assets `characters/vanilla`.
Use `--replace-existing` to regenerate existing archive models and manifests
and replace shared textures and loops. This intentionally replaces custom edits
to matching assets; it does not move or delete unrelated files.

Add `--check` for a read-only check of assets that would be imported. Combine it
with `--replace-existing` to compare existing assets against generated output
too; customized models may intentionally fail that comparison.

## Contributing

Pull requests are welcome. For major changes, please open an issue first
to discuss what you would like to change.

## License

[MIT](https://choosealicense.com/licenses/mit/)

[Discord](https://discord.gg/eswvppFuAD)
