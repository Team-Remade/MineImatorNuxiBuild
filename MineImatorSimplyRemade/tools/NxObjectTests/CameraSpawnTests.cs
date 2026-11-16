using System.Reflection;
using GlmSharp;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private static void CameraSpawnTransforms(Fixture fixture)
    {
        var viewport = new Viewport();
        var preferences = new PreferencesPanel();
        var spawn = new SpawnMenu { Viewport = viewport, PreferencesPanel = preferences };
        typeof(SpawnMenu).GetField("_placeWithCursor", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(spawn, false);

        foreach (bool copyWorkCamera in new[] { false, true })
        foreach (var (target, yaw, pitch, distance) in new[]
        {
            (new vec3(12f, -3f, 8f), 1.2f, -.35f, 7f),
            (new vec3(-5f, 9f, -2f), -.7f, .6f, 3f),
            (vec3.Zero, 0f, 0f, 0f)
        })
        {
            preferences.CopyWorkCameraIntoNewCameras = copyWorkCamera;
            viewport.Camera.Target = target;
            viewport.Camera.Yaw = yaw;
            viewport.Camera.Pitch = pitch;
            viewport.Camera.Distance = distance;
            vec3 expectedPosition = viewport.Camera.Position;
            var expectedRotation = new vec3(-pitch, yaw, 0f);
            CameraSceneObject? camera = null;
            spawn.SpawnWithCursor(() => camera = spawn.SpawnCameraObject("Camera"));

            Check(camera != null && viewport.SceneObjects.Contains(camera), "Camera spawns immediately without cursor placement");
            Near(expectedPosition, camera!.Position, "Spawned position matches the work camera eye");
            Near(expectedRotation, camera.Rotation, "Spawned rotation matches the work camera orientation");
            Near(expectedPosition, camera.LocalPosition, "Properties position is correct before any transform edit");
            Near(expectedRotation, camera.LocalRotation, "Properties rotation is correct before any transform edit");
            Near(expectedPosition, camera.ViewCamera.Position, "Spawned view eye stays in sync");
            Near(yaw, camera.ViewCamera.Yaw, "Spawned view yaw stays in sync");
            Near(pitch, camera.ViewCamera.Pitch, "Spawned view pitch stays in sync");

            camera.UpdateVisualPosition();
            camera.SyncCameraToTransform();
            Near(expectedPosition, camera.ViewCamera.Position, "Evaluation preserves the spawned view eye");
            Near(expectedPosition, camera.LocalPosition, "Evaluation preserves the properties position");
            Near(expectedRotation, camera.LocalRotation, "Evaluation preserves the properties rotation");
        }
    }
}
