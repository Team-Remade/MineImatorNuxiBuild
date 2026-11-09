using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Hexa.NET.ImGui;
using ImageMagick;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemadeNuxi.core.objs;
using StbImageSharp;
using Silk.NET.OpenGL;

internal static partial class Program
{
    private static void RejectTextureSheet(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is ArgumentException or IOException or MagickException) { return; }
        throw new InvalidOperationException("Invalid texture sheet operation was accepted.");
    }

    private static string SheetImage(Fixture fixture)
    {
        byte[] pixels = new byte[6 * 4 * 4];
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 6; x++)
            {
                int offset = (y * 6 + x) * 4;
                pixels[offset] = (byte)(x * 30);
                pixels[offset + 1] = (byte)(y * 50);
                pixels[offset + 2] = 90;
                pixels[offset + 3] = (byte)(120 + x * 10 + y);
            }
        string path = fixture.NewPath(".png");
        using var image = new MagickImage(pixels, new MagickReadSettings
        {
            Width = 6, Height = 4, Format = MagickFormat.Rgba, Depth = 8
        });
        image.Write(path, MagickFormat.Png32);
        return path;
    }

    private static void WithTextureSheetProject(Fixture fixture, Action<ProjectManager> test)
    {
        var manager = ProjectManager.Instance;
        string folder = manager.ProjectFolder, file = manager.ProjectFilePath;
        var manifest = manager.Manifest;
        bool dirty = manager.IsDirty;
        void Set(string name, object value) => typeof(ProjectManager).GetProperty(name)!.SetValue(manager, value);
        try
        {
            Set(nameof(ProjectManager.ProjectFolder), fixture.Root);
            Set(nameof(ProjectManager.ProjectFilePath), fixture.NewPath(".nxProj"));
            Set(nameof(ProjectManager.Manifest), new ProjectManifest());
            manager.SaveManifest();
            test(manager);
        }
        finally
        {
            Set(nameof(ProjectManager.ProjectFolder), folder);
            Set(nameof(ProjectManager.ProjectFilePath), file);
            Set(nameof(ProjectManager.Manifest), manifest);
            manager.SetDirty(dirty);
        }
    }

    private static void TextureSheetPixels(Fixture fixture)
    {
        var manager = BackupProject(fixture, "sheets");
        string source = SheetImage(fixture);
        var sheet = manager.AddAsset(source, ProjectAssetType.Image, 3, 2);
        int previousFlip = StbImage.stbi__vertically_flip_on_load_global;
        try
        {
            StbImage.stbi_set_flip_vertically_on_load(1);
            for (int row = 0; row < 2; row++)
                for (int column = 0; column < 2; column++)
                {
                    string path = manager.GetTextureSheetTile(sheet, column, row);
                    using var tile = new MagickImage(path);
                    Equal((uint)3, tile.Width, "Non-square tile width");
                    Equal((uint)2, tile.Height, "Non-square tile height");
                    byte[] pixels = tile.ToByteArray(MagickFormat.Rgba);
                    for (int y = 0; y < 2; y++)
                        for (int x = 0; x < 3; x++)
                        {
                            int offset = (y * 3 + x) * 4;
                            int sx = column * 3 + x, sy = row * 2 + y;
                            Equal((byte)(sx * 30), pixels[offset], "Exact tile column pixels");
                            Equal((byte)(sy * 50), pixels[offset + 1], "Top-first tile row pixels");
                            Equal((byte)(120 + sx * 10 + sy), pixels[offset + 3], "Alpha is retained");
                        }
                    Equal(path, manager.GetTextureSheetTile(sheet, column, row), "Repeated selection reuses persistent PNG");
                    Check(ReferenceEquals(sheet, manager.FindTextureSheet(path)), "Tile resolves to its source sheet");
                }
            Equal(1, manager.Manifest.Assets.Count, "Tiles do not clutter imported asset list");
            Equal(1, StbImage.stbi__vertically_flip_on_load_global, "Sheet extraction leaves shared flip state alone");
        }
        finally
        {
            StbImage.stbi_set_flip_vertically_on_load(previousFlip);
        }
    }

    private static void TextureSheetValidation(Fixture fixture)
    {
        var manager = BackupProject(fixture, "validation");
        string source = SheetImage(fixture);
        foreach (var (width, height) in new[] { (0, 2), (-1, 2), (3, 0), (4, 2), (3, 3), (7, 2) })
            RejectTextureSheet(() => manager.AddAsset(source, ProjectAssetType.Image, width, height));
        Equal(0, manager.Manifest.Assets.Count, "Rejected sheets leave no asset entries");
        var plain = manager.AddAsset(source, ProjectAssetType.Image);
        Equal(0, plain.TextureTileWidth, "Ordinary image imports are unchanged");
        Check(manager.FindTextureSheet(manager.GetAssetFullPath(plain)) == null, "Ordinary image is not a sheet");
        var sheet = manager.AddAsset(source, ProjectAssetType.Image, 3, 2);
        foreach (var (column, row) in new[] { (-1, 0), (0, -1), (2, 0), (0, 2) })
            RejectTextureSheet(() => manager.GetTextureSheetTile(sheet, column, row));
        Equal(0, sheet.TextureSheetTiles.Count, "Rejected tile selection does not publish a PNG");
        string tile = manager.GetTextureSheetTile(sheet, 1, 1);
        File.Delete(tile);
        Check(File.Exists(manager.GetTextureSheetTile(sheet, 1, 1)), "Missing generated tile is rebuilt");
        File.Delete(manager.GetAssetFullPath(sheet));
        RejectTextureSheet(() => manager.GetTextureSheetTile(sheet, 0, 0));
        var legacy = JsonSerializer.Deserialize("""{"displayName":"old.png"}""",
            AppJsonContext.Default.ProjectAssetEntry)!;
        Equal(0, legacy.TextureTileWidth, "Old project assets default to ordinary images");
    }

    private static void TextureSheetPersistence(Fixture fixture) => WithTextureSheetProject(fixture, manager =>
    {
        var sheet = manager.AddAsset(SheetImage(fixture), ProjectAssetType.Image, 3, 2);
        string albedo = manager.GetTextureSheetTile(sheet, 1, 0);
        string normal = manager.GetTextureSheetTile(sheet, 0, 1);
        var root = new SceneObject
        {
            Name = "Sheet material", AlbedoTexturePath = albedo, NormalTexturePath = normal,
            EmissionTexturePath = albedo, OcclusionTexturePath = normal
        };
        var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(root);
        manager.Manifest.SceneObjects.Add(snapshot);
        manager.SaveManifest();
        var restored = JsonSerializer.Deserialize(File.ReadAllText(manager.ProjectFilePath),
            AppJsonContext.Default.ProjectManifest)!;
        var restoredSheet = restored.Assets.Single();
        Equal(3, restoredSheet.TextureTileWidth, "Tile width persists");
        Equal(2, restoredSheet.TextureTileHeight, "Tile height persists");
        Equal(2, restoredSheet.TextureSheetTiles.Count, "Tile mappings persist");
        Equal(manager.ToProjectRelativePath(albedo), restored.SceneObjects.Single().AlbedoTexturePath, "Selection is project-relative");
        Check(restoredSheet.TextureSheetTiles.Values.All(path => !Path.IsPathRooted(path)), "Tile paths are portable");
        Check(TextureSheetPicker.Label(albedo).Contains("Column 2, Row 1"), "Selected tile label identifies sheet and coordinates");
        var (archive, directory) = fixture.Roundtrip(root);
        Check(archive.Root.AlbedoTexturePath != archive.Root.NormalTexturePath, "Material slots retain independent selections in object export");
        Check(File.Exists(Path.Combine(directory, archive.Root.AlbedoTexturePath)), "Object export contains selected tile pixels");
        root.AlbedoTexturePath = normal;
        typeof(ProjectSceneSerializer).GetMethod("ApplyEntryToObject", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [root, snapshot, null]);
        Equal(snapshot.AlbedoTexturePath, root.AlbedoTexturePath, "Snapshot undo restores selected tile");
        Equal(albedo, TextureSheetPicker.ResolveSelection(albedo), "Selected tile is not replaced with the default slot");
        Equal(manager.GetTextureSheetTile(sheet, 0, 0), TextureSheetPicker.ResolveSelection(manager.GetAssetFullPath(sheet)),
            "Choosing a sheet defaults to its first tile");
        string converted = fixture.NewPath(".png");
        File.Copy(normal, converted);
        manager.RegisterTextureSheetTileAlias(normal, converted);
        Check(ReferenceEquals(sheet, manager.FindTextureSheet(converted)), "Converted material map retains its sheet identity");
        Check(TextureSheetPicker.Label(converted).Contains("Column 1, Row 2"), "Converted map retains its selected coordinates");
    });

    private static unsafe void TextureSheetControls(Fixture fixture) => WithTextureSheetProject(fixture, manager =>
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new Vector2(1000, 900);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            Localization.SetLanguage("en_us");
            var dialog = new TextureImportDialog();
            string source = SheetImage(fixture);
            dialog.Request(source, _ => throw new InvalidOperationException("Rendering must not import."));
            void Frame()
            {
                ImGui.NewFrame();
                ImGui.Begin("Texture sheet controls");
                dialog.Render();
                ImGui.End();
                ImGui.Render();
            }
            void OverlayChecks()
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                void Set(string name, object value) => typeof(TextureImportDialog).GetField(name, flags)!.SetValue(dialog, value);
                Set("_imageWidth", 6);
                Set("_imageHeight", 4);
                Set("_tileWidth", 3);
                Set("_tileHeight", 2);
                var overlay = typeof(TextureImportDialog).GetMethod("RenderTileCuts", flags)!;
                ImGui.NewFrame();
                ImGui.Begin("Tile cut overlay");
                var draw = ImGui.GetWindowDrawList();
                Vector2 origin = ImGui.GetCursorScreenPos();
                int DrawCuts()
                {
                    int before = draw.VtxBuffer.Size;
                    overlay.Invoke(dialog, [origin, new Vector2(300, 200)]);
                    return draw.VtxBuffer.Size - before;
                }
                Set("_useSheet", false);
                int borderVertices = DrawCuts();
                Set("_useSheet", true);
                int firstVertex = draw.VtxBuffer.Size;
                int cutVertices = DrawCuts();
                Check(cutVertices > borderVertices, "Sheet preview draws tile cuts over the image");
                bool vertical = false, horizontal = false;
                for (int i = firstVertex; i < draw.VtxBuffer.Size; i++)
                {
                    Vector2 point = draw.VtxBuffer[i].Pos - origin;
                    vertical |= Math.Abs(point.X - 150) < 3 && Math.Abs(point.Y) < 3;
                    horizontal |= Math.Abs(point.X) < 3 && Math.Abs(point.Y - 100) < 3;
                }
                Check(vertical && horizontal, "Tile cuts align with source pixel boundaries in an aspect-correct preview");
                Set("_tileWidth", 2);
                Check(DrawCuts() > cutVertices, "Changing tile width updates the overlay immediately");
                Set("_tileWidth", 0);
                Set("_tileHeight", -1);
                Equal(borderVertices, DrawCuts(), "Invalid tile dimensions draw no cuts and do not divide by zero");
                ImGui.End();
                ImGui.Render();
            }
            Frame();
            typeof(TextureImportDialog).GetField("_useSheet", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, true);
            Frame();
            Equal(0, manager.Manifest.Assets.Count, "Opening modal and enabling sheet does not commit an import");
            Equal("Use as sheet", Localization.T("text.texture_sheet.use"), "Checkbox is localized");
            Equal("Tile width (pixels)", Localization.T("text.texture_sheet.width"), "Tile width is labeled in pixels");
            OverlayChecks();
            var sheet = manager.AddAsset(source, ProjectAssetType.Image, 3, 2);
            string path = manager.GetTextureSheetTile(sheet, 1, 1);
            ImGui.NewFrame();
            ImGui.Begin("Tile field");
            Check(!TextureSheetPicker.Render("Test", path, out string selected), "Opening tile controls does not change selection");
            Equal(path, selected, "Picker preserves the current material path");
            ImGui.End();
            ImGui.Render();
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    });

    private static unsafe void TextureSheetGraphics()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        Check(glfw.Init(), "GLFW initialization");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "Texture sheet test", null, null);
        Check(window != null, "OpenGL context");
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = GL.GetApi(glfw.GetProcAddress);
            MineImatorLoader.Instance.Initialize(gl);
            using var fixture = new Fixture();
            WithTextureSheetProject(fixture, manager =>
            {
                var sheet = manager.AddAsset(SheetImage(fixture), ProjectAssetType.Image, 3, 2);
                string path = manager.GetTextureSheetTile(sheet, 1, 1);
                foreach (string category in new[] { "Primitives", "Blocks", "Custom Models" })
                {
                    var obj = new SceneObject { SpawnCategory = category };
                    uint texture = obj.LoadTexture(path);
                    Check(texture != 0, "Selected tile uploads through ordinary material loader");
                    gl.BindTexture(GLEnum.Texture2D, texture);
                    gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureWidth, out int width);
                    gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureHeight, out int height);
                    Equal(3, width, "GPU tile width");
                    Equal(2, height, "GPU tile height");
                    byte[] pixels = new byte[width * height * 4];
                    fixed (byte* pointer = pixels)
                        gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, pointer);
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            int sy = category == "Primitives" ? 3 - y : 2 + y;
                            int offset = (y * width + x) * 4;
                            Equal((byte)((3 + x) * 30), pixels[offset], "GPU contains only the selected column");
                            Equal((byte)(sy * 50), pixels[offset + 1], "GPU preserves category-specific row orientation");
                            Equal((byte)(120 + (3 + x) * 10 + sy), pixels[offset + 3], "GPU retains tile alpha");
                        }
                }
                TextureSheetPreviewGraphics(gl, fixture, manager);
            });
        }
        finally
        {
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }

    private static unsafe void TextureSheetPreviewGraphics(GL gl, Fixture fixture, ProjectManager manager)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        var dialog = new TextureImportDialog();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string projectFolder = manager.ProjectFolder;
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new Vector2(1000, 900);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            dialog.Request(SheetImage(fixture), _ => throw new InvalidOperationException("Preview must not import."));
            void Frame()
            {
                ImGui.NewFrame();
                ImGui.Begin("Import preview test");
                dialog.Render(gl);
                ImGui.End();
                ImGui.Render();
            }
            Frame();
            uint texture = (uint)typeof(TextureImportDialog).GetField("_previewTexture", flags)!.GetValue(dialog)!;
            Check(texture != 0, "Import dialog uploads an image preview");
            Frame();
            gl.BindTexture(GLEnum.Texture2D, texture);
            gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureWidth, out int width);
            gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureHeight, out int height);
            Equal(6, width, "Preview retains source width for a small image");
            Equal(4, height, "Preview retains source height for a small image");
            byte[] pixels = new byte[width * height * 4];
            fixed (byte* pointer = pixels)
                gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, pointer);
            Equal((byte)0, pixels[1], "Preview starts at the top image row");
            Equal((byte)150, pixels[(height - 1) * width * 4 + 1], "Preview includes the bottom row without flipping");
            Equal((byte)120, pixels[3], "Preview retains transparency");
            bool drawsImage = false;
            var data = ImGui.GetDrawData();
            for (int i = 0; i < data.CmdListsCount; i++)
                for (int j = 0; j < data.CmdLists[i].CmdBuffer.Size; j++)
                    drawsImage |= data.CmdLists[i].CmdBuffer[j].TexRef.TexID == (ulong)texture;
            Check(drawsImage, "Modal draw commands display the preview texture");
            Frame();
            Equal(texture, (uint)typeof(TextureImportDialog).GetField("_previewTexture", flags)!.GetValue(dialog)!,
                "Preview upload is reused between frames");
            typeof(ProjectManager).GetProperty(nameof(ProjectManager.ProjectFolder))!.SetValue(manager, fixture.Root + "-changed");
            Frame();
            Check(!gl.IsTexture(texture), "Leaving the project releases the dialog preview texture");
        }
        finally
        {
            typeof(ProjectManager).GetProperty(nameof(ProjectManager.ProjectFolder))!.SetValue(manager, projectFolder);
            typeof(TextureImportDialog).GetMethod("ReleasePreview", flags)!.Invoke(dialog, null);
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }
}
