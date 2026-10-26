using Hexa.NET.ImGui;

namespace MineImatorSimplyRemade.core.ui;

public static class Keybinds
{
    private enum BindingScope { General, Timeline, Flight }
    private readonly record struct ActionBinding(string Id, string Default, string TextKey,
        BindingScope Scope = BindingScope.General, bool Held = false, bool Repeat = false, bool ExtraModifiers = false);
    private readonly record struct Chord(ImGuiKey Key, bool Ctrl, bool Shift, bool Alt, bool Super)
    {
        public override string ToString() =>
            (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") +
            (Alt ? "Alt+" : "") + (Super ? "Super+" : "") + (Key switch
            {
                ImGuiKey.ModCtrl => "Ctrl",
                ImGuiKey.ModShift => "Shift",
                ImGuiKey.ModAlt => "Alt",
                ImGuiKey.ModSuper => "Super",
                _ => Key.ToString()
            });
    }

    private static readonly ActionBinding[] Actions =
    [
        new("newProject", "Ctrl+N", "text.common.new_project"),
        new("openProject", "Ctrl+O", "text.menu.open_project"),
        new("saveProject", "Ctrl+S", "text.menu.save_project"),
        new("saveProjectAs", "Ctrl+Shift+S", "text.menu.save_as"),
        new("undo", "Ctrl+Z", "text.menu.undo"),
        new("redo", "Ctrl+Y", "text.menu.redo"),
        new("redoAlternate", "Ctrl+Shift+Z", "text.keybinds.redo_alternate"),
        new("duplicate", "Ctrl+D", "text.common.duplicate"),
        new("delete", "Delete", "text.common.delete"),
        new("cut", "Ctrl+X", "text.menu.cut", BindingScope.Timeline),
        new("copy", "Ctrl+C", "text.menu.copy", BindingScope.Timeline),
        new("paste", "Ctrl+V", "text.menu.paste", BindingScope.Timeline),
        new("selectAll", "Ctrl+A", "text.menu.select_all"),
        new("preferences", "Ctrl+Comma", "text.common.preferences"),
        new("importAsset", "Ctrl+I", "text.common.import_asset"),
        new("hide", "Ctrl+H", "text.menu.hide"),
        new("show", "Ctrl+Shift+H", "text.common.show"),
        new("rename", "F2", "text.common.rename"),
        new("group", "Ctrl+F", "text.keybinds.group"),
        new("playPause", "Space", "text.keybinds.play_pause", BindingScope.Timeline),
        new("stepBackward", "LeftArrow", "text.keybinds.step_backward", BindingScope.Timeline),
        new("stepForward", "RightArrow", "text.keybinds.step_forward", BindingScope.Timeline),
        new("jumpToStart", "Home", "text.keybinds.jump_to_start", BindingScope.Timeline),
        new("jumpToEnd", "End", "text.keybinds.jump_to_end", BindingScope.Timeline),
        new("highQualityPreview", "F5", "text.keybinds.high_quality_preview"),
        new("shadowDebug", "Ctrl+F6", "text.keybinds.shadow_debug"),
        new("renderImage", "F7", "text.menu.render_image"),
        new("renderAnimation", "F8", "text.menu.render_animation"),
        new("renderingDebug", "F11", "text.keybinds.rendering_debug"),
        new("metrics", "F12", "text.keybinds.metrics"),
        new("toggleGizmoSpace", "G", "text.keybinds.toggle_gizmo_space", Repeat: true, ExtraModifiers: true),
        new("resetFlyCamera", "R", "text.keybinds.reset_fly_camera", BindingScope.Flight),
        new("flyForward", "W", "text.keybinds.fly_forward", BindingScope.Flight, Held: true),
        new("flyBackward", "S", "text.keybinds.fly_backward", BindingScope.Flight, Held: true),
        new("flyLeft", "A", "text.keybinds.fly_left", BindingScope.Flight, Held: true),
        new("flyRight", "D", "text.keybinds.fly_right", BindingScope.Flight, Held: true),
        new("flyUp", "E", "text.keybinds.fly_up", BindingScope.Flight, Held: true),
        new("flyDown", "Q", "text.keybinds.fly_down", BindingScope.Flight, Held: true),
        new("flyFaster", "Space", "text.keybinds.fly_faster", BindingScope.Flight, Held: true),
        new("flySlower", "Shift", "text.keybinds.fly_slower", BindingScope.Flight, Held: true)
    ];
    private static readonly Dictionary<string, string> Defaults = Actions.ToDictionary(a => a.Id, a => a.Default, StringComparer.Ordinal);
    private static readonly Dictionary<string, ActionBinding> Definitions = Actions.ToDictionary(a => a.Id, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Bindings = new(StringComparer.Ordinal);
    private static int _selected;
    private static bool _capturing;
    private static bool _armed;
    private static bool _waitForRelease;
    private static ImGuiContextPtr _captureContext;
    private static int _captureEndFrame;
    private static string? _conflictId;
    private static Chord? _modifierCandidate;

    // Return a snapshot so callers cannot bypass chord validation.
    public static Dictionary<string, string> Overrides => new(Bindings, StringComparer.Ordinal);

    public static bool IsCapturing
    {
        get
        {
            if (_waitForRelease && _captureContext.Equals(ImGui.GetCurrentContext()) &&
                ImGui.GetFrameCount() > _captureEndFrame && !AnyInputDown())
                _waitForRelease = false;
            return _capturing || _waitForRelease;
        }
    }

    public static string Label(string id) => Defaults.TryGetValue(id, out string? fallback)
        ? Bindings.GetValueOrDefault(id, fallback) : "";

    public static string DisplayLabel(string id) => Label(id) is { Length: > 0 } label
        ? label : Localization.T("text.keybinds.unbound");

    public static bool Pressed(string id) => Match(id, held: false);
    public static bool Down(string id) => Match(id, held: true);

    private static bool Match(string id, bool held)
    {
        if (IsCapturing || !Definitions.TryGetValue(id, out var action) ||
            (action.Scope == BindingScope.Timeline && Input.IsAnyFreeFlyActive))
            return false;
        var io = ImGui.GetIO();
        if (io.WantTextInput || ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel))
            return false;
        if (!TryParse(Label(id), out var chord)) return false;
        bool ctrl = chord.Ctrl || chord.Key == ImGuiKey.ModCtrl;
        bool shift = chord.Shift || chord.Key == ImGuiKey.ModShift;
        bool alt = chord.Alt || chord.Key == ImGuiKey.ModAlt;
        bool super = chord.Super || chord.Key == ImGuiKey.ModSuper;
        // Flight keys must still work with the speed modifiers held (e.g. Shift+W).
        bool extra = action.Scope == BindingScope.Flight || action.ExtraModifiers;
        return (extra ? (!ctrl || io.KeyCtrl) && (!shift || io.KeyShift) &&
                       (!alt || io.KeyAlt) && (!super || io.KeySuper)
                     : io.KeyCtrl == ctrl && io.KeyShift == shift && io.KeyAlt == alt && io.KeySuper == super) &&
            (held ? ImGui.IsKeyDown(chord.Key) : ImGui.IsKeyPressed(chord.Key, action.Repeat));
    }

    // Arming watches effective primary keys, even if their modifiers are not held yet.
    public static bool AllReleased(params string[] ids) => ids.All(id =>
        !TryParse(Label(id), out var chord) || !ImGui.IsKeyDown(chord.Key));

    public static void LoadOverrides(Dictionary<string, string>? overrides)
    {
        // Materialize before clearing, including when handed a previously obtained snapshot.
        var valid = new Dictionary<string, string>(StringComparer.Ordinal);
        if (overrides != null)
        {
            foreach (var (id, value) in overrides)
            {
                if (!Defaults.TryGetValue(id, out string? fallback) || value == null)
                    continue;
                string normalized;
                if (value.Length == 0)
                    normalized = "";
                else if (TryParse(value, out var chord) && (!IsModifier(chord.Key) || Definitions[id].Held))
                    normalized = chord.ToString();
                else
                    continue;
                if (normalized != fallback)
                    valid[id] = normalized;
            }
        }
        Bindings.Clear();
        foreach (var (id, value) in valid)
            Bindings[id] = value;
    }

    public static void Reset()
    {
        Bindings.Clear();
        _conflictId = null;
        CancelCapture();
    }

    internal static void CancelCapture()
    {
        if (_capturing)
        {
            _waitForRelease = true;
            _captureEndFrame = ImGui.GetFrameCount();
        }
        _capturing = false;
        _armed = false;
        _modifierCandidate = null;
    }

    public static bool RenderPreferences()
    {
        if (!ImGui.CollapsingHeader(Localization.Label("text.keybinds.title", "Keybinds##PrefKeybinds")))
        {
            CancelCapture();
            return false;
        }

        bool changed = false;
        ImGui.Indent();
        ImGui.BeginDisabled(_capturing);
        ImGui.SetNextItemWidth(280 * UiTheme.Scale);
        if (ImGui.BeginCombo(Localization.Label("text.keybinds.action", "Action##KeybindAction"), Localization.T(Actions[_selected].TextKey)))
        {
            for (int i = 0; i < Actions.Length; i++)
            {
                if (ImGui.Selectable(Localization.T(Actions[i].TextKey) + "##" + Actions[i].Id, i == _selected))
                {
                    _selected = i;
                    _conflictId = null;
                }
            }
            ImGui.EndCombo();
        }
        ImGui.EndDisabled();
        ImGui.TextWrapped(Localization.T("text.keybinds.scope_hint"));

        string id = Actions[_selected].Id;
        if (Actions[_selected].Held)
            ImGui.TextWrapped(Localization.T("text.keybinds.held_hint"));
        string label = Label(id);
        ImGui.TextUnformatted(Localization.Format("text.keybinds.current", label.Length == 0 ? Localization.T("text.keybinds.unbound") : label));

        if (_capturing)
        {
            ImGui.TextWrapped(Localization.T(_armed ? "text.keybinds.capture_prompt" : "text.keybinds.release_prompt"));
            if (ImGui.IsKeyPressed(ImGuiKey.Escape, false) || ImGui.Button(Localization.Label("text.common.cancel", "Cancel##KeybindCancel")))
                CancelCapture();
            else if (!_armed)
                _armed = !AnyInputDown();
            else
            {
                var io = ImGui.GetIO();
                for (var key = ImGuiKey.Tab; key < ImGuiKey.GamepadStart; key++)
                {
                    if (!IsBindable(key) || !ImGui.IsKeyPressed(key, false))
                        continue;
                    changed = SetBinding(id, new Chord(key, io.KeyCtrl, io.KeyShift, io.KeyAlt, io.KeySuper).ToString());
                    CancelCapture();
                    break;
                }
                if (_capturing && Actions[_selected].Held)
                {
                    int modifierCount = (io.KeyCtrl ? 1 : 0) + (io.KeyShift ? 1 : 0) +
                        (io.KeyAlt ? 1 : 0) + (io.KeySuper ? 1 : 0);
                    if (modifierCount == 1)
                        _modifierCandidate = new Chord(io.KeyCtrl ? ImGuiKey.ModCtrl : io.KeyShift ? ImGuiKey.ModShift :
                            io.KeyAlt ? ImGuiKey.ModAlt : ImGuiKey.ModSuper, false, false, false, false);
                    else if (modifierCount > 1)
                        _modifierCandidate = null;
                    else if (_modifierCandidate is { } modifier)
                    {
                        changed = SetBinding(id, modifier.ToString());
                        CancelCapture();
                    }
                }
            }
        }
        else
        {
            if (ImGui.Button(Localization.Label("text.keybinds.rebind", "Capture / Rebind##KeybindCapture")))
            {
                _capturing = true;
                _captureContext = ImGui.GetCurrentContext();
                _armed = false;
                _conflictId = null;
            }
            ImGui.SameLine();
            if (ImGui.Button(Localization.Label("text.keybinds.unbind", "Unbind##KeybindUnbind")))
                changed |= SetBinding(id, "");
            if (ImGui.Button(Localization.Label("text.keybinds.reset_selected", "Reset Selected##KeybindReset")))
                changed |= SetBinding(id, Defaults[id]);
            ImGui.SameLine();
            if (ImGui.Button(Localization.Label("text.keybinds.reset_all", "Reset All Keybinds##KeybindResetAll")))
            {
                changed |= Bindings.Count != 0;
                Reset();
            }
        }

        if (_conflictId != null)
            ImGui.TextWrapped(Localization.Format("text.keybinds.conflict", Localization.T(Actions.First(a => a.Id == _conflictId).TextKey)));
        ImGui.Unindent();
        return changed;
    }

    private static bool SetBinding(string id, string value)
    {
        _conflictId = value.Length == 0 ? null : Actions.FirstOrDefault(a => a.Id != id && Conflicts(Definitions[id], value, a)).Id;
        if (_conflictId != null)
            return false;
        bool changed = Label(id) != value;
        if (value == Defaults[id])
            Bindings.Remove(id);
        else
            Bindings[id] = value;
        return changed;
    }

    private static bool Conflicts(ActionBinding action, string value, ActionBinding other)
    {
        // Flight owns its keyboard input while RMB navigation is active; timeline commands are suspended.
        if ((action.Scope == BindingScope.Flight && other.Scope == BindingScope.Timeline) ||
            (other.Scope == BindingScope.Flight && action.Scope == BindingScope.Timeline)) return false;
        string otherValue = Label(other.Id);
        if (value == otherValue) return true;
        if (!TryParse(value, out var chord) || !TryParse(otherValue, out var otherChord) || chord.Key != otherChord.Key)
            return false;
        // Preserve shipped combinations such as Ctrl+S (save) with S (fly backward).
        if (value == action.Default && otherValue == other.Default) return false;
        bool extra = action.Scope == BindingScope.Flight || action.ExtraModifiers;
        bool otherExtra = other.Scope == BindingScope.Flight || other.ExtraModifiers;
        return (extra && otherExtra) ||
            (extra && (!chord.Ctrl || otherChord.Ctrl) && (!chord.Shift || otherChord.Shift) &&
                      (!chord.Alt || otherChord.Alt) && (!chord.Super || otherChord.Super)) ||
            (otherExtra && (!otherChord.Ctrl || chord.Ctrl) && (!otherChord.Shift || chord.Shift) &&
                           (!otherChord.Alt || chord.Alt) && (!otherChord.Super || chord.Super));
    }

    private static bool IsModifier(ImGuiKey key) => key is ImGuiKey.ModCtrl or ImGuiKey.ModShift or ImGuiKey.ModAlt or ImGuiKey.ModSuper;

    private static bool IsBindable(ImGuiKey key) => key >= ImGuiKey.Tab && key < ImGuiKey.GamepadStart &&
        !(key >= ImGuiKey.LeftCtrl && key <= ImGuiKey.RightSuper);

    private static bool TryParse(string value, out Chord chord)
    {
        chord = default;
        string[] parts = value.Split('+', StringSplitOptions.TrimEntries);
        bool ctrl = false, shift = false, alt = false, super = false;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToUpperInvariant())
            {
                case "CTRL" when !ctrl: ctrl = true; break;
                case "SHIFT" when !shift: shift = true; break;
                case "ALT" when !alt: alt = true; break;
                case "SUPER" when !super: super = true; break;
                default: return false;
            }
        }
        // Enum.TryParse also accepts numeric strings; accept only named keyboard keys.
        string name = parts[^1];
        ImGuiKey modifierKey = name.ToUpperInvariant() switch
        {
            "CTRL" => ImGuiKey.ModCtrl,
            "SHIFT" => ImGuiKey.ModShift,
            "ALT" => ImGuiKey.ModAlt,
            "SUPER" => ImGuiKey.ModSuper,
            _ => ImGuiKey.None
        };
        if (modifierKey != ImGuiKey.None)
        {
            if ((modifierKey == ImGuiKey.ModCtrl && ctrl) || (modifierKey == ImGuiKey.ModShift && shift) ||
                (modifierKey == ImGuiKey.ModAlt && alt) || (modifierKey == ImGuiKey.ModSuper && super)) return false;
            chord = new(modifierKey, ctrl, shift, alt, super);
            return true;
        }
        if (!Enum.TryParse(name, true, out ImGuiKey key) || !IsBindable(key) ||
            !string.Equals(name, key.ToString(), StringComparison.OrdinalIgnoreCase))
            return false;
        chord = new(key, ctrl, shift, alt, super);
        return true;
    }

    private static bool AnyInputDown()
    {
        var io = ImGui.GetIO();
        if (io.KeyCtrl || io.KeyShift || io.KeyAlt || io.KeySuper)
            return true;
        for (var key = ImGuiKey.Tab; key < ImGuiKey.GamepadStart; key++)
            if (ImGui.IsKeyDown(key))
                return true;
        for (int button = 0; button < 5; button++)
            if (ImGui.IsMouseDown((ImGuiMouseButton)button))
                return true;
        return false;
    }
}
