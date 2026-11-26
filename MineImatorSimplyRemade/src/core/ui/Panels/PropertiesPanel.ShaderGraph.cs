using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl.material;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class PropertiesPanel
{
    // Node card layout in unscaled canvas units. Rows hold outputs, then settings, then inputs.
    private const float ShaderHeaderHeight = 26, ShaderRowStart = 28, ShaderRowHeight = 22, ShaderFooter = 6;

    private bool _shaderEditorOpen, _shaderEditorFocused, _shaderCanvasHovered;
    private SceneObject? _shaderEditorObject;
    private Vector2 _shaderPan, _shaderDrag, _shaderCanvasOrigin, _shaderCanvasSize, _shaderMenuPos;
    private float _shaderZoom = 1f, _shaderScale = 1f;
    private int _shaderSelected, _shaderSource, _shaderSourceOutput, _shaderMoving, _shaderMenuNode;
    private bool _shaderPanning, _shaderLinkDragging;
    private string? _shaderError;
    private string _shaderSearch = "";
    private ShaderNode? _shaderClipboard;

    /// <summary>True while the shader editor window owns keyboard edit shortcuts.</summary>
    public bool ShaderEditorHasFocus => _shaderEditorOpen && _shaderEditorFocused && _shaderEditorObject != null;
    public bool CanCopyShaderNode => TryGetShaderEditorGraph(out _, out var graph) &&
        graph.Nodes.Any(n => n.Id == _shaderSelected && !ShaderGraph.IsOutput(n.Kind));
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
        if (node == null || ShaderGraph.IsOutput(node.Kind)) return graph;
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
        if (node == null || ShaderGraph.IsOutput(node.Kind)) return graph;
        return ShaderAddNode(obj, graph, node with { Id = NextShaderNodeId(graph), X = node.X + 24, Y = node.Y + 24 });
    }

    private bool ShaderCopyNode(ShaderGraph graph, int id)
    {
        var node = graph.Nodes.FirstOrDefault(n => n.Id == id);
        if (node == null || ShaderGraph.IsOutput(node.Kind)) return false;
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

    /// <summary>Swaps the output node for another kind; links into the old output are dropped.</summary>
    private ShaderGraph ShaderReplaceOutput(SceneObject obj, ShaderGraph graph, ShaderNodeKind kind)
    {
        var candidate = ShaderGraph.WithOutputKind(graph, kind);
        if (candidate.ValidationError != null)
        {
            _shaderError = candidate.ValidationError;
            return graph;
        }
        SetShaderGraph(obj, candidate);
        _shaderSelected = candidate.Nodes.First(node => ShaderGraph.IsOutput(node.Kind)).Id;
        _shaderError = null;
        return candidate;
    }

    private IEnumerable<PathSceneObject> ShaderPathObjects()
    {
        static IEnumerable<PathSceneObject> Walk(IEnumerable<SceneObject> objects)
        {
            foreach (var obj in objects)
            {
                if (obj is PathSceneObject path) yield return path;
                foreach (var nested in Walk(obj.Children)) yield return nested;
            }
        }
        return Viewport == null ? [] : Walk(Viewport.SceneObjects);
    }

    private string ShaderPathLabel(string? target)
    {
        if (string.IsNullOrEmpty(target)) return "None";
        return ShaderPathObjects().FirstOrDefault(path => path.ObjectId == target)?.Name ?? "Missing";
    }

    private ShaderGraph ShaderReplaceNode(SceneObject obj, ShaderGraph graph, ShaderNode node, ShaderNode replacement)
    {
        var candidate = graph with { Nodes = graph.Nodes.Replace(node, replacement) };
        if (candidate.ValidationError != null)
        {
            _shaderError = candidate.ValidationError;
            return graph;
        }
        SetShaderGraph(obj, candidate);
        return candidate;
    }

    // ── Layout and styling ────────────────────────────────────────────────────

    private static uint ShaderRgb(int r, int g, int b, int a = 255) => (uint)((a << 24) | (b << 16) | (g << 8) | r);

    private static (string Name, uint Color) ShaderCategory(ShaderNodeKind kind) => ShaderGraph.Spec(kind).Category switch
    {
        "Output" => ("Output", ShaderRgb(128, 38, 52)),
        "Input" => ("Input", ShaderRgb(124, 52, 82)),
        "Texture" => ("Texture", ShaderRgb(134, 82, 36)),
        "Color" => ("Color", ShaderRgb(112, 102, 42)),
        "Converter" => ("Converter", ShaderRgb(38, 98, 130)),
        _ => ("Vector", ShaderRgb(70, 64, 140))
    };

    private static uint ShaderSocketColor(ShaderSocketType type) => type switch
    {
        ShaderSocketType.Color => ShaderRgb(214, 196, 58),
        ShaderSocketType.Vector => ShaderRgb(112, 108, 222),
        _ => ShaderRgb(168, 168, 168)
    };

    private static ShaderSocketType ShaderOutputType(ShaderNodeKind kind, int output) =>
        (uint)output < (uint)ShaderGraph.OutputCount(kind) ? ShaderGraph.OutputSockets(kind)[output].Type : ShaderSocketType.Float;

    private static int ShaderOutputRows(ShaderNodeKind kind) => ShaderGraph.OutputCount(kind);

    /// <summary>Rows for dropdowns, toggles, parameters and previews, drawn between outputs and inputs.</summary>
    private static int ShaderSettingRows(ShaderNodeKind kind)
    {
        var spec = ShaderGraph.Spec(kind);
        int rows = (spec.Mode != null ? 1 : 0) + (spec.Option != null ? 1 : 0) + (spec.Variant != null ? 1 : 0) +
                   (spec.ClampLabel != null ? 1 : 0) + spec.Parameters.Length;
        return rows + (kind is ShaderNodeKind.Color or ShaderNodeKind.Ramp or ShaderNodeKind.Path ? 1 : 0);
    }

    private static float ShaderNodeWidth(ShaderNodeKind kind) => ShaderGraph.IsOutput(kind) ? 240 : 200;

    private static float ShaderNodeHeight(ShaderNodeKind kind) => ShaderRowStart + ShaderFooter + ShaderRowHeight *
        (ShaderOutputRows(kind) + ShaderSettingRows(kind) + ShaderGraph.InputCount(kind));

    private static float ShaderRowCenter(int row) => ShaderRowStart + ShaderRowHeight * (row + 0.5f);

    private static float ShaderInputY(ShaderNodeKind kind, int input) =>
        ShaderRowCenter(ShaderOutputRows(kind) + ShaderSettingRows(kind) + input);

    private Vector2 ShaderScreenToGraph(Vector2 screen) =>
        (screen - _shaderCanvasOrigin - _shaderPan) / (_shaderScale * _shaderZoom);

    private static string ShortcutSuffix(string id) => Keybinds.Label(id) is { Length: > 0 } label ? $" ({label})" : "";

    private static string ShaderChoiceText(ShaderNodeChoice choice, int value) =>
        choice.Values[Math.Clamp(value, 0, choice.Values.Length - 1)];

    private static string ShaderSourceLabel(ShaderNode source, int output)
    {
        string label = Localization.DisplayText(ShaderGraph.Label(source.Kind));
        return ShaderGraph.OutputCount(source.Kind) > 1
            ? $"{label} · {Localization.DisplayText(ShaderGraph.OutputSockets(source.Kind)[output].Name)}"
            : label;
    }

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
                float inspectorWidth = 280 * UiTheme.Scale;
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
        if (ImGui.Button(Localization.DisplayText("Add Node")))
        {
            _shaderSearch = "";
            ImGui.OpenPopup("##shaderNodes");
        }
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
                if (!ImGui.MenuItem(Localization.DisplayText(ShaderGraphPresets.Label(preset)))) continue;
                graph = ShaderGraphPresets.Create(preset) with { Enabled = graph.Enabled };
                SetShaderGraph(obj, graph);
                ResetShaderCanvas();
            }
            ImGui.Separator();
            if (ImGui.MenuItem(Localization.DisplayText("Empty Graph")))
            {
                graph = new ShaderGraph
                {
                    Enabled = graph.Enabled, Nodes = [ShaderGraph.CreateNode(1, ShaderNodeKind.Output, 470, 80)]
                };
                SetShaderGraph(obj, graph);
                ResetShaderCanvas();
            }
            if (ImGui.MenuItem(Localization.DisplayText("Fog Volume")))
            {
                graph = ShaderGraphPresets.CreateFogVolume() with { Enabled = graph.Enabled };
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
                "Drag from an output to an input  |  Shift+A or right-click to add  |  Middle-drag to pan  |  Scroll to zoom"));
        string zoom = $"{_shaderZoom * 100:0}%";
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), right - ImGui.CalcTextSize(zoom).X));
        UiText.Disabled(zoom);
    }

    private ShaderGraph DrawShaderAddMenu(SceneObject obj, ShaderGraph graph, Vector2 at)
    {
        bool full = graph.Nodes.Length >= ShaderGraph.MaxNodes;
        if (full) UiText.Disabled(Localization.DisplayText("Node limit reached"));
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(220 * UiTheme.Scale);
        ImGui.InputTextWithHint("##shaderNodeSearch", Localization.DisplayText("Search nodes..."), ref _shaderSearch, 64);
        string search = _shaderSearch.Trim();
        ShaderGraph Add(ShaderNodeKind kind)
        {
            ImGui.CloseCurrentPopup();
            return ShaderAddNode(obj, graph, ShaderGraph.CreateNode(NextShaderNodeId(graph), kind, at.X, at.Y));
        }
        // A graph has exactly one output, so choosing an output kind swaps it instead of adding a node.
        ShaderGraph AddOrReplace(ShaderNodeKind kind)
        {
            if (!ShaderGraph.IsOutput(kind)) return Add(kind);
            ImGui.CloseCurrentPopup();
            return ShaderReplaceOutput(obj, graph, kind);
        }
        if (search.Length > 0)
        {
            var matches = ShaderGraph.Categories.SelectMany(ShaderGraph.MenuKinds).Concat(ShaderGraph.OutputKinds)
                .Where(kind => ShaderGraph.Label(kind).Contains(search, StringComparison.OrdinalIgnoreCase) ||
                               Localization.DisplayText(ShaderGraph.Label(kind)).Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 0) UiText.Disabled(Localization.DisplayText("No matching nodes"));
            bool enter = ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter);
            for (int i = 0; i < matches.Count; i++)
            {
                bool allowed = !full || ShaderGraph.IsOutput(matches[i]);
                if (ImGui.MenuItem(Localization.DisplayText(ShaderGraph.Label(matches[i])), ShaderGraph.Spec(matches[i]).Category, false, allowed) ||
                    (i == 0 && enter && allowed))
                    graph = AddOrReplace(matches[i]);
            }
            return graph;
        }
        foreach (string category in ShaderGraph.Categories)
        {
            if (!ImGui.BeginMenu(Localization.DisplayText(category), !full)) continue;
            foreach (var kind in ShaderGraph.MenuKinds(category))
            {
                if (ImGui.MenuItem(Localization.DisplayText(ShaderGraph.Label(kind))))
                    graph = Add(kind);
            }
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu(Localization.DisplayText("Output")))
        {
            foreach (var kind in ShaderGraph.OutputKinds)
            {
                bool current = graph.Nodes.Any(node => node.Kind == kind);
                if (ImGui.MenuItem(Localization.DisplayText(ShaderGraph.Label(kind)), "", current, !current))
                    graph = AddOrReplace(kind);
            }
            UiText.Disabled(Localization.DisplayText("Replaces the current output and its links."));
            ImGui.EndMenu();
        }
        return graph;
    }

    private void ResetShaderCanvas()
    {
        _shaderPan = _shaderDrag = Vector2.Zero;
        _shaderZoom = 1f;
        _shaderSelected = _shaderSource = _shaderSourceOutput = _shaderMoving = _shaderMenuNode = 0;
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
            _shaderCanvasSize.Y / (size.Y * _shaderScale)), 0.3f, 1.25f);
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
            float zoom = Math.Clamp(_shaderZoom * MathF.Pow(1.15f, io.MouseWheel), 0.3f, 1.75f);
            _shaderPan = mouse - origin - (mouse - origin - _shaderPan) * (zoom / _shaderZoom);
            _shaderZoom = zoom;
        }
        if (hovered && io.KeyShift && !io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.A, false))
        {
            _shaderMenuPos = ShaderScreenToGraph(mouse);
            _shaderSearch = "";
            ImGui.OpenPopup("##shaderCanvasMenu");
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
        Vector2 Out(ShaderNode n, int output) => Position(n) + new Vector2(ShaderNodeWidth(n.Kind), ShaderRowCenter(output)) * s;
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
        static uint ColorU32(float r, float g, float b) => ImGui.ColorConvertFloat4ToU32(new Vector4(
            Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1), 1f));

        var nodes = graph.Nodes.ToDictionary(node => node.Id);
        foreach (var link in graph.Links)
        {
            var source = nodes[link.Source];
            bool active = link.Source == _shaderSelected || link.Target == _shaderSelected;
            uint color = ShaderSocketColor(ShaderOutputType(source.Kind, link.Output));
            Wire(Out(source, link.Output), In(nodes[link.Target], link.Input), active ? Brighten(color) : Dim(color, 200), active ? 3f : 2.2f);
        }
        if (_shaderSource != 0 && nodes.TryGetValue(_shaderSource, out var from))
            Wire(Out(from, _shaderSourceOutput), mouse, Brighten(ShaderSocketColor(ShaderOutputType(from.Kind, _shaderSourceOutput))), 2.6f);

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

        foreach (var original in graph.Nodes)
        {
            var n = ShaderGraph.Normalize(original);
            var spec = ShaderGraph.Spec(n.Kind);
            var pos = Position(n);
            var end = pos + Size(n);
            float width = ShaderNodeWidth(n.Kind);
            float rounding = 6 * s;
            var (_, headerColor) = ShaderCategory(n.Kind);
            var inputs = ShaderGraph.InputSockets(n.Kind);
            var outputs = ShaderGraph.OutputSockets(n.Kind);

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
                Localization.DisplayText(spec.Label));

            float RowTop(int row) => pos.Y + (ShaderRowCenter(row) * s) - fontSize * 0.5f;
            for (int k = 0; k < outputs.Length; k++)
            {
                int output = k;
                string label = Localization.DisplayText(outputs[k].Name);
                Text(new Vector2(pos.X + (width - 12) * s - TextWidth(label), RowTop(k)), ShaderRgb(220, 220, 224), label);
                Socket(Out(n, k), outputs[k].Type, graph.Links.Any(link => link.Source == n.Id && link.Output == output));
            }

            int row = outputs.Length;
            (Vector2 Min, Vector2 Max) Field(int fieldRow)
            {
                var top = pos + new Vector2(8, ShaderRowStart + ShaderRowHeight * fieldRow + 2) * s;
                return (top, top + new Vector2(width - 16, ShaderRowHeight - 4) * s);
            }
            void Setting(string name, string value)
            {
                var (min, max) = Field(row);
                draw.AddRectFilled(min, max, ShaderRgb(34, 34, 38), 4 * s);
                float y = RowTop(row);
                Text(new Vector2(pos.X + 14 * s, y), ShaderRgb(150, 150, 158), Localization.DisplayText(name));
                string text = Localization.DisplayText(value);
                Text(new Vector2(pos.X + (width - 14) * s - TextWidth(text), y), ShaderRgb(230, 230, 234), text);
                row++;
            }
            if (spec.Mode != null) Setting(spec.Mode.Label, ShaderChoiceText(spec.Mode, n.Mode));
            if (spec.Option != null) Setting(spec.Option.Label, ShaderChoiceText(spec.Option, n.Option));
            if (spec.Variant != null) Setting(spec.Variant.Label, ShaderChoiceText(spec.Variant, n.Variant));
            if (spec.ClampLabel != null) Setting(spec.ClampLabel, n.Clamp ? "On" : "Off");
            float[] parameters = [n.A, n.B, n.C, n.D];
            for (int p = 0; p < spec.Parameters.Length; p++)
                Setting(spec.Parameters[p].Name, spec.Parameters[p].Percent ? $"{parameters[p] * 100:0.#}%" : $"{parameters[p]:0.###}");
            if (n.Kind == ShaderNodeKind.Path) Setting("Path", ShaderPathLabel(n.Target));
            if (n.Kind == ShaderNodeKind.Color)
            {
                var (min, max) = Field(row++);
                draw.AddRectFilled(min, max, ColorU32(n.A, n.B, n.C), 4 * s);
                draw.AddRect(min, max, ShaderRgb(16, 16, 18), 4 * s, ImDrawFlags.None, 1f * s);
            }
            else if (n.Kind == ShaderNodeKind.Ramp)
                DrawShaderRampPreview(draw, n, Field(row++), s);

            for (int i = 0; i < inputs.Length; ++i)
            {
                int input = i;
                bool linked = graph.Links.Any(link => link.Target == n.Id && link.Input == input);
                float y = RowTop(row + i);
                Text(new Vector2(pos.X + 12 * s, y), linked ? ShaderRgb(220, 220, 224) : ShaderRgb(170, 170, 176),
                    Localization.DisplayText(inputs[i].Name));
                Socket(In(n, i), inputs[i].Type, linked);
                if (linked || inputs[i].Auto != ShaderAutoInput.None) continue;
                var (x, vy, z) = ShaderGraph.InputValue(n, i);
                float right = pos.X + (width - 12) * s;
                if (inputs[i].Type == ShaderSocketType.Color)
                {
                    var min = new Vector2(right - 34 * s, pos.Y + (ShaderRowCenter(row + i) - 6) * s);
                    draw.AddRectFilled(min, min + new Vector2(34, 12) * s, ColorU32(x, vy, z), 3 * s);
                    draw.AddRect(min, min + new Vector2(34, 12) * s, ShaderRgb(16, 16, 18), 3 * s, ImDrawFlags.None, 1f * s);
                }
                else
                {
                    string value = inputs[i].Type == ShaderSocketType.Vector ? $"{x:0.##}, {vy:0.##}, {z:0.##}"
                        : inputs[i].Percent ? $"{x * 100:0.#}%" : $"{x:0.###}";
                    Text(new Vector2(right - TextWidth(value), y), ShaderRgb(205, 205, 210), value);
                }
            }
        }
        draw.PopClipRect();

        bool clicked = hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        bool rightClick = hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right);
        if (_shaderLinkDragging && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _shaderLinkDragging = false;
            graph = FinishShaderLinkDrag();
        }
        if (clicked || rightClick) graph = HandleShaderCanvasClick();
        return DrawShaderCanvasMenus(obj, graph);

        ShaderGraph ConnectInput(ShaderNode n, int input, bool connect)
        {
            var links = graph.Links.RemoveAll(link => link.Target == n.Id && link.Input == input);
            if (connect) links = links.Add(new ShaderLink(_shaderSource, n.Id, input, _shaderSourceOutput));
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
                for (int i = 0; i < ShaderGraph.InputCount(n.Kind); ++i)
                    if (Near(In(n, i))) return ConnectInput(n, i, connect: true);
            }
            // Releasing on the output itself keeps the wire pending for click-to-connect.
            if (!Near(Out(source, _shaderSourceOutput))) _shaderSource = 0;
            return graph;
        }

        ShaderGraph HandleShaderCanvasClick()
        {
            for (int ni = graph.Nodes.Length - 1; ni >= 0; ni--)
            {
                var n = graph.Nodes[ni];
                for (int i = 0; i < ShaderGraph.InputCount(n.Kind); ++i)
                {
                    if (!Near(In(n, i))) continue;
                    int input = i;
                    if (clicked && _shaderSource == 0)
                    {
                        _shaderSelected = n.Id;
                        var existing = graph.Links.FirstOrDefault(link => link.Target == n.Id && link.Input == input);
                        if (existing.Source == 0) return graph;
                        // Pick up the connected wire so it can be dragged to another input or dropped.
                        (_shaderSource, _shaderSourceOutput) = (existing.Source, existing.Output);
                        _shaderLinkDragging = true;
                        var detached = graph with { Links = graph.Links.Remove(existing) };
                        SetShaderGraph(obj, detached);
                        return detached;
                    }
                    return ConnectInput(n, input, clicked);
                }
                if (clicked)
                {
                    for (int k = 0; k < ShaderGraph.OutputCount(n.Kind); k++)
                    {
                        if (!Near(Out(n, k))) continue;
                        bool same = _shaderSource == n.Id && _shaderSourceOutput == k;
                        (_shaderSource, _shaderSourceOutput) = same ? (0, 0) : (n.Id, k);
                        _shaderLinkDragging = _shaderSource != 0;
                        _shaderSelected = n.Id;
                        _shaderError = null;
                        return graph;
                    }
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
                _shaderSearch = "";
                ImGui.OpenPopup("##shaderCanvasMenu");
            }
            return graph;
        }
    }

    private static void DrawShaderRampPreview(ImDrawListPtr draw, ShaderNode node, (Vector2 Min, Vector2 Max) area, float s)
    {
        node = ShaderGraph.Normalize(node);
        int stops = Math.Clamp(node.Option, 0, 2) + 2;
        float[] positions = [node.A, node.B, node.C, node.D];
        const int segments = 32;
        float width = area.Max.X - area.Min.X;
        for (int i = 0; i < segments; i++)
        {
            float t = (i + 0.5f) / segments;
            var (r, g, b) = SampleShaderRamp(node, positions, stops, t);
            uint color = ImGui.ColorConvertFloat4ToU32(new Vector4(Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1), 1));
            draw.AddRectFilled(new Vector2(area.Min.X + width * i / segments, area.Min.Y),
                new Vector2(area.Min.X + width * (i + 1) / segments + 0.5f, area.Max.Y), color);
        }
        draw.AddRect(area.Min, area.Max, ShaderRgb(16, 16, 18), 0, ImDrawFlags.None, 1f * s);
    }

    private static (float R, float G, float B) SampleShaderRamp(ShaderNode node, float[] positions, int stops, float f)
    {
        var p = new float[4];
        p[0] = Math.Clamp(positions[0], 0, 1);
        for (int k = 1; k < 4; k++) p[k] = Math.Clamp(positions[k], p[k - 1], 1);
        (float, float, float) Color(int stop) => ShaderGraph.InputValue(node, stop + 1);
        static (float, float, float) Mix((float, float, float) a, (float, float, float) b, float t) =>
            (a.Item1 + (b.Item1 - a.Item1) * t, a.Item2 + (b.Item2 - a.Item2) * t, a.Item3 + (b.Item3 - a.Item3) * t);
        if (f <= p[0]) return Color(0);
        for (int k = 0; k + 1 < stops; k++)
        {
            if (f >= p[k + 1]) continue;
            if (node.Mode == 1) return Color(k);
            float t = (f - p[k]) / Math.Max(p[k + 1] - p[k], 1e-6f);
            if (node.Mode == 2) t = t * t * (3 - 2 * t);
            return Mix(Color(k), Color(k + 1), t);
        }
        return Color(stops - 1);
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
                bool editable = !ShaderGraph.IsOutput(node.Kind);
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
                if (ImGui.MenuItem(Localization.DisplayText("Reset Values")))
                {
                    var reset = ShaderGraph.CreateNode(node.Id, node.Kind, node.X, node.Y);
                    graph = ShaderReplaceNode(obj, graph, node, reset);
                }
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
        var original = graph.Nodes.FirstOrDefault(n => n.Id == _shaderSelected);
        if (original == null)
        {
            UiTheme.RenderSectionHeader(Localization.DisplayText("Inspector"));
            UiText.Disabled(Localization.DisplayText("No node selected"));
            ImGui.Spacing();
            ImGui.TextWrapped(Localization.DisplayText("Select a node to edit its settings, or right-click the canvas to add one."));
            ImGui.Spacing();
            ImGui.TextWrapped(Localization.DisplayText("Add an Input > Time node to animate values with the timeline."));
            return graph;
        }

        var node = ShaderGraph.Normalize(original);
        var spec = ShaderGraph.Spec(node.Kind);
        var (category, categoryColor) = ShaderCategory(node.Kind);
        var draw = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        float barHeight = ImGui.GetTextLineHeightWithSpacing() + ImGui.GetTextLineHeight();
        draw.AddRectFilled(start, start + new Vector2(4 * UiTheme.Scale, barHeight), categoryColor, 2 * UiTheme.Scale);
        ImGui.Indent(10 * UiTheme.Scale);
        ImGui.TextWrapped(Localization.DisplayText(spec.Label));
        UiText.Disabled(Localization.DisplayText($"{category} node"));
        ImGui.Unindent(10 * UiTheme.Scale);
        ImGui.Separator();

        ImGui.PushItemWidth(-1);
        var edited = node;
        bool Choice(ShaderNodeChoice? choice, string id, ref int value)
        {
            if (choice == null) return false;
            UiText.Disabled(Localization.DisplayText(choice.Label));
            bool changed = false;
            int current = Math.Clamp(value, 0, choice.Values.Length - 1);
            if (ImGui.BeginCombo("##" + id, Localization.DisplayText(choice.Values[current])))
            {
                for (int i = 0; i < choice.Values.Length; i++)
                {
                    if (ImGui.Selectable(Localization.DisplayText(choice.Values[i]), i == current))
                        (value, changed) = (i, true);
                    if (i == current) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            return changed;
        }
        int mode = node.Mode, option = node.Option, variant = node.Variant;
        bool changedNode = false;
        if (Choice(spec.Mode, "shaderMode", ref mode)) (edited, changedNode) = (edited with { Mode = mode }, true);
        if (Choice(spec.Option, "shaderOption", ref option))
        {
            edited = edited with { Option = option };
            if (node.Kind == ShaderNodeKind.Ramp) edited = SpreadNewRampStops(edited, node.Option);
            changedNode = true;
        }
        if (Choice(spec.Variant, "shaderVariant", ref variant)) (edited, changedNode) = (edited with { Variant = variant }, true);
        if (spec.ClampLabel != null)
        {
            bool clamp = node.Clamp;
            if (ImGui.Checkbox(Localization.DisplayText(spec.ClampLabel), ref clamp))
                (edited, changedNode) = (edited with { Clamp = clamp }, true);
        }

        float a = node.A, b = node.B, c = node.C, d = node.D;
        bool Parameter(string name, ref float value, float min, float max, float speed = 0.01f)
        {
            UiText.Disabled(Localization.DisplayText(name));
            return ImGui.DragFloat("##" + name, ref value, speed, min, max, "%.3f", ImGuiSliderFlags.AlwaysClamp);
        }
        bool changedParameters = false;
        if (node.Kind == ShaderNodeKind.Color)
        {
            var color = new Vector3(node.A, node.B, node.C);
            UiText.Disabled(Localization.DisplayText("Color"));
            changedParameters = ImGui.ColorEdit3("##graphColor", ref color, ImGuiColorEditFlags.DisplayHex | ImGuiColorEditFlags.Float);
            (a, b, c) = (color.X, color.Y, color.Z);
        }
        else if (node.Kind == ShaderNodeKind.Ramp)
        {
            int stops = Math.Clamp(edited.Option, 0, 2) + 2;
            float[] positions = [a, b, c, d];
            for (int k = 0; k < stops; k++)
            {
                float low = k == 0 ? 0 : positions[k - 1], high = k + 1 < stops ? positions[k + 1] : 1;
                changedParameters |= Parameter($"Stop {k + 1} Position", ref positions[k], low, Math.Max(low, high), 0.005f);
            }
            (a, b, c, d) = (positions[0], positions[1], positions[2], positions[3]);
        }
        else
        {
            float[] values = [a, b, c, d];
            for (int p = 0; p < spec.Parameters.Length; p++)
            {
                var parameter = spec.Parameters[p];
                float speed = Math.Max(0.005f, (parameter.Max - parameter.Min) / 400f);
                if (node.Kind == ShaderNodeKind.Value) speed = 0.01f;
                if (parameter.Percent)
                {
                    float percent = values[p] * 100f;
                    UiText.Disabled(Localization.DisplayText(parameter.Name));
                    if (ImGui.DragFloat("##" + parameter.Name, ref percent, 0.25f, parameter.Min * 100f, parameter.Max * 100f,
                            "%.1f%%", ImGuiSliderFlags.AlwaysClamp))
                    {
                        values[p] = percent / 100f;
                        changedParameters = true;
                    }
                }
                else
                    changedParameters |= Parameter(parameter.Name, ref values[p], parameter.Min, parameter.Max, speed);
            }
            (a, b, c, d) = (values[0], values[1], values[2], values[3]);
        }
        if (changedParameters) (edited, changedNode) = (edited with { A = a, B = b, C = c, D = d }, true);
        if (node.Kind == ShaderNodeKind.Path)
        {
            UiText.Disabled(Localization.DisplayText("Path"));
            var paths = ShaderPathObjects().ToList();
            if (ImGui.BeginCombo("##shaderPath", Localization.DisplayText(ShaderPathLabel(node.Target))))
            {
                if (ImGui.Selectable(Localization.DisplayText("None"), string.IsNullOrEmpty(node.Target)))
                    (edited, changedNode) = (edited with { Target = null }, true);
                foreach (var path in paths)
                {
                    if (ImGui.Selectable(path.Name + "##" + path.ObjectId, path.ObjectId == node.Target))
                        (edited, changedNode) = (edited with { Target = path.ObjectId }, true);
                }
                ImGui.EndCombo();
            }
            UiText.Disabled(Localization.DisplayText(paths.Count == 0
                ? "Spawn a Path object to follow it. Without one the whole volume stays visible."
                : "Visibility is 100% within Radius of the path between Start and End, fading over Falloff."));
        }
        if (spec.Mode == null && spec.Option == null && spec.Variant == null && spec.ClampLabel == null &&
            spec.Parameters.Length == 0 && node.Kind != ShaderNodeKind.Color && spec.Inputs.Length == 0)
            UiText.Disabled(Localization.DisplayText(node.Kind == ShaderNodeKind.Time
                ? "Outputs the timeline position in seconds and frames." : "This node has no settings."));

        var inputs = spec.Inputs;
        var removeLink = default(ShaderLink?);
        if (inputs.Length > 0)
        {
            ImGui.Spacing();
            ImGui.SeparatorText(Localization.DisplayText("Inputs"));
            var nodes = graph.Nodes.ToDictionary(n => n.Id);
            for (int i = 0; i < inputs.Length; i++)
            {
                int input = i;
                var socket = inputs[i];
                var link = graph.Links.FirstOrDefault(l => l.Target == node.Id && l.Input == input);
                ImGui.PushID(i);
                if (link.Source != 0 && nodes.TryGetValue(link.Source, out var from))
                {
                    ImGui.TextUnformatted(Localization.DisplayText(socket.Name));
                    ImGui.SameLine();
                    string source = "← " + ShaderSourceLabel(from, link.Output);
                    float buttonWidth = ImGui.GetFrameHeight();
                    float x = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(source).X -
                              buttonWidth - ImGui.GetStyle().ItemSpacing.X;
                    ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), x));
                    ImGui.TextUnformatted(source);
                    ImGui.SameLine();
                    if (ImGui.Button("x", new Vector2(buttonWidth, 0))) removeLink = link;
                    if (ImGui.IsItemHovered()) UiText.Tooltip(Localization.DisplayText("Disconnect"));
                }
                else if (socket.Auto != ShaderAutoInput.None)
                {
                    ImGui.TextUnformatted(Localization.DisplayText(socket.Name));
                    ImGui.SameLine();
                    UiText.Disabled(Localization.DisplayText(socket.Auto == ShaderAutoInput.UV
                        ? "UV coordinates" : "Object coordinates"));
                }
                else
                {
                    UiText.Disabled(Localization.DisplayText(socket.Name));
                    var (x, y, z) = ShaderGraph.InputValue(node, i);
                    bool changed;
                    if (socket.Type == ShaderSocketType.Color)
                    {
                        var color = new Vector3(x, y, z);
                        changed = ImGui.ColorEdit3("##value", ref color,
                            ImGuiColorEditFlags.DisplayHex | ImGuiColorEditFlags.Float | ImGuiColorEditFlags.Hdr);
                        (x, y, z) = (color.X, color.Y, color.Z);
                    }
                    else if (socket.Type == ShaderSocketType.Vector)
                    {
                        var vector = new Vector3(x, y, z);
                        changed = ImGui.DragFloat3("##value", ref vector, 0.01f, socket.Min, socket.Max, "%.3f");
                        (x, y, z) = (vector.X, vector.Y, vector.Z);
                    }
                    else if (socket.Percent)
                    {
                        float percent = x * 100f;
                        changed = ImGui.DragFloat("##value", ref percent, 0.25f, socket.Min * 100f, socket.Max * 100f, "%.1f%%",
                            ImGuiSliderFlags.AlwaysClamp);
                        x = percent / 100f;
                        (y, z) = (x, x);
                    }
                    else
                    {
                        float range = socket.Max - socket.Min;
                        float speed = range <= 16 ? 0.005f : range >= 1000 ? 0.05f : 0.01f;
                        changed = ImGui.DragFloat("##value", ref x, speed, socket.Min, socket.Max, "%.3f", ImGuiSliderFlags.AlwaysClamp);
                        (y, z) = (x, x);
                    }
                    if (changed) (edited, changedNode) = (ShaderGraph.WithInputValue(edited, i, x, y, z), true);
                }
                ImGui.PopID();
            }
        }
        ImGui.PopItemWidth();

        if (changedNode) graph = ShaderReplaceNode(obj, graph, original, edited);
        if (removeLink is { } removed)
        {
            graph = graph with { Links = graph.Links.Remove(removed) };
            SetShaderGraph(obj, graph);
        }

        if (ShaderGraph.IsOutput(node.Kind))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(Localization.DisplayText(node.Kind == ShaderNodeKind.FogVolumeOutput
                ? "Turns the object into a fog volume; use a closed shape. The NuxT path tracer marches rays through it with voxel-snapped density. The viewport only shows a translucent preview. Voxel size 0 gives smooth fog; Top Cutoff trims the volume at a fraction of its height."
                : "Transmission refracts the scene behind the surface using IOR and Refraction Depth; Alpha fades it out. Both draw in the transparent pass."));
            return graph;
        }
        ImGui.Spacing();
        ImGui.Separator();
        float buttonWidthHalf = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
        ImGui.BeginDisabled(graph.Nodes.Length >= ShaderGraph.MaxNodes);
        if (ImGui.Button(Localization.DisplayText("Duplicate"), new Vector2(buttonWidthHalf, 0)))
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

    /// <summary>Spaces Color Ramp stops evenly when stops are added, so new stops don't overlap the end.</summary>
    private static ShaderNode SpreadNewRampStops(ShaderNode node, int previousOption)
    {
        int before = Math.Clamp(previousOption, 0, 2) + 2, after = Math.Clamp(node.Option, 0, 2) + 2;
        if (after <= before) return node;
        float[] positions = [node.A, node.B, node.C, node.D];
        for (int k = 0; k < after; k++) positions[k] = k / (float)(after - 1);
        return node with { A = positions[0], B = positions[1], C = positions[2], D = positions[3] };
    }
}
