using System.Reflection;
using Hexa.NET.ImGui;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.history;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private static unsafe void ModifierContextInteraction(Fixture f)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new System.Numerics.Vector2(1000, 900);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var obj = new SceneObject { Name = "Context array" };
            obj.AssignObjectId();
            obj.Modifiers = [new() { Distance = 2 }];
            string property = $"modifier.{obj.Modifiers[0].Id}.distance";
            var viewport = new Viewport();
            viewport.SceneObjects.Add(obj);
            var timeline = new Timeline { Viewport = viewport };
            var panel = new PropertiesPanel { Viewport = viewport, Timeline = timeline };
            var fields = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(PropertiesPanel).GetField("_currentObject", fields)!.SetValue(panel, obj);
            var ctxPath = typeof(PropertiesPanel).GetField("_ctxPropertyPath", fields)!;
            var open = typeof(PropertiesPanel).GetField("_openPropContextMenu", fields)!;
            var draw = typeof(PropertiesPanel).GetMethod("DrawModifiers", fields)!;
            var menu = typeof(PropertiesPanel).GetMethod("RenderPropertyContextMenu", fields)!;
            uint hovered = 0, actionId = 0;
            System.Numerics.Vector2 popupPos = default, popupSize = default;
            string action = "";
            bool showMenu = false;
            void Frame(float x = -100, float y = -100, bool left = false, bool right = false)
            {
                io.AddMousePosEvent(x, y);
                io.AddMouseButtonEvent(0, left);
                io.AddMouseButtonEvent(1, right);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(650, 750), ImGuiCond.Always);
                ImGui.Begin("Modifier interaction");
                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                draw.Invoke(panel, [obj]);
                if ((bool)open.GetValue(panel)!)
                {
                    open.SetValue(panel, false);
                    if (showMenu)
                    {
                        typeof(PropertiesPanel).GetField("_propertyContextObject", fields)!.SetValue(panel, obj);
                        ImGui.OpenPopup("##prop_keyframe_ctx");
                    }
                }
                if (showMenu)
                {
                    menu.Invoke(panel, null);
                    if (ImGui.BeginPopup("##prop_keyframe_ctx"))
                    {
                        popupPos = ImGui.GetWindowPos();
                        popupSize = ImGui.GetWindowSize();
                        actionId = ImGui.GetID(action);
                        ImGui.EndPopup();
                    }
                }
                hovered = context.HoveredId;
                ImGui.End();
                ImGui.Render();
            }
            Frame();
            Frame();
            float distanceY = -1;
            for (int y = 60; y < 650; y += 3)
            {
                ctxPath.SetValue(panel, null);
                Frame(120, y, right: true);
                if ((string?)ctxPath.GetValue(panel) == property) distanceY = y;
                Frame(120, y);
                if (distanceY >= 0) break;
            }
            Check(distanceY >= 0, "Right-clicking the rendered distance widget registers its modifier property");
            showMenu = true;
            void ClickAction(string label)
            {
                action = label;
                Frame(120, distanceY, right: true);
                Frame(120, distanceY);
                Frame();
                float actionY = -1;
                for (int y = (int)popupPos.Y + 4; y < popupPos.Y + popupSize.Y - 4; y++)
                {
                    Frame(popupPos.X + 20, y);
                    if (hovered == actionId) { actionY = y; break; }
                }
                Check(actionY >= 0, "Rendered context menu action is available: " + label);
                Frame(popupPos.X + 20, actionY, left: true);
                Frame(popupPos.X + 20, actionY);
                Frame();
            }
            ClickAction("Copy");
            Equal("32", System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)ImGui.GetClipboardText()),
                "Copy gets displayed pixel distance");
            ImGui.SetClipboardText("48");
            ClickAction("Paste");
            Near(3, obj.Modifiers[0].Distance, "Real Paste menu converts pixels");
            ClickAction("Reset Value");
            Near(1, obj.Modifiers[0].Distance, "Real Reset menu uses modifier defaults");
            ClickAction(Localization.Label("text.panel.properties.add_keyframe_at_current_frame", "Add Keyframe at Current Frame"));
            Equal(1f, obj.Keyframes[property][0].Value, "Real Add Keyframe menu keys modifier distance");
            obj.Modifiers[0] = obj.Modifiers[0] with { Distance = 4 };
            ClickAction("Cut");
            Near(1, obj.Modifiers[0].Distance, "Real Cut menu resets modifier distance");
            Equal("64", System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)ImGui.GetClipboardText()), "Cut copies pixels");
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private static void ModifierKeyframes(Fixture f)
    {
        var path = StraightPath();
        var obj = new SceneObject { Name = "Animated array", PivotOffset = vec3.Zero };
        obj.AssignObjectId();
        var array = new SceneModifier { Count = 3 };
        var other = new SceneModifier { Count = 2, Axis = ArrayAxis.Y };
        obj.Modifiers = [array, other, new() { Type = SceneModifierType.FollowPath }];
        string prefix = $"modifier.{array.Id}";
        var viewport = new Viewport();
        viewport.SceneObjects.Add(obj);
        viewport.SceneObjects.Add(path);
        var timeline = new Timeline { Viewport = viewport };
        var panel = new PropertiesPanel { Timeline = timeline, Viewport = viewport };
        typeof(PropertiesPanel).GetField("_currentObject", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, obj);
        var get = typeof(Timeline).GetMethod("GetPropertyValue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var set = typeof(Timeline).GetMethod("SetPropertyValue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var contextGet = typeof(PropertiesPanel).GetMethod("GetContextPropertyValue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var contextSet = typeof(PropertiesPanel).GetMethod("ApplyContextPropertyValues", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var paths = typeof(Timeline).GetMethod("GetKeyframePropertyPaths", BindingFlags.Static | BindingFlags.NonPublic)!;
        void Seek(Timeline target, int frame, bool render = false)
        {
            typeof(Timeline).GetField("_currentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, frame);
            typeof(Timeline).GetMethod("ApplyKeyframesAtCurrentFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(target, [render]);
        }
        typeof(Timeline).GetField("_autoKeyframe", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(timeline, true);
        SelectionManager.Instance!.ClearSelection();
        SelectionManager.Instance.SelectObject(obj);
        SelectionManager.Instance.SelectObject(path);

        Equal(16f, contextGet.Invoke(panel, [$"{prefix}.distance", false]), "Context distance uses pixels");
        Equal(2f, contextGet.Invoke(panel, [$"{prefix}.count", true]), "Count default matches inspector");
        Equal(1f, contextGet.Invoke(panel, [$"{prefix}.scale.z", true]), "Scale default is one");
        Equal("", contextGet.Invoke(panel, [$"{prefix}.target", true]), "Target default is none");
        void Edit(string channel, object value) => contextSet.Invoke(panel, [new[] { $"{prefix}.{channel}" }, new[] { value }, false]);
        Edit("distance", 32f);
        Near(2, obj.Modifiers[0].Distance, "Context distance converts back to world units");
        Equal(2f, obj.Keyframes[$"{prefix}.distance"][0].Value, "Auto key stores world units");
        Equal(0, path.Keyframes.Count, "Modifier edits do not affect another selected object");
        Edit("target", path.ObjectId);
        Equal(path.ObjectId, obj.Keyframes[$"{prefix}.target"][0].Value, "Path target auto key");
        string[] rotations = ["x", "y", "z"];
        contextSet.Invoke(panel, [rotations.Select(axis => $"{prefix}.rotation.{axis}").ToArray(), new object[] { 10f, 20f, 30f }, false]);
        foreach (string axis in rotations)
            Check(obj.Keyframes.ContainsKey($"{prefix}.rotation.{axis}"), "Vector context action keys all components");
        var before = obj.Modifiers[0];
        Edit("axis", 7f);
        Equal(before, obj.Modifiers[0], "Invalid context axis edit is rejected atomically");
        Check(typeof(PropertiesPanel).GetField("_modifierError", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel) is string,
            "Invalid modifier edit surfaces an error");
        Check(!obj.Keyframes.ContainsKey($"{prefix}.axis"), "Rejected edit creates no key");

        foreach (string channel in new[] { "enabled", "count", "axis", "distance", "rotation.x", "rotation.y", "rotation.z",
                     "scale.x", "scale.y", "scale.z", "target" })
        {
            Check(((IEnumerable<string>)paths.Invoke(null, [obj])!).Contains($"{prefix}.{channel}"), "Channel enumerated: " + channel);
            _ = get.Invoke(timeline, [obj, $"{prefix}.{channel}"]);
        }
        string followEnabled = $"modifier.{obj.FollowPathModifier!.Id}.enabled";
        timeline.AddKeyframeForProperty(obj, followEnabled, 0);
        timeline.AddKeyframeForProperty(obj, $"{prefix}.count", 0);
        timeline.AddKeyframeForProperty(obj, $"{prefix}.axis", 0);
        timeline.AddKeyframeForProperty(obj, $"{prefix}.enabled", 0);
        timeline.AddKeyframeForProperty(obj, $"{prefix}.scale.x", 0);
        set.Invoke(timeline, [obj, $"{prefix}.distance", 4f]);
        set.Invoke(timeline, [obj, $"{prefix}.count", 5f]);
        set.Invoke(timeline, [obj, $"{prefix}.axis", 2f]);
        set.Invoke(timeline, [obj, $"{prefix}.enabled", 0f]);
        set.Invoke(timeline, [obj, $"{prefix}.scale.x", 0.5f]);
        set.Invoke(timeline, [obj, $"{prefix}.target", ""]);
        set.Invoke(timeline, [obj, followEnabled, 0f]);
        foreach (string channel in new[] { "distance", "count", "axis", "enabled", "scale.x", "target" })
            timeline.AddKeyframeForProperty(obj, $"{prefix}.{channel}", 10);
        timeline.AddKeyframeForProperty(obj, followEnabled, 10);
        Seek(timeline, 5);
        Near(3, obj.Modifiers[0].Distance, "Distance interpolates");
        Near(0.75f, obj.Modifiers[0].ScaleX, "Scale interpolates");
        Equal(3, obj.Modifiers[0].Count, "Count is stepped");
        Equal(ArrayAxis.X, obj.Modifiers[0].Axis, "Axis is stepped");
        Check(obj.Modifiers[0].Enabled && obj.FollowPathModifier!.Enabled, "Enabled flags are stepped");
        Equal(path.ObjectId, obj.Modifiers[0].PathObjectId, "Path reference holds until next key");
        Seek(timeline, 10);
        Equal(5, obj.Modifiers[0].Count, "Exact key uses new count");
        Check(!obj.Modifiers[0].Enabled && !obj.FollowPathModifier!.Enabled, "Exact enabled keys applied");
        obj.Modifiers.Reverse();
        timeline.RefreshFromScene();
        Seek(timeline, 5);
        Near(3, obj.Modifiers.Single(modifier => modifier.Id == array.Id).Distance, "Reordering preserves track ownership");
        Equal(other, obj.Modifiers.Single(modifier => modifier.Id == other.Id), "Unkeyed modifier is unchanged");
        Seek(timeline, 0, render: true);
        Near(2, obj.Modifiers.Single(modifier => modifier.Id == array.Id).Distance, "Backward render seek is reproducible");

        var root = new SceneObject { Name = "Animated modifier root" };
        viewport.SceneObjects.Clear();
        root.AddChild(obj);
        root.AddChild(path);
        viewport.SceneObjects.Add(root);
        var (manifest, _) = f.Roundtrip(root);
        var loadedViewport = new Viewport();
        var imported = ProjectSceneSerializer.SpawnObjectFromEntry(manifest.Root, loadedViewport,
            new SpawnMenu { Viewport = loadedViewport })!;
        var restored = imported.Children[0];
        Equal(array.Id, restored.Modifiers.Single(modifier => modifier.Type == SceneModifierType.Array && modifier.Id == array.Id).Id,
            "Persistent modifier identity");
        var restoredTarget = restored.Keyframes[$"{prefix}.target"][0].Value;
        Equal(imported.Children[1].ObjectId, restoredTarget, "Modifier target key remapped on archive import");
        var restoredTimeline = new Timeline { Viewport = loadedViewport };
        restoredTimeline.LoadKeyframesForAllObjects([restored]);
        Seek(restoredTimeline, 5);
        Near(3, restored.Modifiers.Single(modifier => modifier.Id == array.Id).Distance, "Loaded modifier track evaluates");
        var duplicate = (SceneObject)typeof(SceneTree).GetMethod("DuplicateObject", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new SceneTree { Viewport = loadedViewport }, [imported, false, null])!;
        Equal(duplicate.Children[1].ObjectId, duplicate.Children[0].Keyframes[$"{prefix}.target"][0].Value,
            "Duplicate remaps animated array target");
        var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(restored);
        restored.Modifiers.Clear();
        typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [restored, snapshot, null]);
        restoredTimeline.RefreshFromScene();
        Seek(restoredTimeline, 10);
        Equal(5, restored.Modifiers.Single(modifier => modifier.Id == array.Id).Count, "Undo snapshot preserves modifier track identity");

        obj.Modifiers.RemoveAll(modifier => modifier.Id == array.Id);
        Seek(timeline, 10);
        Check(obj.Modifiers.All(modifier => modifier.Id != array.Id), "Cached track cannot recreate removed modifier");
        obj.Modifiers.Add(new SceneModifier());
        Seek(timeline, 10);
        Equal(2, obj.Modifiers[^1].Count, "New modifier does not inherit deleted modifier tracks");
        SelectionManager.Instance.ClearSelection();
    }

    private static vec3 InstanceOrigin(ModifierEvaluation evaluation, SceneObject obj, int index) =>
        (evaluation.GetDeltas(obj)[index] * obj.GetWorldMatrixNoPivot() * new vec4(0, 0, 0, 1)).xyz;

    private static void ModifierTransforms(Fixture f)
    {
        var obj = new SceneObject { Name = "Array", Position = new(3, 4, 5), PivotOffset = new(0, 0.5f, 0) };
        var child = new SceneObject { Name = "Child", Position = vec3.UnitY };
        obj.AddChild(child);
        var original = obj.GetWorldMatrix();
        foreach (var axis in Enum.GetValues<ArrayAxis>())
        {
            obj.Modifiers = [new() { Count = 4, Axis = axis, Distance = 2f }];
            var evaluation = new ModifierEvaluation([obj]);
            Equal(4, evaluation.GetDeltas(obj).Count, "Count includes original");
            vec3 direction = axis switch { ArrayAxis.X => vec3.UnitX, ArrayAxis.Y => vec3.UnitY, _ => vec3.UnitZ };
            for (int i = 0; i < 4; i++)
            {
                Near(obj.Position + direction * (2f * i), InstanceOrigin(evaluation, obj, i), "Axis spacing");
                Near(obj.Position + vec3.UnitY + direction * (2f * i), InstanceOrigin(evaluation, child, i), "Child copies");
            }
            Equal(original, obj.GetWorldMatrix(), "Non-destructive source");
            Equal(1, obj.Children.Count, "No generated tree objects");
            Equal(0, child.Modifiers.Count, "No child settings mutation");
        }
        obj.Rotation = new(0, 0, MathF.PI / 2f);
        obj.Scale = new(2, 3, 4);
        obj.Modifiers = [new() { Count = 3, Distance = 2, RotationZ = 90, ScaleX = 0.5f, ScaleY = 0.5f, ScaleZ = 0.5f }];
        var rotated = new ModifierEvaluation([obj]);
        Near(obj.Position + vec3.UnitY * 4, InstanceOrigin(rotated, obj, 2), "Local direction independent of source scale");
        mat4 last = rotated.GetDeltas(obj)[2] * obj.GetWorldMatrixNoPivot();
        Near(0.5f, new vec3(last.m00, last.m01, last.m02).Length, "Compounded scale");
        vec3 lastDirection = (last * new vec4(1, 0, 0, 0)).xyz.Normalized;
        Near(-vec3.UnitY, lastDirection, "Incremental rotation in degrees");
        obj.Modifiers[0] = obj.Modifiers[0] with { Enabled = false };
        Equal(1, new ModifierEvaluation([obj]).GetDeltas(child).Count, "Disabled array includes only source");
        obj.Scale = vec3.Zero;
        obj.Modifiers[0] = new() { Count = 2 };
        Check(float.IsFinite(InstanceOrigin(new ModifierEvaluation([obj]), obj, 1).LengthSqr), "Zero-scale source remains finite");
    }

    private static void ModifierPaths(Fixture f)
    {
        var path = StraightPath();
        path.Position = new(8, 2, 3);
        var obj = new SceneObject { Position = new(90, 20, 30), PivotOffset = vec3.Zero };
        var child = new SceneObject { Position = vec3.UnitZ };
        obj.AddChild(child);
        foreach (var axis in Enum.GetValues<ArrayAxis>())
        {
            obj.Modifiers = [new() { Axis = axis, Count = 4, Distance = 2, PathObjectId = path.ObjectId }];
            var evaluation = new ModifierEvaluation([obj, path]);
            for (int i = 0; i < 4; i++)
                Near(path.Position + vec3.UnitY * (2 * i), InstanceOrigin(evaluation, obj, i), "Copies start at path origin");
            vec3 direction = axis switch { ArrayAxis.X => vec3.UnitX, ArrayAxis.Y => vec3.UnitY, _ => vec3.UnitZ };
            mat4 model = evaluation.GetDeltas(obj)[2] * obj.GetWorldMatrixNoPivot();
            Near(vec3.UnitY, (model * new vec4(direction, 0)).xyz.Normalized, "Selected axis aligns to tangent");
            Equal(4, evaluation.GetDeltas(child).Count, "Path repeats subtree");
        }
        obj.Modifiers = [new() { Axis = ArrayAxis.Y, Count = 4, Distance = 7, PathObjectId = path.ObjectId }];
        Near(path.Position + vec3.UnitY * 10, InstanceOrigin(new ModifierEvaluation([obj, path]), obj, 3), "Open path clamps");
        path.Settings = path.Settings with { Closed = true };
        var closed = new ModifierEvaluation([obj, path]);
        Near((path.GetWorldMatrixNoPivot() * new vec4(PathGeometry.AtDistance(path.Samples, 21, true).Position, 1)).xyz,
            InstanceOrigin(closed, obj, 3), "Closed path wraps");
        obj.Modifiers[0] = obj.Modifiers[0] with { Distance = -3 };
        Near((path.GetWorldMatrixNoPivot() * new vec4(PathGeometry.AtDistance(path.Samples, -3, true).Position, 1)).xyz,
            InstanceOrigin(new ModifierEvaluation([obj, path]), obj, 1), "Negative spacing wraps");
        path.Settings = path.Settings with { Closed = false };
        obj.Modifiers[0] = obj.Modifiers[0] with { Count = 2, Distance = 2, ScaleX = 0.5f, ScaleY = 0.5f, ScaleZ = 0.5f };
        foreach (PathPointSceneObject point in path.Children) point.PathPointScale = 2;
        var scaled = new ModifierEvaluation([obj, path]);
        mat4 second = scaled.GetDeltas(obj)[1] * obj.GetWorldMatrixNoPivot();
        Near(1f, new vec3(second.m00, second.m01, second.m02).Length, "Path-point and per-copy scale combine");
        path.Children[1].Position += vec3.UnitX;
        var edited = new ModifierEvaluation([obj, path]);
        Check((InstanceOrigin(edited, obj, 1) - InstanceOrigin(scaled, obj, 1)).Length > 0.1f, "Path edits reevaluate copies");
        path.Children[1].Position -= vec3.UnitX;
        Near(InstanceOrigin(scaled, obj, 1), InstanceOrigin(new ModifierEvaluation([obj, path]), obj, 1), "Path seek is deterministic");
        obj.Modifiers[0] = obj.Modifiers[0] with { PathObjectId = "missing" };
        Equal(1, new ModifierEvaluation([obj, path]).GetDeltas(obj).Count, "Deleted path inactive");
        obj.Modifiers[0] = obj.Modifiers[0] with { PathObjectId = path.ObjectId };
        obj.AddChild(path);
        Equal(1, new ModifierEvaluation([obj]).GetDeltas(obj).Count, "Descendant path inactive");
        obj.RemoveChild(path);
        foreach (var point in path.Children) point.Position = vec3.Zero;
        Equal(1, new ModifierEvaluation([obj, path]).GetDeltas(obj).Count, "Degenerate path inactive");
    }

    private static void ModifierStacks(Fixture f)
    {
        var obj = new SceneObject { PivotOffset = vec3.Zero };
        obj.Modifiers = [new() { Count = 3, Distance = 2 }, new() { Count = 2, Axis = ArrayAxis.Y, Distance = 3 }];
        var child = new SceneObject { Position = vec3.UnitZ, PivotOffset = vec3.Zero };
        obj.AddChild(child);
        var evaluation = new ModifierEvaluation([obj]);
        Equal(6, evaluation.GetDeltas(obj).Count, "Stack multiplication");
        Near(new vec3(4, 3, 0), InstanceOrigin(evaluation, obj, 5), "Two-dimensional grid");
        Near(new vec3(4, 3, 1), InstanceOrigin(evaluation, child, 5), "Stack subtree placement");
        child.Modifiers = [new() { Count = 3, Axis = ArrayAxis.Z }];
        Equal(18, new ModifierEvaluation([obj]).GetDeltas(child).Count, "Child arrays compose with parent");
        obj.Modifiers = [new() { Count = 1000 }, new() { Count = 1000 }];
        Equal(1000, new ModifierEvaluation([obj]).GetDeltas(obj).Count, "Stack safety cap");
        Equal(1000, new ModifierEvaluation([obj]).GetDeltas(child).Count, "Inherited safety cap");
        foreach (var invalid in new SceneModifier[]
        {
            new() { Count = 0 }, new() { Count = 1001 }, new() { Axis = (ArrayAxis)9 },
            new() { Type = (SceneModifierType)9 }, new() { Distance = float.NaN },
            new() { RotationX = float.PositiveInfinity }, new() { ScaleY = float.NaN }
        })
        {
            bool rejected = false;
            try { invalid.Validated(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid settings rejected");
        }
        obj.Modifiers = [new() { Count = 1000, ScaleX = 2 }];
        bool overflow = false;
        try { new ModifierEvaluation([obj]).GetDeltas(obj); } catch (InvalidDataException) { overflow = true; }
        Check(overflow, "Transform overflow reported explicitly");
        obj.Modifiers = [new() { Count = 2, RotationZ = 90 }, new() { Count = 2, Distance = 2 }];
        Near(new vec3(1, 2, 0), InstanceOrigin(new ModifierEvaluation([obj]), obj, 3), "Earlier rotations affect later arrays");
        obj.Modifiers.Reverse();
        Near(new vec3(3, 0, 0), InstanceOrigin(new ModifierEvaluation([obj]), obj, 3), "Reordering changes evaluation order");
    }

    private static void ModifierCombination(Fixture f)
    {
        var root = new SceneObject { PivotOffset = vec3.Zero, Position = new(3, 2, 1) };
        var child = new SceneObject { PivotOffset = vec3.Zero, Position = vec3.UnitY, InheritSelect = true };
        var grandchild = new SceneObject { PivotOffset = vec3.Zero, Position = vec3.UnitZ };
        root.AddChild(child);
        child.AddChild(grandchild);
        root.Modifiers = [new() { Count = 3, Distance = 2 }];
        child.Modifiers = [new() { Count = 2, Axis = ArrayAxis.Y, Distance = 4 }];
        grandchild.Modifiers = [new() { Count = 2, Axis = ArrayAxis.Z }];
        child.InheritModifiers = false;
        var evaluation = new ModifierEvaluation([root]);
        Equal(2, evaluation.GetDeltas(child).Count, "Inheritance off retains child's own stack");
        Equal(4, evaluation.GetDeltas(grandchild).Count, "Descendants inherit only the remaining stack");
        Near(root.Position + vec3.UnitY * 5, InstanceOrigin(evaluation, child, 1),
            "Blocking modifiers does not block inherited transforms");
        var originalWorld = grandchild.GetWorldMatrix();
        var originalStack = child.Modifiers.ToArray();
        child.CombineChildren = true;
        Equal(2, new ModifierEvaluation([root]).GetDeltas(grandchild).Count, "Nested combined owner blocks outer modifiers");
        root.CombineChildren = true;
        evaluation = new ModifierEvaluation([root]);
        Equal(3, evaluation.GetDeltas(grandchild).Count, "Combined geometry uses one stack despite source inheritance flags");
        Check(ReferenceEquals(evaluation.GetDeltas(root), evaluation.GetDeltas(grandchild)), "Parts share owner evaluation");
        Near(root.Position + new vec3(4, 1, 1), InstanceOrigin(evaluation, grandchild, 2), "Combined relative geometry retained");
        Equal(originalWorld, grandchild.GetWorldMatrix(), "Combining does not change transforms");
        Equal(root, grandchild.GetViewportSelectionTarget(), "Combined viewport picking resolves outer owner");
        var selection = SelectionManager.Instance;
        selection.ClearSelection();
        selection.SelectObject(root);
        Check(selection.SelectedObjects.SequenceEqual([root]), "Combined selection never separately selects inherited parts");
        selection.ClearSelection();
        root.CombineChildren = false;
        Equal(child, grandchild.GetViewportSelectionTarget(), "Uncombining restores nested combined owner");
        child.CombineChildren = false;
        Equal(grandchild, grandchild.GetViewportSelectionTarget(), "Uncombining restores individual picking");
        Equal(4, new ModifierEvaluation([root]).GetDeltas(grandchild).Count, "Uncombining restores original stacks");
        Check(child.Modifiers.SequenceEqual(originalStack), "No source modifier settings changed");
        child.InheritModifiers = true;
        Equal(12, new ModifierEvaluation([root]).GetDeltas(grandchild).Count, "Re-enabling inheritance restores full stack");
    }

    private static void ModifierCombinationPersistence(Fixture f)
    {
        var root = new SceneObject { Name = "Combined", CombineChildren = true, InheritModifiers = false };
        var child = new SceneObject { Name = "Part", InheritModifiers = false };
        root.AssignObjectId();
        child.AssignObjectId();
        root.AddChild(child);
        root.Modifiers = [new() { Count = 3 }];
        child.Modifiers = [new() { Count = 2 }];
        child.Keyframes["position.y"] = [new() { Frame = 0, Value = 1f }, new() { Frame = 10, Value = 2f }];
        var (archive, _) = f.Roundtrip(root);
        var viewport = new Viewport();
        var spawn = new SpawnMenu { Viewport = viewport };
        var imported = ProjectSceneSerializer.SpawnObjectFromEntry(archive.Root, viewport, spawn)!;
        Check(imported.CombineChildren && !imported.InheritModifiers && !imported.Children[0].InheritModifiers,
            "Archive restores combination and independent inheritance flags");
        Equal(2, imported.Children[0].Modifiers[0].Count, "Suspended modifiers persist");
        Equal(2, imported.Children[0].Keyframes["position.y"].Count, "Source animation persists");
        var manifest = new ProjectManifest();
        ProjectSceneSerializer.WriteSceneToManifest(manifest, viewport);
        var saved = System.Text.Json.JsonSerializer.Deserialize(
            System.Text.Json.JsonSerializer.Serialize(manifest, MineImatorSimplyRemade.AppJsonContext.Default.ProjectManifest),
            MineImatorSimplyRemade.AppJsonContext.Default.ProjectManifest)!;
        var loaded = new Viewport();
        ProjectSceneSerializer.LoadSceneFromManifest(saved, loaded, new SpawnMenu { Viewport = loaded });
        Check(loaded.SceneObjects[0].CombineChildren && !loaded.SceneObjects[0].Children[0].InheritModifiers,
            "Project save/load restores combination");
        var cloneLibrary = typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive", BindingFlags.Static | BindingFlags.NonPublic)!;
        var library = (ProjectSceneObjectEntry)cloneLibrary.Invoke(null, [archive.Root])!;
        Check(library.CombineChildren && !library.InheritModifiers && !library.Children[0].InheritModifiers,
            "Library clones preserve both flags recursively");
        var duplicate = typeof(SceneTree).GetMethod("DuplicateObject", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var copied = (SceneObject)duplicate.Invoke(new SceneTree { Viewport = viewport }, [imported, false, null])!;
        Check(copied.CombineChildren && !copied.InheritModifiers && !copied.Children[0].InheritModifiers,
            "Duplication preserves both flags recursively");
        ulong initial = SceneObjectFingerprint.Compute(viewport.SceneObjects);
        var before = ProjectSceneSerializer.SerializeObjectForLibrary(imported);
        var hierarchy = new SceneHistoryAction.HierarchyState(viewport);
        var project = SceneHistoryAction.ProjectState.Capture(ProjectManager.Instance.Manifest);
        imported.CombineChildren = false;
        Check(SceneObjectFingerprint.Compute(viewport.SceneObjects) != initial, "Combination invalidates dirty fingerprint");
        imported.InheritModifiers = true;
        var after = ProjectSceneSerializer.SerializeObjectForLibrary(imported);
        var action = SceneHistoryAction.Create([(imported, before, after)], hierarchy, hierarchy, project, project)!;
        Check(action.Apply(viewport, null, undo: true), "Combination undo applies");
        Check(imported.CombineChildren && !imported.InheritModifiers, "Undo restores combination and inheritance");
        Equal(initial, SceneObjectFingerprint.Compute(viewport.SceneObjects), "Undo restores dirty fingerprint");
        Check(action.Apply(viewport, null, undo: false), "Combination redo applies");
        Check(!imported.CombineChildren && imported.InheritModifiers, "Redo restores independent parts");
        ulong uncombined = SceneObjectFingerprint.Compute(viewport.SceneObjects);
        imported.InheritModifiers = false;
        Check(SceneObjectFingerprint.Compute(viewport.SceneObjects) != uncombined, "Inheritance alone invalidates dirty fingerprint");
        var legacy = System.Text.Json.JsonSerializer.Deserialize("{}", MineImatorSimplyRemade.AppJsonContext.Default.ProjectSceneObjectEntry)!;
        Check(legacy.InheritModifiers && !legacy.CombineChildren, "Old projects keep inherited modifiers and independent geometry");
        SelectionManager.Instance.ClearSelection();
    }

    private static void ModifierFollowPath(Fixture f)
    {
        var path = StraightPath();
        path.Position = new(8, 2, 3);
        path.Rotation = new(0, 0, MathF.PI / 4f);
        path.Scale = new(2);
        var obj = new SceneObject { PivotOffset = vec3.Zero };
        var child = new SceneObject { Position = vec3.UnitZ, PivotOffset = vec3.Zero };
        obj.AddChild(child);
        obj.Modifiers =
        [
            new() { Type = SceneModifierType.FollowPath, PathObjectId = path.ObjectId, PathOffset = 3 },
            new() { Count = 4, Axis = ArrayAxis.Y, Distance = 2 }
        ];
        var roots = new List<SceneObject> { obj, path };
        InverseKinematicsSolver.Evaluate(roots);
        var evaluation = new ModifierEvaluation(roots);
        Equal(4, evaluation.GetDeltas(obj).Count, "Follow Path does not multiply copies");
        for (int i = 0; i < 4; i++)
        {
            var sample = PathGeometry.AtDistance(path.Samples, 3 + 2 * i, false);
            mat4 frame = path.GetWorldMatrixNoPivot() * sample.Frame;
            Near((frame * new vec4(0, 0, 0, 1)).xyz, InstanceOrigin(evaluation, obj, i),
                "Implicit array path starts at follow offset");
            Near((frame * new vec4(0, 0, 1, 1)).xyz, InstanceOrigin(evaluation, child, i),
                "Child inherits each offset copy");
        }
        obj.Modifiers[1] = obj.Modifiers[1] with { PathObjectId = path.ObjectId };
        Near(InstanceOrigin(evaluation, obj, 2), InstanceOrigin(new ModifierEvaluation(roots), obj, 2),
            "Explicit same path also uses follow offset");
        obj.Modifiers.Reverse();
        InverseKinematicsSolver.Evaluate(roots);
        Near(InstanceOrigin(evaluation, obj, 2), InstanceOrigin(new ModifierEvaluation(roots), obj, 2),
            "Follow Path remains a base-transform modifier after reordering");
        var timeline = new Timeline();
        var set = typeof(Timeline).GetMethod("SetPropertyValue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (bool closed in new[] { false, true })
        {
            path.Settings = path.Settings with { Closed = closed };
            foreach (float offset in new[] { -3f, 7f, 30f })
            {
                set.Invoke(timeline, [obj, "path_follow.offset", offset]);
                InverseKinematicsSolver.Evaluate(roots);
                var moved = new ModifierEvaluation(roots);
                for (int i = 0; i < 4; i++)
                    Near((path.GetWorldMatrixNoPivot() *
                        new vec4(PathGeometry.AtDistance(path.Samples, offset + 2 * i, closed).Position, 1)).xyz,
                        InstanceOrigin(moved, obj, i), "Raw offset plus spacing clamps or wraps per copy");
            }
        }
        path.Settings = path.Settings with { Closed = false };
        obj.PathFollowOffset = 3;
        InverseKinematicsSolver.Evaluate(roots);
        Near(InstanceOrigin(evaluation, obj, 2), InstanceOrigin(new ModifierEvaluation(roots), obj, 2),
            "Animated array start is reproducible after backward seek");
        var other = StraightPath();
        other.Position = new(-5, 0, 0);
        roots.Add(other);
        obj.Modifiers[0] = obj.Modifiers[0] with { PathObjectId = other.ObjectId };
        Near(other.Position + vec3.UnitY * 4, InstanceOrigin(new ModifierEvaluation(roots), obj, 2),
            "Different explicit path ignores follow offset");
        obj.Modifiers[0] = obj.Modifiers[0] with { PathObjectId = path.ObjectId };
        obj.Modifiers[1] = obj.Modifiers[1] with { Enabled = false };
        InverseKinematicsSolver.Evaluate(roots);
        Check(!obj.IsFollowingPath, "Disabled Follow Path clears base frame");
        Near((path.GetWorldMatrixNoPivot() * new vec4(0, 4, 0, 1)).xyz,
            InstanceOrigin(new ModifierEvaluation(roots), obj, 2), "Disabled follower leaves explicit array starting at zero");
        obj.Modifiers[0] = obj.Modifiers[0] with { PathObjectId = "" };
        Near(vec3.UnitY * 4, InstanceOrigin(new ModifierEvaluation(roots), obj, 2),
            "Disabled follower restores local array");
        obj.Modifiers[1] = obj.Modifiers[1] with { Enabled = true };
        obj.Modifiers[0] = obj.Modifiers[0] with { Enabled = false };
        InverseKinematicsSolver.Evaluate(roots);
        Equal(1, new ModifierEvaluation(roots).GetDeltas(obj).Count, "Disabled array retains single path follower");
        Near((path.GetWorldMatrixNoPivot() * new vec4(0, 3, 0, 1)).xyz, obj.GetWorldPosition(),
            "Follow Path remains active with array disabled");
        obj.Modifiers.RemoveAt(1);
        InverseKinematicsSolver.Evaluate(roots);
        set.Invoke(timeline, [obj, "path_follow.target", path.ObjectId]);
        set.Invoke(timeline, [obj, "path_follow.offset", 5f]);
        Check(obj.FollowPathModifier == null && !obj.IsFollowingPath, "Removed follower is not recreated by timeline evaluation");
    }

    private static void ModifierFollowPersistence(Fixture f)
    {
        var root = new SceneObject { Name = "Root" };
        var path = StraightPath();
        var obj = new SceneObject { Name = "Follower" };
        obj.Modifiers =
        [
            new() { Type = SceneModifierType.FollowPath, Enabled = false, PathObjectId = path.ObjectId, PathOffset = -3 },
            new() { Count = 4, Axis = ArrayAxis.Y, Distance = 2 }
        ];
        obj.Keyframes["path_follow.target"] = [new() { Frame = 0, Value = path.ObjectId }];
        obj.Keyframes["path_follow.offset"] = [new() { Frame = 0, Value = -3f }];
        root.AddChild(path);
        root.AddChild(obj);
        var (manifest, _) = f.Roundtrip(root);
        Equal(obj.Modifiers[0], manifest.Root.Children[1].Modifiers![0], "Follow modifier archive settings");
        var viewport = new Viewport();
        var spawn = new SpawnMenu { Viewport = viewport };
        var imported = ProjectSceneSerializer.SpawnObjectFromEntry(manifest.Root, viewport, spawn)!;
        var follower = imported.Children[1];
        Equal(imported.Children[0].ObjectId, follower.FollowPathModifier!.PathObjectId, "Follow modifier reference remaps");
        Check(!follower.FollowPathModifier.Enabled, "Disabled Follow Path remains disabled after load");
        Near(-3, follower.PathFollowOffset, "Follow modifier offset persists");
        Equal(imported.Children[0].ObjectId, follower.Keyframes["path_follow.target"][0].Value,
            "Follow target keyframe remaps alongside modifier");
        var duplicate = typeof(SceneTree).GetMethod("DuplicateObject", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var copied = (SceneObject)duplicate.Invoke(new SceneTree { Viewport = viewport }, [imported, false, null])!;
        Equal(copied.Children[0].ObjectId, copied.Children[1].FollowPathModifier!.PathObjectId, "Duplicate follower path remaps");
        copied.Children[1].PathFollowOffset = 8;
        Near(-3, follower.PathFollowOffset, "Duplicate follower settings independent");
        var capture = ProjectSceneSerializer.SerializeObjectForLibrary(follower);
        follower.PathFollowOffset = 9;
        typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [follower, capture, null]);
        Near(-3, follower.PathFollowOffset, "Undo restores follow offset");
        Check(!follower.FollowPathModifier!.Enabled, "Undo restores follow enabled state");

        var legacy = ProjectSceneSerializer.SerializeObjectForLibrary(obj);
        legacy.Modifiers = [new() { Count = 4 }];
        legacy.PathFollowObjectId = path.ObjectId;
        legacy.PathFollowOffset = 6;
        typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [obj, legacy, null]);
        Equal(2, obj.Modifiers.Count, "Legacy follow settings migrate alongside existing arrays");
        Equal(path.ObjectId, obj.FollowPathModifier!.PathObjectId, "Legacy target migrates");
        Near(6, obj.FollowPathModifier.PathOffset, "Legacy offset migrates");
        legacy.PathFollowObjectId = "";
        legacy.PathFollowOffset = 0;
        typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [obj, legacy, null]);
        Check(obj.FollowPathModifier != null, "Legacy keyframe-only follow gets a modifier");
        foreach (var invalid in new SceneModifier[]
        {
            new() { Type = SceneModifierType.FollowPath, PathOffset = float.NaN },
            new() { Type = SceneModifierType.FollowPath, PathOffset = float.PositiveInfinity }
        })
        {
            bool rejected = false;
            try { invalid.Validated(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid follow offset rejected");
        }
        foreach (SceneObject special in new SceneObject[] { new CameraSceneObject(), new LightSceneObject() })
        {
            special.Modifiers = [new() { Type = SceneModifierType.FollowPath, PathObjectId = path.ObjectId, PathOffset = 2 }];
            InverseKinematicsSolver.Evaluate([special, path]);
            Near(vec3.UnitY * 2, special.GetWorldPosition(), "Cameras and lights still follow paths");
        }
    }

    private static void ModifierPersistence(Fixture f)
    {
        var root = new SceneObject { Name = "Root" };
        var path = StraightPath();
        var source = new SceneObject { Name = "Source" };
        source.Modifiers = [new() { Count = 4, Axis = ArrayAxis.Z, Distance = -2, RotationX = 15,
            RotationY = 30, RotationZ = 45, ScaleX = 0.8f, ScaleY = 1.1f, ScaleZ = -1, PathObjectId = path.ObjectId },
            new() { Enabled = false, Count = 3 }];
        root.AddChild(path); root.AddChild(source);
        var (manifest, _) = f.Roundtrip(root);
        Equal(source.Modifiers[0], manifest.Root.Children[1].Modifiers![0], "Source-generated archive settings");
        var viewport = new Viewport();
        var spawn = new SpawnMenu { Viewport = viewport };
        var imported = ProjectSceneSerializer.SpawnObjectFromEntry(manifest.Root, viewport, spawn)!;
        Equal(imported.Children[0].ObjectId, imported.Children[1].Modifiers[0].PathObjectId, "Imported internal path remaps");
        var cloneLibrary = typeof(PropertiesPanel).GetMethod("CloneLibraryEntryRecursive", BindingFlags.Static | BindingFlags.NonPublic)!;
        var library = (ProjectSceneObjectEntry)cloneLibrary.Invoke(null, [manifest.Root])!;
        Check(!ReferenceEquals(library.Children[1].Modifiers, manifest.Root.Children[1].Modifiers), "Independent library stack");
        Equal(source.Modifiers[0], library.Children[1].Modifiers![0], "Library settings");
        var duplicate = typeof(SceneTree).GetMethod("DuplicateObject", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var copied = (SceneObject)duplicate.Invoke(new SceneTree { Viewport = viewport }, [imported, false, null])!;
        Equal(copied.Children[0].ObjectId, copied.Children[1].Modifiers[0].PathObjectId, "Duplicate path remaps");
        copied.Children[1].Modifiers[0] = copied.Children[1].Modifiers[0] with { Count = 9 };
        Equal(4, imported.Children[1].Modifiers[0].Count, "Duplicate changes do not alter original");
        var (standalone, _) = f.Roundtrip(source);
        Equal("", standalone.Root.Modifiers![0].PathObjectId, "Standalone clears external references");
        var capture = ProjectSceneSerializer.SerializeObjectForLibrary(source);
        var fingerprint = typeof(ProjectSceneSerializer).Assembly.GetType("MineImatorSimplyRemade.core.project.SceneObjectFingerprint")!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!;
        ulong Hash() => (ulong)fingerprint.Invoke(null, [new List<SceneObject> { source }])!;
        ulong initial = Hash();
        source.Modifiers[0] = source.Modifiers[0] with { Count = 8 };
        Check(Hash() != initial, "Modifier changes dirty fingerprint");
        typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [source, capture, null]);
        Equal(initial, Hash(), "Undo snapshot restores modifiers");
        source.Modifiers.Reverse();
        Check(Hash() != initial, "Stack ordering affects dirty fingerprint");
    }

    private static void ModifierRenderCollection(Fixture f)
    {
        var viewport = new Viewport();
        var obj = new SceneObject { PivotOffset = vec3.Zero };
        var mesh = new Mesh(null!);
        mesh.Vertices.Add(vec3.Zero);
        mesh.BoundingSphereRadius = 0.1f;
        obj.Visuals.Add(mesh);
        obj.Modifiers = [new() { Count = 4, Axis = ArrayAxis.X, Distance = 2 }];
        viewport.SceneObjects.Add(obj);
        var items = new List<(mat4, Mesh, float, bool, bool, bool)>();
        var noAo = new List<(mat4, Mesh, float, bool, bool, bool)>();
        var overlays = new List<(mat4, Mesh)>();
        var mode = typeof(Viewport).GetNestedType("SceneRenderMode", BindingFlags.NonPublic)!;
        var collect = typeof(Viewport).GetMethod("CollectRenderPairs", BindingFlags.Instance | BindingFlags.NonPublic)!;
        collect.Invoke(viewport, [viewport.SceneObjects, new vec3(0, 0, 10), Enum.Parse(mode, "Rendered"),
            Frustum.FromViewProj(mat4.Ortho(-20, 20, -20, 20, -20, 20)), items, noAo, overlays, null]);
        Equal(4, items.Count + noAo.Count, "Real viewport collects each instance");
        for (int i = 0; i < items.Count; i++)
            Near(new vec3(2 * i, 0, 0), (items[i].Item1 * new vec4(0, 0, 0, 1)).xyz, "Render item placement");
        var signature = typeof(Viewport).GetMethod("ComputeShadowCacheSignature", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int before = (int)signature.Invoke(viewport, [viewport.SceneObjects, false])!;
        obj.Modifiers[0] = obj.Modifiers[0] with { Distance = 3 };
        typeof(Viewport).GetMethod("ResetModifierEvaluation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewport, null);
        int after = (int)signature.Invoke(viewport, [viewport.SceneObjects, false])!;
        Check(before != after, "Changed copies invalidate shadow cache");
        var part = new SceneObject { PivotOffset = vec3.Zero, Position = vec3.UnitY, InheritModifiers = false };
        part.Visuals.Add(mesh);
        part.Modifiers = [new() { Count = 2, Axis = ArrayAxis.Z }];
        obj.AddChild(part);
        obj.Visuals.Clear();
        void CollectParts()
        {
            items.Clear(); noAo.Clear(); overlays.Clear();
            typeof(Viewport).GetMethod("ResetModifierEvaluation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewport, null);
            collect.Invoke(viewport, [viewport.SceneObjects, new vec3(0, 0, 10), Enum.Parse(mode, "Rendered"),
                Frustum.FromViewProj(mat4.Ortho(-20, 20, -20, 20, -20, 20)), items, noAo, overlays, null]);
        }
        CollectParts();
        Equal(2, items.Count + noAo.Count, "Uncombined render respects inheritance boundary and source stack");
        obj.CombineChildren = true;
        CollectParts();
        Equal(4, items.Count + noAo.Count, "Combined render evaluates parts with owner's stack");
        for (int i = 0; i < items.Count; i++)
            Near(new vec3(3 * i, 1, 0), (items[i].Item1 * new vec4(0, 0, 0, 1)).xyz, "Combined part retains local position");
        var plane = new SceneObject { ObjectType = "Plane", SpawnCategory = "Primitives",
            PrimitivePlaneFaceCamera = true, PivotOffset = vec3.Zero };
        plane.Visuals.Add(mesh);
        plane.Modifiers = [new() { Count = 2, Distance = 10 }];
        mat4 delta = new ModifierEvaluation([plane]).GetDeltas(plane)[1];
        vec3 camera = new(0, 0, 10);
        mat4 billboard = (mat4)typeof(Viewport).GetMethod("GetRenderableWorldMatrixForInstance",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [plane, camera, delta])!;
        Near(new vec3(10, 0, 0), (billboard * new vec4(0, 0, 0, 1)).xyz, "Billboard instance position");
        Near(new vec3(-10, 0, 10).Normalized, (billboard * new vec4(0, 0, 1, 0)).xyz.Normalized,
            "Each billboard faces camera from its own instance position");
    }

    private static unsafe void ModifierGraphics(string? modelPath = null)
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Array smoke test", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = Silk.NET.OpenGL.GL.GetApi(glfw.GetProcAddress);
            using var ubo = new SceneUniformBuffer(gl);
            Mesh.SceneUBO = ubo;
            Mesh.ShadowsEnabled = false;
            ubo.Upload(mat4.Identity, vec3.Ones, vec3.UnitY, vec3.Zero, vec3.UnitY, vec3.Zero,
                vec3.UnitY, vec3.Zero, 0, false, false, false);
            const uint size = 256;
            var viewport = new Viewport { Gl = gl };
            var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };
            var source = spawn.SpawnPrimitiveObject("Cube", "Array")!;
            source.Position = new(-3, 0, 0);
            source.Scale = new(0.5f);
            source.PivotOffset = vec3.Zero;
            source.Modifiers = [new() { Count = 4, Distance = 2 }];
            var mesh = source.Visuals.Single();
            var bone = new BoneSceneObject { BoneName = "arrayBone", Position = new(0, 0.25f, 0), PivotOffset = vec3.Zero };
            source.AddChild(bone);
            var piece = new SceneObject { Name = "Combined piece", PivotOffset = vec3.Zero, InheritModifiers = false,
                IsSelectable = false, Modifiers = [new() { Count = 2, Axis = ArrayAxis.Z }] };
            piece.AssignObjectId();
            source.AddChild(piece);
            mesh.BoneNames.Add(bone.BoneName);
            mesh.BoneInverseBindMatrices.Add(mat4.Identity);
            foreach (var vertex in mesh.Vertices)
            {
                mesh.BoneIndices.Add(new ivec4(0));
                mesh.BoneWeights.Add(new vec4(1, 0, 0, 0));
            }
            mesh.Upload();
            var path = StraightPath();
            path.Position = source.Position;
            path.Rotation = new(0, 0, -MathF.PI / 2f);
            viewport.SceneObjects.Add(path);
            mat4 view = mat4.Translate(new vec3(0, 0, -10));
            mat4 projection = mat4.Ortho(-5, 5, -3, 3, 0.1f, 30f);
            var mode = typeof(Viewport).GetNestedType("SceneRenderMode", BindingFlags.NonPublic)!;
            object quality = Enum.Parse(mode, "Rendered");
            object? Call(string name, params object?[] args) =>
                typeof(Viewport).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewport, args);
            uint Field(string name) => (uint)typeof(Viewport).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewport)!;
            using var pick = new Shader(gl);
            pick.CompileShader("pick.vert", "pick.frag");
            using var silhouette = new Shader(gl);
            silhouette.CompileShader("pick.vert", "silhouette.frag");
            using var shadow = new Shader(gl);
            shadow.CompileShader("shadow_depth.vert", "shadow_depth.frag");
            using var pointShadow = new Shader(gl);
            pointShadow.CompileShader("point_shadow_depth.vert", "point_shadow_depth.frag");
            typeof(Viewport).GetField("_pickShader", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewport, pick);
            typeof(Viewport).GetField("_silhouetteShader", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewport, silhouette);
            Call("InitPickFramebuffer", size, size);
            Call("InitSilhouetteFbo", size, size);
            float activeOffset = 0f;
            try
            {
                foreach (bool combined in new[] { false, true })
                foreach (var (onPath, offset, implicitPath) in new[]
                    { (false, 0f, false), (true, 0f, false), (true, 1f, false), (true, 1.5f, true) })
                {
                    source.CombineChildren = combined;
                    source.Visuals.Clear();
                    piece.Visuals.Clear();
                    (combined ? piece : source).AddMesh(mesh);
                    activeOffset = offset;
                    source.Position = offset == 0f ? path.Position : vec3.Zero;
                    source.Modifiers = [new()
                    {
                        Count = 4, Distance = 2,
                        PathObjectId = onPath && !implicitPath ? path.ObjectId : "",
                        Axis = onPath ? ArrayAxis.Y : ArrayAxis.X
                    }];
                    if (offset != 0f)
                    {
                        source.Modifiers.Add(new() { Type = SceneModifierType.FollowPath, PathObjectId = path.ObjectId, PathOffset = offset });
                    }
                    InverseKinematicsSolver.Evaluate(viewport.SceneObjects);
                    Call("ResetModifierEvaluation");
                    var items = new List<(mat4, Mesh, float, bool, bool, bool)>();
                    var noAo = new List<(mat4, Mesh, float, bool, bool, bool)>();
                    var overlays = new List<(mat4, Mesh)>();
                    Call("CollectRenderPairs", viewport.SceneObjects, new vec3(0, 0, 10), quality,
                        Frustum.FromViewProj(projection * view), items, noAo, overlays, null);
                    Equal(4, items.Count + noAo.Count, "Four GPU draw instances");
                    BindAndClear();
                    foreach (var item in items.Concat(noAo)) item.Item2.Render(item.Item1, view, projection);
                    AssertCopies(ReadColor(), "Scene pixels");
                    BindAndClear();
                    gl.UseProgram(pick.ShaderProgram);
                    Call("RenderPickObjects", new[] { source }, view, projection, null, false);
                    AssertCopies(ReadColor(), "Pick pixels");
                    for (int i = 0; i < 4; i++)
                    {
                        byte[] rgb = new byte[4];
                        fixed (byte* p = rgb)
                            gl.ReadPixels(PixelX(i), (int)size / 2 + 4, 1, 1, Silk.NET.OpenGL.GLEnum.Rgba, Silk.NET.OpenGL.GLEnum.UnsignedByte, p);
                        Equal((int)MathF.Round(source.PickColor.x * 255), (int)rgb[0], "Copy selects source ID");
                        Equal((int)MathF.Round(source.PickColor.y * 255), (int)rgb[1], "Copy selects source ID");
                        Equal((int)MathF.Round(source.PickColor.z * 255), (int)rgb[2], "Copy selects source ID");
                    }
                    SelectionManager.Instance!.ClearSelection();
                    SelectionManager.Instance.SelectObject(source);
                    Call("RenderSilhouettePassGeneric", Field("_silhouetteFbo"), size, size, view, projection);
                    gl.BindFramebuffer(Silk.NET.OpenGL.GLEnum.Framebuffer, Field("_silhouetteFbo"));
                    gl.ReadBuffer(Silk.NET.OpenGL.GLEnum.ColorAttachment0);
                    AssertCopies(ReadColor(), "Outline pixels");
                    BindAndClear();
                    gl.DrawBuffer(Silk.NET.OpenGL.GLEnum.None);
                    Call("RenderShadowCasters", new[] { source }, projection * view, shadow, quality, null);
                    AssertDepth("Directional shadow");
                    BindAndClear();
                    gl.DrawBuffer(Silk.NET.OpenGL.GLEnum.None);
                    Call("RenderPointShadowCasters", new[] { source }, projection * view, new vec3(0, 0, 10), 30f, pointShadow, quality, null);
                    AssertDepth("Point shadow");
                    var palette = mesh.BoneMatrices ?? throw new InvalidOperationException("Missing skinning palette.");
                    mat4 sourcePalette = source.GetWorldMatrix().Inverse * bone.GetWorldMatrix();
                    Near((sourcePalette * new vec4(0, 0, 0, 1)).xyz,
                        (palette[0] * new vec4(0, 0, 0, 1)).xyz, "Copies retain source-local skinning palette");
                    Equal(Silk.NET.OpenGL.GLEnum.NoError, gl.GetError(), "OpenGL errors");
                }

                if (modelPath != null)
                {
                    var loader = new MineImatorSimplyRemade.core.mdl.mineImator.MineImatorLoader();
                    loader.Initialize(gl);
                    var model = loader.LoadModel(Path.GetFullPath(modelPath))
                        ?? throw new InvalidDataException("Unable to load combination test model.");
                    var character = loader.CreateCharacterFromModel(model)
                        ?? throw new InvalidDataException("Unable to instantiate combination test model.");
                    var nodes = character.GetAllDescendants().Prepend(character).ToArray();
                    try
                    {
                        var skirt = nodes.Single(node => node.Name == "Skirt");
                        var parts = skirt.GetAllDescendants().Prepend(skirt).ToArray();
                        Check(parts.Length > 2, "Model skirt includes nested source parts");
                        var meshes = parts.SelectMany(part => part.Visuals.Where(mesh => !mesh.PickOnly)
                            .Select(mesh => (Part: part, Mesh: mesh, World: part.GetWorldMatrix(),
                                Texture: mesh.TextureId, Uvs: mesh.TexCoords.ToArray()))).ToArray();
                        Check(meshes.Length > 1, "Model skirt has multiple meshes");
                        vec3 min = new(float.MaxValue), max = new(float.MinValue);
                        foreach (var item in meshes)
                        foreach (vec3 vertex in item.Mesh.Vertices)
                        {
                            vec3 p = (item.World * new vec4(vertex, 1)).xyz;
                            min = vec3.Min(min, p);
                            max = vec3.Max(max, p);
                        }
                        vec3 center = (min + max) * 0.5f;
                        float radius = MathF.Max(0.1f, (max - min).Length * 0.65f);
                        var modelView = mat4.Translate(-center + new vec3(0, 0, -radius * 3 - 1));
                        var modelProjection = mat4.Ortho(-radius, radius, -radius, radius, 0.1f, radius * 6 + 10);
                        byte[] RenderModel()
                        {
                            BindAndClear();
                            var evaluation = new ModifierEvaluation([character]);
                            foreach (var item in meshes)
                            foreach (var delta in evaluation.GetDeltas(item.Part))
                                item.Mesh.Render(delta * item.Part.GetWorldMatrix(), modelView, modelProjection);
                            return ReadColor();
                        }
                        byte[] originalPixels = RenderModel();
                        Check(originalPixels.Where((value, index) => index % 4 != 3).Any(value => value != 0),
                            "Model skirt produces visible GPU pixels");
                        skirt.CombineChildren = true;
                        Check(originalPixels.SequenceEqual(RenderModel()), "Runtime combination preserves exact skirt pixels");
                        skirt.InheritModifiers = false;
                        skirt.Modifiers = [new() { Count = 3, Distance = 2 }];
                        skirt.Parent!.Modifiers = [new() { Count = 2, Distance = 5 }];
                        foreach (var part in parts.Skip(1))
                        {
                            part.InheritModifiers = false;
                            part.Modifiers = [new() { Count = 2, Axis = ArrayAxis.Z }];
                        }
                        var evaluation = new ModifierEvaluation([character]);
                        foreach (var item in meshes)
                        {
                            Equal(3, evaluation.GetDeltas(item.Part).Count, "Every skirt piece uses combined stack");
                            Equal(skirt, item.Part.GetViewportSelectionTarget(), "Skirt mesh picks combined owner");
                            Equal(item.World, item.Part.GetWorldMatrix(), "Skirt transforms retained");
                            Equal(item.Texture, item.Mesh.TextureId, "Skirt textures retained");
                            Check(item.Uvs.SequenceEqual(item.Mesh.TexCoords), "Skirt UVs retained");
                        }
                        skirt.CombineChildren = false;
                        foreach (var part in parts.Skip(1))
                            Equal(2, new ModifierEvaluation([character]).GetDeltas(part).Count,
                                "Uncombined skirt restores independent stacks");
                        Console.WriteLine($"PASS Model skirt runtime combination: {parts.Length} parts, {meshes.Length} textured meshes, exact GPU pixel preservation");
                    }
                    finally
                    {
                        foreach (var node in nodes)
                            foreach (var modelMesh in node.Visuals)
                                modelMesh.Dispose();
                    }
                }

                void BindAndClear()
                {
                    gl.BindFramebuffer(Silk.NET.OpenGL.GLEnum.Framebuffer, Field("_pickFbo"));
                    gl.DrawBuffer(Silk.NET.OpenGL.GLEnum.ColorAttachment0);
                    gl.ReadBuffer(Silk.NET.OpenGL.GLEnum.ColorAttachment0);
                    gl.Viewport(0, 0, size, size);
                    gl.Enable(Silk.NET.OpenGL.GLEnum.DepthTest);
                    gl.DepthMask(true);
                    gl.DepthFunc(Silk.NET.OpenGL.GLEnum.Less);
                    gl.Disable(Silk.NET.OpenGL.GLEnum.CullFace);
                    gl.Disable(Silk.NET.OpenGL.GLEnum.Blend);
                    gl.ClearColor(0, 0, 0, 0);
                    gl.Clear(Silk.NET.OpenGL.ClearBufferMask.ColorBufferBit | Silk.NET.OpenGL.ClearBufferMask.DepthBufferBit);
                }
                byte[] ReadColor()
                {
                    byte[] pixels = new byte[size * size * 4];
                    fixed (byte* p = pixels)
                        gl.ReadPixels(0, 0, size, size, Silk.NET.OpenGL.GLEnum.Rgba, Silk.NET.OpenGL.GLEnum.UnsignedByte, p);
                    return pixels;
                }
                int PixelX(int i) => (int)(((-3f + activeOffset + 2 * i + 0.125f) / 10f + 0.5f) * size);
                void AssertCopies(byte[] pixels, string message)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        int occupied = 0;
                        for (int y = (int)size / 2 - 12; y < (int)size / 2 + 12; y++)
                        for (int x = (int)PixelX(i) - 8; x < (int)PixelX(i) + 8; x++)
                        {
                            int offset = (y * (int)size + x) * 4;
                            if (pixels[offset] != 0 || pixels[offset + 1] != 0 || pixels[offset + 2] != 0) occupied++;
                        }
                        Check(occupied > 16, $"{message}: copy {i} occupies {occupied} pixels");
                    }
                }
                void AssertDepth(string message)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        float depth = 1;
                        gl.ReadPixels(PixelX(i), (int)size / 2 + 4, 1, 1, Silk.NET.OpenGL.GLEnum.DepthComponent, Silk.NET.OpenGL.GLEnum.Float, &depth);
                        Check(depth < 0.99f, $"{message}: copy {i} writes depth ({depth})");
                    }
                }
            }
            finally
            {
                SelectionManager.Instance?.ClearSelection();
                mesh.Dispose();
                gl.DeleteFramebuffer(Field("_pickFbo"));
                gl.DeleteRenderbuffer(Field("_pickRbo"));
                gl.DeleteTexture(Field("_pickColorTex"));
                gl.DeleteFramebuffer(Field("_silhouetteFbo"));
                gl.DeleteTexture(Field("_silhouetteTex"));
                Mesh.SceneUBO = null;
            }
        }
        finally { glfw.DestroyWindow(window); glfw.Terminate(); }
    }
}
