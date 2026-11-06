using GlmSharp;
using MineImatorSimplyRemade.core.mdl.mineImator;
using NumericsMatrix = System.Numerics.Matrix4x4;
using NumericsQuaternion = System.Numerics.Quaternion;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public enum CopyConstraintType { Position, Rotation, Scale, RotationToBend }

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
    public int SourceRotationAxis { get; set; }
    public int BendAxis { get; set; }
    public float RotationMultiplier { get; set; } = 1f;
    public bool UseBendAxisMask { get; set; }
    public int? SourceRotationAxisX { get; set; }
    public int? SourceRotationAxisY { get; set; }
    public int? SourceRotationAxisZ { get; set; }
    internal mat4? EvaluatedInput { get; set; }
    internal vec3 EvaluatedInputScale { get; set; }
    internal mat4? EvaluatedOutput { get; set; }
    internal vec3 EvaluatedOutputScale { get; set; }
    internal vec3? EvaluatedInputBend { get; set; }
    internal vec3? EvaluatedOutputBend { get; set; }

    public bool IsBendAxisEnabled(int axis) => UseBendAxisMask
        ? axis == 0 ? AxisX : axis == 1 ? AxisY : AxisZ
        : axis == BendAxis;

    public void SetBendAxisEnabled(int axis, bool enabled)
    {
        // Projects saved with a single bend axis retain their original mapping until edited.
        if (!UseBendAxisMask)
        {
            AxisX = BendAxis == 0;
            AxisY = BendAxis == 1;
            AxisZ = BendAxis == 2;
            UseBendAxisMask = true;
        }
        if (axis == 0) AxisX = enabled;
        else if (axis == 1) AxisY = enabled;
        else if (axis == 2) AxisZ = enabled;
    }

    public int GetSourceRotationAxis(int axis) =>
        (axis == 0 ? SourceRotationAxisX : axis == 1 ? SourceRotationAxisY : SourceRotationAxisZ)
        ?? (axis == BendAxis ? SourceRotationAxis : axis);

    public CopyConstraint Clone()
    {
        var copy = (CopyConstraint)MemberwiseClone();
        copy.EvaluatedInput = null;
        copy.EvaluatedOutput = null;
        copy.EvaluatedInputBend = null;
        copy.EvaluatedOutputBend = null;
        return copy;
    }
}

/// <summary>World-space copy channels, evaluated without changing authored animation channels.</summary>
public static class CopyConstraintSolver
{
    internal sealed record EvaluationJob(SceneObject Owner, IReadOnlyList<SceneObject> Driven,
        IReadOnlyList<SceneObject> Sources, Action Apply, bool IsCopy = false, bool IsPath = false);

    internal static void Evaluate(List<SceneObject> objects, Dictionary<string, SceneObject> ids,
        List<EvaluationJob> jobs)
    {
        foreach (var owner in objects)
        {
            var constraints = owner.CopyConstraints.Where(c => c.Enabled && float.IsFinite(c.Blend) && c.Blend > 0f &&
                (c.Type != CopyConstraintType.RotationToBend || owner is MiBoneSceneObject { BendParameters: not null }) &&
                ids.TryGetValue(c.TargetObjectId, out var target) && SafeHierarchy(target) &&
                SafeHierarchy(owner) && !IsWithin(target, owner)).ToArray();
            if (constraints.Length == 0) continue;
            var sources = constraints.Select(c => ids[c.TargetObjectId]).ToList();
            if (owner is MiBoneSceneObject bendOwner && constraints.Any(c => c.Type == CopyConstraintType.RotationToBend))
                sources.AddRange(bendOwner.GetBendEvaluationSources());
            jobs.Add(new EvaluationJob(owner, new[] { owner },
                sources, () =>
                {
                    mat4 world = owner.GetWorldMatrixNoPivot();
                    vec3 scale = owner.GetCopyConstraintScale(), originalScale = scale;
                    foreach (var constraint in owner.CopyConstraints)
                    {
                        if (owner is MiBoneSceneObject inputBone)
                            constraint.EvaluatedInputBend = constraint.EvaluatedOutputBend = inputBone.GetEvaluatedEditableBendAngle();
                        // Cache the stack-local pose even for inactive entries, for keep-pose activation.
                        if (!constraints.Contains(constraint)) continue;
                        var target = ids[constraint.TargetObjectId];
                        constraint.EvaluatedInput = world;
                        constraint.EvaluatedInputScale = scale;
                        mat4 frame = TargetFrame(target, constraint);
                        if (constraint.Type == CopyConstraintType.RotationToBend && owner is MiBoneSceneObject bone)
                        {
                            vec3 bend = bone.GetEvaluatedEditableBendAngle();
                            constraint.EvaluatedInputBend = bend;
                            if (float.IsFinite(constraint.RotationMultiplier) && Finite(constraint.Offset))
                            {
                                vec3 rotation = SourceRotationDegrees(frame);
                                for (int axis = 0; axis < 3; axis++)
                                {
                                    int sourceAxis = constraint.GetSourceRotationAxis(axis);
                                    if (!constraint.IsBendAxisEnabled(axis) || sourceAxis is < 0 or > 2) continue;
                                    float desired = rotation[sourceAxis] * constraint.RotationMultiplier + constraint.Offset[axis];
                                    bend[axis] += (desired - bend[axis]) * Math.Clamp(constraint.Blend, 0f, 1f);
                                }
                                if (Finite(bend)) bone.SetEvaluatedBend(bend);
                            }
                            constraint.EvaluatedOutputBend = bone.GetEvaluatedEditableBendAngle();
                        }
                        else
                            world = Apply(world, constraint, frame, ref scale, target.GetCopyConstraintScale());
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
                    ((job.IsCopy || dependency.IsCopy || job.IsPath || dependency.IsPath) &&
                        job.Driven.Any(owner => owner.Parent != null && IsWithin(owner.Parent, driven)))) ||
                    (job.IsCopy && !dependency.IsCopy && dependency.Driven.Contains(job.Owner)) ||
                    (dependency.IsPath && job.Driven.Any(owner => IsWithin(owner, dependency.Owner)));
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
            case CopyConstraintType.RotationToBend when owner is MiBoneSceneObject bone:
                vec3 currentBend = constraint.EvaluatedOutputBend ?? bone.GetEvaluatedEditableBendAngle();
                vec3 inputBend = constraint.EvaluatedInputBend ?? currentBend;
                vec3 bendOffset = constraint.Offset;
                vec3 sourceRotation = SourceRotationDegrees(frame);
                for (int axis = 0; axis < 3; axis++)
                {
                    int sourceAxis = constraint.GetSourceRotationAxis(axis);
                    if (!constraint.IsBendAxisEnabled(axis) || sourceAxis is < 0 or > 2) continue;
                    bendOffset[axis] = inputBend[axis] + (currentBend[axis] - inputBend[axis]) / blend
                        - sourceRotation[sourceAxis] * constraint.RotationMultiplier;
                }
                if (Finite(bendOffset)) constraint.Offset = bendOffset;
                break;
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

    private static vec3 SourceRotationDegrees(mat4 frame) =>
        MineImatorLoader.MatrixToEulerRzRyRx(BendSourceRotation(frame)) * (180f / MathF.PI);

    private static mat4 BendSourceRotation(mat4 frame)
    {
        vec3 x = new(frame.m00, frame.m01, frame.m02);
        vec3 y = new(frame.m10, frame.m11, frame.m12);
        vec3 z = new(frame.m20, frame.m21, frame.m22);
        if (!Finite(x) || !Finite(y) || !Finite(z) || vec3.Dot(x, vec3.Cross(y, z)) <= 1e-12f)
            return Rotation(frame);
        // Polar decomposition removes scale on either side of the rotation. The engine
        // normally scales before rotating, so normalizing columns would distort the driver.
        mat4 result = new(new vec4(x, 0f), new vec4(y, 0f), new vec4(z, 0f), new vec4(0f, 0f, 0f, 1f));
        for (int iteration = 0; iteration < 32; iteration++)
        {
            mat4 next = (result + result.Inverse.Transposed) * 0.5f;
            float difference = 0f;
            for (int column = 0; column < 3; column++)
                for (int row = 0; row < 3; row++)
                {
                    if (!float.IsFinite(next[column, row])) return Rotation(frame);
                    difference = MathF.Max(difference, MathF.Abs(next[column, row] - result[column, row]));
                }
            result = next;
            if (difference < 1e-6f) break;
        }
        return Rotation(result);
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
