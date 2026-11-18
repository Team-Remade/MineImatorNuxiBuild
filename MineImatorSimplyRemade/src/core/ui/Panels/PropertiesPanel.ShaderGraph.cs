using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl.material;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core.objs;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class PropertiesPanel
{
    private enum ShaderSocketType { Float, Color, Vector }

    // Node card layout in unscaled canvas units. Rows hold the output, then inputs, then parameter previews.
    private const float ShaderHeaderHeight = 26, ShaderRowStart = 28, ShaderRowHeight = 22, ShaderFooter = 6;

    private static readonly (string Category, ShaderNodeKind[] Kinds)[] ShaderNodeMenu =
    [
        ("Input", [ShaderNodeKind.Value, ShaderNodeKind.Color, ShaderNodeKind.Object, ShaderNodeKind.UV]),
        ("Texture", [ShaderNodeKind.Noise, ShaderNodeKind.Wave, ShaderNodeKind.Voronoi, ShaderNodeKind.Checker]),
        ("Color", [ShaderNodeKind.Ramp, ShaderNodeKind.Mix]),
        ("Math", [ShaderNodeKind.Add, ShaderNodeKind.Multiply]),
        ("Vector", [ShaderNodeKind.Mapping])
    ];

    private bool _shaderEditorOpen, _shaderEditorFocused, _shaderCanvasHovered;
    private SceneObject? _shaderEditorObject;
    private Vector2 _shaderPan, _shaderDrag, _shaderCanvasOrigin, _shaderCanvasSize, _shaderMenuPos;
    private float _shaderZoom = 1f, _shaderScale = 1f;
    private int _shaderSelected, _shaderSource, _shaderMoving, _shaderMenuNode;
    private bool _shaderPanning, _shaderLinkDragging;
    private string? _shaderError;
    private ShaderNode? _shaderClipboard;

    /// <summary>True while the shader editor window owns keyboard edit shortcuts.</summary>
    public bool ShaderEditorHasFocus => _shaderEditorOpen && _shaderEditorFocused && _shaderEditorObject != null;
    public bool CanCopyShaderNode => TryGetShaderEditorGraph(out _, out var graph) &&
        graph.Nodes.Any(n => n.Id == _shaderSelected && n.Kind != ShaderNodeKind.Output);
    public bool CanPasteShaderNode => _shaderClipboard != null && TryGetShaderEditorGraph(out _, out _);

    private static void SetShaderGraph(SceneObject obj, ShaderGraph graph)
    {
        obj.SetExplicitMaterialSettings();
        obj.MaterialSettings.CustomShader = graph;
        obj.PropagateMaterialSettingsToChildren();
        ProjectManager.Instance.SetDirty(true);
    }

    private void DrawShaderGraphControls(SceneObject obj)
    {
        var graph = obj.MaterialSettings?.CustomShader;
        bool enabled = graph?.Enabled ?? false;
        if (ImGui.Checkbox(Localization.DisplayText("Use Custom Shader"), ref enabled))
            SetShaderGraph(obj, (graph ?? new ShaderGraph()) with { Enabled = enabled });
        if (enabled || graph != null)
        {
            if (ImGui.Button(Localization.DisplayText("Shader Editor...")))
                _shaderEditorOpen = true;
            if (enabled)
                ImGui.TextWrapped(Localization.DisplayText("The graph replaces surface shading. Standard opacity and cutout settings still apply."));
        }
    }

    // ── Keyboard shortcuts ────────────────────────────────────────────────────

    /// <summary>
    /// Applies the app's edit keybinds to shader nodes while the editor is focused. Returns true when a
    /// shortcut was consumed, so callers must not also apply it to scene objects.
    /// </summary>
    public bool HandleShaderEditorShortcuts()
    {
        if (!ShaderEditorHasFocus) return false;
        // Consume scene-object edits even mid-interaction so they never fall through to the scene.
        bool idle = !ImGui.IsAnyItemActive();
        if (Keybinds.Pressed("delete")) { if (idle) DeleteSelectedShaderNode(); }
        else if (Keybinds.Pressed("duplicate")) { if (idle) DuplicateSelectedShaderNode(); }
        else if (Keybinds.Pressed("cut")) { if (idle) CutSelectedShaderNode(); }
        else if (Keybinds.Pressed("copy")) { if (idle) CopySelectedShaderNode(); }
        else if (Keybinds.Pressed("paste")) { if (idle) PasteShaderNode(); }
        else if (!Keybinds.Pressed("selectAll") && !Keybinds.Pressed("hide") && !Keybinds.Pressed("show"))
            return false;
        return true;
    }

    public void DeleteSelectedShaderNode()
    {
        if (TryGetShaderEditorGraph(out var obj, out var graph)) ShaderRemoveNode(obj, graph, _shaderSelected);
    }

    public void DuplicateSelectedShaderNode()
    {
        if (TryGetShaderEditorGraph(out var obj, out var graph)) ShaderDuplicateNode(obj, graph, _shaderSelected);
    }

    public void CopySelectedShaderNode()
    {
        if (TryGetShaderEditorGraph(out _, out var graph)) ShaderCopyNode(graph, _shaderSelected);
    }

    public void CutSelectedShaderNode()
    {
        if (!TryGetShaderEditorGraph(out var obj, out var graph) || !ShaderCopyNode(graph, _shaderSelected)) return;
        ShaderRemoveNode(obj, graph, _shaderSelected);
    }

    public void PasteShaderNode()
    {
        if (!TryGetShaderEditorGraph(out var obj, out var graph)) return;
        var screen = _shaderCanvasHovered ? ImGui.GetIO().MousePos : _shaderCanvasOrigin + _shaderCanvasSize * 0.5f;
        ShaderPasteNode(obj, graph, ShaderScreenToGraph(screen) - new Vector2(20, 12));
    }

    private bool TryGetShaderEditorGraph(out SceneObject obj, out ShaderGraph graph)
    {
        obj = _shaderEditorObject!;
        graph = obj?.MaterialSettings?.CustomShader!;
        return _shaderEditorOpen && obj != null && graph != null && graph.ValidationError == null;
    }

    // ── Graph edits ───────────────────────────────────────────────────────────

    private static int NextShaderNodeId(ShaderGraph graph)
    {
        int id = 1;
        while (graph.Nodes.Any(node => node.Id == id)) id++;
        return id;
    }

    private ShaderGraph ShaderAddNode(SceneObject obj, ShaderGraph graph, ShaderNode node)
    {
        if (graph.Nodes.Length >= ShaderGraph.MaxNodes)
        {
            _shaderError = Localization.DisplayText($"Graphs support up to {ShaderGraph.MaxNodes} nodes.");
            return graph;
        }
        graph = graph with { Nodes = graph.Nodes.Add(node) };
        SetShaderGraph(obj, graph);
        _shaderSelected = node.Id;
        _shaderError = null;
        return graph;
    }

    private ShaderGraph ShaderRemoveNode(SceneObject obj, ShaderGraph graph, int id)
    {
        var node = graph.Nodes.FirstOrDefault(n => n.Id == id);
        if (node == null || node.Kind == ShaderNodeKind.Output) return graph;
        graph = graph with
        {
            Nodes = graph.Nodes.Remove(node),
            Links = graph.Links.RemoveAll(link => link.Target == id || link.Source == id)
        };
        SetShaderGraph(obj, graph);
        if (_shaderSource == id) _shaderSource = 0;
        if (_shaderMoving == id) (_shaderMoving, _shaderDrag) = (0, Vector2.Zero);
        if (_shaderSelected == id) _shaderSelected = 0;
        _shaderError = null;
        return graph;
    }

    private ShaderGraph ShaderDuplicateNode(SceneObject obj, ShaderGraph graph, int id)
    {
        var node = graph.Nodes.FirstOrDefault(n => n.Id == id);
        if (node == null || node.Kind == ShaderNodeKind.Output) return graph;
        return ShaderAddNode(obj, graph, node with { Id = NextShaderNodeId(graph), X = node.X + 24, Y = node.Y + 24 });
    }

    private bool ShaderCopyNode(ShaderGraph graph, int id)
    {
        var node = graph.Nodes.FirstOrDefault(n => n.Id == id);
        if (node == null || node.Kind == ShaderNodeKind.Output) return false;
        _shaderClipboard = node;
        return true;
    }

    private ShaderGraph ShaderPasteNode(SceneObject obj, ShaderGraph graph, Vector2 at) => _shaderClipboard is { } node
        ? ShaderAddNode(obj, graph, node with { Id = NextShaderNodeId(graph), X = at.X, Y = at.Y })
        : graph;

    private ShaderGraph ShaderDisconnectNode(SceneObject obj, ShaderGraph graph, int id)
    {
        graph = graph with { Links = graph.Links.RemoveAll(link => link.Target == id || link.Source == id) };
        SetShaderGraph(obj, graph);
        return graph;
    }

    // ── Layout and styling ────────────────────────────────────────────────────

    private static uint ShaderRgb(int r, int g, int b, int a = 255) => (uint)((a << 24) | (b << 16) | (g << 8) | r);

    private static (string Name, uint Color) ShaderCategory(ShaderNodeKind kind) => kind switch
    {
        ShaderNodeKind.Output => ("Output", ShaderRgb(128, 38, 52)),
        ShaderNodeKind.Value or ShaderNodeKind.Color or ShaderNodeKind.Object or ShaderNodeKind.UV
            => ("Input", ShaderRgb(124, 52, 82)),
        ShaderNodeKind.Noise or ShaderNodeKind.Wave or ShaderNodeKind.Voronoi or ShaderNodeKind.Checker
            => ("Texture", ShaderRgb(134, 82, 36)),
        ShaderNodeKind.Ramp or ShaderNodeKind.Mix => ("Color", ShaderRgb(112, 102, 42)),
        ShaderNodeKind.Add or ShaderNodeKind.Multiply => ("Math", ShaderRgb(38, 98, 130)),
        _ => ("Vector", ShaderRgb(70, 64, 140))
    };

    private static ShaderSocketType ShaderOutputType(ShaderNodeKind kind) => kind switch
    {
        ShaderNodeKind.Color or ShaderNodeKind.Ramp or ShaderNodeKind.Mix => ShaderSocketType.Color,
        ShaderNodeKind.Object or ShaderNodeKind.UV or ShaderNodeKind.Mapping => ShaderSocketType.Vector,
        _ => ShaderSocketType.Float
    };

    private static string ShaderOutputLabel(ShaderNodeKind kind) => ShaderOutputType(kind) switch
    {
        ShaderSocketType.Color => "Color",
        ShaderSocketType.Vector => "Vector",
        _ => kind is ShaderNodeKind.Value or ShaderNodeKind.Add or ShaderNodeKind.Multiply ? "Value" : "Factor"
    };

    private static ShaderSocketType ShaderInputType(string input) => input switch
    {
        "Base Color" or "Emission" or "Color A" or "Color B" => ShaderSocketType.Color,
        "Vector" => ShaderSocketType.Vector,
        _ => ShaderSocketType.Float
    };

    private static uint ShaderSocketColor(ShaderSocketType type) => type switch
    {
        ShaderSocketType.Color => ShaderRgb(214, 196, 58),
        ShaderSocketType.Vector => ShaderRgb(112, 108, 222),
        _ => ShaderRgb(168, 168, 168)
    };

    private static int ShaderOutputRows(ShaderNodeKind kind) => kind == ShaderNodeKind.Output ? 0 : 1;

    private static int ShaderParameterRows(ShaderNodeKind kind) => kind switch
    {
        ShaderNodeKind.Value or ShaderNodeKind.Color or ShaderNodeKind.Noise or ShaderNodeKind.Voronoi
            or ShaderNodeKind.Checker => 1,
        ShaderNodeKind.Wave or ShaderNodeKind.Ramp or ShaderNodeKind.Mapping => 2,
        _ => 0
    };

    private static float ShaderNodeWidth(ShaderNodeKind kind) => kind == ShaderNodeKind.Output ? 220 : 180;

    private static float ShaderNodeHeight(ShaderNodeKind kind) => ShaderRowStart + ShaderFooter + ShaderRowHeight *
        (ShaderOutputRows(kind) + ShaderGraph.Inputs(kind).Length + ShaderParameterRows(kind));

    private static float ShaderRowCenter(int row) => ShaderRowStart + ShaderRowHeight * (row + 0.5f);

    private static float ShaderInputY(ShaderNodeKind kind, int input) => ShaderRowCenter(ShaderOutputRows(kind) + input);

    private Vector2 ShaderScreenToGraph(Vector2 screen) =>
        (screen - _shaderCanvasOrigin - _shaderPan) / (_shaderScale * _shaderZoom);

    private static string ShortcutSuffix(string id) => Keybinds.Label(id) is { Length: > 0 } label ? $" ({label})" : "";

    // ── Editor window ─────────────────────────────────────────────────────────

    private void RenderShaderGraphEditor()
    {
        _shaderCanvasHovered = false;
        if (!_shaderEditorOpen)
        {
            _shaderEditorFocused = false;
            return;
        }
        var obj = _currentObject;
        if (obj?.MaterialSettings?.CustomShader is not { } graph)
        {
            _shaderEditorOpen = _shaderEditorFocused = false;
            _shaderEditorObject = null;
            return;
        }
        if (_shaderEditorObject != obj)
        {
            _shaderEditorObject = obj;
            ResetShaderCanvas();
        }

        ImGui.SetNextWindowSize(new Vector2(1150, 620) * UiTheme.Scale, ImGuiCond.FirstUseEver);
        if (ImGui.Begin(Localization.DisplayText("Shader Editor") + "###ShaderEditor", ref _shaderEditorOpen,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            // Keep the editing target while a menu is open so menu commands use the same scope.
            if (!ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel))
                _shaderEditorFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
            if (_shaderEditorFocused && _shaderSource != 0 && ImGui.IsKeyPressed(ImGuiKey.Escape, false))
                (_shaderSource, _shaderLinkDragging) = (0, false);

            UiTheme.RenderSectionHeader(obj.Name, Localization.DisplayText(graph.Enabled
                ? "Custom shader active" : "Standard material active"));
            graph = DrawShaderToolbar(obj, graph);

            if (graph.ValidationError is { } validationError)
                UiText.Colored(new Vector4(1f, 0.45f, 0.4f, 1f), validationError);
            else
            {
                float statusHeight = ImGui.GetFrameHeightWithSpacing();
                float inspectorWidth = 240 * UiTheme.Scale;
                float width = Math.Max(1f, ImGui.GetContentRegionAvail().X - inspectorWidth - ImGui.GetStyle().ItemSpacing.X);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
                if (ImGui.BeginChild("##shaderCanvas", new Vector2(width, -statusHeight), ImGuiChildFlags.Borders,
                        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
                    graph = DrawShaderCanvas(obj, graph);
                ImGui.EndChild();
                ImGui.PopStyleVar();
                ImGui.SameLine();
                if (ImGui.BeginChild("##shaderInspector", new Vector2(0, -statusHeight), ImGuiChildFlags.Borders))
                    graph = DrawShaderNodeInspector(obj, graph);
                ImGui.EndChild();
                DrawShaderStatusBar();
            }
        }
        else _shaderEditorFocused = false;
        ImGui.End();
        if (!_shaderEditorOpen)
        {
            _shaderEditorObject = null;
            _shaderEditorFocused = false;
        }
    }

    private ShaderGraph DrawShaderToolbar(SceneObject obj, ShaderGraph graph)
    {
        bool valid = graph.ValidationError == null;
        ImGui.BeginDisabled(!valid);
        if (ImGui.Button(Localization.DisplayText("Add Node"))) ImGui.OpenPopup("##shaderNodes");
        ImGui.EndDisabled();
        if (ImGui.BeginPopup("##shaderNodes"))
        {
            var center = ShaderScreenToGraph(_shaderCanvasOrigin + _shaderCanvasSize * 0.5f) - new Vector2(90, 30);
            graph = DrawShaderAddMenu(obj, graph, center);
            ImGui.EndPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button(Localization.DisplayText("Replace with Preset")))
            ImGui.OpenPopup("##shaderPresets");
        if (ImGui.BeginPopup("##shaderPresets"))
        {
            foreach (var preset in Enum.GetValues<ShaderGraphPreset>())
            {
                if (!ImGui.MenuItem(Localization.DisplayText(preset == ShaderGraphPreset.BrushedSteel ? "Brushed Steel" : preset.ToString()))) continue;
                graph = ShaderGraphPresets.Create(preset) with { Enabled = graph.Enabled };
                SetShaderGraph(obj, graph);
                ResetShaderCanvas();
            }
            ImGui.Separator();
            if (ImGui.MenuItem(Localization.DisplayText("Empty Graph")))
            {
                graph = new ShaderGraph { Enabled = graph.Enabled };
                SetShaderGraph(obj, graph);
                ResetShaderCanvas();
            }
            ImGui.EndPopup();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
        ImGui.BeginDisabled(!valid);
        if (ImGui.Button(Localization.DisplayText("Frame All"))) FrameShaderNodes(graph);
        if (ImGui.IsItemHovered()) UiText.Tooltip(Localization.DisplayText("Fit every node in the view"));
        ImGui.SameLine();
        if (ImGui.Button(Localization.DisplayText("Reset View")))
        {
            _shaderPan = Vector2.Zero;
            _shaderZoom = 1f;
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
        UiText.Disabled(Localization.DisplayText($"{graph.Nodes.Length}/{ShaderGraph.MaxNodes} nodes"));
        return graph;
    }

    private void DrawShaderStatusBar()
    {
        float right = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        ImGui.AlignTextToFramePadding();
        if (_shaderError != null)
            UiText.Colored(new Vector4(1f, 0.45f, 0.4f, 1f), _shaderError);
        else if (_shaderSource != 0)
            UiText.Colored(ImGui.GetStyle().Colors[(int)ImGuiCol.CheckMark],
                Localization.DisplayText("Click an input socket to connect. Press Esc or right-click to cancel."));
        else
            UiText.Disabled(Localization.DisplayText(
                "Drag or click from an output to an input  |  Right-click for options  |  Middle-drag to pan  |  Scroll to zoom"));
        string zoom = $"{_shaderZoom * 100:0}%";
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), right - ImGui.CalcTextSize(zoom).X));
        UiText.Disabled(zoom);
    }

    private ShaderGraph DrawShaderAddMenu(SceneObject obj, ShaderGraph graph, Vector2 at)
    {
        bool full = graph.Nodes.Length >= ShaderGraph.MaxNodes;
        if (full) UiText.Disabled(Localization.DisplayText("Node limit reached"));
        foreach (var (category, kinds) in ShaderNodeMenu)
        {
            if (!ImGui.BeginMenu(Localization.DisplayText(category), !full)) continue;
            foreach (var kind in kinds)
            {
                if (ImGui.MenuItem(Localization.DisplayText(ShaderGraph.Label(kind))))
                    graph = ShaderAddNode(obj, graph, ShaderGraph.CreateNode(NextShaderNodeId(graph), kind, at.X, at.Y));
            }
            ImGui.EndMenu();
        }
        return graph;
    }

    private void ResetShaderCanvas()
    {
        _shaderPan = _shaderDrag = Vector2.Zero;
        _shaderZoom = 1f;
        _shaderSelected = _shaderSource = _shaderMoving = _shaderMenuNode = 0;
        _shaderPanning = _shaderLinkDragging = false;
        _shaderError = null;
    }

    private void FrameShaderNodes(ShaderGraph graph)
    {
        if (graph.Nodes.IsDefaultOrEmpty || _shaderCanvasSize.X <= 1 || _shaderCanvasSize.Y <= 1)
        {
            _shaderPan = Vector2.Zero;
            _shaderZoom = 1f;
            return;
        }
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var n in graph.Nodes)
        {
            min = Vector2.Min(min, new Vector2(n.X, n.Y));
            max = Vector2.Max(max, new Vector2(n.X + ShaderNodeWidth(n.Kind), n.Y + ShaderNodeHeight(n.Kind)));
        }
        var size = max - min + new Vector2(80);
        _shaderZoom = Math.Clamp(Math.Min(_shaderCanvasSize.X / (size.X * _shaderScale),
            _shaderCanvasSize.Y / (size.Y * _shaderScale)), 0.45f, 1.25f);
        _shaderPan = _shaderCanvasSize * 0.5f - (min + max) * 0.5f * _shaderScale * _shaderZoom;
    }

    // ── Canvas ────────────────────────────────────────────────────────────────

    private unsafe ShaderGraph DrawShaderCanvas(SceneObject obj, ShaderGraph graph)
    {
        var origin = ImGui.GetCursorScreenPos();
        var extent = Vector2.Max(ImGui.GetContentRegionAvail(), Vector2.One);
        _shaderCanvasOrigin = origin;
        _shaderCanvasSize = extent;
        _shaderScale = UiTheme.Scale;
        ImGui.InvisibleButton("##shaderBackground", extent);
        bool hovered = ImGui.IsItemHovered();
        _shaderCanvasHovered = hovered;
        var io = ImGui.GetIO();
        var mouse = io.MousePos;
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Middle)) _shaderPanning = true;
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Middle)) _shaderPanning = false;
        if (_shaderPanning) _shaderPan += io.MouseDelta;
        if (hovered && io.MouseWheel != 0 && _shaderMoving == 0)
        {
            float zoom = Math.Clamp(_shaderZoom * MathF.Pow(1.15f, io.MouseWheel), 0.45f, 1.75f);
            _shaderPan = mouse - origin - (mouse - origin - _shaderPan) * (zoom / _shaderZoom);
            _shaderZoom = zoom;
        }
        float s = _shaderScale * _shaderZoom;
        if (_shaderMoving != 0)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) _shaderDrag += io.MouseDelta / s;
            else
            {
                var moved = graph.Nodes.FirstOrDefault(node => node.Id == _shaderMoving);
                if (moved != null && _shaderDrag != Vector2.Zero)
                {
                    graph = graph with { Nodes = graph.Nodes.Replace(moved, moved with
                    {
                        X = moved.X + _shaderDrag.X, Y = moved.Y + _shaderDrag.Y
                    }) };
                    SetShaderGraph(obj, graph);
                }
                _shaderDrag = Vector2.Zero;
                _shaderMoving = 0;
            }
        }

        Vector2 Position(ShaderNode n) => origin + _shaderPan +
            (new Vector2(n.X, n.Y) + (n.Id == _shaderMoving ? _shaderDrag : Vector2.Zero)) * s;
        Vector2 Size(ShaderNode n) => new Vector2(ShaderNodeWidth(n.Kind), ShaderNodeHeight(n.Kind)) * s;
        bool IsOutput(ShaderNode n) => n.Kind == ShaderNodeKind.Output;
        Vector2 Out(ShaderNode n) => Position(n) + new Vector2(ShaderNodeWidth(n.Kind), ShaderRowCenter(0)) * s;
        Vector2 In(ShaderNode n, int input) => Position(n) + new Vector2(0, ShaderInputY(n.Kind, input)) * s;
        bool Near(Vector2 p) => Vector2.DistanceSquared(mouse, p) <= 81 * s * s;
        bool Within(Vector2 min, Vector2 max) => mouse.X >= min.X && mouse.Y >= min.Y && mouse.X <= max.X && mouse.Y <= max.Y;

        // Topmost hovered card; later nodes draw over earlier ones.
        int hoveredNode = 0;
        if (hovered)
        {
            for (int ni = graph.Nodes.Length - 1; ni >= 0 && hoveredNode == 0; ni--)
            {
                var pos = Position(graph.Nodes[ni]);
                if (Within(pos - new Vector2(6 * s, 0), pos + Size(graph.Nodes[ni]) + new Vector2(6 * s, 0)))
                    hoveredNode = graph.Nodes[ni].Id;
            }
        }

        var draw = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        float fontSize = ImGui.GetFontSize() * _shaderZoom;
        uint accent = ImGui.ColorConvertFloat4ToU32(ImGui.GetStyle().Colors[(int)ImGuiCol.CheckMark] with { W = 1f });
        draw.PushClipRect(origin, origin + extent, true);
        draw.AddRectFilled(origin, origin + extent, ShaderRgb(29, 29, 32));
        void Grid(float spacing, uint color)
        {
            float Offset(float value) => (value % spacing + spacing) % spacing;
            for (float x = Offset(_shaderPan.X); x < extent.X; x += spacing)
                draw.AddLine(origin + new Vector2(x, 0), origin + new Vector2(x, extent.Y), color);
            for (float y = Offset(_shaderPan.Y); y < extent.Y; y += spacing)
                draw.AddLine(origin + new Vector2(0, y), origin + new Vector2(extent.X, y), color);
        }
        if (16 * s >= 7) Grid(16 * s, ShaderRgb(36, 36, 40));
        Grid(80 * s, ShaderRgb(46, 46, 52));

        void Wire(Vector2 from, Vector2 to, uint color, float thickness)
        {
            float d = Math.Max(40 * s, Math.Abs(to.X - from.X) * 0.5f);
            draw.AddBezierCubic(from, from + new Vector2(d, 0), to - new Vector2(d, 0), to, ShaderRgb(0, 0, 0, 120), (thickness + 2.5f) * s);
            draw.AddBezierCubic(from, from + new Vector2(d, 0), to - new Vector2(d, 0), to, color, thickness * s);
        }
        static uint Brighten(uint color) => (color & 0xFF000000) |
            (uint)((((color >> 16) & 0xFF) + 255) / 2 << 16) | (uint)((((color >> 8) & 0xFF) + 255) / 2 << 8) | (((color & 0xFF) + 255) / 2);
        static uint Dim(uint color, int alpha) => (color & 0x00FFFFFF) | (uint)(alpha << 24);

        var nodes = graph.Nodes.ToDictionary(node => node.Id);
        foreach (var link in graph.Links)
        {
            var source = nodes[link.Source];
            bool active = link.Source == _shaderSelected || link.Target == _shaderSelected;
            uint color = ShaderSocketColor(ShaderOutputType(source.Kind));
            Wire(Out(source), In(nodes[link.Target], link.Input), active ? Brighten(color) : Dim(color, 200), active ? 3f : 2.2f);
        }
        if (_shaderSource != 0 && nodes.TryGetValue(_shaderSource, out var from))
            Wire(Out(from), mouse, Brighten(ShaderSocketColor(ShaderOutputType(from.Kind))), 2.6f);

        void Socket(Vector2 center, ShaderSocketType type, bool linked)
        {
            bool hot = hovered && Near(center);
            float radius = (hot ? 6.5f : 5f) * s;
            draw.AddCircleFilled(center, radius + 1.5f * s, ShaderRgb(16, 16, 18));
            uint color = ShaderSocketColor(type);
            draw.AddCircleFilled(center, radius, linked || hot ? color : Dim(color, 170));
            if (hot) draw.AddCircle(center, radius + 1.5f * s, 0xFFFFFFFF, 0, 1.5f * s);
        }
        void Text(Vector2 at, uint color, string text) => draw.AddText(font, fontSize, at, color, text);
        float TextWidth(string text) => ImGui.CalcTextSize(text).X * _shaderZoom;

        foreach (var n in graph.Nodes)
        {
            var pos = Position(n);
            var end = pos + Size(n);
            float width = ShaderNodeWidth(n.Kind);
            float rounding = 6 * s;
            var (_, headerColor) = ShaderCategory(n.Kind);
            var inputs = ShaderGraph.Inputs(n.Kind);

            draw.AddRectFilled(pos + new Vector2(3, 5) * s, end + new Vector2(3, 5) * s, ShaderRgb(0, 0, 0, 80), rounding);
            draw.AddRectFilled(pos, end, ShaderRgb(48, 48, 53, 248), rounding);
            draw.AddRectFilled(pos, pos + new Vector2(width, ShaderHeaderHeight) * s, headerColor, rounding, ImDrawFlags.RoundCornersTop);
            draw.AddLine(pos + new Vector2(0, ShaderHeaderHeight) * s, pos + new Vector2(width, ShaderHeaderHeight) * s,
                ShaderRgb(0, 0, 0, 110), 1f * s);
            if (_shaderSelected == n.Id)
                draw.AddRect(pos - new Vector2(1.5f * s), end + new Vector2(1.5f * s), accent, rounding + 1.5f * s, ImDrawFlags.None, 2f * s);
            else
                draw.AddRect(pos, end, hoveredNode == n.Id ? ShaderRgb(120, 120, 130) : ShaderRgb(16, 16, 18), rounding, ImDrawFlags.None, 1f * s);
            Text(pos + new Vector2(10 * s, (ShaderHeaderHeight * s - fontSize) * 0.5f), 0xFFFFFFFF,
                Localization.DisplayText(ShaderGraph.Label(n.Kind)));

            float RowTop(int row) => pos.Y + (ShaderRowCenter(row) * s) - fontSize * 0.5f;
            if (!IsOutput(n))
            {
                string label = Localization.DisplayText(ShaderOutputLabel(n.Kind));
                Text(new Vector2(pos.X + (width - 12) * s - TextWidth(label), RowTop(0)), ShaderRgb(220, 220, 224), label);
                Socket(Out(n), ShaderOutputType(n.Kind), graph.Links.Any(link => link.Source == n.Id));
            }
            for (int i = 0; i < inputs.Length; ++i)
            {
                int input = i;
                bool linked = graph.Links.Any(link => link.Target == n.Id && link.Input == input);
                Text(new Vector2(pos.X + 12 * s, RowTop(ShaderOutputRows(n.Kind) + i)),
                    linked ? ShaderRgb(220, 220, 224) : ShaderRgb(170, 170, 176), Localization.DisplayText(inputs[i]));
                Socket(In(n, i), ShaderInputType(inputs[i]), linked);
            }

            int firstParameter = ShaderOutputRows(n.Kind) + inputs.Length;
            void Field(int row, string name, string value)
            {
                var top = pos + new Vector2(8, ShaderRowStart + ShaderRowHeight * (firstParameter + row) + 2) * s;
                draw.AddRectFilled(top, top + new Vector2(width - 16, ShaderRowHeight - 4) * s, ShaderRgb(34, 34, 38), 4 * s);
                float y = RowTop(firstParameter + row);
                Text(new Vector2(pos.X + 14 * s, y), ShaderRgb(150, 150, 158), Localization.DisplayText(name));
                Text(new Vector2(pos.X + (width - 14) * s - TextWidth(value), y), ShaderRgb(230, 230, 234), value);
            }
            switch (n.Kind)
            {
                case ShaderNodeKind.Color:
                {
                    var top = pos + new Vector2(8, ShaderRowStart + ShaderRowHeight * firstParameter + 3) * s;
                    var bottom = top + new Vector2(width - 16, ShaderRowHeight - 6) * s;
                    draw.AddRectFilled(top, bottom, ImGui.ColorConvertFloat4ToU32(new Vector4(
                        Math.Clamp(n.A, 0, 1), Math.Clamp(n.B, 0, 1), Math.Clamp(n.C, 0, 1), 1f)), 4 * s);
                    draw.AddRect(top, bottom, ShaderRgb(16, 16, 18), 4 * s, ImDrawFlags.None, 1f * s);
                    break;
                }
                case ShaderNodeKind.Value: Field(0, "Value", $"{n.A:0.###}"); break;
                case ShaderNodeKind.Noise or ShaderNodeKind.Voronoi or ShaderNodeKind.Checker:
                    Field(0, "Scale", $"{n.A:0.###}"); break;
                case ShaderNodeKind.Wave:
                    Field(0, "Scale", $"{n.A:0.###}");
                    Field(1, "Distortion", $"{n.B:0.###}");
                    break;
                case ShaderNodeKind.Ramp:
                    Field(0, "Start", $"{n.A:0.###}");
                    Field(1, "End", $"{n.B:0.###}");
                    break;
                case ShaderNodeKind.Mapping:
                    Field(0, "Scale", $"{n.A:0.###}");
                    Field(1, "Offset", $"{n.B:0.##}, {n.C:0.##}, {n.D:0.##}");
                    break;
            }
        }
        draw.PopClipRect();

        bool clicked = hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        bool right = hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right);
        if (_shaderLinkDragging && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _shaderLinkDragging = false;
            graph = FinishShaderLinkDrag();
        }
        if (clicked || right) graph = HandleShaderCanvasClick();
        return DrawShaderCanvasMenus(obj, graph);

        ShaderGraph ConnectInput(ShaderNode n, int input, bool connect)
        {
            var links = graph.Links.RemoveAll(link => link.Target == n.Id && link.Input == input);
            if (connect) links = links.Add(new ShaderLink(_shaderSource, n.Id, input));
            var candidate = graph with { Links = links };
            _shaderError = candidate.ValidationError;
            if (_shaderError != null) return graph;
            _shaderSource = 0;
            SetShaderGraph(obj, candidate);
            return candidate;
        }

        ShaderGraph FinishShaderLinkDrag()
        {
            if (_shaderSource == 0 || !nodes.TryGetValue(_shaderSource, out var source)) return graph;
            for (int ni = graph.Nodes.Length - 1; ni >= 0; ni--)
            {
                var n = graph.Nodes[ni];
                for (int i = 0; i < ShaderGraph.Inputs(n.Kind).Length; ++i)
                    if (Near(In(n, i))) return ConnectInput(n, i, connect: true);
            }
            // Releasing on the output itself keeps the wire pending for click-to-connect.
            if (!Near(Out(source))) _shaderSource = 0;
            return graph;
        }

        ShaderGraph HandleShaderCanvasClick()
        {
            for (int ni = graph.Nodes.Length - 1; ni >= 0; ni--)
            {
                var n = graph.Nodes[ni];
                var inputs = ShaderGraph.Inputs(n.Kind);
                for (int i = 0; i < inputs.Length; ++i)
                {
                    if (!Near(In(n, i))) continue;
                    int input = i;
                    if (clicked && _shaderSource == 0)
                    {
                        _shaderSelected = n.Id;
                        var existing = graph.Links.FirstOrDefault(link => link.Target == n.Id && link.Input == input);
                        if (existing.Source == 0) return graph;
                        // Pick up the connected wire so it can be dragged to another input or dropped.
                        _shaderSource = existing.Source;
                        _shaderLinkDragging = true;
                        var detached = graph with { Links = graph.Links.Remove(existing) };
                        SetShaderGraph(obj, detached);
                        return detached;
                    }
                    return ConnectInput(n, input, clicked);
                }
                if (clicked && !IsOutput(n) && Near(Out(n)))
                {
                    _shaderSource = _shaderSource == n.Id ? 0 : n.Id;
                    _shaderLinkDragging = _shaderSource != 0;
                    _shaderSelected = n.Id;
                    _shaderError = null;
                    return graph;
                }
                if (n.Id != hoveredNode) continue;
                _shaderSelected = n.Id;
                _shaderError = null;
                if (_shaderSource != 0)
                    _shaderSource = 0;
                else if (clicked)
                    _shaderMoving = n.Id;
                else
                {
                    _shaderMenuNode = n.Id;
                    ImGui.OpenPopup("##shaderNodeMenu");
                }
                return graph;
            }
            _shaderError = null;
            if (_shaderSource != 0)
            {
                _shaderSource = 0;
                return graph;
            }
            if (clicked) _shaderSelected = 0;
            else
            {
                _shaderMenuPos = ShaderScreenToGraph(mouse);
                ImGui.OpenPopup("##shaderCanvasMenu");
            }
            return graph;
        }
    }

    private ShaderGraph DrawShaderCanvasMenus(SceneObject obj, ShaderGraph graph)
    {
        if (ImGui.BeginPopup("##shaderCanvasMenu"))
        {
            UiText.Disabled(Localization.DisplayText("Add Node"));
            ImGui.Separator();
            graph = DrawShaderAddMenu(obj, graph, _shaderMenuPos);
            ImGui.Separator();
            if (ImGui.MenuItem(Localization.DisplayText("Paste"), Keybinds.Label("paste"), false,
                    _shaderClipboard != null && graph.Nodes.Length < ShaderGraph.MaxNodes))
                graph = ShaderPasteNode(obj, graph, _shaderMenuPos);
            if (ImGui.MenuItem(Localization.DisplayText("Frame All"))) FrameShaderNodes(graph);
            ImGui.EndPopup();
        }
        if (ImGui.BeginPopup("##shaderNodeMenu"))
        {
            var node = graph.Nodes.FirstOrDefault(n => n.Id == _shaderMenuNode);
            if (node == null) ImGui.CloseCurrentPopup();
            else
            {
                bool editable = node.Kind != ShaderNodeKind.Output;
                UiText.Disabled(Localization.DisplayText(ShaderGraph.Label(node.Kind)));
                ImGui.Separator();
                if (ImGui.MenuItem(Localization.DisplayText("Duplicate"), Keybinds.Label("duplicate"), false,
                        editable && graph.Nodes.Length < ShaderGraph.MaxNodes))
                    graph = ShaderDuplicateNode(obj, graph, node.Id);
                if (ImGui.MenuItem(Localization.DisplayText("Copy"), Keybinds.Label("copy"), false, editable))
                    ShaderCopyNode(graph, node.Id);
                if (ImGui.MenuItem(Localization.DisplayText("Cut"), Keybinds.Label("cut"), false, editable) &&
                    ShaderCopyNode(graph, node.Id))
                    graph = ShaderRemoveNode(obj, graph, node.Id);
                if (ImGui.MenuItem(Localization.DisplayText("Disconnect All"), "", false,
                        graph.Links.Any(link => link.Source == node.Id || link.Target == node.Id)))
                    graph = ShaderDisconnectNode(obj, graph, node.Id);
                ImGui.Separator();
                if (ImGui.MenuItem(Localization.DisplayText("Delete"), Keybinds.Label("delete"), false, editable))
                    graph = ShaderRemoveNode(obj, graph, node.Id);
            }
            ImGui.EndPopup();
        }
        return graph;
    }

    // ── Inspector ─────────────────────────────────────────────────────────────

    private ShaderGraph DrawShaderNodeInspector(SceneObject obj, ShaderGraph graph)
    {
        var node = graph.Nodes.FirstOrDefault(n => n.Id == _shaderSelected);
        if (node == null)
        {
            UiTheme.RenderSectionHeader(Localization.DisplayText("Inspector"));
            UiText.Disabled(Localization.DisplayText("No node selected"));
            ImGui.Spacing();
            ImGui.TextWrapped(Localization.DisplayText("Select a node to edit its settings, or right-click the canvas to add one."));
            return graph;
        }

        var (category, categoryColor) = ShaderCategory(node.Kind);
        var draw = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        float barHeight = ImGui.GetTextLineHeightWithSpacing() + ImGui.GetTextLineHeight();
        draw.AddRectFilled(start, start + new Vector2(4 * UiTheme.Scale, barHeight), categoryColor, 2 * UiTheme.Scale);
        ImGui.Indent(10 * UiTheme.Scale);
        ImGui.TextWrapped(Localization.DisplayText(ShaderGraph.Label(node.Kind)));
        UiText.Disabled(Localization.DisplayText($"{category} node"));
        ImGui.Unindent(10 * UiTheme.Scale);
        ImGui.Separator();

        ImGui.PushItemWidth(-1);
        var color = new Vector3(node.A, node.B, node.C);
        float a = node.A, b = node.B, c = node.C, d = node.D;
        bool Parameter(string name, ref float value, float min, float max)
        {
            UiText.Disabled(Localization.DisplayText(name));
            return ImGui.DragFloat("##" + name, ref value, 0.02f, min, max, "%.3f", ImGuiSliderFlags.AlwaysClamp);
        }
        bool changed = false;
        switch (node.Kind)
        {
            case ShaderNodeKind.Color:
                UiText.Disabled(Localization.DisplayText("Color"));
                changed = ImGui.ColorEdit3("##graphColor", ref color, ImGuiColorEditFlags.DisplayHex);
                (a, b, c) = (color.X, color.Y, color.Z);
                break;
            case ShaderNodeKind.Value:
                changed = Parameter("Value", ref a, -32, 32);
                break;
            case ShaderNodeKind.Noise:
            case ShaderNodeKind.Voronoi:
            case ShaderNodeKind.Checker:
            case ShaderNodeKind.Wave:
                changed = Parameter("Scale", ref a, 0.01f, 256);
                if (node.Kind == ShaderNodeKind.Wave) changed |= Parameter("Distortion", ref b, 0, 32);
                break;
            case ShaderNodeKind.Ramp:
                changed = Parameter("Start", ref a, 0, Math.Max(0, b - 0.001f));
                changed |= Parameter("End", ref b, Math.Min(1, a + 0.001f), 1);
                break;
            case ShaderNodeKind.Mapping:
                changed = Parameter("Scale", ref a, -256, 256);
                changed |= Parameter("Offset X", ref b, -32, 32);
                changed |= Parameter("Offset Y", ref c, -32, 32);
                changed |= Parameter("Offset Z", ref d, -32, 32);
                break;
            default:
                UiText.Disabled(Localization.DisplayText("This node has no settings."));
                break;
        }
        ImGui.PopItemWidth();
        if (changed)
        {
            var candidate = graph with { Nodes = graph.Nodes.Replace(node, node with { A = a, B = b, C = c, D = d }) };
            if (candidate.ValidationError == null)
            {
                SetShaderGraph(obj, candidate);
                graph = candidate;
                node = graph.Nodes.First(n => n.Id == node.Id);
            }
            else _shaderError = candidate.ValidationError;
        }

        var inputs = ShaderGraph.Inputs(node.Kind);
        if (inputs.Length > 0)
        {
            ImGui.Spacing();
            ImGui.SeparatorText(Localization.DisplayText("Inputs"));
            var nodes = graph.Nodes.ToDictionary(n => n.Id);
            for (int i = 0; i < inputs.Length; i++)
            {
                int input = i;
                var link = graph.Links.FirstOrDefault(l => l.Target == node.Id && l.Input == input);
                ImGui.TextUnformatted(Localization.DisplayText(inputs[i]));
                ImGui.SameLine();
                string source = link.Source != 0 && nodes.TryGetValue(link.Source, out var from)
                    ? Localization.DisplayText(ShaderGraph.Label(from.Kind))
                    : Localization.DisplayText("Not connected");
                float x = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(source).X;
                ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), x));
                if (link.Source != 0) ImGui.TextUnformatted(source);
                else UiText.Disabled(source);
            }
        }

        if (node.Kind == ShaderNodeKind.Output) return graph;
        ImGui.Spacing();
        ImGui.Separator();
        float buttonWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
        ImGui.BeginDisabled(graph.Nodes.Length >= ShaderGraph.MaxNodes);
        if (ImGui.Button(Localization.DisplayText("Duplicate"), new Vector2(buttonWidth, 0)))
            graph = ShaderDuplicateNode(obj, graph, node.Id);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.Tooltip(Localization.DisplayText("Duplicate node") + ShortcutSuffix("duplicate"));
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.68f, 0.22f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.78f, 0.26f, 0.26f, 1f));
        if (ImGui.Button(Localization.DisplayText("Delete"), new Vector2(-1, 0)))
            graph = ShaderRemoveNode(obj, graph, node.Id);
        ImGui.PopStyleColor(3);
        if (ImGui.IsItemHovered()) UiText.Tooltip(Localization.DisplayText("Delete node") + ShortcutSuffix("delete"));
        return graph;
    }
}
