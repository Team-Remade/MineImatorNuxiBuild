using System.Reflection;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

internal static partial class Program
{
    private static void ModelPartCatalogTests(Fixture fixture)
    {
        string data = Path.GetFullPath("data");
        if (!Directory.Exists(data))
            data = Path.GetFullPath(Path.Combine("MineImatorSimplyRemade", "data"));
        var injected = new List<List<CharacterEntry>>();
        var expected = new List<CharacterEntry>();
        try
        {
            foreach (string name in new[] { "characters", "equipment", "props" })
            {
                var catalog = (List<CharacterEntry>)typeof(CharacterRegistry).GetField(
                    "_" + name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                Check(catalog.Count == 0, "Model catalog starts empty");
                injected.Add(catalog);
                foreach (string directory in Directory.EnumerateDirectories(data, name, SearchOption.AllDirectories))
                foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Where(path => Path.GetExtension(path) is ".mimodel" or ".glb"))
                {
                    var entry = new CharacterEntry { Name = Path.GetFileNameWithoutExtension(path), FilePath = path };
                    catalog.Add(entry);
                    var parts = CharacterRegistry.GetModelParts(path);
                    if (parts.Count > 1) expected.Add(entry);
                    if (Path.GetExtension(path) != ".mimodel") continue;
                    var model = MineImatorLoader.Instance.LoadModel(path)!;
                    foreach (var part in parts)
                    {
                        var siblings = model.Parts;
                        MiPart? selected = null;
                        foreach (int index in part.SourceNodePath)
                        {
                            selected = siblings[index];
                            siblings = selected.Parts;
                        }
                        Equal(part.Name, selected!.Name, "Source path matches authored part");
                        Check(selected.Shapes.Count > 0, "Only geometry-bearing parts listed");
                    }
                }
                Check(catalog.Count > 0, $"{name} bundled catalog covered");
                Check(catalog.Any(expected.Contains), $"{name} multi-part models covered");
            }
            string single = fixture.File("single.mimodel",
                """{"name":"single","texture":"none","texture_size":[16,16],"parts":[{"name":"wrapper","parts":[{"name":"only","shapes":[{"type":"block","from":[0,0,0],"to":[1,1,1]}]}]}]}""");
            Equal(1, CharacterRegistry.GetModelParts(single).Count, "Empty wrapper is not an extra part");
            injected[0].Add(new CharacterEntry { Name = "single", FilePath = single });
            var variantModel = new CharacterEntry
            {
                Name = "multi-part variant",
                FilePath = single,
                TextureVariants = [new CharacterTextureVariant { Name = "multi-part", ModelPath = expected[0].FilePath }]
            };
            injected[0].Add(variantModel);
            expected.Add(variantModel);
            var spawn = new SpawnMenu();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(SpawnMenu).GetField("_selectedCategory", flags)!.SetValue(spawn, "Model part");
            var actual = (IReadOnlyList<CharacterEntry>)typeof(SpawnMenu)
                .GetProperty("SelectedModelCatalog", flags)!.GetValue(spawn)!;
            Equal(expected.Count, actual.Count, "Exactly all multi-part built-in models included");
            Check(actual.All(expected.Contains), "Single-part models excluded");
            typeof(SpawnMenu).GetField("_selectedObjectIndex", flags)!.SetValue(spawn, actual.Count - 1);
            typeof(SpawnMenu).GetField("_searchQuery", flags)!.SetValue(spawn, actual[^1].Name);
            var previewObjects = (List<string>)typeof(SpawnMenu).GetMethod("GetFilteredObjects", flags)!.Invoke(spawn, [])!;
            Equal(actual.Count, previewObjects.Count, "Preview uses the same registry indices as model selection");
            Check((bool)typeof(SpawnMenu).GetMethod("CanSpawn", flags)!.Invoke(spawn, [])!,
                "Selecting a model part enables the spawn button even with search active");
            typeof(SpawnMenu).GetField("_selectedVariantIndex", flags)!.SetValue(spawn, int.MaxValue);
            Check(!(bool)typeof(SpawnMenu).GetMethod("CanSpawn", flags)!.Invoke(spawn, [])!,
                "An unavailable part cannot be spawned");
            Console.WriteLine($"Checked {injected.Sum(catalog => catalog.Count)} models; {actual.Count} multi-part models.");
        }
        finally
        {
            foreach (var catalog in injected) catalog.Clear();
        }
    }

    private static void ModelPartGraphicsTests(SpawnMenu spawn, Viewport viewport, string native,
        Action update, Func<byte[]> readPixels)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(SpawnMenu).GetField("_selectedCategory", flags)!.SetValue(spawn, "Model part");
        typeof(SpawnMenu).GetField("_selectedObjectIndex", flags)!.SetValue(spawn, 0);
        var parts = CharacterRegistry.GetModelParts(native);
        int armIndex = parts.ToList().FindIndex(part => part.Name == "right_arm");
        Check(armIndex >= 0, "Steve arm is offered");
        typeof(SpawnMenu).GetField("_selectedVariantIndex", flags)!.SetValue(spawn, armIndex);
        Check((bool)typeof(SpawnMenu).GetMethod("CanSpawn", flags)!.Invoke(spawn, [])!,
            "Selected arm enables the actual spawn-button predicate");
        update();
        var preview = (MiBoneSceneObject)typeof(SpawnMenu).GetField("_previewCharacter", flags)!.GetValue(spawn)!;
        Equal("right_arm", preview.BoneName, "Preview contains only selected arm");
        Equal(0, preview.Children.Count, "Preview excludes other parts");
        int pixels = 0;
        byte[] image = readPixels();
        for (int i = 0; i < image.Length; i += 4)
            if (Math.Abs(image[i] - 31) > 2 || Math.Abs(image[i + 1] - 31) > 2 ||
                Math.Abs(image[i + 2] - 36) > 2) pixels++;
        Check(pixels > 100, $"Arm preview renders pixels ({pixels})");
        ModelPartPreviewColumnTests(spawn);
        typeof(SpawnMenu).GetField("_placeWithCursor", flags)!.SetValue(spawn, false);
        typeof(SpawnMenu).GetMethod("TrySpawn", flags)!.Invoke(spawn, []);
        var arm = (MiBoneSceneObject)viewport.SceneObjects.Single();
        Equal("right_arm", arm.BoneName, "Menu spawns just Steve's arm");
        Equal(vec3.Zero, arm.OffsetPosition, "Part pivot starts at placement origin");
        Equal(0, arm.Children.Count, "Spawn excludes descendants");
        Check(arm.SourceNodePath!.SequenceEqual(parts[armIndex].SourceNodePath), "Stable source identity");
        Equal(preview.Visuals.Count, arm.Visuals.Count, "Spawn matches preview geometry");
        Check(arm.Visuals.All(mesh => mesh.TextureId != 0), "Part keeps skin texture");
        arm.SetEditableBendAngle(new vec3(35f, 0f, 0f));
        arm.RegenerateMeshes();
        Check(arm.Visuals.Count > 0, "Detached part bend regenerates");
        arm.SetLocalPosition(new vec3(2f, 3f, 4f));
        var entry = ProjectSceneSerializer.SerializeObjectForLibrary(arm);
        viewport.SceneObjects.Remove(arm);
        var restored = (MiBoneSceneObject)ProjectSceneSerializer.SpawnObjectFromEntry(entry, viewport, spawn)!;
        Equal(0, restored.Children.Count, "Reload never restores full model");
        Equal(arm.Position, restored.Position, "Placement survives reload");
        Equal(arm.OffsetPosition, restored.OffsetPosition, "Part base pose survives reload");
        Equal(arm.GetEditableBendAngle(), restored.GetEditableBendAngle(), "Bend survives reload");
        using var fixture = new Fixture();
        string archive = Path.Combine(fixture.Root, "arm.nxObj");
        NxObjectArchive.Export(archive, arm);
        viewport.SceneObjects.Remove(restored);
        var imported = NxObjectArchive.Import(archive, viewport, spawn);
        Equal(0, imported.Children.Count, "Object export/import retains isolated part");
        var duplicate = typeof(SceneTree).GetMethod("CreateSceneObjectDuplicate", flags)!;
        var clone = (MiBoneSceneObject)duplicate.Invoke(new SceneTree { Gl = spawn.Gl },
            [arm, new Dictionary<string, string>()])!;
        Equal(arm.GetEditableBendAngle(), clone.GetEditableBendAngle(), "Duplication keeps bend");
        clone.RegenerateMeshes();
        Check(clone.Visuals.Count > 0, "Duplicate bend regenerates");
        foreach (var obj in new[] { arm, restored, imported, clone })
        {
            foreach (var mesh in obj.GetMeshInstancesRecursively()) mesh.Dispose();
            if (obj is BoneSceneObject bone) bone.IndicatorMesh?.Dispose();
        }
        viewport.SceneObjects.Clear();

        int bodyIndex = parts.ToList().FindIndex(part => part.Name == "body");
        var body = spawn.SpawnModelPartFromPath(native, parts[bodyIndex].SourceNodePath)!;
        Equal(0, body.Children.Count, "Spawning body does not include head or arms");
        foreach (var mesh in body.Visuals) mesh.Dispose();
        ((BoneSceneObject)body).IndicatorMesh?.Dispose();
        viewport.SceneObjects.Clear();
        int headIndex = parts.ToList().FindIndex(part => part.Name == "head");
        string skin = Path.Combine(Path.GetDirectoryName(native)!, "herobrine.png");
        var head = spawn.SpawnModelPartFromPath(native, parts[headIndex].SourceNodePath, skin)!;
        Equal(0, head.Children.Count, "Spawning head excludes hat");
        Equal(skin, head.TextureOverridePath, "Selected texture recorded on isolated part");
        uint texture = MineImatorLoader.Instance.LoadTextureFromFile(skin, makeSquare: true);
        Check(head.Visuals.All(mesh => mesh.TextureId == texture), "Selected skin applied to part");
        var headEntry = ProjectSceneSerializer.SerializeObjectForLibrary(head);
        viewport.SceneObjects.Remove(head);
        var headReload = ProjectSceneSerializer.SpawnObjectFromEntry(headEntry, viewport, spawn)!;
        Check(headReload.Visuals.All(mesh => mesh.TextureId == texture), "Selected skin survives reload");
        foreach (var obj in new[] { head, headReload })
        {
            foreach (var mesh in obj.Visuals) mesh.Dispose();
            ((BoneSceneObject)obj).IndicatorMesh?.Dispose();
        }
        viewport.SceneObjects.Clear();
        Console.WriteLine("PASS Model part preview, menu spawn, textures, bends, reload, archive and duplication");
    }

    private static unsafe void ModelPartPreviewColumnTests(SpawnMenu spawn)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new System.Numerics.Vector2(800, 700);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var renderer = (PreviewRenderer)typeof(SpawnMenu).GetField("_previewRenderer", flags)!.GetValue(spawn)!;
            bool hasImage = false;
            for (int frame = 0; frame < 2; frame++)
            {
                ImGui.NewFrame();
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(400, 400), ImGuiCond.Always);
                ImGui.Begin("Part preview regression");
                typeof(SpawnMenu).GetMethod("RenderStandardPreviewColumn", flags)!.Invoke(spawn, []);
                ImGui.End();
                ImGui.Render();
                var data = ImGui.GetDrawData();
                for (int i = 0; i < data.CmdListsCount; i++)
                for (int j = 0; j < data.CmdLists[i].CmdBuffer.Size; j++)
                    if (data.CmdLists[i].CmdBuffer[j].TexRef.TexID == (ulong)renderer.ColorTexture)
                        hasImage = true;
            }
            Check(hasImage, "Actual spawn-menu preview column displays the part's rendered texture");
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }
}
