using GlmSharp;
using MineImatorSimplyRemade.core.mdl.meshes;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public enum SceneModifierType { Array, FollowPath }
public enum ArrayAxis { X, Y, Z }

public sealed record SceneModifier
{
    public const int MaxInstances = 1000;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public SceneModifierType Type { get; init; }
    public bool Enabled { get; init; } = true;
    public int Count { get; init; } = 2;
    public ArrayAxis Axis { get; init; }
    public float Distance { get; init; } = 1f;
    public float RotationX { get; init; }
    public float RotationY { get; init; }
    public float RotationZ { get; init; }
    public float ScaleX { get; init; } = 1f;
    public float ScaleY { get; init; } = 1f;
    public float ScaleZ { get; init; } = 1f;
    public string PathObjectId { get; init; } = "";
    public float PathOffset { get; init; }

    public SceneModifier Validated()
    {
        if (!Guid.TryParseExact(Id, "N", out _) || !Enum.IsDefined(Type) || PathObjectId == null || !float.IsFinite(PathOffset))
            throw new InvalidDataException("Modifiers require a valid type, path reference and finite path offset.");
        if (Type == SceneModifierType.FollowPath) return this;
        if (!Enum.IsDefined(Axis) || Count is < 1 or > MaxInstances || !float.IsFinite(Distance) ||
            !float.IsFinite(RotationX) || !float.IsFinite(RotationY) || !float.IsFinite(RotationZ) ||
            !float.IsFinite(ScaleX) || !float.IsFinite(ScaleY) || !float.IsFinite(ScaleZ))
            throw new InvalidDataException("Array modifiers require valid axes, finite transforms and a count from 1 to 1000.");
        int last = Count - 1;
        if (!float.IsFinite(Distance * last) || !float.IsFinite(RotationX * last) ||
            !float.IsFinite(RotationY * last) || !float.IsFinite(RotationZ * last) ||
            !float.IsFinite(MathF.Pow(ScaleX, last)) || !float.IsFinite(MathF.Pow(ScaleY, last)) ||
            !float.IsFinite(MathF.Pow(ScaleZ, last)))
            throw new InvalidDataException("Array distance, rotation or compounded scale exceeds the supported numeric range. Reduce the count or per-copy transform.");
        return this;
    }
}

/// <summary>World-space deltas shared by all geometry passes, without changing scene objects or bone poses.</summary>
public sealed class ModifierEvaluation
{
    private static readonly mat4[] Identity = [mat4.Identity];
    private readonly Dictionary<string, SceneObject> _ids = new(StringComparer.Ordinal);
    private readonly Dictionary<SceneObject, IReadOnlyList<mat4>> _deltas = new();

    public ModifierEvaluation(IEnumerable<SceneObject> roots)
    {
        foreach (var root in roots) Collect(root);
        void Collect(SceneObject obj)
        {
            if (!string.IsNullOrEmpty(obj.ObjectId)) _ids.TryAdd(obj.ObjectId, obj);
            foreach (var child in obj.Children) Collect(child);
        }
    }

    public IReadOnlyList<mat4> GetDeltas(SceneObject obj)
    {
        if (_deltas.TryGetValue(obj, out var cached)) return cached;
        IReadOnlyList<mat4> result = obj.Parent == null ? Identity : GetDeltas(obj.Parent);
        foreach (var modifier in obj.Modifiers)
        {
            modifier.Validated();
            if (!modifier.Enabled || modifier.Type == SceneModifierType.FollowPath) continue;
            PathSceneObject? path = null;
            IReadOnlyList<PathSample> samples = [];
            string pathId = modifier.PathObjectId;
            if (pathId.Length == 0 && obj.IsFollowingPath) pathId = obj.PathFollowObjectId;
            float startOffset = obj.IsFollowingPath && pathId == obj.PathFollowObjectId ? obj.PathFollowOffset : 0f;
            if (pathId.Length != 0)
            {
                // Deleted, incomplete or self-dependent paths retain the source until repaired in the editor.
                if (!_ids.TryGetValue(pathId, out var target) || target is not PathSceneObject candidate ||
                    IsWithin(candidate, obj) || candidate.Samples.Count < 2) continue;
                path = candidate;
                samples = path.Samples;
            }
            mat4 pathWorld = path?.GetWorldMatrixNoPivot() ?? mat4.Identity;

            mat4 world = obj.GetWorldMatrixNoPivot();
            vec3 position = new(world.m30, world.m31, world.m32);
            mat4 anchor = mat4.Translate(position) * CopyConstraintSolver.Rotation(world);
            mat4 inverse = anchor.Inverse;
            vec3 axis = modifier.Axis switch { ArrayAxis.X => vec3.UnitX, ArrayAxis.Y => vec3.UnitY, _ => vec3.UnitZ };
            mat4 alignment = modifier.Axis switch
            {
                ArrayAxis.X => mat4.RotateZ(MathF.PI / 2f),
                ArrayAxis.Z => mat4.RotateX(-MathF.PI / 2f),
                _ => mat4.Identity
            };
            var expanded = new List<mat4>(Math.Min(SceneModifier.MaxInstances, result.Count * modifier.Count));
            foreach (var inherited in result)
            {
                for (int i = 0; i < modifier.Count && expanded.Count < SceneModifier.MaxInstances; i++)
                {
                    mat4 rotation = mat4.RotateZ(glm.Radians(modifier.RotationZ * i)) *
                        mat4.RotateY(glm.Radians(modifier.RotationY * i)) *
                        mat4.RotateX(glm.Radians(modifier.RotationX * i));
                    mat4 scale = mat4.Scale(new vec3(MathF.Pow(modifier.ScaleX, i),
                        MathF.Pow(modifier.ScaleY, i), MathF.Pow(modifier.ScaleZ, i)));
                    mat4 frame;
                    if (path != null)
                    {
                        float distance = startOffset + modifier.Distance * i;
                        if (!float.IsFinite(distance))
                            throw new InvalidDataException($"Array path offset on '{obj.Name}' exceeds the supported numeric range.");
                        var sample = PathGeometry.AtDistance(samples, distance, path.Settings.Closed);
                        frame = pathWorld * sample.Frame * alignment * mat4.Scale(new vec3(sample.Scale));
                    }
                    else frame = anchor * mat4.Translate(axis * (modifier.Distance * i));
                    mat4 delta = inherited * frame * rotation * scale * inverse;
                    if (!Finite(delta))
                        throw new InvalidDataException($"Array modifier on '{obj.Name}' exceeds the supported numeric range.");
                    expanded.Add(delta);
                }
                if (expanded.Count == SceneModifier.MaxInstances) break;
            }
            result = expanded;
        }
        _deltas.Add(obj, result);
        return result;
    }

    public static bool IsWithin(SceneObject node, SceneObject ancestor)
    {
        for (SceneObject? current = node; current != null; current = current.Parent)
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    private static bool Finite(mat4 m) =>
        float.IsFinite(m.m00) && float.IsFinite(m.m01) && float.IsFinite(m.m02) && float.IsFinite(m.m03) &&
        float.IsFinite(m.m10) && float.IsFinite(m.m11) && float.IsFinite(m.m12) && float.IsFinite(m.m13) &&
        float.IsFinite(m.m20) && float.IsFinite(m.m21) && float.IsFinite(m.m22) && float.IsFinite(m.m23) &&
        float.IsFinite(m.m30) && float.IsFinite(m.m31) && float.IsFinite(m.m32) && float.IsFinite(m.m33);
}
