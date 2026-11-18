using System.Reflection;
using System.Text.Json;
using ImageMagick;
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
            foreach (var (piece, material) in new[] { ("helmet", "diamond"), ("leggings", "gold") })
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
            ArmorTrimPixels(entry, fixture);
            ArmorDyePixels(entry, fixture);
        }
    }

    private static void ArmorDyePixels(CharacterEntry entry, Fixture fixture)
    {
        int leather = entry.TextureVariants.ToList().FindIndex(v => v.Name == "Leather");
        var choices = new[]
        {
            new ArmorPieceSelection(leather),
            new ArmorPieceSelection(leather, LeatherColor: 0xFF0000),
            new ArmorPieceSelection(leather, LeatherColor: 0x00FF00),
            new ArmorPieceSelection(leather, LeatherColor: 0x000000)
        };
        var dyed = ArmorModelBuilder.Build(entry, choices);
        var authored = MineImatorLoader.Instance.LoadModel(entry.TextureVariants[leather].ModelPath);
        string Texture(MiModel model, int slot) => ArmorParts(model).SelectMany(p => p.Shapes)
            .First(s => s.Description.StartsWith(ArmorModelBuilder.Pieces[slot], StringComparison.Ordinal)).Texture;
        byte[] Pixels(string path)
        {
            using var image = new MagickImage(path) { Depth = 8 };
            return image.ToByteArray(MagickFormat.Rgba);
        }
        for (int slot = 0; slot < 4; slot++)
        {
            string source = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(entry.FilePath)!, Texture(authored, slot)));
            byte[] original = Pixels(source), result = Pixels(Texture(dyed, slot));
            byte[] overlay = Pixels(Path.Combine(Path.GetDirectoryName(source)!, "leather_overlay.png"));
            int dyedPixels = 0, overlayPixels = 0;
            uint color = slot == 0 ? 0xA06540u : choices[slot].LeatherColor;
            for (int p = 0; p < original.Length; p += 4)
            {
                if (overlay[p + 3] == 255)
                {
                    Check(overlay.AsSpan(p, 4).SequenceEqual(result.AsSpan(p, 4)), "Leather details remain undyed");
                    overlayPixels++;
                }
                else if (overlay[p + 3] == 0)
                {
                    for (int c = 0; c < 3; c++)
                        Equal((byte)((original[p + c] * ((color >> (16 - c * 8)) & 255) + 127) / 255),
                            result[p + c], $"{entry.Name}: exact per-piece leather RGB, including default #A06540");
                    Equal(original[p + 3], result[p + 3], "Leather dye preserves transparency");
                    if (original[p + 3] != 0) dyedPixels++;
                }
            }
            Check(dyedPixels > 0 && overlayPixels > 0, "Both dyed leather and undyed overlay pixels are exercised");
        }
        choices[0] = choices[0] with { TrimPattern = "sentry", TrimMaterial = "redstone" };
        var trimmed = ArmorModelBuilder.Build(entry, choices);
        byte[] tinted = Pixels(Texture(dyed, 0)), withTrim = Pixels(Texture(trimmed, 0));
        string textures = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(entry.FilePath)!, "..", "..", "..", "textures"));
        byte[] pattern = Pixels(Path.Combine(textures, "trims", "entity", "humanoid", "sentry.png"));
        byte[] palette = Pixels(Path.Combine(textures, "palettes", "trim_base.png"));
        byte[] redstone = Pixels(Path.Combine(textures, "palettes", "trim", "redstone.png"));
        for (int p = 0; p < tinted.Length; p += 4)
        {
            if (p >= pattern.Length || pattern[p + 3] == 0)
                Check(tinted.AsSpan(p, 4).SequenceEqual(withTrim.AsSpan(p, 4)), "Trims preserve the selected leather color");
            else
            {
                int index = Enumerable.Range(0, palette.Length / 4).First(i =>
                    palette.AsSpan(i * 4, 3).SequenceEqual(pattern.AsSpan(p, 3)));
                Check(redstone.AsSpan(index * 4, 3).SequenceEqual(withTrim.AsSpan(p, 3)),
                    "Leather dye never tints trim colors");
            }
        }
        string saved = ArmorModelBuilder.Save(trimmed, fixture.Root);
        var restored = MineImatorLoader.Instance.LoadModel(saved);
        Check(withTrim.SequenceEqual(Pixels(Texture(restored, 0))), "Saved leather keeps dye, overlay and trim pixels");
        choices[0] = choices[0] with { LeatherColor = 0x1000000 };
        Reject(() => ArmorModelBuilder.Build(entry, choices), "Out-of-range leather RGB is rejected");
        choices[0] = new ArmorPieceSelection(entry.TextureVariants.ToList().FindIndex(v => v.Name == "Diamond"),
            LeatherColor: 0);
        Check(Path.GetFileName(Texture(ArmorModelBuilder.Build(entry, choices), 0)) == "diamond.png",
            "Switching away from leather removes dyeing");
    }

    private static void ArmorTrimPixels(CharacterEntry entry, Fixture fixture)
    {
        int diamond = entry.TextureVariants.ToList().FindIndex(v => v.Name == "Diamond");
        var choices = Enumerable.Repeat(new ArmorPieceSelection(diamond), 4).ToArray();
        var plain = ArmorModelBuilder.Build(entry, choices);
        choices[0] = choices[0] with { TrimPattern = "sentry", TrimMaterial = "redstone" };
        choices[1] = choices[1] with { TrimPattern = "coast", TrimMaterial = "diamond" };
        choices[2] = choices[2] with { TrimPattern = "dune", TrimMaterial = "gold" };
        choices[3] = choices[3] with { TrimPattern = "wild", TrimMaterial = "emerald" };
        var trimmed = ArmorModelBuilder.Build(entry, choices);
        string textures = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(entry.FilePath)!, "..", "..", "..", "textures"));
        byte[] Pixels(string path)
        {
            using var image = new MagickImage(path) { Depth = 8 };
            return image.ToByteArray(MagickFormat.Rgba);
        }
        for (int slot = 0; slot < 4; slot++)
        {
            string piece = ArmorModelBuilder.Pieces[slot];
            string Base(MiModel model) => ArmorParts(model).SelectMany(p => p.Shapes)
                .First(s => s.Description == piece || s.Description == piece + "_baby").Texture;
            byte[] original = Pixels(Base(plain)), result = Pixels(Base(trimmed));
            string layer = slot == 2 ? "humanoid_leggings" : "humanoid";
            byte[] pattern = Pixels(Path.Combine(textures, "trims", "entity", layer, choices[slot].TrimPattern + ".png"));
            byte[] palette = Pixels(Path.Combine(textures, "palettes", "trim_base.png"));
            string material = choices[slot].TrimMaterial + (slot == 1 ? "_darker" : "");
            byte[] colors = Pixels(Path.Combine(textures, "palettes", "trim", material + ".png"));
            int changed = 0;
            for (int i = 0; i < original.Length; i += 4)
            {
                if (i >= pattern.Length || pattern[i + 3] == 0)
                {
                    Check(original.AsSpan(i, 4).SequenceEqual(result.AsSpan(i, 4)), "Trim preserves untouched texels and baby atlas rows");
                    continue;
                }
                int index = Enumerable.Range(0, palette.Length / 4).First(p =>
                    palette.AsSpan(p * 4, 3).SequenceEqual(pattern.AsSpan(i, 3)));
                Check(colors.AsSpan(index * 4, 3).SequenceEqual(result.AsSpan(i, 3)),
                    $"{entry.Name}: {piece} exact palette color, including same-material darkening");
                Equal((byte)255, result[i + 3], "Trim covers transparent base texels");
                changed++;
            }
            Check(changed > 0 && !original.SequenceEqual(result), "Each piece receives its selected trim");
        }
        string saved = ArmorModelBuilder.Save(trimmed, fixture.Root);
        var restored = MineImatorLoader.Instance.LoadModel(saved);
        Check(ArmorParts(restored).SelectMany(p => p.Shapes).All(s => File.Exists(s.Texture)),
            "Saved trimmed armor retains persistent generated textures");
        // PNG creation timestamps must not affect texture/model cache identity.
        Thread.Sleep(1100);
        Equal(saved, ArmorModelBuilder.Save(ArmorModelBuilder.Build(entry, choices), fixture.Root),
            "Identical trims reuse persistent content");
        choices[0] = choices[0] with { TrimPattern = "../invalid" };
        Reject(() => ArmorModelBuilder.Build(entry, choices), "Unavailable trim patterns are rejected");
        choices[0] = choices[0] with { TrimPattern = "sentry", TrimMaterial = "invalid" };
        Reject(() => ArmorModelBuilder.Build(entry, choices), "Unavailable trim materials are rejected");
        choices[0] = choices[0] with { TrimMaterial = "none" };
        var disabled = ArmorModelBuilder.Build(entry, choices);
        Equal(ArmorParts(plain).SelectMany(p => p.Shapes).First(s => s.Description.StartsWith("helmet")).Texture,
            ArmorParts(disabled).SelectMany(p => p.Shapes).First(s => s.Description.StartsWith("helmet")).Texture,
            "None material disables the trim");
    }

    private static void MiImportArmor(Fixture fixture)
    {
        foreach (string name in new[] { "armor", "armor_baby" })
        {
            var entry = ArmorEntry(name);
            string json = """
                {"templates":[{"id":"outfit","type":"special_block",
                  "model":{"name":"armor","file":MODEL,"state":{
                    "helmet":"leather","chestplate":"gold","leggings":"leather","boots":"none"}},
                  "armor":{"helmet_trim_pattern":"sentry","helmet_trim_material":"diamond",
                    "chestplate_trim_pattern":"coast","chestplate_trim_material":"redstone",
                    "leggings_dye":"#1A73CC","leggings_trim_pattern":"dune","leggings_trim_material":"gold"}}],
                 "timelines":[{"id":"armor","temp":"outfit","type":"special_block","keyframes":{}}]}
                """.Replace("MODEL", JsonSerializer.Serialize(entry.FilePath));
            var loader = new MineImatorLoader();
            var document = loader.LoadMiObject(fixture.File(name + ".miobject", json));
            var warnings = new List<string>();
            var scene = loader.CreateSceneFromMiObject(document, warning: warnings.Add);
            var character = scene.Children.OfType<CharacterSceneObject>().Single();
            var model = character.SourceMineImatorModel!;
            var expected = ArmorModelBuilder.Build(entry,
            [
                new(entry.TextureVariants.ToList().FindIndex(v => v.Name == "Leather"), TrimPattern: "sentry", TrimMaterial: "diamond"),
                new(entry.TextureVariants.ToList().FindIndex(v => v.Name == "Gold"), TrimPattern: "coast", TrimMaterial: "redstone"),
                new(entry.TextureVariants.ToList().FindIndex(v => v.Name == "Leather"), TrimPattern: "dune", TrimMaterial: "gold",
                    LeatherColor: 0x1A73CC),
                new(-1)
            ]);
            Equal(JsonSerializer.Serialize(expected, AppJsonContext.Default.MiModel),
                JsonSerializer.Serialize(model, AppJsonContext.Default.MiModel), "MI import uses the editor's exact piece/trim mapping");
            Check(File.Exists(model.FullPath), "Imported generated armor has a persistent model source");
            Check(!warnings.Any(w => w.Contains("armor") || w.Contains("builtin model state")), "Mapped armor settings do not report unsupported warnings");
            var invalid = MiDocument(json.Replace("#1A73CC", "invalid", StringComparison.Ordinal));
            invalid.DirectoryPath = document.DirectoryPath;
            Reject(() => loader.CreateSceneFromMiObject(invalid), "Invalid imported leather dye is surfaced");
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
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
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
                void Press(ImGuiKey key)
                {
                    io.AddKeyEvent(key, true);
                    Frame();
                    io.AddKeyEvent(key, false);
                    Frame();
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
                float step = ImGui.GetTextLineHeightWithSpacing() + ImGui.GetFrameHeightWithSpacing() * 3 + style.ItemSpacing.Y;
                void SelectFirstTrim(int row)
                {
                    var combo = new System.Numerics.Vector2(430f, comboY + row * ImGui.GetFrameHeightWithSpacing());
                    Frame(combo);
                    Frame(combo, true);
                    Frame(combo, true);
                    Frame(combo);
                    Frame(combo);
                    foreach (var key in new[] { ImGuiKey.Home, ImGuiKey.DownArrow, ImGuiKey.Enter })
                        Press(key);
                    Frame();
                }
                SelectFirstTrim(1);
                Equal("bolt", choices[0].TrimPattern, "Actual trim pattern dropdown changes the helmet independently");
                Check(!full.SequenceEqual(readPixels(renderer)), "Selecting a trim changes real preview pixels");
                byte[] quartzTrim = readPixels(renderer);
                SelectFirstTrim(2);
                Equal("amethyst", choices[0].TrimMaterial, "Actual trim material dropdown selects a palette");
                Check(!quartzTrim.SequenceEqual(readPixels(renderer)), "Selecting a trim material changes real preview colors");
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
                var helmetCombo = new System.Numerics.Vector2(430f, comboY);
                Frame(helmetCombo);
                Frame(helmetCombo, true);
                Frame(helmetCombo, true);
                Frame(helmetCombo);
                Frame(helmetCombo);
                Press(ImGuiKey.Home);
                int leather = entry.TextureVariants.ToList().FindIndex(v => v.Name == "Leather");
                for (int i = 0; i <= leather; i++) Press(ImGuiKey.DownArrow);
                Press(ImGuiKey.Enter);
                Frame();
                Equal(leather, choices[0].VariantIndex, "Actual armor dropdown can select leather");
                Equal(0xA06540u, choices[0].LeatherColor, "New leather selection starts with #A06540");
                byte[] defaultLeather = readPixels(renderer);
                var colorInput = new System.Numerics.Vector2(450f, comboY + ImGui.GetFrameHeightWithSpacing());
                Frame(colorInput);
                Frame(colorInput, true);
                Frame(colorInput);
                io.AddKeyEvent(ImGuiKey.ModCtrl, true);
                Press(ImGuiKey.A);
                io.AddKeyEvent(ImGuiKey.ModCtrl, false);
                io.AddInputCharactersUTF8("2244CC");
                Frame();
                Press(ImGuiKey.Enter);
                Frame();
                Equal(0x2244CCu, choices[0].LeatherColor, "Actual leather color control accepts a chosen hex color");
                Check(!defaultLeather.SequenceEqual(readPixels(renderer)), "Leather color edits immediately change rendered pixels");
                choices[0] = new ArmorPieceSelection(entry.TextureVariants.ToList().FindIndex(v => v.Name == "Diamond"),
                    TrimPattern: "sentry", TrimMaterial: "diamond");
                choices[1] = new ArmorPieceSelection(entry.TextureVariants.ToList().FindIndex(v => v.Name == "Leather"),
                    TrimPattern: "coast", TrimMaterial: "redstone", LeatherColor: 0x2244CC);
                choices[2] = new ArmorPieceSelection(entry.TextureVariants.ToList().FindIndex(v => v.Name == "Gold"),
                    TrimPattern: "dune", TrimMaterial: "gold");
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
                using (var importedRenderer = new PreviewRenderer(spawn.Gl!) { AutoRotateSpeed = 0f })
                {
                    importedRenderer.Render([], "imported-trimmed-armor", 0d, sceneRoot: imported,
                        centerMeshes: true, isolatedLighting: true);
                    Check(readPixels(renderer).SequenceEqual(readPixels(importedRenderer)),
                        $"{name}: archived trimmed armor retains exact preview pixels");
                }
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
