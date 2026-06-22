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
    private CameraViewport? _panel;

    /// <summary>The main window's GLFW handle, used to restore its GL context
    /// after this window finishes its ImGui frame.</summary>
    private unsafe WindowHandle* _mainHandle;

    public unsafe CameraWindow(Glfw glfw, GL gl, WindowHandle* shareWith)
        : base(640, 420, "Camera View", glfw, gl, shareWith)
    {
        _mainHandle = shareWith;
    }

    public CameraViewport? Panel
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

        // Compute the available size from the last known window dimensions.
        // We use WindowWidth/WindowHeight from the base class (updated by the
        // framebuffer-size callback).
        uint sceneW = (uint)Math.Max(4, WindowWidth);
        uint sceneH = (uint)Math.Max(4, WindowHeight - 30); // subtract approx header height

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

        ImGui.Separator();

        // Display the FBO texture (texture objects are shared across GL contexts).
        var avail = ImGui.GetContentRegionAvail();
        if (avail.X >= 4 && avail.Y >= 4)
        {
            _panel.HandleFreeFlyPublic(activeCam, sceneObj, ImGui.IsWindowHovered());

            ImGui.Image(
                new ImTextureRef(texId: (ulong)_panel.ColorTexture),
                avail,
                new Vector2(0, 1),
                new Vector2(1, 0));
        }

        ImGui.End();
    }
}
