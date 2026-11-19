using MineImatorSimplyRemade.core.project;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

/// <summary>Applies saved rigid body bakes to evaluated world transforms during pose evaluation.</summary>
public static class RigidBodyEvaluation
{
    private static int _suspended;

    /// <summary>True while a bake samples authored animation, which must not see previous results.</summary>
    public static bool IsSuspended => Volatile.Read(ref _suspended) > 0;

    public static IDisposable Suspend()
    {
        Interlocked.Increment(ref _suspended);
        return new Scope();
    }

    public static RigidBodyWorldSettings World => ProjectManager.Instance.Manifest.Settings.RigidBodyWorld ?? new();

    public static SceneModifier? ActiveModifier(SceneObject obj) =>
        obj.Modifiers.FirstOrDefault(modifier => modifier.Type == SceneModifierType.RigidBody && modifier.Enabled);

    /// <summary>True when the saved bake no longer matches settings or frame rate; the authored pose is shown instead.</summary>
    public static bool IsStale(SceneModifier modifier, float framerate) =>
        modifier.RigidBodyBake is { } bake && !bake.Matches(modifier.RigidBody, World, framerate);

    public static void Apply(SceneObject obj, int frame, float framerate)
    {
        if (IsSuspended || ActiveModifier(obj) is not { RigidBodyBake: { } bake } modifier ||
            !bake.Matches(modifier.RigidBody, World, framerate))
        {
            obj.SetEvaluatedRigidBodyWorld(null);
            return;
        }
        obj.SetEvaluatedRigidBodyWorld(bake.GetWorld(ModifierEvaluation.SampleFrame(obj, frame)));
    }

    private sealed class Scope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Decrement(ref _suspended);
        }
    }
}
