using System.Reflection;
using System.Text.Json;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static void OpaqueLeavesSettings(Fixture fixture)
    {
        var defaults = JsonSerializer.Deserialize("{}", AppJsonContext.Default.ProjectRenderSettings)!;
        Check(!defaults.OpaqueLeaves, "Old projects retain transparent leaves");
        bool previous = TerrainAtlas.OpaqueLeaves;
        try
        {
            var manifest = new ProjectManifest();
            new PropertiesPanel { OpaqueLeaves = true }.WriteProjectSettingsToManifest(manifest);
            string json = JsonSerializer.Serialize(manifest, AppJsonContext.Default.ProjectManifest);
            var restored = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectManifest)!;
            var loaded = new PropertiesPanel();
            loaded.LoadProjectSettingsFromManifest(restored);
            Check(loaded.OpaqueLeaves && TerrainAtlas.OpaqueLeaves, "Opaque leaves persist and apply on load");
            loaded.LoadProjectSettingsFromManifest(new ProjectManifest());
            Check(!loaded.OpaqueLeaves && !TerrainAtlas.OpaqueLeaves, "New projects reset opaque leaves");

            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var viewport = new Viewport();
            var signature = typeof(Viewport).GetMethod("ComputeShadowCacheSignature",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (bool pointLights in new[] { false, true })
            {
                TerrainAtlas.SetOpaqueLeaves(false);
                int before = (int)signature.Invoke(viewport, [viewport.SceneObjects, pointLights])!;
                TerrainAtlas.SetOpaqueLeaves(true);
                int after = (int)signature.Invoke(viewport, [viewport.SceneObjects, pointLights])!;
                Check(before != after, "Opaque leaves invalidate directional and point shadow caches");
            }
            var isLeaf = typeof(TerrainAtlas).GetMethod("IsLeafTexture", flags)!;
            foreach (string key in new[] { "oak_leaves", "cherry_leaves", "pack:mod/path/red_poplar_leaves", "leaves_oak" })
                Check((bool)isLeaf.Invoke(null, [key])!, $"Leaf texture recognized: {key}");
            foreach (string key in new[] { "bamboo_large_leaves", "bamboo_small_leaves", "oak_log", "glass", "oak_leaves_n" })
                Check(!(bool)isLeaf.Invoke(null, [key])!, $"Non-leaf texture excluded: {key}");
            byte[] original = [12, 34, 56, 255, 200, 150, 100, 0, 100, 80, 60, 128];
            var create = typeof(TerrainAtlas).GetMethod("CreateOpaqueLeafPixels", flags)!;
            var opaque = (byte[])create.Invoke(null, [original])!;
            Check(opaque.SequenceEqual(new byte[] { 12, 34, 56, 255, 0, 0, 0, 255, 50, 40, 30, 255 }),
                "Opaque variant preserves leaf color and composites holes over black");
            Equal((byte)0, original[7], "Original cutout pixels remain unchanged");
        }
        finally
        {
            TerrainAtlas.SetOpaqueLeaves(previous);
        }
    }

    private static unsafe void OpaqueLeavesGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        var window = glfw.CreateWindow(16, 16, "Opaque leaves regression", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var glField = typeof(TerrainAtlas).GetField("_gl", flags)!;
            object? previousGl = glField.GetValue(null);
            bool previousOpaque = TerrainAtlas.OpaqueLeaves;
            uint texture = 0;
            uint ctmTexture = 0;
            const string key = "test_opaque_leaves";
            byte[] source = [100, 150, 200, 255, 90, 80, 70, 0];
            var upsert = typeof(TerrainAtlas).GetMethod("UpsertTexture", flags)!;
            byte[] Read(uint id)
            {
                gl.BindTexture(GLEnum.Texture2D, id);
                byte[] pixels = new byte[source.Length];
                fixed (byte* data = pixels)
                    gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, data);
                return pixels;
            }
            try
            {
                glField.SetValue(null, gl);
                TerrainAtlas.SetOpaqueLeaves(false);
                upsert.Invoke(null, [key, source, 2, 1, false]);
                texture = TerrainAtlas.Textures[key];
                Check(Read(texture).SequenceEqual(source), "Initial GPU cutout is unchanged");
                TerrainAtlas.SetOpaqueLeaves(true);
                upsert.Invoke(null, [key, source, 2, 1, false]);
                texture = TerrainAtlas.Textures[key];
                Equal((byte)255, Read(texture)[7], "Late-loaded pack leaf textures are opaque");
                TerrainAtlas.SetOpaqueLeaves(false);
                Check(Read(texture).SequenceEqual(source), "Reloaded textures retain original cutout pixels");
                TerrainAtlas.SetOpaqueLeaves(true);
                Equal(texture, TerrainAtlas.Textures[key], "Toggling preserves mesh texture handles");
                Check(Read(texture).SequenceEqual(new byte[] { 100, 150, 200, 255, 0, 0, 0, 255 }),
                    "GPU texture contains the opaque variant");
                Check(TerrainAtlas.TilePixels[key].SequenceEqual(Read(texture)), "CPU and GPU pixels agree");
                TerrainAtlas.SetOpaqueLeaves(false);
                Check(Read(texture).SequenceEqual(source), "Disabling restores exact original GPU pixels");
                TerrainAtlas.SetOpaqueLeaves(true);
                byte[] imageBytes =
                [
                    0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 1, 0, 32, 40,
                    200, 150, 100, 255, 70, 80, 90, 0
                ];
                var loadCtm = typeof(CtmAtlas).GetMethod("LoadTextureFromBytes", flags)!;
                var ctmGlField = typeof(CtmAtlas).GetField("_gl", flags)!;
                object? previousCtmGl = ctmGlField.GetValue(null);
                try
                {
                    ctmGlField.SetValue(null, gl);
                    ctmTexture = (uint)loadCtm.Invoke(null, [imageBytes, 0u, "test:ctm/leaves/0"])!;
                    Check(ctmTexture != 0, "Connected leaf texture loads");
                    Equal((byte)255, Read(ctmTexture)[7], "Late-loaded CTM leaves use opaque pixels");
                    TerrainAtlas.SetOpaqueLeaves(false);
                    Check(Read(ctmTexture).SequenceEqual(source), "CTM leaves restore cutout pixels");
                }
                finally
                {
                    ctmGlField.SetValue(null, previousCtmGl);
                }
                Equal(GLEnum.NoError, gl.GetError(), "No OpenGL errors");
            }
            finally
            {
                var unregister = typeof(TerrainAtlas).GetMethod("UnregisterLeafTexture", flags)!;
                foreach (uint id in new[] { texture, ctmTexture })
                {
                    unregister.Invoke(null, [id]);
                    if (id != 0) gl.DeleteTexture(id);
                }
                TerrainAtlas.Textures.Remove(key);
                TerrainAtlas.TilePixels.Remove(key);
                TerrainAtlas.SetOpaqueLeaves(previousOpaque);
                glField.SetValue(null, previousGl);
            }
        }
        finally
        {
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }
}
