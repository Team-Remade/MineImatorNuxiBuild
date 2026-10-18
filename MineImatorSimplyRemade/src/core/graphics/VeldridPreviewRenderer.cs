using System;
using GlmSharp;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

public class VeldridPreviewRenderer : IDisposable
{
    public const int TextureSize = 256;

    public VeldridFramebuffer Framebuffer { get; private set; }

    public float Yaw = 0.75f;
    public float Pitch = 0.4f;
    public float Distance = 2.2f;
    public float AutoRotateSpeed = 0.6f;

    public string LastSelectionKey { get; set; } = "";

    public VeldridPreviewRenderer(GraphicsDevice gd)
    {
        Framebuffer = new VeldridFramebuffer(gd, TextureSize, TextureSize, hasDepth: true);
    }

    public void UpdateRotation(float deltaTime, bool isHovered)
    {
        if (!isHovered && AutoRotateSpeed > 0f)
        {
            Yaw += AutoRotateSpeed * deltaTime;
        }
    }

    public void BeginRender(CommandList cl, RgbaFloat clearColor)
    {
        cl.SetFramebuffer(Framebuffer.Framebuffer);
        cl.ClearColorTarget(0, clearColor);
        cl.ClearDepthStencil(1.0f);
    }

    public void EndRender(CommandList cl)
    {
    }

    public void Dispose()
    {
        Framebuffer.Dispose();
    }
}
