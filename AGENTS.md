# Agent development instructions

Use this file for development, debugging, testing, and implementation context.
Keep the [README](Readme.md) focused on the app and user-facing feature usage.
Commands below use PowerShell syntax and run from the repository root unless
otherwise noted.

## Build and run

Requirements:

- .NET 10.0 SDK or newer (the app and harness target `net10.0`).
- A C++ compiler (MSVC, GCC, or Clang) for NativeAOT publishing.
- An OpenGL 3.3+ compatible graphics driver and desktop session for the app
  and graphics regression checks.

After cloning, open the repository directory:

```powershell
dotnet restore
dotnet build
dotnet run --project .\MineImatorSimplyRemade\MineImatorSimplyRemade.csproj
```

The app project enables NativeAOT publishing, embeds `assets`, copies root native
DLLs for non-publish builds, and copies `data` and `mods` into the build output.
Do not remove these resources to work around startup failures. The build requires
`MineImatorSimplyRemade\assets\ffmpeg\win-x64.zip`; if missing, restore the bundled
archive or use [UpdateBundledFfmpeg.ps1](MineImatorSimplyRemade/tools/UpdateBundledFfmpeg.ps1).

## Debugging workflow

1. Reproduce with the default OpenGL renderer first. Record the scene, timeline
   position, reproduction steps, and whether the problem affects the viewport,
   camera previews, picking, shadows, or export.
2. Inspect `logs\latest.log` next to the running executable. Startup logging
   mirrors `Console.WriteLine` and `Console.Error` into this file and retains up
   to five `logs\previous-N.log` files. Capture the relevant log before restarting,
   since startup rotates it.
3. Inspect timestamped `crash-reports\crash-yyyyMMdd-HHmmss.log` files next to the
   executable for fatal exceptions and recent log context. Logging and crash
   handling live in [Logger.cs](MineImatorSimplyRemade/src/core/log/Logger.cs) and
   [CrashReporter.cs](MineImatorSimplyRemade/src/core/log/CrashReporter.cs).
4. App-local settings use `%LOCALAPPDATA%\SimplyRemadeNuxi`; the ImGui layout is
   `imgui.ini` there. Preserve user settings and projects when investigating.
5. For performance, use Release builds and the existing
   [FrameProfiler.cs](MineImatorSimplyRemade/src/core/diagnostics/FrameProfiler.cs).
   Its CPU wall-time and allocation sampling is main-thread only; it does not
   measure GPU execution or background cloth solver time.
6. Add focused regression cases to the existing harness for behavior being fixed.
   Run the smallest relevant checks, then broaden only as needed. Report command
   failures and unmet graphics prerequisites explicitly; do not claim graphics
   correctness from CPU tests alone.

## Regression tests

[NxObjectTests](MineImatorSimplyRemade/tools/NxObjectTests/NxObjectTests.csproj)
is a custom executable harness, not a `dotnet test` project. No test adapter is
required. Pass harness options after `--`; build options go before it.
Check [Program.cs](MineImatorSimplyRemade/tools/NxObjectTests/Program.cs) for
additional selectors and their dispatch behavior. Some graphics selectors return
early, so run them separately rather than assuming every combined flag executes.

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

Coverage and prerequisites:

- `--backups`: preference defaults/persistence, timed snapshots, retention,
  failure safety, and preservation of unsaved edits without a GPU.
- `--texture-sheets`: import validation, exact rectangular tile pixels and alpha,
  project/object persistence, undo, ImGui controls, and live tile-cut overlays.
- `--texture-sheets-graphics`: OpenGL 3.3; uploaded tile dimensions, pixels, alpha,
  existing material orientation rules, and the dialog's image preview,
  orientation, transparency, and texture cleanup.
- `--effects-graphics`: OpenGL 3.3 desktop session; real rendered pixels,
  animation/seek reproducibility, emission, and empty-mesh rebuilding.
- `--particles-graphics`: OpenGL 3.3; particle sprite image orientation, textures,
  animation frames, opacity, camera-facing rotation, and block particles.
- `--emissive-graphics`: OpenGL 3.3; auto-emissive source positions in modern and
  legacy schematics, including the terrain format used by world imports.
- `--block-rotation-graphics`: OpenGL 3.3; button mounting/facing (including legacy
  stone and wooden buttons), trapdoors, both door halves, multipart rotations,
  and consistent geometry after conversion to animatable blocks, rebuilding,
  and resource-pack reapplication.

### Cloth tests and profiling

```powershell
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --cloth
dotnet run --configuration Release --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --cloth-graphics
```

`--cloth-graphics` checks OpenGL pixels/geometry, exercises bent loader planes,
and reports frame time and allocations for a 32x32 extruded plane. Use
`--configuration Release` before `--project` for representative performance
measurements. CPU checks include dense-scene idle/playback allocation budgets,
saved-bake roundtrips, shuffled-collider BVH locality, and animated bend/skin
collider parity.

For cloth in an existing project, use
`--cloth-project "path\to\project.nxProj"` to restore its saved hierarchy,
pose/groups, and visible colliders, then validate a full-timeline bake and
mesh-area bounds without modifying the project. This needs OpenGL and the
application's full asset data. Have the user close the application before placing
the harness output in its build folder:

```powershell
dotnet run --configuration Release --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -p:OutputPath="<absolute application bin folder>" -p:AppendTargetFrameworkToOutputPath=false -- --cloth-project "path\to\project.nxProj"
```

Replace the placeholder with the actual application output directory. Do not
end the quoted output path with a backslash. Do not overwrite a running app's
build output or modify the supplied project to make this check pass.

## Implementation notes

### Biome colors

Preset climate and color values are based on the
[Java 26.3 biome data](https://github.com/misode/mcmeta/blob/26.3-summary/data/worldgen/biome/data.json)
and the bundled grass/foliage colormaps.

### Cloth

Preserve the user-facing behavior and limits documented in the README:
cloth is bake-only, never live, and all enabled cloth is solved together.
Failed bakes keep previous saved frames; playback, seeking, and export read
saved frames without running the solver or evaluating colliders.

- Scene animation/colliders are sampled one frame at a time on the UI thread;
  rendering continues while the background worker solves each frame.
- Paused redraws reuse uploaded geometry. Viewport, camera feeds, shadows,
  picking, and export must share the deformation.
- Contacts near pinned vertices bound each particle's correction to penetration
  depth rather than magnifying tiny interpolation weights.
- Dense extruded planes retain full render geometry. Self-contact reuses
  refitted collision trees and padded contact candidates, refreshing them as
  particles move instead of rebuilding and sorting trees every solver iteration.
- During baking, collider collection reuses triangle buffers, caches unchanged
  local bend geometry, and builds bone lookups only for skinned colliders, once
  per model root per sample.
- BVH refits preserve spatially sorted triangle identities; unchanged scene
  colliders reuse their bounds without substep refits. Animated colliders use
  swept bounds and interpolated substeps.
- `ClothColliderCollection` identifies UI-thread sampling costs. The main-thread
  frame profiler does not include background solver time.

Weld and deformation invariants:

- Equal-sized groups share simulated particles, including velocity and collision
  response. Different-sized groups attach extra vertices at interpolated
  positions along the smaller group, preserving the denser edge's detail.
  Matching positions share particles.
- Edge-only groups use normalized mesh/vertex index order, not spatial proximity
  or edge length. **Reverse target order** reverses the correspondence.
- Groups containing selected faces match normalized two-dimensional surface
  coordinates fitted in mesh-local space, independent of object position and
  rotation. They use barycentric attachments to the smaller region's triangles.
  Extra rows outside a triangle extrapolate instead of clamping several vertices
  onto its boundary. Use corresponding surface patches, not closed surfaces or
  overlapping folds. **Reverse target order** flips the region's length direction.
- Region-to-edge welds and region matches that would collapse distinct vertices
  are rejected explicitly. Stretch/bend rest lengths retain authored geometry,
  rather than recording initial weld distortion.
- A single-vertex group welds an edge seam to one point. UV/triangle duplicates
  at exactly coincident source positions are automatically welded.
- Pins overlapping a seam snap-bind to its joined rest pose when baking. Each pin
  follows its object's animation from that joined local-space anchor, rather
  than its original separated position. Animation that pulls joined pinned
  anchors apart is rejected explicitly.
- Cloth operates on source geometry before Array copies and is not inherited
  from parent modifiers. A bent mesh uses its current bend as the initial rest
  shape, retained in baked frames; cloth replaces bend deformation rather than
  bending twice. Disabling cloth restores normal bend rendering.
- Animated collider skinning/bending is sampled on the CPU.

### Procedural 3D effects

Presets share a path -> fractal noise -> branches -> tube geometry pipeline.
The implementation uses original, seed-hashed multiscale displacement, bounded
branching, and parallel-transport tube frames. Stateless timeline evaluation
makes every export frame repeatable.

Geometry is bounded to 128 path segments, 12 tube sides, and 16 single-level
branches (at most 84,168 vertices per effect). Unchanged evaluations reuse their
uploaded mesh; animated changes use the existing mesh-upload pipeline.
The experimental Veldrid renderer can draw the geometry, but retains the
lighting/post-processing limitations below.

Research behind the approach:

- [Conn Buranicz: Procedural Lightning](https://www.connburanicz.com/procedural-lightning)
  describes controllable source/target paths, randomized offsets, branching,
  and procedural strand geometry. Camera-facing ribbons/quads are economical;
  tubes cost more geometry but remain volumetric from every camera angle.
- [Jettelly: Draw Lightning Generators](https://jettelly.com/blog/draw-lightning-generators-procedural-lightning-with-geometry-nodes-in-blender)
  describes curve-driven lightning and reuse of generated curves for other
  procedural structures. This motivates separating path generation from tube
  construction rather than introducing a lightning-only renderer.

The implementation does not embed either source's code or assets.

## Experimental Veldrid scene renderer

OpenGL remains the default renderer. From the repository root:

```powershell
dotnet run --project .\MineImatorSimplyRemade\MineImatorSimplyRemade.csproj -- --renderer=opengl
dotnet run --project .\MineImatorSimplyRemade\MineImatorSimplyRemade.csproj -- --renderer=vulkan
dotnet run --project .\MineImatorSimplyRemade\MineImatorSimplyRemade.csproj -- --renderer=d3d11
```

Direct3D 11 is Windows-only. This is an incremental migration, not a GL-free
renderer. Veldrid renders the main viewport's geometry into an offscreen target
and submits real GPU draw commands. Its output is read back and uploaded into
the existing OpenGL UI. The window, asset uploads, editor overlays, camera
previews, and exports still use OpenGL.

Advanced scene lighting and post-processing are not yet ported; editor overlays
do not have the Veldrid scene depth buffer. The bridge incurs GPU synchronization
and CPU-transfer overhead and is not a performance upgrade. The viewport labels
the experimental backend, and initialization or rendering failures are logged
before falling back to OpenGL. Verify the active backend and logs when testing;
a successful launch alone does not prove the requested backend is working.

## Importing Mine-imator Minecraft assets

Use Python 3.9+ and the matching ZIP and `.midata` files from a local Mine-imator
installation:

```powershell
python .\MineImatorSimplyRemade\tools\ImportMinecraftCharacters\import_characters.py "D:\path\to\Data\Minecraft\26.3.zip"
```

Existing models (including their texture manifests), textures, and animation
loops are skipped by default. This preserves customized or manually moved
assets under `characters/vanilla`.

Use `--replace-existing` to regenerate existing archive models and manifests
and replace shared textures and loops. This intentionally replaces custom edits
to matching assets; it does not move or delete unrelated files. Do not use it
without approval to replace those edits.

Add `--check` for a read-only check of assets that would be imported. Combine it
with `--replace-existing` to compare existing assets against generated output
too; customized models may intentionally fail that comparison.
