using System.Reflection;
using System.Numerics;
using System.Collections.Generic;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.startup;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace MineImatorSimplyRemade.core.window.windows;

public class StartupProgressWindow : Window
{
    private readonly record struct GifFrame(uint TextureId, int Width, int Height, int DelayMs);

    public StartupProgressState ProgressState { get; } = new();

    private double _startTime = DateTime.UtcNow.TimeOfDay.TotalSeconds;
    private bool _loadingGifAttempted;
    private readonly List<GifFrame> _loadingGifFrames = new();
    private int _loadingGifFrameIndex;
    private double _loadingGifNextFrameAtSeconds;

    public StartupProgressWindow(int width, int height, string title, Glfw glfw, GL gl = null)
        : base(width, height, title, glfw, gl)
    {
    }

    protected override void RenderUi()
    {
        EnsureLoadingGifTexture();

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar |
                                 ImGuiWindowFlags.NoResize |
                                 ImGuiWindowFlags.NoMove |
                                 ImGuiWindowFlags.NoScrollbar |
                                 ImGuiWindowFlags.NoSavedSettings;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(22f, 20f));
        ImGui.Begin("##StartupProgress", flags);
        ImGui.PopStyleVar();

        float clampedProgress = Math.Clamp(ProgressState.Progress, 0f, 1f);
        string stepLabel = ProgressState.TotalSteps > 0
            ? $"Step {Math.Clamp(ProgressState.CurrentStep, 1, ProgressState.TotalSteps)}/{ProgressState.TotalSteps}"
            : "Startup";

        ImGui.TextColored(new Vector4(0.92f, 0.74f, 0.31f, 1f), ProgressState.Title);
        ImGui.TextDisabled(stepLabel);
        ImGui.Separator();

        double elapsed = DateTime.UtcNow.TimeOfDay.TotalSeconds - _startTime;
        int dots = ((int)(elapsed * 2.0) % 4);
        string animated = "Working" + new string('.', dots);

        ImGui.Dummy(new Vector2(0, 6));
        ImGui.TextWrapped(ProgressState.Phase);
        ImGui.Dummy(new Vector2(0, 10));
        ImGui.ProgressBar(clampedProgress, new Vector2(-1, 16f), $"{MathF.Round(clampedProgress * 100f)}%");
        ImGui.Dummy(new Vector2(0, 10));
        ImGui.Text(animated);
        ImGui.TextWrapped(ProgressState.Status);

        if (!string.IsNullOrWhiteSpace(ProgressState.Detail))
        {
            ImGui.Dummy(new Vector2(0, 6));
            ImGui.TextColored(new Vector4(0.70f, 0.74f, 0.80f, 1f), ProgressState.Detail);
        }

        DrawLoadingGifBottomRight();

        ImGui.End();
    }

    private unsafe void EnsureLoadingGifTexture()
    {
        if (_loadingGifAttempted)
            return;

        _loadingGifAttempted = true;

        Assembly assembly = Assembly.GetExecutingAssembly();
        using Stream? stream = assembly.GetManifestResourceStream("MineImatorSimplyRemade.assets.img.loading.gif");
        if (stream == null)
        {
            Console.WriteLine("[StartupProgressWindow] Embedded loading.gif not found.");
            return;
        }

        List<AnimatedFrameResult> frames = new();
        try
        {
            foreach (AnimatedFrameResult frame in ImageResult.AnimatedGifFramesFromStream(stream, ColorComponents.RedGreenBlueAlpha))
                frames.Add(frame);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StartupProgressWindow] Failed to decode loading.gif with stb_image: {ex.Message}");
            return;
        }

        if (frames.Count == 0)
            return;

        foreach (AnimatedFrameResult frame in frames)
        {
            if (frame.Width <= 0 || frame.Height <= 0 || frame.Data.Length == 0)
                continue;

            uint texture = GL.GenTexture();
            GL.BindTexture(GLEnum.Texture2D, texture);

            fixed (byte* pixels = frame.Data)
            {
                GL.TexImage2D(
                    GLEnum.Texture2D,
                    0,
                    InternalFormat.Rgba8,
                    (uint)frame.Width,
                    (uint)frame.Height,
                    0,
                    PixelFormat.Rgba,
                    GLEnum.UnsignedByte,
                    pixels);
            }

            GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            int delayMs = frame.DelayInMs > 0 ? frame.DelayInMs : 80;
            _loadingGifFrames.Add(new GifFrame(texture, frame.Width, frame.Height, delayMs));
        }

        GL.BindTexture(GLEnum.Texture2D, 0);

        _loadingGifFrameIndex = 0;
        _loadingGifNextFrameAtSeconds = -1;
    }

    private unsafe void DrawLoadingGifBottomRight()
    {
        if (_loadingGifFrames.Count == 0)
            return;

        AdvanceLoadingGifFrame();
        GifFrame frame = _loadingGifFrames[_loadingGifFrameIndex];

        ImGuiStylePtr style = ImGui.GetStyle();
        Vector2 windowSize = ImGui.GetWindowSize();

        const float maxSize = 84f;
        float scale = MathF.Min(maxSize / frame.Width, maxSize / frame.Height);
        scale = MathF.Max(0.1f, scale);

        Vector2 drawSize = new(frame.Width * scale, frame.Height * scale);
        Vector2 drawPos = new(
            windowSize.X - style.WindowPadding.X - drawSize.X,
            windowSize.Y - style.WindowPadding.Y - drawSize.Y);

        ImGui.SetCursorPos(drawPos);
        ImGui.Image(new ImTextureRef(texId: (ulong)frame.TextureId), drawSize);
    }

    private void AdvanceLoadingGifFrame()
    {
        if (_loadingGifFrames.Count <= 1)
            return;

        double now = ImGui.GetTime();

        if (_loadingGifNextFrameAtSeconds < 0)
        {
            _loadingGifNextFrameAtSeconds = now + (_loadingGifFrames[_loadingGifFrameIndex].DelayMs / 1000.0);
            return;
        }

        while (now >= _loadingGifNextFrameAtSeconds)
        {
            _loadingGifFrameIndex = (_loadingGifFrameIndex + 1) % _loadingGifFrames.Count;
            _loadingGifNextFrameAtSeconds += _loadingGifFrames[_loadingGifFrameIndex].DelayMs / 1000.0;
        }
    }
}
