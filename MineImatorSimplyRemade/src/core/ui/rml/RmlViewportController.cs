using MineImatorSimplyRemade.core.ui.Panels;
using System.Numerics;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Renders the editor scene into an application-owned texture displayed by RmlUi.</summary>
public sealed class RmlViewportController
{
    private readonly Element _root;
    private readonly Viewport _main;
    private readonly Viewport _surface;
    private int _width = 1;
    private int _height = 1;
    private Vector2 _mouse;
    private bool _left;
    private bool _right;
    private bool _middle;
    private bool _control;
    private bool _shift;

    /// <summary>Invoked when the "Add object" button in the viewport header is clicked.</summary>
    public Action? SpawnObjectRequested { get; set; }

    public RmlViewportController(Element root, Viewport main, Viewport surface)
    {
        _root = root;
        _main = main;
        _surface = surface;
        Build();
    }

    public void Render()
    {
        int width = Math.Max(1, (int)_root.GetClientWidth());
        int height = Math.Max(1, (int)_root.GetClientHeight() - 31);
        if (width != _width || height != _height)
        {
            _width = width;
            _height = height;
            _surface.InitPreviewViewport((uint)width, (uint)height);
            Build();
        }
        var spawned = _surface.GetSpawnedCamerasPublic();
        var (camera, sceneObject) = _surface.DrawCameraDropdownInternal(spawned);
        _surface.RenderScenePublic(camera, sceneObject, (uint)width, (uint)height,
            _surface.HighQualityPreviewEnabled, applyCameraEffects: true);
    }

    private void Build()
    {
        List<MineImatorSimplyRemadeNuxi.core.objs.sceneObjects.CameraSceneObject> cameras = _surface.GetSpawnedCamerasPublic();
        int cameraIndex = _surface.SelectedCameraIndex;
        string cameraName = cameraIndex == Viewport.RenderOutputIndex ? "Render Output" : cameraIndex == 0 ? "Work Camera" :
            cameraIndex - 1 < cameras.Count ? cameras[cameraIndex - 1].GetDisplayName() : "Work Camera";
        string source = $"gl-texture://{_surface.ColorTexture}/{_width}/{_height}";
        // Note: SetInnerRml() only parses element markup for the fragment being inserted -
        // unlike a document's <head>, a <style> tag embedded here isn't picked up as a
        // stylesheet by RmlUi, so it would just render as raw literal text. All of the
        // viewport's CSS lives in EditorShell's document-level <style> block instead.
        _root.SetInnerRml($$"""
          <div id="viewport-tools"><button id="viewport-camera">Camera: {{System.Net.WebUtility.HtmlEncode(cameraName)}}</button><button id="viewport-overlays">Overlays</button><button id="viewport-particles">Particles</button>
            <button id="viewport-quality">{{(_surface.HighQualityPreviewEnabled ? "Rendered" : "Solid")}}</button><button id="viewport-shadow">Shadow debug</button>
            <button id="viewport-preview">Preview</button>
            <button id="spawn-object"><img src="embedded://bench"/>Add object</button></div>
          <img id="viewport-image" tabindex="0" src="{{source}}"/>
          """);
        Bind("viewport-overlays", () => _main.OverlaysEnabled = !_main.OverlaysEnabled);
        Bind("viewport-particles", () => _main.ParticlePreviewEnabled = !_main.ParticlePreviewEnabled);
        Bind("viewport-camera", () => { int next = _surface.SelectedCameraIndex + 1; if (next > cameras.Count) next = Viewport.RenderOutputIndex; _surface.SelectedCameraIndex = next; Build(); });
        Bind("viewport-quality", () => { _surface.ToggleHighQualityPreview(); Build(); });
        Bind("viewport-shadow", () => { _surface.ToggleShadowDebugMode(); Build(); });
        Bind("viewport-preview", () => _main.PreviewViewport?.ToggleInlineVisibility());
        Bind("spawn-object", () => SpawnObjectRequested?.Invoke());
        Element? image = _root.GetElementById("viewport-image");
        image?.AddEventListener("mousemove", e => { ReadMouse(e); PushInput(); });
        image?.AddEventListener("mousedown", e => { ReadMouse(e); int button = Number(e, "button"); SetButton(button, true); PushInput(leftPressed: button == 0, rightPressed: button == 1); });
        image?.AddEventListener("mouseup", e => { ReadMouse(e); int button = Number(e, "button"); SetButton(button, false); PushInput(leftReleased: button == 0, rightReleased: button == 1); });
        image?.AddEventListener("wheel", e => { ReadMouse(e); PushInput(wheel: -Float(e, "wheel_delta_y", Float(e, "delta_y"))); });
        image?.AddEventListener("keydown", e => { UpdateModifier(e); PushKey(e, true); });
        image?.AddEventListener("keyup", e => { UpdateModifier(e); PushKey(e, false); });
    }

    private void ReadMouse(Event e)
    {
        _mouse = new Vector2(Float(e, "mouse_x", Float(e, "client_x")), Float(e, "mouse_y", Float(e, "client_y")));
        UpdateModifier(e);
    }

    private void UpdateModifier(Event e)
    {
        _control = Flag(e, "ctrl_key") || Flag(e, "meta_key");
        _shift = Flag(e, "shift_key");
    }

    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private void PushKey(Event e, bool down)
    {
        string key = Text(e, "key_identifier", Text(e, "key"));
        if (down) _keys.Add(key); else _keys.Remove(key);
        PushInput(keyG: down && IsKey(key, "G"), keyR: down && IsKey(key, "R"));
    }

    private void SetButton(int button, bool down) { if (button == 0) _left = down; else if (button == 1) _right = down; else if (button == 2) _middle = down; }
    private void PushInput(bool leftPressed = false, bool rightPressed = false, bool leftReleased = false, bool rightReleased = false, float wheel = 0, bool keyG = false, bool keyR = false)
    {
        var spawned = _surface.GetSpawnedCamerasPublic();
        var (camera, sceneObject) = _surface.DrawCameraDropdownInternal(spawned);
        Vector2 min = new(_root.GetAbsoluteLeft(), _root.GetAbsoluteTop() + 31f);
        Vector2 size = new(_width, _height);
        _surface.ProcessExternalPreviewInput(camera, sceneObject, min, size, _mouse, _left, _right, _middle,
            leftPressed, rightPressed, leftReleased, rightReleased, wheel, _control, _shift, 1f / 60f, true,
            Down("W"), Down("S"), Down("A"), Down("D"), Down("E"), Down("Q"), Down("SPACE"), keyG, keyR);
    }

    private bool Down(string name) => _keys.Any(key => IsKey(key, name));
    private static bool IsKey(string value, string name) => value.Equals(name, StringComparison.OrdinalIgnoreCase) || value.EndsWith("_" + name, StringComparison.OrdinalIgnoreCase);
    private static object? Parameter(Event e, string name) => e.Parameters.TryGetValue(name, out object? value) ? value : null;
    private static float Float(Event e, string name, float fallback = 0) => Parameter(e, name) is object value && float.TryParse(value.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result) ? result : fallback;
    private static int Number(Event e, string name) => Parameter(e, name) is object value && int.TryParse(value.ToString(), out int result) ? result : 0;
    private static bool Flag(Event e, string name) => Parameter(e, name) is object value && (value is bool flag ? flag : bool.TryParse(value.ToString(), out bool result) && result);
    private static string Text(Event e, string name, string fallback = "") => Parameter(e, name)?.ToString() ?? fallback;

    private void Bind(string id, Action action) => _root.GetElementById(id)?.AddEventListener("click", _ => action());
}
