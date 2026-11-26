# Agent development instructions

Use this file for development, debugging, testing, and implementation context.
Keep the [README](Readme.md) focused on the app and user-facing feature usage.
Commands below use PowerShell syntax and run from the repository root unless
otherwise noted.

## Build and run

Requirements:

- .NET 11.0 SDK or newer (the app and harness target `net11.0`).
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
CPU selectors also filter successively: combining unrelated selectors can execute
zero tests. Run each feature selector separately and check the reported test count.

```powershell
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --frames
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --mi-import
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --audio
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --audio-playback
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --meshcache
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --meshcache-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --texture-sheets
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --texture-sheets-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --resourcepack-animation-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --backups
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --modifiers
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --world-player
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --world-preview
dotnet run --configuration Release --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --water-terrain-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --effects-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --particles-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --emissive-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --lights-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --material-colors
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --material-colors-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --shader-graph
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --shader-graph-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --block-rotation-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --rigid-body
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --nuxt
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --nuxt-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --shader-graph
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --shader-graph-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --fluid
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --fluid-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --instancing-graphics
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --veldrid-glsl
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --veldrid-post-graphics
```

Coverage and prerequisites:

- `--mi-import`: sparse/default-relative poses, cameras/lights/text, constraints,
  modifiers, effect toggles/conversions, builtin model lookup, normal-map formats,
  background aliases, portable resource tracks and meshcache-first scenery.
- `--audio`: imported sound triggers, portable project assets, source/end trims,
  pitch, loop bounds, overlapping clips and exact exported PCM samples.
- `--audio-playback`: FFmpeg and OpenAL; actual decoding, pitch, trimmed playback,
  looping seeks and bounded stopping. For silent checks with OpenAL Soft, set
  `$env:ALSOFT_DRIVERS = 'null'` in the test process.
- `--meshcache-graphics`: OpenGL 3.3; actual cache geometry/UV pixels, cache-only
  `.miobject` scenery and portable archive reload, without stray scene roots.
- `--lights-graphics`: OpenGL 3.3; point/spot specular strength through timeline
  evaluation, light collection and GPU upload, standard/PBR dielectric/metal
  pixels, energy/cone/unlit gating, and exclusion of highlights from diffuse GI.

- `--startup-graphics`: OpenGL 3.3; loading-logo GIF frame pixels, per-frame
  delays, looping, and the startup UI's animated image draw commands.
- `--world-preview`: nonblocking preview polling, cancellation, empty terrain,
  and worker error propagation without a GPU. `--water-terrain-graphics` also
  checks asynchronous terrain geometry/material parity, filter snapshots, and
  full-height tile UI-stage timing with OpenGL 3.3.
- `--frames`: keyframe import/export, timeline navigation, mouse-anchored zoom,
  interactive playhead-pivot scaling and endpoint stretching, including frame
  bounds, collisions, reversible previews, custom bindings, and undo/redo.
  These checks drive ImGui without a GPU.
- `--camera-spawn`: immediate camera position/rotation properties without cursor
  placement, with work-camera copying enabled and disabled; no GPU required.
- `--armor`: adult/baby mixed armor variants, omitted pieces, custom textures,
  rig preservation, per-piece leather dye/undyed overlay pixels, trim palette
  pixels (including darker matching materials) and persistent generated models.
  `--mi-import` also covers imported armor piece materials, leather dyes and trims.
  `--spawn-preview-graphics`
  also checks the armor editor's left-hand modal preview, exact pixel parity
  with original and spawned armor, and outfit reload with OpenGL 3.3 and the
  application's full asset data.
- `--backups`: preference defaults/persistence, timed snapshots, retention,
  failure safety, and preservation of unsaved edits without a GPU.
- `--texture-sheets`: import validation, exact rectangular tile pixels and alpha,
  project/object persistence, undo, ImGui controls, and live tile-cut overlays.
- `--texture-sheets-graphics`: OpenGL 3.3; uploaded tile dimensions, pixels, alpha,
  existing material orientation rules, and the dialog's image preview,
  orientation, transparency, and texture cleanup.
- `--resourcepack-animation-graphics`: OpenGL 3.3; incremental pack loading preserves
  existing terrain, item and imported sheet texture handles/pixels; animated
  textures reproduce paused seeks, exact tick boundaries, speed changes, shadow
  signatures, independent asset previews, camera previews and PNG exports.
- `--effects-graphics`: OpenGL 3.3 desktop session; real rendered pixels,
  animation/seek reproducibility, emission, and empty-mesh rebuilding.
- `--particles-graphics`: OpenGL 3.3; particle sprite image orientation, textures,
  animation frames, opacity, camera-facing rotation, and block particles.
- `--emissive-graphics`: OpenGL 3.3; auto-emissive source positions in modern and
  legacy schematics, including the terrain format used by world imports.
- `--material-colors`: object/camera color-operation persistence, duplication,
  undo, inheritance, timeline channels, reset defaults, and ImGui controls.
- `--material-colors-graphics`: OpenGL 3.3 and Windows D3D11; exact RGB/HSV
  operation pixels, stacking/clamping, alpha, PBR parity, previews, and PNG export.
- `--shader-graph`: graph validation, project archive/library roundtrips, duplication, and ImGui socket interactions.
- `--shader-graph-graphics`: OpenGL 3.3 sphere renders for all presets, including
  bump, emission, metallic response, alpha coverage, and disabled-mode parity.
  Supply a PNG path after the flag to save the render contact sheet.
- `--fluid`: deterministic position-based solving, box and scene-mesh collision,
  bounds, validation, and source-generated archive/import/duplication persistence.
- `--fluid-graphics`: OpenGL 3.3; particle and reconstructed surface pixels,
  animation, and backward-seek reproduction.
- `--instancing-graphics`: OpenGL 3.3; instanced scene draws against per-mesh draws
  (exact pixels, merged draw/mesh counts, interleaved meshes staying in scene order, opt-in
  reordering), plus a mutation check that changes every public `Mesh` and `StandardMaterial`
  setting and requires `Mesh.CanInstanceWith` to notice it.
- `--veldrid-glsl`: no GPU; every fullscreen, sky, background, scene and shadow shader converts
  through `VeldridGlslAdapter` and compiles to SPIR-V, and the std140 layout rules hold. It also
  checks conditional shader branches, named uniform blocks and literal-index sampler arrays, and
  runs in the default suite.
- `--veldrid-post-graphics`: OpenGL 3.3 plus Direct3D 11 and Vulkan (a backend the machine does
  not support is skipped). Veldrid pixelate against its CPU reference; the scene renderer's
  row order against OpenGL with a lopsided quad (centre pixels cannot catch a vertical flip);
  static geometry buffer reuse and `Mesh.GeometryVersion` invalidation; `VeldridCameraEffects`
  passes for Bloom, Depth of Field, Glow, Lens Dirt, Watermark and the other camera effects against
  production `Viewport.Apply*Pass` methods on the same image; and the sky (procedural, sphere, box)
  and background image against `RenderSky` and `RenderBackgroundPlane` with injected synthetic
  textures. Most effects match within one level; Lens Dirt allows two levels.
  Film grain, FXAA and the star field are allowed a small fraction of outlier pixels because
  their hashes and thresholds round differently on each API. All backends share one OpenGL
  context, because the application caches compiled programs in statics tied to the context
  that created them.
- `--block-rotation-graphics`: OpenGL 3.3; button mounting/facing (including legacy
  stone and wooden buttons), trapdoors, both door halves, multipart rotations,
  and consistent geometry after conversion to animatable blocks, rebuilding,
  and resource-pack reapplication.
- `--rigid-body`: no GPU; Jolt falling/resting, determinism, shapes, collision
  collections, kinematic bodies and keyframed handoff, fluid buoyancy/sinking/
  particle displacement, viewport bake playback, staleness, start frame,
  clearing, coupled fluid bakes and persistence.
- `--nuxt`: no GPU; NuxT path-tracer environment, intersections, albedo/alpha-mask
  cutouts, emission, diffuse bounces, ambient/directional/point lighting and
  shadows, deterministic samples, settings validation, fog quality settings and
  their project round trip, the app-level renderer preference format, and
  progressive cancellation/reset.
- `--nuxt-graphics`: OpenGL 4.3; compute shader compilation and dispatch, textured
  scene pixels against the CPU reference, progressive accumulation, camera/scene
  resets, empty-scene environment output, and a 200,000-triangle scene whose old
  expanded triangle buffer exceeded 64 MiB. It also traces custom shader graph
  surfaces and fog volumes (glow, absorption/tint, ambient and point-light scattering,
  anisotropy, top cutoff, voxel snapping, Path node windows and fog shadows) and
  compares real radiance values (`NuxTShaderGraphTests.cs`).
  `--nuxt-project "path\to\project.nxProj"`
  restores a supplied project read-only, prepares its NuxT scene and reports one
  GPU sample or a device-limit fallback; run it with the harness output directed
  to the application bin folder so bundled data assets resolve.
- `--shader-graph`: custom shader graph validation, socket values, multi-output
  links, legacy graph upgrades, persistence, canvas/inspector controls for every
  node kind, editor shortcuts, and the Fog Volume Output, Path node, output
  swapping and tracer-side path resolution.
- `--shader-graph-graphics`: OpenGL 3.3; preset pixels, every node kind and
  output on the GPU, Alpha, Time-driven animation, screen-space transmission
  and IOR refraction, and raster fog volume marching (coverage, centre-vs-rim
  thickness, density, emission, an opaque object inside the volume, a camera
  inside it, and noise-driven graphs). Pass a PNG path after the selector to
  write a contact sheet.

NuxT shader graphs and fog volumes:

- `nuxt.comp` defines `GRAPH_COMPUTE` and includes `shader_graph.glsl`, so the viewport
  and the path tracer run one interpreter. In compute mode each graph program is one
  row of the `uShaderGraphData` texture (binding 15) and the per-hit state (graph row,
  bounds, frame, path slots, `uModel`) is set by `BindGraph`. Path polylines follow the
  program rows in the same texture, resampled to `NuxTShaderGraph.PathSamples` points
  by arc length. Material texels are `MaterialStride` (15): the 11 surface texels, then
  graph info, bounds minimum and frame, bounds size and the four path rows.
- `NuxTMaterial.ShaderGraph` is a `NuxTShaderGraph`: the compiled program plus bounds,
  time and resolved path points. It compares by content so unchanged scenes keep
  their fingerprint. `Viewport.NuxT.cs` hashes graph identity and followed path points
  in the capture hash, because path edits change no mesh geometry.
- A fog volume is a closed mesh whose graph ends in `FogVolumeOutput`. `Trace` enters
  it on a front-facing hit, ray marches to the next hit on the same instance, and
  treats a back-facing first hit as starting inside. Fog never blocks light itself:
  shadow rays go through `ShadowTransmittance`, which integrates fog with a few
  samples (and is skipped when the Fog shadows setting is off). Voxel size, randomness
  and Top Cutoff are plain node settings because the sample position is snapped to the
  voxel grid before the graph runs. A graph's Path slot is its index in
  `ShaderGraphProgram.PathTargets`, which follows graph evaluation order, not node ids.
  `MaxPaths` bounds the distinct paths so slots fit one texel.
- The CPU tracer ignores graphs and treats fog volumes as invisible; only the GPU
  tracer implements them. Fog quality lives in `NuxTSettings` (and project settings).
- Fog Volume Output's `Clamp` flag is labelled **Ignore Object Scale** and packed as `w` of
  the settings texel (`readShaderGraphVolumeSettings`, now a `vec4`). When set, the raster
  march (`fogEvaluate`) and the tracer (`EvaluateVolumeAt`, `VolumeVoxelCenter`) snap voxels
  on `local * scale` and run the graph with `gGraphPositionScale` set to the per-axis length of
  the `uModel` columns (`graphMatrixScale`), so position-based nodes see world-sized units.
  Object-space `local` still drives edge softness and the top cutoff plane. `Generated`
  coordinates come from `graphGeneratedCoordinates(local, scale)`, which maps the bounds to
  0..1 at scale 1 and otherwise grows outward from the bounds' centre by `scale`, so a
  bigger object shows more of the same pattern (values outside 0..1) instead of stretching
  it; `fogEvaluate` and `EvaluateVolumeAt` also feed it into the UV input, and the Texture
  Coordinate node divides the scale out of its position first. It is a per-axis scale of the
  full object-to-world matrix (parent scale included); shear and rotation are not separated.
  Reset `gGraphPositionScale` to 1 after the call, since the global is shared by every
  graph. `--shader-graph`, `--shader-graph-graphics` and `--nuxt-graphics` render a scaled
  object with and without the option, driven by both Position and Generated coordinates.
- The raster viewport ray marches fog volumes in `simple.frag` (`fogVolumeMarch`),
  using the same `runShaderGraph` and `readShaderGraphVolume` as the tracer.
  `Viewport.RenderDepthOrdered` calls `CaptureFogVolumeInputs` right before each volume:
  it copies the scene depth (`Mesh.FogVolumeSceneDepthTexture`, unit 17) and draws the
  volume into an RGBA32F range texture (`Mesh.FogVolumeRangeTexture`, unit 16) with
  `Mesh.FogVolumeRangePass`. That pass blends with max for red (farthest inward-facing
  surface distance) and min for alpha (nearest outward-facing one), so the march pass can
  draw both faces with no depth test and let exactly one fragment per pixel run: the
  nearest outward-facing surface, or the farthest inward-facing one when the camera is
  inside. It marches from there to the exit or the scene depth, whichever is nearer.
  Winding follows `CullFrontFaces` through `uFogInvertFaces`; the distances are measured
  from `uCameraPosition`, so a pixel's fragments must agree within a tolerance.
- Raster fog is premultiplied: the volume draws with `One / OneMinusSrcAlpha`, writing
  display-encoded in-scattered light and opacity `1 - min(transmittance)`. Coloured
  extinction adds the remainder using the captured backdrop (`uShaderGraphSceneColor`).
  Lighting is ambient plus every directional light (one shadow-map tap per step) and up
  to `FOG_MAX_POINT_LIGHTS` point lights (one shadow-cube tap each), with the phase
  function scaled so an isotropic medium is 1. Step size, step cap and dither are
  constants in `simple.frag`. Fog volumes are excluded from every shadow pass and shadow
  bounds (`Mesh.IsFogVolume`), because the medium would otherwise shadow itself. Draws
  without those inputs (unshaded views, GPUs below 18 texture units) fall back to
  `graphVolumePreview`, a translucent shell. The march reads the range and depth
  textures with unit offsets from `uFogOrigin`, so a viewport with a window offset works.
- The tracer tone maps with `x / (1 + x)` before sRGB encoding, so a radiance of 1
  reads as byte 188. Graphics tests that check graph or fog output should invert both
  (see `NuxTShaderGraphGraphics`). `new NuxTSettings()` zeroes every field; use named
  arguments such as `new NuxTSettings(Samples: 32)` to get the declared defaults.
- The renderer choice is an app preference (`UseNuxTRenderer` in `preferences.json`),
  not a project setting. `Viewport.SetNuxTRenderer` saves it, so tests must set
  `Viewport.UseNuxTRenderer` instead to avoid writing the developer's real preferences.
  Startup reads it before the window exists unless `--renderer=` is passed.

Shader graph nodes are described by `ShaderGraph.Spec`; their numeric kinds are
saved in projects and match the instruction codes in `shader_graph.glsl`. Add
new sockets at the end of a node's input list and keep existing kind values so
saved links stay valid.

### Camera outline regression checks

```powershell
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --outline
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --outline-graphics
```

CPU checks cover saved settings, independent copies, undo/dirty detection,
timeline interpolation and conditional normal-pass controls. The graphics
selector requires OpenGL 3.3 and checks depth/normal edges, grazing-terrain
slope rejection (including image borders), pixel radius, soft
thresholds, all five blend modes, strength, alpha and preview/PNG export parity.

### Pixelate camera regression checks

```powershell
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --pixelate
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --pixelate-graphics
```

CPU checks cover saved settings, independent copies, undo/dirty detection,
timeline interpolation and the ImGui pixel-size slider. The graphics selector
requires OpenGL 3.3 and verifies exact block sizes, partial image-edge blocks,
whole-pixel rounding, alpha, stacking, fast/high-quality previews and PNG export.

### Cinematic bars camera regression checks

```powershell
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --cinematic-bars
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --cinematic-bars-graphics
```

CPU checks cover persistence, independent copies, undo/dirty detection, timeline
interpolation, sliders and the color picker. Graphics checks require OpenGL 3.3
and cover exact bar coverage, rotation, offset, color/alpha, stacking, all preview
shading modes (without forcing camera effects), and PNG export.

### Heat distort camera regression checks

```powershell
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --heat-distort
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --heat-distort-graphics
```

CPU checks cover saved settings, independent copies, undo/dirty detection,
timeline interpolation and all three ImGui percentage sliders. Graphics checks
require OpenGL 3.3 and cover displacement, strength/speed/scale, deterministic
seeking, frozen animation, alpha, stacking, previews and PNG export.

### Frame Skip modifier regression checks

Run `--modifiers` with the NxObjectTests harness for stepped transform, imported
rotation, bone bend, Follow Path and camera sampling, independent child tracks,
backward seeking, export/physics sampling, discrete control keys, persistence,
duplication, undo/dirty detection and the actual Frames Per Pose slider.
Frame Skip quantizes an object's keyframe sample time, not the global playhead;
its own controls always sample unquantized timeline time.
It also steps Shake noise and its keyframe-influence envelope, regardless of
modifier order, and saved Cloth playback. Physics solving still advances every
frame. Run `--cloth` for held baked geometry, upload reuse and restoration checks.
Run `--shake-graphics` for held position/rotation/bend pixels, boundary changes
and backward-seek repeatability with OpenGL 3.3.

### Shake modifier regression checks

```powershell
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --modifiers
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --shake-graphics
```

Modifier CPU/ImGui checks cover deterministic transform and bend sampling,
camera/child/Array propagation, model bend limits, keyframe interpolation and
influence, percentage controls, persistence, duplication, undo and dirty detection.
The graphics selector requires OpenGL 3.3 and verifies changed position, rotation
and bend pixels, exact backward-seek repeatability and disabled-pose restoration.
Shake is evaluated before constraints and physics, without changing authored
transforms or bends. Its automatic pattern is keyed by the saved modifier ID.

### Wiggle bone regression checks

```powershell
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --wiggle
dotnet run --project .\MineImatorSimplyRemade\tools\NxObjectTests\NxObjectTests.csproj -- --wiggle-graphics
```

CPU checks exercise endpoint springs, damping, gravity/wind, connected chains,
sphere contacts, freeze/mute, full-timeline bake persistence, backward seeking,
undo, atomic cancellation/failure, and actual ImGui controls. The graphics selector
requires OpenGL 3.3 and verifies visible bone-mesh pixels, repeatability, and rigid
pose restoration. Bone physics lives in evaluated world transforms, never authored
position/rotation/scale. Bake wiggle before sampling cloth that depends on it.

Mesh collisions use visible evaluated mesh surfaces and the shared collision BVH;
exclude self/descendant meshes and skin palettes driven by those bones. MiBone
bend wiggle uses independent angular springs in editable bend space, preserving
model axes/limits and IK input. Save bend frames alongside world transforms and
include both in the bake memory budget.

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

### Meshcache scenery

`MeshCacheData` reads big-endian QDataStream scalars and little-endian raw
vertex/index arrays. Format 7 uses Zstandard and byte-plane shuffled arrays;
formats 2/4/6 use gzip; format 2 also supports uncompressed input. Single-buffer
format-2 custom meshes have dummy 1x1x1 dimensions, no block atlas, and mesh-local
UVs (often negative). Preserve authored offsets and UVs; do not recenter their
bounding box or resolve UVs as block tiles. Compose `render_world_block`'s legacy
matrix with `temp_update_rot_point`'s default scenery pivot, then convert Z-up to
Y-up. In block units this is `(y - width/2, z, height - x - length/2)`; normals
become `(ny, nz, -nx)`. The reference `matrix_build` transposes its +90-degree Z
rotation. Keep UVs and reflected winding unchanged; do not apply these custom
transforms to block-atlas caches. Later formats already contain Mine-imator's
timeline-axis transform. Preserve centered block units, outward winding, per-tile
UVs and material batches.
Reject incompatible sidecars with a logged schematic fallback.

`tools/ImportMinecraftCharacters/import_characters.py` also generates
`meshcache-atlas.json` from the matching `.midata` texture-slot tables. Do not
reorder these tables or infer slot order from filenames. They contain texture
identifiers and sheet dimensions, not image assets or renderer source.

Run `--meshcache-model-graphics "path\to\model.miobject" "path\to\render.png"`
for the supplied Phainon outfit's actual OpenGL attachment pixels against an
independent raw-cache reference transform, using one body-framed camera for all
three caches. It requires the full application asset data; the PNG is optional.
Run the custom harness with `--meshcache` for CPU decoding, malformed input,
sidecar freshness and asset/archive persistence; run `--meshcache-graphics`
separately for OpenGL pixel/orientation and reload checks. Use
`--meshcache-file "path\to\file.meshcache"` to validate a real file without
modifying it. Imports are bounded to 512 MiB decompressed data and eight million
rendered vertices.

### Scene instancing

Consecutive scene-pass meshes with identical geometry and shading state draw as one
`DrawElementsInstanced` call (`Mesh.Instancing.cs`, `Viewport.RenderDepthOrdered`). The
vertex shader reads the model matrix from attributes 5-8 and clip position from `uViewProj`
when `uUseInstancing` is set; everything else comes from the first mesh's uniforms.

- Merging never changes draw order, so blending and depth results stay identical. Meshes
  that are skinned, actively bent (non-zero `GpuBend.Angle`), shape-keyed, cloth, wind
  affected, animated-texture, glint, shader-graph or picking-only stay on per-mesh draws,
  as do scenes with more than `MaxLightsPerMesh` point lights (light selection is per
  mesh position).
- Geometry equality is exact: `GetGeometryShare` hashes the uploaded vertex data and
  confirms it element by element. Material equality is the hand-written
  `Mesh.InstanceStateEquals`. It deliberately avoids reflection, which NativeAOT cannot
  rely on. When you add a public setting to `Mesh` or `StandardMaterial`, add it to that
  method; `--instancing-graphics` fails when a changed setting is not noticed.
- `Mesh.InstancingReorder` (off by default) additionally gathers identical meshes that are
  not neighbours. Moving a mesh across others can change partially transparent parts that
  still write depth, so enable it only for scenes known to be opaque or cutout.
- Per-mesh submission cost dominates large scenes. Instancing removes it only for merged
  runs. Mine-imator character parts with an active bend remain individual draws.

### Render batch state reuse

Inside `Mesh.BeginRenderBatch`, `Mesh.Render` avoids re-sending state the previous mesh
already left in place. Most of a mesh's roughly 90 uniforms are identical to its
neighbour's, and checking them one by one was the largest per-mesh cost.

- `ApplyMaterialUniforms` is skipped when the mesh's material matches the last mesh drawn
  with the same program: `MaterialStateEquals` (the shared part of `InstanceStateEquals`)
  plus the call inputs that `RenderMeshWithModeOverride` overrides (fog, unlit) and the
  lighting flag. Everything else in that block is a scene-wide static that cannot change
  inside a batch. `uPbrEnabled` is set outside the block because `Render` resets it after
  every draw. Add per-mesh values that are not covered by `MaterialStateEquals` to that
  method, not to the block.
- The batch also tracks the bound program, scene uniform buffer link, vertex array, face
  culling and texture unit 13, and restores the defaults once when it ends. Code that
  changes GL state inside a batch must call `Mesh.InvalidateBatchTextureCache`.
- `Mesh.BatchStateReuse = false` (`NX_NO_STATE_REUSE=1` in `--perf-project`) sends all state
  for every mesh. `--instancing-graphics` renders a mixed-material row both ways and
  requires identical pixels; it fails if a reused value goes stale.
- Shadow passes (`RenderShadowCasters`, `RenderPointShadowCasters`) run in their own
  batch per map or cube face. Each point-light face culls casters against that face's
  frustum.

### Playback performance

- `--perf-project "path\to\project.nxProj" [frames] [width] [height] [startFrame]` renders
  real frames of a saved project and prints the `FrameProfiler` stage table and counters.
  It registers the timeline, marks it as playing and applies each frame's keyframes
  (`Keyframe application` line) before rendering, like playback in the editor. Switches:
  `NX_PAUSED=1` measures the paused editor, `NX_RENDERED=1` renders the Rendered view
  (shadows and lighting passes), `NX_NO_INSTANCING=1`, `NX_REORDER=1` and
  `NX_NO_STATE_REUSE=1` turn individual optimizations off for A/B runs. Use a Release
  build. A bare frame counter without keyframe application hides the animation cost.
- `Timeline.ApplyKeyframeChannels` resolves track owners through one id lookup per call
  (`BuildObjectLookup`, same precedence as `FindObjectById`). Searching the scene for each
  of thousands of tracks made playback cost tracks x objects per frame.
- A keyframe pass defers bend visuals with `MiBoneSceneObject.BeginBendRefreshBatch`, so a
  bone with CPU-bent geometry (extruded 3D planes) rebuilds once per frame instead of once
  per axis. `RegenerateMeshes` disposes the meshes it replaces, and a rebuild is skipped
  while the effective bend angle and the bone's meshes are unchanged. Anything else that
  changes how those meshes are generated must reset `_generatedBendAngle`.
- Shadow maps cannot be reused while the timeline plays or an animation exports
  (`CanReuseShadowMaps`), so the scene signature is not hashed then and the cached map is
  marked invalid.
- Per-frame costs measured on the bundled benchmark project (Release, 1280x720, timeline
  playing): the plain preview takes about 20 ms, and the Rendered view with shadows about
  60 ms, most of it the shadow passes.

### Look At constraints

Camera forward is local -Z; Mine-imator character and bone faces use local +Z.
Look At reads target positions, so reciprocal aiming is valid. Keep dependencies
on position-writing constraints and on ancestors that can move target origins;
do not make an object's own rotation-only constraint a position dependency.
Camera targets use their evaluated eye position, including pivot displacement.

Run `--copy-constraints` for CPU/ImGui checks, or `--look-at-graphics` with the
NxObjectTests harness for OpenGL 3.3 front-face pixel checks using the bundled
adult armor, baby armor and Steve models.

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

### Rigid bodies (Jolt Physics)

Preserve the README behavior: rigid bodies are bake-only and solved together.
`RigidBodySimulation` is UI-independent; `Viewport.RigidBody.cs` samples each
frame on the UI thread (with `RigidBodyEvaluation.Suspend()` so old bakes don't
feed the authored pose) and solves it on a background task.

- Bakes store object world transforms without pivot. `RigidBodyEvaluation.Apply`
  runs inside `InverseKinematicsSolver.Evaluate` before copy constraints/IK, and
  `SceneObject` gives the override priority over copy transforms, so viewport,
  export, cloth/wiggle sampling and children all see the same motion.
- Simulated poses are deltas: `N_t = B_t * B_0^-1 * N_0`, where `B` is the rigid
  Jolt body frame and `N` the captured object world matrix. This preserves
  scale, shear, mirroring and pivots of the authored transform.
- `RigidBodyBake.Matches` ignores `Animated`, because it is keyframeable for
  animated-to-dynamic handoff; other setting, world or frame-rate changes make
  the bake stale and the authored pose is shown.
- Coupled fluids are created at frame 0 and stepped at their own fixed 60 Hz
  inside physics substeps. Buoyancy uses Jolt's `Body.ApplyBuoyancyImpulse` with
  a water level from `2 * median - floor` of particles in a ring around the body,
  which ignores splashes. Particles are pushed out with exact body-local signed
  distances (hull planes come from Jolt's hull faces); water trapped against the
  fluid domain slides along the wall instead of being clamped back in.
- JoltPhysicsSharp 2.24 binding bugs: `BodyInterface.ApplyBuoyancyImpulse`
  passes the body ID by pointer (always fails) and
  `NarrowPhaseQuery.CollideShape` ignores the query transform. Use the `Body`
  overload and the signed-distance push-out instead. Verify any new binding call
  against joltc behavior with a focused test.
- NativeAOT publishes embed `joltc.dll` from `$(PkgJoltPhysics_Native)` for
  `NativeLibraryBootstrap`, since loose publish DLLs are deleted.

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
renderer. Veldrid renders the main viewport (sky, background image, geometry,
lighting, fog and the camera effects listed below) into an offscreen target and
submits real GPU draw commands. Static, non-deforming mesh buffers persist across
frames and are invalidated by `Mesh.GeometryVersion`; dynamic geometry stays
frame-scoped. The finished image is read back and uploaded into the existing
OpenGL UI. The window, texture and mesh uploads, editor overlays, camera previews,
exports and picking still use OpenGL.

What the Veldrid viewport covers:

- Scene: ambient, directional, point and spot lighting, fog, wind, material colours,
  normal and emission maps and the end portal. Shadows, ambient occlusion, indirect
  lighting, subsurface scattering and glow are not drawn in this mode.
- Backdrop (`Viewport.DrawVeldridBackdrop`): the procedural, sphere and box sky and the
  background image, drawn before the geometry with `sky.frag` and `background.frag`.
- Camera effects (`VeldridCameraEffects`, assembled by `Viewport.BuildVeldridEffects` in the
  OpenGL order): transparent-background alpha resolve, colour management, colour correction
  with the camera material, vignette, chromatic aberration, distort, heat distort, FXAA, film
  grain, pixelate, Bloom, Depth of Field, watermark and cinematic bars. Bloom and Depth of Field
  match their production OpenGL passes within two channel levels on the graphics regression;
  watermark texture pixels match within one level.
- A frame switches to the OpenGL scene, with the reason shown above the viewport, for custom
  shader-graph materials, Outline and Lens Dirt (`Viewport.GetVeldridFallbackReason`). Watermark
  and cinematic bars use OpenGL only when the configured watermark image cannot be imported.

How the port works:

- `VeldridGlslAdapter` rewrites the project's GLSL 3.3 sources (loose uniforms, `sampler2D`,
  named std140 uniform blocks, literal-index sampler arrays, conditional branches, implicit
  locations, `gl_VertexID`) into the Vulkan GLSL that Veldrid.SPIRV compiles. Shader
  bodies stay untouched, so one `.frag` file feeds both renderers until the OpenGL one is
  removed. Loose uniforms become one std140 block, each sampler becomes a texture and sampler
  pair, `bool` uniforms are stored as `uint`, uniform initializers become defaults, sampler
  parameters of helper functions are split into texture and sampler, and identifiers that GLSL
  4.x reserves (`sample`, `filter`, ...) are renamed. Sampler arrays require literal indices and
  share one sampler across their separate texture bindings; interface blocks and sampler arrays
  passed to helper functions are not supported. The production `simple.*` shaders compile through
  the adapter with the shader-graph interpreter disabled, but the live scene renderer still uses
  its existing equivalent shader and per-mesh writer; the real shader and its full uniform writer
  are not wired into the scene yet.
- Veldrid targets are stored in OpenGL row order (row zero is the bottom of the picture). The
  adapter and the scene vertex shader negate clip-space Y when the device's UV origin is
  top-left (`VeldridFullscreenPass.FlipClipY`), and the scene pipelines use the matching front
  face, so `gl_FragCoord`, `texelFetch` and UVs behave as in OpenGL and readback needs no row
  flip. Keep this convention for every new Veldrid target or shader.
- `VeldridFullscreenPass` runs one adapted shader over a target (`Set("uSize", 4f)`,
  `SetTexture("uScene", view, sampler)`), from a vertex-less triangle or from the shared backdrop
  quad. `VeldridPostProcessor` owns the passes, samplers and the ping-pong target and runs an
  ordered `IVeldridPostEffect` list; `VeldridSceneRenderer.EndFrame(texture, processor, effects)`
  runs it before the readback.
- `VeldridSceneRenderer.ImportPersistentTexture` keeps static OpenGL textures (sky, background)
  across frames, keyed by texture id and a revision that `Viewport` bumps whenever it replaces
  them (`_skyTextureRevision`, `_backgroundTextureRevision`). Mesh textures are still copied from
  OpenGL every frame; moving texture ownership to Veldrid is a prerequisite for removing OpenGL.
- Direct3D cannot compile implicit-derivative lookups (`texture()`) inside divergent loops, so
  `sky.frag` and the directional/point shadow lookups in `simple.frag` use `textureLod(..., 0.0)`
  for their mipmap-less textures. Use explicit levels for lookups inside loops in any shader
  Veldrid must run.

To port another OpenGL effect: add a builder to `VeldridCameraEffects` that mirrors the OpenGL
pass's settings, skip rules and uniforms; add a case to `VeldridEffectParity`
(`VeldridPostParityTests.cs`) that compares it with the production pass; wire it into
`Viewport.BuildVeldridEffects` (or remove its type from `GetVeldridFallbackReason`); and list new
shaders in `VeldridFullscreenShaders` so the CPU compile check covers them.

The next large steps are wiring the compiled `simple.*` shader into the scene pass with a complete
`Mesh` uniform writer and MRT outputs; adding directional/point shadow maps and their caster
passes; then porting AO, indirect lighting, glow, Outline and Lens Dirt. NuxT compute, shader graphs
and fog volumes, previews, exports, picking and editor overlays remain outside this viewport port.

The bridge incurs GPU synchronization and CPU-transfer overhead and is not a performance
upgrade. The viewport labels the experimental backend, and initialization or rendering failures
are logged before falling back to OpenGL. Verify the active backend and logs when testing; a
successful launch alone does not prove the requested backend is working.
`VeldridPostProcessPipeline`, `VeldridShadowPass` and the other unused scaffolding in
`src/core/graphics` predate this port and are not wired in.

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
