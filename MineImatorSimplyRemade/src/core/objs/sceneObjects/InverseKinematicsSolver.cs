using GlmSharp;
using MineImatorSimplyRemade.core.mdl.mineImator;
using NumericsMatrix = System.Numerics.Matrix4x4;
using NumericsQuaternion = System.Numerics.Quaternion;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

/// <summary>Non-destructive, numeric CCD evaluated after animation and before any world-transform consumers.</summary>
public static class InverseKinematicsSolver
{
    private const float Degrees = 180f / MathF.PI;

    /// <summary>Owner first, then bone parents. A non-bone parent terminates the chain.</summary>
    public static List<BoneSceneObject> GetChain(BoneSceneObject owner, int length)
    {
        var result = new List<BoneSceneObject>();
        var visited = new HashSet<BoneSceneObject>();
        for (BoneSceneObject? bone = owner; bone != null && result.Count < length && visited.Add(bone);
             bone = bone.Parent as BoneSceneObject)
            result.Add(bone);
        return result;
    }

    public static IkBoneSettings? GetBoneSettings(InverseKinematicsSettings settings, BoneSceneObject bone) =>
        settings.GetBoneSettings(bone.ObjectId);

    /// <summary>Rebuilds evaluated IK from the current authored/keyframed pose on every call.</summary>
    public static void Evaluate(IEnumerable<SceneObject> roots)
    {
        using var evaluation = SceneObject.BeginRenderEvaluation();
        var objects = new List<SceneObject>();
        var visited = new HashSet<SceneObject>();
        var pending = new Stack<SceneObject>(roots);
        while (pending.TryPop(out var obj))
        {
            if (obj == null || !visited.Add(obj)) continue;
            objects.Add(obj);
            foreach (var child in obj.Children) pending.Push(child);
        }

        foreach (var obj in objects)
        {
            obj.SetEvaluatedCopyTransform(null, vec3.Ones);
            foreach (var constraint in obj.CopyConstraints)
            {
                constraint.EvaluatedInput = null;
                constraint.EvaluatedOutput = null;
            }
        }
        var bones = objects.OfType<BoneSceneObject>().ToList();
        foreach (var bone in bones) bone.ResetInverseKinematics();
        // Ambiguous IDs must not silently resolve to an arbitrary target.
        var ids = objects.Where(obj => !string.IsNullOrEmpty(obj.ObjectId))
            .GroupBy(obj => obj.ObjectId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var jobs = new List<Job>();
        foreach (var owner in bones)
        foreach (var constraint in owner.Constraints)
        {
            if (constraint == null || !constraint.Enabled || constraint.Type != BoneConstraintType.InverseKinematics ||
                constraint.InverseKinematics is not { } settings || !float.IsFinite(settings.Influence) ||
                settings.Influence <= 0f || settings.ChainLength <= 0 || settings.Iterations <= 0 ||
                string.IsNullOrEmpty(settings.TargetObjectId) || !ids.TryGetValue(settings.TargetObjectId, out var target)) continue;
            var chain = GetChain(owner, settings.ChainLength);
            if (chain.Count == 0 || !SafeHierarchy(target) || chain.Any(bone => !SafeHierarchy(bone)) ||
                chain.Any(bone => IsWithin(target, bone))) continue;
            SceneObject? pole = null;
            if (!string.IsNullOrEmpty(settings.PoleObjectId)) ids.TryGetValue(settings.PoleObjectId, out pole);
            if (pole != null && (!SafeHierarchy(pole) || chain.Any(bone => IsWithin(pole, bone)))) pole = null;
            jobs.Add(new Job(owner, chain, settings, target, pole));
        }

        // Evaluate target dependencies first; cycles are skipped instead of feeding back frame-to-frame.
        CopyConstraintSolver.Evaluate(objects, ids, jobs.Select(job =>
            new CopyConstraintSolver.EvaluationJob(job.Owner, job.Chain.Cast<SceneObject>().ToArray(),
                job.Pole == null ? new[] { job.Target } : new[] { job.Target, job.Pole }, () => Solve(job))).ToList());
        foreach (var bone in bones.OfType<MiBoneSceneObject>())
            if (SafeHierarchy(bone)) bone.RefreshInverseKinematicsVisuals();
    }

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

    private sealed class Job(BoneSceneObject owner, List<BoneSceneObject> chain,
        InverseKinematicsSettings settings, SceneObject target, SceneObject? pole)
    {
        public readonly BoneSceneObject Owner = owner;
        public readonly List<BoneSceneObject> Chain = chain;
        public readonly InverseKinematicsSettings Settings = settings;
        public readonly SceneObject Target = target;
        public readonly SceneObject? Pole = pole;
        public readonly mat4? LockedWorldOrientation = settings.LockOrientation ? owner.CaptureInverseKinematicsWorldRotation() : null;
    }

    private static void Solve(Job job)
    {
        vec3 target = job.Target.GetWorldPosition();
        vec3? pole = job.Pole?.GetWorldPosition();
        if (!Finite(target) || (pole.HasValue && !Finite(pole.Value)) || !Finite(job.Owner.GetEndWorldPosition())) return;
        // A locked effector holds its animated world orientation while the leg/arm carries it.
        // Passive intermediate links still inherit their parent normally.
        if (job.LockedWorldOrientation is { } orientation)
            job.Owner.PreserveInverseKinematicsWorldRotation(orientation);
        var rotations = job.Chain.Select(bone => bone.EvaluatedIkRotation).ToArray();
        var bends = job.Chain.Select(bone => (bone as MiBoneSceneObject)?.GetEvaluatedEditableBendAngle()).ToArray();
        var root = job.Chain[^1];
        float scale = Math.Max(1e-4f, (ChainEndpoint(job) - root.GetWorldPosition()).Length);
        float Score()
        {
            vec3 endpoint = job.Owner.GetEndWorldPosition();
            if (!Finite(endpoint)) return float.PositiveInfinity;
            float score = (endpoint - target).LengthSqr;
            if (pole.HasValue)
                score += PoleError(job, target, pole.Value) * scale * scale * 0.00001f;
            return float.IsFinite(score) ? score : float.PositiveInfinity;
        }
        bool Settled() => (job.Owner.GetEndWorldPosition() - target).LengthSqr <= scale * scale * 1e-10f &&
            (!pole.HasValue || PoleError(job, target, pole.Value) < 1e-5f);

        float initialScore = Score();
        if (!float.IsFinite(initialScore)) return;
        // A perfectly straight chain has zero CCD gradient toward a nearer axial target.
        // Seed a real distal joint (including an internal bend), not an authored channel.
        if (initialScore > scale * scale * 1e-8f)
        {
            SeedJoint(job, target);
            // Align the seeded chain before the distal CCD pass, otherwise that
            // first pass would immediately erase the seed on an axial target.
            if (CanRotate(job, root))
            {
                ApplyShortestArc(job, root, target, Score);
                if (pole.HasValue) ApplyPole(job, target, pole.Value, Score);
                for (int axis = 0; axis < 3 && !Settled(); axis++)
                {
                    mat4 start = root.EvaluatedIkRotation;
                    int component = axis;
                    Optimize(0f, -MathF.PI, MathF.PI, value =>
                        root.SetEvaluatedIkRotation(start * AxisRotation(component, value)), Score);
                }
            }
        }

        int iterations = Math.Clamp(job.Settings.Iterations, 1, 256);
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            if (Settled()) break;
            float before = Score();
            foreach (var bone in job.Chain)
            {
                if (Settled()) break;
                var control = GetBoneSettings(job.Settings, bone);
                if (control?.AllowBend != false && (bone != job.Owner || job.Owner.EndOffset.HasValue) &&
                    bone is MiBoneSceneObject mi && mi.BendParameters is { } bp)
                {
                    for (int axis = 0; axis < 3; axis++)
                    {
                        if (!(axis == 0 ? bp.AxisX : axis == 1 ? bp.AxisY : bp.AxisZ)) continue;
                        vec3 start = mi.GetEvaluatedEditableBendAngle();
                        int component = axis;
                        float min = Component(bp.DirectionMin, axis), max = Component(bp.DirectionMax, axis);
                        if (!float.IsFinite(min) || !float.IsFinite(max)) continue;
                        Optimize(Component(start, axis), Math.Min(min, max), Math.Max(min, max), value =>
                            mi.SetEvaluatedIkBend(WithComponent(start, component, value)), Score);
                    }
                }
                if (!CanRotate(job, bone)) continue;
                ApplyShortestArc(job, bone, target, Score);
                if (pole.HasValue) ApplyPole(job, target, pole.Value, Score);
                for (int axis = 0; axis < 3 && !Settled(); axis++)
                {
                    mat4 start = bone.EvaluatedIkRotation;
                    int component = axis;
                    Optimize(0f, -MathF.PI, MathF.PI, value =>
                        bone.SetEvaluatedIkRotation(start * AxisRotation(component, value)), Score);
                }
            }
            if (pole.HasValue) ApplyPole(job, target, pole.Value, Score);
            float after = Score();
            if (iteration > 1 && MathF.Abs(before - after) < scale * scale * 1e-10f) break;
        }

        bool accepted = Score() <= initialScore + scale * scale * 1e-9f;
        float influence = accepted ? Math.Clamp(job.Settings.Influence, 0f, 1f) : 0f;
        for (int index = 0; index < job.Chain.Count; index++)
        {
            var bone = job.Chain[index];
            bone.SetEvaluatedIkRotation(BlendRotation(rotations[index], bone.EvaluatedIkRotation, influence));
            if ((bone != job.Owner || job.Owner.EndOffset.HasValue) && bone is MiBoneSceneObject mi &&
                bends[index] is { } bend && GetBoneSettings(job.Settings, bone)?.AllowBend != false)
            {
                vec3 solved = mi.GetEvaluatedEditableBendAngle();
                mi.SetEvaluatedIkBend(influence <= 0f || !Finite(solved) ? bend : bend + (solved - bend) * influence);
            }
        }
    }

    private static void SeedJoint(Job job, vec3 target)
    {
        float SeedAngle(vec3 joint)
        {
            vec3 origin = job.Chain[^1].GetWorldPosition();
            float a = (joint - origin).Length, b = (ChainEndpoint(job) - joint).Length;
            if (a * b < 1e-10f) return 0.1f;
            float cosine = ((ChainTarget(job, target) - origin).LengthSqr - a * a - b * b) / (2f * a * b);
            // An out-of-reach target needs a straight chain, not a singularity-escape bend.
            if (cosine >= 1f) return 0f;
            return Math.Clamp(MathF.Acos(Math.Clamp(cosine, -1f, 1f)), 0.01f, MathF.PI - 0.01f);
        }
        foreach (var bone in job.Chain)
        {
            var control = GetBoneSettings(job.Settings, bone);
            if (control?.AllowBend != false && (bone != job.Owner || job.Owner.EndOffset.HasValue) &&
                bone is MiBoneSceneObject mi && mi.BendParameters is { } bp)
            {
                vec3 angle = mi.GetEvaluatedEditableBendAngle();
                for (int axis = 0; axis < 3; axis++)
                {
                    if (!(axis == 0 ? bp.AxisX : axis == 1 ? bp.AxisY : bp.AxisZ)) continue;
                    float value = Component(angle, axis);
                    if (MathF.Abs(value) > 0.01f) return;
                    float max = Component(bp.DirectionMax, axis), min = Component(bp.DirectionMin, axis);
                    float seed = SeedAngle(BoneSceneObject.TransformPoint(mi.GetWorldMatrixNoPivot(),
                        BendHelper.GetBendPivot(bp, vec3.Zero))) * Degrees;
                    mi.SetEvaluatedIkBend(WithComponent(angle, axis, max > 0f ? Math.Min(seed, max) : Math.Max(-seed, min)));
                    return;
                }
            }
            if (CanRotate(job, bone) && bone != job.Chain[^1] &&
                (ChainEndpoint(job) - bone.GetWorldPosition()).LengthSqr > 1e-10f)
            {
                vec3 direction = ChainEndpoint(job) - bone.GetWorldPosition();
                vec4 local = bone.GetWorldMatrixNoPivot().Inverse * new vec4(direction, 0f);
                vec3 localDirection = new(local.x, local.y, local.z);
                vec3 axis = Cross(localDirection, MathF.Abs(localDirection.x) < MathF.Abs(localDirection.z) ? vec3.UnitX : vec3.UnitZ);
                if (!Finite(axis) || axis.LengthSqr < 1e-12f) axis = vec3.UnitX;
                bone.SetEvaluatedIkRotation(bone.EvaluatedIkRotation * Rotation(axis / axis.Length, SeedAngle(bone.GetWorldPosition())));
                return;
            }
        }
    }

    // Global angular sampling escapes folded/straight singularities; golden-section
    // refinement makes this numeric CCD independent of model bend axes and scale.
    private static void Optimize(float current, float min, float max, Action<float> apply, Func<float> score)
    {
        if (max - min < 1e-6f) return;
        const int samples = 8;
        float step = (max - min) / samples;
        float best = current, bestScore = score();
        // Keep the existing pose on flat objectives instead of introducing free
        // axial twist merely because floating-point roundoff made one sample smaller.
        bool Better(float value) => float.IsFinite(value) && (!float.IsFinite(bestScore) ||
            value < bestScore - Math.Max(1e-12f, MathF.Abs(bestScore) * 1e-6f));
        for (int sample = 0; sample <= samples; sample++)
        {
            float candidate = min + step * sample;
            apply(candidate);
            float value = score();
            if (Better(value)) { best = candidate; bestScore = value; }
        }
        float low = Math.Max(min, best - step), high = Math.Min(max, best + step);
        const float ratio = 0.61803398875f;
        float a = high - (high - low) * ratio, b = low + (high - low) * ratio;
        apply(a); float fa = score();
        apply(b); float fb = score();
        for (int iteration = 0; iteration < 15; iteration++)
        {
            if (fa < fb)
            {
                high = b; b = a; fb = fa; a = high - (high - low) * ratio;
                apply(a); fa = score();
            }
            else
            {
                low = a; a = b; fa = fb; b = low + (high - low) * ratio;
                apply(b); fb = score();
            }
        }
        if (Better(fa)) { best = a; bestScore = fa; }
        if (Better(fb)) best = b;
        apply(best);
    }

    private static vec3 PoleJoint(Job job)
    {
        // The internal bend pivot is the knee/elbow, not the part origin or foot.
        for (int index = job.Chain.Count - 1; index >= 0; index--)
            if (job.Chain[index] is MiBoneSceneObject mi && mi.BendParameters is { } bp)
                return BoneSceneObject.TransformPoint(mi.GetWorldMatrixNoPivot(), BendHelper.GetBendPivot(bp, vec3.Zero));
        return job.Chain.Count > 1 ? job.Chain[^2].GetWorldPosition() : job.Owner.GetEndWorldPosition();
    }

    private static vec3 ChainEndpoint(Job job) =>
        job.Settings.LockOrientation ? job.Owner.GetWorldPosition() : job.Owner.GetEndWorldPosition();

    private static vec3 ChainTarget(Job job, vec3 target) => job.Settings.LockOrientation
        ? target - (job.Owner.GetEndWorldPosition() - job.Owner.GetWorldPosition())
        : target;

    private static float PoleError(Job job, vec3 target, vec3 pole)
    {
        vec3 origin = job.Chain[^1].GetWorldPosition(), axis = ChainTarget(job, target) - origin;
        if (axis.LengthSqr < 1e-12f) return 0f;
        axis /= axis.Length;
        vec3 desired = Project(pole - origin, axis), current = Project(PoleJoint(job) - origin, axis);
        if (desired.LengthSqr < 1e-12f || current.LengthSqr < 1e-12f) return 0f;
        desired = RotateVector(desired / desired.Length, axis, PoleAngle(job));
        return 1f - Math.Clamp(Dot(current / current.Length, desired), -1f, 1f);
    }

    private static bool CanRotate(Job job, BoneSceneObject bone) =>
        !(job.Settings.LockOrientation && bone == job.Owner) && GetBoneSettings(job.Settings, bone)?.AllowRotation != false;

    private static void ApplyPole(Job job, vec3 target, vec3 pole, Func<float> score)
    {
        var root = job.Chain[^1];
        if (!CanRotate(job, root)) return;
        // The locked tip offset does not rotate with the chain. Its pole plane must
        // pass through the requested ankle/wrist position, not the tip target.
        vec3 axis = ChainTarget(job, target) - root.GetWorldPosition();
        if (axis.LengthSqr < 1e-12f) return;
        axis /= axis.Length;
        vec3 current = Project(PoleJoint(job) - root.GetWorldPosition(), axis);
        vec3 desired = Project(pole - root.GetWorldPosition(), axis);
        if (current.LengthSqr < 1e-12f || desired.LengthSqr < 1e-12f) return;
        desired = RotateVector(desired, axis, PoleAngle(job));
        float angle = MathF.Atan2(Dot(axis, Cross(current, desired)), Dot(current, desired));
        mat4 start = root.EvaluatedIkRotation;
        vec4 local = root.GetWorldMatrixNoPivot().Inverse * new vec4(axis, 0f);
        vec3 localAxis = new(local.x, local.y, local.z);
        if (!Finite(localAxis) || localAxis.LengthSqr < 1e-12f) return;
        localAxis /= localAxis.Length;
        float before = score();
        root.SetEvaluatedIkRotation(start * Rotation(localAxis, angle));
        if (score() > before) root.SetEvaluatedIkRotation(start);
    }

    private static mat4 BlendRotation(mat4 from, mat4 to, float influence)
    {
        if (influence <= 0f) return from;
        if (influence >= 1f) return to;
        NumericsQuaternion Quaternion(mat4 m) => NumericsQuaternion.CreateFromRotationMatrix(new NumericsMatrix(
            m.m00, m.m01, m.m02, 0f, m.m10, m.m11, m.m12, 0f,
            m.m20, m.m21, m.m22, 0f, 0f, 0f, 0f, 1f));
        var matrix = NumericsMatrix.CreateFromQuaternion(NumericsQuaternion.Normalize(
            NumericsQuaternion.Slerp(Quaternion(from), Quaternion(to), influence)));
        return new mat4(new vec4(matrix.M11, matrix.M12, matrix.M13, 0f),
            new vec4(matrix.M21, matrix.M22, matrix.M23, 0f),
            new vec4(matrix.M31, matrix.M32, matrix.M33, 0f), new vec4(0f, 0f, 0f, 1f));
    }

    private static float PoleAngle(Job job) => float.IsFinite(job.Settings.PoleAngle) ? job.Settings.PoleAngle / Degrees : 0f;

    private static void ApplyShortestArc(Job job, BoneSceneObject bone, vec3 target, Func<float> score)
    {
        vec3 origin = bone.GetWorldPosition();
        vec3 from = ChainEndpoint(job) - origin, to = ChainTarget(job, target) - origin;
        if (from.LengthSqr < 1e-12f || to.LengthSqr < 1e-12f) return;
        from /= from.Length;
        to /= to.Length;
        float cosine = Math.Clamp(Dot(from, to), -1f, 1f);
        vec3 axis = Cross(from, to);
        float sine = axis.Length;
        if (sine < 1e-7f)
        {
            if (cosine >= 0f) return;
            // Antiparallel vectors have no unique minimum-roll plane. Choose a
            // stable orthogonal axis rather than normalizing the zero cross product.
            axis = Cross(from, MathF.Abs(from.x) < 0.9f ? vec3.UnitX : vec3.UnitY);
        }
        axis /= axis.Length;
        vec4 local = bone.GetWorldMatrixNoPivot().Inverse * new vec4(axis, 0f);
        vec3 localAxis = new(local.x, local.y, local.z);
        float localLength = localAxis.Length;
        if (!Finite(localAxis) || !float.IsFinite(localLength) || localLength < 1e-6f) return;
        mat4 start = bone.EvaluatedIkRotation;
        float before = score();
        bone.SetEvaluatedIkRotation(start * Rotation(localAxis / localLength, MathF.Atan2(sine, cosine)));
        // Uniform inherited frames give the exact world-space shortest arc.
        // Nonuniform scale/inheritance overrides may not; retain numeric CCD as fallback.
        if (!(score() < before)) bone.SetEvaluatedIkRotation(start);
    }

    private static float Component(vec3 value, int axis) => axis == 0 ? value.x : axis == 1 ? value.y : value.z;
    private static vec3 WithComponent(vec3 value, int axis, float component)
    {
        if (axis == 0) value.x = component;
        else if (axis == 1) value.y = component;
        else value.z = component;
        return value;
    }
    private static bool Finite(vec3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    private static float Dot(vec3 a, vec3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
    private static vec3 Cross(vec3 a, vec3 b) => new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    private static vec3 Project(vec3 value, vec3 normal) => value - normal * Dot(value, normal);
    private static vec3 RotateVector(vec3 vector, vec3 axis, float angle) =>
        vector * MathF.Cos(angle) + Cross(axis, vector) * MathF.Sin(angle) + axis * Dot(axis, vector) * (1f - MathF.Cos(angle));
    private static mat4 AxisRotation(int axis, float angle) =>
        axis == 0 ? mat4.RotateX(angle) : axis == 1 ? mat4.RotateY(angle) : mat4.RotateZ(angle);
    private static mat4 Rotation(vec3 axis, float angle) => new(
        new vec4(RotateVector(new vec3(1f, 0f, 0f), axis, angle), 0f),
        new vec4(RotateVector(new vec3(0f, 1f, 0f), axis, angle), 0f),
        new vec4(RotateVector(new vec3(0f, 0f, 1f), axis, angle), 0f), new vec4(0f, 0f, 0f, 1f));
}
