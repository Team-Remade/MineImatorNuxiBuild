using System.IO.Compression;
using System.Reflection;
using System.Text;
using GlmSharp;
using Hexa.NET.ImGui;
using ImageMagick;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.render;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using Silk.NET.OpenGL;
using StbImageSharp;

internal static partial class Program
{
    private static unsafe void ResourcePackAnimationGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "Resource-pack animation GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Resource-pack animation regression", null, null);
        Check(window != null, "Resource-pack animation OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            MineImatorLoader.Instance.Initialize(gl);
            TerrainAtlas.Initialize(gl);
            ItemsAtlas.Initialize(gl);
            using var fixture = new Fixture();
            string originalProjectRoot = (string)(typeof(MinecraftDataLoader).GetField("_projectRoot",
                BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null) ?? "");
            string packId = "animation-regression-" + Guid.NewGuid().ToString("N");
            string packPath = Path.Combine(fixture.Root, "mods", "resourcepacks", packId + ".zip");
            Directory.CreateDirectory(Path.GetDirectoryName(packPath)!);
            byte[] spriteSheet = CreateSpriteSheet();
            using (var archive = ZipFile.Open(packPath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "assets/minecraft/textures/block/animated_test.png", spriteSheet);
                WriteEntry(archive, "assets/minecraft/textures/block/animated_test.png.mcmeta",
                    Encoding.UTF8.GetBytes("{\"animation\":{\"frametime\":1}}"));
                WriteEntry(archive, "assets/minecraft/textures/item/item_test.png", CreateSolidPng(0, 0, 255));
            }

            const string oldTerrainKey = "existing-terrain-animation-regression";
            const string oldItemKey = "existing-item-animation-regression";
            var oldTerrainPixels = new byte[] { 17, 34, 51, 255 };
            var oldItemPixels = new byte[] { 68, 85, 102, 255 };
            uint oldTerrainTexture = CreateTestTexture(gl, oldTerrainPixels, 1, 1);
            uint oldItemTexture = CreateTestTexture(gl, oldItemPixels, 1, 1);
            TerrainAtlas.Textures[oldTerrainKey] = oldTerrainTexture;
            TerrainAtlas.TilePixels[oldTerrainKey] = oldTerrainPixels;
            ItemsAtlas.Textures[oldItemKey] = oldItemTexture;
            ItemsAtlas.TilePixels[oldItemKey] = oldItemPixels;
            ItemsAtlas.TileDimensions[oldItemKey] = (1, 1);
            var oldTerrain = new KeyValuePair<string, uint>(oldTerrainKey, oldTerrainTexture);
            var oldItems = new KeyValuePair<string, uint>(oldItemKey, oldItemTexture);
            var originalTerrainPixels = ReadTexture(gl, oldTerrain.Value);
            var originalItemPixels = ReadTexture(gl, oldItems.Value);
            string sheetPath = SheetImage(fixture);
            string sheetKey = ItemsAtlas.BuildTemporaryItemSheetKey(sheetPath, 2, 2);
            string sheetTileKey = sheetKey + ":0,1";
            Check(ItemsAtlas.TryRegisterTemporaryItemSheet(sheetKey, sheetPath, 2, 2), "Register imported item sheet");
            Check(ItemsAtlas.TryRegisterTemporaryItemTile(sheetKey, sheetTileKey, 0, 1), "Register imported sheet tile");
            uint sheetTexture = ItemsAtlas.Textures[sheetTileKey];
            var sheetPixels = ReadTexture(gl, sheetTexture);
            string animatedKey = MinecraftDataLoader.BuildResourcePackTextureKeyFromId(packId, "animated_test");
            string itemKey = MinecraftDataLoader.BuildResourcePackTextureKeyFromId(packId, "item_test");
            var oldTimelineTime = Mesh.AnimatedTextureTimelineTimeSeconds;
            double oldSpeed = Mesh.AnimatedTextureSpeedScale;
            bool oldAdvance = Mesh.AdvanceAnimatedTextures;
            try
            {
                MinecraftDataLoader.SetProjectRoot(fixture.Root);
                var packIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { packId };
                TerrainAtlas.LoadAdditionalResourcePacks(packIds);
                ItemsAtlas.LoadAdditionalResourcePacks(packIds);

                Check(TerrainAtlas.Textures.ContainsKey(animatedKey), "New animated block texture is loaded");
                Check(ItemsAtlas.Textures.ContainsKey(itemKey), "New item texture is loaded");
                Check(TerrainAtlas.AnimatedTextures.ContainsKey(animatedKey), "Animated texture metadata is loaded");
                Check(gl.IsTexture(oldTerrain.Value), "Existing terrain texture handle remains valid");
                Check(gl.IsTexture(oldItems.Value), "Existing item texture handle remains valid");
                Check(ReadTexture(gl, oldTerrain.Value).Pixels.SequenceEqual(originalTerrainPixels.Pixels),
                    "Existing terrain texture pixels survive the import");
                Check(ReadTexture(gl, oldItems.Value).Pixels.SequenceEqual(originalItemPixels.Pixels),
                    "Existing item texture pixels survive the import");
                Equal(sheetTexture, ItemsAtlas.Textures[sheetTileKey], "Imported model item sheet keeps its handle");
                Check(gl.IsTexture(sheetTexture), "Imported model item sheet handle remains live");
                Check(ReadTexture(gl, sheetTexture).Pixels.SequenceEqual(sheetPixels.Pixels),
                    "Imported model item sheet retains exact pixels");
                Equal((3, 2), ItemsAtlas.TileDimensions[sheetTileKey], "Imported tile keeps rectangular dimensions");

                using var mesh = new Mesh(gl);
                mesh.Vertices.AddRange([new(-0.5f, -0.5f, -1), new(0.5f, -0.5f, -1),
                    new(0.5f, 0.5f, -1), new(-0.5f, 0.5f, -1)]);
                mesh.Normals.AddRange(Enumerable.Repeat(vec3.UnitZ, 4));
                mesh.TexCoords.AddRange([new(0, 0), new(1, 0), new(1, 1), new(0, 1)]);
                mesh.Indices = [0, 1, 2, 0, 2, 3];
                mesh.TextureId = TerrainAtlas.Textures[animatedKey];
                mesh.AnimationKey = animatedKey;
                mesh.Unlit = true;
                mesh.DoubleSided = true;
                mesh.Upload();

                byte[] Render(double timeSeconds)
                {
                    Mesh.AnimatedTextureTimelineTimeSeconds = timeSeconds;
                    gl.BindFramebuffer(GLEnum.Framebuffer, 0);
                    gl.Viewport(0, 0, 64, 64);
                    gl.Disable(GLEnum.Blend);
                    gl.Disable(GLEnum.CullFace);
                    gl.Disable(GLEnum.DepthTest);
                    gl.ClearColor(0, 0, 0, 0);
                    gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    mesh.Render(mat4.Identity, mat4.Identity,
                        mat4.Ortho(-0.5f, 0.5f, -0.5f, 0.5f, 0.1f, 10), useSceneLighting: false);
                    gl.Finish();
                    byte[] pixels = new byte[64 * 64 * 4];
                    fixed (byte* pointer = pixels)
                        gl.ReadPixels(0, 0, 64, 64, GLEnum.Rgba, GLEnum.UnsignedByte, pointer);
                    return pixels;
                }

                Mesh.AdvanceAnimatedTextures = true;
                Mesh.AnimatedTextureSpeedScale = 1;
                byte[] frameZero = Render(0);
                byte[] frameOne = Render(SecondsPerAnimationTick);
                Check(!frameZero.SequenceEqual(frameOne), "Animation samples a different spritesheet frame at the next tick");
                Check(frameOne.SequenceEqual(Render(SecondsPerAnimationTick)), "Repeated views do not advance animation");
                Mesh.AdvanceAnimatedTextures = false;
                Check(frameZero.SequenceEqual(Render(0)), "Paused seeking samples the selected frame");
                Render(SecondsPerAnimationTick);
                Check(frameZero.SequenceEqual(Render(0)), "Backward seek reproduces exact animated pixels");
                Mesh.AnimatedTextureSpeedScale = 2;
                Check(frameOne.SequenceEqual(Render(SecondsPerAnimationTick / 2)),
                    "Texture animation speed scales absolute timeline time");
                Check(frameZero.SequenceEqual(Render(SecondsPerAnimationTick)),
                    "Animated textures loop at the spritesheet duration");

                var shadowState = typeof(Mesh).GetMethod("AddShadowCacheState", BindingFlags.Instance | BindingFlags.NonPublic)!;
                int ShadowHash(double time)
                {
                    Mesh.AnimatedTextureTimelineTimeSeconds = time;
                    object[] arguments = [new HashCode()];
                    shadowState.Invoke(mesh, arguments);
                    return ((HashCode)arguments[0]).ToHashCode();
                }
                Mesh.AnimatedTextureSpeedScale = 1;
                int initialShadow = ShadowHash(0);
                Check(initialShadow != ShadowHash(SecondsPerAnimationTick), "Timeline seek invalidates animated shadow cache");
                Equal(initialShadow, ShadowHash(0), "Backward seek restores the same shadow signature");

                using (var assetPreview = new PreviewRenderer(gl, 64) { AutoRotateSpeed = 0 })
                {
                    Mesh.AnimatedTextureTimelineTimeSeconds = 1.25;
                    assetPreview.Render([mesh], "animated-preview", 0, centerMeshes: true);
                    byte[] first = ReadTexture(gl, assetPreview.ColorTexture).Pixels;
                    assetPreview.Render([mesh], "animated-preview", SecondsPerAnimationTick, centerMeshes: true);
                    Check(!first.SequenceEqual(ReadTexture(gl, assetPreview.ColorTexture).Pixels),
                        "Standalone preview advances independently of a paused timeline");
                    Equal((double?)1.25, Mesh.AnimatedTextureTimelineTimeSeconds, "Preview restores scene texture time");
                    Check(!Mesh.AdvanceAnimatedTextures, "Preview restores paused scene state");
                }
                ResourcePackAnimationCameraGraphics(gl, mesh, fixture);
                Equal(GLEnum.NoError, gl.GetError(), "Resource-pack animation rendering has no OpenGL errors");
            }
            finally
            {
                MinecraftDataLoader.SetProjectRoot(originalProjectRoot);
                Mesh.AnimatedTextureTimelineTimeSeconds = oldTimelineTime;
                Mesh.AnimatedTextureSpeedScale = oldSpeed;
                Mesh.AdvanceAnimatedTextures = oldAdvance;
                RemoveResourcePackTextures(gl, packId);
                TerrainAtlas.Textures.Remove(oldTerrainKey);
                TerrainAtlas.TilePixels.Remove(oldTerrainKey);
                ItemsAtlas.Textures.Remove(oldItemKey);
                ItemsAtlas.TilePixels.Remove(oldItemKey);
                ItemsAtlas.TileDimensions.Remove(oldItemKey);
                ItemsAtlas.Textures.Remove(sheetTileKey);
                ItemsAtlas.TilePixels.Remove(sheetTileKey);
                ItemsAtlas.TileDimensions.Remove(sheetTileKey);
                ItemsAtlas.TemporaryItemSheets.Remove(sheetKey);
                gl.DeleteTexture(sheetTexture);
                gl.DeleteTexture(oldTerrain.Value);
                gl.DeleteTexture(oldItems.Value);
            }
        }
        finally
        {
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }

        static byte[] CreateSpriteSheet()
        {
            byte[] pixels = [255, 0, 0, 255, 0, 255, 0, 255];
            using var image = new MagickImage(pixels, new MagickReadSettings
            {
                Width = 1, Height = 2, Format = MagickFormat.Rgba, Depth = 8
            });
            return image.ToByteArray(MagickFormat.Png);
        }

        static byte[] CreateSolidPng(byte red, byte green, byte blue)
        {
            byte[] pixels = [red, green, blue, 255];
            using var image = new MagickImage(pixels, new MagickReadSettings
            {
                Width = 1, Height = 1, Format = MagickFormat.Rgba, Depth = 8
            });
            return image.ToByteArray(MagickFormat.Png);
        }

        static void WriteEntry(ZipArchive archive, string name, byte[] data)
        {
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(data);
        }

        static uint CreateTestTexture(GL gl, byte[] pixels, int width, int height)
        {
            uint texture = gl.GenTexture();
            gl.BindTexture(GLEnum.Texture2D, texture);
            fixed (byte* pointer = pixels)
                gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height,
                    0, GLEnum.Rgba, GLEnum.UnsignedByte, pointer);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
            return texture;
        }

        static (int Width, int Height, byte[] Pixels) ReadTexture(GL gl, uint texture)
        {
            gl.BindTexture(GLEnum.Texture2D, texture);
            gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureWidth, out int width);
            gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureHeight, out int height);
            byte[] pixels = new byte[width * height * 4];
            fixed (byte* pointer = pixels)
                gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, pointer);
            return (width, height, pixels);
        }

        static void RemoveResourcePackTextures(GL gl, string packId)
        {
            string prefix = MinecraftDataLoader.BuildResourcePackTextureKeyFromId(packId, "");
            foreach (string key in TerrainAtlas.Textures.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            {
                gl.DeleteTexture(TerrainAtlas.Textures[key]);
                TerrainAtlas.Textures.Remove(key);
                TerrainAtlas.TilePixels.Remove(key);
                TerrainAtlas.AnimatedTextures.Remove(key);
            }
            foreach (string key in ItemsAtlas.Textures.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            {
                gl.DeleteTexture(ItemsAtlas.Textures[key]);
                ItemsAtlas.Textures.Remove(key);
                ItemsAtlas.TilePixels.Remove(key);
                ItemsAtlas.TileDimensions.Remove(key);
            }
        }
    }

    private static unsafe void ResourcePackAnimationCameraGraphics(GL gl, Mesh mesh, Fixture fixture)
    {
        var previousContext = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        var previousTimeline = Timeline.Instance;
        var viewport = new Viewport { Gl = gl, OverlaysEnabled = false,
            PropertiesPanel = new PropertiesPanel
            {
                TransparentBackground = true, ShadowsEnabled = false, FogEnabled = false,
                ColorManagementEnabled = false, AntiAliasingEnabled = false
            } };
        var preview = new Viewport { Gl = gl, IsPreviewViewport = true, MainViewport = viewport, OverlaysEnabled = false };
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DeltaTime = 1f / 60f;
            viewport.InitFramebuffer(64, 64);
            preview.InitPreviewViewport(64, 64);
            var obj = new SceneObject { PivotOffset = vec3.Zero };
            obj.Visuals.Add(mesh);
            viewport.SceneObjects.Add(obj);
            var timeline = new Timeline { Viewport = viewport };
            typeof(Timeline).GetProperty(nameof(Timeline.Instance))!.SetValue(null, timeline);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(Timeline).GetField("_frameRate", flags)!.SetValue(timeline, 60f);
            byte[] Render(int frame, bool highQuality)
            {
                timeline.SetCurrentFrameForRender(frame);
                preview.RenderScenePublic(viewport.Camera, null, 64, 64, highQuality: highQuality);
                byte[] pixels = new byte[64 * 64 * 4];
                gl.BindTexture(GLEnum.Texture2D, preview.ColorTexture);
                fixed (byte* pointer = pixels)
                    gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, pointer);
                return pixels;
            }
            foreach (bool highQuality in new[] { false, true })
            {
                byte[] zero = Render(0, highQuality);
                byte[] next = Render(3, highQuality);
                Check(!zero.SequenceEqual(next), "Camera preview samples timeline texture frames");
                foreach (int frame in new[] { 9, 15, 21, 27 })
                    Check(next.SequenceEqual(Render(frame, highQuality)), $"Camera samples exact tick boundary at frame {frame}");
                Check(next.SequenceEqual(Render(3, highQuality)), "Camera redraw does not advance texture time");
                Render(17, highQuality);
                Check(zero.SequenceEqual(Render(0, highQuality)), "Camera backward seek reproduces exact pixels");
                viewport.PropertiesPanel.TextureAnimationFps = 40;
                Check(zero.SequenceEqual(Render(3, highQuality)), "Camera render respects project texture animation speed");
                viewport.PropertiesPanel.TextureAnimationFps = 20;

                byte[] Export(int frame)
                {
                    timeline.SetCurrentFrameForRender(frame);
                    string path = fixture.NewPath(".png");
                    new RenderExporter(preview).ExportImage(64, 64, "png", path, highQuality);
                    using var stream = File.OpenRead(path);
                    return ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha).Data;
                }
                preview.ToggleHighQualityPreview();
                byte[] exportedZero = Export(0);
                Check(!exportedZero.SequenceEqual(Export(3)), "PNG export samples animated texture timeline time");
                Check(exportedZero.SequenceEqual(Export(0)), "PNG export repeats exact pixels after backward seeking");
                preview.ToggleHighQualityPreview();
            }
        }
        finally
        {
            preview.DisposeFramebufferResources();
            viewport.DisposeFramebufferResources();
            typeof(Timeline).GetProperty(nameof(Timeline.Instance))!.SetValue(null, previousTimeline);
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previousContext);
        }
    }

    private const double SecondsPerAnimationTick = 0.05;
}