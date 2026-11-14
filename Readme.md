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

## Cloth modifier

In **Properties > Modifiers > + > Cloth**, enable cloth on an object that owns
a triangle mesh. For useful folds, import a subdivided mesh; the built-in plane
has only four vertices. Adjust gravity, damping, stretch/bend stiffness,
collision thickness, substeps and solver iterations.

- Enable **Select cloth vertices** to click or box-select vertices in the main
  viewport. Selection is through the mesh (including hidden vertices). Shift adds,
  Ctrl subtracts; right/middle mouse retain camera controls.
- Create named groups from the selection. **Pin to animated object** anchors a
  group to its source position, following the object's animation. Leave the object
  stationary for fixed world-space pins.
- On another cloth object, create an edge group, then choose the
  first group under **Weld to group**. Equal-sized groups share simulated
  particles, including velocity and collision response. Different-sized groups
  attach extra vertices at interpolated positions along the smaller group,
  preserving the denser edge's detail. Matching positions share particles.
  Edge-only groups use normalized mesh/vertex index order (not spatial proximity
  or edge length), with **Reverse target order** available.
  Groups containing selected faces instead match normalized two-dimensional
  surface coordinates fitted in mesh-local space, independent of object
  position and rotation. They snap together using barycentric attachments to
  the smaller region's triangles. Extra rows outside a triangle extrapolate
  instead of clamping several vertices onto its boundary. Use corresponding
  surface patches, not closed surfaces or overlapping folds.
  **Reverse target order** flips the region's length direction.
  Region-to-edge welds and region matches that would collapse distinct vertices
  are rejected explicitly. Stretch/bend rest lengths retain authored geometry,
  rather than recording any initial weld distortion.
  A single-vertex group welds an edge seam to one point. UV/triangle
  duplicates at exactly coincident source positions are automatically welded.
  Pins overlapping a seam snap-bind to its joined rest pose when baking, so
  both panel tops can remain pinned. Each pin follows its object's animation
  from that joined local-space anchor, rather than its original separated
  position. Animate joined panels together; animation that pulls their pinned
  anchors apart is rejected explicitly.
- Scene-mesh contact and cloth self-contact (vertex/triangle and edge/edge) are
  enabled by default. Hidden/non-rendering objects and editor helpers do not
  collide. Cloth objects interact through self-collision. Raise substeps and
  thickness for fast motion or thin geometry; this is an approximate position-based
  solver, not a guarantee against tunneling under arbitrary motion.
  Contacts near pinned vertices bound each particle's correction to the
  penetration depth, rather than magnifying tiny interpolation weights.
  Dense extruded planes retain their full render geometry; self-contact reuses
  refitted collision trees and padded contact candidates, refreshing them as
  particles move instead of rebuilding and sorting trees every solver iteration.
- Cloth **never simulates live**. In the Cloth modifier settings, click **Bake
  cloth** to solve all enabled cloth together from frame 0 through the timeline
  end (including pins, seams and animated colliders). Baking pauses playback and
  runs the solver on a background worker with a modal progress bar showing
  completed frames. Scene animation/colliders are sampled one frame at a time on
  the UI thread; rendering continues while the worker solves each frame.
  The popup prevents scene edits until baking finishes. Failed bakes keep the
  previous saved frames. Until baked, cloth adds no deformation.
  Playback, backward scrubbing and export read saved frames without running the
  solver or evaluating colliders; paused redraws reuse uploaded geometry.
  Bakes are saved with the project, so reopening does not require rebaking.
  **Clear bake** removes all scene cloth bakes and restores source rendering.
  Rebake after editing source geometry, bend, animation, pins, seams or colliders.
  Changes to cloth settings, mesh vertex counts or frame rate reject an outdated
  bake; frames outside the baked range also require rebaking. Scene edits do not
  silently start a new simulation. The frame-position cache is limited to 256 MiB
  per scene; reduce the timeline length or cloth vertex count if necessary.
  During baking, collider collection reuses triangle buffers, caches unchanged local bend geometry,
  and builds bone lookups only for skinned colliders, once per model root per
  sample. BVH refits preserve spatially sorted triangle identities; unchanged
  scene colliders reuse their bounds without substep refits. Animated colliders
  still use swept bounds and interpolated substeps.
  `ClothColliderCollection` identifies UI-thread sampling costs. The main-thread
  frame profiler does not include background solver time.
  The viewport, camera feeds, shadows, picking and export share the deformation.
  Settings/groups survive project saves, object archives, duplication and undo.

Cloth operates on source geometry before Array copies; cloth is not inherited
from parent modifiers. A bent mesh uses its current bend when cloth initializes as
the rest shape, retained in baked frames; cloth then replaces the bend
deformation rather than bending twice.
Disabling cloth restores normal bend rendering. Cloth cannot coexist on the same
mesh with skinning, shape keys, shader wind or camera-facing/item-animation transforms. Animated
collider skinning/bending is sampled on the CPU. Changing topology requires
reselecting groups. Limits are 12,000 cloth source vertices per scene, 256 groups
per modifier, and 200,000 visible collider triangles. Invalid setups are reported
in the modifier panel rather than partially simulated.

Regression checks:
`dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --cloth`
and `--cloth-graphics` for OpenGL pixel/geometry checks.
The graphics checks also exercise bent loader planes and report frame time and
allocations for a 32x32 extruded plane. Use `--configuration Release` before
`--project` for representative performance measurements.
CPU checks include dense-scene idle/playback allocation budgets, saved-bake
roundtrips, shuffled-collider BVH locality and animated bend/skin collider parity.
For cloth in an existing project, use
`--cloth-project "path\to\project.nxProj"` to restore its saved hierarchy,
pose/groups and visible colliders, then validate a full-timeline bake and
mesh-area bounds without modifying the project.
This check needs OpenGL and the application's full asset data. Close the
application and place the harness output in its build folder using
`-p:OutputPath="<absolute application bin folder>" -p:AppendTargetFrameworkToOutputPath=false`
before `--`. Do not end the quoted output path with a backslash.

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
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --frames
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --texture-sheets
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --texture-sheets-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --backups
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --modifiers
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --world-player
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
