using System.Text.Json;
using System.Reflection;
using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.history;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static MiBoneSceneObject ReplacementBone(string name, vec3 position)
    {
        var bone = new MiBoneSceneObject { Name = name, BoneName = name };
        bone.AssignObjectId();
        bone.SetLocalPosition(position);
        bone.CommitBasePose();
        return bone;
    }

    private static void LibraryReplacementMapping(Fixture fixture)
    {
        var old = new SceneObject { Name = "Steve", ObjectId = Guid.NewGuid().ToString("N"), LibrarySourceId = "steve", SourceAssetPath = "old.mimodel" };
        old.SetLocalPosition(new vec3(10, 20, 30));
        var arm = ReplacementBone("left_arm", new vec3(1, 2, 3));
        arm.LibrarySourceId = "left-arm";
        arm.OffsetPosition = new vec3(2, 3, 4);
        arm.OffsetRotation = new vec3(.2f, .3f, .4f);
        arm.OffsetScale = new vec3(1.5f, 2, 3);
        arm.Keyframes["rotation.x"] = [new ObjectKeyframe { Frame = 12, Value = .7f, InterpolationType = "ease_in" }];
        arm.ObjectVisible = false;
        old.AddChild(arm);
        var target = new SceneObject { Name = "Custom", ObjectId = Guid.NewGuid().ToString("N"), SourceAssetPath = "custom.mimodel" };
        var newArm = ReplacementBone("Arm_L", new vec3(8, 9, 10));
        newArm.SourceAssetPath = "custom.mimodel";
        newArm.SourceNodePath = [0];
        target.AddChild(newArm);
        var replacement = LibraryAssetReplacement.Prepare(old, target);
        Equal(2, replacement.Matches.Count, "Common limb alias matches");
        Equal(0, replacement.RetainedPartCount, "No missing parts");
        Near(old.Position, target.Position, "Root placement preserved");
        Near(arm.OffsetPosition, newArm.OffsetPosition, "New model rest position retained");
        Near(arm.OffsetRotation, newArm.OffsetRotation, "Pose rotation preserved");
        Near(arm.OffsetScale, newArm.OffsetScale, "Pose scale preserved");
        Equal(arm.ObjectId, newArm.ObjectId, "Stable object ID");
        Equal(arm.LibrarySourceId, newArm.LibrarySourceId, "Stable library identity");
        Equal("Arm_L", newArm.BoneName, "New rig bone identity retained");
        Check(newArm.SourceNodePath!.SequenceEqual([0]), "New source path retained");
        Check(newArm.SourceAssetPath.EndsWith("custom.mimodel"), "New source asset retained");
        Check(!newArm.ObjectVisible, "Visibility preserved");
        var key = newArm.Keyframes["rotation.x"].Single();
        Equal(12, key.Frame, "Keyframe frame preserved");
        Equal("ease_in", key.InterpolationType, "Interpolation preserved");
        Check(!ReferenceEquals(arm.Keyframes["rotation.x"], newArm.Keyframes["rotation.x"]), "No shared animation lists");
        newArm.Keyframes["rotation.x"].Clear();
        Equal(1, arm.Keyframes["rotation.x"].Count, "Original animation remains independent");
        Check(old.Children.Contains(arm), "Preparation does not mutate live hierarchy");
    }

    private static void LibraryReplacementRetention(Fixture fixture)
    {
        var old = TreeObject("old");
        var missing = TreeObject("unmatched");
        var hand = TreeObject("hand");
        var attachment = TreeObject("sword");
        old.AddChild(missing);
        missing.AddChild(hand);
        hand.AddChild(attachment);
        var target = TreeObject("new");
        var newHand = TreeObject("hand");
        target.AddChild(newHand);
        var accessory = TreeObject("new accessory");
        accessory.CopyConstraints.Add(new CopyConstraint { TargetObjectId = newHand.ObjectId });
        target.AddChild(accessory);
        var replacement = LibraryAssetReplacement.Prepare(old, target);
        Equal(2, replacement.RetainedPartCount, "Missing part and attachment counted");
        replacement.RetainUnmatchedParts();
        Check(target.Children.Contains(missing) && missing.Children.Count == 0, "Unmatched geometry retained without duplicate hand");
        Check(newHand.Children.Single() == attachment, "Attachment follows its matched part");
        Equal(hand.ObjectId, accessory.CopyConstraints.Single().TargetObjectId, "New rig references remapped to preserved IDs");

        var duplicateSource = TreeObject("source");
        var ambiguous = TreeObject("finger");
        duplicateSource.AddChild(ambiguous);
        var duplicateTarget = TreeObject("target");
        duplicateTarget.AddChild(TreeObject("finger"));
        duplicateTarget.AddChild(TreeObject("finger"));
        var duplicate = LibraryAssetReplacement.Prepare(duplicateSource, duplicateTarget);
        Equal(1, duplicate.Matches.Count, "Ambiguous names must not arbitrarily steal animation");
        Equal(1, duplicate.RetainedPartCount, "Ambiguous part retained");

        var parent = TreeObject("parent");
        var before = TreeObject("before");
        var after = TreeObject("after");
        parent.AddChild(before);
        parent.AddChild(duplicateSource);
        parent.AddChild(after);
        var scene = new List<SceneObject> { parent };
        duplicate.Commit(scene);
        Check(parent.Children.SequenceEqual([before, duplicateTarget, after]), "Parent and sibling slot preserved");
        Check(duplicateTarget.Children.Contains(ambiguous), "Unmatched animated node remains alive");
    }

    private static void LibraryReplacementHistory(Fixture fixture)
    {
        var manager = ProjectManager.Instance;
        var originalLibrary = manager.Manifest.ObjectLibrary;
        var viewport = new Viewport();
        var old = TreeObject("old");
        old.LibrarySourceId = "asset";
        viewport.SceneObjects.Add(old);
        SelectionManager.Instance.ClearSelection();
        SelectionManager.Instance.SelectObject(old);
        try
        {
            manager.Manifest.ObjectLibrary = [new ProjectSceneObjectEntry { LibraryEntryId = "asset", SourceAssetPath = "old.mimodel" }];
            var before = new SceneHistoryAction.HierarchyState(viewport);
            var beforeProject = SceneHistoryAction.ProjectState.Capture(manager.Manifest);
            var replacement = LibraryAssetReplacement.Prepare(old, TreeObject("new"));
            replacement.Commit(viewport.SceneObjects);
            manager.Manifest.ObjectLibrary = [new ProjectSceneObjectEntry { LibraryEntryId = "asset", SourceAssetPath = "new.mimodel" }];
            SelectionManager.Instance.ClearSelection();
            SelectionManager.Instance.SelectObject(replacement.Replacement);
            var after = SceneHistoryAction.HierarchyState.Capture(viewport, before);
            var afterProject = SceneHistoryAction.ProjectState.Capture(manager.Manifest);
            var action = SceneHistoryAction.Create([], before, after, beforeProject, afterProject)!;
            for (int i = 0; i < 2; i++)
            {
                Check(action.Apply(viewport, null, true), "Replacement undo");
                Equal(old, viewport.SceneObjects.Single(), "Undo original live instance");
                Equal("old.mimodel", manager.Manifest.ObjectLibrary.Single().SourceAssetPath, "Undo library");
                Check(action.Apply(viewport, null, false), "Replacement redo");
                Equal(replacement.Replacement, viewport.SceneObjects.Single(), "Redo replacement instance");
                Equal("new.mimodel", manager.Manifest.ObjectLibrary.Single().SourceAssetPath, "Redo library");
            }
            var libraryOnly = SceneHistoryAction.Create([], after, after, beforeProject, afterProject)!;
            Check(libraryOnly.Apply(viewport, null, true), "Unused library asset undo");
            Equal("old.mimodel", manager.Manifest.ObjectLibrary.Single().SourceAssetPath, "Unused library restored");
        }
        finally
        {
            manager.Manifest.ObjectLibrary = originalLibrary;
            SelectionManager.Instance.ClearSelection();
        }
    }

    private static unsafe void LibraryReplacementGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Library replacement test", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            MineImatorLoader.Instance.Initialize(gl);
            using var fixture = new Fixture();
            var viewport = new Viewport();
            var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };
            var timeline = new Timeline { Viewport = viewport };
            var panel = new PropertiesPanel { Viewport = viewport, SpawnMenu = spawn, Timeline = timeline };
            var manager = ProjectManager.Instance;
            string data = Directory.Exists("data") ? "data" : Path.Combine("MineImatorSimplyRemade", "data");
            string steve = Path.GetFullPath(Path.Combine(data, "minecraft", "versions", "26.3",
                "characters", "vanilla", "steve", "steve.mimodel"));
            string custom = fixture.File("custom.mimodel", """
                {"name":"Custom","texture":"none","texture_size":[64,64],"parts":[
                  {"name":"torso","position":[0,18,0],"shapes":[{"type":"block","from":[-6,0,-3],"to":[6,14,3]}],
                   "parts":[{"name":"Arm_L","position":[8,12,0],"bend":{"offset":-4,"part":"lower","axis":"x"},
                     "shapes":[{"type":"block","from":[-3,-12,-3],"to":[3,2,3]}]}]},
                  {"name":"antenna","position":[0,34,0],"shapes":[{"type":"block","from":[-1,0,-1],"to":[1,6,1]}]}]}
                """);
            var first = spawn.SpawnCustomModelFromPath(steve, registerAsset: false)!;
            foreach (var node in LibraryAssetReplacement.Nodes(first))
                node.LibrarySourceId = node.ObjectId;
            var library = ProjectSceneSerializer.SerializeObjectForLibrary(first);
            SetTestLibraryIds(library);
            manager.Manifest.ObjectLibrary = [library];
            var second = ProjectSceneSerializer.SpawnObjectFromEntry(library, viewport, spawn)!;
            var holder = TreeObject("holder");
            viewport.SceneObjects.Add(holder);
            var armEntry = library.Children.SelectMany(EntryNodes).Single(entry => entry.BoneName == "left_arm");
            var standalone = ProjectSceneSerializer.SpawnObjectFromEntry(armEntry, viewport, spawn, holder)!;
            standalone.Keyframes["rotation.x"] = [new ObjectKeyframe { Frame = 0, Value = 45f }];
            var standaloneId = standalone.ObjectId;
            var firstArm = LibraryAssetReplacement.Nodes(first).OfType<MiBoneSceneObject>().Single(b => b.BoneName == "left_arm");
            var secondArm = LibraryAssetReplacement.Nodes(second).OfType<MiBoneSceneObject>().Single(b => b.BoneName == "left_arm");
            firstArm.Keyframes["rotation.x"] = [new ObjectKeyframe { Frame = 0, Value = 10f }, new ObjectKeyframe { Frame = 20, Value = 90f }];
            secondArm.Keyframes["rotation.x"] = [new ObjectKeyframe { Frame = 0, Value = -20f }, new ObjectKeyframe { Frame = 20, Value = -80f }];
            first.SetLocalPosition(new vec3(3, 4, 5));
            second.SetLocalPosition(new vec3(-3, 0, 0));
            var untouched = new SceneObject { Name = "untouched" };
            viewport.SceneObjects.Insert(1, untouched);
            var selected = SelectionManager.Instance;
            selected.ClearSelection();
            selected.SelectObject(firstArm);
            var before = new SceneHistoryAction.HierarchyState(viewport);
            var beforeProject = SceneHistoryAction.ProjectState.Capture(manager.Manifest);
            var result = panel.ReplaceLibraryAsset(manager, library, custom);
            Equal(3, result.Instances, "All linked instances and separately placed library parts replaced");
            Check(result.Matched >= 2 && result.Retained > 0, "Matched alias and retained missing parts reported");
            Equal(4, viewport.SceneObjects.Count, "No temporary roots leaked");
            Equal(untouched, viewport.SceneObjects[1], "Unrelated root and order preserved");
            var newFirst = viewport.SceneObjects[0];
            var newSecond = viewport.SceneObjects[2];
            var arm1 = LibraryAssetReplacement.Nodes(newFirst).OfType<MiBoneSceneObject>().Single(b => b.BoneName == "Arm_L");
            var arm2 = LibraryAssetReplacement.Nodes(newSecond).OfType<MiBoneSceneObject>().Single(b => b.BoneName == "Arm_L");
            Equal(firstArm.ObjectId, arm1.ObjectId, "Mapped scene ID stable");
            Equal(1, arm1.Visuals.Count, "Custom mesh replaces Steve's arm and overlay geometry");
            Near(6f / 16f, arm1.Visuals.Single().Vertices.Max(vertex => vertex.x) -
                arm1.Visuals.Single().Vertices.Min(vertex => vertex.x), "New asset's exact arm width");
            var standaloneArm = (MiBoneSceneObject)holder.Children.Single();
            Equal("Arm_L", standaloneArm.BoneName, "Separately placed part gets the new rig part");
            Equal(standaloneId, standaloneArm.ObjectId, "Separately placed part preserves identity and parent");
            Equal(arm1, selected.SelectedObjects.Single(), "Selection follows mapped part");
            Check(LibraryAssetReplacement.Nodes(newFirst).Any(node => node.Name == "antenna"), "New parts appear");
            Check(LibraryAssetReplacement.Nodes(newFirst).Any(node => node.Name == "right_arm"), "Missing old part kept");
            Seek(20);
            Near(MathF.PI / 2f, arm1.OffsetRotation.x, "First instance animation seeks");
            Near(-80f * MathF.PI / 180f, arm2.OffsetRotation.x, "Second instance independent animation seeks");
            Seek(0);
            Near(10f * MathF.PI / 180f, arm1.OffsetRotation.x, "Backward seeking");
            Near(-20f * MathF.PI / 180f, arm2.OffsetRotation.x, "Backward seeking second instance");
            Near(MathF.PI / 4f, standaloneArm.OffsetRotation.x, "Separately placed part preserves animation");
            var after = SceneHistoryAction.HierarchyState.Capture(viewport, before);
            var action = SceneHistoryAction.Create([], before, after, beforeProject,
                SceneHistoryAction.ProjectState.Capture(manager.Manifest))!;
            Check(action.Apply(viewport, panel, true), "Real-model undo");
            Equal(first, viewport.SceneObjects[0], "Undo restores original model");
            Check(action.Apply(viewport, panel, false), "Real-model redo");
            Equal(newFirst, viewport.SceneObjects[0], "Redo restores new geometry");
            Equal(standaloneArm, holder.Children.Single(), "Redo restores parented replacement");

            var saved = ProjectSceneSerializer.SerializeObjectForLibrary(newFirst);
            var roundTrip = JsonSerializer.Deserialize(JsonSerializer.Serialize(saved,
                AppJsonContext.Default.ProjectSceneObjectEntry), AppJsonContext.Default.ProjectSceneObjectEntry)!;
            var loaded = ProjectSceneSerializer.SpawnObjectFromEntry(roundTrip, viewport, spawn)!;
            var loadedArm = LibraryAssetReplacement.Nodes(loaded).OfType<MiBoneSceneObject>().Single(b => b.BoneName == "Arm_L");
            Equal(2, loadedArm.Keyframes["rotation.x"].Count, "Saved animation restored");
            Equal(LibraryAssetReplacement.Nodes(newFirst).Count(), LibraryAssetReplacement.Nodes(loaded).Count(), "Mixed-source hierarchy restored exactly");
            Near(arm1.OffsetRotation, loadedArm.OffsetRotation, "Saved pose retains custom rest pose");
            viewport.SceneObjects.Remove(loaded);
            var archive = fixture.Roundtrip(newFirst);
            Equal(LibraryAssetReplacement.Nodes(newFirst).Count(), EntryNodes(archive.Manifest.Root).Count(),
                "Object export preserves mixed-source hierarchy");
            Check(EntryNodes(archive.Manifest.Root).Select(entry => entry.SourceAssetPath)
                .Where(source => !string.IsNullOrEmpty(source)).Distinct().Count() >= 2,
                "Object archive includes both replacement and retained source assets");
            var rootsBeforeFailure = viewport.SceneObjects.ToArray();
            var libraryBeforeFailure = manager.Manifest.ObjectLibrary;
            try
            {
                panel.ReplaceLibraryAsset(manager, manager.Manifest.ObjectLibrary.Single(), fixture.NewPath(".mimodel"));
                throw new Exception("Missing model should fail");
            }
            catch (InvalidDataException)
            {
                Check(rootsBeforeFailure.SequenceEqual(viewport.SceneObjects), "Failure preserves scene and ordering");
                Check(ReferenceEquals(libraryBeforeFailure, manager.Manifest.ObjectLibrary), "Failure preserves library");
            }
            viewport.SceneObjects.Clear();
            var unused = panel.ReplaceLibraryAsset(manager, manager.Manifest.ObjectLibrary.Single(), custom);
            Equal(0, unused.Instances, "Unused library assets can be replaced without spawning an instance");
            Equal(0, viewport.SceneObjects.Count, "Unused asset replacement leaves the scene empty");
            Equal(GLEnum.NoError, gl.GetError(), "OpenGL error");

            static IEnumerable<ProjectSceneObjectEntry> EntryNodes(ProjectSceneObjectEntry entry)
            {
                yield return entry;
                foreach (var child in entry.Children)
                    foreach (var descendant in EntryNodes(child))
                        yield return descendant;
            }

            void Seek(int frame)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(Timeline).GetField("_currentFrame", flags)!.SetValue(timeline, frame);
                typeof(Timeline).GetMethod("ApplyKeyframesAtCurrentFrame", flags)!.Invoke(timeline, null);
            }

            static void SetTestLibraryIds(ProjectSceneObjectEntry entry)
            {
                entry.LibraryEntryId = entry.LibrarySourceId;
                foreach (var child in entry.Children)
                    SetTestLibraryIds(child);
            }
        }
        finally
        {
            SelectionManager.Instance.ClearSelection();
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}
