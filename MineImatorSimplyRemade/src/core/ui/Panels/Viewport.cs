using System.Numerics;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui.Panels;

public class Viewport : UiPanel
{
    public List<SceneObject> SceneObjects { get; } = new();

    private uint fbo;
    private uint textureColorBuffer;
    private uint rbo;

    private uint viewportWidth, viewportHeight = 0;

    public unsafe void InitFramebuffer(uint width, uint height)
    {
        Gl.GenFramebuffers(1, out fbo);
        Gl.BindFramebuffer(GLEnum.Framebuffer, fbo);
        
        Gl.GenTextures(1, out textureColorBuffer);
        Gl.BindTexture(GLEnum.Texture2D, textureColorBuffer);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgb, width, height, 0, PixelFormat.Rgb, GLEnum.UnsignedByte, (void*)0);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
        Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
        
        Gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0, GLEnum.Texture2D, textureColorBuffer, 0);
        
        Gl.GenRenderbuffers(1, out rbo);
        Gl.BindRenderbuffer(GLEnum.Renderbuffer, rbo);
        Gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.Depth24Stencil8, width, height);
        
        Gl.FramebufferRenderbuffer(GLEnum.Framebuffer, GLEnum.DepthStencilAttachment, GLEnum.Renderbuffer, rbo);

        if (Gl.CheckFramebufferStatus(GLEnum.Framebuffer) != GLEnum.FramebufferComplete)
        {
            Console.WriteLine($"Framebuffer failed: {Gl.CheckFramebufferStatus(GLEnum.Framebuffer)}");
        }
        
        Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
    }
    
    public override void Draw()
    {
        
    }

    private unsafe void ResizeFramebuffer(uint width, uint height)
    {
        viewportWidth = width;
        viewportHeight = height;
        
        Gl.BindTexture(GLEnum.Texture2D, textureColorBuffer);
        Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgb, width, height, 0, PixelFormat.Rgb, GLEnum.UnsignedByte, (void*)0);
        
        Gl.BindRenderbuffer(GLEnum.Renderbuffer, rbo);
        Gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.Depth24Stencil8, width, height);
        
        Gl.BindTexture(GLEnum.Texture2D, 0);
        Gl.BindRenderbuffer(GLEnum.Renderbuffer, 0);
    }
    
    public override unsafe void Render()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        
        ImGui.Begin("Viewport");
        
        var size = ImGui.GetContentRegionAvail();

        if (size.X != 0 || size.Y != 0)
        {
            if (Math.Abs(viewportWidth - size.X) > 0.01 || Math.Abs(viewportHeight - size.Y) > 0.01)
            {
                ResizeFramebuffer((uint)size.X, (uint)size.Y);
            }
            
            Gl.BindFramebuffer(GLEnum.Framebuffer, fbo);
            Gl.Viewport(0, 0, (uint)size.X, (uint)size.Y);
            
            Gl.Clear(ClearBufferMask.ColorBufferBit);
            
            // TODO: setup 3D
            //Gl.Enable(GLEnum.DepthTest);
            
            SceneObjects.ForEach(sceneObject =>
                {
                    foreach (Mesh mesh in sceneObject.GetMeshInstancesRecursively())
                    {
                        mesh.Render();
                    }
                }
            );
            
            Gl.BindFramebuffer(GLEnum.Framebuffer, 0);

            var uv0 = new vec2(0, 1);
            var uv1 = new vec2(1, 0);
            
            ImGui.Image(new ImTextureRef(texId: (ulong)textureColorBuffer), size, new Vector2(uv0.x, uv0.y), new Vector2(uv1.x, uv1.y));
        }
        
        ImGui.End();
        
        ImGui.PopStyleVar(2);
    }
}