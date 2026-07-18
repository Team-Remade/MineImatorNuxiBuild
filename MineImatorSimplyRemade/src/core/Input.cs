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

    private static Input? _orbitOwner;
    private static Input? _panOwner;
    private static Input? _gizmoOwner;

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

    // ── GLFW references (for mouse wrap via SetCursorPos) ──────────────────

    private Glfw? _glfw;
    private unsafe WindowHandle* _glfwWindow;

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
        bool windowHovered,
        bool ctrlHeld = false,
        bool overlaysEnabled = true)
    {
        // Cache GLFW for mouse-wrap SetCursorPos calls in the sub-methods.
        _glfw = glfw;
        _glfwWindow = glfwWindow;

        // ── Gizmo global-space toggle (G key) ──────────────────────────────
        // Only allow toggling local/global space when overlays (and therefore
        // the gizmo) are visible in this viewport.
        if (overlaysEnabled && gizmo != null && gizmo.Visible && !gizmo.Editing && keyPressed_G)
            gizmo.UseLocalSpace = !gizmo.UseLocalSpace;

        // ── Orbit input (left mouse button) ────────────────────────────────
        ProcessOrbitInput(camera, gizmo, ref mousePos, mouseDown_Left, mouseClicked_Left,
                         mouseReleased_Left, windowHovered, imageMin, imageSize, ctrlHeld,
                         overlaysEnabled);

        // ── Pan input (middle mouse button) ────────────────────────────────
        ProcessPanInput(camera, ref mousePos, mouseDown_Middle, windowHovered, imageMin, imageSize);

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
        ref Vector2 mousePos,
        bool mouseDown_Left,
        bool mouseClicked_Left,
        bool mouseReleased_Left,
        bool windowHovered,
        Vector2 imageMin,
        Vector2 imageSize,
        bool ctrlHeld,
        bool overlaysEnabled = true)
    {
        // Seed last position on first call to avoid spurious delta
        if (double.IsNaN(_orbitLastMouseX))
        {
            _orbitLastMouseX = mousePos.X;
            _orbitLastMouseY = mousePos.Y;
        }

        // Cross-viewport guard: if another viewport's Input already owns the
        // orbit or gizmo drag, this one must not steal it. We still need to
        // track the mouse position so when ownership eventually returns here
        // we don't apply a huge accumulated delta.
        if (mouseDown_Left && (_orbitOwner != null && _orbitOwner != this) ||
            (_gizmoOwner != null && _gizmoOwner != this))
        {
            _orbitLastMouseX = mousePos.X;
            _orbitLastMouseY = mousePos.Y;
            if (mouseReleased_Left)
            {
                _pressMouseX = float.NaN;
                _pressMouseY = float.NaN;
            }
            return;
        }

        float dx = (float)(mousePos.X - _orbitLastMouseX);
        float dy = (float)(mousePos.Y - _orbitLastMouseY);

        // Update hover when not dragging
        if (!mouseDown_Left && gizmo != null && overlaysEnabled)
            gizmo.UpdateHover(mousePos, camera, imageMin, imageSize);

        // If overlays are disabled for this viewport the gizmo is not visible
        // and must not be interactive. Cancel any in-progress gizmo drag that
        // started before overlays were toggled off so it cannot be resumed on
        // a viewport whose gizmo is currently hidden.
        if (!overlaysEnabled && _gizmoDragging && _gizmoOwner == this)
        {
            gizmo?.EndEdit();
            _gizmoDragging = false;
            _gizmoOwner = null;
        }

        bool pressInsideImage =
            !float.IsNaN(_pressMouseX) &&
            _pressMouseX >= imageMin.X && _pressMouseX <= imageMin.X + imageSize.X &&
            _pressMouseY >= imageMin.Y && _pressMouseY <= imageMin.Y + imageSize.Y;

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
                    pressInsideImage =
                        mousePos.X >= imageMin.X && mousePos.X <= imageMin.X + imageSize.X &&
                        mousePos.Y >= imageMin.Y && mousePos.Y <= imageMin.Y + imageSize.Y;
                }

                // Only orbit/queue-pick if the press started on the rendered image.
                // This prevents the title bar of a draggable preview window from
                // starting an orbit drag (matches free-fly's bounds check).
                if (pressInsideImage)
                {
                    if (overlaysEnabled && gizmo != null && gizmo.TryBeginEdit(mousePos, camera, imageMin, imageSize))
                    {
                        _gizmoDragging = true;
                        _gizmoOwner = this;
                    }
                    else
                    {
                        // Check orbit drag threshold
                        float moveDist = MathF.Sqrt(
                            (mousePos.X - _pressMouseX) * (mousePos.X - _pressMouseX) +
                            (mousePos.Y - _pressMouseY) * (mousePos.Y - _pressMouseY));
                        if (moveDist >= OrbitDragThreshold)
                        {
                            _dragging = true;
                            _orbitOwner = this;
                        }
                    }
                }
            }

            // Mouse wrap: while actively orbiting or dragging a gizmo, if the
            // cursor reaches the viewport edge, teleport it to the opposite edge
            // and clamp the just-applied dx/dy to the movement up to the edge.
            if (_dragging || _gizmoDragging)
            {
                var wrap = ComputeMouseWrap(mousePos, imageMin, imageSize);
                if (wrap.Wrapped)
                {
                    dx = wrap.ClampedPos.X - (float)_orbitLastMouseX;
                    dy = wrap.ClampedPos.Y - (float)_orbitLastMouseY;
                    mousePos = wrap.WrappedPos;
                    ApplyMouseWrap(wrap.WrappedPos);
                }
            }

            if (_gizmoDragging)
                gizmo?.ContinueEdit(mousePos, camera, imageMin, imageSize);
            else if (_dragging && pressInsideImage)
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
            if (_orbitOwner == this) _orbitOwner = null;
            if (_gizmoOwner == this) _gizmoOwner = null;

            // Queue pick if not from gizmo/orbit
            // When overlays are disabled the gizmo isn't visible in this viewport,
            // so the hover state from the gizmo (which is shared across viewports)
            // must not suppress scene-object picking here.
            bool gizmoHovering = overlaysEnabled && (gizmo?.Hovering ?? false);
            if (!wasGizmoDragging && !wasOrbitDragging && !gizmoHovering)
            {
                _pendingPickX = mousePos.X;
                _pendingPickY = mousePos.Y;
                _pendingPickCtrl = ctrlHeld;
            }

            _pressMouseX = float.NaN;
            _pressMouseY = float.NaN;
        }

        _orbitLastMouseX = mousePos.X;
        _orbitLastMouseY = mousePos.Y;
    }

    private void ProcessPanInput(
        Camera camera,
        ref Vector2 mousePos,
        bool mouseDown_Middle,
        bool windowHovered,
        Vector2 imageMin,
        Vector2 imageSize)
    {
        // If another viewport's Input already owns the pan, this one must not
        // participate. Sync position to avoid a stale delta later.
        if (_panOwner != null && _panOwner != this)
        {
            _panLastMouseX = mousePos.X;
            _panLastMouseY = mousePos.Y;
            return;
        }

        // Normal release path: if the cursor leaves the viewport and we are not
        // the active pan owner, abort. The owning viewport will keep panning
        // because its own copy of this method short-circuits the owner check
        // above (when _panOwner == this).
        if (!windowHovered && _panOwner != this)
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
            // Mouse wrap: while actively panning, teleport the cursor to the
            // opposite edge when it hits the viewport boundary, and clamp dx/dy
            // to the movement up to the edge so fast drags don't cause a jump.
            if (_panning)
            {
                var wrap = ComputeMouseWrap(mousePos, imageMin, imageSize);
                if (wrap.Wrapped)
                {
                    dx = wrap.ClampedPos.X - (float)_panLastMouseX;
                    dy = wrap.ClampedPos.Y - (float)_panLastMouseY;
                    mousePos = wrap.WrappedPos;
                    ApplyMouseWrap(wrap.WrappedPos);
                }
            }

            if (_panning)
            {
                if (camera.Distance < 0.01f)
                {
                    // First-person / scene-object camera: Distance is near-zero, so the
                    // distance-scaled Pan() factor collapses to nothing. Translate both
                    // Target and eye by a fixed world-unit-per-pixel delta in the
                    // camera's right/up plane so panning still works.
                    var view = camera.GetViewMatrix();
                    vec3 right = new vec3(view.m00, view.m10, view.m20);
                    vec3 up    = new vec3(view.m01, view.m11, view.m21);
                    vec3 delta = right * (-dx * 0.05f) + up * (dy * 0.05f);
                    camera.Target += delta;
                    // Position is derived as Target + OffsetFromTarget(); since the
                    // offset is near-zero for first-person cameras, this also moves
                    // the eye by `delta`.
                }
                else
                {
                    camera.Pan(-dx * 0.01f * (camera.Distance / 5f),
                               dy * 0.01f * (camera.Distance / 5f));
                }
            }
            _panning = true;
            _panOwner = this;
        }
        else
        {
            _panning = false;
            if (_panOwner == this) _panOwner = null;
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

    // ── Mouse wrap helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Result of <see cref="ComputeMouseWrap"/>.  When <see cref="Wrapped"/> is
    /// true, <see cref="ClampedPos"/> is the position the cursor would have if
    /// clamped to the viewport edge (used to compute a sane dx/dy) and
    /// <see cref="WrappedPos"/> is the position on the opposite edge that the
    /// OS cursor should be teleported to.
    /// </summary>
    private readonly struct MouseWrapResult
    {
        public readonly bool Wrapped;
        public readonly Vector2 ClampedPos;
        public readonly Vector2 WrappedPos;

        public MouseWrapResult(bool wrapped, Vector2 clamped, Vector2 wrappedPos)
        {
            Wrapped = wrapped;
            ClampedPos = clamped;
            WrappedPos = wrappedPos;
        }
    }

    /// <summary>
    /// If the cursor is at or past the viewport edge on either axis, returns a
    /// result describing how to wrap it.  The wrap is per-axis: a cursor at the
    /// bottom-right corner will teleport to the top-left.
    /// </summary>
    private static MouseWrapResult ComputeMouseWrap(Vector2 mousePos, Vector2 imageMin, Vector2 imageSize)
    {
        if (imageSize.X <= 0f || imageSize.Y <= 0f)
            return new MouseWrapResult(false, mousePos, mousePos);

        // Epsilon is the "edge zone" near each border where a wrap is triggered.
        // The wrap target is placed strictly outside the opposite edge zone
        // (epsilon + safeOffset pixels in) so the next frame's mouse position
        // doesn't immediately re-trigger the wrap, which would cause the
        // cursor to oscillate between opposite edges.
        const float epsilon = 1f;
        const float safeOffset = 1f;
        float right  = imageMin.X + imageSize.X;
        float bottom = imageMin.Y + imageSize.Y;

        bool wrapX = false, wrapY = false;
        float clampedX = mousePos.X, clampedY = mousePos.Y;
        float wrappedX = mousePos.X, wrappedY = mousePos.Y;

        if (mousePos.X >= right - epsilon)
        {
            clampedX = right;
            wrappedX = imageMin.X + epsilon + safeOffset;
            wrapX = true;
        }
        else if (mousePos.X <= imageMin.X + epsilon)
        {
            clampedX = imageMin.X;
            wrappedX = right - epsilon - safeOffset;
            wrapX = true;
        }

        if (mousePos.Y >= bottom - epsilon)
        {
            clampedY = bottom;
            wrappedY = imageMin.Y + epsilon + safeOffset;
            wrapY = true;
        }
        else if (mousePos.Y <= imageMin.Y + epsilon)
        {
            clampedY = imageMin.Y;
            wrappedY = bottom - epsilon - safeOffset;
            wrapY = true;
        }

        if (!wrapX && !wrapY)
            return new MouseWrapResult(false, mousePos, mousePos);

        return new MouseWrapResult(
            true,
            new Vector2(clampedX, clampedY),
            new Vector2(wrappedX, wrappedY));
    }

    /// <summary>
    /// Applies a mouse wrap: updates the last-tracked positions for orbit and
    /// pan to the wrapped position (so the next frame's delta is small) and
    /// moves the OS cursor via GLFW so the OS reports the wrapped position
    /// from the next frame onward.
    /// </summary>
    private unsafe void ApplyMouseWrap(Vector2 wrappedPos)
    {
        _orbitLastMouseX = wrappedPos.X;
        _orbitLastMouseY = wrappedPos.Y;
        _panLastMouseX   = wrappedPos.X;
        _panLastMouseY   = wrappedPos.Y;

        if (_glfw != null)
        {
            _glfw.GetWindowPos(_glfwWindow, out int winX, out int winY);
            _glfw.SetCursorPos(_glfwWindow, wrappedPos.X - winX, wrappedPos.Y - winY);
        }
    }
}
