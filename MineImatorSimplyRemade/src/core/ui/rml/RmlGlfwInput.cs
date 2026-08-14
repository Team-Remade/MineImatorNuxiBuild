using System.Text;
using RmlUiNet;
using RmlUiNet.Input;
using Silk.NET.GLFW;
using GlfwKey = Silk.NET.GLFW.Keys;
using GlfwMouseButton = Silk.NET.GLFW.MouseButton;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Forwards GLFW input callbacks to one RmlUi context.</summary>
public sealed unsafe class RmlGlfwInput : IDisposable
{
    private readonly Glfw _glfw;
    private readonly WindowHandle* _window;
    private readonly Context _context;
    private readonly GlfwCallbacks.CursorPosCallback _cursorPos;
    private readonly GlfwCallbacks.CursorEnterCallback _cursorEnter;
    private readonly GlfwCallbacks.MouseButtonCallback _mouseButton;
    private readonly GlfwCallbacks.ScrollCallback _scroll;
    private readonly GlfwCallbacks.KeyCallback _key;
    private readonly GlfwCallbacks.CharCallback _character;

    public RmlGlfwInput(Glfw glfw, WindowHandle* window, Context context)
    {
        _glfw = glfw;
        _window = window;
        _context = context;
        _cursorPos = OnCursorPos;
        _cursorEnter = OnCursorEnter;
        _mouseButton = OnMouseButton;
        _scroll = OnScroll;
        _key = OnKey;
        _character = OnCharacter;
        glfw.SetCursorPosCallback(window, _cursorPos);
        glfw.SetCursorEnterCallback(window, _cursorEnter);
        glfw.SetMouseButtonCallback(window, _mouseButton);
        glfw.SetScrollCallback(window, _scroll);
        glfw.SetKeyCallback(window, _key);
        glfw.SetCharCallback(window, _character);
    }

    private KeyModifier Modifiers(KeyModifiers modifiers)
    {
        KeyModifier result = KeyModifier.None;
        if ((modifiers & KeyModifiers.Control) != 0) result |= KeyModifier.KM_CTRL;
        if ((modifiers & KeyModifiers.Shift) != 0) result |= KeyModifier.KM_SHIFT;
        if ((modifiers & KeyModifiers.Alt) != 0) result |= KeyModifier.KM_ALT;
        if ((modifiers & KeyModifiers.Super) != 0) result |= KeyModifier.KM_META;
        return result;
    }

    private KeyModifier CurrentModifiers()
    {
        KeyModifier result = KeyModifier.None;
        if (Down(GlfwKey.ControlLeft) || Down(GlfwKey.ControlRight)) result |= KeyModifier.KM_CTRL;
        if (Down(GlfwKey.ShiftLeft) || Down(GlfwKey.ShiftRight)) result |= KeyModifier.KM_SHIFT;
        if (Down(GlfwKey.AltLeft) || Down(GlfwKey.AltRight)) result |= KeyModifier.KM_ALT;
        if (Down(GlfwKey.SuperLeft) || Down(GlfwKey.SuperRight)) result |= KeyModifier.KM_META;
        return result;
    }

    private bool Down(GlfwKey key) => _glfw.GetKey(_window, key) is (int)InputAction.Press or (int)InputAction.Repeat;

    private void OnCursorPos(WindowHandle* window, double x, double y) =>
        _context.ProcessMouseMove((int)x, (int)y, CurrentModifiers());

    private void OnCursorEnter(WindowHandle* window, bool entered)
    {
        if (!entered) _context.ProcessMouseLeave();
    }

    private void OnMouseButton(WindowHandle* window, GlfwMouseButton button, InputAction action, KeyModifiers modifiers)
    {
        int index = button switch
        {
            GlfwMouseButton.Left => 0,
            GlfwMouseButton.Right => 1,
            GlfwMouseButton.Middle => 2,
            _ => (int)button
        };
        if (action == InputAction.Press) _context.ProcessMouseButtonDown(index, Modifiers(modifiers));
        else if (action == InputAction.Release) _context.ProcessMouseButtonUp(index, Modifiers(modifiers));
    }

    private void OnScroll(WindowHandle* window, double x, double y) =>
        _context.ProcessMouseWheel(new Vector2f((float)-x, (float)-y), CurrentModifiers());

    private void OnKey(WindowHandle* window, GlfwKey key, int scanCode, InputAction action, KeyModifiers modifiers)
    {
        KeyIdentifier identifier = MapKey(key);
        if (action is InputAction.Press or InputAction.Repeat)
            _context.ProcessKeyDown(identifier, Modifiers(modifiers));
        else if (action == InputAction.Release)
            _context.ProcessKeyUp(identifier, Modifiers(modifiers));
    }

    private void OnCharacter(WindowHandle* window, uint codepoint)
    {
        if (Rune.TryCreate(codepoint, out Rune rune)) _context.ProcessTextInput(rune.ToString());
    }

    private static KeyIdentifier MapKey(GlfwKey key)
    {
        if (key >= GlfwKey.A && key <= GlfwKey.Z)
            return (KeyIdentifier)((int)KeyIdentifier.KI_A + (key - GlfwKey.A));
        if (key >= GlfwKey.Number0 && key <= GlfwKey.Number9)
            return (KeyIdentifier)((int)KeyIdentifier.KI_0 + (key - GlfwKey.Number0));
        if (key >= GlfwKey.F1 && key <= GlfwKey.F24)
            return (KeyIdentifier)((int)KeyIdentifier.KI_F1 + (key - GlfwKey.F1));
        return key switch
        {
            GlfwKey.Space => KeyIdentifier.KI_SPACE,
            GlfwKey.Backspace => KeyIdentifier.KI_BACK,
            GlfwKey.Tab => KeyIdentifier.KI_TAB,
            GlfwKey.Enter => KeyIdentifier.KI_RETURN,
            GlfwKey.Escape => KeyIdentifier.KI_ESCAPE,
            GlfwKey.PageUp => KeyIdentifier.KI_PRIOR,
            GlfwKey.PageDown => KeyIdentifier.KI_NEXT,
            GlfwKey.End => KeyIdentifier.KI_END,
            GlfwKey.Home => KeyIdentifier.KI_HOME,
            GlfwKey.Left => KeyIdentifier.KI_LEFT,
            GlfwKey.Up => KeyIdentifier.KI_UP,
            GlfwKey.Right => KeyIdentifier.KI_RIGHT,
            GlfwKey.Down => KeyIdentifier.KI_DOWN,
            GlfwKey.Insert => KeyIdentifier.KI_INSERT,
            GlfwKey.Delete => KeyIdentifier.KI_DELETE,
            GlfwKey.ShiftLeft => KeyIdentifier.KI_LSHIFT,
            GlfwKey.ShiftRight => KeyIdentifier.KI_RSHIFT,
            GlfwKey.ControlLeft => KeyIdentifier.KI_LCONTROL,
            GlfwKey.ControlRight => KeyIdentifier.KI_RCONTROL,
            GlfwKey.AltLeft => KeyIdentifier.KI_LMENU,
            GlfwKey.AltRight => KeyIdentifier.KI_RMENU,
            GlfwKey.SuperLeft => KeyIdentifier.KI_LMETA,
            GlfwKey.SuperRight => KeyIdentifier.KI_RMETA,
            GlfwKey.Minus => KeyIdentifier.KI_OEM_MINUS,
            GlfwKey.Equal => KeyIdentifier.KI_OEM_PLUS,
            GlfwKey.Comma => KeyIdentifier.KI_OEM_COMMA,
            GlfwKey.Period => KeyIdentifier.KI_OEM_PERIOD,
            GlfwKey.Slash => KeyIdentifier.KI_OEM_2,
            GlfwKey.Semicolon => KeyIdentifier.KI_OEM_1,
            GlfwKey.Apostrophe => KeyIdentifier.KI_OEM_7,
            GlfwKey.LeftBracket => KeyIdentifier.KI_OEM_4,
            GlfwKey.RightBracket => KeyIdentifier.KI_OEM_6,
            GlfwKey.BackSlash => KeyIdentifier.KI_OEM_5,
            GlfwKey.GraveAccent => KeyIdentifier.KI_OEM_3,
            _ => KeyIdentifier.KI_UNKNOWN
        };
    }

    public void Dispose()
    {
        _glfw.SetCursorPosCallback(_window, null);
        _glfw.SetCursorEnterCallback(_window, null);
        _glfw.SetMouseButtonCallback(_window, null);
        _glfw.SetScrollCallback(_window, null);
        _glfw.SetKeyCallback(_window, null);
        _glfw.SetCharCallback(_window, null);
    }
}

