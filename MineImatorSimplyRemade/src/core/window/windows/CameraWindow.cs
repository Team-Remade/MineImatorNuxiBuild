using System.Numerics;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.GLFW;
using Hexa.NET.ImGui.Backends.OpenGL3;
using MineImatorSimplyRemade.core.diagnostics;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.window.windows;

/// <summary>
/// A standalone GLFW window for the camera viewport.
/// Created once at startup (hidden) with a shared GL context so textures
/// from the main window are accessible.
///
/// GL rendering (scene into FBO) always runs on the main GL context because
/// FBOs are per-context objects and cannot be shared.  Only ImGui and
/// SwapBuffers run on this window's own context.
/// </summary>
public class CameraWindow : Window
{
    private Viewport? _panel;
    public Action? RenderingDebugRequested { get; set; }
    public PerformanceMetricsOverlay? MetricsOverlay { get; set; }

    /// <summary>The main window's GLFW handle, used to restore its GL context
    /// after this window finishes its ImGui frame.</summary>
    private unsafe WindowHandle* _mainHandle;

    public unsafe CameraWindow(Glfw glfw, GL gl, WindowHandle* shareWith)
        : base(640, 420, Localization.T("text.common.camera_view"), glfw, gl, shareWith)
    {
        _mainHandle = shareWith;
    }

    public Viewport? Panel
    {
        set => _panel = value;
    }

    /// <summary>
    /// Renders one frame of the camera window.
    ///
    /// Step 1 — main GL context: run the 3-D scene into the FBO (FBOs are
    ///          per-context, so they must be rendered where they were created).
    /// Step 2 — camera GL context: clear, run ImGui frame, display the FBO
    ///          texture (textures ARE shared across contexts), SwapBuffers.
    /// Step 3 — restore main GL context so the caller's subsequent work is
    ///          not disrupted.
    /// </summary>
    public override unsafe void Render()
    {
        if (_panel == null) return;

        {
            using var setupScope = FrameProfiler.Shared.Measure(ProfileStage.UiSetup);
            SetWindowTitle(Localization.T("text.common.camera_view"));
            SetContextCurrent();
            UpdateDpiScale();
        }

        // ── Step 1: scene render on the main context ──────────────────────────
        Glfw.MakeContextCurrent(_mainHandle);

        var spawned   = _panel.GetSpawnedCamerasPublic();
        var (activeCam, sceneObj) = _panel.DrawCameraDropdownInternal(spawned);

        // Estimate available content size for this frame and fit it to the
        // project's render-resolution aspect ratio so the preview stays accurate.
        var estimatedAvail = new Vector2(
            Math.Max(4, WindowWidth - 8 * ImGui.GetStyle().FontScaleDpi),
            Math.Max(4, WindowHeight - 56 * ImGui.GetStyle().FontScaleDpi));
        Vector2 drawSizeEstimate = _panel.GetPreviewDrawSize(estimatedAvail);
        uint sceneW = (uint)Math.Max(4, (int)MathF.Round(drawSizeEstimate.X));
        uint sceneH = (uint)Math.Max(4, (int)MathF.Round(drawSizeEstimate.Y));

        _panel.ResizeFboPublic(sceneW, sceneH);
        _panel.RenderScenePublic(activeCam, sceneObj, sceneW, sceneH);

        // ── Step 2: ImGui frame on this window's context ──────────────────────
        // Make GL context current FIRST, before setting ImGui contexts
        {
            using var setupScope = FrameProfiler.Shared.Measure(ProfileStage.UiSetup);
            SetContextCurrent();
            Glfw.MakeContextCurrent(WindowHandle);

            GL.ClearColor(ClearR, ClearG, ClearB, ClearA);
            GL.Clear(ClearBufferMask.ColorBufferBit |
                     ClearBufferMask.DepthBufferBit);

            ImGuiImplOpenGL3.NewFrame();
            ImGuiImplGLFW.NewFrame();
            UpdateDpiScale();
            ImGui.NewFrame();
        }

        RenderUi(activeCam, sceneObj, sceneW, sceneH);

        {
            using var drawScope = FrameProfiler.Shared.Measure(ProfileStage.ImGuiDraw);
            ImGui.Render();
            ImGuiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());
        }

        // Required every frame that ends with ImGui.Render() — without this,
        // ImGui asserts on the next NewFrame() that UpdatePlatformWindows was skipped.
        var ioFlags = ImGui.GetIO().ConfigFlags;
        if ((ioFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
        {
            using var platformScope = FrameProfiler.Shared.Measure(ProfileStage.PlatformWindows);
            ImGui.UpdatePlatformWindows();
            ImGui.RenderPlatformWindowsDefault();
        }

        {
            using var swapScope = FrameProfiler.Shared.Measure(ProfileStage.SwapBuffers);
            Glfw.SwapBuffers(WindowHandle);
        }

        // ── Step 3: restore main context ──────────────────────────────────────
        Glfw.MakeContextCurrent(_mainHandle);
    }

    protected override void RenderUi() { } // unused; overridden by the typed version below

    private unsafe void RenderUi(
        Camera activeCam,
        CameraSceneObject? sceneObj,
        uint sceneW, uint sceneH)
    {
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Right) || ImGui.GetIO().AppFocusLost ||
            ImGui.GetIO().WantTextInput || Keybinds.IsCapturing)
            _panel?.StopFreeFly();

        // Handle keyboard shortcuts specific to this undocked window
        HandleCameraWindowKeyboardShortcuts();

        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.Pos);
        ImGui.SetNextWindowSize(vp.Size);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("##CamWinHost",
            ImGuiWindowFlags.NoTitleBar      |
            ImGuiWindowFlags.NoResize        |
            ImGuiWindowFlags.NoMove          |
            ImGuiWindowFlags.NoScrollbar     |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoBringToFrontOnFocus);
        ImGui.PopStyleVar();

        // Header row.
        var spawned = _panel!.GetSpawnedCamerasPublic();
        _panel.DrawCameraDropdownPublic(spawned); // real combo box UI, mirrors the inline preview header

        ImGui.SameLine();
        _panel.DrawRuleOfThirdsToggle();

        ImGui.SameLine();
        if (ViewportIcons.Button("vpDock", ViewportIcons.Dock, Localization.T("text.window.camera.dock")))
        {
            _panel.DockToInlineVisible();
            // Reset the camera viewport's GLFW references back to the main window
            _panel.GlfwWindowPreview = _mainHandle;
            Hide();
        }

        // Overlays toggle — mirrors the inline panel button.
        ImGui.SameLine();
        {
            bool overlays = _panel.OverlaysEnabled;
            if (!overlays)
            {
                ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.20f, 0.20f, 0.20f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.30f, 0.30f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.40f, 0.40f, 0.40f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.Text,          new Vector4(0.50f, 0.50f, 0.50f, 1.0f));
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.25f, 0.45f, 0.25f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.55f, 0.30f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.35f, 0.60f, 0.35f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.Text,          new Vector4(0.90f, 0.95f, 0.90f, 1.0f));
            }

            if (ViewportIcons.Button("vpOverlays", ViewportIcons.Overlays, Localization.T("text.common.overlays")))
                _panel.OverlaysEnabled = !_panel.OverlaysEnabled;

            ImGui.PopStyleColor(4);
        }

        // Corner picker — mirrors the inline preview header. This sets the
        // corner the preview will snap to once this window is docked again.
        ImGui.SameLine();
        _panel.DrawCornerPicker();

        ImGui.SameLine();
        _panel.DrawRenderModeSelectorPublic("PreviewWindowHeader", 176f * UiTheme.Scale);

        ImGui.Separator();

        // Display the FBO texture (texture objects are shared across GL contexts).
        var avail = ImGui.GetContentRegionAvail();
        if (avail.X >= 4 && avail.Y >= 4)
        {
            Vector2 drawSize = _panel.GetPreviewDrawSize(avail);
            Vector2 startPos = ImGui.GetCursorPos();
            if (avail.X > drawSize.X)
                ImGui.SetCursorPosX(startPos.X + (avail.X - drawSize.X) * 0.5f);
            if (avail.Y > drawSize.Y)
                ImGui.SetCursorPosY(startPos.Y + (avail.Y - drawSize.Y) * 0.5f);

            // Handle input AFTER cursor is positioned (so bounds are correct for centered image)
            var imageMin = ImGui.GetCursorScreenPos();
            var imageSize = drawSize;
            _panel.HandleFreeFlyPublic(activeCam, sceneObj, ImGui.IsWindowHovered(), imageMin, imageSize);

            ImGui.Image(
                new ImTextureRef(texId: (ulong)_panel.ColorTexture),
                drawSize,
                new Vector2(0, 1),
                new Vector2(1, 0));

            _panel.RenderRuleOfThirdsOverlay(imageMin, imageSize);

            // Gizmo rotation-arc overlay for the undocked window.
            if (_panel.OverlaysEnabled)
                _panel.Gizmo?.RenderOverlay(activeCam, imageMin, imageSize);

            if (_panel.HighQualityPreviewEnabled)
                _panel.RenderFpsOverlay(imageMin, imageSize, Viewport.Corner.BottomRight);
        }

        ImGui.End();
        MetricsOverlay?.Render();
    }

    /// <summary>
    /// Handles the configured preview/debug shortcuts in the undocked camera window.
    /// This ensures that when the camera window is in focus in its own native window,
    /// keyboard shortcuts are processed by this window's ImGui context instead of 
    /// being lost to the main window's context.
    /// </summary>
    private bool _keyboardShortcutsArmed;

    private void HandleCameraWindowKeyboardShortcuts()
    {
        if (_panel == null)
            return;

        if (!_keyboardShortcutsArmed)
        {
            if (Keybinds.AllReleased("highQualityPreview", "shadowDebug", "renderingDebug", "metrics"))
                _keyboardShortcutsArmed = true;
            return;
        }

        if (Keybinds.Pressed("metrics"))
            MetricsOverlay?.ToggleVisibility();
        else if (Keybinds.Pressed("renderingDebug"))
            RenderingDebugRequested?.Invoke();
        else if (Keybinds.Pressed("highQualityPreview"))
            _panel.ToggleHighQualityPreview();
        else if (Keybinds.Pressed("shadowDebug"))
            _panel.ToggleShadowDebugMode();
    }
}
