using GlmSharp;
using MineImatorSimplyRemade.core.mdl.mineImator;
using NumericsMatrix = System.Numerics.Matrix4x4;
using NumericsQuaternion = System.Numerics.Quaternion;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public enum CopyConstraintType { Position, Rotation, Scale }

public sealed class CopyConstraint
{
    public bool Enabled { get; set; } = true;
    public CopyConstraintType Type { get; set; }
    public string TargetObjectId { get; set; } = "";
    private vec3? _offset;
    public vec3 Offset { get => _offset ?? (Type == CopyConstraintType.Scale ? vec3.Ones : vec3.Zero); set => _offset = value; }
    public bool AxisX { get; set; } = true;
    public bool AxisY { get; set; } = true;
    public bool AxisZ { get; set; } = true;
    public float Blend { get; set; } = 1f;
    public bool BentHalf { get; set; }
    public bool Rotate { get; set; }
    internal mat4? EvaluatedInput { get; set; }
    internal vec3 EvaluatedInputScale { get; set; }
    internal mat4? EvaluatedOutput { get; set; }
    internal vec3 EvaluatedOutputScale { get; set; }

    public CopyConstraint Clone()
    {
        var copy = (CopyConstraint)MemberwiseClone();
        copy.EvaluatedInput = null;
        copy.EvaluatedOutput = null;
        return copy;
    }
}

/// <summary>World-space copy channels, evaluated without changing authored animation channels.</summary>
public static class CopyConstraintSolver
{
    internal sealed record EvaluationJob(SceneObject Owner, IReadOnlyList<SceneObject> Driven,
        IReadOnlyList<SceneObject> Sources, Action Apply, bool IsCopy = false);

    internal static void Evaluate(List<SceneObject> objects, Dictionary<string, SceneObject> ids,
        List<EvaluationJob> jobs)
    {
        foreach (var owner in objects)
        {
            var constraints = owner.CopyConstraints.Where(c => c.Enabled && float.IsFinite(c.Blend) && c.Blend > 0f &&
                ids.TryGetValue(c.TargetObjectId, out var target) && SafeHierarchy(target) &&
                SafeHierarchy(owner) && !IsWithin(target, owner)).ToArray();
            if (constraints.Length == 0) continue;
            jobs.Add(new EvaluationJob(owner, new[] { owner },
                constraints.Select(c => ids[c.TargetObjectId]).ToArray(), () =>
                {
                    mat4 world = owner.GetWorldMatrixNoPivot();
                    vec3 scale = owner.GetCopyConstraintScale(), originalScale = scale;
                    foreach (var constraint in constraints)
                    {
                        var target = ids[constraint.TargetObjectId];
                        constraint.EvaluatedInput = world;
                        constraint.EvaluatedInputScale = scale;
                        world = Apply(world, constraint, TargetFrame(target, constraint), ref scale, target.GetCopyConstraintScale());
                        constraint.EvaluatedOutput = world;
                        constraint.EvaluatedOutputScale = scale;
                    }
                    owner.SetEvaluatedCopyTransform(world, SafeRatio(scale, originalScale), scale);
                }, true));
        }

        var states = new Dictionary<EvaluationJob, int>();
        bool Visit(EvaluationJob job)
        {
            if (states.TryGetValue(job, out int state)) return state == 2;
            states[job] = 1;
            bool valid = true;
            foreach (var dependency in jobs)
            {
                if (ReferenceEquals(dependency, job)) continue;
                bool required = dependency.Driven.Any(driven =>
                    job.Sources.Any(source => IsWithin(source, driven)) ||
                    ((job.IsCopy || dependency.IsCopy) && job.Driven.Any(owner => owner.Parent != null && IsWithin(owner.Parent, driven)))) ||
                    (job.IsCopy && !dependency.IsCopy && dependency.Driven.Contains(job.Owner));
                if (required && !Visit(dependency)) valid = false;
            }
            states[job] = valid ? 2 : 3;
            if (valid) job.Apply();
            return valid;
        }
        foreach (var job in jobs) Visit(job);
    }

    public static void CaptureOffset(SceneObject owner, CopyConstraint constraint, SceneObject target)
    {
        // Preserve this stack entry's result, not a later constraint's contribution.
        mat4 world = constraint.EvaluatedOutput ?? owner.GetWorldMatrixNoPivot(), frame = TargetFrame(target, constraint);
        mat4 input = constraint.EvaluatedInput ?? world;
        float blend = float.IsFinite(constraint.Blend) && constraint.Blend > 1e-6f ? Math.Clamp(constraint.Blend, 0f, 1f) : 1f;
        switch (constraint.Type)
        {
            case CopyConstraintType.Position:
                vec3 desiredPosition = Translation(input) + (Translation(world) - Translation(input)) / blend;
                vec3 offset = desiredPosition - Translation(frame);
                constraint.Offset = constraint.Rotate ? Direction(Rotation(frame).Transposed, offset) : offset;
                break;
            case CopyConstraintType.Rotation:
                mat4 desiredRotation = AllRotationAxes(constraint)
                    ? BlendRotation(Rotation(input), Rotation(world), 1f / blend)
                    : BlendRotationAxes(Rotation(input), Rotation(world), constraint, 1f / blend);
                constraint.Offset = MineImatorLoader.MatrixToEulerRzRyRx(Rotation(frame).Transposed * desiredRotation);
                break;
            case CopyConstraintType.Scale:
                vec3 currentScale = constraint.EvaluatedOutput.HasValue ? constraint.EvaluatedOutputScale : owner.GetCopyConstraintScale();
                vec3 inputScale = constraint.EvaluatedInput.HasValue ? constraint.EvaluatedInputScale : currentScale;
                constraint.Offset = SafeRatio(inputScale + (currentScale - inputScale) / blend, target.GetCopyConstraintScale());
                break;
        }
    }

    private static mat4 TargetFrame(SceneObject target, CopyConstraint constraint)
    {
        mat4 frame = target.GetWorldMatrixNoPivot();
        if (constraint.Type != CopyConstraintType.Scale && constraint.BentHalf && target is MiBoneSceneObject bone)
            frame *= bone.GetBentHalfTransform(vec3.Zero);
        return frame;
    }

    private static mat4 Apply(mat4 world, CopyConstraint constraint, mat4 target, ref vec3 scale, vec3 targetScale)
    {
        if (!Finite(constraint.Offset)) return world;
        float blend = Math.Clamp(constraint.Blend, 0f, 1f);
        vec3 Mask(vec3 from, vec3 to) => new(constraint.AxisX ? to.x : from.x,
            constraint.AxisY ? to.y : from.y, constraint.AxisZ ? to.z : from.z);
        switch (constraint.Type)
        {
            case CopyConstraintType.Position:
                vec3 from = Translation(world);
                vec3 offset = constraint.Rotate ? Direction(Rotation(target), constraint.Offset) : constraint.Offset;
                vec3 position = from + (Mask(from, Translation(target) + offset) - from) * blend;
                if (Finite(position)) { world.m30 = position.x; world.m31 = position.y; world.m32 = position.z; }
                break;
            case CopyConstraintType.Rotation:
                mat4 current = Rotation(world);
                mat4 desired = Rotation(target) * Euler(constraint.Offset);
                mat4 blended = AllRotationAxes(constraint) ? BlendRotation(current, desired, blend)
                    : BlendRotationAxes(current, desired, constraint, blend);
                mat4 delta = blended * current.Transposed;
                vec3 positionBefore = Translation(world);
                world = delta * world;
                world.m30 = positionBefore.x; world.m31 = positionBefore.y; world.m32 = positionBefore.z;
                break;
            case CopyConstraintType.Scale:
                vec3 desiredScale = Mask(scale, targetScale * constraint.Offset);
                // A zero target scale has no finite keep-transform multiplier.
                if (MathF.Abs(targetScale.x) <= 1e-8f) desiredScale.x = scale.x;
                if (MathF.Abs(targetScale.y) <= 1e-8f) desiredScale.y = scale.y;
                if (MathF.Abs(targetScale.z) <= 1e-8f) desiredScale.z = scale.z;
                vec3 result = scale + (desiredScale - scale) * blend;
                if (Finite(result))
                {
                    mat4 fallback = Rotation(world);
                    world *= mat4.Scale(SafeRatio(result, scale));
                    for (int axis = 0; axis < 3; axis++)
                        if (MathF.Abs(scale[axis]) <= 1e-8f)
                            for (int row = 0; row < 3; row++) world[axis, row] = fallback[axis, row] * result[axis];
                    scale = result;
                }
                break;
        }
        return world;
    }

    internal static vec3 BasisScale(mat4 matrix) => new(
        new vec3(matrix.m00, matrix.m01, matrix.m02).Length,
        new vec3(matrix.m10, matrix.m11, matrix.m12).Length,
        new vec3(matrix.m20, matrix.m21, matrix.m22).Length);

    internal static mat4 Rotation(mat4 matrix)
    {
        // Orthonormalize the world basis so nonuniform ancestor scale cannot become rotation/shear.
        vec3 x = new(matrix.m00, matrix.m01, matrix.m02);
        vec3 y = new(matrix.m10, matrix.m11, matrix.m12);
        vec3 originalZ = new(matrix.m20, matrix.m21, matrix.m22);
        if (!Finite(x) || !Finite(y) || !Finite(originalZ)) return mat4.Identity;
        if (x.LengthSqr < 1e-12f)
        {
            x = vec3.Cross(y, originalZ);
            if (x.LengthSqr < 1e-12f) x = vec3.UnitX;
        }
        x = x.Normalized;
        y -= x * vec3.Dot(x, y);
        if (y.LengthSqr < 1e-12f)
        {
            y = vec3.Cross(originalZ, x);
            if (y.LengthSqr < 1e-12f)
            {
                vec3 seed = MathF.Abs(x.y) < 0.9f ? vec3.UnitY : vec3.UnitZ;
                y = seed - x * vec3.Dot(seed, x);
            }
        }
        y = y.Normalized;
        vec3 z = vec3.Cross(x, y).Normalized;
        return new mat4(new vec4(x, 0f), new vec4(y, 0f), new vec4(z, 0f), new vec4(0f, 0f, 0f, 1f));
    }

    private static mat4 BlendRotation(mat4 from, mat4 to, float blend)
    {
        NumericsQuaternion Quaternion(mat4 m) => NumericsQuaternion.CreateFromRotationMatrix(new NumericsMatrix(
            m.m00, m.m01, m.m02, 0f, m.m10, m.m11, m.m12, 0f,
            m.m20, m.m21, m.m22, 0f, 0f, 0f, 0f, 1f));
        var m = NumericsMatrix.CreateFromQuaternion(NumericsQuaternion.Normalize(
            NumericsQuaternion.Slerp(Quaternion(from), Quaternion(to), blend)));
        return new mat4(new vec4(m.M11, m.M12, m.M13, 0f), new vec4(m.M21, m.M22, m.M23, 0f),
            new vec4(m.M31, m.M32, m.M33, 0f), new vec4(0f, 0f, 0f, 1f));
    }

    private static bool AllRotationAxes(CopyConstraint constraint) => constraint.AxisX && constraint.AxisY && constraint.AxisZ;
    private static mat4 BlendRotationAxes(mat4 from, mat4 to, CopyConstraint constraint, float blend)
    {
        vec3 start = MineImatorLoader.MatrixToEulerRzRyRx(from);
        vec3 end = MineImatorLoader.MatrixToEulerRzRyRx(to);
        for (int axis = 0; axis < 3; axis++)
            if (axis == 0 ? constraint.AxisX : axis == 1 ? constraint.AxisY : constraint.AxisZ)
                start[axis] += MathF.IEEERemainder(end[axis] - start[axis], 2f * MathF.PI) * blend;
        return Euler(start);
    }

    private static mat4 Euler(vec3 value) => mat4.RotateZ(value.z) * mat4.RotateY(value.y) * mat4.RotateX(value.x);
    private static vec3 Translation(mat4 matrix) => new(matrix.m30, matrix.m31, matrix.m32);
    private static vec3 Direction(mat4 matrix, vec3 value)
    {
        vec4 result = matrix * new vec4(value, 0f);
        return new vec3(result.x, result.y, result.z);
    }
    private static vec3 SafeRatio(vec3 value, vec3 divisor) => new(
        MathF.Abs(divisor.x) > 1e-8f ? value.x / divisor.x : 1f,
        MathF.Abs(divisor.y) > 1e-8f ? value.y / divisor.y : 1f,
        MathF.Abs(divisor.z) > 1e-8f ? value.z / divisor.z : 1f);
    private static bool Finite(vec3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    private static bool SafeHierarchy(SceneObject obj)
    {
        var visited = new HashSet<SceneObject>();
        for (SceneObject? current = obj; current != null; current = current.Parent)
            if (!visited.Add(current)) return false;
        return true;
    }
    private static bool IsWithin(SceneObject obj, SceneObject ancestor)
    {
        var visited = new HashSet<SceneObject>();
        for (SceneObject? current = obj; current != null && visited.Add(current); current = current.Parent)
            if (current == ancestor) return true;
        return false;
    }
}
