using System.Reflection;
using System.Text.Json;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.graphics;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private static BoneSceneObject WiggleBone() => new()
    {
        Name = "Wiggle test bone", PivotOffset = vec3.Zero, EndOffset = vec3.UnitY,
        Wiggle = new() { Enabled = true, Tail = new() { Enabled = true, Gravity = 0, Wind = 0, Stiffness = 100, Damping = 2 } }
    };

    private static MiBoneSceneObject WiggleMiBone()
    {
        var bone = new MiBoneSceneObject
        {
            Name = "Bend wiggle test", EndOffset = vec3.UnitY,
            Wiggle = new()
            {
                Enabled = true, Tail = new() { Enabled = false },
                Bend = new() { Enabled = true, Stiffness = 100, Damping = 2, Gravity = 0, Wind = 0 }
            }
        };
        bone.SetBendParameters(new BendParams
        {
            AxisX = true, Part = BendPart.Upper, BendOffset = 8, BendSize = 4,
            DirectionMin = new(-80, 0, 0), DirectionMax = new(80, 0, 0)
        }, 1);
        return bone;
    }

    private static void WiggleMotion(Fixture fixture)
    {
        float Swing(float damping, float mass)
        {
            var bone = WiggleBone();
            bone.Wiggle = bone.Wiggle with { Tail = bone.Wiggle.Tail with { Damping = damping, Mass = mass } };
            var sim = new WiggleSimulation([bone]);
            var scene = new WiggleSceneSettings { Preroll = 0 };
            sim.Step(0, scene, default);
            bone.SetLocalPosition(vec3.UnitX * 0.5f);
            sim.Step(1 / 60f, scene, default);
            vec3 endpoint = bone.GetEndWorldPosition();
            Check(endpoint.x < 0.3f, "Tail lags an animated parent rather than following rigidly");
            Near(1, (endpoint - bone.GetWorldPosition()).Length, "Rigid bone length is preserved");
            Near(vec3.UnitX * 0.5f, bone.LocalPosition, "Simulation leaves authored position unchanged");
            Near(vec3.Zero, bone.LocalRotation, "Simulation leaves authored rotation unchanged");
            float late = 0;
            for (int i = 0; i < 180; i++)
            {
                sim.Step(1 / 60f, scene, default);
                if (i > 120) late = Math.Max(late, MathF.Abs(bone.GetEndWorldPosition().x - 0.5f));
            }
            return late;
        }
        Check(Swing(30, 1) < Swing(0.1f, 1) * 0.2f, "Damping measurably suppresses late oscillation");
        Swing(2, 10);
        var falling = WiggleBone();
        falling.Wiggle = falling.Wiggle with
        {
            Head = new() { Enabled = true, Stiffness = 0, Damping = 0 },
            Tail = new() { Enabled = true, Stiffness = 0, Damping = 0 }
        };
        var physics = new WiggleSimulation([falling]);
        var settings = new WiggleSceneSettings { Preroll = 0 };
        for (int i = 0; i < 60; i++) physics.Step(1 / 60f, settings, default);
        Check(falling.GetWorldPosition().y < -4, "Free head and tail respond to gravity");
        var windBone = WiggleBone();
        windBone.Wiggle = windBone.Wiggle with { Tail = windBone.Wiggle.Tail with { Wind = 1 } };
        physics = new([windBone]);
        for (int i = 0; i < 60; i++)
            physics.Step(1 / 60f, settings, new(true, 0, 0, 0, 0, 4, i / 60f));
        Check(windBone.GetEndWorldPosition().x > 0.01f, "Scene wind drives enabled tail dynamics");
        var before = windBone.GetWorldMatrixNoPivot();
        windBone.Wiggle = windBone.Wiggle with { Freeze = true };
        windBone.SetLocalPosition(vec3.UnitX * 2);
        physics.Step(1 / 60f, settings, default);
        Equal(before, windBone.GetWorldMatrixNoPivot(), "Freeze holds the evaluated world pose");
        windBone.Wiggle = windBone.Wiggle with { Mute = true };
        physics.Step(1 / 60f, settings, default);
        Near(vec3.UnitX * 2, windBone.GetWorldPosition(), "Mute restores the authored pose");
        Reject(() => new WiggleSettings { Tail = new() { Mass = 0 } }.Validate(), "Zero mass is rejected");
        Reject(() => new WiggleSettings { Radius = float.NaN }.Validate(), "Nonfinite radius is rejected");
        var bent = WiggleMiBone();
        physics = new([bent]);
        physics.Step(0, settings, default);
        bent.SetEditableBendAngle(new(60, 0, 0));
        physics.Step(1 / 60f, settings, default);
        Check(bent.GetEvaluatedEditableBendAngle().x < 10, "Independent bend spring lags animated bend");
        Near(new vec3(60, 0, 0), bent.GetEditableBendAngle(), "Bend wiggle preserves authored angles");
        Equal(mat4.Identity, bent.GetWorldMatrixNoPivot(), "Bend-only motion leaves the bone base transform unchanged");
        for (int i = 0; i < 240; i++) physics.Step(1 / 60f, settings, default);
        Check(MathF.Abs(bent.GetEvaluatedEditableBendAngle().x - 60) < 2, "Bend spring settles toward the animated target");
        var heldBend = bent.GetEvaluatedEditableBendAngle();
        bent.Wiggle = bent.Wiggle with { Freeze = true };
        bent.SetEditableBendAngle(new(-60, 0, 0));
        physics.Step(1 / 60f, settings, default);
        Near(heldBend, bent.GetEvaluatedEditableBendAngle(), "Freeze holds bend and base pose");
        bent.Wiggle = bent.Wiggle with { Mute = true };
        physics.Step(1 / 60f, settings, default);
        Near(new vec3(-60, 0, 0), bent.GetEvaluatedEditableBendAngle(), "Mute restores authored bend");
        bent.Wiggle = bent.Wiggle with
        { Freeze = false, Mute = false, Bend = bent.Wiggle.Bend with { Wind = 1, Stiffness = 0 } };
        physics = new([bent]);
        for (int i = 0; i < 120; i++)
            physics.Step(1 / 60f, settings, new(true, 0, 0, 90, 0, 100, i / 60f));
        vec3 limit = bent.GetEvaluatedEditableBendAngle();
        Check(MathF.Abs(limit.x + 60) > 5 && MathF.Abs(limit.x) <= 80.001f && limit.y == 0 && limit.z == 0,
            "Wind drives bend while respecting model axes and limits");
        bent = WiggleMiBone();
        physics = new([bent]);
        physics.Step(0, settings, default);
        bent.SetLocalRotation(new(45, 0, 0));
        physics.Step(1 / 60f, settings, default);
        Check(MathF.Abs(bent.GetEvaluatedEditableBendAngle().x) > 1,
            "Rotating the driver produces bend inertia even without gravity or wind");
        Near(vec3.Zero, bent.GetEditableBendAngle(), "Driver inertia does not author bend angles");
    }

    private static void WiggleChainsCollisions(Fixture fixture)
    {
        var parent = WiggleBone();
        var child = WiggleBone();
        child.SetLocalPosition(vec3.UnitY);
        child.Wiggle = child.Wiggle with { Connected = true, Chain = true };
        parent.AddChild(child);
        var scene = new WiggleSceneSettings { Preroll = 0, Quality = 16 };
        var sim = new WiggleSimulation([parent]);
        sim.Step(0, scene, default);
        parent.SetLocalPosition(vec3.UnitX * 0.5f);
        for (int i = 0; i < 60; i++) sim.Step(1 / 60f, scene, default);
        Near(parent.GetEndWorldPosition(), child.GetWorldPosition(), "Connected child head shares parent tail");
        Near(1, (child.GetEndWorldPosition() - child.GetWorldPosition()).Length, "Connected child retains its length");
        var collider = new SceneObject { PivotOffset = vec3.Zero, WiggleCollider = new() { Enabled = true, Radius = 0.4f } };
        collider.SetLocalPosition(new(0, -1, 0));
        var bone = WiggleBone();
        bone.SetLocalPosition(new(0, 0, 0));
        bone.Wiggle = bone.Wiggle with
        {
            Collisions = true, Radius = 0.1f, Friction = 0.5f, Bounce = 0.2f,
            Head = new() { Enabled = true, Stiffness = 0, Damping = 2 },
            Tail = new() { Enabled = true, Stiffness = 0, Damping = 2 }
        };
        sim = new([bone, collider]);
        for (int i = 0; i < 120; i++) sim.Step(1 / 120f, scene, default);
        Check((bone.GetWorldPosition() - collider.GetWorldPosition()).Length >= 0.499f,
            "Head sphere remains outside the collider surface");
        Check(bone.GetWorldPosition().y > -0.51f, "Collision prevents the head falling through the sphere");
        Check((bone.GetEndWorldPosition() - collider.GetWorldPosition()).Length >= 0.499f,
            "Tail sphere remains outside the collider surface");
        var floor = new SceneObject { PivotOffset = vec3.Zero };
        var mesh = new Mesh(null!);
        mesh.Vertices.AddRange([new(-10, -1, -10), new(10, -1, -10), new(0, -1, 10)]);
        floor.Visuals.Add(mesh);
        bone = WiggleBone();
        bone.Wiggle = bone.Wiggle with
        {
            MeshCollisions = true, Radius = 0.1f,
            Head = new() { Enabled = true, Stiffness = 0, Damping = 2 },
            Tail = new() { Enabled = true, Stiffness = 0, Damping = 2 }
        };
        sim = new([bone, floor]);
        for (int i = 0; i < 120; i++) sim.Step(1 / 120f, scene, default);
        Check(bone.GetWorldPosition().y >= -0.9001f, "Mesh collision supports the head above a visible triangle");
        Check(bone.GetWorldPosition().y < -0.8f, "Head settles at the configured mesh collision radius");
        Check(bone.GetEndWorldPosition().y >= -0.9001f, "Tail also stays outside the mesh surface");
        floor.ObjectVisible = false;
        for (int i = 0; i < 120; i++) sim.Step(1 / 120f, scene, default);
        Check(bone.GetWorldPosition().y < -1.5f, "Hidden meshes no longer collide");
        floor.ObjectVisible = true;
        bone = WiggleBone();
        bone.Wiggle = bone.Wiggle with
        { MeshCollisions = true, Head = new() { Enabled = true, Stiffness = 0, Damping = 0 }, Tail = new() { Enabled = true, Stiffness = 0, Damping = 0 } };
        bone.AddChild(floor);
        sim = new([bone]);
        for (int i = 0; i < 120; i++) sim.Step(1 / 120f, scene, default);
        Check(bone.GetWorldPosition().y < -2, "Descendant meshes do not feed back into their wiggle bone");
        var tree = ClothCollisionTree.ForScene([new(new(-10, 0, -10), new(10, 0, -10), new(0, 0, 10))], []);
        vec3 point = new(0, -2, 0), velocity = new(2, -4, 0);
        tree.CollideSphere(ref point, vec3.UnitY, ref velocity, 0.1f, 0.5f, 0.25f);
        Near(0.1f, point.y, "Swept endpoint catches crossing through a static mesh");
        Near(new vec3(1, 1, 0), velocity, "Mesh contacts apply configured friction and bounce");
        var bentCollider = WiggleMiBone();
        bentCollider.Wiggle = new();
        var bentMesh = new Mesh(null!)
        {
            GpuBend = new() { SegmentAxis = 1, BendStart = 0, BendEnd = 1, BendSize = 1, Plane = true }
        };
        bentMesh.Vertices.AddRange([vec3.Zero, vec3.UnitX, vec3.UnitY]);
        bentCollider.Visuals.Add(bentMesh);
        foreach (float angle in new[] { 0f, 60f, -30f })
        {
            bentCollider.SetEditableBendAngle(new(angle, 0, 0));
            var vertices = VeldridSceneRenderer.BuildVertices(bentMesh).Select(v => v.Position).ToArray();
            vec3 normal = vec3.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0]).Normalized;
            vec3 center = (vertices[0] + vertices[1] + vertices[2]) / 3;
            bone = WiggleBone();
            bone.Wiggle = bone.Wiggle with
            {
                MeshCollisions = true, Radius = 0.1f,
                Head = new() { Enabled = true, Gravity = 0, Stretch = 1 }, Tail = new() { Enabled = false }
            };
            bone.SetLocalPosition(center + normal * 0.02f);
            sim = new([bone, bentCollider]);
            sim.Step(0, scene, default);
            Check(vec3.Dot(bone.GetWorldPosition() - center, normal) >= 0.099f,
                "Mesh contacts follow the renderer's current bent surface");
        }
        var skinRoot = new SceneObject { PivotOffset = vec3.Zero };
        var skinBone = new BoneSceneObject { BoneName = "skin", PivotOffset = vec3.Zero };
        skinRoot.AddChild(skinBone);
        var skinMesh = new Mesh(null!);
        skinMesh.Vertices.AddRange([vec3.Zero, vec3.UnitX, vec3.UnitY]);
        skinMesh.BoneIndices.AddRange([ivec4.Zero, ivec4.Zero, ivec4.Zero]);
        skinMesh.BoneWeights.AddRange([new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0)]);
        skinMesh.BoneNames.Add("skin"); skinMesh.BoneInverseBindMatrices.Add(mat4.Identity);
        skinRoot.Visuals.Add(skinMesh);
        skinBone.SetLocalPosition(new(0, 0, 2));
        bone = WiggleBone();
        bone.BoneName = "skin";
        bone.Wiggle = bone.Wiggle with
        { MeshCollisions = true, Radius = 0.1f, Head = new() { Enabled = true, Gravity = 0, Stretch = 1 }, Tail = new() { Enabled = false } };
        bone.SetLocalPosition(new(0.3f, 0.3f, 2.02f));
        sim = new([bone, skinRoot]); sim.Step(0, scene, default);
        Check(bone.GetWorldPosition().z >= 2.099f,
            "Mesh contacts use the live skin palette and do not exclude another rig with the same bone name");
        skinMesh.Indices = [0, 1, 99];
        Reject(() => sim.Step(0, scene, default), "Invalid mesh collider indices are reported explicitly");
    }

    private static void WiggleBakePersistence(Fixture fixture)
    {
        var singleton = typeof(Timeline).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)!;
        var previous = singleton.GetValue(null);
        var settings = ProjectManager.Instance.Manifest.Settings;
        var bone = WiggleMiBone(); bone.Wiggle = bone.Wiggle with { MeshCollisions = true }; bone.AssignObjectId();
        var rig = new SceneObject { Name = "Rig", PivotOffset = vec3.Zero }; rig.AssignObjectId(); rig.AddChild(bone);
        var viewport = new Viewport(); viewport.SceneObjects.Add(rig);
        var timeline = new Timeline { Viewport = viewport };
        singleton.SetValue(null, timeline);
        ProjectManager.Instance.Manifest.Settings = new() { Wiggle = new() { Preroll = 0 } };
        var update = typeof(Viewport).GetMethod("UpdateWiggle", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Seek(int frame)
        {
            typeof(Timeline).GetField("_currentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(timeline, frame);
            typeof(Timeline).GetMethod("ApplyKeyframesAtCurrentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(timeline, null);
        }
        try
        {
            timeline.AddKeyframeForProperty(rig, "position.x", 0);
            rig.SetLocalPosition(vec3.UnitX);
            timeline.AddKeyframeForProperty(rig, "position.x", 20);
            timeline.AddKeyframeForProperty(bone, "bend.x", 0);
            bone.SetEditableBendAngle(new(60, 0, 0));
            timeline.AddKeyframeForProperty(bone, "bend.x", 20);
            Seek(20);
            Check(viewport.BakeWiggle(), "Bake starts without a graphics context");
            Check(bone.WiggleBake == null && viewport.IsWiggleBaking, "Bake publishes no incomplete frames");
            while (viewport.IsWiggleBaking) viewport.AdvanceWiggleBake();
            Check(viewport.WiggleError == null && bone.WiggleBake != null, "Full bake succeeds: " + viewport.WiggleError);
            Equal(20, timeline.CurrentFrame, "Bake restores playhead");
            Near(vec3.UnitX, rig.LocalPosition, "Bake restores animated driver pose");
            Equal(timeline.MaxFrames + 1, bone.WiggleBake!.Frames.Length, "Bake includes every frame and the timeline end");
            void At(int frame)
            {
                Seek(frame);
                InverseKinematicsSolver.Evaluate(viewport.SceneObjects);
                update.Invoke(viewport, [true]);
            }
            At(10); var pose = bone.GetWorldMatrixNoPivot();
            var bendPose = bone.GetEvaluatedEditableBendAngle();
            var endpoint = bone.GetEndWorldPosition();
            At(0); At(20); At(10);
            Equal(pose, bone.GetWorldMatrixNoPivot(), "Backward/random seeking reproduces exact baked transforms");
            Near(bendPose, bone.GetEvaluatedEditableBendAngle(), "Backward seeking reproduces saved bend angles");
            Near(endpoint, bone.GetEndWorldPosition(), "Saved bend and world transform reproduce the actual endpoint");
            Check(MathF.Abs(bendPose.x - bone.GetEditableBendAngle().x) > 0.1f, "Baked bend contains secondary motion, not authored angles");
            var signature = typeof(Viewport).GetMethod("ComputeShadowCacheSignature", BindingFlags.Instance | BindingFlags.NonPublic)!;
            int before = (int)signature.Invoke(viewport, [viewport.SceneObjects, false])!;
            At(20);
            Check(before != (int)signature.Invoke(viewport, [viewport.SceneObjects, false])!, "Wiggle motion invalidates cached shadows");
            var (archive, _) = fixture.Roundtrip(rig);
            var entry = JsonSerializer.Deserialize(JsonSerializer.Serialize(archive.Root, AppJsonContext.Default.ProjectSceneObjectEntry),
                AppJsonContext.Default.ProjectSceneObjectEntry)!;
            var loaded = WiggleMiBone();
            var apply = typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!;
            apply.Invoke(null, [loaded, entry.Children.Single(), null]);
            Equal(bone.Wiggle, loaded.Wiggle, "Source-generated archive/project persistence restores bone settings");
            Check(loaded.WiggleBake!.Frames[10].SequenceEqual(bone.WiggleBake.Frames[10]), "Saved bake survives JSON and archive roundtrip");
            Check(loaded.WiggleBake.BendFrames[10].SequenceEqual(bone.WiggleBake.BendFrames[10]),
                "Saved bend frames survive source-generated JSON and archive roundtrip");
            var duplicate = (BoneSceneObject)typeof(SceneTree).GetMethod("CreateSceneObjectDuplicate",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(new SceneTree(), [bone, new Dictionary<string, string>()])!;
            Equal(bone.Wiggle, duplicate.Wiggle, "Duplication preserves independent immutable settings");
            Check(duplicate.WiggleBake != null, "Duplication retains saved frames");
            var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(bone);
            var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
                .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
            ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { bone }])!;
            ulong authored = Hash();
            bone.Wiggle = bone.Wiggle with { Tail = bone.Wiggle.Tail with { Damping = 42 } };
            Check(Hash() != authored, "Wiggle edits participate in undo dirty detection");
            bone.Wiggle = bone.Wiggle with { MeshCollisions = true, Bend = bone.Wiggle.Bend with { Enabled = false } };
            Check(Hash() != authored, "Mesh collision and bend settings participate in undo dirty detection");
            apply.Invoke(null, [bone, snapshot, null]);
            Equal(authored, Hash(), "Undo restores settings and bake");
            var saved = bone.WiggleBake;
            Check(viewport.BakeWiggle(), "Rebake starts with previous cache retained");
            viewport.AdvanceWiggleBake();
            viewport.CancelWiggleBake();
            Check(ReferenceEquals(saved, bone.WiggleBake), "Cancel keeps previous complete bake");
            bone.Wiggle = bone.Wiggle with { Tail = bone.Wiggle.Tail with { Mass = 2 } };
            Check(viewport.BakeWiggle(), "Updated settings can start a new bake");
            bone.EndOffset = vec3.Zero;
            viewport.AdvanceWiggleBake();
            Check(!viewport.IsWiggleBaking && viewport.WiggleError != null && ReferenceEquals(saved, bone.WiggleBake),
                "Failed bake is reported and keeps previous frames");
            bone.EndOffset = vec3.UnitY;
            viewport.ClearWiggleBake();
            Check(bone.WiggleBake == null, "Clear removes saved frames");
            bool rejected = false;
            try { At(0); } catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { rejected = true; }
            Check(rejected, "Export/cloth sampling rejects unbaked wiggle rather than silently exporting a rigid pose");
            Seek(0);
            InverseKinematicsSolver.Evaluate(viewport.SceneObjects);
            update.Invoke(viewport, [false]);
            var live = bone.GetWorldMatrixNoPivot();
            InverseKinematicsSolver.Evaluate(viewport.SceneObjects);
            update.Invoke(viewport, [false]);
            Equal(live, bone.GetWorldMatrixNoPivot(), "Repeated camera/feed draws do not advance live simulation");
            Check(viewport.WiggleError == null, "Successful live preview clears an earlier export error");
            bone.Wiggle = bone.Wiggle with { Freeze = true };
            rig.SetLocalPosition(vec3.UnitX * 2);
            InverseKinematicsSolver.Evaluate(viewport.SceneObjects);
            update.Invoke(viewport, [false]);
            Equal(live, bone.GetWorldMatrixNoPivot(), "Freeze survives paused driver edits and live cache resets");
        }
        finally
        {
            singleton.SetValue(null, previous);
            ProjectManager.Instance.Manifest.Settings = settings;
        }
    }

    private static unsafe void WiggleControls(Fixture fixture)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        var settings = ProjectManager.Instance.Manifest.Settings;
        var singleton = typeof(Timeline).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)!;
        var previousTimeline = singleton.GetValue(null);
        try
        {
            ProjectManager.Instance.Manifest.Settings = new() { Wiggle = new() { Preroll = 0 } };
            singleton.SetValue(null, null);
            var io = ImGui.GetIO(); io.IniFilename = null; io.LogFilename = null;
            io.DisplaySize = new(1200, 1800); io.DeltaTime = 1 / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var bone = WiggleMiBone(); bone.Wiggle = bone.Wiggle with
            { Enabled = false, Bend = bone.Wiggle.Bend with { Enabled = false } };
            var viewport = new Viewport(); viewport.SceneObjects.Add(bone);
            var panel = new PropertiesPanel { Viewport = viewport };
            var draw = typeof(PropertiesPanel).GetMethod("DrawWiggle", BindingFlags.Instance | BindingFlags.NonPublic)!;
            uint hovered = 0, target = 0;
            void Frame(string key, float y = -100, bool down = false, bool boneControl = false, bool bendControl = false)
            {
                float x = key == "text.wiggle.clear_bake"
                    ? 55 + ImGui.CalcTextSize(Localization.T("text.wiggle.bake")).X +
                        ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X
                    : 70;
                io.AddMousePosEvent(x, y); io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new(900, 1700), ImGuiCond.Always);
                ImGui.Begin("Wiggle controls");
                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                draw.Invoke(panel, [bone]);
                ImGui.PushID("wiggle");
                if (boneControl) { ImGui.PushID("bone"); ImGui.PushID(Localization.T("text.wiggle.bone")); }
                if (bendControl) { ImGui.PushID("text.wiggle.bend"); ImGui.PushID(Localization.T("text.wiggle.bend")); }
                target = ImGui.GetID(bendControl ? Localization.DisplayLabel("Enabled") : Localization.T(key));
                if (bendControl) { ImGui.PopID(); ImGui.PopID(); }
                if (boneControl) { ImGui.PopID(); ImGui.PopID(); }
                ImGui.PopID();
                hovered = context.HoveredId;
                ImGui.End(); ImGui.Render();
            }
            void Click(string key, bool boneControl = false, bool bendControl = false)
            {
                Frame(key, boneControl: boneControl, bendControl: bendControl); Frame(key, boneControl: boneControl, bendControl: bendControl);
                float found = -1;
                for (int y = 40; y < 1650; y += 3)
                {
                    Frame(key, y, boneControl: boneControl, bendControl: bendControl);
                    if (hovered == target) { found = y; break; }
                }
                Check(found >= 0, "Wiggle UI control is reachable: " + key);
                Frame(key, found, true, boneControl, bendControl); Frame(key, found, false, boneControl, bendControl);
            }
            Click("text.wiggle.enable_bone", true);
            Check(bone.Wiggle.Enabled, "Rendered Enable Bone checkbox enables physics");
            Click("text.wiggle.mesh_collisions", true);
            Check(bone.Wiggle.MeshCollisions, "Rendered Mesh Collisions checkbox enables mesh contacts independently");
            Click("Enabled", true, true);
            Check(bone.Wiggle.Bend.Enabled, "Rendered Bend Enabled checkbox enables its independent spring");
            Click("text.wiggle.bake");
            while (viewport.IsWiggleBaking) viewport.AdvanceWiggleBake();
            Check(bone.WiggleBake != null, "Rendered Bake button creates saved poses");
            Click("text.wiggle.clear_bake");
            Check(bone.WiggleBake == null, "Rendered Clear button restores live preview");
            Check(viewport.BakeWiggle(), "Bake can restart after clearing");
            void PopupFrame()
            {
                io.AddMousePosEvent(-100, -100);
                ImGui.NewFrame();
                viewport.DrawWiggleBakePopup();
                Check(ImGui.IsPopupOpen(Localization.T("text.wiggle.bake") + "##WiggleBakeProgress"),
                    "Wiggle bake opens a modal to prevent edits");
                ImGui.Render();
            }
            PopupFrame(); PopupFrame();
            Check(ImGui.GetDrawData().TotalVtxCount > 0, "Bake progress modal renders actual controls");
            viewport.CancelWiggleBake();
            PopupFrame();
            Check(!viewport.IsWiggleBaking && bone.WiggleBake == null, "Modal cancellation keeps the scene unbaked");
        }
        finally
        {
            ProjectManager.Instance.Manifest.Settings = settings;
            singleton.SetValue(null, previousTimeline);
            ImGui.DestroyContext(context); ImGui.SetCurrentContext(previous);
        }
    }

    private static unsafe void WiggleGraphics(Fixture fixture)
        {
            using var glfw = Silk.NET.GLFW.Glfw.GetApi();
            Check(glfw.Init(), "GLFW initialization");
            glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
            glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
            glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
            glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
            var window = glfw.CreateWindow(64, 64, "Wiggle GPU regression", null, null);
            Check(window != null, "OpenGL context");
            try
            {
                glfw.MakeContextCurrent(window);
                using var gl = Silk.NET.OpenGL.GL.GetApi(glfw.GetProcAddress);
                using var preview = new PreviewRenderer(gl, 160) { Distance = 3, AutoRotateSpeed = 0 };
                var bone = WiggleBone();
                using var mesh = new Mesh(gl, [new(-0.15f, 0, 0), new(0.15f, 0, 0), new(0.15f, 1, 0), new(-0.15f, 1, 0)],
                    null, [0, 1, 2, 0, 2, 3]);
                mesh.DoubleSided = true;
                bone.Visuals.Add(mesh);
                byte[] Render()
                {
                    preview.Render([], "wiggle", 0, sceneRoot: bone, isolatedLighting: true);
                    byte[] pixels = new byte[160 * 160 * 4];
                    gl.BindTexture(Silk.NET.OpenGL.GLEnum.Texture2D, preview.ColorTexture);
                    fixed (byte* pointer = pixels)
                        gl.GetTexImage(Silk.NET.OpenGL.GLEnum.Texture2D, 0, Silk.NET.OpenGL.GLEnum.Rgba,
                            Silk.NET.OpenGL.GLEnum.UnsignedByte, pointer);
                    return pixels;
                }
                var simulation = new WiggleSimulation([bone]);
                var scene = new WiggleSceneSettings { Preroll = 0 };
                simulation.Step(0, scene, default);
                bone.SetLocalPosition(vec3.UnitX * 0.6f);
                simulation.Step(1 / 60f, scene, default);
                byte[] deformed = Render();
                InverseKinematicsSolver.Evaluate([bone]);
                byte[] rigid = Render();
                int changed = Enumerable.Range(0, 160 * 160).Count(i =>
                    rigid[i * 4] != deformed[i * 4] || rigid[i * 4 + 1] != deformed[i * 4 + 1] || rigid[i * 4 + 2] != deformed[i * 4 + 2]);
                Check(changed > 100, $"Wiggle changes actual bone mesh pixels, got {changed}");
                simulation.Apply();
                Check(deformed.SequenceEqual(Render()), "Reapplying evaluated wiggle reproduces exact preview pixels");
                InverseKinematicsSolver.Evaluate([bone]);
                Check(rigid.SequenceEqual(Render()), "Disabling dynamic pose restores exact rigid rendering");
                var bent = WiggleMiBone();
                bone.Visuals.Clear();
                bent.Visuals.Add(mesh);
                mesh.GpuBend = new()
                { SegmentAxis = 1, BendStart = 0.25f, BendEnd = 0.75f, BendSize = 0.5f, Pivot = new(0, 0.5f, 0), Plane = true };
                bone = bent;
                bent.SetEditableBendAngle(new(60, 0, 0));
                simulation = new([bent]);
                simulation.Step(0, scene, default);
                bent.SetEditableBendAngle(new(-60, 0, 0));
                simulation.Step(1 / 60f, scene, default);
                deformed = Render();
                InverseKinematicsSolver.Evaluate([bent]);
                rigid = Render();
                changed = Enumerable.Range(0, 160 * 160).Count(i =>
                    rigid[i * 4] != deformed[i * 4] || rigid[i * 4 + 1] != deformed[i * 4 + 1] || rigid[i * 4 + 2] != deformed[i * 4 + 2]);
                Check(changed > 100, $"Independent bend wiggle changes actual mesh pixels, got {changed}");
                simulation.Apply();
                Check(deformed.SequenceEqual(Render()), "Reapplying wiggle bend reproduces exact mesh pixels");
                InverseKinematicsSolver.Evaluate([bent]);
                Check(rigid.SequenceEqual(Render()), "Resetting wiggle restores exact authored bend pixels");
                Equal(Silk.NET.OpenGL.GLEnum.NoError, gl.GetError(), "Wiggle rendering has no OpenGL errors");
            }
            finally { glfw.DestroyWindow(window); glfw.Terminate(); }
        }
}
