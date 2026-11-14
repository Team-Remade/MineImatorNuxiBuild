using System.Numerics;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Viewport
{
    public SceneObject? ClothEditObject { get; private set; }
    public HashSet<ClothVertex> ClothVertexSelection { get; } = [];
    private Vector2? _clothBoxStart;
    public IEnumerable<SceneObject> EnumerateClothSceneObjects() => EnumerateClothObjects((MainViewport ?? this).SceneObjects);
    public void SetClothEditObject(SceneObject? owner)
    {
        if (ClothEditObject != owner) ClothVertexSelection.Clear();
        ClothEditObject = owner;
        _clothBoxStart = null;
        _input.ClearPendingPick(); _pendingPickX = _pendingPickY = float.NaN;
    }
    public void SelectAllClothVertices(SceneObject owner)
    {
        SetClothEditObject(owner);
        for (int m = 0; m < owner.Visuals.Count; m++)
            for (int v = 0; v < owner.Visuals[m].Vertices.Count; v++) ClothVertexSelection.Add(new(m, v));
    }
    private bool ClothEditActive()
    {
        if (ClothEditObject == null) return false;
        if (!EnumerateClothSceneObjects().Contains(ClothEditObject) ||
            !ClothEditObject.Modifiers.Any(m => m.Type == SceneModifierType.Cloth) ||
            SelectionManager.Instance?.SelectedObjects.Contains(ClothEditObject) != true)
        { SetClothEditObject(null); return false; }
        return true;
    }
    private IEnumerable<(ClothVertex Vertex, Vector2 Screen, float Depth)> ProjectClothVertices(Camera camera, Vector2 min, Vector2 size)
    {
        if (ClothEditObject == null) yield break;
        mat4 matrix = camera.GetProjectionMatrix(size.X / size.Y) * camera.GetViewMatrix() * ClothEditObject.GetWorldMatrix();
        for (int m = 0; m < ClothEditObject.Visuals.Count; m++)
        {
            var vertices = ClothEditObject.Visuals[m].RenderVertices;
            for (int v = 0; v < vertices.Count; v++)
            {
                vec4 clip = matrix * new vec4(vertices[v], 1);
                if (clip.w <= 0) continue;
                vec3 ndc = clip.xyz / clip.w;
                if (ndc.z < -1 || ndc.z > 1 || MathF.Abs(ndc.x) > 1 || MathF.Abs(ndc.y) > 1) continue;
                yield return (new(m, v), min + new Vector2((ndc.x + 1) * size.X / 2, (1 - ndc.y) * size.Y / 2), ndc.z);
            }
        }
    }
    private bool HandleClothVertexInput(Camera camera, Vector2 min, Vector2 size, bool blocked)
    {
        if (!ClothEditActive()) return false;
        var io = ImGui.GetIO();
        if (io.AppFocusLost) { _clothBoxStart = null; return false; }
        Vector2 mouse = io.MousePos;
        if (!blocked && ImGui.IsWindowHovered() && !io.WantTextInput &&
            mouse.X >= min.X && mouse.Y >= min.Y && mouse.X < min.X + size.X && mouse.Y < min.Y + size.Y &&
            ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            _clothBoxStart = mouse;
        if (_clothBoxStart is not { } start) return false;
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            var points = ProjectClothVertices(camera, min, size).ToArray();
            bool box = Vector2.DistanceSquared(start, mouse) > 16;
            var low = Vector2.Min(start, mouse); var high = Vector2.Max(start, mouse);
            var hits = box ? points.Where(p => p.Screen.X >= low.X && p.Screen.Y >= low.Y && p.Screen.X <= high.X && p.Screen.Y <= high.Y).ToArray()
                : points.Where(p => Vector2.DistanceSquared(p.Screen, mouse) <= 64 * UiTheme.Scale * UiTheme.Scale)
                    .OrderBy(p => Vector2.DistanceSquared(p.Screen, mouse)).ThenBy(p => p.Depth).Take(1).ToArray();
            if (!io.KeyShift && !io.KeyCtrl) ClothVertexSelection.Clear();
            var obj = ClothEditObject!;
            var positions = hits.Select(hit => obj.Visuals[hit.Vertex.Mesh].Vertices[hit.Vertex.Vertex]).ToHashSet();
            for (int m = 0; m < obj.Visuals.Count; m++)
            {
                // UV splits and triangle-soup duplicates represent one cloth particle.
                for (int v = 0; v < obj.Visuals[m].Vertices.Count; v++)
                    if (positions.Contains(obj.Visuals[m].Vertices[v]))
                    {
                        var vertex = new ClothVertex(m, v);
                        if (io.KeyCtrl) ClothVertexSelection.Remove(vertex);
                        else ClothVertexSelection.Add(vertex);
                    }
            }
            _clothBoxStart = null;
        }
        _input.ClearPendingPick(); _pendingPickX = _pendingPickY = float.NaN;
        return true;
    }
    private void RenderClothVertexOverlay(Camera camera, Vector2 min, Vector2 size)
    {
        if (!ClothEditActive()) return;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(min, min + size, true);
        foreach (var point in ProjectClothVertices(camera, min, size))
        {
            bool selected = ClothVertexSelection.Contains(point.Vertex);
            draw.AddCircleFilled(point.Screen, (selected ? 4 : 2.5f) * UiTheme.Scale,
                selected ? 0xff40c0ffu : 0xffe0e0e0u);
        }
        if (_clothBoxStart is { } start) draw.AddRect(Vector2.Min(start, ImGui.GetIO().MousePos),
            Vector2.Max(start, ImGui.GetIO().MousePos), 0xff40c0ffu);
        draw.PopClipRect();
    }
}
