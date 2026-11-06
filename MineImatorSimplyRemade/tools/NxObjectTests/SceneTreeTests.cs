using System.Text.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.history;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private static SceneObject TreeObject(string name, params SceneObject[] children)
    {
        var obj = new SceneObject { Name = name, ObjectType = "Empty", SpawnCategory = "Primitives" };
        obj.AssignObjectId();
        foreach (var child in children)
            obj.AddChild(child);
        return obj;
    }

    private static void TreeOrder(IEnumerable<SceneObject> objects, params SceneObject[] expected) =>
        Check(objects.SequenceEqual(expected), "Unexpected scene order: " + string.Join(", ", objects.Select(obj => obj.Name)));

    private static void SceneTreeOrdering(Fixture f)
    {
        var a = TreeObject("A");
        var b = TreeObject("B");
        var c = TreeObject("C");
        var d = TreeObject("D");
        var scene = new List<SceneObject> { a, b, c, d };
        Check(SceneHierarchy.Move(scene, [d], b, SceneDropPlacement.Before), "Move root upward");
        TreeOrder(scene, a, d, b, c);
        Check(SceneHierarchy.Move(scene, [a], b, SceneDropPlacement.After), "Move root downward");
        TreeOrder(scene, d, b, a, c);
        Check(SceneHierarchy.Move(scene, [c, d], a, SceneDropPlacement.Before), "Batch uses tree order, not click order");
        TreeOrder(scene, b, d, c, a);
        Check(!SceneHierarchy.Move(scene, [d, c], b, SceneDropPlacement.After), "Adjacent batch is a no-op");
        TreeOrder(scene, b, d, c, a);
        Check(SceneHierarchy.Move(scene, [b, d], a, SceneDropPlacement.After), "Batch crosses its own former indices");
        TreeOrder(scene, c, a, b, d);
        Check(SceneHierarchy.Move(scene, [c], null, SceneDropPlacement.RootEnd), "Root blank drop appends");
        TreeOrder(scene, a, b, d, c);
        Check(!SceneHierarchy.Move(scene, [c], null, SceneDropPlacement.RootEnd), "Already-last root is a no-op");
        Check(scene.All(obj => obj.Parent == null), "Reordering does not introduce parents");
    }

    private static void SceneTreeParenting(Fixture f)
    {
        var a = TreeObject("A");
        var b = TreeObject("B");
        var c = TreeObject("C");
        var p = TreeObject("Parent", a, b, c);
        var q = TreeObject("Other");
        var child = TreeObject("Nested");
        a.AddChild(child);
        var scene = new List<SceneObject> { p, q };
        a.SetLocalPosition(new vec3(3, 5, 7));
        a.SetLocalRotation(new vec3(10, 20, 30));
        a.SetLocalScale(new vec3(2, 3, 4));
        var position = a.Position;
        var rotation = a.Rotation;
        var scale = a.Scale;
        TreeOrder(SceneHierarchy.GetMoveRoots(scene, [child, a, c]), a, c);
        Check(SceneHierarchy.Move(scene, [child, a, c], q, SceneDropPlacement.Inside), "Move selected subtree roots");
        TreeOrder(p.Children, b);
        TreeOrder(q.Children, a, c);
        Check(a.Parent == q && child.Parent == a && c.Parent == q, "Descendants retain hierarchy");
        Near(position, a.Position, "Parenting preserves local position");
        Near(rotation, a.Rotation, "Parenting preserves local rotation");
        Near(scale, a.Scale, "Parenting preserves local scale");
        Check(SceneHierarchy.Move(scene, [c], a, SceneDropPlacement.Before), "Reorder siblings");
        TreeOrder(q.Children, c, a);
        Check(SceneHierarchy.Move(scene, [a, b], c, SceneDropPlacement.Before), "Batch across parents");
        TreeOrder(q.Children, b, a, c);
        TreeOrder(p.Children);
        Check(SceneHierarchy.Move(scene, [b, c], p, SceneDropPlacement.Before), "Insert children at root edge");
        TreeOrder(scene, b, c, p, q);
        Check(b.Parent == null && c.Parent == null && a.Parent == q, "Root-edge drop unparents");
    }

    private static void SceneTreeSafety(Fixture f)
    {
        var child = TreeObject("Child");
        var parent = TreeObject("Parent", child);
        var other = TreeObject("Other");
        var scene = new List<SceneObject> { parent, other };
        foreach (var placement in new[] { SceneDropPlacement.Before, SceneDropPlacement.Inside, SceneDropPlacement.After })
        {
            Check(!SceneHierarchy.CanMove(scene, [parent, other], child, placement), "Reject descendant target for whole batch");
            Check(!SceneHierarchy.CanMove(scene, [parent], parent, placement), "Reject self target");
            bool rejected = false;
            try { SceneHierarchy.Move(scene, [parent, other], child, placement); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Invalid mutations report failure");
            TreeOrder(scene, parent, other);
            TreeOrder(parent.Children, child);
        }
        Check(!SceneHierarchy.CanMove(scene, [], other, SceneDropPlacement.Inside), "Empty selection rejected");
        Check(!SceneHierarchy.CanMove(scene, [TreeObject("Detached")], other, SceneDropPlacement.Inside), "Stale source rejected");
        Check(!SceneHierarchy.CanMove(scene, [other], TreeObject("Detached"), SceneDropPlacement.Before), "Stale target rejected");
        Check(!SceneHierarchy.CanMove(scene, [other], null, SceneDropPlacement.Before), "Missing target rejected");
        child.HideInSceneTree = true;
        Check(!SceneHierarchy.CanMove(scene, [other], child, SceneDropPlacement.Inside), "Hidden target rejected");
        child.HideInSceneTree = false;
        other.IsRuntimeTransient = true;
        Check(!SceneHierarchy.CanMove(scene, [other], parent, SceneDropPlacement.Inside), "Transient source rejected");
        Check(!SceneHierarchy.CanMove(scene, [parent], other, SceneDropPlacement.Before), "Transient target rejected");
    }

    private static void SceneTreeShift(Fixture f)
    {
        var a = TreeObject("A");
        var b = TreeObject("B");
        var c = TreeObject("C");
        var d = TreeObject("D");
        var e = TreeObject("E");
        var scene = new List<SceneObject> { a, b, c, d, e };
        Check(SceneHierarchy.Shift(scene, [c, b], down: false), "Shift contiguous block up");
        TreeOrder(scene, b, c, a, d, e);
        Check(!SceneHierarchy.CanShift(scene, [c, b], down: false), "Block at upper boundary disabled");
        Check(!SceneHierarchy.Shift(scene, [c, b], down: false), "Boundary is a no-op");
        Check(SceneHierarchy.Shift(scene, [b, d], down: true), "Shift separated selections down");
        TreeOrder(scene, c, b, a, e, d);
        Check(!SceneHierarchy.CanShift(scene, [d], down: true), "Lower boundary disabled");
        var p = TreeObject("Parent", a, b, c);
        scene = [p, d, e];
        Check(SceneHierarchy.Shift(scene, [b, d], down: true), "Shift independently across parents");
        TreeOrder(scene, p, e, d);
        TreeOrder(p.Children, a, c, b);
        Check(SceneHierarchy.Shift(scene, [p, b], down: true), "Selected descendants do not move independently");
        TreeOrder(scene, e, p, d);
        TreeOrder(p.Children, a, c, b);
    }

    private static void SceneTreeEquipment(Fixture f)
    {
        var character = new CharacterSceneObject { Name = "Character" };
        var limb = new MiBoneSceneObject { Name = "Arm", BoneName = "arm", ModelOwner = character };
        character.AddChild(limb);
        character.BoneObjects.Add("arm", limb);
        var equipment = new CharacterSceneObject { Name = "Armor", SpawnCategory = "Equipment" };
        var part = new MiBoneSceneObject { Name = "Armor arm", BoneName = "arm", ModelOwner = equipment };
        equipment.AddChild(part);
        equipment.BoneObjects.Add("arm", part);
        var prop = TreeObject("Prop");
        var scene = new List<SceneObject> { character, equipment, prop };
        equipment.SetLocalPosition(new vec3(5, 6, 7));
        equipment.SetLocalRotation(new vec3(10, 20, 30));
        equipment.SetLocalScale(new vec3(2));
        Check(SceneHierarchy.Move(scene, [prop, equipment], limb, SceneDropPlacement.Inside), "Mixed equipment and ordinary batch");
        Check(equipment.Parent == character && prop.Parent == limb, "Armor resolves to character; prop stays on limb");
        Near(vec3.Zero, equipment.Position, "Armor position fitted");
        Near(vec3.Zero, equipment.Rotation, "Armor rotation fitted");
        Near(vec3.Ones, equipment.Scale, "Armor scale fitted");
        Check(equipment.InheritPose, "Armor inherits pose");
        equipment.SetLocalPosition(new vec3(1, 2, 3));
        Check(SceneHierarchy.Move(scene, [equipment], limb, SceneDropPlacement.Before), "Armor sibling reorder");
        TreeOrder(character.Children, equipment, limb);
        Near(new vec3(1, 2, 3), equipment.Position, "Sibling reorder does not refit armor");
    }

    private static void SceneTreeHistory(Fixture f)
    {
        var child = TreeObject("Child");
        var a = TreeObject("A", child);
        var b = TreeObject("B");
        var c = TreeObject("C");
        var viewport = new Viewport();
        viewport.SceneObjects.AddRange([a, b, c]);
        var selection = SelectionManager.Instance;
        selection.ClearSelection();
        selection.SelectObject(a);
        selection.SelectObject(c);
        var selected = selection.SelectedObjects.ToArray();
        var before = new SceneHistoryAction.HierarchyState(viewport);
        var project = SceneHistoryAction.ProjectState.Capture(ProjectManager.Instance.Manifest);
        Check(SceneHierarchy.Move(viewport.SceneObjects, [c, a, child], b, SceneDropPlacement.Inside), "History batch move");
        var after = SceneHistoryAction.HierarchyState.Capture(viewport, before);
        var action = SceneHistoryAction.Create([], before, after, project, project);
        Check(action != null, "Hierarchy changes create history");
        for (int i = 0; i < 3; i++)
        {
            Check(action!.Apply(viewport, null, undo: true), "Undo applies");
            TreeOrder(viewport.SceneObjects, a, b, c);
            TreeOrder(a.Children, child);
            TreeOrder(b.Children);
            Check(selection.SelectedObjects.SequenceEqual(selected), "Undo preserves selection and object identity");
            Check(action.Apply(viewport, null, undo: false), "Redo applies");
            TreeOrder(viewport.SceneObjects, b);
            TreeOrder(b.Children, a, c);
            TreeOrder(a.Children, child);
            Check(selection.SelectedObjects.SequenceEqual(selected), "Redo preserves selection and object identity");
        }

        before = new SceneHistoryAction.HierarchyState(viewport);
        SceneHierarchy.Move(viewport.SceneObjects, [c], a, SceneDropPlacement.Before);
        after = SceneHistoryAction.HierarchyState.Capture(viewport, before);
        action = SceneHistoryAction.Create([], before, after, project, project);
        Check(action != null && action.Apply(viewport, null, undo: true), "Sibling reorder undo");
        TreeOrder(b.Children, a, c);
        Check(action!.Apply(viewport, null, undo: false), "Sibling reorder redo");
        TreeOrder(b.Children, c, a);
        selection.ClearSelection();
    }

    private static void SceneTreePersistence(Fixture f)
    {
        var a = TreeObject("A");
        var b = TreeObject("B");
        var c = TreeObject("C");
        var parent = TreeObject("Parent", a, b);
        var viewport = new Viewport();
        viewport.SceneObjects.AddRange([parent, c]);
        SceneHierarchy.Move(viewport.SceneObjects, [c], a, SceneDropPlacement.After);
        SceneHierarchy.Move(viewport.SceneObjects, [b], parent, SceneDropPlacement.Before);
        TreeOrder(viewport.SceneObjects, b, parent);
        TreeOrder(parent.Children, a, c);
        var manifest = new ProjectManifest();
        ProjectSceneSerializer.WriteSceneToManifest(manifest, viewport);
        string json = JsonSerializer.Serialize(manifest, AppJsonContext.Default.ProjectManifest);
        var saved = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectManifest)!;
        var loaded = new Viewport();
        ProjectSceneSerializer.LoadSceneFromManifest(saved, loaded, new SpawnMenu { Viewport = loaded });
        Check(loaded.SceneObjects.Select(obj => obj.Name).SequenceEqual(["B", "Parent"]), "Root order survives save/load");
        Check(loaded.SceneObjects[1].Children.Select(obj => obj.Name).SequenceEqual(["A", "C"]), "Child order survives save/load");
        Check(loaded.SceneObjects[1].Children.All(obj => obj.Parent == loaded.SceneObjects[1]), "Loaded parent pointers");
        Check(loaded.SceneObjects[0].ObjectId == b.ObjectId && loaded.SceneObjects[1].Children[1].ObjectId == c.ObjectId,
            "IDs survive save/load");

        SelectionManager.Instance.ClearSelection();
        SelectionManager.Instance.SelectObject(loaded.SceneObjects[1].Children[1]);
        var tree = new SceneTree { Viewport = loaded };
        tree.UnparentSelectedObjects();
        Check(loaded.SceneObjects.Select(obj => obj.Name).SequenceEqual(["B", "Parent", "C"]), "Context unparent appends");
        tree.MoveSelectedObjects(down: false);
        Check(loaded.SceneObjects.Select(obj => obj.Name).SequenceEqual(["B", "C", "Parent"]), "Context move uses shared ordering");
        SelectionManager.Instance.ClearSelection();
    }

    private static void SceneTreeGrouping(Fixture f)
    {
        var a = TreeObject("A");
        var b = TreeObject("B");
        var c = TreeObject("C");
        var child = TreeObject("Nested");
        b.AddChild(child);
        var parent = TreeObject("Parent", a, b, c);
        var viewport = new Viewport();
        viewport.SceneObjects.Add(parent);
        var selection = SelectionManager.Instance;
        selection.ClearSelection();
        selection.SelectObject(c);
        selection.SelectObject(b);
        var tree = new SceneTree { Viewport = viewport };
        tree.GroupSelectedObjects();
        var group = parent.Children[1];
        TreeOrder(parent.Children, a, group);
        TreeOrder(group.Children, b, c);
        TreeOrder(b.Children, child);
        Check(group.Parent == parent && selection.IsSelected(group), "Grouping inserts at the first selected sibling and selects group");
        selection.ClearSelection();
    }

    private static unsafe void SceneTreeInteraction(Fixture f)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        var selection = SelectionManager.Instance;
        Action? selectionChanged = null;
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new System.Numerics.Vector2(800, 700);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            var inner = TreeObject("Inner");
            var a = TreeObject("A");
            var b = TreeObject("B", inner);
            var c = TreeObject("C");
            var d = TreeObject("D");
            var viewport = new Viewport();
            viewport.SceneObjects.AddRange([a, b, c, d]);
            var tree = new SceneTree { Viewport = viewport };
            tree.Initialize();
            selectionChanged = typeof(SceneTree).GetMethod("OnSelectionChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action>(tree);
            foreach (string field in new[] { "_lockedIcon", "_unlockedIcon", "_visibleIcon", "_hiddenIcon" })
                typeof(SceneTree).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tree, 1u);

            var ids = new Dictionary<SceneObject, uint>();
            var opened = new Dictionary<SceneObject, bool>();
            uint hovered = 0;
            void Frame(float x = -100, float y = -100, bool down = false)
            {
                io.AddMousePosEvent(x, y);
                io.AddMouseButtonEvent(0, down);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(600, 600), ImGuiCond.Always);
                tree.Render();
                hovered = context.HoveredId;
                ImGui.Begin(Localization.Label("text.common.scene_tree", "Scene Tree"));
                ImGui.PushID("##sceneTreeItems");
                void NodeIds(SceneObject obj)
                {
                    ImGui.PushID(RuntimeHelpers.GetHashCode(obj));
                    ids[obj] = ImGui.GetID("###node");
                    opened[obj] = ImGui.GetStateStorage().GetBool(ids[obj]);
                    ImGui.PushID("###node");
                    foreach (var child in obj.Children)
                        NodeIds(child);
                    ImGui.PopID();
                    ImGui.PopID();
                }
                foreach (var obj in viewport.SceneObjects)
                    NodeIds(obj);
                ImGui.PopID();
                ImGui.End();
                ImGui.Render();
            }

            (float Top, float Bottom) Row(SceneObject obj)
            {
                var hits = new List<int>();
                for (int y = 70; y < 350; y++)
                {
                    Frame(120, y);
                    if (hovered == ids[obj])
                        hits.Add(y);
                }
                Check(hits.Count >= 8, "Actual ImGui row was not found: " + obj.Name);
                return (hits[0], hits[^1]);
            }

            Frame();
            Frame();
            var source = Row(a);
            var target = Row(d);
            selection.ClearSelection();
            selection.SelectObject(a);
            selection.SelectObject(c);
            Frame(120, (source.Top + source.Bottom) / 2, down: true);
            Check(selection.IsSelected(a) && selection.IsSelected(c), "Pressing selected row preserves multi-selection");
            Frame(150, (source.Top + source.Bottom) / 2, down: true);
            Frame(120, target.Top + 1, down: true);
            Frame(120, target.Top + 1, down: true);
            Frame(120, target.Top + 1);
            Frame();
            TreeOrder(viewport.SceneObjects, b, a, c, d);
            Check(selection.IsSelected(a) && selection.IsSelected(c), "Actual multi-drag keeps selection");

            source = Row(a);
            target = Row(b);
            Frame(120, (source.Top + source.Bottom) / 2, down: true);
            Frame(150, (source.Top + source.Bottom) / 2, down: true);
            for (int i = 0; i < 40; i++)
                Frame(120, (target.Top + target.Bottom) / 2, down: true);
            Check(opened[b], "Hovering for 0.6 seconds opens the actual tree branch");
            Frame(120, (target.Top + target.Bottom) / 2);
            Frame();
            TreeOrder(viewport.SceneObjects, b, d);
            TreeOrder(b.Children, inner, a, c);

            var expand = typeof(SceneTree).GetMethod("SetAllExpanded", BindingFlags.Instance | BindingFlags.NonPublic)!;
            expand.Invoke(tree, [false]);
            Frame();
            Check(!opened[b], "Collapse All closes the branch");
            expand.Invoke(tree, [true]);
            Frame();
            Check(opened[b], "Expand All opens the branch");

            source = Row(a);
            Frame(120, (source.Top + source.Bottom) / 2, down: true);
            Frame(150, (source.Top + source.Bottom) / 2, down: true);
            Frame(120, 550, down: true);
            Frame(120, 550, down: true);
            Frame(120, 550);
            Frame();
            TreeOrder(viewport.SceneObjects, b, d, a, c);
            TreeOrder(b.Children, inner);

            selection.ClearSelection();
            selection.SelectObject(c);
            source = Row(c);
            target = Row(b);
            Frame(120, (source.Top + source.Bottom) / 2, down: true);
            Frame(150, (source.Top + source.Bottom) / 2, down: true);
            Frame(120, target.Bottom - 1, down: true);
            Frame(120, target.Bottom - 1, down: true);
            Frame(120, target.Bottom - 1);
            Frame();
            TreeOrder(viewport.SceneObjects, b, c, d, a);

            selection.ClearSelection();
            selection.SelectObject(b);
            source = Row(b);
            target = Row(inner);
            Frame(120, (source.Top + source.Bottom) / 2, down: true);
            Frame(150, (source.Top + source.Bottom) / 2, down: true);
            Frame(120, (target.Top + target.Bottom) / 2, down: true);
            Frame(120, (target.Top + target.Bottom) / 2);
            Frame();
            TreeOrder(viewport.SceneObjects, b, c, d, a);
            TreeOrder(b.Children, inner);

            selection.ClearSelection();
            selection.SelectObject(a);
            selection.SelectObject(c);
            source = Row(a);
            Frame(120, (source.Top + source.Bottom) / 2, down: true);
            Check(selection.IsSelected(a) && selection.IsSelected(c), "Multi-selection survives mouse-down");
            Frame(120, (source.Top + source.Bottom) / 2);
            TreeOrder(selection.SelectedObjects, a);

            for (int i = 0; i < 25; i++)
                Frame();
            Frame(120, (source.Top + source.Bottom) / 2, down: true);
            Frame(120, (source.Top + source.Bottom) / 2);
            Frame(120, (source.Top + source.Bottom) / 2, down: true);
            Frame(120, (source.Top + source.Bottom) / 2);
            Check(typeof(SceneTree).GetField("_renamingObject", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tree) == a,
                "Deferred selection click does not cancel double-click rename");
            Frame(650, 650, down: true);
            Frame(650, 650);

            expand.Invoke(tree, [false]);
            typeof(SceneTree).GetField("_searchQuery", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tree, "Inner");
            Frame();
            Frame();
            Check(opened[b], "Search exposes a match inside a collapsed branch");
            selection.ClearSelection();
        }
        finally
        {
            if (selectionChanged != null)
                selection.SelectionChanged -= selectionChanged;
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }
}
