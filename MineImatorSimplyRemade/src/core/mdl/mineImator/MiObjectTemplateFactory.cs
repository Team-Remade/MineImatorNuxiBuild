using System.Globalization;
using System.Text.Json;
using GlmSharp;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.mdl.mineImator;

public static class MiObjectTemplateFactory
{
    public static SceneObject? Create(MineImatorLoader loader, GL gl, MiTemplate template,
        MiTimeline timeline, IReadOnlyDictionary<string, MiResource> resources, string directory)
    {
        string type = template.Type?.ToLowerInvariant() ?? "";
        SceneObject? obj;
        JsonElement? settings;
        if (type == "bodypart")
        {
            if (!string.IsNullOrEmpty(timeline.PartOf) &&
                !string.Equals(timeline.PartOf, "null", StringComparison.OrdinalIgnoreCase))
                return null;
            obj = CreateBodypart(loader, gl, template, timeline, resources, directory);
            settings = null;
        }
        else if (type == "block")
        {
            obj = CreateBlock(gl, template.Block);
            settings = template.Block;
        }
        else if (type is "cube" or "sphere" or "surface")
        {
            settings = template.Shape;
            int detail = Math.Clamp((int)Number(settings, "detail", 32), 4, 128);
            obj = new SceneObject
            {
                ObjectType = type == "surface" ? "Plane" : type == "cube" ? "Cube" : "Sphere",
                SpawnCategory = "Primitives",
                PivotOffset = new vec3(0f, 0.5f, 0f),
                PrimitiveCubeMapped = Bool(settings, "tex_mapped"),
                PrimitiveSphereSegments = detail,
                PrimitiveSphereRings = detail - 2,
                PrimitiveSphereSmooth = Bool(settings, "smooth", true),
                PrimitivePlaneFaceCamera = Bool(settings, "face_camera"),
                InvertFaces = Bool(settings, "invert")
            };
            obj.SetExplicitMaterialSettings();
            obj.MaterialSettings.TextureOffset = new vec2(Number(settings, "tex_hoffset", 0),
                Number(settings, "tex_voffset", 0));
            obj.MaterialSettings.TextureRepeat = new vec2(Number(settings, "tex_hrepeat", 1),
                Number(settings, "tex_vrepeat", 1));
            obj.MaterialSettings.TextureMirror = new bvec2(Bool(settings, "tex_hmirror"),
                Bool(settings, "tex_vmirror"));
            if (gl != null)
            {
                Mesh mesh = type switch
                {
                    "cube" => new CubeMesh(gl, obj.PrimitiveCubeMapped),
                    "sphere" => new SphereMesh(gl, 0.5f, detail, detail - 2, obj.PrimitiveSphereSmooth),
                    _ => new PlaneMesh(gl, 1f, 1f, PlaneOrientation.XY)
                };
                obj.AddMesh(mesh);
            }
        }
        else
            return null;

        if (obj == null) return null;
        obj.Name = !string.IsNullOrWhiteSpace(timeline.Name) ? timeline.Name :
            !string.IsNullOrWhiteSpace(template.Name) ? template.Name : obj.ObjectType;
        if (string.IsNullOrEmpty(obj.ObjectId)) obj.AssignObjectId();
        if (settings.HasValue)
        {
            string path = TexturePath(Text(settings, "tex"), resources, directory);
            obj.AlbedoTexturePath = path;
            if (gl != null && File.Exists(path))
            {
                uint texture = loader.LoadTextureFromFile(path);
                foreach (var mesh in obj.Visuals) mesh.TextureId = texture;
            }
        }
        obj.ApplyMaterialSettingsToMeshes();
        return obj;
    }

    private static SceneObject CreateBlock(GL gl, JsonElement? block)
    {
        string name = Text(block, "name");
        if (name.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase)) name = name[10..];
        var state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stateJson = Property(block, "state");
        if (stateJson is { ValueKind: JsonValueKind.Object })
            foreach (var property in stateJson.Value.EnumerateObject())
                state[property.Name] = property.Value.ToString();

        // Mine-imator's wood template predates flattened Minecraft block IDs.
        if (name == "wood" && state.Remove("variant", out string? wood)) name = wood + "_wood";
        var variant = SpawnMenu.PickBestVariantForProperties(BlockRegistry.GetVariants(name), state, null);
        var repeat = Property(block, "repeat");
        bool tiled = Bool(block, "repeat_enable");
        int Tile(int axis) => tiled && repeat is { ValueKind: JsonValueKind.Array } &&
            repeat.Value.GetArrayLength() > axis
                ? Math.Clamp((int)Scalar(repeat.Value[axis], 1), 1, SceneObject.MaxTilesPerAxis) : 1;
        var obj = new SceneObject
        {
            ObjectType = name,
            SpawnCategory = "Blocks",
            TextureType = "block",
            BlockVariant = variant?.VariantKey ?? string.Join(",", state.Select(p => $"{p.Key}={p.Value}")),
            TileX = Tile(0), TileY = Tile(1), TileZ = Tile(2)
        };
        obj.PivotOffset = new vec3(-obj.TileX * 0.5f, 0f, -obj.TileZ * 0.5f);
        if (gl != null && variant != null)
        {
            AddMeshes(variant, vec3.Zero);
            if (variant.TopHalf != null)
                AddMeshes(variant.TopHalf, new vec3(variant.PartOffsetX, variant.PartOffsetY, variant.PartOffsetZ));
        }
        return obj;

        void AddMeshes(BlockVariantEntry entry, vec3 offset)
        {
            var resolved = string.IsNullOrEmpty(entry.ModelPath) ? null : BlockRegistry.ResolveModel(entry.ModelPath);
            var meshes = !string.IsNullOrEmpty(entry.CemPath)
                ? CemLoader.Load(gl, entry.CemPath, MinecraftDataLoader.GetVersionRoot(), "")
                : resolved != null
                    ? MinecraftModelMesh.Build(gl, resolved, entry.RotationX, entry.RotationY,
                        blockName: name, tileX: obj.TileX, tileY: obj.TileY, tileZ: obj.TileZ)
                    : new List<Mesh>();
            // Registry meshes are centered; MI block custom pivots use positive block coordinates.
            var shift = new vec3(obj.TileX, obj.TileY, obj.TileZ) * 0.5f + offset;
            foreach (var mesh in meshes)
            {
                for (int i = 0; i < mesh.Vertices.Count; i++) mesh.Vertices[i] += shift;
                mesh.Upload();
                obj.AddMesh(mesh);
            }
        }
    }

    private static MiBoneSceneObject? CreateBodypart(MineImatorLoader loader, GL gl,
        MiTemplate template, MiTimeline timeline, IReadOnlyDictionary<string, MiResource> resources,
        string directory)
    {
        var definition = template.ModelDefinition;
        string modelRef = template.Model;
        if (string.IsNullOrEmpty(modelRef)) modelRef = Text(definition, "name");
        string modelPath;
        if (resources.TryGetValue(modelRef ?? "", out var resource))
            modelPath = Path.Combine(directory ?? "", resource.Filename);
        else if (string.Equals(modelRef, "human", StringComparison.OrdinalIgnoreCase))
        {
            string variant = Text(Property(definition, "state"), "variant");
            string character = string.Equals(variant, "alex", StringComparison.OrdinalIgnoreCase) ? "alex" : "steve";
            modelPath = Path.Combine(MinecraftDataLoader.GetVersionRoot(), "characters", "vanilla",
                character, character + ".mimodel");
        }
        else
            modelPath = Path.Combine(directory ?? "", modelRef ?? "");
        if (!File.Exists(modelPath)) return null;
        var model = loader.LoadModel(modelPath);
        if (model == null) return null;
        var characterObject = loader.CreateCharacterFromModel(model);
        string partName = Text(definition, "part_name");
        if (string.IsNullOrEmpty(partName)) partName = timeline.ModelPartName;
        var bone = characterObject?.BoneObjects.Values.OfType<MiBoneSceneObject>()
            .FirstOrDefault(b => string.Equals(b.BoneName, partName, StringComparison.OrdinalIgnoreCase));
        if (bone == null) return null;
        bone.Parent?.RemoveChild(bone);
        foreach (var child in bone.Children.ToArray()) bone.RemoveChild(child);
        bone.SetLocalPosition(vec3.Zero);
        bone.CommitBasePose();
        bone.SourceAssetPath = modelPath;
        bone.TextureOverridePath = TexturePath(template.ModelTex, resources, directory);
        if (gl != null && File.Exists(bone.TextureOverridePath))
            MineImatorLoader.ApplyTextureOverrideToScene(bone,
                loader.LoadTextureFromFile(bone.TextureOverridePath, makeSquare: true));

        // The headless loader cannot build meshes, but regeneration still needs the original shapes.
        if (gl == null)
        {
            vec3 modelScale = model.Scale is { Length: >= 3 }
                ? new vec3(model.Scale[0], model.Scale[1], model.Scale[2]) : vec3.Ones;
            RegisterShapes(model.Parts, modelScale);
        }
        return bone;

        bool RegisterShapes(List<MiPart> parts, vec3 scale)
        {
            foreach (var part in parts)
            {
                vec3 accumulated = scale * (part.Scale is { Length: >= 3 }
                    ? new vec3(part.Scale[0], part.Scale[1], part.Scale[2]) : vec3.Ones);
                if (string.Equals(part.Name, partName, StringComparison.OrdinalIgnoreCase))
                {
                    if (part.Shapes != null)
                        for (int i = 0; i < part.Shapes.Count; i++)
                            if (part.Shapes[i].Visible)
                                bone.RegisterShapeData(new BoneShapeData
                                {
                                    PartName = part.Name, ShapeIndex = i, Shape = part.Shapes[i], Model = model,
                                    AccumulatedScale = accumulated, ModelBendStyle = characterObject!.ModelBendStyle,
                                    TextureSize = part.TextureSize ?? model.TextureSize, DoubleSided = part.Backfaces,
                                    PartColorBlend = bone.ColorBlend, PartColorMix = bone.ColorMix,
                                    PartColorBlendAmount = bone.ColorBlendAmount, PartColorAlpha = bone.ColorAlpha,
                                    PartDepth = bone.Depth
                                });
                    return true;
                }
                if (part.Parts != null && RegisterShapes(part.Parts, accumulated)) return true;
            }
            return false;
        }
    }

    private static string TexturePath(string reference, IReadOnlyDictionary<string, MiResource> resources,
        string directory)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference is "null" or "default") return "";
        if (resources.TryGetValue(reference, out var resource)) reference = resource.Filename;
        return string.IsNullOrWhiteSpace(reference) ? "" : Path.GetFullPath(Path.Combine(directory ?? "", reference));
    }

    private static JsonElement? Property(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } && element.Value.TryGetProperty(name, out var value)
            ? value : null;

    private static string Text(JsonElement? element, string name) => Property(element, name)?.ToString() ?? "";

    private static float Scalar(JsonElement? value, float fallback) =>
        float.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float number) &&
        float.IsFinite(number) ? number : fallback;

    private static float Number(JsonElement? element, string name, float fallback) =>
        Scalar(Property(element, name), fallback);

    private static bool Bool(JsonElement? element, string name, bool fallback = false)
    {
        var value = Property(element, name);
        if (bool.TryParse(value?.ToString(), out bool result)) return result;
        return Scalar(value, fallback ? 1 : 0) != 0;
    }
}
