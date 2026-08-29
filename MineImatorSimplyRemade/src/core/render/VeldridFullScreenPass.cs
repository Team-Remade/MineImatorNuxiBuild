using Veldrid;

namespace MineImatorSimplyRemade.core.render;

/// <summary>
/// Shared plumbing for screen-space post-process passes (ambient occlusion,
/// indirect lighting, and eventually glow/film-grain/edge once those are
/// ported): builds a <see cref="Pipeline"/> from <c>fullscreen.vert</c> plus a
/// caller-supplied fragment shader and <see cref="ResourceLayout"/>, with no
/// vertex buffer at all (the vertex shader synthesizes a full-screen triangle
/// from <c>gl_VertexIndex</c>).
/// </summary>
public static class VeldridFullScreenPass
{
    public static Pipeline CreatePipeline(GraphicsDevice device, string fragmentShaderName,
        ResourceLayout resourceLayout, OutputDescription outputDescription, BlendStateDescription blendState)
    {
        var (vertexShader, fragmentShader) = VeldridShaderCache.GetOrCompile(device, "fullscreen.vert", fragmentShaderName);

        return device.ResourceFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = blendState,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.None, PolygonFillMode.Solid, FrontFace.CounterClockwise, false, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { resourceLayout },
            // No VertexLayoutDescription entries at all - fullscreen.vert takes no vertex input.
            ShaderSet = new ShaderSetDescription(Array.Empty<VertexLayoutDescription>(), new[] { vertexShader, fragmentShader }),
            Outputs = outputDescription,
        });
    }

    /// <summary>Binds <paramref name="pipeline"/>/<paramref name="resourceSet"/> and draws
    /// the 3-vertex full-screen triangle. Caller must have already set the target framebuffer.</summary>
    public static void Draw(CommandList commandList, Pipeline pipeline, ResourceSet resourceSet)
    {
        commandList.SetPipeline(pipeline);
        commandList.SetGraphicsResourceSet(0, resourceSet);
        commandList.Draw(3);
    }
}
