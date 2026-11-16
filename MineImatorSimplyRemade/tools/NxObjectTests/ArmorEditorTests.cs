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
    private static CharacterEntry ArmorEntry(string name)
    {
        string directory = Path.GetFullPath(Path.Combine("data", "minecraft", "versions", "26.3",
            "equipment", "vanilla", name));
        if (!Directory.Exists(directory))
            directory = Path.GetFullPath(Path.Combine("MineImatorSimplyRemade", "data", "minecraft",
                "versions", "26.3", "equipment", "vanilla", name));
        var variants = (IReadOnlyList<CharacterTextureVariant>)typeof(CharacterRegistry)
            .GetMethod("LoadTextureVariants", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [directory])!;
        return new CharacterEntry { Name = name, FilePath = Path.Combine(directory, name + ".mimodel"),
            TextureVariants = variants };
    }

    private static IEnumerable<MiPart> ArmorParts(MiModel model) => Walk(model.Parts);

    private static IEnumerable<MiPart> Walk(IEnumerable<MiPart>? parts)
    {
        foreach (var part in parts ?? [])
        {
            yield return part;
            foreach (var child in Walk(part.Parts)) yield return child;
        }
    }

    private static void ArmorEditorModels(Fixture fixture)
    {
        foreach (string name in new[] { "armor", "armor_baby" })
        {
            var entry = ArmorEntry(name);
            int Variant(string label) => entry.TextureVariants.ToList().FindIndex(v => v.Name == label);
            var selections = new[] { new ArmorPieceSelection(Variant("Diamond")),
                new ArmorPieceSelection(Variant("Leather")), new ArmorPieceSelection(Variant("Gold")),
                new ArmorPieceSelection(-1) };
            byte[] original = File.ReadAllBytes(entry.FilePath);
            var model = ArmorModelBuilder.Build(entry, selections);
            var parts = ArmorParts(model).ToList();
            var shapes = parts.SelectMany(part => part.Shapes).ToList();
            Check(shapes.Count > 0 && !shapes.Any(shape => shape.Description.StartsWith("boots")),
                $"{name}: None omits boots");
            foreach (var (piece, material) in new[] { ("helmet", "diamond"), ("chestplate", "leather"), ("leggings", "gold") })
            {
                var matching = shapes.Where(shape => shape.Description.StartsWith(piece)).ToList();
                Check(matching.Count > 0 && matching.All(shape => Path.GetFileName(shape.Texture) == material + ".png"),
                    $"{name}: independently selected {piece} textures");
                Check(matching.All(shape => Path.IsPathFullyQualified(shape.Texture) && File.Exists(shape.Texture)),
                    $"{name}: textures resolve outside the original model directory");
            }
            if (name == "armor")
                Check(shapes.Where(shape => shape.Description == "leggings").All(shape =>
                    Path.GetDirectoryName(shape.Texture)!.EndsWith("humanoid_leggings")),
                    "Adult leggings retain their separate texture layer");
            var authored = MineImatorLoader.Instance.LoadModel(entry.FilePath);
            Check(parts.Select(part => part.Name).SequenceEqual(ArmorParts(authored).Select(part => part.Name)),
                $"{name}: original rig retained");
            for (int slot = 0; slot < 4; slot++)
            {
                var only = Enumerable.Repeat(new ArmorPieceSelection(-1), 4).ToArray();
                only[slot] = new ArmorPieceSelection(Variant("Netherite"));
                var one = ArmorModelBuilder.Build(entry, only);
                Check(ArmorParts(one).SelectMany(part => part.Shapes).All(shape =>
                    shape.Description.StartsWith(ArmorModelBuilder.Pieces[slot])), $"{name}: isolated piece {slot}");
            }
            int custom = Variant("Custom");
            string customTexture = shapes[0].Texture;
            selections[0] = new ArmorPieceSelection(custom, customTexture);
            selections[1] = new ArmorPieceSelection(custom, shapes.First(shape => shape.Description.StartsWith("chestplate")).Texture);
            var customModel = ArmorModelBuilder.Build(entry, selections);
            Check(ArmorParts(customModel).SelectMany(part => part.Shapes)
                .Where(shape => shape.Description.StartsWith("helmet")).All(shape => shape.Texture == customTexture),
                "Per-piece custom texture applied");
            selections[0] = new ArmorPieceSelection(custom);
            Reject(() => ArmorModelBuilder.Build(entry, selections), "Custom armor requires a file");
            Check(original.SequenceEqual(File.ReadAllBytes(entry.FilePath)), "Bundled armor is never modified");
            string saved = ArmorModelBuilder.Save(model, fixture.Root);
            Equal(saved, ArmorModelBuilder.Save(model, fixture.Root), "Identical outfits reuse a persistent model");
            var restored = MineImatorLoader.Instance.LoadModel(saved);
            Equal(shapes.Count, ArmorParts(restored).Sum(part => part.Shapes.Count), "Saved outfit retains omitted geometry");
        }
    }

    private static unsafe void ArmorEditorGraphics(SpawnMenu spawn, Viewport viewport, Fixture fixture,
        Func<PreviewRenderer, byte[]> readPixels)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(SpawnMenu);
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new System.Numerics.Vector2(1000, 800);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            foreach (string name in new[] { "armor", "armor_baby" })
            {
                var entry = ArmorEntry(name);
                type.GetField("_selectedCharTextureIndex", flags)!.SetValue(spawn, 0);
                type.GetMethod("OpenArmorEditor", flags)!.Invoke(spawn, [entry]);
                void Frame(System.Numerics.Vector2? mouse = null, bool down = false)
                {
                    io.AddMousePosEvent(mouse?.X ?? -100f, mouse?.Y ?? -100f);
                    io.AddMouseButtonEvent(0, down);
                    ImGui.NewFrame();
                    ImGui.Begin("Armor editor host");
                    ImGui.SetNextWindowPos(new System.Numerics.Vector2(100, 100), ImGuiCond.Always);
                    type.GetMethod("RenderArmorEditor", flags)!.Invoke(spawn, []);
                    ImGui.End();
                    ImGui.Render();
                }
                Frame();
                Frame();
                var renderer = (PreviewRenderer)type.GetField("_armorPreviewRenderer", flags)!.GetValue(spawn)!;
                renderer.AutoRotateSpeed = 0f;
                Frame();
                byte[] full = readPixels(renderer);
                var original = MineImatorLoader.Instance.CreateCharacterFromModel(
                    MineImatorLoader.Instance.LoadModel(entry.FilePath));
                try
                {
                    using var referenceRenderer = new PreviewRenderer(spawn.Gl!) { AutoRotateSpeed = 0f };
                    referenceRenderer.Render([], "original-armor", 0d, sceneRoot: original,
                        centerMeshes: true, isolatedLighting: true);
                    Check(full.SequenceEqual(readPixels(referenceRenderer)),
                        $"{name}: editor default must exactly match the original armor's rendered pixels");
                }
                finally
                {
                    foreach (var mesh in original.GetMeshInstancesRecursively()) mesh.Dispose();
                    foreach (var bone in original.GetAllDescendants().OfType<BoneSceneObject>())
                        bone.IndicatorMesh?.Dispose();
                }
                int pixels = 0;
                for (int i = 0; i < full.Length; i += 4)
                    if (Math.Abs(full[i] - 31) > 2 || Math.Abs(full[i + 1] - 31) > 2 ||
                        Math.Abs(full[i + 2] - 36) > 2) pixels++;
                Check(pixels > 100, $"{name}: real preview pixels ({pixels})");
                bool imageOnLeft = false;
                var draw = ImGui.GetDrawData();
                for (int i = 0; i < draw.CmdListsCount; i++)
                for (int j = 0; j < draw.CmdLists[i].CmdBuffer.Size; j++)
                {
                    var command = draw.CmdLists[i].CmdBuffer[j];
                    if (command.TexRef.TexID == (ulong)renderer.ColorTexture)
                        imageOnLeft |= command.ClipRect.Z < 500f;
                }
                Check(imageOnLeft, "The actual modal displays the rendered texture on the left");
                Check(viewport.SceneObjects.Count == 0, "Opening the editor never spawns preview objects");

                var choices = (ArmorPieceSelection[])type.GetField("_armorPieceSelections", flags)!.GetValue(spawn)!;
                var style = ImGui.GetStyle();
                float comboY = 100f + ImGui.GetFrameHeight() + style.WindowPadding.Y +
                    ImGui.GetTextLineHeightWithSpacing() + ImGui.GetFrameHeight() * 0.5f;
                float step = ImGui.GetTextLineHeightWithSpacing() + ImGui.GetFrameHeightWithSpacing() + style.ItemSpacing.Y;
                for (int slot = 0; slot < choices.Length; slot++)
                {
                    var combo = new System.Numerics.Vector2(430f, comboY + slot * step);
                    Frame(combo);
                    Frame(combo, true);
                    Frame(combo, true);
                    Frame(combo);
                    Frame(combo);
                    var none = combo + new System.Numerics.Vector2(0, ImGui.GetFrameHeight() + style.WindowPadding.Y);
                    Frame(none, true);
                    Frame(none);
                    Equal(-1, choices[slot].VariantIndex, $"The actual {ArmorModelBuilder.Pieces[slot]} dropdown can select None");
                }
                Frame();
                Check(!full.SequenceEqual(readPixels(renderer)), "Choosing None immediately changes preview pixels");
                var empty = (SceneObject)type.GetField("_armorPreviewRoot", flags)!.GetValue(spawn)!;
                Check(!empty.GetMeshInstancesRecursively().Any(), "Selecting None for all slots loads no armor geometry");
                choices[0] = new ArmorPieceSelection(entry.TextureVariants.ToList().FindIndex(v => v.Name == "Diamond"));
                choices[1] = new ArmorPieceSelection(entry.TextureVariants.ToList().FindIndex(v => v.Name == "Leather"));
                choices[2] = new ArmorPieceSelection(entry.TextureVariants.ToList().FindIndex(v => v.Name == "Gold"));
                choices[3] = new ArmorPieceSelection(-1);
                type.GetField("_armorPreviewDirty", flags)!.SetValue(spawn, true);
                Frame();
                Check(!full.SequenceEqual(readPixels(renderer)), "Variant/None changes update preview pixels");
                var model = (MiModel)type.GetField("_armorPreviewModel", flags)!.GetValue(spawn)!;
                string path = ArmorModelBuilder.Save(model, fixture.Root);
                var root = spawn.SpawnCustomModelFromPath(path, registerAsset: false)!;
                root.SpawnCategory = "Equipment";
                root.InheritPose = true;
                using (var loadedRenderer = new PreviewRenderer(spawn.Gl!) { AutoRotateSpeed = 0f })
                {
                    loadedRenderer.Render([], "spawned-armor", 0d, sceneRoot: root,
                        centerMeshes: true, isolatedLighting: true);
                    Check(readPixels(renderer).SequenceEqual(readPixels(loadedRenderer)),
                        $"{name}: spawned mixed armor must exactly match the editor preview's rendered pixels");
                }
                var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(root);
                viewport.SceneObjects.Remove(root);
                var restored = ProjectSceneSerializer.SpawnObjectFromEntry(snapshot, viewport, spawn)!;
                Check(restored.InheritPose && restored.SpawnCategory == "Equipment", "Outfit keeps equipment pose inheritance");
                Equal(root.GetMeshInstancesRecursively().Count(), restored.GetMeshInstancesRecursively().Count(),
                    "Save/reload preserves selected geometry");
                Check(restored.GetMeshInstancesRecursively().All(mesh => mesh.TextureId != 0),
                    "Reloaded outfit retains textures");
                string archive = Path.Combine(fixture.Root, name + ".nxObj");
                NxObjectArchive.Export(archive, root);
                var imported = NxObjectArchive.Import(archive, viewport, spawn);
                Equal(root.GetMeshInstancesRecursively().Count(), imported.GetMeshInstancesRecursively().Count(),
                    "Object archive retains mixed/omitted geometry");
                Check(imported.GetMeshInstancesRecursively().All(mesh => mesh.TextureId != 0),
                    "Object archive packages generated model and textures");
                foreach (var obj in new[] { root, restored, imported })
                {
                    foreach (var mesh in obj.GetMeshInstancesRecursively()) mesh.Dispose();
                    foreach (var bone in obj.GetAllDescendants().OfType<BoneSceneObject>())
                        bone.IndicatorMesh?.Dispose();
                }
                viewport.SceneObjects.Clear();
                type.GetField("_armorEditorVisible", flags)!.SetValue(spawn, false);
                Frame();
                type.GetMethod("DisposeArmorEditorPreview", flags)!.Invoke(spawn, []);
            }
        }
        finally
        {
            type.GetMethod("DisposeArmorEditorPreview", flags)!.Invoke(spawn, []);
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
        Console.WriteLine("PASS Armor editor adult/baby modal preview pixels, mixed pieces, omission and reload");
    }
}
