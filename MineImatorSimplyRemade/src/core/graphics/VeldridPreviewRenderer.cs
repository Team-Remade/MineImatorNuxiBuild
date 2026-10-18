using System;
using System.Collections.Generic;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemadeNuxi.core.objs;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

public class VeldridPreviewRenderer : IDisposable
{
    public const int TextureSize = 256;

    public GraphicsDevice GraphicsDevice { get; }
    public VeldridFramebuffer Framebuffer { get; private set; }

    public float Yaw = 0.75f;
    public float Pitch = 0.4f;
    public float Distance = 2.2f;
    public float AutoRotateSpeed = 0.6f;

    public string LastSelectionKey { get; set; } = "";

    public VeldridPreviewRenderer(GraphicsDevice gd)
    {
        GraphicsDevice = gd ?? throw new ArgumentNullException(nameof(gd));
        Framebuffer = new VeldridFramebuffer(gd, TextureSize, TextureSize, hasDepth: true);
    }

    public void UpdateRotation(float deltaTime, bool isHovered)
    {
        if (!isHovered && AutoRotateSpeed > 0f)
        {
            Yaw += AutoRotateSpeed * deltaTime;
        }
    }

    public void Orbit(float deltaYaw, float deltaPitch)
    {
        Yaw += deltaYaw;
        Pitch = Math.Clamp(Pitch + deltaPitch, -1.45f, 1.45f);
    }

    public mat4 GetViewMatrix()
    {
        float cosP = MathF.Cos(Pitch);
        var eye = new vec3(
            cosP * MathF.Sin(Yaw),
            MathF.Sin(Pitch),
            cosP * MathF.Cos(Yaw)) * Distance;

        return mat4.LookAt(eye, vec3.Zero, vec3.UnitY);
    }

    public mat4 GetProjectionMatrix()
    {
        return mat4.Perspective(
            glm.Radians(50f),
            1.0f,
            0.05f,
            100f);
    }

    public void BeginRender(CommandList cl, RgbaFloat clearColor)
    {
        cl.SetFramebuffer(Framebuffer.Framebuffer);
        cl.SetViewport(0, new Viewport(0, 0, TextureSize, TextureSize, 0, 1));
        cl.ClearColorTarget(0, clearColor);
        cl.ClearDepthStencil(1.0f);
    }

    public void EndRender(CommandList cl)
    {
        // Resolve multisampled target if configured
        if (Framebuffer.SampleCount != TextureSampleCount.Count1)
        {
            Framebuffer.Resolve(cl);
        }
    }

    public void Dispose()
    {
        Framebuffer.Dispose();
    }
}
