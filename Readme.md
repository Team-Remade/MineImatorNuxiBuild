# Mine Imator Nuxi

For all your Minecraft animation needs!

For development setup, debugging, regression tests, and implementation notes,
see [AGENTS.md](AGENTS.md).

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
