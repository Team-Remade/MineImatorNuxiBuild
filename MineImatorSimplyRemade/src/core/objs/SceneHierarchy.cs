using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemadeNuxi.core.objs;

internal enum SceneDropPlacement
{
    Before,
    Inside,
    After,
    RootEnd
}

internal static class SceneHierarchy
{
    public static List<SceneObject> GetMoveRoots(IReadOnlyList<SceneObject> scene, IEnumerable<SceneObject> objects)
    {
        var selected = objects.ToHashSet();
        var result = new List<SceneObject>();
        foreach (var root in scene)
            Visit(root);
        return result;

        void Visit(SceneObject obj)
        {
            if (obj.IsRuntimeTransient)
                return;
            if (selected.Contains(obj))
            {
                result.Add(obj);
                return;
            }
            foreach (var child in obj.Children)
                Visit(child);
        }
    }

    public static bool CanMove(IReadOnlyList<SceneObject> scene, IReadOnlyList<SceneObject> objects,
        SceneObject? target, SceneDropPlacement placement)
    {
        if (!Enum.IsDefined(placement) || objects.Count == 0)
            return false;
        var roots = GetMoveRoots(scene, objects);
        if (roots.Count == 0 || objects.Any(obj =>
                !roots.Any(root => obj == root || obj.IsDescendantOf(root))))
            return false;
        if (placement != SceneDropPlacement.RootEnd &&
            (target == null || target.HideInSceneTree || target.IsRuntimeTransient ||
             GetMoveRoots(scene, [target]).Count == 0))
            return false;
        if (placement != SceneDropPlacement.RootEnd &&
            roots.Any(root => target == root || target!.IsDescendantOf(root)))
            return false;
        return roots.All(obj =>
        {
            var parent = DestinationParent(obj, target, placement);
            return parent == null || (parent != obj && !parent.IsDescendantOf(obj));
        });
    }

    public static bool Move(List<SceneObject> scene, IReadOnlyList<SceneObject> objects,
        SceneObject? target, SceneDropPlacement placement)
    {
        if (!CanMove(scene, objects, target, placement))
            throw new InvalidOperationException("The scene move has an invalid source, target, or cyclic destination.");

        var roots = GetMoveRoots(scene, objects);
        var orders = new List<SiblingOrder>();
        SiblingOrder Order(SceneObject? parent)
        {
            var order = orders.FirstOrDefault(item => item.Parent == parent);
            if (order == null)
            {
                order = new SiblingOrder(parent, parent?.Children ?? scene);
                orders.Add(order);
            }
            return order;
        }

        foreach (var obj in roots)
            Order(obj.Parent).After.Remove(obj);

        var fitting = new List<SceneObject>();
        if (placement is SceneDropPlacement.Before or SceneDropPlacement.After)
        {
            var destination = Order(target!.Parent).After;
            int index = destination.IndexOf(target) + (placement == SceneDropPlacement.After ? 1 : 0);
            destination.InsertRange(index, roots);
        }
        else
        {
            foreach (var obj in roots)
            {
                var parent = DestinationParent(obj, target, placement);
                Order(parent).After.Add(obj);
                if (parent != obj.Parent && FitsEquipment(obj, parent))
                    fitting.Add(obj);
            }
        }

        if (orders.All(order => order.Before.SequenceEqual(order.After)))
            return false;

        foreach (var obj in roots)
        {
            if (obj.Parent != null)
                obj.Parent.RemoveChild(obj);
            else
                scene.Remove(obj);
        }
        foreach (var obj in roots)
        {
            var parent = orders.Single(order => order.After.Contains(obj)).Parent;
            if (parent != null)
                parent.AddChild(obj);
            else
                scene.Add(obj);
        }
        foreach (var order in orders)
            ApplyOrder(scene, order.Parent, order.After);
        foreach (var obj in fitting)
        {
            obj.SetLocalPosition(GlmSharp.vec3.Zero);
            obj.SetLocalRotation(GlmSharp.vec3.Zero);
            obj.SetLocalScale(GlmSharp.vec3.Ones);
            obj.InheritPose = true;
        }
        return true;
    }

    public static bool CanShift(IReadOnlyList<SceneObject> scene, IEnumerable<SceneObject> objects, bool down) =>
        ShiftedOrders(scene, objects, down).Any(order => !order.Before.SequenceEqual(order.After));

    public static bool Shift(List<SceneObject> scene, IEnumerable<SceneObject> objects, bool down)
    {
        bool changed = false;
        foreach (var order in ShiftedOrders(scene, objects, down))
        {
            if (order.Before.SequenceEqual(order.After))
                continue;
            ApplyOrder(scene, order.Parent, order.After);
            changed = true;
        }
        return changed;
    }

    private static List<SiblingOrder> ShiftedOrders(IReadOnlyList<SceneObject> scene,
        IEnumerable<SceneObject> objects, bool down)
    {
        var roots = GetMoveRoots(scene, objects);
        var selected = roots.ToHashSet();
        var orders = roots.Select(obj => obj.Parent).Distinct()
            .Select(parent => new SiblingOrder(parent, parent?.Children ?? scene)).ToList();
        foreach (var order in orders)
        {
            var siblings = order.After;
            for (int step = 0; step < siblings.Count - 1; step++)
            {
                int index = down ? siblings.Count - 2 - step : step + 1;
                int next = index + (down ? 1 : -1);
                if (selected.Contains(siblings[index]) && !selected.Contains(siblings[next]))
                    (siblings[index], siblings[next]) = (siblings[next], siblings[index]);
            }
        }
        return orders;
    }

    private static void ApplyOrder(List<SceneObject> scene, SceneObject? parent, IReadOnlyList<SceneObject> order)
    {
        if (parent == null)
        {
            scene.Clear();
            scene.AddRange(order);
        }
        else
        {
            for (int i = 0; i < order.Count; i++)
                if (parent.Children[i] != order[i])
                    parent.MoveChild(order[i], i);
        }
    }

    private static SceneObject? DestinationParent(SceneObject obj, SceneObject? target, SceneDropPlacement placement)
    {
        if (placement == SceneDropPlacement.RootEnd)
            return null;
        if (placement != SceneDropPlacement.Inside)
            return target!.Parent;
        var character = target as CharacterSceneObject ?? (target as MiBoneSceneObject)?.ModelOwner;
        return FitsEquipment(obj, character) ? character : target;
    }

    private static bool FitsEquipment(SceneObject obj, SceneObject? parent) =>
        obj is CharacterSceneObject equipment && parent is CharacterSceneObject character &&
        string.Equals(obj.SpawnCategory, "Equipment", StringComparison.Ordinal) &&
        equipment.BoneObjects.Values.OfType<MiBoneSceneObject>().Any(part =>
            !string.IsNullOrEmpty(part.BoneName) &&
            character.BoneObjects.Values.OfType<MiBoneSceneObject>().Any(bone =>
                string.Equals(part.BoneName, bone.BoneName, StringComparison.Ordinal)));

    private sealed class SiblingOrder(SceneObject? parent, IEnumerable<SceneObject> children)
    {
        public SceneObject? Parent { get; } = parent;
        public List<SceneObject> Before { get; } = children.ToList();
        public List<SceneObject> After { get; } = children.ToList();
    }
}
