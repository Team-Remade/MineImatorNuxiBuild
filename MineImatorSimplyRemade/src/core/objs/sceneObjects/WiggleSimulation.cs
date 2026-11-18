using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.graphics;
using NMatrix = System.Numerics.Matrix4x4;
using NQuaternion = System.Numerics.Quaternion;
using NVector = System.Numerics.Vector3;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

/// <summary>World-space endpoint springs, kept separate from authored bone channels.</summary>
public sealed class WiggleSimulation
{
    public const int MaxBones = 256;
    private readonly SceneObject[] _objects;
    private readonly BoneSceneObject[] _bones;
    private readonly Dictionary<BoneSceneObject, SceneObject> _rigs;
    private readonly List<BoneSceneObject> _activeBones = [];
    private readonly HashSet<BoneSceneObject> _active = [], _frozen = [];
    private readonly Dictionary<BoneSceneObject, State> _states = [];
    private readonly Dictionary<BoneSceneObject, mat4> _worlds = [];
    private readonly Dictionary<MiBoneSceneObject, vec3> _bends = [];
    private readonly List<(SceneObject Owner, vec3 Center, float Radius)> _colliders = [];
    private readonly List<(SceneObject Owner, BoneSceneObject[] Drivers, ClothCollisionTree Tree)> _meshColliders = [];
    private readonly Dictionary<(SceneObject Owner, Mesh Mesh), ClothCollisionTree> _meshTrees = [];
    private readonly List<ClothTriangle> _triangles = [];

    private sealed class State
    {
        public vec3 Head, Tail, HeadVelocity, TailVelocity;
        public vec3 PreviousHead, PreviousTail, SourceHead, SourceTail;
        public mat4 Source;
        public float Length;
        public bool Initialized;
        public vec3 Bend, BendVelocity, BendTarget, DriverPoint, DriverVelocity, DriverAcceleration;
        public vec3 ContactHead, ContactTail;
    }

    public WiggleSimulation(IEnumerable<SceneObject> roots)
    {
        _objects = Objects(roots).ToArray();
        _bones = _objects.OfType<BoneSceneObject>().ToArray();
        _rigs = _bones.ToDictionary(b => b, RigFor);
        if (_bones.Count(b => b.Wiggle.Enabled) > MaxBones)
            throw new InvalidDataException($"Wiggle is limited to {MaxBones} enabled bones.");
    }

    public static IEnumerable<SceneObject> Objects(IEnumerable<SceneObject> roots)
    {
        var visited = new HashSet<SceneObject>();
        var pending = new Stack<SceneObject>(roots.Reverse());
        while (pending.TryPop(out var obj))
        {
            if (obj.IsRuntimeTransient) continue;
            if (!visited.Add(obj)) throw new InvalidDataException("Wiggle requires an acyclic, unshared scene hierarchy.");
            yield return obj;
            foreach (var child in obj.Children.AsEnumerable().Reverse()) pending.Push(child);
        }
    }

    public static SceneObject RigFor(BoneSceneObject bone)
    {
        SceneObject rig = bone;
        var visited = new HashSet<SceneObject>();
        for (SceneObject? current = bone; current != null; current = current.Parent)
        {
            if (!visited.Add(current)) throw new InvalidDataException("Wiggle bone hierarchy contains a cycle.");
            rig = current;
            if (current is CharacterSceneObject) break;
        }
        return rig;
    }

    public static bool Active(BoneSceneObject bone) =>
        bone.Wiggle.Enabled && !bone.Wiggle.Mute && RigFor(bone).WiggleRig is { Enabled: true, Mute: false };

    public void Apply()
    {
        foreach (var bone in _bones)
        {
            bone.SetEvaluatedWiggleWorld(bone.Wiggle.Enabled && !bone.Wiggle.Mute &&
                _rigs[bone].WiggleRig is { Enabled: true, Mute: false } &&
                _worlds.TryGetValue(bone, out var world) ? world : null);
            if (bone is MiBoneSceneObject mi && bone.EvaluatedWiggleWorld.HasValue)
                mi.SetEvaluatedWiggleBend(bone.Wiggle.Bend.Enabled && _bends.TryGetValue(mi, out var bend) ? bend : null);
        }
        foreach (var mi in _objects.OfType<MiBoneSceneObject>()) mi.RefreshInverseKinematicsVisuals();
    }

    public mat4 WorldFor(BoneSceneObject bone) => _worlds.TryGetValue(bone, out var world)
        ? world : throw new InvalidDataException($"No simulated pose for {bone.Name}.");

    internal void HoldWorld(BoneSceneObject bone, mat4 world) => _worlds[bone] = world;
    internal vec3 BendFor(MiBoneSceneObject bone) => _bends.TryGetValue(bone, out var bend)
        ? bend : throw new InvalidDataException($"{bone.Name}: missing simulated bend.");
    internal void HoldBend(MiBoneSceneObject bone, vec3 bend) => _bends[bone] = bend;

    public void Step(float seconds, WiggleSceneSettings scene, WindSettings wind)
    {
        scene.Validate();
        WiggleSettings.Range(seconds, 0, 1, "Simulation timestep");
        using var uncached = SceneObject.SuspendRenderEvaluation();
        foreach (var bone in _bones) bone.SetEvaluatedWiggleWorld(null);
        if (!scene.Enabled) { _worlds.Clear(); _bends.Clear(); Apply(); return; }
        _activeBones.Clear();
        _active.Clear();
        _frozen.Clear();
        foreach (var bone in _bones)
        {
            var rig = _rigs[bone].WiggleRig;
            if (!bone.Wiggle.Enabled || bone.Wiggle.Mute || !rig.Enabled || rig.Mute) continue;
            _activeBones.Add(bone);
            _active.Add(bone);
            if (bone.Wiggle.Freeze || rig.Freeze) _frozen.Add(bone);
        }
        _colliders.Clear();
        foreach (var obj in _objects.Where(o => o.WiggleCollider.Enabled))
        {
            var c = obj.WiggleCollider;
            c.Validate();
            mat4 world = obj.GetWorldMatrixNoPivot();
            vec3 center = BoneSceneObject.TransformPoint(world, new(c.OffsetX, c.OffsetY, c.OffsetZ));
            float scale = Math.Max(new vec3(world.m00, world.m01, world.m02).Length,
                Math.Max(new vec3(world.m10, world.m11, world.m12).Length, new vec3(world.m20, world.m21, world.m22).Length));
            if (!Finite(center) || !float.IsFinite(scale) || scale <= 1e-8f)
                throw new InvalidDataException($"{obj.Name}: invalid wiggle collider transform.");
            _colliders.Add((obj, center, c.Radius * scale));
        }
        foreach (var bone in _activeBones)
        {
            bone.Wiggle.Validate();
            if (bone.Wiggle.Bend.Enabled && bone is not MiBoneSceneObject { BendParameters: not null, EndOffset: not null })
                throw new InvalidDataException($"{bone.Name}: bend wiggle requires a MiBone bend joint and endpoint.");
            mat4 world = bone.GetWorldMatrixNoPivot();
            vec3 head = bone.GetWorldPosition(), tail = bone.GetWiggleEndWorldPosition();
            float length = (tail - head).Length;
            if (!Finite(head) || !Finite(tail) || !float.IsFinite(world.Determinant) ||
                MathF.Abs(world.Determinant) < 1e-10f || length < 1e-5f)
                throw new InvalidDataException($"{bone.Name}: wiggle requires a nonzero tail and nonsingular transform.");
            if (!_states.TryGetValue(bone, out var state)) _states[bone] = state = new();
            // Track an unbent material point so rotation drives inertia without bend feeding back into itself.
            vec3 driverPoint = BoneSceneObject.TransformPoint(world, bone.GetWiggleEndOffset());
            vec3 driverVelocity = seconds > 0 && state.Initialized ? (driverPoint - state.DriverPoint) / seconds : vec3.Zero;
            state.DriverAcceleration = seconds > 0 && state.Initialized ? (driverVelocity - state.DriverVelocity) / seconds : vec3.Zero;
            state.DriverPoint = driverPoint;
            state.DriverVelocity = driverVelocity;
            if (bone is MiBoneSceneObject mi) state.BendTarget = mi.GetEvaluatedEditableBendAngle();
            state.Source = world;
            state.SourceHead = head;
            state.SourceTail = tail;
            state.Length = length;
            if (!state.Initialized)
            {
                state.Head = state.PreviousHead = head;
                state.Tail = state.PreviousTail = tail;
                state.Bend = state.BendTarget;
                state.Initialized = true;
            }
            CollectMeshColliders();
        }
        int substeps = Math.Max(scene.Quality, (int)MathF.Ceiling(seconds * 120));
        float dt = seconds / substeps;
        for (int step = 0; step < substeps; step++)
        {
            float alpha = (step + 1f) / substeps;
            foreach (var bone in _activeBones)
            {
                var state = _states[bone];
                var settings = bone.Wiggle;
                if (_frozen.Contains(bone))
                {
                    if (_worlds.TryGetValue(bone, out var frozen))
                    {
                        bone.SetEvaluatedWiggleWorld(frozen);
                        if (bone is MiBoneSceneObject frozenMi && _bends.TryGetValue(frozenMi, out var heldBend))
                            frozenMi.SetEvaluatedWiggleBend(heldBend);
                        state.Head = bone.GetWorldPosition();
                        state.Tail = bone.GetWiggleEndWorldPosition();
                    }
                    else Store(bone, state);
                    continue;
                }
                bone.SetEvaluatedWiggleWorld(null);
                if (settings.Bend.Enabled && bone is MiBoneSceneObject mi)
                {
                    IntegrateBend(mi, state, dt, scene, wind);
                    mi.SetEvaluatedWiggleBend(state.Bend);
                    vec3 clamped = mi.GetEvaluatedEditableBendAngle();
                    for (int axis = 0; axis < 3; axis++)
                        if (clamped[axis] != state.Bend[axis]) state.BendVelocity[axis] = 0;
                    state.Bend = clamped;
                    _bends[mi] = clamped;
                    state.Length = (BoneSceneObject.TransformPoint(state.Source, mi.GetWiggleBendEndOffset(clamped)) - state.SourceHead).Length;
                }
                vec3 currentHead = bone.GetWorldPosition(), currentTail = bone.GetWiggleEndWorldPosition();
                vec3 headTarget = Mix(state.PreviousHead, currentHead, alpha);
                vec3 tailTarget = Mix(state.PreviousTail, currentTail, alpha);
                if (settings.Bend.Enabled) tailTarget = currentTail;
                bool connected = settings.Connected && bone.Parent is BoneSceneObject parent && _active.Contains(parent);
                if (connected) headTarget = _states[(BoneSceneObject)bone.Parent!].Tail;
                state.ContactHead = state.Head;
                state.ContactTail = state.Tail;
                if (settings.Head.Enabled && !connected)
                    Integrate(ref state.Head, ref state.HeadVelocity, headTarget, settings.Head, dt, scene, wind);
                else { state.Head = headTarget; state.HeadVelocity = vec3.Zero; }
                if (settings.Tail.Enabled)
                    Integrate(ref state.Tail, ref state.TailVelocity, tailTarget, settings.Tail, dt, scene, wind);
                else { state.Tail = tailTarget; state.TailVelocity = vec3.Zero; }
                Store(bone, state);
            }
            for (int iteration = 0; iteration < scene.Quality; iteration++)
                foreach (var bone in _activeBones)
                {
                    var s = _states[bone];
                    var w = bone.Wiggle;
                    if (_frozen.Contains(bone)) continue;
                    State? parentState = null;
                    BoneSceneObject? parentBone = bone.Parent as BoneSceneObject;
                    bool connected = w.Connected && parentBone != null && _active.Contains(parentBone);
                    if (connected) { parentState = _states[parentBone!]; s.Head = parentState.Tail; }
                    float headWeight = w.Head.Enabled && !connected ? 1 / w.Head.Mass : 0;
                    float parentWeight = connected && w.Chain && parentBone!.Wiggle.Tail.Enabled &&
                        !_frozen.Contains(parentBone) ? 1 / parentBone.Wiggle.Tail.Mass : 0;
                    headWeight += parentWeight;
                    float tailWeight = w.Tail.Enabled ? 1 / w.Tail.Mass : 0;
                    vec3 delta = s.Tail - s.Head;
                    float distance = delta.Length;
                    float stretch = Math.Max(w.Head.Stretch, w.Tail.Stretch);
                    float desired = Math.Clamp(distance, s.Length * (1 - stretch), s.Length * (1 + stretch));
                    if (distance > 1e-8f && headWeight + tailWeight > 0)
                    {
                        vec3 correction = delta * ((distance - desired) / (distance * (headWeight + tailWeight)));
                        s.Head += correction * headWeight;
                        s.Tail -= correction * tailWeight;
                        if (parentWeight > 0) parentState!.Tail = s.Head;
                    }
                    if (w.Collisions || w.MeshCollisions)
                    {
                        if (w.Head.Enabled && !connected) Collide(bone, ref s.Head, s.ContactHead, ref s.HeadVelocity);
                        if (w.Tail.Enabled) Collide(bone, ref s.Tail, s.ContactTail, ref s.TailVelocity);
                    }
                    if (parentWeight > 0) Store(parentBone!, parentState!);
                    Store(bone, s);
                }
        }
        foreach (var bone in _activeBones)
        {
            var s = _states[bone];
            // Save the driver's pose without any dynamic ancestor, not the simulated target.
            s.PreviousHead = s.SourceHead;
            s.PreviousTail = s.SourceTail;
        }
        Apply();
    }

    private static void Integrate(ref vec3 point, ref vec3 velocity, vec3 target,
        WigglePointSettings settings, float dt, WiggleSceneSettings scene, WindSettings wind)
    {
        Integrate(ref point, ref velocity, target, settings, dt, Force(settings, scene, wind));
    }

    private static vec3 Force(WigglePointSettings settings, WiggleSceneSettings scene, WindSettings wind)
    {
        vec3 force = new(0, scene.Gravity * settings.Gravity, 0);
        if (wind.Enabled)
        {
            float strength = wind.DirectionalStrength + wind.Strength * MathF.Sin(wind.Time * wind.Speed * MathF.Tau);
            force += new vec3(wind.Heading.x, 0, wind.Heading.y) * (strength * settings.Wind / settings.Mass);
        }
        return force;
    }

    private static void Integrate(ref vec3 point, ref vec3 velocity, vec3 target,
        WigglePointSettings settings, float dt, vec3 force)
    {
        float k = settings.Stiffness / settings.Mass;
        velocity = (velocity + dt * (k * (target - point) + force)) /
            (1 + dt * settings.Damping / settings.Mass + dt * dt * k);
        point += dt * velocity;
        if (!Finite(point) || !Finite(velocity))
            throw new InvalidDataException("Wiggle simulation produced nonfinite motion.");
    }

    private static void IntegrateBend(MiBoneSceneObject bone, State state, float dt, WiggleSceneSettings scene, WindSettings wind)
    {
        const float degrees = 180 / MathF.PI;
        vec3 acceleration = Force(bone.Wiggle.Bend, scene, wind) - state.DriverAcceleration;
        vec3 torque = vec3.Zero;
        for (int axis = 0; axis < 3; axis++)
        {
            vec3 offset = vec3.Zero; offset[axis] = 1;
            // Project linear acceleration onto each joint's angular tangent (per radian).
            vec3 tangent = BoneSceneObject.TransformPoint(state.Source, bone.GetWiggleBendEndOffset(state.Bend + offset)) -
                BoneSceneObject.TransformPoint(state.Source, bone.GetWiggleBendEndOffset(state.Bend - offset));
            tangent *= degrees * 0.5f;
            if (tangent.LengthSqr > 1e-10f) torque[axis] = vec3.Dot(acceleration, tangent) / tangent.LengthSqr * degrees;
        }
        Integrate(ref state.Bend, ref state.BendVelocity, state.BendTarget, bone.Wiggle.Bend, dt, torque);
    }

    private void CollectMeshColliders()
    {
        _meshColliders.Clear();
        if (!_activeBones.Any(b => b.Wiggle.MeshCollisions)) return;
        var modifiers = new ModifierEvaluation(_objects.Where(o => o.Parent == null));
        var rootBones = new Dictionary<SceneObject, Dictionary<string, BoneSceneObject>>();
        int total = 0;
        foreach (var obj in _objects)
        {
            if (!obj.GetEffectiveVisibility() || obj is CameraSceneObject or LightSceneObject or PathSceneObject or
                PathPointSceneObject or ParticleSpawnerSceneObject or ProceduralEffectSceneObject or FluidSceneObject) continue;
            if (obj is MiBoneSceneObject mi) mi.RefreshInverseKinematicsVisuals();
            mat4 source = obj.GetWorldMatrix();
            foreach (var mesh in obj.Visuals)
            {
                if (mesh.PickOnly || mesh.DepthTestDisabled) continue;
                if (mesh.Indices != null && mesh.Indices.Any(index => index >= mesh.Vertices.Count))
                    throw new InvalidDataException($"{obj.Name}: invalid wiggle collision mesh index.");
                BoneSceneObject[] drivers = [];
                if (mesh.IsSkinned)
                {
                    var root = obj;
                    while (root.Parent != null) root = root.Parent;
                    if (!rootBones.TryGetValue(root, out var bones))
                        rootBones.Add(root, bones = MeshSkinning.BuildBoneDictionary(root));
                    MeshSkinning.UpdateBoneMatrices(mesh, source, bones);
                    drivers = mesh.BoneNames.Where(bones.ContainsKey).Select(name => bones[name]).Distinct().ToArray();
                }
                IReadOnlyList<vec3> vertices = mesh.IsSkinned || mesh.HasShapeKeys || mesh.GpuBend != null
                    ? VeldridSceneRenderer.BuildVertices(mesh).Select(v => v.Position).ToArray()
                    : mesh.RenderVertices;
                _triangles.Clear();
                int count = mesh.Indices is { Length: > 0 } ? mesh.Indices.Length : vertices.Count;
                foreach (var delta in modifiers.GetDeltas(obj))
                {
                    mat4 model = delta * source;
                    for (int i = 0; i + 2 < count; i += 3)
                    {
                        int a = mesh.Indices is { Length: > 0 } ? checked((int)mesh.Indices[i]) : i;
                        int b = mesh.Indices is { Length: > 0 } ? checked((int)mesh.Indices[i + 1]) : i + 1;
                        int c = mesh.Indices is { Length: > 0 } ? checked((int)mesh.Indices[i + 2]) : i + 2;
                        if ((uint)a >= vertices.Count || (uint)b >= vertices.Count || (uint)c >= vertices.Count)
                            throw new InvalidDataException($"{obj.Name}: invalid wiggle collision mesh index.");
                        var triangle = new ClothTriangle(BoneSceneObject.TransformPoint(model, vertices[a]),
                            BoneSceneObject.TransformPoint(model, vertices[b]), BoneSceneObject.TransformPoint(model, vertices[c]));
                        if (!Finite(triangle.A) || !Finite(triangle.B) || !Finite(triangle.C))
                            throw new InvalidDataException($"{obj.Name}: nonfinite wiggle collision geometry.");
                        _triangles.Add(triangle);
                        if (++total > 200000) throw new InvalidDataException("Wiggle mesh collision exceeds 200000 triangles. Hide unused collision objects.");
                    }
                }
                if (!_meshTrees.TryGetValue((obj, mesh), out var tree) || tree.EntryCount != _triangles.Count)
                    _meshTrees[(obj, mesh)] = tree = ClothCollisionTree.ForScene(_triangles, []);
                else tree.RefitScene(_triangles, null, 0, 1);
                _meshColliders.Add((obj, drivers, tree));
            }
        }
    }

    private static bool IsSelf(SceneObject owner, BoneSceneObject bone)
    {
        for (SceneObject? ancestor = owner; ancestor != null; ancestor = ancestor.Parent)
            if (ancestor == bone) return true;
        return false;
    }

    private void Collide(BoneSceneObject bone, ref vec3 point, vec3 previous, ref vec3 velocity)
    {
        if (bone.Wiggle.MeshCollisions)
            foreach (var collider in _meshColliders)
            {
                if (IsSelf(collider.Owner, bone) || collider.Drivers.Any(driver => IsSelf(driver, bone))) continue;
                collider.Tree.CollideSphere(ref point, previous, ref velocity, bone.Wiggle.Radius, bone.Wiggle.Friction, bone.Wiggle.Bounce);
            }
        if (bone.Wiggle.Collisions) foreach (var collider in _colliders)
        {
            // A collider attached to this bone would feed its own solved transform back into itself.
            if (IsSelf(collider.Owner, bone)) continue;
            vec3 delta = point - collider.Center;
            float radius = collider.Radius + bone.Wiggle.Radius;
            float distance = delta.Length;
            if (distance >= radius) continue;
            vec3 normal = distance > 1e-8f ? delta / distance : vec3.UnitY;
            point = collider.Center + normal * radius;
            float speed = vec3.Dot(velocity, normal);
            vec3 tangent = velocity - speed * normal;
            velocity = tangent * (1 - bone.Wiggle.Friction) +
                normal * (speed < 0 ? -speed * bone.Wiggle.Bounce : speed);
        }
    }

    private void Store(BoneSceneObject bone, State state)
    {
        vec3 originalHead = state.SourceHead;
        vec3 originalTail = state.SourceTail;
        if (bone.Wiggle.Bend.Enabled && bone is MiBoneSceneObject mi)
            originalTail = BoneSceneObject.TransformPoint(state.Source, mi.GetWiggleBendEndOffset(state.Bend));
        vec3 from = originalTail - originalHead, to = state.Tail - state.Head;
        if (!Finite(to) || to.Length < 1e-6f)
            throw new InvalidDataException($"{bone.Name}: wiggle collapsed the bone; reduce stretch or collision forces.");
        mat4 rotation = Arc(from, to);
        vec3 axis = from / from.Length;
        float extension = to.Length / from.Length - 1;
        mat4 scale = mat4.Identity;
        for (int column = 0; column < 3; column++)
            for (int row = 0; row < 3; row++)
                scale[column, row] += extension * axis[column] * axis[row];
        mat4 world = mat4.Translate(state.Head) * rotation * scale * mat4.Translate(-originalHead) * state.Source;
        if (!float.IsFinite(world.Determinant) || MathF.Abs(world.Determinant) < 1e-10f)
            throw new InvalidDataException($"{bone.Name}: wiggle produced a singular transform.");
        _worlds[bone] = world;
        bone.SetEvaluatedWiggleWorld(world);
        if (bone is MiBoneSceneObject bent && bone.Wiggle.Bend.Enabled) bent.SetEvaluatedWiggleBend(state.Bend);
    }

    private static mat4 Arc(vec3 from, vec3 to)
    {
        from /= from.Length;
        to /= to.Length;
        float dot = Math.Clamp(vec3.Dot(from, to), -1, 1);
        vec3 cross = vec3.Cross(from, to);
        NQuaternion q;
        if (dot < -0.99999f)
        {
            vec3 axis = vec3.Cross(from, MathF.Abs(from.x) < 0.8f ? vec3.UnitX : vec3.UnitY);
            axis /= axis.Length;
            q = NQuaternion.CreateFromAxisAngle(new NVector(axis.x, axis.y, axis.z), MathF.PI);
        }
        else q = NQuaternion.Normalize(new NQuaternion(cross.x, cross.y, cross.z, 1 + dot));
        NMatrix m = NMatrix.CreateFromQuaternion(q);
        return new(new vec4(m.M11, m.M12, m.M13, 0), new vec4(m.M21, m.M22, m.M23, 0),
            new vec4(m.M31, m.M32, m.M33, 0), new vec4(0, 0, 0, 1));
    }

    private static vec3 Mix(vec3 a, vec3 b, float t) => a + (b - a) * t;
    private static bool Finite(vec3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
}
