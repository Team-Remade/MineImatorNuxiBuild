using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;
using StbImageSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MineImatorSimplyRemade.core.mdl.mineImator;

/// <summary>
/// Handles loading and parsing Mine Imator model files (.mimodel / .miobject).
/// Ported from the Godot simply-remade-nuxi project.
///
/// Key adaptations from Godot:
///  - Godot Vector3/Transform3D replaced with GlmSharp vec3/mat4
///  - Godot ArrayMesh/MeshInstance3D replaced with OpenGL Mesh
///  - Godot ImageTexture replaced with GL uint texture handles (loaded via StbImageSharp)
///  - Godot Skeleton3D removed; bones are plain SceneObjects with transforms
///  - No Godot node scene tree; hierarchy uses SceneObject.AddChild
/// </summary>
public class MineImatorLoader
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    public static MineImatorLoader Instance { get; } = new();

    // ── Project-level bend style (set by the app at startup or via settings UI) ──

    /// <summary>
    /// Global project bend style used when a model's own style is ProjectDefault.
    /// Defaults to Realistic.
    /// </summary>
    public static BendStyle ProjectBendStyle = BendStyle.Realistic;

    // ── State ─────────────────────────────────────────────────────────────────

    private GL _gl;

    /// <summary>Must be called once before loading any models.</summary>
    public void Initialize(GL gl) => _gl = gl;

    private CharacterSceneObject _currentCharacter;

    private readonly Dictionary<string, MiModel> _modelCache = new();
    private readonly Dictionary<string, MiObject> _miObjectCache = new();

    // ── Texture cache (path → GL texture handle) ──────────────────────────────

    private readonly Dictionary<string, uint> _textureCache = new();

    // ═════════════════════════════════════════════════════════════════════════
    //  Public API
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Loads a .miobject file.</summary>
    public MiObject LoadMiObject(string objectPath)
    {
        if (_miObjectCache.TryGetValue(objectPath, out var cached)) return cached;

        try
        {
            if (!File.Exists(objectPath))
            {
                Console.Error.WriteLine($"Object file not found: {objectPath}");
                return null;
            }

            var miObject = JsonSerializer.Deserialize(File.ReadAllText(objectPath),
                AppJsonContext.Default.MiObject);

            if (miObject == null) return null;

            miObject.DirectoryPath = Path.GetDirectoryName(objectPath);
            miObject.FullPath = objectPath;
            _miObjectCache[objectPath] = miObject;
            return miObject;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error loading '{objectPath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Creates a SceneObject hierarchy from a loaded MiObject.</summary>
    public SceneObject CreateSceneFromMiObject(MiObject miObject,
        Func<MiTemplate, MiTimeline, IReadOnlyDictionary<string, MiResource>, string, SceneObject?>? itemFactory = null)
    {
        if (miObject == null) return null;

        var sceneRoot = new SceneObject
        {
            ObjectType = "MineImatorObject",
            Name = "MiObject_Scene",
            PivotOffset = vec3.Zero
        };

        var templateDict = new Dictionary<string, MiTemplate>();
        if (miObject.Templates != null)
            foreach (var t in miObject.Templates)
                if (!string.IsNullOrEmpty(t.Id))
                    templateDict[t.Id] = t;

        var resourceDict = new Dictionary<string, string>();
        var resourceInfoById = new Dictionary<string, MiResource>();
        if (miObject.Resources != null)
            foreach (var r in miObject.Resources.Where(r =>
                         !string.IsNullOrEmpty(r.Id) && !string.IsNullOrEmpty(r.Filename)))
            {
                resourceDict[r.Id] = r.Filename;
                resourceInfoById[r.Id] = r;
            }

        var sceneObjectsByTimelineId = new Dictionary<string, SceneObject>();

        if (miObject.Timelines != null)
        {
            foreach (var timeline in miObject.Timelines)
            {
                if (timeline.Type == "bodypart") continue;

                MiTemplate template = null;
                if (!string.IsNullOrEmpty(timeline.Temp))
                    templateDict.TryGetValue(timeline.Temp, out template);

                SceneObject itemObject = null;

                if (template != null && !string.IsNullOrEmpty(template.Model))
                {
                    string modelPath = ResolveTemplateModelPath(template, resourceDict, miObject);
                    if (!string.IsNullOrWhiteSpace(modelPath) && File.Exists(modelPath))
                    {
                        var miModel = LoadModel(modelPath);
                        if (miModel != null)
                        {
                            var character = CreateCharacterFromModel(miModel);
                            if (character != null)
                            {
                                character.Name = !string.IsNullOrWhiteSpace(timeline.ModelPartName)
                                    ? timeline.ModelPartName
                                    : !string.IsNullOrWhiteSpace(timeline.Name)
                                        ? timeline.Name
                                        : "Model";
                                character.PivotOffset = vec3.Zero;
                                uint modelBaseTexture = miModel.GetTexture("texture");

                                // Mine-imator templates can override a model's base skin via
                                // model_tex (direct filename or resource id).
                                uint templateTexture = ResolveTemplateTexture(template, resourceDict,
                                    miObject.DirectoryPath);
                                if (templateTexture != 0)
                                    ApplyTextureOverrideToScene(character, templateTexture, modelBaseTexture);

                                itemObject = character;
                            }
                            else
                            {
                                Console.Error.WriteLine(
                                    $"MIObject model timeline '{timeline.Id}' failed to build character from model '{modelPath}'.");
                            }
                        }
                        else
                        {
                            Console.Error.WriteLine(
                                $"MIObject model timeline '{timeline.Id}' failed to load model '{modelPath}'.");
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine(
                            $"MIObject model timeline '{timeline.Id}' model path could not be resolved from template '{template.Id}' model ref '{template.Model}'.");
                    }
                }

                if (itemObject == null &&
                    template != null &&
                    string.Equals(template.Type, "item", StringComparison.OrdinalIgnoreCase))
                {
                    itemObject = itemFactory?.Invoke(template, timeline, resourceInfoById, miObject.DirectoryPath)
                                 ?? CreateSceneObjectFromItemTemplate(template, timeline, resourceInfoById, miObject.DirectoryPath);
                }

                itemObject ??= new SceneObject
                {
                    Name = !string.IsNullOrWhiteSpace(timeline.ModelPartName)
                        ? timeline.ModelPartName
                        : !string.IsNullOrWhiteSpace(timeline.Name)
                            ? timeline.Name
                            : "Unknown",
                    ObjectType = "Placeholder",
                    PivotOffset = vec3.Zero
                };

                ApplyTimelineTransform(itemObject, timeline);
                ApplyTimelineSettings(itemObject, timeline, includeHierarchySettings: true);

                if (!string.IsNullOrEmpty(timeline.Id))
                    sceneObjectsByTimelineId[timeline.Id] = itemObject;
            }
        }

            // Apply bodypart timelines onto imported model bones.
            ApplyBodypartTimelines(miObject, sceneObjectsByTimelineId);

        // Wire parent-child relationships
        if (miObject.Timelines != null)
        {
            foreach (var timeline in miObject.Timelines)
            {
                if (!sceneObjectsByTimelineId.TryGetValue(timeline.Id, out var itemObject)) continue;

                SceneObject parentObject = null;
                if (!string.IsNullOrEmpty(timeline.Parent))
                    sceneObjectsByTimelineId.TryGetValue(timeline.Parent, out parentObject);

                if (parentObject != null)
                    parentObject.AddChild(itemObject);
                else
                    sceneRoot.AddChild(itemObject);
            }
        }

        return sceneRoot;
    }

    /// <summary>Loads a .mimodel file.</summary>
    public MiModel LoadModel(string modelPath)
    {
        if (_modelCache.TryGetValue(modelPath, out var cached)) return cached;

        try
        {
            if (!File.Exists(modelPath))
            {
                Console.Error.WriteLine($"Model file not found: {modelPath}");
                return null;
            }

            string json = File.ReadAllText(modelPath);
            using var doc = JsonDocument.Parse(json);
            if (!ValidateModelRoot(doc.RootElement, modelPath))
                return null;

            var model = JsonSerializer.Deserialize(json, AppJsonContext.Default.MiModel);

            if (model == null) return null;

            model.DirectoryPath = Path.GetDirectoryName(modelPath);
            model.FullPath = modelPath;
            NormalizeTextureSizeSquare(model.TextureSize);

            if (model.Parts != null)
                NormalizePartAndShapeTextureSizes(model.Parts, model.TextureSize);

            _modelCache[modelPath] = model;
            return model;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error loading model '{modelPath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a CharacterSceneObject with bones and meshes from a MiModel.
    /// </summary>
    public CharacterSceneObject CreateCharacterFromModel(MiModel model)
    {
        if (model?.Parts == null || model.Parts.Count == 0) return null;

        LoadModelTextures(model);

        var boneDataList = new List<(MiPart part, int boneIdx, int parentIdx, vec3 accumulatedParentScale)>();
        vec3 modelScale = vec3.Ones;
        if (model.Scale is { Length: >= 3 })
            modelScale = new vec3(model.Scale[0], model.Scale[1], model.Scale[2]);
        FlattenPartsForBones(model.Parts, -1, modelScale, boneDataList);

        var character = new CharacterSceneObject();
        character.Name = model.Name;
        character.ObjectType = "MineImator";
        character.AssignObjectId();

        _currentCharacter = character;

        // First pass: create all MiBoneSceneObjects
        CreateBoneSceneObjects(character, boneDataList);

        // Second pass: create meshes per bone
        foreach (var (part, boneIdx, parentIdx, accumulatedParentScale) in boneDataList)
        {
            if (!character.BoneObjects.TryGetValue(GetBoneLookupKey(boneIdx), out var boneObject))
                continue;

            vec3 partScale = vec3.Ones;
            if (part.Scale != null && part.Scale.Length >= 3)
                partScale = new vec3(part.Scale[0], part.Scale[1], part.Scale[2]);

            vec3 accumulatedScale = accumulatedParentScale * partScale;

            BendParams? bendParams = BendHelper.ParseBend(part.Bend,
                [accumulatedScale.x, accumulatedScale.y, accumulatedScale.z],
                _currentCharacter.ModelBendStyle);

            // Cast to MiBoneSceneObject to access Mine Imator–specific members
            var miBone = boneObject as MiBoneSceneObject;

            float lockBend = part.LockBend ?? 1f;
            miBone?.SetBendParameters(bendParams, lockBend);

            // Mine-imator applies inherit_bend to the default pose immediately.
            // Previously it was only applied after the parent was edited and the
            // meshes regenerated, so inherited bends loaded in the wrong pose.
            BendParams? effectiveBendParams = miBone?.GetEffectiveBendParameters() ?? bendParams;

            if (part.Shapes is { Count: > 0 })
            {
                int shapeIndex = 0;
                foreach (var shape in part.Shapes)
                {
                    if (!shape.Visible)
                    {
                        shapeIndex++;
                        continue;
                    }

                    uint shapeTexture = GetShapeTexture(shape, part, model);
                    int[]? textureSize = ResolveTextureSize(part, model);

                    // Shape material values override the containing part. This
                    // is used heavily by facial rigs (for example PikanModel's
                    // R_Eye plane is 0.01 alpha while the R_Eye part is opaque).
                    float? colorAlpha = shape.ColorAlpha ?? miBone?.ColorAlpha;
                    vec3? colorBlend = ParseMiColor(shape.ColorBlend) ?? miBone?.ColorBlend;
                    float depth = miBone?.Depth ?? 0f;

                    var mesh = CreateShapeMesh(part.Name, shapeIndex, shape, model, shapeTexture,
                        accumulatedScale, effectiveBendParams, _currentCharacter.ModelBendStyle,
                        colorBlend, colorAlpha, depth, textureSize: textureSize);

                    if (mesh != null)
                    {
                        if (miBone != null) ApplyMaterialSettings(mesh, miBone, shapeTexture);
                        if (colorBlend.HasValue) mesh.BlendColor = new vec4(colorBlend.Value, 1f);
                        if (colorAlpha.HasValue) mesh.Alpha = colorAlpha.Value;
                        mesh.DoubleSided = part.Backfaces;
                        boneObject.AddMesh(mesh);
                        miBone?.RegisterShapeData(new BoneShapeData
                        {
                            PartName = part.Name,
                            ShapeIndex = shapeIndex,
                            Shape = shape,
                            Model = model,
                            TextureId = shapeTexture,
                            AccumulatedScale = accumulatedScale,
                            ModelBendStyle = _currentCharacter.ModelBendStyle,
                            PartColorBlend = colorBlend,
                            PartColorAlpha = colorAlpha,
                            PartDepth = depth,
                            TextureSize = textureSize
                        });
                    }

                    shapeIndex++;
                }
            }
        }

        return character;
    }

    /// <summary>
    /// Public wrapper for CreateShapeMesh used by BoneSceneObject.RegenerateMeshes.
    /// </summary>
    public Mesh CreateShapeMeshPublic(string partName, int shapeIndex, MiShape shape, MiModel model,
        uint textureId, vec3 accumulatedParentScale, BendParams? bendParams = null,
        BendStyle bendStyle = BendStyle.ProjectDefault, vec3? partColorBlend = null,
        float? partColorAlpha = null,
        float depth = 0f, int[]? textureSize = null)
        => CreateShapeMesh(partName, shapeIndex, shape, model, textureId, accumulatedParentScale,
            bendParams, bendStyle, partColorBlend, partColorAlpha, depth, textureSize);

    private static int[]? ResolveTextureSize(MiPart? part, MiModel model)
    {
        if (part?.TextureSize is { Length: >= 2 })
            return part.TextureSize;
        if (model.TextureSize is { Length: >= 2 })
            return model.TextureSize;
        return null;
    }

    /// <summary>
    /// Converts Mine Imator part Euler angles (Y-X-Z composition, degrees) to the engine's
    /// intrinsic X→Y→Z (Rz*Ry*Rx) Euler angles (radians).
    ///
    /// GameMaker stores matrices for row-vector multiplication. Transposing its
    /// matrix_build result into the engine's column-vector convention gives:
    ///                           R_mi = Ry(rot[1]) * Rx(rot[0]) * Rz(rot[2])
    /// Engine convention:        R_en = Rz(z) * Ry(y) * Rx(x)
    ///
    /// The reference project (Godot) negates rot[2] AND uses Basis(Forward, rotZ)
    /// where Forward = (0,0,-1), so the two negations cancel — the net rotation
    /// around +Z equals the original Mine Imator Z value.  Our engine uses
    /// mat4.RotateZ (+Z) directly, so no Z negation is needed.
    /// </summary>
    private static vec3 ConvertMiRotation(float[] rotDeg)
    {
        mat4 m = ConvertMiRotationMatrix(rotDeg);

        // Decompose into Rz(z') * Ry(y') * Rx(x') (engine convention)
        // For R = Rz(z') * Ry(y') * Rx(x'):
        //   m02 = -sin(y')
        //   m12 =  cos(y')*sin(x')
        //   m22 =  cos(y')*cos(x')
        //   m01 =  cos(y')*sin(z')
        //   m00 =  cos(y')*cos(z')
        const float eps = 1e-6f;
        float yy = MathF.Asin(-Math.Clamp(m.m02, -1f, 1f));
        float xx, zz;

        if (MathF.Abs(m.m02) < 1f - eps)
        {
            xx = MathF.Atan2(m.m12, m.m22);
            zz = MathF.Atan2(m.m01, m.m00);
        }
        else
        {
            xx = MathF.Atan2(-m.m21, m.m11);
            zz = 0f;
        }

        return new vec3(xx, yy, zz);
    }

    private static mat4 ConvertMiRotationMatrix(float[] rotDeg)
    {
        float x = BendHelper.DegToRad(rotDeg[0]);
        float y = BendHelper.DegToRad(rotDeg[1]);
        float z = BendHelper.DegToRad(rotDeg[2]);
        return mat4.RotateY(y) * mat4.RotateX(x) * mat4.RotateZ(z);
    }

    /// <summary>
    /// Converts a shape rotation. Modelbench uses the same GameMaker
    /// matrix_build Y-X-Z operation order for generated shape vertices and part
    /// transforms, so both must pass through the same conversion.
    /// </summary>
    private static vec3 ConvertMiShapeRotation(float[] rotDeg)
    {
        return ConvertMiRotation(rotDeg);
    }

    private static bool ValidateModelRoot(JsonElement root, string modelPath)
    {
        bool HasString(string name) =>
            root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String;

        bool HasArray(string name) =>
            root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array;

        if (!HasString("name"))
        {
            Console.Error.WriteLine($"Missing parameter 'name' in {modelPath}");
            return false;
        }

        if (!HasString("texture"))
        {
            Console.Error.WriteLine($"Missing parameter 'texture' in {modelPath}");
            return false;
        }

        if (!HasArray("texture_size"))
        {
            Console.Error.WriteLine($"Missing array 'texture_size' in {modelPath}");
            return false;
        }

        if (!HasArray("parts"))
        {
            Console.Error.WriteLine($"Missing array 'parts' in {modelPath}");
            return false;
        }

        return true;
    }

    private static void NormalizeTextureSizeSquare(int[]? textureSize)
    {
        if (textureSize == null || textureSize.Length < 2) return;
        int s = Math.Max(textureSize[0], textureSize[1]);
        textureSize[0] = s;
        textureSize[1] = s;
    }

    private static void NormalizePartAndShapeTextureSizes(List<MiPart> parts, int[]? fallbackTextureSize)
    {
        foreach (var part in parts)
        {
            if (part.TextureSize is { Length: >= 2 })
                NormalizeTextureSizeSquare(part.TextureSize);

            if (part.Shapes != null)
            {
                foreach (var shape in part.Shapes)
                {
                    if (shape.TextureSize is { Length: >= 2 })
                        NormalizeTextureSizeSquare(shape.TextureSize);
                    else if (part.TextureSize is { Length: >= 2 })
                        shape.TextureSize = new[] { part.TextureSize[0], part.TextureSize[1] };
                    else if (fallbackTextureSize is { Length: >= 2 })
                        shape.TextureSize = new[] { fallbackTextureSize[0], fallbackTextureSize[1] };
                }
            }

            if (part.Parts is { Count: > 0 })
                NormalizePartAndShapeTextureSizes(part.Parts, part.TextureSize ?? fallbackTextureSize);
        }
    }

    public void ClearCache()
    {
        _modelCache.Clear();
        _miObjectCache.Clear();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Private helpers
    // ═════════════════════════════════════════════════════════════════════════

    private void ApplyTimelineTransform(SceneObject obj, MiTimeline timeline)
    {
        if (timeline.Position is { Length: >= 3 })
            obj.SetLocalPosition(new vec3(timeline.Position[0] / 16f, timeline.Position[1] / 16f,
                timeline.Position[2] / 16f));

        if (timeline.Rotation is { Length: >= 3 })
            obj.SetLocalRotation(ConvertMiRotation(timeline.Rotation));

        if (timeline.Scale is { Length: >= 3 })
            obj.SetLocalScale(new vec3(timeline.Scale[0], timeline.Scale[1], timeline.Scale[2]));

        MiKeyframe? firstKeyframe = GetFirstTimelineKeyframe(timeline);
        if (firstKeyframe != null)
        {
            float[]? pos = firstKeyframe.GetPosition();

            if (pos != null)
                obj.SetLocalPosition(new vec3(pos[0] / 16f, pos[1] / 16f, pos[2] / 16f));

            float[]? rot = firstKeyframe.GetRotation();
            if (rot is { Length: >= 3 })
                obj.SetLocalRotation(ConvertMiRotation(rot));

            float[]? scale = firstKeyframe.GetScale();
            if (scale is { Length: >= 3 })
                obj.SetLocalScale(new vec3(scale[0], scale[1], scale[2]));
        }
    }

    private static MiKeyframe? GetFirstTimelineKeyframe(MiTimeline timeline)
    {
        if (timeline.Keyframes == null || timeline.Keyframes.Count == 0)
            return null;

        if (timeline.Keyframes.TryGetValue("0", out var zeroFrame) && zeroFrame != null)
            return zeroFrame;

        return timeline.Keyframes
            .OrderBy(kv => int.TryParse(kv.Key, out var n) ? n : int.MaxValue)
            .Select(kv => kv.Value)
            .FirstOrDefault(kf => kf != null);
    }

    private static bool? ResolveTimelineVisible(MiTimeline timeline, MiKeyframe? firstKf)
    {
        bool? visible = null;

        if (timeline?.DefaultValues != null)
        {
            if (timeline.DefaultValues.TryGetValue("VISIBLE", out float defaultVisible) ||
                timeline.DefaultValues.TryGetValue("visible", out defaultVisible))
            {
                visible = defaultVisible >= 0.5f;
            }
            else if (timeline.DefaultValues.TryGetValue("HIDE", out float defaultHide) ||
                     timeline.DefaultValues.TryGetValue("hide", out defaultHide))
            {
                visible = defaultHide < 0.5f;
            }
        }

        if (firstKf?.Visible.HasValue == true)
            visible = firstKf.Visible.Value;

        return visible;
    }

    private static string ResolveResourceFilename(string value, IReadOnlyDictionary<string, string> resourceDict)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        if (resourceDict.TryGetValue(value, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
            return mapped;
        return value;
    }

    private uint ResolveTemplateTexture(MiTemplate template, IReadOnlyDictionary<string, string> resourceDict,
        string objectDirectory)
    {
        if (template == null || string.IsNullOrWhiteSpace(template.ModelTex))
            return 0;

        return ResolveTextureRefTexture(template.ModelTex, resourceDict, objectDirectory);
    }

    private uint ResolveTextureRefTexture(string textureRef, IReadOnlyDictionary<string, string> resourceDict,
        string objectDirectory)
    {
        string resolvedRef = ResolveResourceFilename(textureRef, resourceDict);
        if (string.IsNullOrWhiteSpace(resolvedRef))
            return 0;

        string texturePath = Path.IsPathRooted(resolvedRef)
            ? resolvedRef
            : Path.Combine(objectDirectory, resolvedRef);

        if (!File.Exists(texturePath))
            return 0;

        return LoadTextureFromFile(texturePath);
    }

    private static string ResolveTemplateModelPath(MiTemplate template,
        IReadOnlyDictionary<string, string> resourceDict, MiObject miObject)
    {
        if (template == null || miObject == null || string.IsNullOrWhiteSpace(miObject.DirectoryPath))
            return string.Empty;

        string modelRef = ResolveResourceFilename(template.Model, resourceDict);
        if (!string.IsNullOrWhiteSpace(modelRef))
        {
            string candidate = Path.IsPathRooted(modelRef)
                ? modelRef
                : Path.Combine(miObject.DirectoryPath, modelRef);
            if (File.Exists(candidate))
                return candidate;
        }

        // Fallback: if the template did not resolve cleanly, use the first model resource.
        var modelResource = miObject.Resources?.FirstOrDefault(r =>
            r != null &&
            string.Equals(r.Type, "model", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(r.Filename));
        if (modelResource != null)
        {
            string modelFilename = modelResource.Filename!;
            string fallback = Path.IsPathRooted(modelFilename)
                ? modelFilename
                : Path.Combine(miObject.DirectoryPath, modelFilename);
            if (File.Exists(fallback))
                return fallback;
        }

        return string.Empty;
    }

    private SceneObject? CreateSceneObjectFromItemTemplate(MiTemplate template, MiTimeline timeline,
        IReadOnlyDictionary<string, MiResource> resourceInfoById, string objectDirectory)
    {
        if (template?.Item == null || string.IsNullOrWhiteSpace(template.Item.Tex))
            return null;
        if (!resourceInfoById.TryGetValue(template.Item.Tex, out var resource) ||
            string.IsNullOrWhiteSpace(resource?.Filename))
            return null;

        string texturePath = Path.IsPathRooted(resource.Filename)
            ? resource.Filename
            : Path.Combine(objectDirectory, resource.Filename);
        if (!File.Exists(texturePath))
            return null;

        uint textureId = LoadTextureFromFile(texturePath);
        if (textureId == 0)
            return null;

        var bytes = File.ReadAllBytes(texturePath);
        ImageResult image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
        int texWidth = image.Width;
        int texHeight = image.Height;

        int columns = Math.Max(1, resource.ItemSheetSize is { Length: >= 1 } ? resource.ItemSheetSize[0] : 1);
        int rows = Math.Max(1, resource.ItemSheetSize is { Length: >= 2 } ? resource.ItemSheetSize[1] : 1);
        int cellWidth = Math.Max(1, texWidth / columns);
        int cellHeight = Math.Max(1, texHeight / rows);

        MiKeyframe? firstKf = GetFirstTimelineKeyframe(timeline);
        int columnIndex = 0;
        int rowIndex = 0;

        if (firstKf?.ItemSlot.HasValue == true)
            columnIndex = Math.Clamp(firstKf.ItemSlot.Value, 0, columns - 1);
        else if (template.Item.Slot.HasValue)
            columnIndex = Math.Clamp(template.Item.Slot.Value, 0, columns - 1);

        if (firstKf?.CustomItemSlot.HasValue == true)
            rowIndex = Math.Clamp(firstKf.CustomItemSlot.Value - 1, 0, rows - 1);

        byte[] tilePixels = ExtractItemSheetTile(image.Data, texWidth, texHeight,
            columnIndex * cellWidth, rowIndex * cellHeight, cellWidth, cellHeight);
        int tileSize = Math.Max(cellWidth, cellHeight);

        uint tileTextureId = LoadTextureFromRgba(tilePixels, cellWidth, cellHeight);
        if (tileTextureId == 0)
            return null;

        var obj = new SceneObject
        {
            Name = !string.IsNullOrWhiteSpace(timeline.Name) ? timeline.Name : "Item",
            ObjectType = "Item",
            PrimitivePlaneFaceCamera = template.Item.FaceCamera,
            SpawnCategory = "Items",
            TextureType = "local",
            Position = vec3.Zero
        };
        obj.AssignObjectId();

        string sheetKey = ItemsAtlas.BuildTemporaryItemSheetKey(texturePath, columns, rows);
        ItemsAtlas.TryRegisterTemporaryItemSheet(sheetKey, texturePath, columns, rows);
        string tileKey = BuildImportedItemTileKey(texturePath, obj.ObjectId, columnIndex, rowIndex);
        ItemsAtlas.TryRegisterTemporaryItemTile(sheetKey, tileKey, columnIndex, rowIndex);

        obj.ItemTileKey = tileKey;
        obj.TemporaryItemSheetPath = texturePath;
        obj.TemporaryItemSheetCacheKey = sheetKey;
        obj.TemporaryItemSheetColumns = columns;
        obj.TemporaryItemSheetRows = rows;
        obj.TemporaryItemSheetColumnIndex = columnIndex;
        obj.TemporaryItemSheetRowIndex = rowIndex;

        Mesh mesh = new ExtrudedItemMesh(
            _gl,
            tileTextureId,
            tilePixels,
            is3D: template.Item.ThreeD,
            tileSize: tileSize,
            tileWidth: cellWidth,
            tileHeight: cellHeight,
            extrudeDepth: 1f / 16f);

        mesh.DoubleSided = true;
        obj.AddMesh(mesh);
        ImportItemSlotKeyframes(obj, timeline, columns, rows);
        return obj;
    }

    private static string BuildImportedItemTileKey(string texturePath, string objectId, int columnIndex, int rowIndex)
    {
        string keyBase = Path.GetFileNameWithoutExtension(texturePath);
        if (string.IsNullOrWhiteSpace(keyBase))
            keyBase = "miobject_item";

        return $"miobject:{SanitizeImportedItemKey(keyBase)}_{objectId}_{columnIndex}_{rowIndex}";
    }

    private static string SanitizeImportedItemKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "miobject_item";

        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_').ToArray();
        string sanitized = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "miobject_item" : sanitized;
    }

    private static void ImportItemSlotKeyframes(SceneObject obj, MiTimeline timeline, int columns, int rows)
    {
        if (obj == null || timeline?.Keyframes == null || timeline.Keyframes.Count == 0)
            return;

        var itemSlotKeyframes = new List<ObjectKeyframe>();
        var customSlotKeyframes = new List<ObjectKeyframe>();

        foreach (var kvp in timeline.Keyframes)
        {
            if (!int.TryParse(kvp.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frame) || kvp.Value == null)
                continue;

            if (kvp.Value.ItemSlot.HasValue)
            {
                itemSlotKeyframes.Add(new ObjectKeyframe
                {
                    Frame = frame,
                    Value = Math.Clamp(kvp.Value.ItemSlot.Value, 0, columns - 1),
                    InterpolationType = "instant"
                });
            }

            if (kvp.Value.CustomItemSlot.HasValue)
            {
                customSlotKeyframes.Add(new ObjectKeyframe
                {
                    Frame = frame,
                    Value = Math.Clamp(kvp.Value.CustomItemSlot.Value - 1, 0, rows - 1),
                    InterpolationType = "instant"
                });
            }
        }

        if (itemSlotKeyframes.Count > 0)
            obj.Keyframes["item.slot"] = itemSlotKeyframes.OrderBy(kf => kf.Frame).ToList();
        if (customSlotKeyframes.Count > 0)
            obj.Keyframes["item.custom_slot"] = customSlotKeyframes.OrderBy(kf => kf.Frame).ToList();
    }

    private static byte[] ExtractItemSheetTile(byte[] rgbaPixels, int imageWidth, int imageHeight,
        int startX, int startY, int tileWidth, int tileHeight)
    {
        var tilePixels = new byte[tileWidth * tileHeight * 4];

        for (int y = 0; y < tileHeight; y++)
        {
            int srcIndex = ((startY + y) * imageWidth + startX) * 4;
            int dstIndex = y * tileWidth * 4;
            System.Buffer.BlockCopy(rgbaPixels, srcIndex, tilePixels, dstIndex, tileWidth * 4);
        }

        return tilePixels;
    }

    private uint LoadTextureFromRgba(byte[] rgbaPixels, int width, int height)
    {
        if (_gl == null || rgbaPixels == null || rgbaPixels.Length == 0 || width <= 0 || height <= 0)
            return 0;

        string cacheKey = $"rgba:{width}x{height}:{Convert.ToBase64String(rgbaPixels)}";
        if (_textureCache.TryGetValue(cacheKey, out uint cached))
            return cached;

        uint tex = _gl.GenTexture();
        _gl.BindTexture(GLEnum.Texture2D, tex);
        unsafe
        {
            fixed (byte* p = rgbaPixels)
                _gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba, (uint)width, (uint)height,
                    0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
        }

        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.BindTexture(GLEnum.Texture2D, 0);

        _textureCache[cacheKey] = tex;
        return tex;
    }

    private static string GetBoneLookupKey(int boneIdx) => $"bone:{boneIdx}";

    private static bool TryFindBoneByModelPartName(CharacterSceneObject character, string modelPartName,
        out BoneSceneObject boneObj)
    {
        boneObj = character.BoneObjects.Values
            .FirstOrDefault(bone => string.Equals(bone.BoneName, modelPartName, StringComparison.Ordinal));
        return boneObj != null;
    }

    private void ApplyBodypartTimelines(MiObject miObject,
        Dictionary<string, SceneObject> sceneObjectsByTimelineId)
    {
        if (miObject?.Timelines == null)
            return;

        foreach (var timeline in miObject.Timelines)
        {
            if (!string.Equals(timeline.Type, "bodypart", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(timeline.PartOf) || string.IsNullOrWhiteSpace(timeline.ModelPartName))
                continue;
            if (!sceneObjectsByTimelineId.TryGetValue(timeline.PartOf, out var ownerSceneObject))
                continue;

            CharacterSceneObject? character = ownerSceneObject as CharacterSceneObject;
            if (character == null)
                continue;
            if (!TryFindBoneByModelPartName(character, timeline.ModelPartName, out var boneObj))
                continue;

            if (!string.IsNullOrWhiteSpace(timeline.Id))
                sceneObjectsByTimelineId[timeline.Id] = boneObj;

            // Bodypart timelines in .miobject often include editor-channel values.
            // Do not override model bone transforms/lock_bend from them at import
            // time; that can corrupt authored bend chains.
            ApplyTimelineSettings(boneObj, timeline, includeHierarchySettings: false,
                includeMaterialAndDepthSettings: false, applyBaseVisibilityFromHide: false);

            bool? explicitVisible = ResolveTimelineVisible(timeline, GetFirstTimelineKeyframe(timeline));
            if (explicitVisible.HasValue)
                boneObj.ObjectVisible = explicitVisible.Value;
        }
    }

    private void ApplyTimelineSettings(SceneObject obj, MiTimeline timeline, bool includeHierarchySettings,
        bool includeMaterialAndDepthSettings = true, bool applyBaseVisibilityFromHide = true)
    {
        if (obj == null || timeline == null)
            return;

        if (applyBaseVisibilityFromHide)
            obj.ObjectVisible = !timeline.Hide;

        if (includeMaterialAndDepthSettings)
        {
            if (timeline.Shadows.HasValue)
                obj.CastShadow = timeline.Shadows.Value;
            if (timeline.Ssao.HasValue)
                obj.IncludeInAmbientOcclusion = timeline.Ssao.Value;
            if (timeline.Fog.HasValue)
                obj.IncludeInFog = timeline.Fog.Value;
            if (timeline.HqHiding.HasValue)
                obj.RenderInHighQuality = !timeline.HqHiding.Value;
            if (timeline.LqHiding.HasValue)
                obj.RenderInLowQuality = !timeline.LqHiding.Value;
            if (timeline.TextureBlur.HasValue)
                obj.BlurTexture = timeline.TextureBlur.Value;
            if (timeline.TextureFiltering.HasValue)
                obj.TextureMipmaps = timeline.TextureFiltering.Value;

            // Mine-imator depth ordering maps to the renderer's per-object depth offset.
            obj.RenderDepthOffset = timeline.Depth;
        }

        if (includeHierarchySettings && timeline.Inherit != null)
        {
            if (timeline.Inherit.Position.HasValue)
                obj.InheritPosition = timeline.Inherit.Position.Value;
            if (timeline.Inherit.Rotation.HasValue)
                obj.InheritRotation = timeline.Inherit.Rotation.Value;
            if (timeline.Inherit.Scale.HasValue)
                obj.InheritScale = timeline.Inherit.Scale.Value;
            if (timeline.Inherit.Visibility.HasValue)
                obj.InheritVisibility = timeline.Inherit.Visibility.Value;
            if (timeline.Inherit.RotPoint.HasValue)
                obj.InheritPivotOffset = timeline.Inherit.RotPoint.Value;
        }

        if (includeHierarchySettings && timeline.RotPointCustom && timeline.RotPoint is { Length: >= 3 })
            obj.PivotOffset = ResolveTimelinePivotOffset(obj, timeline.RotPoint);
        else if (includeHierarchySettings && !timeline.RotPointCustom)
            obj.PivotOffset = vec3.Zero;
        if (includeMaterialAndDepthSettings && timeline.Backfaces)
        {
            obj.SetExplicitMaterialSettings();
            obj.MaterialSettings.DoubleSided = true;
            obj.PropagateMaterialSettingsToChildren();
        }

        MiKeyframe? firstKf = GetFirstTimelineKeyframe(timeline);

        bool? explicitVisible = ResolveTimelineVisible(timeline, firstKf);
        if (explicitVisible.HasValue)
            obj.ObjectVisible = explicitVisible.Value;

        string? mixColorValue = firstKf?.MixColor;
        if (string.IsNullOrWhiteSpace(mixColorValue) && timeline.DefaultValues != null &&
            timeline.DefaultValues.TryGetString("MIX_COLOR", out string defaultMixColor))
        {
            mixColorValue = defaultMixColor;
        }

        float? mixPercentValue = firstKf?.MixPercent;
        if (!mixPercentValue.HasValue && timeline.DefaultValues != null &&
            timeline.DefaultValues.TryGetValue("MIX_PERCENT", out float defaultMixPercent))
        {
            mixPercentValue = defaultMixPercent;
        }

        float? alphaValue = firstKf?.Alpha;
        if (!alphaValue.HasValue && timeline.DefaultValues != null &&
            timeline.DefaultValues.TryGetValue("ALPHA", out float defaultAlpha))
        {
            alphaValue = defaultAlpha;
        }

        bool hasMixColor = includeMaterialAndDepthSettings && !string.IsNullOrWhiteSpace(mixColorValue);
        bool hasMixPercent = includeMaterialAndDepthSettings && mixPercentValue.HasValue;
        bool hasAlpha = includeMaterialAndDepthSettings && alphaValue.HasValue;

        if (!(hasMixColor || hasMixPercent || hasAlpha))
            return;

        obj.SetExplicitMaterialSettings();

        if (hasMixColor)
        {
            vec3? mix = ParseMiColor(mixColorValue);
            if (mix.HasValue)
            {
                float mixAmount = hasMixPercent
                    ? Math.Clamp(mixPercentValue!.Value, 0f, 1f)
                    : obj.MaterialSettings.MixColor.w;
                obj.MaterialSettings.MixColor = new vec4(mix.Value, mixAmount);
            }
        }
        else if (hasMixPercent)
        {
            var mc = obj.MaterialSettings.MixColor;
            obj.MaterialSettings.MixColor = new vec4(mc.x, mc.y, mc.z,
                Math.Clamp(mixPercentValue!.Value, 0f, 1f));
        }

        if (hasAlpha)
        {
            float alpha = Math.Clamp(alphaValue!.Value, 0f, 1f);
            var ac = obj.MaterialSettings.AlbedoColor;
            obj.MaterialSettings.AlbedoColor = new vec4(ac.x, ac.y, ac.z, alpha);
            obj.MaterialSettings.Transparency = 1f - alpha;
        }

        obj.PropagateMaterialSettingsToChildren();
    }

    private static vec3 ResolveTimelinePivotOffset(SceneObject obj, float[] rotPoint)
    {
        var importedItemMesh = obj.Visuals.OfType<ExtrudedItemMesh>().FirstOrDefault();
        if (importedItemMesh == null)
        {
            return new vec3(
                rotPoint[0] / 16f,
                rotPoint[1] / 16f,
                rotPoint[2] / 16f);
        }

        float normalizedWidth = importedItemMesh.TileWidth / (float)importedItemMesh.TileSize;
        float normalizedHeight = importedItemMesh.TileHeight / (float)importedItemMesh.TileSize;
        float halfWidth = normalizedWidth * 0.5f;
        float halfHeight = normalizedHeight * 0.5f;
        float halfDepth = importedItemMesh.Is3D ? importedItemMesh.ExtrudeDepth * 0.5f : 0f;

        // Mine-imator item rot_point values are authored in the item's own 16-unit image space.
        // Our spawned item meshes are centered around the origin, so convert that image-space
        // pivot into the equivalent visual offset for the centered mesh.
        return new vec3(
            halfWidth - rotPoint[0] / 16f,
            halfHeight - rotPoint[1] / 16f,
            halfDepth - rotPoint[2] / 16f);
    }

    private static void ApplyTextureOverrideToScene(SceneObject root, uint textureId, uint onlyIfTextureId = 0)
    {
        if (textureId == 0 || root == null)
            return;

        if (root is MiBoneSceneObject miBone)
        {
            miBone.OverrideTexture(textureId, onlyIfTextureId);
        }
        else
        {
            foreach (var mesh in root.Visuals)
            {
                if (mesh.TextureId == 0)
                    continue;
                if (onlyIfTextureId != 0 && mesh.TextureId != onlyIfTextureId)
                    continue;
                mesh.TextureId = textureId;
            }
        }

        foreach (var child in root.Children)
            ApplyTextureOverrideToScene(child, textureId, onlyIfTextureId);
    }

    private void FlattenPartsForBones(List<MiPart> parts, int parentIdx, vec3 accumulatedParentScale,
        List<(MiPart part, int boneIdx, int parentIdx, vec3 accumulatedParentScale)> list)
    {
        if (parts == null) return;

        foreach (var part in parts)
        {
            int currentIdx = list.Count;
            list.Add((part, currentIdx, parentIdx, accumulatedParentScale));

            if (part.Parts is { Count: > 0 })
            {
                vec3 partScale = vec3.Ones;
                if (part.Scale != null && part.Scale.Length >= 3)
                    partScale = new vec3(part.Scale[0], part.Scale[1], part.Scale[2]);

                FlattenPartsForBones(part.Parts, currentIdx, accumulatedParentScale * partScale, list);
            }
        }
    }

    private void CreateBoneSceneObjects(CharacterSceneObject character,
        List<(MiPart part, int boneIdx, int parentIdx, vec3 accumulatedParentScale)> boneDataList)
    {
        // Pass 1: create all bone objects
        foreach (var (part, boneIdx, _, _) in boneDataList)
        {
            string boneName = part.Name;

            var boneObject = new MiBoneSceneObject
            {
                Name = boneName,
                BoneName = boneName,
                ObjectType = "Bone"
            };
            boneObject.AssignObjectId();
            // Build the octahedron indicator so the Viewport renders and picks it
            // the same way it does for Assimp-imported bones.
            boneObject.CreateIndicator(_gl);
            character.BoneObjects[GetBoneLookupKey(boneIdx)] = boneObject;
        }

        // Pass 2: build hierarchy, set transforms, inherit settings
        foreach (var (part, boneIdx, parentIdx, accumulatedParentScale) in boneDataList)
        {
            if (!character.BoneObjects.TryGetValue(GetBoneLookupKey(boneIdx), out var boneObject))
                continue;

            // Set transform from part data.
            // Bone SceneObjects always have Scale = vec3.Ones (part scale is baked into mesh
            // vertices instead).  Because no bone in the chain carries a non-unit scale,
            // GetWorldMatrix() never propagates parent scale to a child's position.  We must
            // therefore bake accumulatedParentScale (the product of all ancestor part scales)
            // directly into the local position, mirroring what the Godot/Skeleton3D source does.
            vec3 position = vec3.Zero;
            if (part.Position != null && part.Position.Length >= 3)
            {
                position = new vec3(
                    part.Position[0] / 16f,
                    part.Position[1] / 16f,
                    part.Position[2] / 16f
                );
                position *= accumulatedParentScale;
            }

            // Port of Modelbench el_update_part.gml lock_bend positioning. A child
            // locked to a bent half first subtracts the parent's scaled bend offset
            // from its local position, then inherits the parent's bent-half matrix.
            // Omitting this adjustment leaves a gap at every joint in chains made
            // from adjacent planes (PikanModel's JacketBend is a representative case).
            if (parentIdx >= 0)
            {
                MiPart parentPart = boneDataList[parentIdx].part;
                bool lockBend = !part.LockBend.HasValue || part.LockBend.Value != 0f;
                if (lockBend && parentPart?.Bend?.Part != null)
                {
                    float scaledParentOffset = parentPart.Bend.Offset ?? 0f;
                    switch (parentPart.Bend.Part.ToLowerInvariant())
                    {
                        case "left":
                        case "right":
                            position.x -= scaledParentOffset * accumulatedParentScale.x / 16f;
                            break;
                        case "upper":
                        case "lower":
                            position.y -= scaledParentOffset * accumulatedParentScale.y / 16f;
                            break;
                        case "front":
                        case "back":
                            position.z -= scaledParentOffset * accumulatedParentScale.z / 16f;
                            break;
                    }
                }
            }

            vec3 rotation = vec3.Zero;
            if (part.Rotation != null && part.Rotation.Length >= 3)
                rotation = ConvertMiRotation(part.Rotation);

            boneObject.SetLocalPosition(position);
            if (part.Rotation is { Length: >= 3 })
                boneObject.SetLocalRotationMatrix(ConvertMiRotationMatrix(part.Rotation), rotation);
            else
                boneObject.SetLocalRotation(rotation);
            boneObject.CastShadow = part.Shadows;
            boneObject.ObjectVisible = part.Visible;

            // Attach alpha/depth from part
            if (boneObject is MiBoneSceneObject mibone)
            {
                mibone.ColorAlpha = part.ColorAlpha;
                mibone.ColorBlend = ParseMiColor(part.ColorBlend);
                mibone.Depth = part.Depth;
            }

            // Wire into hierarchy
            if (parentIdx >= 0)
            {
                if (character.BoneObjects.TryGetValue(GetBoneLookupKey(parentIdx), out var parentBone))
                    parentBone.AddChild(boneObject);
                else
                    character.AddChild(boneObject);
            }
            else
            {
                character.AddChild(boneObject);
            }

            // Inherit from parent, then lock in the base pose so the UI shows zero/one offsets.
            if (boneObject is MiBoneSceneObject mib)
            {
                mib.InheritColorAlphaFromParent();
                mib.InheritColorBlendFromParent();
                mib.CommitBasePose();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Texture helpers
    // ─────────────────────────────────────────────────────────────────────────

    private uint GetShapeTexture(MiShape shape, MiPart part, MiModel model)
    {
        if (!string.IsNullOrEmpty(shape.Texture) && model?.DirectoryPath != null)
        {
            var path = Path.Combine(model.DirectoryPath, shape.Texture);
            if (File.Exists(path))
            {
                var t = LoadTextureFromFile(path);
                if (t != 0) return t;
            }
        }

        // Part-level texture inheritance (model_file_load_shape.gml parity)
        var partTex = GetPartTexture(part, null, model);
        if (partTex != 0) return partTex;

        return model?.GetTexture() ?? 0;
    }

    private uint GetPartTexture(MiPart part, string textureName, MiModel model)
    {
        if (part.LoadedTextures != null && part.LoadedTextures.Count > 0)
        {
            if (string.IsNullOrEmpty(textureName))
            {
                if (part.LoadedTextures.TryGetValue("resolved_texture", out uint inherited)) return inherited;
                if (part.LoadedTextures.TryGetValue("skin", out uint t1)) return t1;
                if (part.LoadedTextures.TryGetValue("texture", out uint t2)) return t2;
            }
            else if (part.LoadedTextures.TryGetValue(textureName, out uint t3))
            {
                return t3;
            }
        }

        return model?.GetTexture() ?? 0;
    }

    public void LoadModelTextures(MiModel model)
    {
        if (model == null || string.IsNullOrEmpty(model.DirectoryPath)) return;

        if (!string.IsNullOrEmpty(model.Texture))
        {
            var path = Path.Combine(model.DirectoryPath, model.Texture);
            if (File.Exists(path))
            {
                var t = LoadTextureFromFile(path);
                if (t != 0) model.LoadedTextures["texture"] = t;
            }
        }

        if (!string.IsNullOrEmpty(model.TextureMaterial))
        {
            var path = Path.Combine(model.DirectoryPath, model.TextureMaterial);
            if (File.Exists(path))
            {
                var t = LoadTextureFromFile(path);
                if (t != 0) model.LoadedTextures["texture_material"] = t;
            }
        }

        if (!string.IsNullOrEmpty(model.TextureNormal))
        {
            var path = Path.Combine(model.DirectoryPath, model.TextureNormal);
            if (File.Exists(path))
            {
                var t = LoadTextureFromFile(path);
                if (t != 0) model.LoadedTextures["texture_normal"] = t;
            }
        }

        if (model.Textures != null)
        {
            foreach (var (name, texPath) in model.Textures)
            {
                if (model.LoadedTextures.ContainsKey(name)) continue;
                string fullPath = Path.IsPathRooted(texPath)
                    ? texPath
                    : Path.Combine(model.DirectoryPath, texPath);
                if (!File.Exists(fullPath)) continue;
                var t = LoadTextureFromFile(fullPath);
                if (t != 0) model.LoadedTextures[name] = t;
            }
        }

        if (model.Parts != null) LoadPartTextures(model.Parts, model, model.GetTexture());
    }

    private void LoadPartTextures(List<MiPart> parts, MiModel model, uint inheritedTexture)
    {
        if (parts == null) return;
        foreach (var part in parts)
        {
            if (!string.IsNullOrEmpty(part.Texture) && !part.LoadedTextures.ContainsKey("texture"))
            {
                var path = Path.Combine(model.DirectoryPath, part.Texture);
                if (File.Exists(path))
                {
                    var t = LoadTextureFromFile(path);
                    if (t != 0) part.LoadedTextures["texture"] = t;
                }
            }

            if (!string.IsNullOrEmpty(part.TextureMaterial) && !part.LoadedTextures.ContainsKey("texture_material"))
            {
                var path = Path.Combine(model.DirectoryPath, part.TextureMaterial);
                if (File.Exists(path))
                {
                    var t = LoadTextureFromFile(path);
                    if (t != 0) part.LoadedTextures["texture_material"] = t;
                }
            }

            if (!string.IsNullOrEmpty(part.TextureNormal) && !part.LoadedTextures.ContainsKey("texture_normal"))
            {
                var path = Path.Combine(model.DirectoryPath, part.TextureNormal);
                if (File.Exists(path))
                {
                    var t = LoadTextureFromFile(path);
                    if (t != 0) part.LoadedTextures["texture_normal"] = t;
                }
            }

            if (part.Textures != null)
            {
                foreach (var kvp in part.Textures)
                {
                    if (part.LoadedTextures.ContainsKey(kvp.Key)) continue;
                    string fp = Path.IsPathRooted(kvp.Value)
                        ? kvp.Value
                        : Path.Combine(model.DirectoryPath, kvp.Value);
                    if (!File.Exists(fp)) continue;
                    var t = LoadTextureFromFile(fp);
                    if (t != 0) part.LoadedTextures[kvp.Key] = t;
                }
            }

            // A Mine-imator part inherits the nearest ancestor's selected
            // texture. Texture-owning grouping parts frequently contain no
            // shapes themselves (hair and facial rigs are common examples).
            uint resolvedTexture = inheritedTexture;
            if (part.LoadedTextures.TryGetValue("texture", out uint ownTexture) && ownTexture != 0)
                resolvedTexture = ownTexture;
            if (resolvedTexture != 0)
                part.LoadedTextures["resolved_texture"] = resolvedTexture;

            if (part.Parts is { Count: > 0 })
                LoadPartTextures(part.Parts, model, resolvedTexture);
        }
    }

    public uint LoadTextureFromFile(string path)
    {
        if (_textureCache.TryGetValue(path, out uint cached)) return cached;
        if (_gl == null || !File.Exists(path)) return 0;

        try
        {
            var bytes = File.ReadAllBytes(path);
            ImageResult img = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);

            uint tex = _gl.GenTexture();
            _gl.BindTexture(GLEnum.Texture2D, tex);
            unsafe
            {
                fixed (byte* p = img.Data)
                    _gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba, (uint)img.Width, (uint)img.Height,
                        0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
            }

            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);
            _gl.BindTexture(GLEnum.Texture2D, 0);

            _textureCache[path] = tex;
            return tex;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Texture load error '{path}': {ex.Message}");
            return 0;
        }
    }

    private static vec3? ParseMiColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string hex = value.Trim().TrimStart('#');
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        if (hex.Length != 6) return null;

        try
        {
            return new vec3(
                Convert.ToByte(hex.Substring(0, 2), 16) / 255f,
                Convert.ToByte(hex.Substring(2, 2), 16) / 255f,
                Convert.ToByte(hex.Substring(4, 2), 16) / 255f);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static void ApplyMaterialSettings(Mesh mesh, MiBoneSceneObject bone, uint textureId)
    {
        if (textureId != 0)
            mesh.TextureId = textureId;

        if (bone.ColorAlpha.HasValue)
            mesh.Alpha = bone.ColorAlpha.Value;
        if (bone.ColorBlend.HasValue)
            mesh.BlendColor = new vec4(bone.ColorBlend.Value, 1f);

        mesh.SortDepth = bone.Depth;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Mesh generation
    // ═════════════════════════════════════════════════════════════════════════

    private Mesh CreateShapeMesh(string partName, int shapeIndex, MiShape shape, MiModel model,
        uint textureId, vec3 accumulatedParentScale, BendParams? bendParams = null,
        BendStyle bendStyle = BendStyle.ProjectDefault, vec3? partColorBlend = null,
        float? partColorAlpha = null,
        float depth = 0f, int[]? textureSize = null)
    {
        if (shape?.From == null || shape.To == null) return null;

        if (shape.HideBackfaceLegacy.HasValue)
            shape.HideBack = shape.HideBackfaceLegacy.Value;

        int[]? effectiveTextureSize = shape.TextureSize ?? textureSize;
        int texWidth = effectiveTextureSize?[0] ?? 64;
        int texHeight = effectiveTextureSize?[1] ?? 64;

        float uvU = shape.Uv?[0] ?? 0;
        float uvV = shape.Uv?[1] ?? 0;

        vec3 from = new vec3(shape.From[0] / 16f, shape.From[1] / 16f, shape.From[2] / 16f);
        vec3 to = new vec3(shape.To[0] / 16f, shape.To[1] / 16f, shape.To[2] / 16f);

        float sizeX = Math.Abs(shape.To[0] - shape.From[0]);
        float sizeY = Math.Abs(shape.To[1] - shape.From[1]);
        float sizeZ = Math.Abs(shape.To[2] - shape.From[2]);

        vec3 shapePosition = vec3.Zero;
        if (shape.Position != null && shape.Position.Length >= 3)
        {
            shapePosition = new vec3(shape.Position[0] / 16f, shape.Position[1] / 16f, shape.Position[2] / 16f);
            // Mine-imator scales a shape offset by its owning part hierarchy,
            // but not by the shape's own scale.
            shapePosition *= accumulatedParentScale;
        }

        vec3 shapeRotation = vec3.Zero;
        if (shape.Rotation != null && shape.Rotation.Length >= 3)
            shapeRotation = ConvertMiShapeRotation(shape.Rotation);

        vec3 shapeScale = vec3.Ones;
        if (shape.Scale != null && shape.Scale.Length >= 3)
            shapeScale = new vec3(shape.Scale[0], shape.Scale[1], shape.Scale[2]);
        shapeScale *= accumulatedParentScale;

        vec3[]? shapeVertexOffsets = GetShapeVertexOffsets(shape);

        float inflate = shape.Inflate / 16f;

        BendParams? effectiveBend = (shape.Bend && bendParams.HasValue) ? bendParams : null;

        bool planeBent = effectiveBend.HasValue &&
                         (effectiveBend.Value.Angle.x != 0 || effectiveBend.Value.Angle.y != 0 ||
                          effectiveBend.Value.Angle.z != 0);

        Mesh mesh;

        if (shape.Type == "plane")
        {
            if (shape.ThreeD)
            {
                if (planeBent)
                    mesh = CreateBentExtrudedPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
                        textureId, shape.TextureMirror, shape.Invert, inflate, effectiveBend.Value,
                        shapePosition, shapeRotation, shapeScale, bendStyle);
                else
                    mesh = CreateExtrudedPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
                        textureId, shape.TextureMirror, shape.Invert, inflate, shapeRotation, shapeScale);
            }
            else if (planeBent)
            {
                mesh = CreateBentPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
                    shape.TextureMirror, shape.Invert, inflate, effectiveBend.Value, shapePosition,
                    shapeRotation, shapeScale, bendStyle, shape.HideFront, shape.HideBack);
            }
            else
            {
                mesh = CreatePlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
                    shape.TextureMirror, shape.Invert, inflate, shapeRotation, shapeScale,
                    shape.HideFront, shape.HideBack, shapeVertexOffsets);
            }
        }
        else
        {
            mesh = CreateBlockMesh(partName, shapeIndex, from, to, uvU, uvV, sizeX, sizeY, sizeZ,
                texWidth, texHeight, shape.TextureMirror, shape.TextureMirrorY, shape.Invert, inflate, effectiveBend,
                shapePosition, shapeRotation, shapeScale, bendStyle, shapeVertexOffsets);
        }

        if (mesh != null)
        {
            mesh.SortDepth = depth;

            if (textureId != 0) mesh.TextureId = textureId;
            if (partColorBlend.HasValue) mesh.BlendColor = new vec4(partColorBlend.Value, 1f);
            if (partColorAlpha.HasValue) mesh.Alpha = partColorAlpha.Value;

            // Bend matrix setup uses shapePosition to align the pivot region,
            // but the authored shape translation itself still needs to be
            // applied to the final vertices (matching non-bent paths).
            if (shapePosition != vec3.Zero)
            {
                for (int i = 0; i < mesh.Vertices.Count; i++)
                    mesh.Vertices[i] += shapePosition;
                mesh.Upload();
            }
        }

        return mesh;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Block mesh
    // ─────────────────────────────────────────────────────────────────────────

    private Mesh CreateBlockMesh(string partName, int shapeIndex, vec3 from, vec3 to,
        float uvU, float uvV, float sizeX, float sizeY, float sizeZ,
        int texWidth, int texHeight, bool textureMirror, bool textureMirrorY, bool invert, float inflate = 0f,
        BendParams? bend = null, vec3 shapePosition = default, vec3 shapeRotation = default,
        vec3 shapeScale = default, BendStyle bendStyle = BendStyle.ProjectDefault,
        vec3[]? shapeVertexOffsets = null)
    {
        var vertices = new List<vec3>();
        var normals = new List<vec3>();
        var uvs = new List<vec2>();
        var indices = new List<uint>();

        vec3 min = new vec3(Math.Min(from.x, to.x), Math.Min(from.y, to.y), Math.Min(from.z, to.z));
        vec3 max = new vec3(Math.Max(from.x, to.x), Math.Max(from.y, to.y), Math.Max(from.z, to.z));

        if (inflate != 0f)
        {
            min -= new vec3(inflate);
            max += new vec3(inflate);
        }

        float texU = uvU / texWidth;
        float texV = uvV / texHeight;

        float texSizeX = sizeX / texWidth;
        float texSizeY = sizeY / texHeight;
        float texSizeZ = sizeZ / texHeight;

        float texSizeFixX = (sizeX - 1f / 256f) / texWidth;
        float texSizeFixY = (sizeY - 1f / 256f) / texHeight;
        float texSizeFixZ = (sizeZ - 1f / 256f) / texHeight;

        // Face UV coords matching GML model_shape_generate_block layout
        // South face: at (uvU, uvV), extends by (sizeX, sizeZ)
        var texSouth1 = new vec2(texU, texV);
        var texSouth2 = new vec2(texU + texSizeFixX, texV);
        var texSouth3 = new vec2(texU + texSizeFixX, texV + texSizeFixY);
        var texSouth4 = new vec2(texU, texV + texSizeFixY);

        // Exact Modelbench cube-atlas layout, with its Z-up Y/Z dimensions
        // converted to this renderer's Y-up coordinates.
        var texEast1 = new vec2(texU + texSizeX, texV);
        var texEast2 = new vec2(texEast1.x + texSizeFixZ, texV);
        var texEast3 = new vec2(texEast1.x + texSizeFixZ, texV + texSizeFixY);
        var texEast4 = new vec2(texEast1.x, texV + texSizeFixY);

        var texWest1 = new vec2(texU - texSizeZ, texV);
        var texWest2 = new vec2(texWest1.x + texSizeFixZ, texV);
        var texWest3 = new vec2(texWest1.x + texSizeFixZ, texV + texSizeFixY);
        var texWest4 = new vec2(texWest1.x, texV + texSizeFixY);

        var texNorth1 = new vec2(texEast1.x + texSizeZ, texV);
        var texNorth2 = new vec2(texNorth1.x + texSizeFixX, texV);
        var texNorth3 = new vec2(texNorth1.x + texSizeFixX, texV + texSizeFixY);
        var texNorth4 = new vec2(texNorth1.x, texV + texSizeFixY);

        var texUp1 = new vec2(texU, texV - texSizeZ);
        var texUp2 = new vec2(texUp1.x + texSizeFixX, texUp1.y);
        var texUp3 = new vec2(texUp1.x + texSizeFixX, texUp1.y + texSizeFixZ);
        var texUp4 = new vec2(texUp1.x, texUp1.y + texSizeFixZ);

        var texDown4 = new vec2(texUp1.x + texSizeX, texUp1.y);
        var texDown3 = new vec2(texDown4.x + texSizeFixX, texDown4.y);
        var texDown2 = new vec2(texDown4.x + texSizeFixX, texDown4.y + texSizeFixZ);
        var texDown1 = new vec2(texDown4.x, texDown4.y + texSizeFixZ);

        if (textureMirror)
        {
            (texEast1, texWest1) = (texWest1, texEast1);
            (texEast2, texWest2) = (texWest2, texEast2);
            (texEast3, texWest3) = (texWest3, texEast3);
            (texEast4, texWest4) = (texWest4, texEast4);
            (texEast1, texEast2) = (texEast2, texEast1);
            (texEast3, texEast4) = (texEast4, texEast3);
            (texWest1, texWest2) = (texWest2, texWest1);
            (texWest3, texWest4) = (texWest4, texWest3);
            (texSouth1, texSouth2) = (texSouth2, texSouth1);
            (texSouth3, texSouth4) = (texSouth4, texSouth3);
            (texNorth1, texNorth2) = (texNorth2, texNorth1);
            (texNorth3, texNorth4) = (texNorth4, texNorth3);
            (texUp1, texUp2) = (texUp2, texUp1);
            (texUp3, texUp4) = (texUp4, texUp3);
            (texDown1, texDown2) = (texDown2, texDown1);
            (texDown3, texDown4) = (texDown4, texDown3);
        }

        // texture_mirror_y mirrors each atlas face in place. These UVs are
        // shared by both the ordinary and segmented bend paths, so applying it
        // here also preserves the flag when a bend causes the mesh to rebuild.
        if (textureMirrorY)
        {
            FlipFaceVertically(ref texEast1, ref texEast2, ref texEast3, ref texEast4);
            FlipFaceVertically(ref texWest1, ref texWest2, ref texWest3, ref texWest4);
            FlipFaceVertically(ref texSouth1, ref texSouth2, ref texSouth3, ref texSouth4);
            FlipFaceVertically(ref texNorth1, ref texNorth2, ref texNorth3, ref texNorth4);
            FlipFaceVertically(ref texUp1, ref texUp2, ref texUp3, ref texUp4);
            FlipFaceVertically(ref texDown1, ref texDown2, ref texDown3, ref texDown4);
        }

        bool isBent = bend.HasValue &&
                      (bend.Value.Angle.x != 0 || bend.Value.Angle.y != 0 || bend.Value.Angle.z != 0);

        if (!isBent)
        {
            // Build shape rotation + scale transforms
            mat4 shapeRotMat = BuildShapeRotMat(shapeRotation);
            mat4 shapeScaleMat = shapeScale != default && shapeScale != vec3.Ones
                ? mat4.Scale(shapeScale)
                : mat4.Identity;

            // Rotate/scale around the bone origin (vec3.Zero), matching Mine Imator's behaviour.
            // shapePosition is a separate translation baked into vertices after geometry is built.
            vec3 Rv(vec3 v) => BendHelper.TransformPoint(shapeRotMat * shapeScaleMat, v);
            vec3 Rn(vec3 n) => BendHelper.TransformDirection(shapeRotMat * shapeScaleMat, n);

            vec3 GetOffset(int index)
            {
                if (shapeVertexOffsets == null || index < 0 || index >= shapeVertexOffsets.Length)
                    return vec3.Zero;
                return shapeVertexOffsets[index];
            }

            float x1 = min.x, x2 = max.x, y1 = min.y, y2 = max.y, z1 = min.z, z2 = max.z;

            vec3 c1 = new vec3(x1, y2, z1) + GetOffset(0); // vert1
            vec3 c2 = new vec3(x2, y2, z1) + GetOffset(1); // vert2
            vec3 c3 = new vec3(x2, y1, z1) + GetOffset(2); // vert3
            vec3 c4 = new vec3(x1, y1, z1) + GetOffset(3); // vert4
            vec3 c5 = new vec3(x1, y2, z2) + GetOffset(4); // vert5
            vec3 c6 = new vec3(x2, y2, z2) + GetOffset(5); // vert6
            vec3 c7 = new vec3(x2, y1, z2) + GetOffset(6); // vert7
            vec3 c8 = new vec3(x1, y1, z2) + GetOffset(7); // vert8

            AddFaceWithUVs(vertices, normals, uvs, indices,
                Rv(c8), Rv(c7), Rv(c6), Rv(c5),
                Rn(new vec3(0, 0, 1)), texSouth4, texSouth3, texSouth2, texSouth1, invert);

            AddFaceWithUVs(vertices, normals, uvs, indices,
                Rv(c7), Rv(c3), Rv(c2), Rv(c6),
                Rn(new vec3(1, 0, 0)), texEast4, texEast3, texEast2, texEast1, invert);

            AddFaceWithUVs(vertices, normals, uvs, indices,
                Rv(c4), Rv(c8), Rv(c5), Rv(c1),
                Rn(new vec3(-1, 0, 0)), texWest4, texWest3, texWest2, texWest1, invert);

            AddFaceWithUVs(vertices, normals, uvs, indices,
                Rv(c3), Rv(c4), Rv(c1), Rv(c2),
                Rn(new vec3(0, 0, -1)), texNorth4, texNorth3, texNorth2, texNorth1, invert);

            AddFaceWithUVs(vertices, normals, uvs, indices,
                Rv(c5), Rv(c6), Rv(c2), Rv(c1),
                Rn(new vec3(0, 1, 0)), texUp4, texUp3, texUp2, texUp1, invert);

            AddFaceWithUVs(vertices, normals, uvs, indices,
                Rv(c4), Rv(c3), Rv(c7), Rv(c8),
                Rn(new vec3(0, -1, 0)), texDown4, texDown3, texDown2, texDown1, invert);
        }
        else
        {
            // Bent block — segmented geometry matching Modelbench's algorithm
            var b = bend.Value;

            int segAxis = b.Part switch
            {
                BendPart.Right or BendPart.Left => 0,
                BendPart.Upper or BendPart.Lower => 1,
                _ => 2
            };

            float x1 = min.x, x2 = max.x, y1 = min.y, y2 = max.y, z1 = min.z, z2 = max.z;
            float axisScale = MathF.Max(MathF.Abs(shapeScale[segAxis]), 1e-6f);
            // The segmentation loop operates on the unscaled source box, while
            // Mine-imator's bend offset/size and shape position are in scaled
            // part space. Convert the bend region back into the loop's space.
            float bendSize = b.BendSize / 16f / axisScale;
            float bendOffset = b.BendOffset / 16f / axisScale;
            float bendShapePosition = shapePosition[segAxis] / axisScale;

            BendStyle effectiveStyle = (bendStyle == BendStyle.ProjectDefault)
                ? ProjectBendStyle
                : bendStyle;

            bool singleXorZ = (b.AxisX && !b.AxisY && !b.AxisZ) || (!b.AxisX && !b.AxisY && b.AxisZ);
            bool sharpBend = (effectiveStyle == BendStyle.Blocky) && !b.ExplicitBendSize && singleXorZ;

            float detail = BendHelper.CalculateSegmentCount(b.BendSize, sharpBend);
            if (b.ExplicitBendSize && b.BendSize >= 1 && shapeScale[segAxis] > 0.5f)
                detail /= shapeScale[segAxis];

            float segSize = bendSize / detail;

            bool invAngle = (b.Part == BendPart.Lower || b.Part == BendPart.Back || b.Part == BendPart.Left);

            mat4 shapeRotMat = BuildShapeRotMat(shapeRotation);
            mat4 shapeScaleMat = shapeScale != default && shapeScale != vec3.Ones
                ? mat4.Scale(shapeScale)
                : mat4.Identity;
            // Modelbench rotates/scales the shape around its ordinary shape
            // origin first. model_part_get_bend_matrix then bends those rotated
            // vertices around the bend pivot. Rotating around the bend pivot
            // here changes the authored shape transform and separates adjacent
            // rotated shapes as soon as a non-zero bend is applied.
            mat4 rsm = shapeRotMat * shapeScaleMat;
            float axisStart = segAxis == 0 ? x1 : segAxis == 1 ? y1 : z1;

            float bendStart, bendEnd;
            switch (segAxis)
            {
                case 0:
                    bendStart = bendOffset - (bendShapePosition + axisStart) - bendSize / 2f;
                    bendEnd = bendOffset - (bendShapePosition + axisStart) + bendSize / 2f;
                    break;
                case 1:
                    bendStart = bendOffset - (bendShapePosition + axisStart) - bendSize / 2f;
                    bendEnd = bendOffset - (bendShapePosition + axisStart) + bendSize / 2f;
                    break;
                default:
                    bendStart = bendOffset - (bendShapePosition + axisStart) - bendSize / 2f;
                    bendEnd = bendOffset - (bendShapePosition + axisStart) + bendSize / 2f;
                    break;
            }

            float totalSize = segAxis == 0 ? (x2 - x1) : segAxis == 1 ? (y2 - y1) : (z2 - z1);

            float texpSide1, texpSide2, texpSide3;
            switch (segAxis)
            {
                case 0:
                    texpSide1 = texSouth1.x;
                    texpSide2 = texNorth2.x;
                    texpSide3 = texDown4.x;
                    break;
                case 1:
                    texpSide1 = texSouth3.y;
                    texpSide2 = texSouth3.y;
                    texpSide3 = texSouth3.y;
                    break;
                default:
                    texpSide1 = texEast2.x;
                    texpSide2 = texWest1.x;
                    texpSide3 = texUp1.y;
                    break;
            }

            vec3 p1, p2, p3, p4;
            vec3 n1, n2, n3, n4;
            vec2 texStart1, texStart2, texStart3, texStart4;
            vec2 texEnd1, texEnd2, texEnd3, texEnd4;

            switch (segAxis)
            {
                case 0:
                    p1 = new vec3(x1, y1, z2);
                    p2 = new vec3(x1, y2, z2);
                    p3 = new vec3(x1, y2, z1);
                    p4 = new vec3(x1, y1, z1);
                    n1 = new vec3(0, 1, 0);
                    n2 = new vec3(0, -1, 0);
                    n3 = new vec3(0, 0, 1);
                    n4 = new vec3(0, 0, -1);
                    texStart1 = texWest1;
                    texStart2 = texWest2;
                    texStart3 = texWest3;
                    texStart4 = texWest4;
                    texEnd1 = texEast1;
                    texEnd2 = texEast2;
                    texEnd3 = texEast3;
                    texEnd4 = texEast4;
                    break;
                case 1:
                    p1 = new vec3(x2, y1, z2);
                    p2 = new vec3(x1, y1, z2);
                    p3 = new vec3(x1, y1, z1);
                    p4 = new vec3(x2, y1, z1);
                    n1 = new vec3(1, 0, 0);
                    n2 = new vec3(-1, 0, 0);
                    n3 = new vec3(0, 0, 1);
                    n4 = new vec3(0, 0, -1);
                    texStart1 = texDown1;
                    texStart2 = texDown2;
                    texStart3 = texDown3;
                    texStart4 = texDown4;
                    texEnd1 = texUp1;
                    texEnd2 = texUp2;
                    texEnd3 = texUp3;
                    texEnd4 = texUp4;
                    break;
                default:
                    p1 = new vec3(x1, y2, z1);
                    p2 = new vec3(x2, y2, z1);
                    p3 = new vec3(x2, y1, z1);
                    p4 = new vec3(x1, y1, z1);
                    n1 = new vec3(1, 0, 0);
                    n2 = new vec3(-1, 0, 0);
                    n3 = new vec3(0, 1, 0);
                    n4 = new vec3(0, -1, 0);
                    texStart1 = texNorth1;
                    texStart2 = texNorth2;
                    texStart3 = texNorth3;
                    texStart4 = texNorth4;
                    texEnd1 = texSouth1;
                    texEnd2 = texSouth2;
                    texEnd3 = texSouth3;
                    texEnd4 = texSouth4;
                    break;
            }

            p1 = BendHelper.TransformPoint(rsm, p1);
            p2 = BendHelper.TransformPoint(rsm, p2);
            p3 = BendHelper.TransformPoint(rsm, p3);
            p4 = BendHelper.TransformPoint(rsm, p4);
            n1 = BendHelper.TransformDirection(rsm, n1);
            n2 = BendHelper.TransformDirection(rsm, n2);
            n3 = BendHelper.TransformDirection(rsm, n3);
            n4 = BendHelper.TransformDirection(rsm, n4);

            const float scaleFactor = 0.005f;

            float startP = bendStart > 0 ? 0f : bendEnd < 0 ? 1f : 1f - bendEnd / bendSize;
            if (invAngle) startP = 1f - startP;

            vec3 startBendVec = BendHelper.GetBendVector(b.Angle, startP);
            vec3 startScaleCorr = sharpBend
                ? BendHelper.GetBendScaleCorrection(bendStart, bendEnd, startP, 0, b.Angle, b)
                : vec3.Zero;
            mat4 startMat =
                BendHelper.GetBendMatrix(b, startBendVec, shapePosition, shapeScale, vec3.Ones + startScaleCorr);

            p1 = BendHelper.TransformPoint(startMat, p1);
            p2 = BendHelper.TransformPoint(startMat, p2);
            p3 = BendHelper.TransformPoint(startMat, p3);
            p4 = BendHelper.TransformPoint(startMat, p4);
            n1 = BendHelper.TransformDirection(startMat, n1);
            n2 = BendHelper.TransformDirection(startMat, n2);
            n3 = BendHelper.TransformDirection(startMat, n3);
            n4 = BendHelper.TransformDirection(startMat, n4);

            float segPos = 0f;
            while (true)
            {
                if (segPos >= totalSize)
                {
                    vec3 capNormal = segAxis == 0 ? new vec3(1, 0, 0) :
                        segAxis == 1 ? new vec3(0, 1, 0) : new vec3(0, 0, 1);
                    if (segAxis == 0 || segAxis == 2)
                        AddFaceWithUVs(vertices, normals, uvs, indices, p2, p1, p4, p3, capNormal, texEnd1, texEnd2,
                            texEnd3, texEnd4, invert);
                    else
                        AddFaceWithUVs(vertices, normals, uvs, indices, p4, p3, p2, p1, capNormal, texEnd1, texEnd2,
                            texEnd3, texEnd4, invert);
                    break;
                }

                if (segPos == 0f)
                {
                    vec3 startCapNormal = segAxis == 0 ? new vec3(-1, 0, 0) :
                        segAxis == 1 ? new vec3(0, -1, 0) : new vec3(0, 0, -1);
                    AddFaceWithUVs(vertices, normals, uvs, indices, p1, p2, p3, p4, startCapNormal, texStart1,
                        texStart2, texStart3, texStart4, invert);
                }

                float curSegSize;
                if (segPos >= bendEnd)
                    curSegSize = totalSize - segPos;
                else if (segPos < bendStart)
                    curSegSize = Math.Min(totalSize - segPos, bendStart);
                else
                {
                    curSegSize = segSize;
                    if (segPos == 0f)
                    {
                        // Mine-imator uses local from[segAxis] here. bendStart
                        // already includes the shape position term.
                        float fromCoord = segAxis == 0 ? x1
                            : segAxis == 1 ? y1
                            : z1;
                        curSegSize -= ModFix(fromCoord - bendStart, segSize);
                    }

                    curSegSize = Math.Min(totalSize - segPos, curSegSize);
                }

                segPos += Math.Max(curSegSize, 0.005f);

                vec3 np1, np2, np3, np4;
                vec3 nn1, nn2, nn3, nn4;
                float ntexpSide1, ntexpSide2, ntexpSide3;

                switch (segAxis)
                {
                    case 0:
                        np1 = new vec3(x1 + segPos, y1, z2);
                        np2 = new vec3(x1 + segPos, y2, z2);
                        np3 = new vec3(x1 + segPos, y2, z1);
                        np4 = new vec3(x1 + segPos, y1, z1);
                        nn1 = new vec3(0, 1, 0);
                        nn2 = new vec3(0, -1, 0);
                        nn3 = new vec3(0, 0, 1);
                        nn4 = new vec3(0, 0, -1);
                    {
                        float toff = (segPos / totalSize) * texSizeFixX * (textureMirror ? -1 : 1);
                        ntexpSide1 = texSouth1.x + toff;
                        ntexpSide2 = texNorth2.x - toff;
                        ntexpSide3 = texDown4.x + toff;
                    }
                        break;
                    case 1:
                        np1 = new vec3(x2, y1 + segPos, z2);
                        np2 = new vec3(x1, y1 + segPos, z2);
                        np3 = new vec3(x1, y1 + segPos, z1);
                        np4 = new vec3(x2, y1 + segPos, z1);
                        nn1 = new vec3(1, 0, 0);
                        nn2 = new vec3(-1, 0, 0);
                        nn3 = new vec3(0, 0, 1);
                        nn4 = new vec3(0, 0, -1);
                    {
                        float toff = (segPos / totalSize) * texSizeFixY;
                        ntexpSide1 = texSouth3.y - toff;
                        ntexpSide2 = ntexpSide1;
                        ntexpSide3 = ntexpSide1;
                    }
                        break;
                    default:
                        np1 = new vec3(x1, y2, z1 + segPos);
                        np2 = new vec3(x2, y2, z1 + segPos);
                        np3 = new vec3(x2, y1, z1 + segPos);
                        np4 = new vec3(x1, y1, z1 + segPos);
                        nn1 = new vec3(1, 0, 0);
                        nn2 = new vec3(-1, 0, 0);
                        nn3 = new vec3(0, 1, 0);
                        nn4 = new vec3(0, -1, 0);
                    {
                        float toff = (segPos / totalSize) * texSizeFixZ;
                        ntexpSide1 = texEast2.x - toff * (textureMirror ? -1 : 1);
                        ntexpSide2 = texWest1.x + toff * (textureMirror ? -1 : 1);
                        ntexpSide3 = texUp1.y + toff;
                    }
                        break;
                }

                np1 = BendHelper.TransformPoint(rsm, np1);
                np2 = BendHelper.TransformPoint(rsm, np2);
                np3 = BendHelper.TransformPoint(rsm, np3);
                np4 = BendHelper.TransformPoint(rsm, np4);
                nn1 = BendHelper.TransformDirection(rsm, nn1);
                nn2 = BendHelper.TransformDirection(rsm, nn2);
                nn3 = BendHelper.TransformDirection(rsm, nn3);
                nn4 = BendHelper.TransformDirection(rsm, nn4);

                float segP = segPos < bendStart ? 0f
                    : segPos >= bendEnd ? 1f
                    : 1f - (bendEnd - segPos) / bendSize;
                if (invAngle) segP = 1f - segP;

                vec3 segBendVec = sharpBend ? b.Angle * segP : BendHelper.GetBendVector(b.Angle, segP);
                vec3 segScaleCorr = sharpBend
                    ? BendHelper.GetBendScaleCorrection(bendStart, bendEnd, segP, segPos, b.Angle, b)
                    : vec3.Zero;
                vec3 segMatScale = vec3.Ones + segScaleCorr + new vec3(segP * scaleFactor);
                mat4 segMat = BendHelper.GetBendMatrix(b, segBendVec, shapePosition, shapeScale, segMatScale);

                np1 = BendHelper.TransformPoint(segMat, np1);
                np2 = BendHelper.TransformPoint(segMat, np2);
                np3 = BendHelper.TransformPoint(segMat, np3);
                np4 = BendHelper.TransformPoint(segMat, np4);
                nn1 = BendHelper.TransformDirection(segMat, nn1);
                nn2 = BendHelper.TransformDirection(segMat, nn2);
                nn3 = BendHelper.TransformDirection(segMat, nn3);
                nn4 = BendHelper.TransformDirection(segMat, nn4);

                // Modelbench clears supplied normals for sharp bends, causing
                // vbuffer_add_triangle to calculate a flat normal per face.
                if (sharpBend)
                    n1 = n2 = n3 = n4 = nn1 = nn2 = nn3 = nn4 = vec3.Zero;

                switch (segAxis)
                {
                    case 0:
                        AddFaceWithUVs(vertices, normals, uvs, indices, p2, np2, np3, p3, n1, nn1, nn1, n1,
                            new vec2(texpSide1, texSouth1.y), new vec2(ntexpSide1, texSouth1.y),
                            new vec2(ntexpSide1, texSouth3.y), new vec2(texpSide1, texSouth3.y), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, np1, p1, p4, np4, nn2, n2, n2, nn2,
                            new vec2(ntexpSide2, texNorth1.y), new vec2(texpSide2, texNorth1.y),
                            new vec2(texpSide2, texNorth3.y), new vec2(ntexpSide2, texNorth3.y), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, p1, np1, np2, p2, n3, nn3, nn3, n3,
                            new vec2(texpSide1, texUp1.y), new vec2(ntexpSide1, texUp1.y),
                            new vec2(ntexpSide1, texUp3.y), new vec2(texpSide1, texUp3.y), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, p3, np3, np4, p4, n4, nn4, nn4, n4,
                            new vec2(texpSide3, texDown1.y), new vec2(ntexpSide3, texDown1.y),
                            new vec2(ntexpSide3, texDown3.y), new vec2(texpSide3, texDown3.y), invert);
                        texpSide1 = ntexpSide1;
                        texpSide2 = ntexpSide2;
                        texpSide3 = ntexpSide3;
                        break;
                    case 1:
                        AddFaceWithUVs(vertices, normals, uvs, indices, np1, p1, p4, np4, nn1, n1, n1, nn1,
                            new vec2(texEast1.x, ntexpSide1), new vec2(texEast1.x, texpSide1),
                            new vec2(texEast2.x, texpSide1), new vec2(texEast2.x, ntexpSide1), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, p2, np2, np3, p3, n2, nn2, nn2, n2,
                            new vec2(texWest1.x, texpSide1), new vec2(texWest1.x, ntexpSide1),
                            new vec2(texWest2.x, ntexpSide1), new vec2(texWest2.x, texpSide1), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, p2, p1, np1, np2, n3, n3, nn3, nn3,
                            new vec2(texSouth1.x, texpSide1), new vec2(texSouth2.x, texpSide1),
                            new vec2(texSouth2.x, ntexpSide1), new vec2(texSouth1.x, ntexpSide1), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, np3, np4, p4, p3, nn4, nn4, n4, n4,
                            new vec2(texNorth1.x, ntexpSide1), new vec2(texNorth2.x, ntexpSide1),
                            new vec2(texNorth2.x, texpSide1), new vec2(texNorth1.x, texpSide1), invert);
                        texpSide1 = ntexpSide1;
                        break;
                    default:
                        AddFaceWithUVs(vertices, normals, uvs, indices, np2, np3, p3, p2, nn1, n1,
                            new vec2(ntexpSide1, texEast1.y), new vec2(texpSide1, texEast1.y),
                            new vec2(texpSide1, texEast3.y), new vec2(ntexpSide1, texEast3.y), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, np4, np1, p1, p4, nn2, n2,
                            new vec2(texpSide2, texWest1.y), new vec2(ntexpSide2, texWest1.y),
                            new vec2(ntexpSide2, texWest3.y), new vec2(texpSide2, texWest3.y), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, np1, np2, p2, p1, nn3, n3,
                            new vec2(texUp1.x, texpSide3), new vec2(texUp2.x, texpSide3),
                            new vec2(texUp2.x, ntexpSide3), new vec2(texUp1.x, ntexpSide3), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, np3, np4, p4, p3, nn4, n4,
                            new vec2(texDown1.x, ntexpSide3), new vec2(texDown2.x, ntexpSide3),
                            new vec2(texDown2.x, texpSide3), new vec2(texDown1.x, texpSide3), invert);
                        texpSide1 = ntexpSide1;
                        texpSide2 = ntexpSide2;
                        texpSide3 = ntexpSide3;
                        break;
                }

                p1 = np1;
                p2 = np2;
                p3 = np3;
                p4 = np4;
                n1 = nn1;
                n2 = nn2;
                n3 = nn3;
                n4 = nn4;
            }
        }

        return BuildMesh(vertices, normals, uvs, indices);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Plane mesh
    // ─────────────────────────────────────────────────────────────────────────

    private Mesh CreatePlaneMesh(vec3 from, vec3 to, float uvU, float uvV, float sizeX, float sizeY,
        int texWidth, int texHeight, bool textureMirror, bool invert, float inflate = 0f,
        vec3 shapeRotation = default, vec3 shapeScale = default,
        bool hideFront = false, bool hideBack = false, vec3[]? shapeVertexOffsets = null)
    {
        var vertices = new List<vec3>();
        var normals = new List<vec3>();
        var uvs = new List<vec2>();
        var indices = new List<uint>();

        vec3 min = new vec3(Math.Min(from.x, to.x), Math.Min(from.y, to.y), Math.Min(from.z, to.z));
        vec3 max = new vec3(Math.Max(from.x, to.x), Math.Max(from.y, to.y), Math.Max(from.z, to.z));

        if (inflate != 0f)
        {
            min -= new vec3(inflate);
            max += new vec3(inflate);
        }

        float texU = uvU / texWidth;
        float texV = uvV / texHeight;
        float texSizeX = sizeX / texWidth;
        float texSizeZ = sizeY / texHeight;

        var tex1 = new vec2(texU, texV);
        var tex2 = new vec2(texU + texSizeX, texV);
        var tex3 = new vec2(texU + texSizeX, texV + texSizeZ);
        var tex4 = new vec2(texU, texV + texSizeZ);

        if (textureMirror)
        {
            (tex1, tex2) = (tex2, tex1);
            (tex3, tex4) = (tex4, tex3);
        }

        // Rotate/scale around the bone origin (vec3.Zero), matching Mine Imator's behaviour.
        mat4 rsm = BuildShapeRotMat(shapeRotation) *
                   (shapeScale != default && shapeScale != vec3.Ones ? mat4.Scale(shapeScale) : mat4.Identity);
        vec3 Rv(vec3 v) => BendHelper.TransformPoint(rsm, v);

        vec3 GetOffset(int index)
        {
            if (shapeVertexOffsets == null || index < 0 || index >= shapeVertexOffsets.Length)
                return vec3.Zero;
            return shapeVertexOffsets[index];
        }

        float x1 = min.x, x2 = max.x, y1 = min.y, y2 = max.y, z1 = min.z, z2 = max.z;
        vec3 c1 = new vec3(x1, y2, z1) + GetOffset(0); // vert1
        vec3 c2 = new vec3(x2, y2, z1) + GetOffset(1); // vert2
        vec3 c3 = new vec3(x2, y1, z1) + GetOffset(2); // vert3
        vec3 c4 = new vec3(x1, y1, z1) + GetOffset(3); // vert4
        vec3 c5 = new vec3(x1, y2, z2) + GetOffset(4); // vert5
        vec3 c6 = new vec3(x2, y2, z2) + GetOffset(5); // vert6
        vec3 c7 = new vec3(x2, y1, z2) + GetOffset(6); // vert7
        vec3 c8 = new vec3(x1, y1, z2) + GetOffset(7); // vert8

        if (!hideFront)
        {
            int bv = vertices.Count;
            vec3 v0 = Rv(c4);
            vec3 v1 = Rv(c3);
            vec3 v2 = Rv(c2);
            vec3 v3 = Rv(c1);
            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);
            // Generate from the final rotated vertices in the same direction as
            // this side's triangle winding (0, 1, 2).
            var bn = CalculateFaceNormal(v0, v1, v2);
            normals.Add(bn);
            normals.Add(bn);
            normals.Add(bn);
            normals.Add(bn);
            if (invert)
            {
                uvs.Add(tex3);
                uvs.Add(tex4);
                uvs.Add(tex1);
                uvs.Add(tex2);
            }
            else
            {
                uvs.Add(tex4);
                uvs.Add(tex3);
                uvs.Add(tex2);
                uvs.Add(tex1);
            }

            AddQuadIndices(indices, (uint)bv, invert: false);
        }

        if (!hideBack)
        {
            int bv = vertices.Count;
            vec3 v0 = Rv(c8);
            vec3 v1 = Rv(c7);
            vec3 v2 = Rv(c6);
            vec3 v3 = Rv(c5);
            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);
            // The back uses reversed triangle indices (0, 2, 1).
            var fn = CalculateFaceNormal(v0, v2, v1);
            normals.Add(fn);
            normals.Add(fn);
            normals.Add(fn);
            normals.Add(fn);
            if (invert)
            {
                uvs.Add(tex3);
                uvs.Add(tex4);
                uvs.Add(tex1);
                uvs.Add(tex2);
            }
            else
            {
                uvs.Add(tex4);
                uvs.Add(tex3);
                uvs.Add(tex2);
                uvs.Add(tex1);
            }

            AddQuadIndices(indices, (uint)bv, invert: true);
        }

        return BuildMesh(vertices, normals, uvs, indices);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Extruded plane mesh (per-pixel item-style)
    // ─────────────────────────────────────────────────────────────────────────

    private Mesh CreateExtrudedPlaneMesh(vec3 from, vec3 to, float uvU, float uvV, float sizeX, float sizeY,
        int texWidth, int texHeight, uint textureId, bool textureMirror, bool invert, float inflate = 0f,
        vec3 shapeRotation = default, vec3 shapeScale = default)
    {
        if (textureId == 0)
            return CreatePlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
                textureMirror, invert, inflate, shapeRotation, shapeScale);

        var pixels = TryGetPixels(textureId, texWidth, texHeight, out int imgW, out int imgH);
        if (pixels == null)
            return CreatePlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
                textureMirror, invert, inflate, shapeRotation, shapeScale);

        int uvStartX = Math.Max(0, Math.Min((int)uvU, texWidth - 1));
        int uvStartY = Math.Max(0, Math.Min((int)uvV, texHeight - 1));
        int uvEndX = Math.Max(0, Math.Min((int)(uvU + sizeX), texWidth));
        int uvEndY = Math.Max(0, Math.Min((int)(uvV + sizeY), texHeight));

        int regionW = uvEndX - uvStartX;
        int regionH = uvEndY - uvStartY;

        if (regionW <= 0 || regionH <= 0)
            return CreatePlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
                textureMirror, invert, inflate, shapeRotation, shapeScale);

        const float thickness = 1f / 16f;
        float halfThickness = thickness / 2f + inflate;

        vec3 size = to - from;
        float pixScaleX = size.x / regionW;
        float pixScaleY = size.y / regionH;

        // Rotate/scale around the bone origin (vec3.Zero), matching Mine Imator's behaviour.
        mat4 rsm = BuildShapeRotMat(shapeRotation) *
                   (shapeScale != default && shapeScale != vec3.Ones ? mat4.Scale(shapeScale) : mat4.Identity);
        vec3 Rv(vec3 v) => BendHelper.TransformPoint(rsm, v);

        var vertices = new List<vec3>();
        var normals = new List<vec3>();
        var uvs = new List<vec2>();
        var indices = new List<uint>();

        for (int py = 0; py < regionH; py++)
        {
            for (int px = 0; px < regionW; px++)
            {
                int texX = uvStartX + px;
                int texY = uvStartY + py;
                if (texX >= imgW || texY >= imgH) continue;
                if (GetAlpha(pixels, texX, texY, imgW) <= 0.5f) continue;

                float posX = textureMirror ? (to.x - (px + 1) * pixScaleX) : (from.x + px * pixScaleX);
                float posY = to.y - (py + 1) * pixScaleY;

                if (inflate != 0f)
                {
                    posX -= inflate;
                    posY -= inflate;
                }

                float adjPSX = pixScaleX + (inflate != 0f ? inflate * 2 : 0f);
                float adjPSY = pixScaleY + (inflate != 0f ? inflate * 2 : 0f);
                float centerZ = from.z + 0.5f * 0.0625f;

                float uvX = (texX + 0.5f) / texWidth;
                float uvY = (texY + 0.5f) / texHeight;

                int bv = vertices.Count;
                AddExtrudedQuad(vertices, normals, uvs, indices, (uint)bv,
                    Rv(new vec3(posX, posY, centerZ + halfThickness)),
                    Rv(new vec3(posX + adjPSX, posY, centerZ + halfThickness)),
                    Rv(new vec3(posX + adjPSX, posY + adjPSY, centerZ + halfThickness)),
                    Rv(new vec3(posX, posY + adjPSY, centerZ + halfThickness)),
                    uvX, uvY, invert);

                bv = vertices.Count;
                AddExtrudedQuad(vertices, normals, uvs, indices, (uint)bv,
                    Rv(new vec3(posX + adjPSX, posY, centerZ - halfThickness)),
                    Rv(new vec3(posX, posY, centerZ - halfThickness)),
                    Rv(new vec3(posX, posY + adjPSY, centerZ - halfThickness)),
                    Rv(new vec3(posX + adjPSX, posY + adjPSY, centerZ - halfThickness)),
                    uvX, uvY, invert);

                bool leftEmpty = px == 0 || GetAlpha(pixels, uvStartX + px - 1, texY, imgW) <= 0.5f;
                bool rightEmpty = px == regionW - 1 || GetAlpha(pixels, uvStartX + px + 1, texY, imgW) <= 0.5f;
                bool topEmpty = py == 0 || GetAlpha(pixels, texX, uvStartY + py - 1, imgW) <= 0.5f;
                bool bottomEmpty = py == regionH - 1 || GetAlpha(pixels, texX, uvStartY + py + 1, imgW) <= 0.5f;

                bool geoLeft = textureMirror ? rightEmpty : leftEmpty;
                bool geoRight = textureMirror ? leftEmpty : rightEmpty;

                if (geoLeft)
                {
                    bv = vertices.Count;
                    AddExtrudedQuad(vertices, normals, uvs, indices, (uint)bv,
                        Rv(new vec3(posX, posY, centerZ - halfThickness)),
                        Rv(new vec3(posX, posY, centerZ + halfThickness)),
                        Rv(new vec3(posX, posY + adjPSY, centerZ + halfThickness)),
                        Rv(new vec3(posX, posY + adjPSY, centerZ - halfThickness)),
                        uvX, uvY, invert);
                }

                if (geoRight)
                {
                    bv = vertices.Count;
                    AddExtrudedQuad(vertices, normals, uvs, indices, (uint)bv,
                        Rv(new vec3(posX + adjPSX, posY, centerZ + halfThickness)),
                        Rv(new vec3(posX + adjPSX, posY, centerZ - halfThickness)),
                        Rv(new vec3(posX + adjPSX, posY + adjPSY, centerZ - halfThickness)),
                        Rv(new vec3(posX + adjPSX, posY + adjPSY, centerZ + halfThickness)),
                        uvX, uvY, invert);
                }

                if (topEmpty)
                {
                    bv = vertices.Count;
                    AddExtrudedQuad(vertices, normals, uvs, indices, (uint)bv,
                        Rv(new vec3(posX, posY + adjPSY, centerZ + halfThickness)),
                        Rv(new vec3(posX + adjPSX, posY + adjPSY, centerZ + halfThickness)),
                        Rv(new vec3(posX + adjPSX, posY + adjPSY, centerZ - halfThickness)),
                        Rv(new vec3(posX, posY + adjPSY, centerZ - halfThickness)),
                        uvX, uvY, invert);
                }

                if (bottomEmpty)
                {
                    bv = vertices.Count;
                    AddExtrudedQuad(vertices, normals, uvs, indices, (uint)bv,
                        Rv(new vec3(posX, posY, centerZ - halfThickness)),
                        Rv(new vec3(posX + adjPSX, posY, centerZ - halfThickness)),
                        Rv(new vec3(posX + adjPSX, posY, centerZ + halfThickness)),
                        Rv(new vec3(posX, posY, centerZ + halfThickness)),
                        uvX, uvY, invert);
                }
            }
        }

        return BuildMesh(vertices, normals, uvs, indices);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Bent plane mesh
    // ─────────────────────────────────────────────────────────────────────────

    private Mesh CreateBentPlaneMesh(vec3 from, vec3 to, float uvU, float uvV, float sizeX, float sizeY,
        int texWidth, int texHeight, bool textureMirror, bool invert, float inflate,
        BendParams bend, vec3 shapePosition, vec3 shapeRotation = default, vec3 shapeScale = default,
        BendStyle bendStyle = BendStyle.ProjectDefault, bool hideFront = false, bool hideBack = false)
    {
        var vertices = new List<vec3>();
        var normals = new List<vec3>();
        var uvs = new List<vec2>();
        var indices = new List<uint>();

        float x1 = Math.Min(from.x, to.x), x2 = Math.Max(from.x, to.x);
        float y1 = Math.Min(from.y, to.y), y2 = Math.Max(from.y, to.y);
        float z1 = from.z;

        if (inflate != 0f)
        {
            x1 -= inflate;
            x2 += inflate;
            y1 -= inflate;
            y2 += inflate;
        }

        float texU = uvU / texWidth, texV = uvV / texHeight;
        float texSX = sizeX / texWidth, texSY = sizeY / texHeight;

        var tex1 = new vec2(texU, texV);
        var tex2 = new vec2(texU + texSX, texV);
        var tex3 = new vec2(texU + texSX, texV + texSY);
        var tex4 = new vec2(texU, texV + texSY);

        if (textureMirror)
        {
            (tex1, tex2) = (tex2, tex1);
            (tex3, tex4) = (tex4, tex3);
        }

        var b = bend;
        // Plane is flat on Z — Front/Back (Z-axis) and Upper/Lower (Y-axis) both bend along Y
        int segAxis = b.Part switch
        {
            BendPart.Right or BendPart.Left => 0,
            BendPart.Upper or BendPart.Lower => 1,
            _ => 1
        };

        float axisScale = MathF.Max(MathF.Abs(shapeScale[segAxis]), 1e-6f);
        float bendSize = b.BendSize / 16f / axisScale;
        float bendOffset = b.BendOffset / 16f / axisScale;
        float bendShapePosition = shapePosition[segAxis] / axisScale;

        BendStyle effectiveStyle = (bendStyle == BendStyle.ProjectDefault) ? ProjectBendStyle : bendStyle;
        bool singleXorZ = (b.AxisX && !b.AxisY && !b.AxisZ) || (!b.AxisX && !b.AxisY && b.AxisZ);
        bool sharpBend = (effectiveStyle == BendStyle.Blocky) && !b.ExplicitBendSize && singleXorZ;

        float detail = BendHelper.CalculateSegmentCount(b.BendSize, sharpBend);
        if (b.ExplicitBendSize && b.BendSize >= 1 && shapeScale[segAxis] > 0.5f) detail /= shapeScale[segAxis];
        float segSize = bendSize / detail;

        bool invAngle = (b.Part == BendPart.Lower || b.Part == BendPart.Back || b.Part == BendPart.Left);
        float totalSize = segAxis == 0 ? (x2 - x1) : (y2 - y1);

        float bendStart = segAxis == 0
            ? bendOffset - (bendShapePosition + x1) - bendSize / 2f
            : bendOffset - (bendShapePosition + y1) - bendSize / 2f;
        float bendEnd = bendStart + bendSize;

        mat4 rsm = BuildShapeRotMat(shapeRotation) *
                   (shapeScale != default && shapeScale != vec3.Ones ? mat4.Scale(shapeScale) : mat4.Identity);

        vec3 p1 = segAxis == 0 ? new vec3(x1, y2, z1) : new vec3(x1, y1, z1);
        vec3 p2 = segAxis == 0 ? new vec3(x1, y1, z1) : new vec3(x2, y1, z1);
        float texp1 = segAxis == 0 ? tex1.x : tex3.y;

        p1 = BendHelper.TransformPoint(rsm, p1);
        p2 = BendHelper.TransformPoint(rsm, p2);

        float startP = bendStart > 0 ? 0f : bendEnd < 0 ? 1f : 1f - bendEnd / bendSize;
        if (invAngle) startP = 1f - startP;
        vec3 startBendVec = BendHelper.GetBendVector(b.Angle, startP);
        vec3 startScaleCorr = sharpBend
            ? BendHelper.GetBendScaleCorrection(bendStart, bendEnd, startP, 0, b.Angle, b)
            : vec3.Zero;
        mat4 startMat =
            BendHelper.GetBendMatrix(b, startBendVec, shapePosition, shapeScale, vec3.Ones + startScaleCorr);
        p1 = BendHelper.TransformPoint(startMat, p1);
        p2 = BendHelper.TransformPoint(startMat, p2);
        // Match CreatePlaneMesh and Modelbench's South/Front vs North/Back
        // convention after converting its Y-normal planes into our Z-normal
        // model space: Front is -Z and Back is +Z.
        var n1 = BendHelper.TransformDirection(startMat * rsm, new vec3(0, 0, -1));
        var n2 = BendHelper.TransformDirection(startMat * rsm, new vec3(0, 0, 1));

        float segPos = 0f;
        while (segPos < totalSize)
        {
            float curSegSize;
            if (segPos >= bendEnd) curSegSize = totalSize - segPos;
            else if (segPos < bendStart) curSegSize = Math.Min(totalSize - segPos, bendStart);
            else
            {
                curSegSize = segSize;
                if (segPos == 0f)
                {
                    // Mine-imator uses local from[segAxis] here. bendStart
                    // already includes the shape position term.
                    float fromCoord = segAxis == 0 ? x1 : y1;
                    curSegSize -= ModFix(fromCoord - bendStart, segSize);
                }

                curSegSize = Math.Min(totalSize - segPos, curSegSize);
            }

            segPos += Math.Max(curSegSize, 0.005f);

            vec3 np1, np2;
            float ntexp1;
            if (segAxis == 0)
            {
                np1 = BendHelper.TransformPoint(rsm, new vec3(x1 + segPos, y2, z1));
                np2 = BendHelper.TransformPoint(rsm, new vec3(x1 + segPos, y1, z1));
                float toff = (segPos / totalSize) * texSX * (textureMirror ? -1f : 1f);
                ntexp1 = tex1.x + toff;
            }
            else
            {
                np1 = BendHelper.TransformPoint(rsm, new vec3(x1, y1 + segPos, z1));
                np2 = BendHelper.TransformPoint(rsm, new vec3(x2, y1 + segPos, z1));
                float toff = (segPos / totalSize) * texSY;
                ntexp1 = tex3.y - toff;
            }

            float segP = segPos < bendStart ? 0f : segPos >= bendEnd ? 1f : 1f - (bendEnd - segPos) / bendSize;
            if (invAngle) segP = 1f - segP;

            vec3 segBendVec = sharpBend ? b.Angle * segP : BendHelper.GetBendVector(b.Angle, segP);
            // Official non-3D plane generation keeps per-segment scale at 1
            // (sharp-bend correction is only applied on the initial bend).
            mat4 segMat = BendHelper.GetBendMatrix(b, segBendVec, shapePosition, shapeScale, vec3.Ones);

            np1 = BendHelper.TransformPoint(segMat, np1);
            np2 = BendHelper.TransformPoint(segMat, np2);
            var nn1 = BendHelper.TransformDirection(segMat * rsm, new vec3(0, 0, -1));
            var nn2 = BendHelper.TransformDirection(segMat * rsm, new vec3(0, 0, 1));

            if (sharpBend)
                n1 = n2 = nn1 = nn2 = vec3.Zero;

            vec2 t1, t2, t3, t4;
            if (segAxis == 0)
            {
                t1 = new vec2(texp1, tex1.y);
                t2 = new vec2(ntexp1, tex1.y);
                t3 = new vec2(ntexp1, tex3.y);
                t4 = new vec2(texp1, tex3.y);
                // Bent planes arrive from Modelbench with South/North mapped to
                // the opposite serialized front/back flag in our Y-up space.
                if (!hideBack)
                    AddFaceWithUVs(vertices, normals, uvs, indices, p1, np1, np2, p2, n1, nn1, nn1, n1, t1, t2, t3, t4,
                        invert);
                if (!hideFront)
                    AddFaceWithUVs(vertices, normals, uvs, indices, np1, p1, p2, np2, nn2, n2, n2, nn2, t2, t1, t4, t3,
                        invert);
            }
            else
            {
                t1 = new vec2(tex1.x, ntexp1);
                t2 = new vec2(tex2.x, ntexp1);
                t3 = new vec2(tex2.x, texp1);
                t4 = new vec2(tex1.x, texp1);
                if (!hideBack)
                    AddFaceWithUVs(vertices, normals, uvs, indices, np1, np2, p2, p1, nn1, nn1, n1, n1, t1, t2, t3, t4,
                        invert);
                if (!hideFront)
                    AddFaceWithUVs(vertices, normals, uvs, indices, np2, np1, p1, p2, nn2, nn2, n2, n2, t2, t1, t4, t3,
                        invert);
            }

            p1 = np1;
            p2 = np2;
            n1 = nn1;
            n2 = nn2;
            texp1 = ntexp1;
        }

        return BuildMesh(vertices, normals, uvs, indices);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Bent extruded plane mesh
    // ─────────────────────────────────────────────────────────────────────────

    private Mesh CreateBentExtrudedPlaneMesh(vec3 from, vec3 to, float uvU, float uvV, float sizeX, float sizeY,
        int texWidth, int texHeight, uint textureId, bool textureMirror, bool invert,
        float inflate, BendParams bend, vec3 shapePosition, vec3 shapeRotation = default,
        vec3 shapeScale = default, BendStyle bendStyle = BendStyle.ProjectDefault)
    {
        if (textureId == 0)
            return CreateBentPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
                textureMirror, invert, inflate, bend, shapePosition, shapeRotation, shapeScale, bendStyle);

        var pixels = TryGetPixels(textureId, texWidth, texHeight, out int imgW, out int imgH);
        if (pixels == null)
            return CreateBentPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
                textureMirror, invert, inflate, bend, shapePosition, shapeRotation, shapeScale, bendStyle);

        int uvStartX = Math.Max(0, Math.Min((int)uvU, texWidth - 1));
        int uvStartY = Math.Max(0, Math.Min((int)uvV, texHeight - 1));
        int uvEndX = Math.Max(0, Math.Min((int)(uvU + sizeX), texWidth));
        int uvEndY = Math.Max(0, Math.Min((int)(uvV + sizeY), texHeight));
        int regionW = uvEndX - uvStartX;
        int regionH = uvEndY - uvStartY;

        if (regionW <= 0 || regionH <= 0)
            return CreateBentPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
                textureMirror, invert, inflate, bend, shapePosition, shapeRotation, shapeScale, bendStyle);

        float x1 = Math.Min(from.x, to.x), x2 = Math.Max(from.x, to.x);
        float y1 = Math.Min(from.y, to.y), y2 = Math.Max(from.y, to.y);
        float z1 = from.z + 0.5f * 0.0625f;

        float pixScaleX = (x2 - x1) / regionW;
        float pixScaleY = (y2 - y1) / regionH;

        const float thickness = 1f / 16f;
        float halfT = thickness / 2f + inflate;

        var b = bend;
        bool bendAlongX = (b.Part == BendPart.Left || b.Part == BendPart.Right);
        BendStyle effectiveStyle = (bendStyle == BendStyle.ProjectDefault) ? ProjectBendStyle : bendStyle;
        bool singleXorZ = (b.AxisX && !b.AxisY && !b.AxisZ) || (!b.AxisX && !b.AxisY && b.AxisZ);
        bool sharpBend = (effectiveStyle == BendStyle.Blocky) && !b.ExplicitBendSize && singleXorZ;

        // Extruded plane bends map Front/Back to Y-axis (same as Upper/Lower) since the plane is XY-aligned
        int segAxis = b.Part switch
        {
            BendPart.Right or BendPart.Left => 0,
            BendPart.Upper or BendPart.Lower => 1,
            _ => 1
        };
        float axisScale = MathF.Max(MathF.Abs(shapeScale[segAxis]), 1e-6f);
        float bendSize = b.BendSize / 16f / axisScale;
        float bendOffset = b.BendOffset / 16f / axisScale;
        float bendShapePosition = shapePosition[segAxis] / axisScale;
        bool invAngle = (b.Part == BendPart.Lower || b.Part == BendPart.Back || b.Part == BendPart.Left);

        mat4 shapeRotMat = BuildShapeRotMat(shapeRotation);
        mat4 rsm = shapeRotMat *
                   (shapeScale != default && shapeScale != vec3.Ones ? mat4.Scale(shapeScale) : mat4.Identity);
        float axisStart = bendAlongX ? x1 : y1;

        float bendStart = bendAlongX
            ? bendOffset - (bendShapePosition + axisStart) - bendSize / 2f
            : bendOffset - (bendShapePosition + axisStart) - bendSize / 2f;
        float bendEnd = bendStart + bendSize;

        int outerCount = bendAlongX ? regionH : regionW;
        int innerCount = bendAlongX ? regionW : regionH;

        var gridBot = new vec3[outerCount + 1, innerCount + 1];
        var gridTop = new vec3[outerCount + 1, innerCount + 1];

        for (int outer = 0; outer <= outerCount; outer++)
        {
            for (int inner = 0; inner <= innerCount; inner++)
            {
                vec3 pBot, pTop;
                if (bendAlongX)
                {
                    float px = x1 + inner * pixScaleX;
                    float py = y1 + outer * pixScaleY;
                    pBot = new vec3(px, py, z1 - halfT);
                    pTop = new vec3(px, py, z1 + halfT);
                }
                else
                {
                    float px = x1 + outer * pixScaleX;
                    float py = y1 + inner * pixScaleY;
                    pBot = new vec3(px, py, z1 - halfT);
                    pTop = new vec3(px, py, z1 + halfT);
                }

                pBot = BendHelper.TransformPoint(rsm, pBot);
                pTop = BendHelper.TransformPoint(rsm, pTop);

                float innerPos = inner * (bendAlongX ? pixScaleX : pixScaleY);
                float segP;
                if (innerPos >= bendEnd) segP = 1f;
                else if (innerPos < bendStart) segP = 0f;
                else
                {
                    float relPos = innerPos - bendStart;
                    segP = bendSize <= 0f ? 0f : Math.Clamp(relPos / bendSize, 0f, 1f);
                }

                if (invAngle) segP = 1f - segP;

                vec3 bendVec = sharpBend ? b.Angle * segP : BendHelper.GetBendVector(b.Angle, segP);
                vec3 scCorr = sharpBend
                    ? BendHelper.GetBendScaleCorrection(bendStart, bendEnd, segP, innerPos, b.Angle, b)
                    : vec3.Zero;
                mat4 mat = BendHelper.GetBendMatrix(b, bendVec, shapePosition, shapeScale, vec3.Ones + scCorr);
                gridBot[outer, inner] = BendHelper.TransformPoint(mat, pBot);
                gridTop[outer, inner] = BendHelper.TransformPoint(mat, pTop);
            }
        }

        var vertices = new List<vec3>();
        var normals = new List<vec3>();
        var uvs = new List<vec2>();
        var indices = new List<uint>();

        float texNormW = 1f / texWidth;
        float texNormH = 1f / texHeight;

        for (int outer = 0; outer < outerCount; outer++)
        {
            for (int inner = 0; inner < innerCount; inner++)
            {
                int ax, ay;
                if (bendAlongX)
                {
                    ax = textureMirror ? (regionW - 1 - inner) : inner;
                    ay = regionH - 1 - outer;
                }
                else
                {
                    ax = textureMirror ? (regionW - 1 - outer) : outer;
                    ay = regionH - 1 - inner;
                }

                int texX = uvStartX + ax;
                int texY = uvStartY + ay;
                if (texX >= imgW || texY >= imgH) continue;
                if (GetAlpha(pixels, texX, texY, imgW) <= 0.5f) continue;

                float uvX = (texX + 0.5f) * texNormW;
                float uvY = (texY + 0.5f) * texNormH;
                var pixUV = new vec2(uvX, uvY);

                vec3 p1, p2, p3, p4, np1, np2, np3, np4;
                if (bendAlongX)
                {
                    p1 = gridBot[outer + 1, inner];
                    p2 = gridTop[outer + 1, inner];
                    p3 = gridTop[outer, inner];
                    p4 = gridBot[outer, inner];
                    np1 = gridBot[outer + 1, inner + 1];
                    np2 = gridTop[outer + 1, inner + 1];
                    np3 = gridTop[outer, inner + 1];
                    np4 = gridBot[outer, inner + 1];
                }
                else
                {
                    p1 = gridBot[outer, inner];
                    p2 = gridBot[outer + 1, inner];
                    p3 = gridTop[outer + 1, inner];
                    p4 = gridTop[outer, inner];
                    np1 = gridBot[outer, inner + 1];
                    np2 = gridBot[outer + 1, inner + 1];
                    np3 = gridTop[outer + 1, inner + 1];
                    np4 = gridTop[outer, inner + 1];
                }

                bool leftEmpty = ax == 0 || GetAlpha(pixels, uvStartX + ax - 1, texY, imgW) <= 0.5f;
                bool rightEmpty = ax == regionW - 1 || GetAlpha(pixels, uvStartX + ax + 1, texY, imgW) <= 0.5f;
                bool topEmpty = ay == 0 || GetAlpha(pixels, texX, uvStartY + ay - 1, imgW) <= 0.5f;
                bool bottomEmpty = ay == regionH - 1 || GetAlpha(pixels, texX, uvStartY + ay + 1, imgW) <= 0.5f;

                bool wface = textureMirror ? rightEmpty : leftEmpty;
                bool eface = textureMirror ? leftEmpty : rightEmpty;
                bool aface = topEmpty;
                bool bface = bottomEmpty;

                if (bendAlongX)
                {
                    if (eface) AddSimpleQuad(vertices, normals, uvs, indices, np3, np4, np1, np2, pixUV, invert);
                    if (wface) AddSimpleQuad(vertices, normals, uvs, indices, p4, p3, p2, p1, pixUV, invert);
                    AddSimpleQuad(vertices, normals, uvs, indices, p3, np3, np2, p2, pixUV, invert);
                    AddSimpleQuad(vertices, normals, uvs, indices, np4, p4, p1, np1, pixUV, invert);
                    if (aface) AddSimpleQuad(vertices, normals, uvs, indices, p2, np2, np1, p1, pixUV, invert);
                    if (bface) AddSimpleQuad(vertices, normals, uvs, indices, p4, np4, np3, p3, pixUV, invert);
                }
                else
                {
                    if (eface) AddSimpleQuad(vertices, normals, uvs, indices, p3, p2, np2, np3, pixUV, invert);
                    if (wface) AddSimpleQuad(vertices, normals, uvs, indices, p1, p4, np4, np1, pixUV, invert);
                    AddSimpleQuad(vertices, normals, uvs, indices, p4, p3, np3, np4, pixUV, invert);
                    AddSimpleQuad(vertices, normals, uvs, indices, p2, p1, np1, np2, pixUV, invert);
                    if (aface) AddSimpleQuad(vertices, normals, uvs, indices, np4, np3, np2, np1, pixUV, invert);
                    if (bface) AddSimpleQuad(vertices, normals, uvs, indices, p1, p2, p3, p4, pixUV, invert);
                }
            }
        }

        if (vertices.Count == 0)
            return CreateBentPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
                textureMirror, invert, inflate, bend, shapePosition, shapeRotation, shapeScale, bendStyle);

        return BuildMesh(vertices, normals, uvs, indices);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Mesh building helpers
    // ─────────────────────────────────────────────────────────────────────────

    private Mesh BuildMesh(List<vec3> vertices, List<vec3> normals, List<vec2> uvs, List<uint> indices)
    {
        if (_gl == null || vertices.Count == 0) return null;

        var mesh = new Mesh(_gl);
        mesh.Vertices.AddRange(vertices);
        mesh.Normals.AddRange(normals);
        mesh.TexCoords.AddRange(uvs);
        mesh.Indices = indices.ToArray();
        mesh.Upload();
        return mesh;
    }

    private static mat4 BuildShapeRotMat(vec3 rot)
    {
        if (rot == default || rot == vec3.Zero) return mat4.Identity;
        // Mine-imator matrix_build(pos, rotX, rotY, rotZ, scale) applies X->Y->Z.
        // With column vectors, that corresponds to M = Rz * Ry * Rx.
        mat4 rx = mat4.RotateX(rot.x);
        mat4 ry = mat4.RotateY(rot.y);
        mat4 rz = mat4.RotateZ(rot.z);
        return rz * ry * rx;
    }

    private static vec3[]? GetShapeVertexOffsets(MiShape shape)
    {
        float[][]? offsets = new[]
        {
            shape.Vert1, shape.Vert2, shape.Vert3, shape.Vert4,
            shape.Vert5, shape.Vert6, shape.Vert7, shape.Vert8
        };

        bool hasAny = false;
        var result = new vec3[8];
        for (int i = 0; i < offsets.Length; i++)
        {
            var entry = offsets[i];
            if (entry == null || entry.Length < 3)
            {
                result[i] = vec3.Zero;
                continue;
            }

            result[i] = new vec3(entry[0] / 16f, entry[1] / 16f, entry[2] / 16f);
            if (result[i] != vec3.Zero)
                hasAny = true;
        }

        return hasAny ? result : null;
    }

    private static void FlipFaceVertically(ref vec2 uv1, ref vec2 uv2, ref vec2 uv3, ref vec2 uv4)
    {
        (uv1, uv4) = (uv4, uv1);
        (uv2, uv3) = (uv3, uv2);
    }

    // Matches Mine-imator's mod_fix(x, y): wrap negative values before modulo.
    private static float ModFix(float x, float y)
    {
        if (MathF.Abs(y) <= 1e-6f)
            return 0f;

        while (x < 0f)
            x += y;

        return x % y;
    }

    // Adds a face quad with uniform normal and per-vertex UVs
    private static void AddFaceWithUVs(List<vec3> verts, List<vec3> normals, List<vec2> uvs, List<uint> indices,
        vec3 v0, vec3 v1, vec3 v2, vec3 v3, vec3 normal,
        vec2 uv0, vec2 uv1, vec2 uv2, vec2 uv3, bool invert)
        => AddFaceWithUVs(verts, normals, uvs, indices, v0, v1, v2, v3, normal, normal, normal, normal, uv0, uv1, uv2,
            uv3, invert);

    private static void AddFaceWithUVs(List<vec3> verts, List<vec3> normals, List<vec2> uvs, List<uint> indices,
        vec3 v0, vec3 v1, vec3 v2, vec3 v3, vec3 n01, vec3 n23,
        vec2 uv0, vec2 uv1, vec2 uv2, vec2 uv3, bool invert)
        => AddFaceWithUVs(verts, normals, uvs, indices, v0, v1, v2, v3, n01, n01, n23, n23, uv0, uv1, uv2, uv3, invert);

    private static void AddFaceWithUVs(List<vec3> verts, List<vec3> normals, List<vec2> uvs, List<uint> indices,
        vec3 v0, vec3 v1, vec3 v2, vec3 v3,
        vec3 n0, vec3 n1, vec3 n2, vec3 n3,
        vec2 uv0, vec2 uv1, vec2 uv2, vec2 uv3, bool invert)
    {
        // A zero-normal quad is the sharp-bend equivalent of Modelbench's
        // null normal arguments. Emit separate triangles so even a slightly
        // non-planar corrected quad retains a genuinely hard edge.
        if (n0.LengthSqr < 1e-10f && n1.LengthSqr < 1e-10f &&
            n2.LengthSqr < 1e-10f && n3.LengthSqr < 1e-10f)
        {
            AddFlatTriangle(verts, normals, uvs, indices, v0, v1, v2, uv0, uv1, uv2, invert);
            AddFlatTriangle(verts, normals, uvs, indices, v0, v2, v3, uv0, uv2, uv3, invert);
            return;
        }

        uint bv = (uint)verts.Count;
        verts.Add(v0);
        verts.Add(v1);
        verts.Add(v2);
        verts.Add(v3);
        normals.Add(invert ? -n0 : n0);
        normals.Add(invert ? -n1 : n1);
        normals.Add(invert ? -n2 : n2);
        normals.Add(invert ? -n3 : n3);

        // In Mine-imator, invert changes face orientation only. UVs remain
        // attached to their original vertices.
        uvs.Add(uv0);
        uvs.Add(uv1);
        uvs.Add(uv2);
        uvs.Add(uv3);
        AddQuadIndices(indices, bv, invert);
    }

    private static void AddFlatTriangle(List<vec3> verts, List<vec3> normals, List<vec2> uvs,
        List<uint> indices, vec3 v0, vec3 v1, vec3 v2, vec2 uv0, vec2 uv1, vec2 uv2, bool invert)
    {
        if (invert)
        {
            (v0, v1) = (v1, v0);
            (uv0, uv1) = (uv1, uv0);
        }

        vec3 normal = CalculateFaceNormal(v0, v1, v2);
        uint baseVertex = (uint)verts.Count;
        verts.Add(v0);
        verts.Add(v1);
        verts.Add(v2);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        uvs.Add(uv0);
        uvs.Add(uv1);
        uvs.Add(uv2);
        indices.Add(baseVertex);
        indices.Add(baseVertex + 1);
        indices.Add(baseVertex + 2);
    }

    private static void AddQuadIndices(List<uint> indices, uint baseVertex, bool invert)
    {
        if (invert)
        {
            indices.Add(baseVertex + 0);
            indices.Add(baseVertex + 2);
            indices.Add(baseVertex + 1);
            indices.Add(baseVertex + 0);
            indices.Add(baseVertex + 3);
            indices.Add(baseVertex + 2);
        }
        else
        {
            indices.Add(baseVertex + 0);
            indices.Add(baseVertex + 1);
            indices.Add(baseVertex + 2);
            indices.Add(baseVertex + 0);
            indices.Add(baseVertex + 2);
            indices.Add(baseVertex + 3);
        }
    }

    private static void AddSimpleQuad(List<vec3> verts, List<vec3> normals, List<vec2> uvs, List<uint> indices,
        vec3 v0, vec3 v1, vec3 v2, vec3 v3, vec2 uv, bool invert)
    {
        var normal = CalculateFaceNormal(v0, v1, v2);
        if (invert) normal = -normal;

        uint bv = (uint)verts.Count;
        verts.Add(v0);
        verts.Add(v1);
        verts.Add(v2);
        verts.Add(v3);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        uvs.Add(uv);
        uvs.Add(uv);
        uvs.Add(uv);
        uvs.Add(uv);

        AddQuadIndices(indices, bv, invert);
    }

    private static vec3 CalculateFaceNormal(vec3 v0, vec3 v1, vec3 v2)
    {
        vec3 normal = vec3.Cross(v1 - v0, v2 - v0);
        return normal.LengthSqr < 1e-10f ? vec3.UnitY : normal.Normalized;
    }

    private static void AddExtrudedQuad(List<vec3> verts, List<vec3> normals, List<vec2> uvs, List<uint> indices,
        uint baseVertex, vec3 v0, vec3 v1, vec3 v2, vec3 v3, float uvX, float uvY, bool invert)
    {
        verts.Add(v0);
        verts.Add(v1);
        verts.Add(v2);
        verts.Add(v3);
        // Positions already contain the plane's shape rotation and scale. Match
        // the generated normal to the actual (0, 1, 2) triangle winding instead
        // of relying on the pre-transform axis normal supplied by the caller.
        vec3 normal = CalculateFaceNormal(v0, v1, v2);
        normal = invert ? -normal : normal;
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        var uv = new vec2(uvX, uvY);
        uvs.Add(uv);
        uvs.Add(uv);
        uvs.Add(uv);
        uvs.Add(uv);
        AddQuadIndices(indices, baseVertex, invert);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Pixel helpers (reads back texture pixels from GPU for extruded meshes)
    // ─────────────────────────────────────────────────────────────────────────

    // Cache for readback pixel data: textureId → (pixels, width, height)
    private readonly Dictionary<uint, (byte[] pixels, int w, int h)> _pixelCache = new();

    private byte[]? TryGetPixels(uint textureId, int texWidth, int texHeight, out int imgW, out int imgH)
    {
        if (_pixelCache.TryGetValue(textureId, out var cached))
        {
            imgW = cached.w;
            imgH = cached.h;
            return cached.pixels;
        }

        if (_gl == null)
        {
            imgW = imgH = 0;
            return null;
        }

        try
        {
            // We'll use the expected texWidth/texHeight since we know them from the model
            imgW = texWidth;
            imgH = texHeight;
            int size = texWidth * texHeight * 4;
            byte[] pixels = new byte[size];

            _gl.BindTexture(GLEnum.Texture2D, textureId);
            unsafe
            {
                fixed (byte* p = pixels)
                    _gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
            }

            _gl.BindTexture(GLEnum.Texture2D, 0);

            _pixelCache[textureId] = (pixels, texWidth, texHeight);
            return pixels;
        }
        catch
        {
            imgW = imgH = 0;
            return null;
        }
    }

    private static float GetAlpha(byte[] pixels, int x, int y, int width)
    {
        int idx = (y * width + x) * 4 + 3;
        if (idx < 0 || idx >= pixels.Length) return 0f;
        return pixels[idx] / 255f;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  Data classes
// ═════════════════════════════════════════════════════════════════════════════

#region Mine Imator Data Classes

public class MiModel
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; }
    [JsonPropertyName("texture")] public string Texture { get; set; }
    [JsonPropertyName("texture_material")] public string TextureMaterial { get; set; }
    [JsonPropertyName("texture_normal")] public string TextureNormal { get; set; }
    [JsonPropertyName("texture_size")] public int[] TextureSize { get; set; }
    [JsonPropertyName("textures")] public Dictionary<string, string> Textures { get; set; }
    [JsonPropertyName("parts")] public List<MiPart> Parts { get; set; }
    [JsonPropertyName("player_skin")] [JsonConverter(typeof(MiNullableBoolConverter))] public bool? PlayerSkin { get; set; }
    [JsonPropertyName("floor_box_uvs")] [JsonConverter(typeof(MiNullableBoolConverter))] public bool? FloorBoxUvs { get; set; }
    [JsonPropertyName("model_color")] public string ModelColor { get; set; }
    [JsonPropertyName("scale")] public float[] Scale { get; set; }

    [JsonIgnore] public string DirectoryPath { get; set; }
    [JsonIgnore] public string FullPath { get; set; }
    [JsonIgnore] public Dictionary<string, uint> LoadedTextures { get; set; } = new();

    public uint GetTexture(string textureName = null)
    {
        if (LoadedTextures == null || LoadedTextures.Count == 0) return 0;
        if (string.IsNullOrEmpty(textureName))
        {
            if (LoadedTextures.TryGetValue("texture", out uint t2)) return t2;
            if (LoadedTextures.TryGetValue("skin", out uint t1)) return t1;
            return 0;
        }

        if (LoadedTextures.TryGetValue(textureName, out uint t3)) return t3;
        if (LoadedTextures.TryGetValue("texture", out uint t4)) return t4;
        return 0;
    }
}

public class MiPart
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("visible")] [JsonConverter(typeof(MiBoolConverter))] public bool Visible { get; set; } = true;
    [JsonPropertyName("description")] public string Description { get; set; }
    [JsonPropertyName("texture")] public string Texture { get; set; }
    [JsonPropertyName("texture_material")] public string TextureMaterial { get; set; }
    [JsonPropertyName("texture_normal")] public string TextureNormal { get; set; }
    [JsonPropertyName("texture_size")] public int[] TextureSize { get; set; }

    [JsonPropertyName("texture_scroll_speed")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float? TextureScrollSpeed { get; set; }

    [JsonPropertyName("texture_scroll_direction")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float? TextureScrollDirection { get; set; }

    [JsonPropertyName("textures")] public Dictionary<string, string> Textures { get; set; }
    [JsonPropertyName("position")] public float[] Position { get; set; }
    [JsonPropertyName("rotation")] public float[] Rotation { get; set; }
    [JsonPropertyName("scale")] public float[] Scale { get; set; }
    [JsonPropertyName("bend")] public MiBend Bend { get; set; }

    [JsonPropertyName("lock_bend")]
    [JsonConverter(typeof(MiBoolOrNumberConverter))]
    public float? LockBend { get; set; }

    [JsonPropertyName("locked")] [JsonConverter(typeof(MiBoolConverter))] public bool Locked { get; set; }
    [JsonPropertyName("depth")] public float Depth { get; set; }
    [JsonPropertyName("backfaces")] [JsonConverter(typeof(MiBoolConverter))] public bool Backfaces { get; set; }
    [JsonPropertyName("shadows")] [JsonConverter(typeof(MiBoolConverter))] public bool Shadows { get; set; } = true;

    [JsonPropertyName("color_alpha")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float? ColorAlpha { get; set; }

    [JsonPropertyName("color_blend")] public string? ColorBlend { get; set; }

    [JsonPropertyName("shapes")] public List<MiShape> Shapes { get; set; }
    [JsonPropertyName("parts")] public List<MiPart> Parts { get; set; }

    [JsonIgnore] public Dictionary<string, uint> LoadedTextures { get; set; } = new();
}

public class MiShape
{
    [JsonPropertyName("visible")] [JsonConverter(typeof(MiBoolConverter))] public bool Visible { get; set; } = true;
    [JsonPropertyName("type")] public string Type { get; set; } = "block";
    [JsonPropertyName("description")] public string Description { get; set; }
    [JsonPropertyName("use_model_color")] [JsonConverter(typeof(MiBoolConverter))] public bool UseModelColor { get; set; }

    [JsonPropertyName("color_alpha")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float? ColorAlpha { get; set; }

    [JsonPropertyName("color_blend")] public string? ColorBlend { get; set; }
    [JsonPropertyName("from")] public float[] From { get; set; }
    [JsonPropertyName("to")] public float[] To { get; set; }
    [JsonPropertyName("uv")] public float[] Uv { get; set; }
    [JsonPropertyName("position")] public float[] Position { get; set; }
    [JsonPropertyName("rotation")] public float[] Rotation { get; set; }
    [JsonPropertyName("scale")] public float[] Scale { get; set; }
    [JsonPropertyName("invert")] [JsonConverter(typeof(MiBoolConverter))] public bool Invert { get; set; }
    [JsonPropertyName("texture_mirror")] [JsonConverter(typeof(MiBoolConverter))] public bool TextureMirror { get; set; }
    [JsonPropertyName("texture_mirror_y")] [JsonConverter(typeof(MiBoolConverter))] public bool TextureMirrorY { get; set; }
    [JsonPropertyName("texture")] public string Texture { get; set; }
    [JsonPropertyName("texture_material")] public string TextureMaterial { get; set; }
    [JsonPropertyName("texture_normal")] public string TextureNormal { get; set; }
    [JsonPropertyName("texture_size")] public int[]? TextureSize { get; set; }

    [JsonPropertyName("texture_scroll_speed")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float? TextureScrollSpeed { get; set; }

    [JsonPropertyName("texture_scroll_direction")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float? TextureScrollDirection { get; set; }

    [JsonPropertyName("3d")] [JsonConverter(typeof(MiBoolConverter))] public bool ThreeD { get; set; }
    [JsonPropertyName("inflate")] public float Inflate { get; set; }
    [JsonPropertyName("bend")] [JsonConverter(typeof(MiBoolConverter))] public bool Bend { get; set; } = true;
    [JsonPropertyName("hide_front")] [JsonConverter(typeof(MiBoolConverter))] public bool HideFront { get; set; }
    [JsonPropertyName("hide_back")] [JsonConverter(typeof(MiBoolConverter))] public bool HideBack { get; set; }
    [JsonPropertyName("hide_backface")] [JsonConverter(typeof(MiNullableBoolConverter))] public bool? HideBackfaceLegacy { get; set; }
    [JsonPropertyName("face_camera")] [JsonConverter(typeof(MiBoolConverter))] public bool FaceCamera { get; set; }
    [JsonPropertyName("item_bounce")] [JsonConverter(typeof(MiBoolConverter))] public bool ItemBounce { get; set; }
    [JsonPropertyName("locked")] [JsonConverter(typeof(MiBoolConverter))] public bool Locked { get; set; }
    [JsonPropertyName("move_required")] public float[]? MoveRequired { get; set; }
    [JsonPropertyName("vert1")] public float[]? Vert1 { get; set; }
    [JsonPropertyName("vert2")] public float[]? Vert2 { get; set; }
    [JsonPropertyName("vert3")] public float[]? Vert3 { get; set; }
    [JsonPropertyName("vert4")] public float[]? Vert4 { get; set; }
    [JsonPropertyName("vert5")] public float[]? Vert5 { get; set; }
    [JsonPropertyName("vert6")] public float[]? Vert6 { get; set; }
    [JsonPropertyName("vert7")] public float[]? Vert7 { get; set; }
    [JsonPropertyName("vert8")] public float[]? Vert8 { get; set; }
}

public class MiBend
{
    [JsonPropertyName("offset")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float? Offset { get; set; }

    [JsonPropertyName("size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float? Size { get; set; }

    [JsonPropertyName("detail")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float? Detail { get; set; }

    [JsonPropertyName("inherit_bend")]
    [JsonConverter(typeof(MiBoolOrNumberConverter))]
    public float? InheritBend { get; set; }

    [JsonPropertyName("end_offset")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float? EndOffset { get; set; }

    [JsonPropertyName("part")] public string Part { get; set; }
    [JsonPropertyName("axis")] public object Axis { get; set; }

    [JsonPropertyName("direction")]
    [JsonConverter(typeof(MiStringOrArrayStringConverter))]
    public string[] Direction { get; set; }

    [JsonPropertyName("direction_min")]
    [JsonConverter(typeof(MiSingleOrArrayConverter))]
    public float[] DirectionMin { get; set; }

    [JsonPropertyName("direction_max")]
    [JsonConverter(typeof(MiSingleOrArrayConverter))]
    public float[] DirectionMax { get; set; }

    [JsonPropertyName("angle")]
    [JsonConverter(typeof(MiSingleOrArrayConverter))]
    public float[] Angle { get; set; }

    [JsonPropertyName("invert")]
    [JsonConverter(typeof(MiSingleOrArrayBoolConverter))]
    public bool[] Invert { get; set; }
}

public class MiObject
{
    [JsonPropertyName("format")] public int Format { get; set; }
    [JsonPropertyName("created_in")] public string CreatedIn { get; set; }
    [JsonPropertyName("templates")] public List<MiTemplate> Templates { get; set; }
    [JsonPropertyName("timelines")] public List<MiTimeline> Timelines { get; set; }
    [JsonPropertyName("resources")] public List<MiResource> Resources { get; set; }

    [JsonIgnore] public string DirectoryPath { get; set; }
    [JsonIgnore] public string FullPath { get; set; }
}

public class MiTemplate
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("model")] public string Model { get; set; }
    [JsonPropertyName("model_tex")] public string ModelTex { get; set; }
    [JsonPropertyName("model_tex_material")] public string ModelTexMaterial { get; set; }
    [JsonPropertyName("model_tex_normal")] public string ModelTexNormal { get; set; }
    [JsonPropertyName("item")] public MiTemplateItem? Item { get; set; }
}

public class MiTemplateItem
{
    [JsonPropertyName("tex")] public string Tex { get; set; }
    [JsonPropertyName("slot")] public int? Slot { get; set; }
    [JsonPropertyName("3d")] [JsonConverter(typeof(MiBoolConverter))] public bool ThreeD { get; set; }
    [JsonPropertyName("face_camera")] [JsonConverter(typeof(MiBoolConverter))] public bool FaceCamera { get; set; }
}

public class MiTimeline
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("temp")] public string Temp { get; set; }
    [JsonPropertyName("hide")] [JsonConverter(typeof(MiBoolConverter))] public bool Hide { get; set; }
    [JsonPropertyName("depth")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float Depth { get; set; }
    [JsonPropertyName("parent")] public string Parent { get; set; }
    [JsonPropertyName("part_of")] public string PartOf { get; set; }
    [JsonPropertyName("model_part_name")] public string ModelPartName { get; set; }
    [JsonPropertyName("lock_bend")]
    [JsonConverter(typeof(MiBoolOrNumberConverter))]
    public float? LockBend { get; set; }
    [JsonPropertyName("inherit")] public MiInherit? Inherit { get; set; }
    [JsonPropertyName("rot_point_custom")] [JsonConverter(typeof(MiBoolConverter))] public bool RotPointCustom { get; set; }
    [JsonPropertyName("rot_point")] public float[]? RotPoint { get; set; }
    [JsonPropertyName("backfaces")] [JsonConverter(typeof(MiBoolConverter))] public bool Backfaces { get; set; }
    [JsonPropertyName("texture_blur")] [JsonConverter(typeof(MiNullableBoolConverter))] public bool? TextureBlur { get; set; }
    [JsonPropertyName("texture_filtering")] [JsonConverter(typeof(MiNullableBoolConverter))] public bool? TextureFiltering { get; set; }
    [JsonPropertyName("shadows")] [JsonConverter(typeof(MiNullableBoolConverter))] public bool? Shadows { get; set; }
    [JsonPropertyName("ssao")] [JsonConverter(typeof(MiNullableBoolConverter))] public bool? Ssao { get; set; }
    [JsonPropertyName("fog")] [JsonConverter(typeof(MiNullableBoolConverter))] public bool? Fog { get; set; }
    [JsonPropertyName("hq_hiding")] [JsonConverter(typeof(MiNullableBoolConverter))] public bool? HqHiding { get; set; }
    [JsonPropertyName("lq_hiding")] [JsonConverter(typeof(MiNullableBoolConverter))] public bool? LqHiding { get; set; }
    [JsonPropertyName("default_values")] public MiKeyValueMap? DefaultValues { get; set; }
    [JsonPropertyName("position")] public float[] Position { get; set; }
    [JsonPropertyName("rotation")] public float[] Rotation { get; set; }
    [JsonPropertyName("scale")] public float[] Scale { get; set; }
    [JsonPropertyName("keyframes")] public Dictionary<string, MiKeyframe> Keyframes { get; set; }
}

public class MiKeyframe
{
    [JsonPropertyName("position")] public float[] Position { get; set; }
    [JsonPropertyName("POS_X")] public float? PosX { get; set; }
    [JsonPropertyName("POS_Y")] public float? PosY { get; set; }
    [JsonPropertyName("POS_Z")] public float? PosZ { get; set; }
    [JsonPropertyName("rotation")] public float[] Rotation { get; set; }
    [JsonPropertyName("ROT_X")] public float? RotX { get; set; }
    [JsonPropertyName("ROT_Y")] public float? RotY { get; set; }
    [JsonPropertyName("ROT_Z")] public float? RotZ { get; set; }
    [JsonPropertyName("scale")] public float[] Scale { get; set; }
    [JsonPropertyName("SCA_X")] public float? ScaX { get; set; }
    [JsonPropertyName("SCA_Y")] public float? ScaY { get; set; }
    [JsonPropertyName("SCA_Z")] public float? ScaZ { get; set; }
    [JsonPropertyName("VISIBLE")]
    [JsonConverter(typeof(MiNullableBoolConverter))]
    public bool? Visible { get; set; }
    [JsonPropertyName("TEXTURE_OBJ")] public string? TextureObj { get; set; }
    [JsonPropertyName("MIX_COLOR")] public string? MixColor { get; set; }
    [JsonPropertyName("MIX_PERCENT")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float? MixPercent { get; set; }
    [JsonPropertyName("ALPHA")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float? Alpha { get; set; }
    [JsonPropertyName("ITEM_SLOT")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public int? ItemSlot { get; set; }
    [JsonPropertyName("CUSTOM_ITEM_SLOT")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public int? CustomItemSlot { get; set; }

    public float[] GetPosition()
    {
        if (Position is { Length: >= 3 }) return Position;
        if (PosX.HasValue || PosY.HasValue || PosZ.HasValue)
            // Mine-imator keyframe channels are authored in Z-up project space.
            // Convert to this engine's Y-up coordinates by swapping Y/Z.
            return new[] { PosX ?? 0, PosZ ?? 0, PosY ?? 0 };
        return null;
    }

    public float[] GetRotation()
    {
        if (Rotation is { Length: >= 3 }) return Rotation;
        if (RotX.HasValue || RotY.HasValue || RotZ.HasValue)
            return new[] { RotX ?? 0, RotY ?? 0, RotZ ?? 0 };
        return null;
    }

    public float[] GetScale()
    {
        if (Scale is { Length: >= 3 }) return Scale;
        if (ScaX.HasValue || ScaY.HasValue || ScaZ.HasValue)
            return new[] { ScaX ?? 1, ScaY ?? 1, ScaZ ?? 1 };
        return null;
    }
}

public class MiResource
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("filename")] public string Filename { get; set; }
    [JsonPropertyName("item_sheet_size")] public int[]? ItemSheetSize { get; set; }
}

public class MiInherit
{
    [JsonPropertyName("position")] [JsonConverter(typeof(MiNullableBoolConverter))] public bool? Position { get; set; }
    [JsonPropertyName("rotation")] [JsonConverter(typeof(MiNullableBoolConverter))] public bool? Rotation { get; set; }
    [JsonPropertyName("scale")] [JsonConverter(typeof(MiNullableBoolConverter))] public bool? Scale { get; set; }
    [JsonPropertyName("alpha")] [JsonConverter(typeof(MiNullableBoolConverter))] public bool? Alpha { get; set; }
    [JsonPropertyName("visibility")] [JsonConverter(typeof(MiNullableBoolConverter))] public bool? Visibility { get; set; }
    [JsonPropertyName("rot_point")] [JsonConverter(typeof(MiNullableBoolConverter))] public bool? RotPoint { get; set; }
}

public class MiKeyValueMap
{
    [JsonExtensionData] public Dictionary<string, JsonElement> Values { get; set; } = new();

    public bool TryGetString(string key, out string value)
    {
        value = string.Empty;
        if (!Values.TryGetValue(key, out var element))
            return false;

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                value = element.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            case JsonValueKind.Number:
                value = element.ToString();
                return !string.IsNullOrWhiteSpace(value);
            case JsonValueKind.True:
                value = "1";
                return true;
            case JsonValueKind.False:
                value = "0";
                return true;
            default:
                return false;
        }
    }

    public bool TryGetValue(string key, out float value)
    {
        value = 0f;
        if (!Values.TryGetValue(key, out var element))
            return false;

        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetSingle(out value);
            case JsonValueKind.True:
                value = 1f;
                return true;
            case JsonValueKind.False:
                value = 0f;
                return true;
            case JsonValueKind.String:
                return float.TryParse(element.GetString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value);
            default:
                return false;
        }
    }
}

// ── JSON converters ───────────────────────────────────────────────────────────

/// <summary>Mine-imator stores several flags as either booleans or 0/1 numbers.</summary>
public sealed class MiBoolOrNumberConverter : JsonConverter<float?>
{
    public override float? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => 1f,
            JsonTokenType.False => 0f,
            JsonTokenType.Number => reader.GetSingle(),
            JsonTokenType.String when float.TryParse(reader.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float value) => value,
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for boolean/number")
        };
    }

    public override void Write(Utf8JsonWriter writer, float? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}

public class MiSingleOrArrayConverter : JsonConverter<float[]>
{
    public override float[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number: return new[] { reader.GetSingle() };
            case JsonTokenType.StartArray:
            {
                var list = new List<float>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray) break;
                    if (reader.TokenType == JsonTokenType.Number) list.Add(reader.GetSingle());
                }

                return list.ToArray();
            }
            case JsonTokenType.Null: return null;
            default: throw new JsonException($"Unexpected token {reader.TokenType}");
        }
    }

    public override void Write(Utf8JsonWriter writer, float[] value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.Length == 1)
        {
            writer.WriteNumberValue(value[0]);
            return;
        }

        writer.WriteStartArray();
        foreach (var v in value) writer.WriteNumberValue(v);
        writer.WriteEndArray();
    }
}

public class MiSingleOrArrayBoolConverter : JsonConverter<bool[]>
{
    public override bool[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
            case JsonTokenType.False: return new[] { reader.GetBoolean() };
            case JsonTokenType.Number: return new[] { reader.GetInt32() != 0 };
            case JsonTokenType.StartArray:
            {
                var list = new List<bool>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray) break;
                    if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
                        list.Add(reader.GetBoolean());
                    else if (reader.TokenType == JsonTokenType.Number)
                        list.Add(reader.GetInt32() != 0);
                }

                return list.ToArray();
            }
            case JsonTokenType.Null: return null;
            default: throw new JsonException($"Unexpected token {reader.TokenType}");
        }
    }

    public override void Write(Utf8JsonWriter writer, bool[] value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.Length == 1)
        {
            writer.WriteBooleanValue(value[0]);
            return;
        }

        writer.WriteStartArray();
        foreach (var v in value) writer.WriteBooleanValue(v);
        writer.WriteEndArray();
    }
}

public class MiStringOrArrayStringConverter : JsonConverter<string[]>
{
    public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return new[] { reader.GetString() ?? string.Empty };
            case JsonTokenType.StartArray:
            {
                var list = new List<string>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray) break;
                    if (reader.TokenType == JsonTokenType.String)
                        list.Add(reader.GetString() ?? string.Empty);
                }

                return list.ToArray();
            }
            case JsonTokenType.Null:
                return null;
            default:
                throw new JsonException($"Unexpected token {reader.TokenType}");
        }
    }

    public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.Length == 1)
        {
            writer.WriteStringValue(value[0]);
            return;
        }

        writer.WriteStartArray();
        foreach (var v in value) writer.WriteStringValue(v);
        writer.WriteEndArray();
    }
}

public class MiBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True: return true;
            case JsonTokenType.False: return false;
            case JsonTokenType.String:
                var s = reader.GetString()?.Trim().ToLowerInvariant();
                return s switch
                {
                    "true" or "1" or "yes" or "on" => true,
                    "false" or "0" or "no" or "off" or "" or null => false,
                    _ => throw new JsonException($"Cannot parse boolean from string \"{s}\"")
                };
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var l) ? l != 0 : reader.GetDouble() != 0.0;
            case JsonTokenType.Null: return false;
            default: throw new JsonException($"Unexpected token {reader.TokenType} for boolean");
        }
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}

public sealed class MiNullableBoolConverter : JsonConverter<bool?>
{
    private static readonly MiBoolConverter Inner = new();

    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : Inner.Read(ref reader, typeof(bool), options);

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteBooleanValue(value.Value);
        else writer.WriteNullValue();
    }
}

#endregion
