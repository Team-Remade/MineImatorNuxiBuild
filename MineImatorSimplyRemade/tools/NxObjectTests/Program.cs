using System.IO.Compression;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private static readonly MethodInfo ExtractMethod = typeof(NxObjectArchive).GetMethod("Extract",
        BindingFlags.Static | BindingFlags.NonPublic) ?? throw new MissingMethodException("NxObjectArchive.Extract");

    private static int Main(string[] args)
    {
        SelectionManager.Initialize();
        if (args.Contains("--water-terrain-graphics", StringComparer.Ordinal) ||
            args.Contains("--water-terrain-veldrid-graphics", StringComparer.Ordinal))
        {
            try
            {
                WaterTerrainGraphics(args.Contains("--water-terrain-veldrid-graphics", StringComparer.Ordinal));
                Console.WriteLine("PASS Water/terrain import batches, opaque occlusion, translucent depth and preview rendering");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--end-portals-graphics", StringComparer.Ordinal) ||
            args.Contains("--end-portals-veldrid-graphics", StringComparer.Ordinal))
        {
            try
            {
                EndPortalGraphics(args.Contains("--end-portals-veldrid-graphics", StringComparer.Ordinal));
                Console.WriteLine("PASS End portal/gateway geometry, import batches, rebuilding, shader pixels and deterministic animation");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--mob-heads-graphics", StringComparer.Ordinal))
        {
            try
            {
                MobHeadGraphics();
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--block-models-graphics", StringComparer.Ordinal))
        {
            try
            {
                BlockModelGraphics();
                Console.WriteLine("PASS Block model GPU geometry, per-face textures, UV orientation, override and archive reload");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--texture-sheets-graphics", StringComparer.Ordinal))
        {
            try
            {
                TextureSheetGraphics();
                Console.WriteLine("PASS Texture sheet GPU tile dimensions, pixels, alpha and material orientation");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--transparency-graphics", StringComparer.Ordinal))
        {
            try
            {
                TransparencyGraphics();
                Console.WriteLine("PASS Transparent previews, RGBA post effects, image/sequence exports and decoded ProRes MOV alpha");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--replace-asset-graphics", StringComparer.Ordinal))
        {
            try
            {
                LibraryReplacementGraphics();
                Console.WriteLine("PASS Library replacement imports, linked instances, seeking, persistence, undo and failure safety");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--wind-graphics", StringComparer.Ordinal) ||
            args.Contains("--wind-veldrid-graphics", StringComparer.Ordinal))
        {
            try
            {
                WindGraphics(args.Contains("--wind-veldrid-graphics", StringComparer.Ordinal));
                Console.WriteLine("PASS Wind GPU deformation, terrain masks, precedence, timeline seeking and render-pass parity");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--biome-graphics", StringComparer.Ordinal))
        {
            try
            {
                BiomeGraphics();
                Console.WriteLine("PASS Biome standalone blocks, merged scenery, liquid meshes, clones and live GPU pixels");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--opaque-leaves-graphics", StringComparer.Ordinal))
        {
            try
            {
                OpaqueLeavesGraphics();
                Console.WriteLine("PASS Opaque leaf GPU textures, live toggling, restoration and connected textures");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--sky-graphics", StringComparer.Ordinal))
        {
            try
            {
                SkyGraphics();
                Console.WriteLine("PASS Textured skies, cube UV compatibility, XYZ rotation, fog, depth and texture lifecycle");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--block-rotation-graphics", StringComparer.Ordinal))
        {
            try
            {
                BlockRotationGraphics();
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--emissive-graphics", StringComparer.Ordinal))
        {
            try
            {
                EmissiveGraphics();
                Console.WriteLine("PASS Imported emissive block positions, batching, transforms, duplication and material toggles");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--model-uv-graphics", StringComparer.Ordinal))
        {
            try
            {
                ModelUvGraphics();
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--spawn-preview-graphics", StringComparer.Ordinal))
        {
            try
            {
                SpawnPreviewGraphics();
                Console.WriteLine("PASS Spawn model previews, framing, isolated lighting and scene-state preservation");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--modifiers-graphics", StringComparer.Ordinal))
        {
            try
            {
                ModifierGraphics();
                Console.WriteLine("PASS Array OpenGL pixels, skinned copies, path placement, picking, outlines and shadows");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--paths-graphics", StringComparer.Ordinal))
        {
            try
            {
                PathGraphics();
                Console.WriteLine("PASS Path OpenGL geometry, editor-only guides, rebuilds and pixels");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--particles-graphics", StringComparer.Ordinal))
        {
            try
            {
                ParticleGraphics();
                Console.WriteLine("PASS Particle sprite textures, pixels, opacity, frames and camera-facing rotation");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        if (args.Contains("--effects-graphics", StringComparer.Ordinal))
        {
            try
            {
                EffectGraphics();
                Console.WriteLine("PASS Procedural effect OpenGL upload, animation, emission, empty/rebuild and pixel rendering");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
        (string Name, Action<Fixture> Run)[] tests =
        [
            ("Item options persistence, duplication, undo and non-keyframable channels", ItemOptionsPersistence),
            ("Item options deterministic motion, camera priority and instance transforms", ItemOptionsTransforms),
            ("Item options ImGui toggles and multi-selection without auto-key", ItemOptionsControls),
            ("Block model parent inheritance, child aliases and automatic textures", BlockModelResolution),
            ("Block model project copying and portable archive dependencies", BlockModelPersistence),
            ("Block model missing textures, invalid models and cycles", BlockModelValidation),
            ("Watermark defaults, persistence and disabled settings retention", WatermarkSettings),
            ("Watermark Enabled checkbox hides and restores controls", WatermarkControls),
            ("Frames selected export, roundtrip, broadcast, timing, replacement and seeking", FramesRoundtrip),
            ("Frames hierarchy, semantic properties, references and ambiguity", FramesMapping),
            ("Frames reordered shape keys, ancestor names, IK and rotation compatibility", FramesAdvancedMapping),
            ("Frames validation, atomic failures, assets and undo redo", FramesValidationHistory),
            ("Frames ImGui timeline context menu and localization", FramesContextMenu),
            ("Visibility appearance right-click manual keys and multi-selection auto keys", VisibilityControls),
            ("Visibility stepped seeking, inheritance, render cache and persistence", VisibilityTimeline),
            ("Texture sheet rectangular tile pixels, alpha, row orientation and reuse", TextureSheetPixels),
            ("Texture sheet invalid sizes, slots, missing files and legacy defaults", TextureSheetValidation),
            ("Texture sheet project, independent material slots, object export and undo", TextureSheetPersistence),
            ("Texture sheet ImGui modal, checkbox, tile controls and localization", TextureSheetControls),
            ("Minecraft skin download, username validation and immutable local storage", MinecraftSkinDownloads),
            ("Minecraft skin HTTP, profile, URL and PNG failures", MinecraftSkinFailures),
            ("Minecraft skin selection, preview resolution, stale requests and retry", MinecraftSkinSelection),
            ("Minecraft skin project copy, manifest persistence and deduplication", MinecraftSkinProjectStorage),
            ("Snap settings validation, modifier inversion and source-generated persistence", SnapPreferences),
            ("Snap relative/absolute lattices, all constraints and normal alignment math", SnapConstraints),
            ("Snap vertex, edge midpoint, face, origin, radius and camera clipping", SnapTargets),
            ("Snap occlusion, X-Ray, closest surface and triangulation seams", SnapOcclusion),
            ("Snap actual transforms, Ctrl/Shift, group center and parent conversion", SnapTransforms),
            ("Snap face alignment, restoration, constraints and snapshot lifecycle", SnapAlignment),
            ("Snap scale and rotation increments through actual gizmo drags", SnapScaleRotation),
            ("Snap viewport geometry collection, selection exclusion, hidden objects and pivots", SnapSceneCollection),
            ("Snap settings ImGui controls and localization", SnapControls),
            ("Snap input ownership, physical modifiers and overlay cancellation", SnapInput),
            ("Snap aligned transforms auto-key, undo and redo", SnapKeyframesHistory),
            ("Snap large-scene exact results and query performance", SnapLargeScene),
            ("Backup preference defaults and source-generated persistence", BackupPreferences),
            ("Backup live scene, metadata, assets and unsaved state isolation", BackupSnapshot),
            ("Backup interval boundaries, deferred capture, disable and project switching", BackupScheduling),
            ("Backup retention, changed limits and project isolation", BackupRetention),
            ("Backup failed capture/write preserves saves, rotation and retry interval", BackupFailures),
            ("Camera orbit defaults, transforms, look-at, poles and parented navigation", CameraOrbitTransforms),
            ("Camera orbit project/archive/library, duplication, undo and dirty detection", CameraOrbitPersistence),
            ("Camera orbit timeline discovery, read/write, interpolation and seeking", CameraOrbitTimeline),
            ("Camera orbit ImGui enable/disable, show/hide and multi-selection", CameraOrbitControls),
            ("Camera blades defaults, ranges, persistence, duplication and undo", CameraBladePersistence),
            ("Camera blades timeline discovery, interpolation and seeking", CameraBladeTimeline),
            ("Camera blades ImGui controls and multi-selection", CameraBladeControls),
            ("Lens dirt settings, persistence, packaging, duplication, undo and timeline", LensDirtPersistence),
            ("Lens dirt ImGui texture and percentage controls", LensDirtControls),
            ("Vignette settings, persistence, duplication, undo and timeline", VignettePersistence),
            ("Vignette ImGui percentage sliders and color picker", VignetteControls),
            ("Chromatic aberration settings, persistence, duplication, undo and timeline", ChromaticAberrationPersistence),
            ("Distort settings, persistence, duplication, undo and timeline", DistortPersistence),
            ("Distort ImGui percentage sliders and checkbox", DistortControls),
            ("Chromatic aberration ImGui percentage sliders and checkbox", ChromaticAberrationControls),
            ("Wind defaults, project/object persistence, duplication, undo, animation and classification", WindPersistence),
            ("Biome catalog, independent custom colors, project persistence and reset", BiomeSettings),
            ("Opaque leaf defaults, persistence, texture selection and pixels", OpaqueLeavesSettings),
            ("Textured sky defaults, project persistence and background animation", SkySettings),
            ("Transparent background defaults, persistence and MOV extension", TransparencySettings),
            ("UI scale persistence, limits, fonts, metrics and independent contexts", UiScaleTests),
            ("Library replacement matching, rest poses, values and independent keyframes", LibraryReplacementMapping),
            ("Library replacement unmatched branches, ambiguity, ordering and references", LibraryReplacementRetention),
            ("Library replacement library-only and scene undo/redo", LibraryReplacementHistory),
            ("Model part bundled catalogs, single-part exclusion and stable paths", ModelPartCatalogTests),
            ("MI import paths, points, references, sparse poses and repeated loads", MiImportPaths),
            ("MI import static current values and invalid path settings", MiImportStatic),
            ("MI import background colors, toggles, sparse keys, easing and persistence", MiImportBackground),
            ("MI import embedded particle presets, controls and references", MiImportParticles),
            ("MI import unsupported particles report warnings and retain other objects", MiImportWarnings),
            ("Scene tree root and batch insertion ordering and no-ops", SceneTreeOrdering),
            ("Scene tree child ordering, mixed parents and subtree preservation", SceneTreeParenting),
            ("Scene tree cyclic, stale, hidden and runtime target rejection is atomic", SceneTreeSafety),
            ("Scene tree move up/down blocks, boundaries and multiple parents", SceneTreeShift),
            ("Scene tree armor fitting and ordinary parenting compatibility", SceneTreeEquipment),
            ("Scene tree undo/redo ordering, hierarchy, selection and identity", SceneTreeHistory),
            ("Scene tree saved ordering, IDs and context operations", SceneTreePersistence),
            ("Scene tree grouping preserves sibling position, order and descendants", SceneTreeGrouping),
            ("Scene tree ImGui multi-drag, parenting, hover expansion and branch controls", SceneTreeInteraction),
            ("Subtree, transforms, keyframes and material preservation", Subtree),
            ("Same-basename resource collision and deduplication", BasenameCollision),
            ("JSON model and external mimodel texture closure", JsonClosure),
            ("JSON object-form texture dependency closure", JsonObjectReferences),
            ("JSON object-form texture dependency traversal rejection", JsonObjectReferenceTraversal),
            ("Mine-imator numeric texture sentinels are not file resources", MiTextureSentinels),
            ("glTF buffer/image URI remapping and embedded data", GltfClosure),
            ("GLB external image closure preserves binary chunks", GlbClosure),
            ("OBJ/MTL dependency closure with texture options", ObjClosure),
            ("Missing direct resource preserves atomic destination", MissingResource),
            ("Missing transitive resource preserves atomic destination", MissingDependency),
            ("Opaque model export rejects atomically with conversion guidance", UnsupportedExport),
            ("Opaque models in crafted archives rejected", UnsupportedArchive),
            ("Safe STL geometry remains supported", StlGeometry),
            ("Archive format/version rejection", Versions),
            ("ZIP traversal and Windows path rejection", ZipTraversal),
            ("Manifest resource traversal rejection", ManifestTraversal),
            ("Duplicate ZIP entries including case aliases rejected", DuplicateEntries),
            ("ZIP symbolic link entries rejected", SymbolicLinkEntry),
            ("Missing manifest/resource rejection", MissingArchiveEntries),
            ("Model dependency traversal and remote URI rejection", DependencyTraversal),
            ("Child SourceNodePath persistence and standalone selection", SourceNodePaths),
            ("Stable source selection survives sibling deletion and reparenting", StableSourceNodePath),
            ("Exact source hierarchy restoration preserves deletions and node identity", ExactSourceHierarchy),
            ("Particle library transitive/cyclic closure", ParticleClosure),
            ("Overlapping particle library roots deduplicated in both discovery orders", ParticleOverlappingRoots),
            ("Missing particle library entry preserves destination", MissingParticleLibrary),
            ("Repeated extraction has independent resources and DTOs", IndependentExtraction),
            ("GPU-free repeated import remaps IDs and internal references", IndependentImport),
            ("Animated material texture resource closure", AnimatedTexture),
            ("Missing animated texture preserves atomic destination", MissingAnimatedTexture),
            ("Animated texture manifest path validation", AnimatedTextureTraversal),
            ("Animated font dependency closure and built-in sentinel", AnimatedFont),
            ("Rotation-to-bend world axes, multiplier, degree offset, blend and no drift", RotationBendMapping),
            ("Rotation-to-bend disabled and missing target reset evaluated pose", RotationBendReset),
            ("Rotation-to-bend limits, inversion and unsupported axes", RotationBendLimits),
            ("Rotation-to-bend follows source copy-rotation dependencies", RotationBendDependencies),
            ("Rotation-to-bend cyclic drivers and dependents are skipped", RotationBendCycles),
            ("Rotation-to-bend capture offset preserves blended stack pose", RotationBendCaptureOffset),
            ("Rotation-to-bend source-generated archive and runtime persistence", RotationBendPersistence),
            ("Rotation-to-bend extracts rotation from nonuniformly scaled rows", RotationBendScaledRows),
            ("Rotation-to-bend enabling earlier driver captures stack prefix pose", RotationBendEnableCapture),
            ("Rotation-to-bend inherited pose dependency precedes partial blend", RotationBendInheritedPose),
            ("Rotation-to-bend all axis masks, independent sources and toggles", RotationBendAxisMasks),
            ("Rotation-to-bend masked axes respect limits and unsupported axes", RotationBendMaskLimits),
            ("Rotation-to-bend legacy fallback and explicit checkbox conversion", RotationBendMaskLegacy),
            ("Rotation-to-bend multi-axis capture preserves blended stack", RotationBendMaskCapture),
            ("Rotation-to-bend mask and nullable source archive persistence and clone", RotationBendMaskPersistence),
            ("Rotation-to-bend timeline mask reads, writes and discrete animation", RotationBendMaskTimeline),
            ("Procedural effect deterministic seek, seed and stepped/smooth motion", EffectDeterminism),
            ("Procedural effect ring closure, reveal and branch attachment", EffectPaths),
            ("Procedural effect tube counts, winding, normals, UVs and bounds", EffectTubes),
            ("Procedural effect bounded parameters and invalid input rejection", EffectLimits),
            ("Procedural effect source-generated archive, library and duplicate persistence", EffectPersistence),
            ("Procedural effect snapshot restoration and dirty fingerprint", EffectSnapshots),
            ("Procedural effect timeline discovery, read/write and interpolation", EffectTimeline),
            ("Path spawn, ordered points, arc length, smooth and closed sampling", PathSampling),
            ("Path flat/tube geometry, winding, normals, UVs, roll and scale", PathMeshes),
            ("Path limits, degenerate curves and invalid input rejection", PathLimits),
            ("Path following world transforms, clamping, wrapping and no drift", PathFollowing),
            ("Path following dependency ordering, copy constraints and cycles", PathDependencies),
            ("Path archive, import, library, duplicate and reference persistence", PathPersistence),
            ("Path snapshots, point ordering and dirty detection", PathSnapshots),
            ("Path timeline discovery, interpolation and seek reproducibility", PathTimeline),
            ("Array axes, spacing, compounded transforms and non-destructive subtree copies", ModifierTransforms),
            ("Array path alignment, spacing, wrapping, point edits and unavailable targets", ModifierPaths),
            ("Array stacks, inherited instances, safety cap and invalid transform rejection", ModifierStacks),
            ("Follow Path modifier and Array offset composition, toggles and animation", ModifierFollowPath),
            ("Follow Path modifier migration, persistence, references and special objects", ModifierFollowPersistence),
            ("Array archive, library, duplicate references, snapshots and dirty detection", ModifierPersistence),
            ("Array viewport render collection and shadow cache invalidation", ModifierRenderCollection),
            ("Modifier context values, auto keys, interpolation, identity and persistence", ModifierKeyframes),
            ("Modifier ImGui context menu copy, paste, reset and manual keyframes", ModifierContextInteraction),
            ("Property reset Shift-right-click, defaults, groups, selection, auto-key and persistence", PropertyResetInteraction),
            ("All 15 bundled particle presets, assets and finite simulation", ParticleBundled),
            ("Particle spawn menu selection, search and all provided presets", ParticleSpawnMenu),
            ("Particle per-minute rate, multi-type burst, restart and amount limit", ParticleEmission),
            ("Particle random ranges, per-axis motion, color, alpha and scale", ParticleParameters),
            ("Particle regions, ground/custom/sphere bounds, bounce and destruction", ParticleCollisions),
            ("Particle animation frames, reverse, loop, ping-pong and destruction", ParticleAnimation),
            ("Particle deterministic fixed-step seek, freeze and runtime cleanup", ParticleRuntime),
            ("Particle active sprite save/load excludes runtime roots and library entries", ParticleRuntimePersistence),
            ("Particle direct and grouped deletion cleans sprites through undo/redo", ParticleDeletion),
            ("Particle project/archive, library, duplicate, snapshot and reference persistence", ParticlePersistence),
            ("Particle preset timeline controls and legacy channel preservation", ParticleTimeline),
            ("Particle invalid and unsupported preset rejection", ParticleInvalid),
            ("World importer player position from legacy and 26.1+ Java saves", WorldPlayerPositionReading),
            ("World importer default block states for 26.1+ bare palette entries", WorldDefaultBlockStates)
        ];
        if (args.Contains("--world-player", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("World importer player", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--world-states", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("World importer default block states", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--block-models", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Block model", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--visibility", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Visibility", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--property-reset", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Property reset", StringComparison.Ordinal) ||
                test.Name.StartsWith("Modifier ImGui context menu", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--watermark", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Watermark", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--watermark-graphics", StringComparer.Ordinal))
            tests = [("Watermark GPU alignment, padding, scale, opacity, orientation and export parity", WatermarkGraphics)];
        if (args.Contains("--texture-sheets", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Texture sheet", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--snapping", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Snap ", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--camera-orbit", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Camera orbit", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--camera-blades", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Camera blades", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--camera-blades-graphics", StringComparer.Ordinal))
            tests = [("Camera blades rendered aperture, angle, stretch and default parity", CameraBladeGraphics)];
        if (args.Contains("--lens-dirt", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Lens dirt", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--lens-dirt-graphics", StringComparer.Ordinal))
            tests = [("Lens dirt GPU source toggles, texture orientation, radius, intensity, power and alpha", LensDirtGraphics)];
        if (args.Contains("--vignette", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Vignette", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--vignette-graphics", StringComparer.Ordinal))
            tests = [("Vignette GPU radius, softness, strength, color, stacking, alpha and preview/export parity", VignetteGraphics)];
        if (args.Contains("--chromatic-aberration", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Chromatic aberration", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--distort", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Distort", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--distort-graphics", StringComparer.Ordinal))
            tests = [("Distort GPU signed distortion, zoom, repeat, alpha, stacking and preview/export parity", DistortGraphics)];
        if (args.Contains("--chromatic-aberration-graphics", StringComparer.Ordinal))
            tests = [("Chromatic aberration GPU channel offsets, distortion, blur, stacking, alpha and preview/export parity", ChromaticAberrationGraphics)];
        if (args.Contains("--wind", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Wind", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--biomes", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Biome", StringComparison.Ordinal) ||
                test.Name.StartsWith("Opaque leaf", StringComparison.Ordinal) ||
                test.Name.StartsWith("Textured sky", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--mi-import", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("MI import", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--model-parts", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Model part", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--replace-asset", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Library replacement", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--particles", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Particle", StringComparison.Ordinal) ||
                test.Name.StartsWith("All 15 bundled particle", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--paths", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Path", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--frames", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Frames ", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--modifiers", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Array", StringComparison.Ordinal) ||
                test.Name.StartsWith("Follow Path modifier", StringComparison.Ordinal) ||
                test.Name.StartsWith("Modifier", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--scene-tree", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Scene tree", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--scene-tree-ui", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Scene tree ImGui", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--ui-scale", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("UI scale", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--backups", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Backup", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--skins", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Minecraft skin", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--items", StringComparer.Ordinal))
            tests = tests.Where(test => test.Name.StartsWith("Item options", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--items-graphics", StringComparer.Ordinal))
            tests = [("Item options spawned settings, live 3D geometry, duplication and undo", ItemOptionsGraphics)];
        int failures = 0;
        foreach (var test in tests)
        {
            using var fixture = new Fixture();
            var library = ProjectManager.Instance.Manifest.ObjectLibrary;
            var original = library.ToArray();
            library.Clear();
            try
            {
                test.Run(fixture);
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"FAIL {test.Name}: {exception.GetType().Name}: {exception.Message}");
                Console.WriteLine(exception.StackTrace);
            }
            finally
            {
                library.Clear();
                library.AddRange(original);
            }
        }
        Console.WriteLine($"RESULT {tests.Length - failures}/{tests.Length} passed; {failures} failed.");
        return failures == 0 ? 0 : 1;
    }

    private static unsafe void EffectGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization failed");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Procedural effect smoke test", null, null);
        try
        {
            Check(window != null, "OpenGL context creation failed");
            glfw.MakeContextCurrent(window);
            using var gl = Silk.NET.OpenGL.GL.GetApi(glfw.GetProcAddress);
            using var ubo = new SceneUniformBuffer(gl);
            Mesh.SceneUBO = ubo;
            ubo.Upload(mat4.Identity, new vec3(0.1f), vec3.UnitY, vec3.Zero, vec3.UnitY, vec3.Zero,
                vec3.UnitY, vec3.Zero, 0, false, false, false);
            using var preview = new PreviewRenderer(gl, 160) { Distance = 6f, AutoRotateSpeed = 0f };
            preview.Render([], "effects", 0);
            byte[] baseline = ReadPixels();
            var viewport = new Viewport();
            var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };
            foreach (var settings in new[] { ProceduralEffectSettings.Lightning, ProceduralEffectSettings.Portal })
            {
                var effect = spawn.SpawnProceduralEffectObject("graphics test", settings)!;
                var mesh = effect.Visuals.Single();
                try
                {
                    Check(mesh.BoundingSphereRadius > 0f, "Uploaded bounds");
                    byte[] Render()
                    {
                        preview.Render(effect.Visuals, "effects", 0, centerMeshes: true);
                        return ReadPixels();
                    }
                    var first = Render();
                    int changedPixels = 0;
                    for (int i = 0; i < first.Length; i += 4)
                        if (first[i] != baseline[i] || first[i + 1] != baseline[i + 1] || first[i + 2] != baseline[i + 2])
                            changedPixels++;
                    Check(changedPixels > 32, $"Effect must actually draw visible pixels; got {changedPixels}");
                    effect.EvaluateGeometry(0.75f);
                    Check(!first.SequenceEqual(Render()), "Animated GPU output must change");
                    effect.EvaluateGeometry(0f);
                    Check(first.SequenceEqual(Render()), "GPU output must reproduce after backwards seek");
                    var versionField = typeof(Mesh).GetField("_shadowGeometryVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    int version = (int)versionField.GetValue(mesh)!;
                    effect.EvaluateGeometry(0f);
                    Equal(version, (int)versionField.GetValue(mesh)!, "Repeated views must not re-upload geometry");
                    effect.MaterialSettings!.EmissionEnergy = 0f;
                    effect.ApplyMaterialSettingsToMeshes();
                    Check(!first.SequenceEqual(Render()), "Emission energy must affect rendered output");
                    effect.Settings = settings with { Reveal = 0f };
                    effect.EvaluateGeometry(0f);
                    Check(baseline.SequenceEqual(Render()), "Zero reveal must render no pixels");
                    effect.Settings = settings;
                    effect.MaterialSettings.EmissionEnergy = 5f;
                    effect.ApplyMaterialSettingsToMeshes();
                    effect.EvaluateGeometry(0f);
                    Check(first.SequenceEqual(Render()), "Empty geometry must rebuild correctly");
                    Equal(Silk.NET.OpenGL.GLEnum.NoError, gl.GetError(), "OpenGL error");
                    Console.WriteLine($"PASS {settings.Shape}: {changedPixels} visible pixels; {mesh.Vertices.Count} vertices");
                }
                finally
                {
                    mesh.Dispose();
                    viewport.SceneObjects.Remove(effect);
                }
            }
            Mesh.SceneUBO = null;

            byte[] ReadPixels()
            {
                byte[] pixels = new byte[160 * 160 * 4];
                gl.BindTexture(Silk.NET.OpenGL.GLEnum.Texture2D, preview.ColorTexture);
                fixed (byte* pointer = pixels)
                    gl.GetTexImage(Silk.NET.OpenGL.GLEnum.Texture2D, 0, Silk.NET.OpenGL.GLEnum.Rgba,
                        Silk.NET.OpenGL.GLEnum.UnsignedByte, pointer);
                gl.BindTexture(Silk.NET.OpenGL.GLEnum.Texture2D, 0);
                return pixels;
            }
        }
        finally
        {
            Mesh.SceneUBO = null;
            if (window != null) glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }

    private static void EffectDeterminism(Fixture f)
    {
        var s = ProceduralEffectSettings.Lightning;
        var first = ProceduralEffectGeometry.GeneratePaths(s, 1.25f);
        ProceduralEffectGeometry.GeneratePaths(s, 7f);
        var replay = ProceduralEffectGeometry.GeneratePaths(s, 1.25f);
        Equal(first.Count, replay.Count, "Seek must preserve branch count");
        for (int i = 0; i < first.Count; i++)
            Check(first[i].Points.SequenceEqual(replay[i].Points), "Random-access evaluation must reproduce every point");
        var next = ProceduralEffectGeometry.GeneratePaths(s, 1.26f);
        Check(first[0].Points.SequenceEqual(next[0].Points), "Stepped motion must hold within a refresh interval");
        var changed = ProceduralEffectGeometry.GeneratePaths(s, 1.35f);
        Check(!first[0].Points.SequenceEqual(changed[0].Points), "Next refresh must change the bolt");
        changed = ProceduralEffectGeometry.GeneratePaths(s with { Seed = 42 }, 1.25f);
        Check(!first[0].Points.SequenceEqual(changed[0].Points), "Seed must affect geometry");
        s = ProceduralEffectSettings.Portal;
        first = ProceduralEffectGeometry.GeneratePaths(s, 1.25f);
        next = ProceduralEffectGeometry.GeneratePaths(s, 1.26f);
        Check(!first[0].Points.SequenceEqual(next[0].Points), "Smooth motion must evolve inside an interval");
        var offset = ProceduralEffectGeometry.GeneratePaths(s with { TimeOffset = 1f }, 0.25f);
        Check(first[0].Points.SequenceEqual(offset[0].Points), "Time offset must agree with direct seek");
        var frozen = s with { RefreshRate = 0f };
        Check(ProceduralEffectGeometry.GeneratePaths(frozen, 0f)[0].Points
            .SequenceEqual(ProceduralEffectGeometry.GeneratePaths(frozen, 10f)[0].Points), "Zero rate freezes motion");
    }

    private static void EffectPaths(Fixture f)
    {
        var s = ProceduralEffectSettings.Lightning;
        var paths = ProceduralEffectGeometry.GeneratePaths(s, 2f);
        Equal(s.Branches + 1, paths.Count, "Full reveal branch count");
        Equal(vec3.Zero, paths[0].Points[0], "Lightning start must be anchored");
        Equal(new vec3(s.EndX, s.EndY, s.EndZ), paths[0].Points[^1], "Lightning end must be anchored");
        foreach (var branch in paths.Skip(1))
        {
            bool attached = false;
            var main = paths[0].Points;
            for (int i = 0; i < main.Length - 1; i++)
            {
                vec3 delta = main[i + 1] - main[i];
                float t = Math.Clamp(vec3.Dot(branch.Points[0] - main[i], delta) / delta.LengthSqr, 0f, 1f);
                if ((branch.Points[0] - main[i] - delta * t).Length < 0.00001f) attached = true;
            }
            Check(attached, "Each branch must start on its parent path");
        }
        Equal(0, ProceduralEffectGeometry.GeneratePaths(s with { Reveal = 0f }, 0f).Count, "Hidden reveal");
        var half = ProceduralEffectGeometry.GeneratePaths(s with { Reveal = 0.5f, Noise = 0f, Branches = 0 }, 0f)[0];
        Near(new vec3(0, 1.5f, 0), half.Points[^1], "Reveal must trim rather than rescale the full path");
        var portal = ProceduralEffectGeometry.GeneratePaths(ProceduralEffectSettings.Portal, 0.7f)[0];
        Check(portal.Closed, "Full ring must be closed");
        Equal(portal.Points[0], portal.Points[^1], "Ring seam exact equality");
        portal = ProceduralEffectGeometry.GeneratePaths(ProceduralEffectSettings.Portal with { Reveal = 0.5f }, 0.7f)[0];
        Check(!portal.Closed && portal.Points[0] != portal.Points[^1], "Partial ring must be an open arc");
        var helix = ProceduralEffectGeometry.GeneratePaths(s with
        {
            Shape = EffectPathShape.Helix, Noise = 0, Branches = 0, Turns = 2, Radius = 2
        }, 0f)[0];
        Near(2f, new vec3(helix.Points[0].x, 0, helix.Points[0].z).Length, "Helix radius");
        Near(3f, helix.Points[^1].y, "Helix axial length");
    }

    private static void EffectTubes(Fixture f)
    {
        var mesh = new Mesh(null!);
        var s = ProceduralEffectSettings.Lightning with { Noise = 0, Branches = 0, Taper = 0 };
        ProceduralEffectGeometry.Populate(mesh, s, 0f);
        Equal(s.Segments * s.Sides * 6 + s.Sides * 6, mesh.Vertices.Count, "Tube plus two caps triangle budget");
        Equal(mesh.Vertices.Count, mesh.Normals.Count, "Normal stream count");
        Equal(mesh.Vertices.Count, mesh.TexCoords.Count, "UV stream count");
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            Check(float.IsFinite(mesh.Vertices[i].LengthSqr), "Finite positions");
            Near(1f, mesh.Normals[i].Length, "Unit normals");
            Check(mesh.TexCoords[i].x is >= 0f and <= 1f && mesh.TexCoords[i].y is >= 0f and <= 1f, "UV domain");
            if (i < s.Segments * s.Sides * 6)
                Near(s.Width / 2f, new vec3(mesh.Vertices[i].x, 0, mesh.Vertices[i].z).Length, "Width must be diameter");
        }
        for (int i = 0; i < mesh.Vertices.Count; i += 3)
        {
            vec3 normal = vec3.Cross(mesh.Vertices[i + 1] - mesh.Vertices[i], mesh.Vertices[i + 2] - mesh.Vertices[i]);
            Check(vec3.Dot(normal, mesh.Normals[i]) > 0f, "Outward triangle winding, including caps");
        }
        s = ProceduralEffectSettings.Portal;
        ProceduralEffectGeometry.Populate(mesh, s, 0.7f);
        Equal(s.Segments * s.Sides * 6, mesh.Vertices.Count, "Closed loops have no end caps");
        for (int j = 0; j < s.Sides; j++)
        {
            int first = j * 6;
            int last = ((s.Segments - 1) * s.Sides + j) * 6 + 2;
            Equal(mesh.Vertices[first], mesh.Vertices[last], "Welded loop positions");
            Equal(mesh.Normals[first], mesh.Normals[last], "Welded loop normals");
        }
        ProceduralEffectGeometry.Populate(mesh, s with { Reveal = 0 }, 0f);
        Equal(0, mesh.Vertices.Count, "Empty reveal clears old geometry");
        Equal(0, mesh.Normals.Count, "Empty reveal clears old normals");
        Equal(0, mesh.TexCoords.Count, "Empty reveal clears old UVs");
    }

    private static void EffectLimits(Fixture f)
    {
        var s = new ProceduralEffectSettings
        {
            Segments = int.MaxValue, Sides = int.MaxValue, Branches = int.MaxValue, NoiseOctaves = int.MaxValue
        };
        var mesh = new Mesh(null!);
        ProceduralEffectGeometry.Populate(mesh, s, 0f);
        Equal(84168, mesh.Vertices.Count, "Worst-case vertex count must be capped exactly");
        s = s.Validated();
        Equal(128, s.Segments, "Segment cap");
        Equal(12, s.Sides, "Side cap");
        Equal(16, s.Branches, "Branch cap");
        Reject(() => (s with { Noise = float.NaN }).Validated(), "Nonfinite noise must fail explicitly");
        Reject(() => (s with { EndX = float.PositiveInfinity }).Validated(), "Nonfinite endpoint must fail explicitly");
        Equal(0, ProceduralEffectGeometry.GeneratePaths(s with { EndY = 0, Noise = 0, Branches = 0 }, 0f).Count,
            "Coincident line endpoints must not generate degenerate geometry");
        foreach (var shape in Enum.GetValues<EffectPathShape>())
        {
            ProceduralEffectGeometry.Populate(mesh, s with { Shape = shape, EndY = 0, Noise = 0, Branches = 0 }, 0f);
            Check(mesh.Vertices.All(p => float.IsFinite(p.LengthSqr)), "Zero-axis fallback must stay finite");
        }
    }

    private static void EffectPersistence(Fixture f)
    {
        var effect = new ProceduralEffectSceneObject
        {
            Name = "Portal", Settings = ProceduralEffectSettings.Portal with { Seed = 734, Noise = 0.23f, Reveal = 0.75f }
        };
        effect.Keyframes["effect.radius"] = [new ObjectKeyframe { Frame = 4, Value = 2f }];
        var (manifest, _) = f.Roundtrip(effect);
        Equal(effect.Settings, manifest.Root.ProceduralEffect, "Source-generated settings roundtrip");
        Check(manifest.Root.Keyframes.ContainsKey("effect.radius"), "Archive effect keyframes");
        var viewport = new Viewport();
        var spawn = new SpawnMenu { Viewport = viewport };
        var imported = ProjectSceneSerializer.SpawnObjectFromEntry(manifest.Root, viewport, spawn) as ProceduralEffectSceneObject;
        Check(imported != null, "Import must preserve runtime type");
        Equal(effect.Settings, imported!.Settings, "Import settings");
        Check(effect.ObjectId != imported.ObjectId, "Independent imported ID");
        var libraryClone = typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive", BindingFlags.Static | BindingFlags.NonPublic)!;
        var clonedEntry = (ProjectSceneObjectEntry)libraryClone.Invoke(null, [manifest.Root])!;
        Equal(effect.Settings, clonedEntry.ProceduralEffect, "Object library clone settings");
        var duplicate = typeof(SceneTree).GetMethod("CreateSceneObjectDuplicate", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var clone = (ProceduralEffectSceneObject)duplicate.Invoke(new SceneTree(), [effect, new Dictionary<string, string>()])!;
        Equal(effect.Settings, clone.Settings, "Scene duplicate settings");
        clone.Settings = clone.Settings with { Radius = 8f };
        Check(effect.Settings.Radius != clone.Settings.Radius, "Immutable settings keep duplicates independent");
        Near(effect.MaterialSettings!.EmissionEnergy, clone.MaterialSettings!.EmissionEnergy, "Duplicate material emission");
    }

    private static void EffectSnapshots(Fixture f)
    {
        var effect = new ProceduralEffectSceneObject { Settings = ProceduralEffectSettings.Portal };
        var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(effect);
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { effect }])!;
        ulong originalHash = Hash();
        foreach (var control in ProceduralEffectSettings.Controls)
        {
            var original = effect.Settings;
            float value = control.Read(original);
            effect.Settings = control.Set(original, value == control.Max ? control.Min : control.Max);
            Check(originalHash != Hash(), $"Dirty detection must include {control.Path}");
            effect.Settings = original;
        }
        effect.Settings = effect.Settings with { Radius = 12f };
        typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [effect, snapshot, null]);
        Equal(ProceduralEffectSettings.Portal, effect.Settings, "Undo snapshot restoration");
        Equal(originalHash, Hash(), "Restored fingerprint");
    }

    private static void EffectTimeline(Fixture f)
    {
        var timeline = new Timeline();
        var effect = new ProceduralEffectSceneObject();
        var get = typeof(Timeline).GetMethod("GetPropertyValue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var set = typeof(Timeline).GetMethod("SetPropertyValue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var interpolate = typeof(Timeline).GetMethod("InterpolateKeyframes", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var paths = ((IEnumerable<string>)typeof(Timeline).GetMethod("GetKeyframePropertyPaths", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [effect])!).ToHashSet();
        foreach (var control in ProceduralEffectSettings.Controls)
        {
            Check(paths.Contains(control.Path), $"Discover {control.Path}");
            set.Invoke(timeline, [effect, control.Path, control.Max]);
            Near(control.Max, Convert.ToSingle(get.Invoke(timeline, [effect, control.Path])), $"Read/write {control.Path}");
            var keys = new List<TimelineKeyframe>
            {
                new() { Frame = 0, Value = control.Min }, new() { Frame = 10, Value = control.Max }
            };
            float expected = control.Discrete ? control.Min : (control.Min + control.Max) * 0.5f;
            Near(expected, Convert.ToSingle(interpolate.Invoke(timeline, [keys, control.Path, 5, false])),
                $"Interpolation {control.Path}");
        }
    }

    private static void MiTextureSentinels(Fixture f)
    {
        string texture = Path.GetFullPath(f.File("textures/custom.png", "custom texture"));
        var resources = new Dictionary<string, MiResource>
        {
            ["custom-texture"] = new() { Filename = "textures/custom.png" }
        };
        foreach (string type in new[] { "block", "cube" })
        foreach (string reference in new[] { "-3", "-1", "null", "\"null\"", "\"default\"", "\"custom-texture\"", "\"textures/custom.png\"" })
        {
            using var settings = JsonDocument.Parse("{\"name\":\"redstone_lamp\",\"tex\":" + reference + "}");
            var template = new MiTemplate
            {
                Type = type, Block = settings.RootElement, Shape = settings.RootElement
            };
            var obj = MiObjectTemplateFactory.Create(null!, null!, template, new MiTimeline(), resources, f.Root)!;
            string expected = reference is "\"custom-texture\"" or "\"textures/custom.png\"" ? texture : "";
            Equal(expected, obj.AlbedoTexturePath, $"{type} texture reference {reference}");
            var (manifest, directory) = f.Roundtrip(obj);
            if (expected.Length == 0)
                Equal("", manifest.Root.AlbedoTexturePath, "Built-in material acquired a file dependency");
            else
                Equal("custom texture", File.ReadAllText(Resource(directory, manifest.Root.AlbedoTexturePath)), "Custom texture missing");
        }
    }

    private static void ExactSourceHierarchy(Fixture f)
    {
        var parent = Node("source root");
        parent.RestoreExactChildren = true;
        var first = Node("same name");
        first.SourceNodePath = [0];
        var second = Node("same name");
        second.SourceNodePath = [1];
        var helper = Node("runtime helper");
        helper.IsRuntimeTransient = true;
        parent.AddChild(first);
        parent.AddChild(second);
        parent.AddChild(helper);
        var match = typeof(ProjectSceneSerializer).GetMethod("FindMatchingChild", BindingFlags.Static | BindingFlags.NonPublic)!;
        var selected = new ProjectSceneObjectEntry { Name = "same name", SourceNodePath = [1], RestoreExactChildren = true };
        Check(ReferenceEquals(second, match.Invoke(null, [parent, selected, new HashSet<SceneObject>()])),
            "Matching must use source identity instead of ambiguous names or types.");
        selected.SourceNodePath = null;
        Check(match.Invoke(null, [parent, selected, new HashSet<SceneObject>()]) == null,
            "An authored node must not reuse an unrelated source-generated node.");

        var restore = typeof(ProjectSceneSerializer).GetMethod("RestoreChildren", BindingFlags.Static | BindingFlags.NonPublic)!;
        var restoreState = Activator.CreateInstance(restore.GetParameters()[5].ParameterType, nonPublic: true)!;
        var viewport = new Viewport();
        restore.Invoke(null, [new ProjectSceneObjectEntry { RestoreExactChildren = true }, parent, viewport,
            new SpawnMenu { Viewport = viewport }, vec3.Zero, restoreState]);
        Equal(1, parent.Children.Count, "Source children absent from the saved hierarchy must stay deleted");
        Check(ReferenceEquals(helper, parent.Children[0]), "Runtime helpers must be retained.");
        Check(ProjectSceneSerializer.SerializeObjectForLibrary(parent).RestoreExactChildren,
            "Exact hierarchy restoration must survive project serialization.");
    }

    private static MiBoneSceneObject BendBone(string name, vec3 angle)
    {
        var bone = new MiBoneSceneObject { Name = name, ObjectId = Guid.NewGuid().ToString("N"), ObjectType = "Bone" };
        bone.SetBendParameters(new BendParams
        {
            Angle = angle, AxisX = true, AxisY = true, AxisZ = true,
            DirectionMin = new vec3(-180f), DirectionMax = new vec3(180f),
            Part = BendPart.Upper, BendSize = 4f
        }, 1f);
        return bone;
    }

    private static vec3 EvaluatedBend(MiBoneSceneObject bone) => (vec3)(typeof(MiBoneSceneObject)
        .GetMethod("GetEvaluatedEditableBendAngle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("MiBoneSceneObject.GetEvaluatedEditableBendAngle")).Invoke(bone, null)!;

    private static void Near(float expected, float actual, string message) =>
        Check(float.IsFinite(actual) && MathF.Abs(expected - actual) < 0.002f,
            $"{message}: expected '{expected}', got '{actual}'.");

    private static void Near(vec3 expected, vec3 actual, string message)
    {
        for (int axis = 0; axis < 3; axis++) Near(expected[axis], actual[axis], $"{message}, axis {axis}");
    }

    private static void RotationBendMapping(Fixture f)
    {
        var parent = Node("rotated source parent");
        parent.SetLocalRotation(new vec3(0f, 0f, 30f) * (MathF.PI / 180f));
        var source = Node("scaled source");
        source.SetLocalRotation(new vec3(20f, -15f, 10f) * (MathF.PI / 180f));
        source.SetLocalScale(new vec3(2f, 3f, 4f));
        parent.AddChild(source);
        vec3 authored = new(4f, 8f, 12f), worldDegrees = new(20f, -15f, 40f);
        for (int sourceAxis = 0; sourceAxis < 3; sourceAxis++)
        {
            int bendAxis = (sourceAxis + 1) % 3;
            var bone = BendBone("mapped bone", authored);
            bone.SetLocalPosition(new vec3(1f, 2f, 3f));
            bone.SetLocalRotation(new vec3(0.1f, 0.2f, 0.3f));
            bone.SetLocalScale(new vec3(1.5f, 2f, 0.5f));
            var constraint = new CopyConstraint
            {
                Type = CopyConstraintType.RotationToBend, TargetObjectId = source.ObjectId,
                SourceRotationAxis = sourceAxis, BendAxis = bendAxis,
                RotationMultiplier = -1.5f, Offset = new vec3(7f, 11f, 13f), Blend = 0.25f
            };
            bone.CopyConstraints.Add(constraint);
            vec3 expected = authored;
            expected[bendAxis] += (worldDegrees[sourceAxis] * -1.5f + constraint.Offset[bendAxis] - authored[bendAxis]) * 0.25f;
            for (int frame = 0; frame < 12; frame++)
            {
                InverseKinematicsSolver.Evaluate([parent, bone]);
                Near(expected, EvaluatedBend(bone), $"World source axis {sourceAxis}, evaluation {frame}");
                Equal(authored, bone.BendParameters!.Value.Angle, "Evaluation mutated stored bend");
                Equal(authored, bone.GetEditableBendAngle(), "Evaluation mutated editable bend");
                Equal(new vec3(1f, 2f, 3f), bone.Position, "Evaluation mutated authored position");
                Equal(new vec3(0.1f, 0.2f, 0.3f), bone.Rotation, "Evaluation mutated authored rotation");
                Equal(new vec3(1.5f, 2f, 0.5f), bone.Scale, "Evaluation mutated authored scale");
            }
        }
    }

    private static void RotationBendReset(Fixture f)
    {
        vec3 authored = new(5f, 6f, 7f);
        var bone = BendBone("reset bone", authored);
        var source = Node("reset source");
        source.SetLocalRotation(new vec3(0.5f, 0f, 0f));
        var constraint = new CopyConstraint { Type = CopyConstraintType.RotationToBend, TargetObjectId = source.ObjectId };
        bone.CopyConstraints.Add(constraint);
        foreach (string reason in new[] { "disabled", "missing ID", "target removed", "zero blend" })
        {
            constraint.Enabled = true;
            constraint.TargetObjectId = source.ObjectId;
            constraint.Blend = 1f;
            InverseKinematicsSolver.Evaluate([source, bone]);
            Near(0.5f * (180f / MathF.PI), EvaluatedBend(bone).x, "Active driver before reset");
            if (reason == "disabled") constraint.Enabled = false;
            if (reason == "missing ID") constraint.TargetObjectId = "missing-target";
            if (reason == "zero blend") constraint.Blend = 0f;
            InverseKinematicsSolver.Evaluate(reason == "target removed" ? new SceneObject[] { bone } : new SceneObject[] { source, bone });
            Near(authored, EvaluatedBend(bone), $"Stale evaluated bend after {reason}");
            Equal(authored, bone.BendParameters!.Value.Angle, $"Authored bend changed after {reason}");
        }
    }

    private static void RotationBendLimits(Fixture f)
    {
        var bone = BendBone("limited inverted bone", new vec3(-7f, 0f, 9f));
        var parameters = bone.BendParameters!.Value;
        parameters.InvertX = true;
        parameters.AxisY = false;
        parameters.DirectionMin = new vec3(-10f, -180f, -180f);
        parameters.DirectionMax = new vec3(25f, 180f, 180f);
        bone.SetBendParameters(parameters, 1f);
        var source = Node("limit source");
        source.SetLocalRotation(new vec3(MathF.PI / 4f, 0f, 0f));
        var constraint = new CopyConstraint { Type = CopyConstraintType.RotationToBend, TargetObjectId = source.ObjectId };
        bone.CopyConstraints.Add(constraint);
        InverseKinematicsSolver.Evaluate([source, bone]);
        Near(new vec3(25f, 0f, 9f), EvaluatedBend(bone), "Editable upper bend limit");
        Near(new vec3(-25f, 0f, 9f), bone.GetEffectiveBendParameters()!.Value.Angle, "Inversion must apply only to effective stored bend");
        constraint.RotationMultiplier = -3f;
        InverseKinematicsSolver.Evaluate([source, bone]);
        Near(new vec3(-10f, 0f, 9f), EvaluatedBend(bone), "Editable lower bend limit");
        Near(10f, bone.GetEffectiveBendParameters()!.Value.Angle.x, "Inverted lower bend limit");
        constraint.BendAxis = 1;
        InverseKinematicsSolver.Evaluate([source, bone]);
        Near(new vec3(7f, 0f, 9f), EvaluatedBend(bone), "Unsupported bend axis must remain zero");
        Equal(parameters.Angle, bone.BendParameters!.Value.Angle, "Limits mutated authored bend");
        Equal(parameters.DirectionMin, bone.BendParameters.Value.DirectionMin, "Evaluation mutated bend limits");
    }

    private static void RotationBendDependencies(Fixture f)
    {
        var source = Node("ultimate rotation source");
        source.SetLocalRotation(new vec3(0f, 0f, 55f) * (MathF.PI / 180f));
        var intermediate = Node("copy rotation source");
        intermediate.SetLocalRotation(new vec3(0f, 0f, -10f) * (MathF.PI / 180f));
        intermediate.CopyConstraints.Add(new CopyConstraint { Type = CopyConstraintType.Rotation, TargetObjectId = source.ObjectId });
        var bone = BendBone("dependent bend", new vec3(3f, 4f, 5f));
        bone.CopyConstraints.Add(new CopyConstraint
        {
            Type = CopyConstraintType.RotationToBend, TargetObjectId = intermediate.ObjectId, SourceRotationAxis = 2, BendAxis = 1
        });
        // Evaluate the consumer first in traversal order, then reverse the roots.
        foreach (SceneObject[] roots in new[] { new SceneObject[] { source, intermediate, bone }, new SceneObject[] { bone, intermediate, source } })
        {
            InverseKinematicsSolver.Evaluate(roots);
            Near(new vec3(3f, 55f, 5f), EvaluatedBend(bone), "Bend must read evaluated copy rotation, not authored source rotation");
            Near(-10f * (MathF.PI / 180f), intermediate.Rotation.z, "Source copy rotation mutated authored channel");
            source.SetLocalRotation(new vec3(0f, 0f, 25f) * (MathF.PI / 180f));
            InverseKinematicsSolver.Evaluate(roots);
            Near(25f, EvaluatedBend(bone).y, "Dependency must update on the next evaluation");
            source.SetLocalRotation(new vec3(0f, 0f, 55f) * (MathF.PI / 180f));
        }
    }

    private static void RotationBendCycles(Fixture f)
    {
        var first = BendBone("first cyclic driver", new vec3(3f, 4f, 5f));
        var second = BendBone("second cyclic driver", new vec3(6f, 7f, 8f));
        var dependent = BendBone("cyclic dependent", new vec3(9f, 10f, 11f));
        first.SetLocalRotation(new vec3(20f, 0f, 0f) * (MathF.PI / 180f));
        second.SetLocalRotation(new vec3(40f, 0f, 0f) * (MathF.PI / 180f));
        first.CopyConstraints.Add(new CopyConstraint { Type = CopyConstraintType.RotationToBend, TargetObjectId = second.ObjectId });
        dependent.CopyConstraints.Add(new CopyConstraint { Type = CopyConstraintType.RotationToBend, TargetObjectId = first.ObjectId });
        InverseKinematicsSolver.Evaluate([first, second, dependent]);
        Near(40f, EvaluatedBend(first).x, "Acyclic setup must first produce an evaluated bend");
        second.CopyConstraints.Add(new CopyConstraint { Type = CopyConstraintType.RotationToBend, TargetObjectId = first.ObjectId });
        for (int frame = 0; frame < 3; frame++)
        {
            InverseKinematicsSolver.Evaluate([first, second, dependent]);
            foreach (var bone in new[] { first, second, dependent })
                Near(bone.BendParameters!.Value.Angle, EvaluatedBend(bone), "Cyclic driver/dependent must reset and remain authored");
        }
    }

    private static void RotationBendCaptureOffset(Fixture f)
    {
        var bone = BendBone("capture bend", new vec3(10f, 2f, 3f));
        var source = Node("capture source");
        source.SetLocalRotation(new vec3(20f, 0f, 0f) * (MathF.PI / 180f));
        var laterSource = Node("later stack source");
        laterSource.SetLocalRotation(new vec3(40f, 0f, 0f) * (MathF.PI / 180f));
        var constraint = new CopyConstraint
        {
            Type = CopyConstraintType.RotationToBend, TargetObjectId = source.ObjectId,
            RotationMultiplier = 1.5f, Offset = new vec3(7f, 8f, 9f), Blend = 0.4f
        };
        bone.CopyConstraints.Add(constraint);
        bone.CopyConstraints.Add(new CopyConstraint
        {
            Type = CopyConstraintType.RotationToBend, TargetObjectId = laterSource.ObjectId, Blend = 0.5f
        });
        InverseKinematicsSolver.Evaluate([source, laterSource, bone]);
        vec3 before = EvaluatedBend(bone);
        Near(new vec3(30.4f, 2f, 3f), before, "Initial blended bend stack");
        source.SetLocalRotation(new vec3(50f, 0f, 0f) * (MathF.PI / 180f));
        CopyConstraintSolver.CaptureOffset(bone, constraint, source);
        Near(new vec3(-38f, 8f, 9f), constraint.Offset, "Capture must preserve this entry's result using degree offsets");
        InverseKinematicsSolver.Evaluate([source, laterSource, bone]);
        Near(before, EvaluatedBend(bone), "Captured offset must preserve blended stack pose");
        Equal(new vec3(10f, 2f, 3f), bone.BendParameters!.Value.Angle, "Capture mutated authored bend");
    }

    private static void RotationBendPersistence(Fixture f)
    {
        Equal(0, (int)CopyConstraintType.Position, "Existing position enum value changed");
        Equal(1, (int)CopyConstraintType.Rotation, "Existing rotation enum value changed");
        Equal(2, (int)CopyConstraintType.Scale, "Existing scale enum value changed");
        Equal(3, (int)CopyConstraintType.RotationToBend, "Rotation-to-bend must be appended to persisted enum");
        var defaults = new CopyConstraint { Type = CopyConstraintType.RotationToBend };
        Equal(0, defaults.SourceRotationAxis, "Default source axis");
        Equal(0, defaults.BendAxis, "Default bend axis");
        Equal(1f, defaults.RotationMultiplier, "Default rotation multiplier");
        Equal(vec3.Zero, defaults.Offset, "Default bend offset");
        var root = Node("persistent drivers");
        var source = Node("persistent source");
        var bone = BendBone("persistent bend", new vec3(4f, 5f, 6f));
        root.AddChild(source);
        root.AddChild(bone);
        var constraint = new CopyConstraint
        {
            Type = CopyConstraintType.RotationToBend, TargetObjectId = source.ObjectId,
            SourceRotationAxis = 2, BendAxis = 1, RotationMultiplier = -2.5f,
            Offset = new vec3(12f, 23f, 34f), Blend = 0.35f, BentHalf = true
        };
        bone.CopyConstraints.Add(constraint);
        source.SetLocalRotation(new vec3(0f, 0f, 0.5f));
        InverseKinematicsSolver.Evaluate([root]);
        // Roundtrip uses NxObjectArchive's source-generated JSON context, not reflection serialization.
        var (manifest, _) = f.Roundtrip(root);
        var savedBone = manifest.Root.Children.Single(entry => entry.ObjectId == bone.ObjectId);
        var saved = savedBone.CopyConstraints!.Single();
        Equal(CopyConstraintType.RotationToBend, saved.Type, "Persisted constraint type");
        Equal(source.ObjectId, saved.TargetObjectId, "Persisted internal driver reference");
        Equal(2, saved.SourceRotationAxis, "Persisted source rotation axis");
        Equal(1, saved.BendAxis, "Persisted bend axis");
        Equal(-2.5f, saved.RotationMultiplier, "Persisted rotation multiplier");
        Equal(23f, saved.Offset.Y, "Degree offset must not be converted to radians");
        Equal(0.35f, saved.Blend, "Persisted bend blend");
        Check(saved.BentHalf && saved.Enabled, "Persisted driver flags");
        Equal(5f, savedBone.BendAngle!.Y, "Saving must preserve authored bend rather than evaluated pose");
        var deserialize = typeof(ProjectSceneSerializer).GetMethod("DeserializeCopyConstraint", BindingFlags.Static | BindingFlags.NonPublic)!;
        var restored = (CopyConstraint)deserialize.Invoke(null, [saved])!;
        Equal(constraint.Type, restored.Type, "Restored runtime constraint type");
        Equal(constraint.TargetObjectId, restored.TargetObjectId, "Restored runtime target");
        Equal(constraint.SourceRotationAxis, restored.SourceRotationAxis, "Restored runtime source axis");
        Equal(constraint.BendAxis, restored.BendAxis, "Restored runtime bend axis");
        Equal(constraint.RotationMultiplier, restored.RotationMultiplier, "Restored runtime multiplier");
        Equal(constraint.Offset, restored.Offset, "Restored runtime degree offsets");
        Equal(constraint.Blend, restored.Blend, "Restored runtime blend");
        var legacy = (CopyConstraint)deserialize.Invoke(null, [new ProjectCopyConstraintEntry()])!;
        Equal(0, legacy.SourceRotationAxis, "Legacy DTO default source axis");
        Equal(0, legacy.BendAxis, "Legacy DTO default bend axis");
        Equal(1f, legacy.RotationMultiplier, "Legacy DTO default multiplier");
        var clone = constraint.Clone();
        Equal(constraint.SourceRotationAxis, clone.SourceRotationAxis, "Cloned source axis");
        Equal(constraint.BendAxis, clone.BendAxis, "Cloned bend axis");
        Equal(constraint.RotationMultiplier, clone.RotationMultiplier, "Cloned multiplier");
    }

    private static void RotationBendScaledRows(Fixture f)
    {
        (vec3 Degrees, vec3 Scale)[] cases =
        [
            (new vec3(45f, 0f, 0f), new vec3(1f, 2f, 1f)),
            (new vec3(25f, -35f, 40f), new vec3(2f, 3f, 0.5f))
        ];
        foreach (var sample in cases)
        {
            var source = Node("row-scaled rotation source");
            // The non-resize path composes S * R, unlike the column-scaled R * S path.
            source.ScaleResize = false;
            vec3 radians = sample.Degrees * (MathF.PI / 180f);
            source.SetLocalRotation(radians);
            source.SetLocalScale(sample.Scale);
            mat4 expectedWorld = mat4.Scale(sample.Scale) * mat4.RotateZ(radians.z) * mat4.RotateY(radians.y) * mat4.RotateX(radians.x);
            mat4 actualWorld = source.GetWorldMatrixNoPivot();
            for (int column = 0; column < 3; column++)
                for (int row = 0; row < 3; row++)
                    Near(expectedWorld[column, row], actualWorld[column, row], "Fixture must contain row-scaled rotation");
            var bone = BendBone("row-scaled driven bend", vec3.Zero);
            for (int axis = 0; axis < 3; axis++)
                bone.CopyConstraints.Add(new CopyConstraint
                {
                    Type = CopyConstraintType.RotationToBend, TargetObjectId = source.ObjectId,
                    SourceRotationAxis = axis, BendAxis = axis
                });
            InverseKinematicsSolver.Evaluate([source, bone]);
            Near(sample.Degrees, EvaluatedBend(bone), "Nonuniform row scale must not distort source Euler angles");
            Equal(vec3.Zero, bone.BendParameters!.Value.Angle, "Rotation extraction mutated authored bend");
        }
    }

    private static void RotationBendEnableCapture(Fixture f)
    {
        var bone = BendBone("enable capture bend", vec3.Zero);
        var earlierSource = Node("disabled earlier source");
        earlierSource.SetLocalRotation(new vec3(80f, 0f, 0f) * (MathF.PI / 180f));
        var laterSource = Node("active later source");
        laterSource.SetLocalRotation(new vec3(40f, 0f, 0f) * (MathF.PI / 180f));
        var earlier = new CopyConstraint
        {
            Type = CopyConstraintType.RotationToBend, TargetObjectId = earlierSource.ObjectId,
            Enabled = false, Blend = 0.5f, Offset = new vec3(0f, 11f, 12f)
        };
        bone.CopyConstraints.Add(earlier);
        bone.CopyConstraints.Add(new CopyConstraint
        {
            Type = CopyConstraintType.RotationToBend, TargetObjectId = laterSource.ObjectId, Blend = 0.5f
        });
        InverseKinematicsSolver.Evaluate([earlierSource, laterSource, bone]);
        Near(new vec3(20f, 0f, 0f), EvaluatedBend(bone), "Disabled earlier driver must leave later partial blend at 20 degrees");
        earlier.Enabled = true;
        CopyConstraintSolver.CaptureOffset(bone, earlier, earlierSource);
        Near(new vec3(-80f, 11f, 12f), earlier.Offset, "Newly enabled driver must capture its stack prefix, not the final pose");
        for (int frame = 0; frame < 3; frame++)
        {
            InverseKinematicsSolver.Evaluate([earlierSource, laterSource, bone]);
            Near(new vec3(20f, 0f, 0f), EvaluatedBend(bone), "Enabling and capturing earlier driver must preserve final stack pose");
            Equal(vec3.Zero, bone.BendParameters!.Value.Angle, "Enable capture mutated authored bend");
        }
    }

    private static void RotationBendInheritedPose(Fixture f)
    {
        var body = new CharacterSceneObject { Name = "body model", ObjectId = Guid.NewGuid().ToString("N") };
        var armor = new CharacterSceneObject { Name = "armor model", ObjectId = Guid.NewGuid().ToString("N"), InheritPose = true };
        var bodyBone = BendBone("body arm", new vec3(10f, 0f, 0f));
        var armorBone = BendBone("armor arm", new vec3(4f, 0f, 0f));
        bodyBone.BoneName = armorBone.BoneName = "arm";
        body.BoneObjects.Add("arm", bodyBone);
        armor.BoneObjects.Add("arm", armorBone);
        var modelOwner = typeof(MiBoneSceneObject).GetProperty("ModelOwner", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMemberException("MiBoneSceneObject.ModelOwner");
        modelOwner.SetValue(bodyBone, body);
        modelOwner.SetValue(armorBone, armor);
        // The inherited pose source is a sibling of the armor model, not a bone ancestor.
        body.AddChild(bodyBone);
        body.AddChild(armor);
        armor.AddChild(armorBone);
        var bodyDriver = Node("body bend driver");
        var armorDriver = Node("armor bend driver");
        armorDriver.SetLocalRotation(new vec3(20f, 0f, 0f) * (MathF.PI / 180f));
        bodyBone.CopyConstraints.Add(new CopyConstraint { Type = CopyConstraintType.RotationToBend, TargetObjectId = bodyDriver.ObjectId });
        armorBone.CopyConstraints.Add(new CopyConstraint
        {
            Type = CopyConstraintType.RotationToBend, TargetObjectId = armorDriver.ObjectId, Blend = 0.5f
        });
        SceneObject[][] orders =
        [
            [body, bodyDriver, armorDriver, bodyBone, armorBone],
            [armorBone, body, armorDriver, bodyDriver, bodyBone]
        ];
        foreach (var roots in orders)
        {
            foreach (float drivenBodyAngle in new[] { 60f, 80f })
            {
                bodyDriver.SetLocalRotation(new vec3(drivenBodyAngle, 0f, 0f) * (MathF.PI / 180f));
                InverseKinematicsSolver.Evaluate(roots);
                Near(drivenBodyAngle, EvaluatedBend(bodyBone).x, "Body bend driver must evaluate");
                Near((4f + drivenBodyAngle + 20f) * 0.5f, EvaluatedBend(armorBone).x,
                    "Armor partial blend must start from its own bend plus the body's evaluated pose in either root order");
                Equal(new vec3(10f, 0f, 0f), bodyBone.BendParameters!.Value.Angle, "Inherited pose evaluation mutated body bend");
                Equal(new vec3(4f, 0f, 0f), armorBone.BendParameters!.Value.Angle, "Inherited pose evaluation mutated armor bend");
            }
        }
    }

    private static void RotationBendAxisMasks(Fixture f)
    {
        vec3 authored = new(4f, 8f, 12f);
        var bone = BendBone("masked bend", authored);
        var source = Node("masked source");
        source.SetLocalRotation(new vec3(20f, -30f, 40f) * (MathF.PI / 180f));
        var constraint = new CopyConstraint
        {
            Type = CopyConstraintType.RotationToBend, TargetObjectId = source.ObjectId,
            UseBendAxisMask = true, SourceRotationAxisX = 2, SourceRotationAxisY = 0, SourceRotationAxisZ = 1,
            RotationMultiplier = -1.5f, Offset = new vec3(3f, 7f, 11f), Blend = 0.4f
        };
        bone.CopyConstraints.Add(constraint);
        vec3 mapped = new(40f, 20f, -30f);
        foreach (int mask in new[] { 1, 2, 3, 4, 5, 6, 7, 0, 7, 2, 0 })
        {
            vec3 expected = authored;
            for (int axis = 0; axis < 3; axis++)
            {
                bool enabled = (mask & (1 << axis)) != 0;
                constraint.SetBendAxisEnabled(axis, enabled);
                Equal(enabled, constraint.IsBendAxisEnabled(axis), "Checkbox mask selection");
                if (enabled) expected[axis] += (mapped[axis] * -1.5f + constraint.Offset[axis] - authored[axis]) * 0.4f;
            }
            for (int frame = 0; frame < 3; frame++)
            {
                InverseKinematicsSolver.Evaluate([source, bone]);
                Near(expected, EvaluatedBend(bone), $"Single-constraint mask {mask}, evaluation {frame}");
                Equal(authored, bone.BendParameters!.Value.Angle, "Axis toggles mutated authored bend");
            }
        }
        Equal(1, bone.CopyConstraints.Count, "Multiple axes must be driven by one constraint");
    }

    private static void RotationBendMaskLimits(Fixture f)
    {
        var bone = BendBone("masked limits", new vec3(-7f, 0f, 9f));
        var parameters = bone.BendParameters!.Value;
        parameters.AxisY = false;
        parameters.InvertX = true;
        parameters.DirectionMin = new vec3(-10f, -180f, -12f);
        parameters.DirectionMax = new vec3(25f, 180f, 18f);
        bone.SetBendParameters(parameters, 1f);
        var source = Node("masked limits source");
        source.SetLocalRotation(new vec3(20f, -30f, 40f) * (MathF.PI / 180f));
        var constraint = new CopyConstraint
        {
            Type = CopyConstraintType.RotationToBend, TargetObjectId = source.ObjectId,
            UseBendAxisMask = true, SourceRotationAxisX = 2, SourceRotationAxisY = 0, SourceRotationAxisZ = 1
        };
        bone.CopyConstraints.Add(constraint);
        InverseKinematicsSolver.Evaluate([source, bone]);
        Near(new vec3(25f, 0f, -12f), EvaluatedBend(bone), "Each selected axis must obey its own model limit/support");
        Near(new vec3(-25f, 0f, -12f), bone.GetEffectiveBendParameters()!.Value.Angle, "Masked bend inversion");
        constraint.RotationMultiplier = -2f;
        InverseKinematicsSolver.Evaluate([source, bone]);
        Near(new vec3(-10f, 0f, 18f), EvaluatedBend(bone), "Masked opposite limits");
        Equal(parameters.Angle, bone.BendParameters!.Value.Angle, "Masked limits mutated stored angles");
    }

    private static void RotationBendMaskLegacy(Fixture f)
    {
        var constraint = new CopyConstraint { Type = CopyConstraintType.RotationToBend, BendAxis = 1, SourceRotationAxis = 2 };
        Check(!constraint.UseBendAxisMask && constraint.SourceRotationAxisX == null && constraint.SourceRotationAxisY == null && constraint.SourceRotationAxisZ == null,
            "Legacy constraints must default to single-axis mode and nullable source overrides");
        Check(!constraint.IsBendAxisEnabled(0) && constraint.IsBendAxisEnabled(1) && !constraint.IsBendAxisEnabled(2),
            "Legacy single axis must ignore the default all-true Axis flags");
        Equal(0, constraint.GetSourceRotationAxis(0), "Unselected X fallback must match X");
        Equal(2, constraint.GetSourceRotationAxis(1), "Selected Y must retain legacy source Z");
        Equal(2, constraint.GetSourceRotationAxis(2), "Unselected Z fallback must match Z");
        var bone = BendBone("legacy masked bend", new vec3(4f, 8f, 12f));
        var source = Node("legacy mask source");
        source.SetLocalRotation(new vec3(20f, -30f, 40f) * (MathF.PI / 180f));
        constraint.TargetObjectId = source.ObjectId;
        bone.CopyConstraints.Add(constraint);
        InverseKinematicsSolver.Evaluate([source, bone]);
        Near(new vec3(4f, 40f, 12f), EvaluatedBend(bone), "Unconverted legacy Y-from-Z mapping");
        constraint.BendAxis = 2;
        constraint.SourceRotationAxis = 0;
        InverseKinematicsSolver.Evaluate([source, bone]);
        Near(new vec3(4f, 8f, 20f), EvaluatedBend(bone), "Legacy single-axis animation must still change the selected axis");
        Check(!constraint.UseBendAxisMask, "Legacy field edits must not activate mask mode");
        constraint.SetBendAxisEnabled(0, true);
        Check(constraint.UseBendAxisMask && constraint.AxisX && !constraint.AxisY && constraint.AxisZ,
            "First checkbox edit must retain only the old selected axis plus the newly enabled one");
        InverseKinematicsSolver.Evaluate([source, bone]);
        Near(new vec3(20f, 8f, 20f), EvaluatedBend(bone), "Conversion must preserve old Z-from-X and default new X-from-X mapping");
        constraint.SourceRotationAxisZ = 1;
        InverseKinematicsSolver.Evaluate([source, bone]);
        Near(new vec3(20f, 8f, -30f), EvaluatedBend(bone), "Explicit per-axis source must override legacy source");
        var disable = new CopyConstraint { BendAxis = 1 };
        disable.SetBendAxisEnabled(1, false);
        Check(disable.UseBendAxisMask && !disable.AxisX && !disable.AxisY && !disable.AxisZ,
            "Disabling the sole legacy checkbox must convert to all-off, not enable other default flags");
    }

    private static void RotationBendMaskCapture(Fixture f)
    {
        var bone = BendBone("multi-axis capture", new vec3(4f, 8f, 12f));
        var source = Node("multi-axis capture source");
        source.SetLocalRotation(new vec3(20f, -10f, 30f) * (MathF.PI / 180f));
        var laterSource = Node("multi-axis later source");
        laterSource.SetLocalRotation(new vec3(12f, 22f, 32f) * (MathF.PI / 180f));
        var constraint = new CopyConstraint
        {
            Type = CopyConstraintType.RotationToBend, TargetObjectId = source.ObjectId, UseBendAxisMask = true,
            SourceRotationAxisX = 2, SourceRotationAxisY = 0, SourceRotationAxisZ = 1,
            RotationMultiplier = 1.25f, Offset = new vec3(3f, 5f, 7f), Blend = 0.4f
        };
        bone.CopyConstraints.Add(constraint);
        bone.CopyConstraints.Add(new CopyConstraint
        {
            Type = CopyConstraintType.RotationToBend, TargetObjectId = laterSource.ObjectId,
            UseBendAxisMask = true, SourceRotationAxisX = 0, SourceRotationAxisY = 1, SourceRotationAxisZ = 2, Blend = 0.5f
        });
        InverseKinematicsSolver.Evaluate([source, laterSource, bone]);
        vec3 before = EvaluatedBend(bone);
        Near(new vec3(15.3f, 19.4f, 18.5f), before, "Initial multi-axis blended stack");
        source.SetLocalRotation(new vec3(-15f, 25f, 10f) * (MathF.PI / 180f));
        CopyConstraintSolver.CaptureOffset(bone, constraint, source);
        Near(new vec3(28f, 48.75f, -36.75f), constraint.Offset, "Capture must solve all selected axis offsets from this entry's output");
        InverseKinematicsSolver.Evaluate([source, laterSource, bone]);
        Near(before, EvaluatedBend(bone), "Multi-axis capture must preserve final blended stack pose");
        constraint.SetBendAxisEnabled(1, false);
        vec3 offset = constraint.Offset;
        source.SetLocalRotation(new vec3(5f, 15f, 25f) * (MathF.PI / 180f));
        CopyConstraintSolver.CaptureOffset(bone, constraint, source);
        Equal(offset.y, constraint.Offset.y, "Capture must not overwrite an unchecked axis offset");
        Equal(new vec3(4f, 8f, 12f), bone.BendParameters!.Value.Angle, "Multi-axis capture mutated authored bend");
    }

    private static void RotationBendMaskPersistence(Fixture f)
    {
        var root = Node("mask archive root");
        var source = Node("mask archive source");
        var bone = BendBone("mask archive bend", new vec3(4f, 8f, 12f));
        root.AddChild(source);
        root.AddChild(bone);
        var constraint = new CopyConstraint
        {
            Type = CopyConstraintType.RotationToBend, TargetObjectId = source.ObjectId, UseBendAxisMask = true,
            AxisX = true, AxisY = false, AxisZ = true,
            SourceRotationAxisX = 2, SourceRotationAxisY = 0, SourceRotationAxisZ = 1,
            RotationMultiplier = -1.25f, Offset = new vec3(3f, 5f, 7f), Blend = 0.4f
        };
        bone.CopyConstraints.Add(constraint);
        bone.CopyConstraints.Add(new CopyConstraint
        {
            Type = CopyConstraintType.RotationToBend, TargetObjectId = source.ObjectId, BendAxis = 1, SourceRotationAxis = 2
        });
        var (manifest, _) = f.Roundtrip(root);
        var saved = manifest.Root.Children.Single(entry => entry.ObjectId == bone.ObjectId).CopyConstraints!;
        Check(saved[0].UseBendAxisMask && saved[0].AxisX && !saved[0].AxisY && saved[0].AxisZ, "Archive lost independent mask flags");
        Equal<int?>(2, saved[0].SourceRotationAxisX, "Archived X source");
        Equal<int?>(0, saved[0].SourceRotationAxisY, "Archived Y source even when unchecked");
        Equal<int?>(1, saved[0].SourceRotationAxisZ, "Archived Z source");
        var deserialize = typeof(ProjectSceneSerializer).GetMethod("DeserializeCopyConstraint", BindingFlags.Static | BindingFlags.NonPublic)!;
        var restored = (CopyConstraint)deserialize.Invoke(null, [saved[0]])!;
        var clone = constraint.Clone();
        foreach (var copy in new[] { restored, clone })
        {
            Check(copy.UseBendAxisMask && copy.AxisX && !copy.AxisY && copy.AxisZ, "Restored/cloned mask flags");
            Equal(constraint.SourceRotationAxisX, copy.SourceRotationAxisX, "Restored/cloned X source");
            Equal(constraint.SourceRotationAxisY, copy.SourceRotationAxisY, "Restored/cloned Y source");
            Equal(constraint.SourceRotationAxisZ, copy.SourceRotationAxisZ, "Restored/cloned Z source");
            Equal(constraint.Offset, copy.Offset, "Restored/cloned per-axis degree offsets");
            Equal(constraint.Blend, copy.Blend, "Restored/cloned shared blend");
            Equal(constraint.RotationMultiplier, copy.RotationMultiplier, "Restored/cloned shared multiplier");
        }
        clone.SourceRotationAxisX = 0;
        clone.SetBendAxisEnabled(2, false);
        Check(constraint.SourceRotationAxisX == 2 && constraint.AxisZ, "Cloned settings must be independent");
        var legacy = (CopyConstraint)deserialize.Invoke(null, [saved[1]])!;
        Check(!legacy.UseBendAxisMask && legacy.SourceRotationAxisX == null && legacy.SourceRotationAxisY == null && legacy.SourceRotationAxisZ == null,
            "Archive must retain legacy mode and null source overrides");
        Check(!legacy.IsBendAxisEnabled(0) && legacy.IsBendAxisEnabled(1) && !legacy.IsBendAxisEnabled(2), "Legacy archive must remain single-axis");
        Equal(2, legacy.GetSourceRotationAxis(1), "Legacy archive source fallback");
        string oldArchive = f.Zip(null, [("manifest.json", """
            {"format":"nxObj","version":1,"root":{"copyConstraints":[{"type":3,"bendAxis":1,"sourceRotationAxis":2}]}}
            """)]);
        var oldEntry = Extract(oldArchive, f.NewPath()).Root.CopyConstraints!.Single();
        var oldRuntime = (CopyConstraint)deserialize.Invoke(null, [oldEntry])!;
        Check(!oldRuntime.UseBendAxisMask && oldRuntime.SourceRotationAxisX == null && oldRuntime.SourceRotationAxisY == null && oldRuntime.SourceRotationAxisZ == null,
            "Old source-generated JSON without new fields must retain legacy defaults");
        Check(!oldRuntime.IsBendAxisEnabled(0) && oldRuntime.IsBendAxisEnabled(1) && !oldRuntime.IsBendAxisEnabled(2),
            "Missing mask field must not activate all default Axis flags");
        Equal(2, oldRuntime.GetSourceRotationAxis(1), "Missing per-axis fields must retain old source selector");
    }

    private static void RotationBendMaskTimeline(Fixture f)
    {
        var timeline = new Timeline();
        var get = typeof(Timeline).GetMethod("GetPropertyValue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var set = typeof(Timeline).GetMethod("SetPropertyValue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var interpolate = typeof(Timeline).GetMethod("InterpolateKeyframes", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var bone = BendBone("timeline masked bend", vec3.Zero);
        var constraint = new CopyConstraint { Type = CopyConstraintType.RotationToBend, BendAxis = 1, SourceRotationAxis = 2 };
        bone.CopyConstraints.Add(constraint);
        var paths = (IEnumerable<string>)typeof(Timeline).GetMethod("GetKeyframePropertyPaths", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [bone])!;
        var pathSet = paths.ToHashSet(StringComparer.Ordinal);
        foreach (string channel in new[] { "source_rotation_axis", "bend_axis" })
            Check(pathSet.Contains("copy_constraint.0." + channel), "Legacy timeline must discover its original selector channels");
        foreach (string channel in new[] { "axis.x", "axis.y", "axis.z", "source_rotation_axis.x", "source_rotation_axis.y", "source_rotation_axis.z" })
            Check(!pathSet.Contains("copy_constraint.0." + channel), "Automatic legacy keying must not introduce overriding mask/source tracks");
        float Read(string channel) => Convert.ToSingle(get.Invoke(timeline, [bone, "copy_constraint.0." + channel]));
        void Set(string channel, object value) => set.Invoke(timeline, [bone, "copy_constraint.0." + channel, value]);
        Near(0f, Read("axis.x"), "Timeline must expose effective legacy X checkbox");
        Near(1f, Read("axis.y"), "Timeline must expose effective legacy Y checkbox");
        Near(2f, Read("source_rotation_axis.y"), "Timeline must expose legacy source fallback");
        Set("bend_axis", 2f);
        Set("source_rotation_axis", 1f);
        Check(!constraint.UseBendAxisMask, "Legacy timeline channels must not activate mask mode");
        Near(1f, Read("axis.z"), "Legacy bend-axis animation must select Z");
        Near(1f, Read("source_rotation_axis.z"), "Legacy source-axis animation must still drive Z mapping");
        Set("axis.x", 1f);
        Check(constraint.UseBendAxisMask && constraint.AxisX && !constraint.AxisY && constraint.AxisZ, "Timeline checkbox write must convert legacy mode");
        var maskedPaths = ((IEnumerable<string>)typeof(Timeline).GetMethod("GetKeyframePropertyPaths", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [bone])!).ToHashSet(StringComparer.Ordinal);
        foreach (string channel in new[] { "axis.x", "axis.y", "axis.z", "source_rotation_axis.x", "source_rotation_axis.y", "source_rotation_axis.z" })
            Check(maskedPaths.Contains("copy_constraint.0." + channel), "Mask mode must discover checkbox and per-axis source tracks");
        foreach (string channel in new[] { "source_rotation_axis", "bend_axis" })
            Check(!maskedPaths.Contains("copy_constraint.0." + channel), "Mask mode must not automatically key obsolete selectors");
        Set("source_rotation_axis.x", 2f);
        Set("source_rotation_axis.y", 0f);
        Set("source_rotation_axis.z", 1f);
        Near(2f, Read("source_rotation_axis.x"), "Timeline X source read/write");
        Near(0f, Read("source_rotation_axis.y"), "Timeline Y source read/write");
        Near(1f, Read("source_rotation_axis.z"), "Timeline Z source read/write");
        Set("axis.y", 0.5f);
        Set("axis.z", 0.49f);
        Check(constraint.AxisY && !constraint.AxisZ, "Timeline checkbox threshold");
        foreach (string channel in new[] { "axis.x", "axis.y", "axis.z", "source_rotation_axis.x", "source_rotation_axis.y", "source_rotation_axis.z", "source_rotation_axis", "bend_axis" })
        {
            float end = channel.StartsWith("axis.", StringComparison.Ordinal) ? 1f : 2f;
            var keys = new List<TimelineKeyframe> { new() { Frame = 0, Value = 0f }, new() { Frame = 10, Value = end } };
            string path = "copy_constraint.0." + channel;
            Near(0f, Convert.ToSingle(interpolate.Invoke(timeline, [keys, path, 9, false])), "Checkbox/source selector animation must hold previous keyframe");
            Near(end, Convert.ToSingle(interpolate.Invoke(timeline, [keys, path, 10, false])), "Discrete channel must switch at exact keyframe");
            Check(interpolate.Invoke(timeline, [keys, path, -1, false]) == null, "Before first keyframe must not change current mode/mapping");
        }
        foreach (string channel in new[] { "offset.x", "offset.y", "offset.z", "blend", "rotation_multiplier" })
        {
            var keys = new List<TimelineKeyframe> { new() { Frame = 0, Value = 0f }, new() { Frame = 10, Value = 1f } };
            Near(0.5f, Convert.ToSingle(interpolate.Invoke(timeline, [keys, "copy_constraint.0." + channel, 5, false])),
                "Shared numeric bend controls and per-axis offsets must remain continuously animated");
        }
    }

    private static SceneObject Node(string name) => new()
    {
        Name = name, ObjectId = Guid.NewGuid().ToString("N"), ObjectType = "Folder"
    };

    private static NxObjectManifest Extract(string archive, string directory)
    {
        try
        {
            return (NxObjectManifest)ExtractMethod.Invoke(null, [archive, directory])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected '{expected}', got '{actual}'.");

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException or JsonException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static string Resource(string directory, string relative)
    {
        Check(relative.StartsWith("resources/", StringComparison.Ordinal), $"Resource was not remapped: {relative}");
        string full = Path.GetFullPath(Path.Combine(directory, relative));
        Check(full.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            $"Resource escapes extraction: {relative}");
        Check(File.Exists(full), $"Resource missing: {relative}");
        return full;
    }

    private static string Dependency(string owner, string relative, string root, bool uri = false)
    {
        if (uri) relative = Uri.UnescapeDataString(relative);
        Check(!Path.IsPathRooted(relative) && !relative.Contains(':'), $"Dependency not relative: {relative}");
        string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(owner)!, relative));
        Check(full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            $"Dependency escaped extraction: {relative}");
        Check(File.Exists(full), $"Dependency missing: {relative}");
        return full;
    }

    private static void Subtree(Fixture f)
    {
        var outside = Node("not exported");
        var root = Node("selected root");
        var child = Node("primitive child");
        child.SpawnCategory = "Primitives";
        var grandchild = Node("nested folder");
        outside.AddChild(root);
        outside.AddChild(Node("sibling not exported"));
        root.AddChild(child);
        child.AddChild(grandchild);
        root.Position = new vec3(1, 2, 3);
        child.Rotation = new vec3(10, 20, 30);
        child.Scale = new vec3(2, 3, 4);
        root.LibrarySourceId = "external-library-link";
        root.CameraTextureObjectId = child.ObjectId;
        grandchild.CameraTextureObjectId = outside.ObjectId;
        child.Keyframes["position.x"] =
        [
            new() { Frame = 20, Value = 8.5f, InterpolationType = "ease_in" },
            new() { Frame = 2, Value = -2f, InterpolationType = "linear" }
        ];
        child.Keyframes["visible"] = [new() { Frame = 5, Value = false, InterpolationType = "constant" }];
        child.SetExplicitMaterialSettings();
        child.MaterialSettings.Metallic = 0.75f;
        child.MaterialSettings.Roughness = 0.25f;
        child.MaterialSettings.PbrEnabled = true;
        child.MaterialSettings.EmissionEnabled = true;
        child.MaterialSettings.EmissionEnergy = 3.5f;
        child.MaterialSettings.AlphaMode = MaterialAlphaMode.Clip;
        child.MaterialSettings.DoubleSided = true;
        child.AlbedoTexturePath = f.File("textures/albedo.png", "albedo");
        child.NormalTexturePath = f.File("textures/normal.png", "normal");
        child.EmissionTexturePath = f.File("textures/emission.png", "emission");
        child.SurfaceTexturePath = f.File("textures/surface.png", "surface");
        child.OcclusionTexturePath = f.File("textures/occlusion.png", "occlusion");
        child.RoughnessTexturePath = f.File("textures/roughness.png", "roughness");
        child.MetallicTexturePath = f.File("textures/metallic.png", "metallic");
        child.HeightTexturePath = f.File("textures/height.png", "height");
        var (manifest, directory) = f.Roundtrip(root);
        Equal("selected root", manifest.Root.Name, "Selected root");
        Equal(1, manifest.Root.Children.Count, "Root child count");
        Equal("nested folder", manifest.Root.Children[0].Children[0].Name, "Grandchild");
        Equal(2f, manifest.Root.Position.Y, "Root position");
        Equal(child.ObjectId, manifest.Root.CameraTextureObjectId, "Internal reference");
        Equal("", manifest.Root.Children[0].Children[0].CameraTextureObjectId, "External reference cleared");
        Equal("", manifest.Root.LibrarySourceId, "External library association cleared");
        var saved = manifest.Root.Children[0];
        Equal(20f, saved.Rotation.Y, "Child rotation");
        Equal(4f, saved.Scale.Z, "Child scale");
        Check(saved.HasMaterialOverrides && saved.PbrEnabled && saved.EmissionEnabled && saved.DoubleSided, "Material flags lost");
        Equal(0.75f, saved.Metallic, "Metallic");
        Equal(0.25f, saved.Roughness, "Roughness");
        Equal(3.5f, saved.EmissionEnergy, "Emission energy");
        Equal(MaterialAlphaMode.Clip, saved.AlphaMode, "Alpha mode");
        Equal(2, saved.Keyframes["position.x"][0].Frame, "Keyframes sorted");
        Equal(8.5f, ((JsonElement)saved.Keyframes["position.x"][1].Value).GetSingle(), "Numeric keyframe");
        Equal("ease_in", saved.Keyframes["position.x"][1].InterpolationType, "Interpolation");
        Check(!((JsonElement)saved.Keyframes["visible"][0].Value).GetBoolean(), "Boolean keyframe lost");
        foreach (string path in new[] { saved.AlbedoTexturePath, saved.NormalTexturePath, saved.EmissionTexturePath,
            saved.SurfaceTexturePath, saved.OcclusionTexturePath!, saved.RoughnessTexturePath!, saved.MetallicTexturePath!, saved.HeightTexturePath! })
            Resource(directory, path);
        Equal("external-library-link", root.LibrarySourceId, "Export mutated source library link");
        Equal(outside.ObjectId, grandchild.CameraTextureObjectId, "Export mutated source object reference");
    }

    private static void BasenameCollision(Fixture f)
    {
        var root = Node("collision");
        root.AlbedoTexturePath = f.File("a/texture.png", "first texture");
        root.NormalTexturePath = f.File("b/texture.png", "second texture");
        root.EmissionTexturePath = root.AlbedoTexturePath;
        f.File("a/texture.png.mcmeta", "{\"animation\":{}}");
        var (manifest, directory) = f.Roundtrip(root);
        Check(manifest.Root.AlbedoTexturePath != manifest.Root.NormalTexturePath, "Same basename collided");
        Equal(manifest.Root.AlbedoTexturePath, manifest.Root.EmissionTexturePath, "Same source was not deduplicated");
        Equal("first texture", File.ReadAllText(Resource(directory, manifest.Root.AlbedoTexturePath)), "First texture bytes");
        Equal("second texture", File.ReadAllText(Resource(directory, manifest.Root.NormalTexturePath)), "Second texture bytes");
        Check(File.Exists(Resource(directory, manifest.Root.AlbedoTexturePath) + ".mcmeta"), "Animation sidecar missing");
    }

    private static void JsonClosure(Fixture f)
    {
        string texture = f.File("external textures/skin.png", "external skin");
        string model = f.File("models/part.mimodel", JsonSerializer.Serialize(new
        {
            textures = new { skin = texture }, parts = new[] { new { texture = "default" } }
        }));
        string source = f.File("objects/root.miobject", JsonSerializer.Serialize(new
        {
            resources = new[] { new { id = "part-id", filename = model } },
            model = "part-id", texture = texture
        }));
        var root = Node("json");
        root.SourceAssetPath = source;
        var (manifest, directory) = f.Roundtrip(root);
        string extracted = Resource(directory, manifest.Root.SourceAssetPath);
        var json = JsonNode.Parse(File.ReadAllText(extracted))!;
        Equal("part-id", json["model"]!.GetValue<string>(), "Resource ID must not be rewritten");
        string nested = Dependency(extracted, json["resources"]![0]!["filename"]!.GetValue<string>(), directory);
        var nestedJson = JsonNode.Parse(File.ReadAllText(nested))!;
        string extractedTexture = Dependency(nested, nestedJson["textures"]!["skin"]!.GetValue<string>(), directory);
        Equal("external skin", File.ReadAllText(extractedTexture), "External mimodel texture");
        Equal(extractedTexture, Dependency(extracted, json["texture"]!.GetValue<string>(), directory), "Shared dependency target");
        Equal("default", nestedJson["parts"]![0]!["texture"]!.GetValue<string>(), "Default texture sentinel");
        Equal(source, root.SourceAssetPath, "Source path mutated");
        Check(File.ReadAllText(model).Contains(texture.Replace("\\", "\\\\"), StringComparison.Ordinal), "Original model was rewritten");
    }

    private static void GltfClosure(Fixture f)
    {
        f.File("buffers/mesh data.bin", "buffer bytes");
        f.File("images/skin #1.png", "image bytes");
        var root = Node("gltf");
        root.SourceAssetPath = f.File("models/model.gltf", """
            {"asset":{"version":"2.0"},"buffers":[{"uri":"../buffers/mesh%20data.bin","byteLength":12}],
            "images":[{"uri":"../images/skin%20%231.png"},{"uri":"data:image/png;base64,AA=="}]}
            """);
        var (manifest, directory) = f.Roundtrip(root);
        string model = Resource(directory, manifest.Root.SourceAssetPath);
        var json = JsonNode.Parse(File.ReadAllText(model))!;
        Equal("buffer bytes", File.ReadAllText(Dependency(model, json["buffers"]![0]!["uri"]!.GetValue<string>(), directory, true)), "glTF buffer");
        Equal("image bytes", File.ReadAllText(Dependency(model, json["images"]![0]!["uri"]!.GetValue<string>(), directory, true)), "glTF image");
        Equal("data:image/png;base64,AA==", json["images"]![1]!["uri"]!.GetValue<string>(), "Embedded URI");
    }

    private static void JsonObjectReferences(Fixture f)
    {
        string texture = f.File("object-form/skin.png", "object-form texture");
        foreach (string property in new[] { "model_tex", "model_tex_normal", "model_tex_material" })
        {
            var document = new JsonObject { [property] = new JsonObject { ["path"] = texture } };
            var root = Node(property);
            root.SourceAssetPath = f.File(property + ".miobject", document.ToJsonString());
            var (manifest, directory) = f.Roundtrip(root);
            string model = Resource(directory, manifest.Root.SourceAssetPath);
            var json = JsonNode.Parse(File.ReadAllText(model))!;
            string reference = json[property]!["path"]!.GetValue<string>();
            Equal("object-form texture", File.ReadAllText(Dependency(model, reference, directory)), property + " object reference");
        }
    }

    private static void JsonObjectReferenceTraversal(Fixture f)
    {
        foreach (string property in new[] { "model_tex", "model_tex_normal", "model_tex_material" })
        {
            foreach (string key in new[] { "path", "file", "id", "resource" })
            {
                var hostile = new JsonObject { [property] = new JsonObject { [key] = "../../outside.png" } };
                f.RejectArchive(new() { Root = new() { SourceAssetPath = "resources/model.miobject" } },
                    [("resources/model.miobject", hostile.ToJsonString())], $"Accepted {property}.{key} traversal");
            }
        }
    }

    private static void ObjClosure(Fixture f)
    {
        f.File("images/obj skin.png", "obj skin");
        f.File("materials/surface.mtl", "newmtl test\nmap_Kd -s 1 1 1 \"../images/obj skin.png\"\n");
        var root = Node("obj");
        root.SourceAssetPath = f.File("models/model.obj", "mtllib ../materials/surface.mtl\nv 0 0 0\n");
        var (manifest, directory) = f.Roundtrip(root);
        string model = Resource(directory, manifest.Root.SourceAssetPath);
        string materialReference = File.ReadAllLines(model)[0]["mtllib ".Length..].Trim('"');
        string material = Dependency(model, materialReference, directory);
        string textureReference = File.ReadAllLines(material)[1]["map_Kd -s 1 1 1 ".Length..].Trim('"');
        Equal("obj skin", File.ReadAllText(Dependency(material, textureReference, directory)), "OBJ material texture");
    }

    private static void GlbClosure(Fixture f)
    {
        f.File("glb/image.png", "GLB external image");
        string source = f.NewPath(".glb");
        byte[] json = Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"2.0\"},\"images\":[{\"uri\":\"glb/image.png\"}]}");
        int paddedLength = (json.Length + 3) & ~3;
        byte[] binary = [1, 2, 3, 4, 5, 6, 7, 8];
        using (var writer = new BinaryWriter(File.Create(source)))
        {
            writer.Write(0x46546C67u);
            writer.Write(2u);
            writer.Write((uint)(20 + paddedLength + 8 + binary.Length));
            writer.Write((uint)paddedLength);
            writer.Write(0x4E4F534Au);
            writer.Write(json);
            for (int i = json.Length; i < paddedLength; i++) writer.Write((byte)' ');
            writer.Write((uint)binary.Length);
            writer.Write(0x004E4942u);
            writer.Write(binary);
        }
        var root = Node("glb");
        root.SourceAssetPath = source;
        var (manifest, directory) = f.Roundtrip(root);
        string model = Resource(directory, manifest.Root.SourceAssetPath);
        using var reader = new BinaryReader(File.OpenRead(model));
        Equal(0x46546C67u, reader.ReadUInt32(), "GLB magic");
        Equal(2u, reader.ReadUInt32(), "GLB version");
        Equal((uint)reader.BaseStream.Length, reader.ReadUInt32(), "Rewritten GLB size");
        int length = (int)reader.ReadUInt32();
        Equal(0, length % 4, "GLB JSON padding");
        Equal(0x4E4F534Au, reader.ReadUInt32(), "GLB JSON chunk type");
        var rewritten = JsonNode.Parse(reader.ReadBytes(length))!;
        string image = Dependency(model, rewritten["images"]![0]!["uri"]!.GetValue<string>(), directory, true);
        Equal("GLB external image", File.ReadAllText(image), "GLB image bytes");
        Equal((uint)binary.Length, reader.ReadUInt32(), "GLB binary chunk length");
        Equal(0x004E4942u, reader.ReadUInt32(), "GLB binary chunk type");
        Check(reader.ReadBytes(binary.Length).SequenceEqual(binary), "GLB binary chunk changed");
    }

    private static void AtomicFailure(Fixture f, SceneObject root)
    {
        string destination = f.File("existing.nxObj", "existing destination must survive");
        Reject(() => NxObjectArchive.Export(destination, root), "Export accepted missing resource");
        Equal("existing destination must survive", File.ReadAllText(destination), "Atomic destination overwritten");
        Equal(0, Directory.GetFiles(f.Root, "*.tmp").Length, "Temporary archive leaked");
    }

    private static void MissingResource(Fixture f)
    {
        var root = Node("missing");
        root.NormalTexturePath = Path.Combine(f.Root, "absent.png");
        AtomicFailure(f, root);
    }

    private static void MissingDependency(Fixture f)
    {
        var root = Node("missing dependency");
        root.SourceAssetPath = f.File("broken.mimodel", "{\"texture\":\"absent.png\"}");
        AtomicFailure(f, root);
    }

    private static void Unsupported(Action action, string extension)
    {
        try { action(); }
        catch (NotSupportedException exception)
        {
            Check(exception.Message.Contains("convert", StringComparison.OrdinalIgnoreCase) &&
                exception.Message.Contains("GLB", StringComparison.OrdinalIgnoreCase), "Unsupported format error omitted conversion guidance");
            return;
        }
        throw new InvalidOperationException($"Accepted unsupported opaque model {extension}");
    }

    private static void UnsupportedExport(Fixture f)
    {
        foreach (string extension in new[] { ".fbx", ".dae", ".3ds", ".ply", ".x3d" })
        {
            var root = Node("unsupported " + extension);
            string opaqueSource = f.File("source" + extension, "opaque model with unchecked external dependencies");
            root.SourceAssetPath = opaqueSource;
            string destination = f.File("existing" + extension + ".nxObj", "keep existing destination");
            Unsupported(() => NxObjectArchive.Export(destination, root), extension);
            Equal("keep existing destination", File.ReadAllText(destination), "Unsupported export replaced destination");
            Equal(0, Directory.GetFiles(f.Root, "*.tmp").Length, "Unsupported export leaked temporary archive");
            root.SourceAssetPath = f.File("wrapper" + extension + ".miobject", JsonSerializer.Serialize(new { model = opaqueSource }));
            Unsupported(() => NxObjectArchive.Export(destination, root), extension);
            Equal("keep existing destination", File.ReadAllText(destination), "Unsupported nested model export replaced destination");
            Equal(0, Directory.GetFiles(f.Root, "*.tmp").Length, "Unsupported nested model export leaked temporary archive");
        }
    }

    private static void UnsupportedArchive(Fixture f)
    {
        foreach (string extension in new[] { ".fbx", ".dae", ".3ds", ".ply", ".x3d" })
        {
            string resource = "resources/source" + extension;
            string archive = f.Zip(new() { Root = new() { SourceAssetPath = resource } },
                [(resource, "opaque model with unchecked external dependencies")]);
            string directory = f.NewPath();
            Unsupported(() => Extract(archive, directory), extension);
            Check(!Directory.Exists(directory), "Rejected opaque model archive left extracted files");
            string nestedArchive = f.Zip(new() { Root = new() { SourceAssetPath = "resources/wrapper.miobject" } },
                [("resources/wrapper.miobject", JsonSerializer.Serialize(new { model = "source" + extension })),
                (resource, "opaque nested model")]);
            string nestedDirectory = f.NewPath();
            Unsupported(() => Extract(nestedArchive, nestedDirectory), extension);
            Check(!Directory.Exists(nestedDirectory), "Rejected nested opaque model archive left extracted files");
        }
    }

    private static void StlGeometry(Fixture f)
    {
        const string geometry = "solid triangle\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid triangle\n";
        var root = Node("safe STL");
        root.SourceAssetPath = f.File("geometry.stl", geometry);
        var (manifest, directory) = f.Roundtrip(root);
        Equal(geometry, File.ReadAllText(Resource(directory, manifest.Root.SourceAssetPath)), "STL geometry bytes changed");
        string archive = f.Zip(new() { Root = new() { SourceAssetPath = "resources/triangle.stl" } },
            [("resources/triangle.stl", geometry)]);
        string craftedDirectory = f.NewPath();
        var crafted = Extract(archive, craftedDirectory);
        Equal(geometry, File.ReadAllText(Resource(craftedDirectory, crafted.Root.SourceAssetPath)), "Crafted safe STL rejected or changed");
    }

    private static void Versions(Fixture f)
    {
        foreach (int version in new[] { -1, 0, 2, int.MaxValue })
            f.RejectArchive(new NxObjectManifest { Version = version }, [], $"Accepted version {version}");
        f.RejectArchive(new NxObjectManifest { Format = "nxProj" }, [], "Accepted wrong format");
        foreach (string json in new[] { "{}", "{\"format\":\"nxObj\",\"root\":{}}", "{\"version\":1,\"root\":{}}",
            "{\"format\":\"nxObj\",\"version\":1}", "{\"format\":\"nxObj\",\"version\":1,\"root\":null}" })
        {
            string archive = f.Zip(null, [("manifest.json", json)]);
            Reject(() => Extract(archive, f.NewPath()), $"Accepted missing required manifest fields: {json}");
        }
    }

    private static void ZipTraversal(Fixture f)
    {
        foreach (string path in new[] { "../escaped.txt", "resources/../../escaped.txt", "/absolute.txt", "C:/absolute.txt",
            "resources\\evil.txt", "resources/./evil.txt", "resources//evil.txt", "resources/file.txt:stream", "resources/trailing./file" })
            f.RejectArchive(new(), [(path, "hostile")], $"Accepted ZIP path {path}");
        Check(!File.Exists(Path.Combine(f.Root, "escaped.txt")), "Traversal wrote outside extraction directory");
    }

    private static void ManifestTraversal(Fixture f)
    {
        foreach (string path in new[] { "../escaped.png", "resources/../../escaped.png", "C:/outside.png", "/outside.png",
            "resources\\safe.png", "manifest.json", "resources/absent.png" })
            f.RejectArchive(new() { Root = new() { AlbedoTexturePath = path } }, [("resources/safe.png", "safe")],
                $"Accepted manifest resource path {path}");
    }

    private static void DuplicateEntries(Fixture f)
    {
        f.RejectArchive(new(), [("resources/a.png", "first"), ("resources/a.png", "second")], "Accepted duplicate ZIP path");
        f.RejectArchive(new(), [("resources/a.png", "first"), ("resources/A.png", "second")], "Accepted case-alias ZIP path");
        f.RejectArchive(new(), [("manifest.json", "{}")], "Accepted duplicate manifest");
    }

    private static void SymbolicLinkEntry(Fixture f)
    {
        string archive = f.Zip(new(), [("resources/link.png", "../../outside.png")]);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
            zip.GetEntry("resources/link.png")!.ExternalAttributes = unchecked((int)0xA1FF0000);
        string directory = f.NewPath();
        Reject(() => Extract(archive, directory), "Accepted ZIP symbolic link");
        Check(!Directory.Exists(directory), "Rejected symlink archive left extracted files");
    }

    private static void MissingArchiveEntries(Fixture f)
    {
        string archive = f.Zip(null, [("resources/a.png", "present")]);
        Reject(() => Extract(archive, f.NewPath()), "Accepted archive without manifest");
        f.RejectArchive(new() { Root = new() { SourceAssetPath = "resources/absent.mimodel" } }, [], "Accepted missing resource");
        f.RejectArchive(new() { ResourcePacks = new() { ["pack"] = "resources/absent.zip" } }, [], "Accepted missing resource pack");
        f.RejectArchive(new() { Root = new() { SourceAssetPath = "resources/model.mimodel" } },
            [("resources/model.mimodel", "{\"texture\":\"absent.png\"}")], "Accepted missing transitive model texture");
    }

    private static void DependencyTraversal(Fixture f)
    {
        foreach (string path in new[] { "../../outside.png", "C:/outside.png", "https://example.invalid/skin.png" })
            f.RejectArchive(new() { Root = new() { SourceAssetPath = "resources/model.mimodel" } },
                [("resources/model.mimodel", JsonSerializer.Serialize(new { texture = path }))], $"Accepted JSON dependency {path}");
        foreach (string uri in new[] { "../../outside.bin", "%2E%2E/%2E%2E/outside.bin", "file:///C:/outside.bin", "https://example.invalid/buffer.bin" })
            f.RejectArchive(new() { Root = new() { SourceAssetPath = "resources/model.gltf" } },
                [("resources/model.gltf", JsonSerializer.Serialize(new { buffers = new[] { new { uri } } }))], $"Accepted glTF dependency {uri}");
        f.RejectArchive(new() { Root = new() { SourceAssetPath = "resources/model.obj" } },
            [("resources/model.obj", "mtllib ../../outside.mtl")], "Accepted OBJ dependency traversal");
        f.RejectArchive(new() { Root = new() { SourceAssetPath = "resources/model.obj" } },
            [("resources/model.obj", "mtllib material.mtl"), ("resources/material.mtl", "map_Kd ../../outside.png")],
            "Accepted MTL dependency traversal");
    }

    private static void SourceNodePaths(Fixture f)
    {
        var root = Node("whole source");
        root.SourceAssetPath = f.File("source.mimodel", "{}");
        root.SpawnCategory = "Custom Models";
        root.SourceNodePath = [4];
        root.AddChild(Node("first"));
        var child = Node("selected part");
        child.SpawnCategory = "Custom Models";
        child.SourceAssetPath = root.SourceAssetPath;
        child.SourceNodePath = [4, 1];
        root.AddChild(child);
        var (whole, _) = f.Roundtrip(root);
        Check(whole.Root.Children[1].SourceNodePath!.SequenceEqual([4, 1]), "Child SourceNodePath not persisted");
        var (part, directory) = f.Roundtrip(child);
        Check(part.Root.SourceNodePath!.SequenceEqual([4, 1]), "Standalone part original source path not preserved");
        Resource(directory, part.Root.SourceAssetPath);
        Equal("selected part", part.Root.Name, "Standalone part selected wrong root");
        Equal(0, part.Root.Children.Count, "Standalone part included siblings");
        Check(root.SourceNodePath.SequenceEqual([4]) && child.SourceNodePath.SequenceEqual([4, 1]), "Export mutated source node paths");
    }

    private static void StableSourceNodePath(Fixture f)
    {
        string source = f.File("stable.mimodel", "{}");
        var originalParent = Node("original imported hierarchy");
        originalParent.SourceAssetPath = source;
        originalParent.SpawnCategory = "Custom Models";
        var child = Node("original second source child");
        child.SpawnCategory = "Custom Models";
        child.SourceAssetPath = source;
        child.SourceNodePath = [1];
        originalParent.AddChild(child);
        Equal(0, originalParent.Children.ToList().IndexOf(child), "Fixture must differ from original source index");
        var (selected, directory) = f.Roundtrip(child);
        Check(selected.Root.SourceNodePath!.SequenceEqual([1]), "Export replaced original source index 1 with live child index 0");
        Resource(directory, selected.Root.SourceAssetPath);
        var newParent = Node("new source-owning parent");
        newParent.SourceAssetPath = source;
        newParent.SpawnCategory = "Custom Models";
        originalParent.RemoveChild(child);
        newParent.AddChild(child);
        var (reparented, reparentedDirectory) = f.Roundtrip(child);
        Check(reparented.Root.SourceNodePath!.SequenceEqual([1]), "Reparenting changed original source selection");
        Resource(reparentedDirectory, reparented.Root.SourceAssetPath);
        Check(child.SourceNodePath.SequenceEqual([1]), "Export mutated stable source annotation");
    }

    private static void ParticleClosure(Fixture f)
    {
        string texture = f.File("particle/texture.png", "particle texture");
        var first = new ProjectSceneObjectEntry
        {
            LibraryEntryId = "first", Name = "first", AlbedoTexturePath = texture,
            Children = [new() { ParticleLibraryEntryId = "second" }]
        };
        var second = new ProjectSceneObjectEntry
        {
            LibraryEntryId = "second", Name = "second", ParticleLibraryEntryId = "first", NormalTexturePath = texture
        };
        var library = ProjectManager.Instance.Manifest.ObjectLibrary;
        library.AddRange([first, second, new() { LibraryEntryId = "unused", Name = "not included" }]);
        var root = new ParticleSpawnerSceneObject { Name = "emitter", ObjectId = "emitter", ParticleLibraryEntryId = "first", Amount = 19 };
        var (manifest, directory) = f.Roundtrip(root);
        Equal(2, manifest.ObjectLibrary.Count, "Transitive cycle was not deduplicated");
        Check(manifest.ObjectLibrary.Select(entry => entry.LibraryEntryId).ToHashSet().SetEquals(["first", "second"]), "Wrong library closure");
        Equal(19, manifest.Root.ParticleAmount, "Particle settings");
        var savedFirst = manifest.ObjectLibrary.Single(entry => entry.LibraryEntryId == "first");
        var savedSecond = manifest.ObjectLibrary.Single(entry => entry.LibraryEntryId == "second");
        Equal("second", savedFirst.Children[0].ParticleLibraryEntryId, "Nested library dependency");
        Equal(savedFirst.AlbedoTexturePath, savedSecond.NormalTexturePath, "Library resource deduplication");
        Equal("particle texture", File.ReadAllText(Resource(directory, savedFirst.AlbedoTexturePath)), "Library texture missing");
        Equal(texture, first.AlbedoTexturePath, "Export mutated live library");
        Equal(3, library.Count, "Export mutated live library count");
    }

    private static void MissingParticleLibrary(Fixture f)
    {
        AtomicFailure(f, new ParticleSpawnerSceneObject { Name = "broken emitter", ParticleLibraryEntryId = "absent" });
    }

    private static void ParticleOverlappingRoots(Fixture f)
    {
        string texture = f.File("overlap/descendant.png", "descendant resource");
        var descendant = new ProjectSceneObjectEntry
        {
            ObjectId = "object-B", LibraryEntryId = "B", Name = "descendant B", AlbedoTexturePath = texture
        };
        var ancestor = new ProjectSceneObjectEntry
        {
            ObjectId = "object-A", LibraryEntryId = "A", Name = "ancestor A", Children = [descendant]
        };
        ProjectManager.Instance.Manifest.ObjectLibrary.Add(ancestor);
        foreach (string[] order in new[] { new[] { "A", "B" }, new[] { "B", "A" } })
        {
            var root = Node("overlap " + string.Join(" then ", order));
            foreach (string id in order)
                root.AddChild(new ParticleSpawnerSceneObject
                {
                    Name = "emitter " + id, ObjectId = Guid.NewGuid().ToString("N"), ParticleLibraryEntryId = id
                });
            var (manifest, directory) = f.Roundtrip(root);
            Equal(1, manifest.ObjectLibrary.Count, "Ancestor and descendant were exported as overlapping library roots");
            Equal("A", manifest.ObjectLibrary[0].LibraryEntryId, "Wrong nonoverlapping library root selected");
            var entries = manifest.ObjectLibrary.SelectMany(entry => new[] { entry }.Concat(entry.Children)).ToList();
            Equal(1, entries.Count(entry => entry.LibraryEntryId == "B"), "Descendant library template B exported more than once");
            Equal(1, entries.Count(entry => entry.ObjectId == "object-B"), "Descendant object B exported more than once");
            Equal("descendant resource", File.ReadAllText(Resource(directory,
                entries.Single(entry => entry.LibraryEntryId == "B").AlbedoTexturePath)), "Descendant resource missing");
            Check(manifest.Root.Children.Select(entry => entry.ParticleLibraryEntryId).SequenceEqual(order), "Emitter references changed");
        }
        Equal(texture, descendant.AlbedoTexturePath, "Export mutated original descendant library template");
        Equal(1, ProjectManager.Instance.Manifest.ObjectLibrary.Count, "Export changed live library roots");
    }

    private static void IndependentExtraction(Fixture f)
    {
        var root = Node("independent");
        root.AlbedoTexturePath = f.File("independent.png", "original");
        string archive = f.NewPath(".nxObj");
        NxObjectArchive.Export(archive, root);
        string firstDirectory = f.NewPath();
        string secondDirectory = f.NewPath();
        var first = Extract(archive, firstDirectory);
        var second = Extract(archive, secondDirectory);
        string firstTexture = Resource(firstDirectory, first.Root.AlbedoTexturePath);
        string secondTexture = Resource(secondDirectory, second.Root.AlbedoTexturePath);
        Check(firstTexture != secondTexture, "Extractions shared a directory");
        File.WriteAllText(firstTexture, "changed");
        first.Root.Name = "changed";
        Equal("original", File.ReadAllText(secondTexture), "Second extraction resource was shared");
        Equal("independent", second.Root.Name, "Second extraction DTO was shared");
        Directory.Delete(firstDirectory, true);
        Check(File.Exists(secondTexture), "Removing first extraction damaged second");
    }

    private static void AnimatedTexture(Fixture f)
    {
        var root = Node("animated texture");
        string texture = f.File("keyframes/animated.png", "animated texture bytes");
        root.Keyframes["material.texture"] = [new() { Frame = 12, Value = texture }];
        var (manifest, directory) = f.Roundtrip(root);
        object value = manifest.Root.Keyframes["material.texture"][0].Value;
        string saved = value is JsonElement element ? element.GetString()! : (string)value;
        Equal("animated texture bytes", File.ReadAllText(Resource(directory, saved)), "Animated texture bytes");
        Equal(texture, (string)root.Keyframes["material.texture"][0].Value, "Source texture keyframe mutated");
    }

    private static void IndependentImport(Fixture f)
    {
        var manager = ProjectManager.Instance;
        var folderProperty = typeof(ProjectManager).GetProperty(nameof(ProjectManager.ProjectFolder))!;
        var fileProperty = typeof(ProjectManager).GetProperty(nameof(ProjectManager.ProjectFilePath))!;
        string originalFolder = manager.ProjectFolder;
        string originalFile = manager.ProjectFilePath;
        bool originalDirty = manager.IsDirty;
        try
        {
            folderProperty.SetValue(manager, f.Root);
            fileProperty.SetValue(manager, Path.Combine(f.Root, "isolated.nxProj"));
            Check(manager.HasProject, "Isolated project was not active");
            var root = Node("import root");
            var child = Node("import child");
            root.AddChild(child);
            root.CameraTextureObjectId = child.ObjectId;
            child.CameraTextureObjectId = "external-object";
            child.Keyframes["position.x"] = [new() { Frame = 3, Value = 2.5f }];
            string archive = f.NewPath(".nxObj");
            NxObjectArchive.Export(archive, root);
            var viewport = new Viewport();
            var spawnMenu = new SpawnMenu { Viewport = viewport };
            var first = NxObjectArchive.Import(archive, viewport, spawnMenu);
            var second = NxObjectArchive.Import(archive, viewport, spawnMenu);
            Equal(2, viewport.SceneObjects.Count, "Import root count");
            Equal(1, first.Children.Count, "First import child count");
            Equal(1, second.Children.Count, "Second import child count");
            Check(new[] { root.ObjectId, child.ObjectId, first.ObjectId, first.Children[0].ObjectId,
                second.ObjectId, second.Children[0].ObjectId }.Distinct().Count() == 6, "Imports reused object IDs");
            Equal(first.Children[0].ObjectId, first.CameraTextureObjectId, "First internal reference remap");
            Equal(second.Children[0].ObjectId, second.CameraTextureObjectId, "Second internal reference remap");
            Equal("", first.Children[0].CameraTextureObjectId, "First external reference cleared");
            Equal("", second.Children[0].CameraTextureObjectId, "Second external reference cleared");
            Equal(3, second.Children[0].Keyframes["position.x"][0].Frame, "Imported keyframe");
            Check(ReferenceEquals(first, first.Children[0].Parent), "Imported parent link missing");
            first.Children[0].Name = "changed";
            Equal("import child", second.Children[0].Name, "Repeated import shared objects");
            Equal(2, Directory.GetDirectories(Path.Combine(manager.ModelsFolder, "nxobjects")).Length,
                "Import extraction directories were not independent");
        }
        finally
        {
            folderProperty.SetValue(manager, originalFolder);
            fileProperty.SetValue(manager, originalFile);
            manager.SetDirty(originalDirty);
        }
    }

    private static void MissingAnimatedTexture(Fixture f)
    {
        var root = Node("missing animated texture");
        root.Keyframes["material.texture"] = [new() { Frame = 1, Value = Path.Combine(f.Root, "absent.png") }];
        AtomicFailure(f, root);
    }

    private static void AnimatedTextureTraversal(Fixture f)
    {
        foreach (string path in new[] { "../../outside.png", "C:/outside.png", "resources/absent.png" })
            f.RejectArchive(new()
            {
                Root = new()
                {
                    Keyframes = new() { ["material.texture"] = [new() { Frame = 1, Value = path }] }
                }
            }, [], $"Accepted animated texture manifest path {path}");
    }

    private static void AnimatedFont(Fixture f)
    {
        var root = Node("animated font");
        root.Keyframes["text.font"] =
        [
            new() { Frame = 0, Value = "minecraftia" },
            new() { Frame = 12, Value = f.File("fonts/custom.ttf", "mock font bytes") }
        ];
        var (manifest, directory) = f.Roundtrip(root);
        string Text(object value) => value is JsonElement element ? element.GetString()! : (string)value;
        Equal("minecraftia", Text(manifest.Root.Keyframes["text.font"][0].Value), "Built-in font sentinel");
        Equal("mock font bytes", File.ReadAllText(Resource(directory, Text(manifest.Root.Keyframes["text.font"][1].Value))), "Font dependency");
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "kilo", "NxObjectTests", Guid.NewGuid().ToString("N"));

        public Fixture() => Directory.CreateDirectory(Root);

        public string NewPath(string suffix = "") => Path.Combine(Root, Guid.NewGuid().ToString("N") + suffix);

        public string File(string relative, string content)
        {
            string path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        public (NxObjectManifest Manifest, string Directory) Roundtrip(SceneObject root)
        {
            string archive = NewPath(".nxObj");
            string directory = NewPath();
            NxObjectArchive.Export(archive, root);
            return (Extract(archive, directory), directory);
        }

        public string Zip(NxObjectManifest? manifest, (string Path, string Content)[] entries)
        {
            string path = NewPath(".nxObj");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            if (manifest != null)
            {
                using var output = archive.CreateEntry("manifest.json").Open();
                JsonSerializer.Serialize(output, manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
            foreach (var entry in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(entry.Path).Open());
                writer.Write(entry.Content);
            }
            return path;
        }

        public void RejectArchive(NxObjectManifest manifest, (string Path, string Content)[] entries, string message)
        {
            string archive = Zip(manifest, entries);
            string directory = NewPath();
            Reject(() => Extract(archive, directory), message);
            Check(!System.IO.Directory.Exists(directory), "Rejected archive left extracted files behind");
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
