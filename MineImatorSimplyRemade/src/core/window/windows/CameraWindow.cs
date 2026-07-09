using System.Numerics;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.GLFW;
using Hexa.NET.ImGui.Backends.OpenGL3;
using MineImatorSimplyRemade.core.ui.Panels;
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

    /// <summary>The main window's GLFW handle, used to restore its GL context
    /// after this window finishes its ImGui frame.</summary>
    private unsafe WindowHandle* _mainHandle;

    public unsafe CameraWindow(Glfw glfw, GL gl, WindowHandle* shareWith)
        : base(640, 420, "Camera View", glfw, gl, shareWith)
    {
        _mainHandle = shareWith;
    }

    public Viewport? Panel
    {
        get => _panel;
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

        // ── Step 1: scene render on the main context ──────────────────────────
        Glfw.MakeContextCurrent(_mainHandle);

        var spawned   = _panel.GetSpawnedCamerasPublic();
        var (activeCam, sceneObj) = _panel.DrawCameraDropdownInternal(spawned);

        // Estimate available content size for this frame and fit it to the
        // project's render-resolution aspect ratio so the preview stays accurate.
        var estimatedAvail = new Vector2(
            Math.Max(4, WindowWidth - 8),
            Math.Max(4, WindowHeight - 56));
        Vector2 drawSizeEstimate = _panel.GetPreviewDrawSize(estimatedAvail);
        uint sceneW = (uint)Math.Max(4, (int)MathF.Round(drawSizeEstimate.X));
        uint sceneH = (uint)Math.Max(4, (int)MathF.Round(drawSizeEstimate.Y));

        _panel.ResizeFboPublic(sceneW, sceneH);
        _panel.RenderScenePublic(activeCam, sceneObj, sceneW, sceneH);

        // ── Step 2: ImGui frame on this window's context ──────────────────────
        SetContextCurrent();
        Glfw.MakeContextCurrent(WindowHandle);

        GL.ClearColor(ClearR, ClearG, ClearB, ClearA);
        GL.Clear(Silk.NET.OpenGL.ClearBufferMask.ColorBufferBit |
                 Silk.NET.OpenGL.ClearBufferMask.DepthBufferBit);

        ImGuiImplOpenGL3.NewFrame();
        ImGuiImplGLFW.NewFrame();
        ImGui.NewFrame();

        RenderUi(activeCam, sceneObj, sceneW, sceneH);

        ImGui.Render();
        ImGuiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());

        // Required every frame that ends with ImGui.Render() — without this,
        // ImGui asserts on the next NewFrame() that UpdatePlatformWindows was skipped.
        var ioFlags = ImGui.GetIO().ConfigFlags;
        if ((ioFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
        {
            ImGui.UpdatePlatformWindows();
            ImGui.RenderPlatformWindowsDefault();
        }

        Glfw.SwapBuffers(WindowHandle);

        // ── Step 3: restore main context ──────────────────────────────────────
        Glfw.MakeContextCurrent(_mainHandle);
    }

    protected override void RenderUi() { } // unused; overridden by the typed version below

    private unsafe void RenderUi(
        MineImatorSimplyRemade.core.Camera activeCam,
        MineImatorSimplyRemadeNuxi.core.objs.sceneObjects.CameraSceneObject? sceneObj,
        uint sceneW, uint sceneH)
    {
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
        _panel.DrawCameraDropdownPublic(spawned); // redraws dropdown for this context

        ImGui.SameLine();
        if (ImGui.Button("Dock"))
        {
            _panel.Undocked = false;
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

            if (ImGui.Button("Overlays"))
                _panel.OverlaysEnabled = !_panel.OverlaysEnabled;

            ImGui.PopStyleColor(4);
        }

        ImGui.Separator();

        // Display the FBO texture (texture objects are shared across GL contexts).
        var avail = ImGui.GetContentRegionAvail();
        if (avail.X >= 4 && avail.Y >= 4)
        {
            _panel.HandleFreeFlyPublic(activeCam, sceneObj, ImGui.IsWindowHovered());

            Vector2 drawSize = _panel.GetPreviewDrawSize(avail);
            Vector2 startPos = ImGui.GetCursorPos();
            if (avail.X > drawSize.X)
                ImGui.SetCursorPosX(startPos.X + (avail.X - drawSize.X) * 0.5f);
            if (avail.Y > drawSize.Y)
                ImGui.SetCursorPosY(startPos.Y + (avail.Y - drawSize.Y) * 0.5f);

            ImGui.Image(
                new ImTextureRef(texId: (ulong)_panel.ColorTexture),
                drawSize,
                new Vector2(0, 1),
                new Vector2(1, 0));
        }

        ImGui.End();
    }
}
