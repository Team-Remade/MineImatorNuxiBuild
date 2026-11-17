# Mine Imator Nuxi

For all your Minecraft animation needs!

For development setup, debugging, regression tests, and implementation notes,
see [AGENTS.md](AGENTS.md).

## Wiggle bones

Select a bone and open **Properties > Wiggle Bones**. The Blender Wiggle 2-inspired
panel groups **Scene**, **Armature**, and **Bone** controls. Enable the bone, then
play the timeline to preview secondary motion without changing authored keyframes.

- **Head** and **Tail** each have mass, stiffness, damping, gravity/wind multipliers,
  and allowed stretch/compression. Higher stiffness follows the animated pose more
  closely; damping reduces oscillation. Existing bone endpoints include bend.
  Otherwise set **Tail Offset** in pixels (16 pixels = one block).
- MiBones with a bend joint also expose **Bend > Enabled**, with independent
  mass, stiffness, damping and gravity/wind settings. Bend follows animated
  angles and responds to driver acceleration without editing bend keyframes.
  Model axes/angle limits are respected. Bend is off by default and can be used
  independently of Head/Tail; saved bakes include its evaluated angles.
- **Connect head to parent tail** snaps a child to an enabled wiggle parent's tail
  and replaces head dynamics. **Chain** lets that child pull the parent's tail.
  Leave connection off for model parts attached somewhere other than the parent tip.
- Scene controls include gravity, quality, preroll, and continuing live physics
  across playback loops. Configure wind under the environment's **Wind** section.
  **Mute** restores authored motion; **Freeze** holds the evaluated world pose.
  Copy settings to selected bones or restore bone defaults from the same panel.
- Enable **Collisions** on the bone. Select a collider object, then enable
  **Wiggle Bones > Object Sphere Collider** and set its radius/local offset.
  Invisible collider spheres follow animation and scale. Bone head/tail spheres
  respond with friction and bounce. Alternatively, enable **Mesh Collisions**
  to use visible scene mesh surfaces, including animated bend/skinning, shape
  keys, saved cloth geometry and Array copies, without adding sphere colliders.
  The toggles are independent and share radius, friction and bounce.
  Collision affects enabled Head/Tail endpoints, not the entire bone or Bend
  joint; very fast moving surfaces may tunnel.
  Colliders attached to a bone or its descendants do not collide with that bone.
  Skinned meshes driven by that bone or its descendants are also excluded.
- **Bake All Wiggle Bones** pauses playback and saves all active bones together
  from frame 0 through the timeline end. A cancellable modal shows progress and
  prevents editing during sampling. Failed/cancelled bakes retain previous poses.
  Saved bakes provide exact repeatable scrubbing, camera previews, shadows, picking,
  and animation export, and survive project/object saves and undo.
- Live simulation advances with timeline time, not every viewport redraw.
  Seeking resets unbaked motion; **Reset Live Simulation** restarts it.
  **Clear All Bakes** returns to live preview. Rebake after animation, hierarchy,
  wind, or collider edits. Changed physics settings or frame rate and frames outside
  the saved range report an error instead of silently exporting a different pose.
  Animation export requires a complete bake. When cloth uses wiggle bones as
  animated pins or colliders, bake wiggle first, then bake cloth.

Limits: 256 enabled wiggle bones, 200,000 mesh collision triangles and a 64 MiB scene pose cache.
This is an original Blender-inspired implementation, not an addon port.
Baking stores evaluated world transforms, not editable rotation keyframes; bakes
are tied to the sampled scene and must be redone after duplicating/reparenting rigs.

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
  preserve the denser edge's detail. Use **Reverse target order** if needed.
  For groups containing selected faces, use corresponding surface patches,
  not closed surfaces or overlapping folds; do not weld a surface group to an
  edge group. A single-vertex group welds an edge seam to one point.
  Both panel tops can remain pinned. Animate joined panels together; animation
  that pulls their pinned anchors apart is rejected explicitly.
- Scene-mesh contact and cloth self-contact (vertex/triangle and edge/edge) are
  enabled by default. Hidden/non-rendering objects and editor helpers do not
  collide. Cloth objects interact through self-collision. Raise substeps and
  thickness for fast motion or thin geometry; this is an approximate position-based
  solver, not a guarantee against tunneling under arbitrary motion.
- Cloth **never simulates live**. In the Cloth modifier settings, click **Bake
  cloth** to solve all enabled cloth together from frame 0 through the timeline
  end (including pins, seams and animated colliders). Baking pauses playback and
  runs the solver on a background worker with a modal progress bar showing
  completed frames. Rendering continues during the bake.
  The popup prevents scene edits until baking finishes. Failed bakes keep the
  previous saved frames. Until baked, cloth adds no deformation.
  Playback, backward scrubbing and export read saved frames without running the
  solver.
  Bakes are saved with the project, so reopening does not require rebaking.
  **Clear bake** removes all scene cloth bakes and restores source rendering.
  Rebake after editing source geometry, bend, animation, pins, seams or colliders.
  Changes to cloth settings, mesh vertex counts or frame rate reject an outdated
  bake; frames outside the baked range also require rebaking. Scene edits do not
  silently start a new simulation. The frame-position cache is limited to 256 MiB
  per scene; reduce the timeline length or cloth vertex count if necessary.
  The viewport, camera feeds, shadows, picking and export share the deformation.
  Settings/groups survive project saves, object archives, duplication and undo.

Cloth is not inherited from parent modifiers. Disabling cloth restores normal
bend rendering. Cloth cannot coexist on the same
mesh with skinning, shape keys, shader wind or camera-facing/item-animation transforms. Animated
colliders can be skinned or bent. Changing topology requires reselecting groups.
Limits are 12,000 cloth source vertices per scene, 256 groups
per modifier, and 200,000 visible collider triangles. Invalid setups are reported
in the modifier panel rather than partially simulated.

## Procedural 3D effects

Open **Spawn > Effects** and choose **Lightning**, **Ring / Portal**, or
**Procedural Effect**. These are editable scene objects, not baked animations.

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

## Importing Mine-imator objects

### Scenery search order

For a scenery reference such as `castle.schematic`, import tries:

1. `castle.schematic.meshcache`
2. `castle.meshcache`
3. `castle.schematic`

An explicit `.meshcache` reference is loaded directly. The adjacent cache takes
priority even when older than the schematic, because it may contain edited
geometry. Object saves retain the actual cache used, so cache-only scenery remains portable. Ordinary schematic reloads
still apply their existing cache-freshness rules.

### Compatibility limits

This is feature/parameter conversion, not pixel-identical Mine-imator rendering.
Native effect kernels, light falloff, procedural noise, tone mapping and constraint
evaluation can differ. Copy Position uses native world-space constraint semantics;
the authored position is not additionally added to its offset. Heat distortion
uses native timeline timing. Cinematic bars use visible per-edge thickness, rather
than the source's offscreen rectangles and quality-dependent padding.

Unsupported objects retain placeholders, and unmapped settings/channels or missing
resources are reported in the import diagnostics. In particular:

- Torus/icosphere geometry has no import mapping.
- Builtin model states beyond the recognized rig selections, generated
  banners/patterns, armor dyes/trims and resource-pack overrides are not fully
  reconstructed.
- Packed SEUS/LabPBR material maps and LabPBR normals require channel conversion
  and are not applied as incompatible native maps; scalar materials are retained.
- Source additive camera brightness and color burn are not equivalent to the
  native operations and are not applied. VHS, vertex snapping, advanced bloom/DOF
  sampling controls, custom camera output sizes and renderer-specific shadow
  controls are not mapped.
- Chromatic aberration imports radial channel scaling, not the source's spectral
  blur or nonlinear channel distortion.
- Depth-ignore, unsupported scenery repeat/texture overrides and unsupported
  particle regions are reported rather than silently simulated.
- Background biome/resource selection, image probes and other unmapped
  environment channels require manual adjustment.

Audio is imported into the project's audio timeline, not the object hierarchy.
Overlapping triggers remain separate clips; missing sounds are reported without
discarding other tracks. Audio imports only on an explicit object spawn, not while
generating thumbnails or restoring scene objects. It survives project saves and
uses the same pitch/trim/loop timing during preview playback and video export.
Project-wide audio tracks are not embedded when exporting an individual `.nxObj`.

## Mine-imator Minecraft assets

The bundled 26.3 catalog includes 129 additional character models from the
Mine-imator Minecraft data archive.
Equipment models and special-block/prop models are available as standalone
objects in the **Equipment** and **Props** spawn categories, with model
previews and compatible texture variants. Equipment is not automatically
attached to a character yet.

## Contributing

Pull requests are welcome. For major changes, please open an issue first
to discuss what you would like to change.

## License

[MIT](https://choosealicense.com/licenses/mit/)

[Discord](https://discord.gg/eswvppFuAD)
