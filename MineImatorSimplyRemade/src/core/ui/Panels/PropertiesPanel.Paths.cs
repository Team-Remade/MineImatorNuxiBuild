using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class PropertiesPanel
{
    private void RenderPathProperties()
    {
        if (_currentObject is PathSceneObject path)
        {
            if (!ImGui.CollapsingHeader(Localization.Label("text.path.title", "Path"), ImGuiTreeNodeFlags.DefaultOpen)) return;
            UiText.Wrapped(Localization.T("text.path.help"));
            var settings = path.Settings;
            bool closed = settings.Closed, smooth = settings.Smooth;
            int detail = settings.Detail, shape = (int)settings.Shape;
            bool changed = ImGui.Checkbox(Localization.Label("text.path.closed", "Closed"), ref closed);
            changed |= ImGui.Checkbox(Localization.Label("text.path.smooth", "Smooth"), ref smooth);
            changed |= ImGui.DragInt(Localization.Label("text.path.detail", "Detail"), ref detail, 0.25f, 1, 64);
            string[] shapes = [Localization.T("text.path.none"), Localization.T("text.path.flat"), Localization.T("text.path.tube")];
            changed |= ImGui.Combo(Localization.Label("text.path.shape", "Shape"), ref shape, shapes, shapes.Length);
            settings = settings with { Closed = closed, Smooth = smooth, Detail = detail, Shape = (PathShape)shape };
            if (shape != 0)
            {
                float radius = settings.Radius * 16f, textureLength = settings.TextureLength * 16f;
                bool invert = settings.Invert, smoothSegments = settings.SmoothSegments;
                bool smoothRing = settings.SmoothRing, mapped = settings.MappedTexture;
                int sides = settings.Sides;
                changed |= ImGui.DragFloat(Localization.Label("text.path.radius", "Radius"), ref radius, 0.1f, 0.01f, 16000f);
                changed |= ImGui.Checkbox(Localization.Label("text.path.invert", "Invert"), ref invert);
                changed |= ImGui.Checkbox(Localization.Label("text.path.smooth_segments", "Smooth Segments"), ref smoothSegments);
                if (shape == (int)PathShape.Tube)
                {
                    changed |= ImGui.Checkbox(Localization.Label("text.path.smooth_ring", "Smooth Ring"), ref smoothRing);
                    changed |= ImGui.DragInt(Localization.Label("text.path.sides", "Tube Sides"), ref sides, 0.25f, 3, 32);
                }
                changed |= ImGui.Checkbox(Localization.Label("text.path.mapped", "Fit Texture to Path"), ref mapped);
                if (!mapped)
                    changed |= ImGui.DragFloat(Localization.Label("text.path.texture_length", "Texture Length"), ref textureLength, 0.1f, 0.01f, 16000f);
                settings = settings with { Radius = Math.Clamp(radius, 0.01f, 16000f) / 16f,
                    Invert = invert, SmoothSegments = smoothSegments, SmoothRing = smoothRing, Sides = sides,
                    MappedTexture = mapped, TextureLength = Math.Clamp(textureLength, 0.01f, 16000f) / 16f };
            }
            if (changed)
            {
                path.Settings = settings;
                path.EvaluateGeometry();
                ProjectManager.Instance.SetDirty(true);
            }
            UiText.Wrapped(string.Format(Localization.T("text.path.length"), path.Length * 16f));
            var points = path.Children.OfType<PathPointSceneObject>().ToArray();
            ImGui.BeginDisabled(SpawnMenu == null || Viewport == null);
            if (ImGui.Button(Localization.Label("text.path.add_point", "Add Point")) && SpawnMenu != null && Viewport != null)
            {
                var point = SpawnMenu.SpawnPathPointObject($"Point {points.Length + 1}");
                if (point != null)
                {
                    point.Position = points.Length == 0 ? vec3.Zero : points[^1].Position +
                        (points.Length > 1 ? points[^1].Position - points[^2].Position : vec3.UnitX);
                    Viewport.SceneObjects.Remove(point);
                    path.AddChild(point);
                    SelectionManager.Instance?.ClearSelection();
                    SelectionManager.Instance?.SelectObject(point);
                    ProjectManager.Instance.SetDirty(true);
                }
            }
            ImGui.EndDisabled();
            foreach (var point in points)
                if (ImGui.Selectable($"{point.GetDisplayName()}##pathPoint{point.ObjectId}"))
                {
                    SelectionManager.Instance?.ClearSelection();
                    SelectionManager.Instance?.SelectObject(point);
                }
            return;
        }
        if (_currentObject is PathPointSceneObject controlPoint)
        {
            if (!ImGui.CollapsingHeader(Localization.Label("text.path.point", "Path Point"), ImGuiTreeNodeFlags.DefaultOpen)) return;
            float angle = controlPoint.PathPointAngle, scale = controlPoint.PathPointScale;
            if (ImGui.DragFloat(Localization.Label("text.path.angle", "Point Angle"), ref angle, 0.5f))
            {
                controlPoint.PathPointAngle = angle;
                Timeline?.RecordAutoKeyframe(controlPoint, "path_point.angle");
                ProjectManager.Instance.SetDirty(true);
            }
            RegisterPropertyContext("path_point.angle");
            if (ImGui.DragFloat(Localization.Label("text.path.scale", "Point Scale"), ref scale, 0.01f, 0f, 100f))
            {
                controlPoint.PathPointScale = Math.Clamp(scale, 0f, 100f);
                Timeline?.RecordAutoKeyframe(controlPoint, "path_point.scale");
                ProjectManager.Instance.SetDirty(true);
            }
            RegisterPropertyContext("path_point.scale");
            if (controlPoint.Parent is PathSceneObject parent)
            {
                int index = parent.Children.ToList().IndexOf(controlPoint);
                ImGui.BeginDisabled(index == 0);
                if (ImGui.Button(Localization.Label("text.path.previous", "Move Earlier")))
                {
                    parent.MoveChild(controlPoint, index - 1);
                    ProjectManager.Instance.SetDirty(true);
                }
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.BeginDisabled(index == parent.Children.Count - 1);
                if (ImGui.Button(Localization.Label("text.path.next", "Move Later")))
                {
                    parent.MoveChild(controlPoint, index + 1);
                    ProjectManager.Instance.SetDirty(true);
                }
                ImGui.EndDisabled();
            }
            UiText.Wrapped(Localization.T("text.path.point_help"));
            return;
        }
        if (_currentObject == null || Viewport == null ||
            !ImGui.CollapsingHeader(Localization.Label("text.path.follow", "Follow Path"))) return;
        var owner = _currentObject;
        var paths = EnumeratePaths(Viewport.SceneObjects).ToArray();
        var selected = paths.FirstOrDefault(p => p.ObjectId == owner.PathFollowObjectId);
        string preview = selected?.GetDisplayName() ?? Localization.T("text.path.none");
        if (ImGui.BeginCombo(Localization.Label("text.path.target", "Path Object"), preview))
        {
            if (ImGui.Selectable(Localization.T("text.path.none"), owner.PathFollowObjectId.Length == 0)) SetTarget("");
            foreach (var candidate in paths)
            {
                bool descendant = false;
                for (SceneObject? p = candidate; p != null; p = p.Parent)
                    if (ReferenceEquals(p, owner)) { descendant = true; break; }
                ImGui.BeginDisabled(descendant);
                if (ImGui.Selectable($"{candidate.GetDisplayName()}##follow{candidate.ObjectId}", selected == candidate))
                    SetTarget(candidate.ObjectId);
                ImGui.EndDisabled();
            }
            ImGui.EndCombo();
        }
        RegisterPropertyContext("path_follow.target");
        if (owner.PathFollowObjectId.Length == 0) return;
        if (selected == null || selected.Length <= 0f || !owner.IsFollowingPath)
            UiText.Wrapped(Localization.T("text.path.unavailable"));
        float offset = owner.PathFollowOffset * 16f;
        if (ImGui.DragFloat(Localization.Label("text.path.offset", "Path Offset"), ref offset, 0.1f))
        {
            owner.PathFollowOffset = offset / 16f;
            Timeline?.RecordAutoKeyframe(owner, "path_follow.offset");
            ProjectManager.Instance.SetDirty(true);
        }
        RegisterPropertyContext("path_follow.offset");
        UiText.Wrapped(Localization.T("text.path.follow_help"));

        void SetTarget(string id)
        {
            owner.PathFollowObjectId = id;
            owner.SetEvaluatedPathFrame(null);
            Timeline?.RecordAutoKeyframe(owner, "path_follow.target");
            ProjectManager.Instance.SetDirty(true);
        }
    }

    private static IEnumerable<PathSceneObject> EnumeratePaths(IEnumerable<SceneObject> objects)
    {
        foreach (var obj in objects)
        {
            if (obj is PathSceneObject path) yield return path;
            foreach (var child in EnumeratePaths(obj.Children)) yield return child;
        }
    }
}
