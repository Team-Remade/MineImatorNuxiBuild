using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.gizmo;
using Silk.NET.GLFW;
using GlmSharp;

namespace MineImatorSimplyRemade.core;

/// <summary>
/// Centralized input handler for camera controls and viewport interaction.
/// Abstracts away ImGui dependencies to support both main and undocked windows.
/// </summary>
public class Input
{
    // ── Free-fly state ──────────────────────────────────────────────────────

    private bool  _freeFlyActive;
    private float _freeFlySpeed = 5f;
    private double _lastMouseX = double.NaN;
    private double _lastMouseY = double.NaN;

    // ── Orbit/pan state ─────────────────────────────────────────────────────

    private bool  _dragging;
    private bool  _panning;
    private bool  _gizmoDragging;
    private double _orbitLastMouseX = double.NaN;
    private double _orbitLastMouseY = double.NaN;
    private double _panLastMouseX = double.NaN;
    private double _panLastMouseY = double.NaN;
    private float _pressMouseX = float.NaN;
    private float _pressMouseY = float.NaN;
    private const float OrbitDragThreshold = 4f;

    // ── Pick state ──────────────────────────────────────────────────────────

    private float _pendingPickX = float.NaN;
    private float _pendingPickY = float.NaN;
    private bool _pendingPickCtrl;

    // ── Public accessors ────────────────────────────────────────────────────

    public bool IsFreeFlyActive => _freeFlyActive;
    public bool IsDragging => _dragging;
    public bool IsPanning => _panning;
    public bool IsGizmoDragging => _gizmoDragging;
    public float FreeFlySpeed => _freeFlySpeed;
    public bool HasPendingPick => !float.IsNaN(_pendingPickX);
    public (float x, float y) PendingPickPosition => (_pendingPickX, _pendingPickY);
    public bool PendingPickCtrl => _pendingPickCtrl;

    // ── Constants ───────────────────────────────────────────────────────────

    private const float FreeFlyLookSensitivity = 0.003f;

    /// <summary>
    /// Process all input for the viewport given explicit input state.
    /// This allows the same logic to work with both main and undocked windows.
    /// </summary>
    public unsafe void ProcessInput(
        Camera camera,
        Gizmo3D? gizmo,
        Vector2 imageMin,
        Vector2 imageSize,
        Glfw? glfw,
        WindowHandle* glfwWindow,
        Vector2 mousePos,
        bool mouseDown_Left,
        bool mouseDown_Right,
        bool mouseDown_Middle,
        bool mouseClicked_Left,
        bool mouseClicked_Right,
        bool mouseReleased_Left,
        bool mouseReleased_Right,
        float mouseWheel,
        bool keyDown_W, bool keyDown_S, bool keyDown_A, bool keyDown_D,
        bool keyDown_E, bool keyDown_Q, bool keyDown_Space, bool keyDown_Shift,
        bool keyPressed_G, bool keyPressed_R,
        float deltaTime,
        bool windowHovered)
    {
        // ── Gizmo global-space toggle (G key) ──────────────────────────────
        if (gizmo != null && gizmo.Visible && !gizmo.Editing && keyPressed_G)
            gizmo.UseLocalSpace = !gizmo.UseLocalSpace;

        // ── Orbit input (left mouse button) ────────────────────────────────
        ProcessOrbitInput(camera, gizmo, mousePos, mouseDown_Left, mouseClicked_Left,
                         mouseReleased_Left, windowHovered, imageMin, imageSize);

        // ── Pan input (middle mouse button) ────────────────────────────────
        ProcessPanInput(camera, mousePos, mouseDown_Middle, windowHovered);

        // ── Zoom input (scroll wheel) ──────────────────────────────────────
        ProcessZoomInput(camera, mouseWheel, windowHovered);

        // ── Free-fly input (right mouse button) ────────────────────────────
        ProcessFreeFlyInput(camera, glfw, glfwWindow, imageMin, imageSize,
                           mousePos, mouseClicked_Right, mouseReleased_Right,
                           keyDown_W, keyDown_S, keyDown_A, keyDown_D,
                           keyDown_E, keyDown_Q, keyDown_Space, keyDown_Shift,
                           keyPressed_R, mouseWheel, deltaTime, windowHovered);
    }

    /// <summary>
    /// Resets pending pick state. Call this after consuming a pending pick.
    /// </summary>
    public void ClearPendingPick()
    {
        _pendingPickX = float.NaN;
        _pendingPickY = float.NaN;
        _pendingPickCtrl = false;
    }

    private void ProcessOrbitInput(
        Camera camera,
        Gizmo3D? gizmo,
        Vector2 mousePos,
        bool mouseDown_Left,
        bool mouseClicked_Left,
        bool mouseReleased_Left,
        bool windowHovered,
        Vector2 imageMin,
        Vector2 imageSize)
    {
        // Seed last position on first call to avoid spurious delta
        if (double.IsNaN(_orbitLastMouseX))
        {
            _orbitLastMouseX = mousePos.X;
            _orbitLastMouseY = mousePos.Y;
        }

        float dx = (float)(mousePos.X - _orbitLastMouseX);
        float dy = (float)(mousePos.Y - _orbitLastMouseY);

        // Update hover when not dragging
        if (!mouseDown_Left && gizmo != null)
            gizmo.UpdateHover(mousePos, camera, imageMin, imageSize);

        // Left-button pressed
        if (mouseDown_Left)
        {
            if (!_dragging && !_gizmoDragging)
            {
                // Record press position for drag vs click discrimination
                if (float.IsNaN(_pressMouseX))
                {
                    _pressMouseX = mousePos.X;
                    _pressMouseY = mousePos.Y;
                }

                // Try gizmo first
                if (gizmo != null && gizmo.TryBeginEdit(mousePos, camera, imageMin, imageSize))
                {
                    _gizmoDragging = true;
                }
                else
                {
                    // Check orbit drag threshold
                    float moveDist = MathF.Sqrt(
                        (mousePos.X - _pressMouseX) * (mousePos.X - _pressMouseX) +
                        (mousePos.Y - _pressMouseY) * (mousePos.Y - _pressMouseY));
                    if (moveDist >= OrbitDragThreshold)
                        _dragging = true;
                }
            }

            if (_gizmoDragging)
                gizmo?.ContinueEdit(mousePos);
            else if (_dragging)
                camera.Orbit(dx * 0.005f, dy * 0.005f);
        }
        else if (mouseReleased_Left)
        {
            // Left-button released
            bool wasGizmoDragging = _gizmoDragging;
            bool wasOrbitDragging = _dragging;

            if (_gizmoDragging) gizmo?.EndEdit();
            _dragging = false;
            _gizmoDragging = false;

            // Queue pick if not from gizmo/orbit
            bool gizmoHovering = gizmo?.Hovering ?? false;
            if (!wasGizmoDragging && !wasOrbitDragging && !gizmoHovering)
            {
                _pendingPickX = mousePos.X;
                _pendingPickY = mousePos.Y;
                _pendingPickCtrl = false; // Note: Ctrl state would need to be passed separately
            }

            _pressMouseX = float.NaN;
            _pressMouseY = float.NaN;
        }

        _orbitLastMouseX = mousePos.X;
        _orbitLastMouseY = mousePos.Y;
    }

    private void ProcessPanInput(
        Camera camera,
        Vector2 mousePos,
        bool mouseDown_Middle,
        bool windowHovered)
    {
        if (!windowHovered)
        {
            _panLastMouseX = double.NaN;
            _panLastMouseY = double.NaN;
            _panning = false;
            return;
        }

        if (double.IsNaN(_panLastMouseX))
        {
            _panLastMouseX = mousePos.X;
            _panLastMouseY = mousePos.Y;
            return;
        }

        float dx = (float)(mousePos.X - _panLastMouseX);
        float dy = (float)(mousePos.Y - _panLastMouseY);

        if (mouseDown_Middle)
        {
            if (_panning)
                camera.Pan(-dx * 0.01f * (camera.Distance / 5f),
                           dy * 0.01f * (camera.Distance / 5f));
            _panning = true;
        }
        else
        {
            _panning = false;
        }

        _panLastMouseX = mousePos.X;
        _panLastMouseY = mousePos.Y;
    }

    private void ProcessZoomInput(
        Camera camera,
        float mouseWheel,
        bool windowHovered)
    {
        if (!windowHovered || _freeFlyActive || mouseWheel == 0) return;
        
        // Skip zoom for first-person cameras (Distance near-zero).
        // CameraSceneObject sets Distance = 0.001f to use first-person mode where
        // the eye position directly equals the object's Position. Applying zoom
        // would change Distance and recalculate Position, causing apparent teleports.
        if (camera.Distance < 0.01f) return;
        
        camera.Zoom(mouseWheel * camera.Distance * 0.1f);
    }

    private unsafe void ProcessFreeFlyInput(
        Camera camera,
        Glfw? glfw,
        WindowHandle* glfwWindow,
        Vector2 imageMin,
        Vector2 imageSize,
        Vector2 mousePos,
        bool mouseClicked_Right,
        bool mouseReleased_Right,
        bool keyDown_W, bool keyDown_S,
        bool keyDown_A, bool keyDown_D,
        bool keyDown_E, bool keyDown_Q,
        bool keyDown_Space, bool keyDown_Shift,
        bool keyPressed_R,
        float mouseWheel,
        float deltaTime,
        bool windowHovered)
    {
        double glfwCursorX = 0, glfwCursorY = 0;
        int winX = 0, winY = 0;
        if (glfw != null)
        {
            glfw.GetCursorPos(glfwWindow, out glfwCursorX, out glfwCursorY);
            glfw.GetWindowPos(glfwWindow, out winX, out winY);
        }

        // Convert GLFW window-local cursor coordinates to global screen coordinates
        // to match ImGui's GetCursorScreenPos() when multi-viewport is enabled.
        double globalCursorX = glfwCursorX + winX;
        double globalCursorY = glfwCursorY + winY;

        bool mouseInViewportBounds = globalCursorX >= imageMin.X && globalCursorX <= imageMin.X + imageSize.X &&
                                     globalCursorY >= imageMin.Y && globalCursorY <= imageMin.Y + imageSize.Y;

        // Detect right-click to enter free-fly mode
        if (mouseClicked_Right && mouseInViewportBounds)
        {
            _freeFlyActive = true;
            if (glfw != null)
            {
                glfw.SetInputMode(glfwWindow, CursorStateAttribute.Cursor, CursorModeValue.CursorDisabled);
                glfw.GetCursorPos(glfwWindow, out double localX, out double localY);
                _lastMouseX = localX + winX;
                _lastMouseY = localY + winY;
            }
        }

        // Continue free-fly while button is held
        if (_freeFlyActive && !mouseReleased_Right && glfw != null)
        {
            glfw.GetCursorPos(glfwWindow, out double cursorX, out double cursorY);
            double globalX = cursorX + winX;
            double globalY = cursorY + winY;

            // Apply mouse look
            if (!double.IsNaN(globalX) && !double.IsNaN(globalY) &&
                !double.IsInfinity(globalX) && !double.IsInfinity(globalY) &&
                !double.IsNaN(_lastMouseX) && !double.IsNaN(_lastMouseY))
            {
                float lookDx = (float)(globalX - _lastMouseX) * FreeFlyLookSensitivity;
                float lookDy = -(float)(globalY - _lastMouseY) * FreeFlyLookSensitivity;
                camera.Look(lookDx, lookDy);
            }

            // Recenter cursor to viewport center (global -> local for SetCursorPos)
            double globalCenterX = imageMin.X + imageSize.X * 0.5;
            double globalCenterY = imageMin.Y + imageSize.Y * 0.5;
            glfw.SetCursorPos(glfwWindow, globalCenterX - winX, globalCenterY - winY);
            _lastMouseX = globalCenterX;
            _lastMouseY = globalCenterY;

            // Calculate movement
            float speed = _freeFlySpeed * camera.Distance * 0.2f;
            if (keyDown_Space) speed *= 2.5f;
            else if (keyDown_Shift) speed *= 0.4f;

            float fwd = 0f, rt = 0f, up = 0f;
            if (keyDown_W) fwd += speed * deltaTime;
            if (keyDown_S) fwd -= speed * deltaTime;
            if (keyDown_D) rt += speed * deltaTime;
            if (keyDown_A) rt -= speed * deltaTime;
            if (keyDown_E) up += speed * deltaTime;
            if (keyDown_Q) up -= speed * deltaTime;
            if (fwd != 0f || rt != 0f || up != 0f)
                camera.MoveFreeFly(fwd, rt, up);

            // Scroll wheel speed adjustment
            if (mouseWheel != 0)
            {
                float factor = mouseWheel > 0 ? 1.3f : 1f / 1.3f;
                for (int i = 0; i < (int)MathF.Abs(mouseWheel); i++)
                    _freeFlySpeed *= factor;
                _freeFlySpeed = Math.Clamp(_freeFlySpeed, 0.1f, 500f);
            }

            // R key to reset
            if (keyPressed_R)
                camera.ResetToDefaultPose();
        }
        else if (mouseReleased_Right && _freeFlyActive)
        {
            // Exit free-fly
            if (glfw != null)
                glfw.SetInputMode(glfwWindow, CursorStateAttribute.Cursor, CursorModeValue.CursorNormal);
            _freeFlyActive = false;
            _lastMouseX = double.NaN;
            _lastMouseY = double.NaN;
        }
    }
}
