using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;
using StbImageSharp;
using System;
using System.Collections.Generic;
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

    private static MineImatorLoader _instance;
    public static MineImatorLoader Instance => _instance ??= new MineImatorLoader();

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

    private readonly Dictionary<string, MiModel>  _modelCache    = new();
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
                Console.Error.WriteLine($"[MineImatorLoader] Object file not found: {objectPath}");
                return null;
            }

            var miObject = JsonSerializer.Deserialize(File.ReadAllText(objectPath),
                AppJsonContext.Default.MiObject);

            if (miObject == null) return null;

            miObject.DirectoryPath = Path.GetDirectoryName(objectPath);
            miObject.FullPath      = objectPath;
            _miObjectCache[objectPath] = miObject;
            return miObject;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MineImatorLoader] Error loading '{objectPath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Creates a SceneObject hierarchy from a loaded MiObject.</summary>
    public SceneObject CreateSceneFromMiObject(MiObject miObject)
    {
        if (miObject == null) return null;

        var sceneRoot = new SceneObject { ObjectType = "MineImatorObject", Name = "MiObject_Scene" };

        var templateDict = new Dictionary<string, MiTemplate>();
        if (miObject.Templates != null)
            foreach (var t in miObject.Templates)
                if (!string.IsNullOrEmpty(t.Id)) templateDict[t.Id] = t;

        var resourceDict = new Dictionary<string, string>();
        if (miObject.Resources != null)
            foreach (var r in miObject.Resources.Where(r => !string.IsNullOrEmpty(r.Id) && !string.IsNullOrEmpty(r.Filename)))
                resourceDict[r.Id] = r.Filename;

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
                    string modelFilename = template.Model;
                    if (resourceDict.TryGetValue(template.Model, out var mapped))
                        modelFilename = mapped;

                    var modelPath = Path.Combine(miObject.DirectoryPath, modelFilename);
                    if (File.Exists(modelPath))
                    {
                        var miModel = LoadModel(modelPath);
                        if (miModel != null)
                        {
                            var character = CreateCharacterFromModel(miModel);
                            if (character != null)
                            {
                                character.Name = timeline.ModelPartName ?? timeline.Name ?? "Model";
                                itemObject = character;
                            }
                        }
                    }
                }

                itemObject ??= new SceneObject
                {
                    Name       = timeline.ModelPartName ?? timeline.Name ?? "Unknown",
                    ObjectType = "Placeholder"
                };

                ApplyTimelineTransform(itemObject, timeline);
                itemObject.ObjectVisible = !timeline.Hide;

                if (!string.IsNullOrEmpty(timeline.Id))
                    sceneObjectsByTimelineId[timeline.Id] = itemObject;
            }
        }

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
                Console.Error.WriteLine($"[MineImatorLoader] Model file not found: {modelPath}");
                return null;
            }

            var model = JsonSerializer.Deserialize(File.ReadAllText(modelPath),
                AppJsonContext.Default.MiModel);

            if (model == null) return null;

            model.DirectoryPath = Path.GetDirectoryName(modelPath);
            model.FullPath      = modelPath;
            _modelCache[modelPath] = model;
            return model;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MineImatorLoader] Error loading model '{modelPath}': {ex.Message}");
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
        FlattenPartsForBones(model.Parts, -1, vec3.Ones, boneDataList);

        var character = new CharacterSceneObject();
        character.Name        = model.Name ?? "MineImatorModel";
        character.ObjectType  = "MineImator";
        character.AssignObjectId();

        _currentCharacter = character;

        // First pass: create all MiBoneSceneObjects
        CreateBoneSceneObjects(character, boneDataList);

        // Second pass: create meshes per bone
        foreach (var (part, boneIdx, parentIdx, accumulatedParentScale) in boneDataList)
        {
            string boneName = part.Name ?? $"Bone_{boneIdx}";
            if (!character.BoneObjects.TryGetValue(boneName, out var boneObject))
                continue;

            vec3 partScale = vec3.Ones;
            if (part.Scale != null && part.Scale.Length >= 3)
                partScale = new vec3(part.Scale[0], part.Scale[1], part.Scale[2]);

            vec3 accumulatedScale = accumulatedParentScale * partScale;

            BendParams? bendParams = BendHelper.ParseBend(part.Bend,
                new float[] { accumulatedScale.x, accumulatedScale.y, accumulatedScale.z },
                _currentCharacter.ModelBendStyle);

            // Cast to MiBoneSceneObject to access Mine Imator–specific members
            var miBone = boneObject as MiBoneSceneObject;

            float lockBend = part.LockBend ?? 1f;
            miBone?.SetBendParameters(bendParams, lockBend);

            if (part.Shapes is { Count: > 0 })
            {
                int shapeIndex = 0;
                foreach (var shape in part.Shapes)
                {
                    uint shapeTexture = GetShapeTexture(shape, part, model);

                    float? colorAlpha = miBone?.ColorAlpha;
                    int    depth      = miBone?.Depth ?? 0;

                    var mesh = CreateShapeMesh(part.Name, shapeIndex, shape, model, shapeTexture,
                        accumulatedScale, bendParams, _currentCharacter.ModelBendStyle,
                        colorAlpha, depth);

                    if (mesh != null)
                    {
                        if (miBone != null) ApplyMaterialSettings(mesh, miBone, shapeTexture);
                        boneObject.AddMesh(mesh);
                        miBone?.RegisterShapeData(new BoneShapeData
                        {
                            PartName         = part.Name,
                            ShapeIndex       = shapeIndex,
                            Shape            = shape,
                            Model            = model,
                            TextureId        = shapeTexture,
                            AccumulatedScale  = accumulatedScale,
                            ModelBendStyle   = _currentCharacter.ModelBendStyle,
                            PartColorAlpha   = colorAlpha,
                            PartDepth        = depth
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
        BendStyle bendStyle = BendStyle.ProjectDefault, float? partColorAlpha = null, int depth = 0)
        => CreateShapeMesh(partName, shapeIndex, shape, model, textureId, accumulatedParentScale,
            bendParams, bendStyle, partColorAlpha, depth);

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
        if (timeline.Keyframes is { Count: > 0 })
        {
            float[] pos = timeline.Keyframes.Select(kf => kf.Value.GetPosition()).FirstOrDefault(p => p != null);
            if (pos != null)
                obj.SetLocalPosition(new vec3(pos[0] / 16f, pos[1] / 16f, pos[2] / 16f));
        }

        if (timeline.Keyframes != null && timeline.Keyframes.TryGetValue("0", out var kf2))
        {
            if (kf2.Rotation != null && kf2.Rotation.Length >= 3)
                obj.SetLocalRotation(new vec3(
                    BendHelper.DegToRad(kf2.Rotation[0]),
                    BendHelper.DegToRad(kf2.Rotation[1]),
                    BendHelper.DegToRad(kf2.Rotation[2])));

            if (kf2.Scale != null && kf2.Scale.Length >= 3)
                obj.SetLocalScale(new vec3(kf2.Scale[0], kf2.Scale[1], kf2.Scale[2]));
        }
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
            string boneName = part.Name ?? $"Bone_{boneIdx}";
            var boneObject = new MiBoneSceneObject
            {
                Name       = boneName,
                BoneName   = boneName,
                ObjectType = "Bone"
            };
            boneObject.AssignObjectId();
            // Build the octahedron indicator so the Viewport renders and picks it
            // the same way it does for Assimp-imported bones.
            boneObject.CreateIndicator(_gl);
            character.BoneObjects[boneName] = boneObject;
        }

        // Pass 2: build hierarchy, set transforms, inherit settings
        foreach (var (part, boneIdx, parentIdx, accumulatedParentScale) in boneDataList)
        {
            string boneName = part.Name ?? $"Bone_{boneIdx}";
            var boneObject  = character.BoneObjects[boneName];

            // Set transform from part data.
            // Convert pixels → blocks (÷16) only — do NOT multiply by accumulatedParentScale
            // here. In this project bones are plain SceneObjects in a parent–child hierarchy
            // whose world transform already incorporates parent scale via GetWorldMatrix().
            // (The Godot source multiplied by accumulatedParentScale because Skeleton3D stores
            // bone positions in a scale-stripped space; that doesn't apply here.)
            vec3 position = vec3.Zero;
            if (part.Position != null && part.Position.Length >= 3)
            {
                position = new vec3(
                    part.Position[0] / 16f,
                    part.Position[1] / 16f,
                    part.Position[2] / 16f
                );
            }

            vec3 rotation = vec3.Zero;
            if (part.Rotation != null && part.Rotation.Length >= 3)
            {
                rotation = new vec3(
                    BendHelper.DegToRad(part.Rotation[0]),
                    BendHelper.DegToRad(part.Rotation[1]),
                    BendHelper.DegToRad(part.Rotation[2])
                );
            }

            boneObject.SetLocalPosition(position);
            boneObject.SetLocalRotation(rotation);

            // Attach alpha/depth from part
            if (boneObject is MiBoneSceneObject mibone)
            {
                mibone.ColorAlpha = part.ColorAlpha;
                mibone.Depth      = part.Depth;
            }

            // Wire into hierarchy
            if (parentIdx >= 0)
            {
                string parentName = boneDataList[parentIdx].part.Name ?? $"Bone_{parentIdx}";
                if (character.BoneObjects.TryGetValue(parentName, out var parentBone))
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
                mib.InheritDepthFromParent();
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

        // Part-level texture
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
                if (part.LoadedTextures.TryGetValue("skin",    out uint t1)) return t1;
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

        model.LoadedTextures ??= new Dictionary<string, uint>();

        if (!string.IsNullOrEmpty(model.Texture))
        {
            var path = Path.Combine(model.DirectoryPath, model.Texture);
            if (File.Exists(path))
            {
                var t = LoadTextureFromFile(path);
                if (t != 0) model.LoadedTextures["texture"] = t;
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

        if (model.Parts != null) LoadPartTextures(model.Parts, model);
    }

    private void LoadPartTextures(List<MiPart> parts, MiModel model)
    {
        if (parts == null) return;
        foreach (var part in parts)
        {
            part.LoadedTextures ??= new Dictionary<string, uint>();

            if (!string.IsNullOrEmpty(part.Texture) && !part.LoadedTextures.ContainsKey("texture"))
            {
                var path = Path.Combine(model.DirectoryPath, part.Texture);
                if (File.Exists(path))
                {
                    var t = LoadTextureFromFile(path);
                    if (t != 0) part.LoadedTextures["texture"] = t;
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

            if (part.Parts is { Count: > 0 })
                LoadPartTextures(part.Parts, model);
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
            Console.Error.WriteLine($"[MineImatorLoader] Texture load error '{path}': {ex.Message}");
            return 0;
        }
    }

    private static void ApplyMaterialSettings(Mesh mesh, MiBoneSceneObject bone, uint textureId)
    {
        if (textureId != 0)
            mesh.TextureId = textureId;

        if (bone.ColorAlpha.HasValue)
            mesh.Alpha = bone.ColorAlpha.Value;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Mesh generation
    // ═════════════════════════════════════════════════════════════════════════

    private Mesh CreateShapeMesh(string partName, int shapeIndex, MiShape shape, MiModel model,
        uint textureId, vec3 accumulatedParentScale, BendParams? bendParams = null,
        BendStyle bendStyle = BendStyle.ProjectDefault, float? partColorAlpha = null, int depth = 0)
    {
        if (shape?.From == null || shape.To == null) return null;

        int texWidth  = model.TextureSize?[0] ?? 64;
        int texHeight = model.TextureSize?[1] ?? 64;

        float uvU = shape.Uv?[0] ?? 0;
        float uvV = shape.Uv?[1] ?? 0;

        vec3 from = new vec3(shape.From[0] / 16f, shape.From[1] / 16f, shape.From[2] / 16f);
        vec3 to   = new vec3(shape.To[0]   / 16f, shape.To[1]   / 16f, shape.To[2]   / 16f);

        float sizeX = Math.Abs(shape.To[0] - shape.From[0]);
        float sizeY = Math.Abs(shape.To[1] - shape.From[1]);
        float sizeZ = Math.Abs(shape.To[2] - shape.From[2]);

        vec3 shapePosition = vec3.Zero;
        if (shape.Position != null && shape.Position.Length >= 3)
            shapePosition = new vec3(shape.Position[0] / 16f, shape.Position[1] / 16f, shape.Position[2] / 16f);

        // The rotation pivot is the center of the shape's bounding box, not shapePosition.
        // shapePosition is a pure translation applied after rotation.
        vec3 shapePivot = (from + to) * 0.5f;

        vec3 shapeRotation = vec3.Zero;
        if (shape.Rotation != null && shape.Rotation.Length >= 3)
            shapeRotation = new vec3(
                BendHelper.DegToRad(shape.Rotation[0]),
                BendHelper.DegToRad(shape.Rotation[1]),
                BendHelper.DegToRad(shape.Rotation[2])
            );

        vec3 shapeScale = vec3.Ones;
        if (shape.Scale != null && shape.Scale.Length >= 3)
            shapeScale = new vec3(shape.Scale[0], shape.Scale[1], shape.Scale[2]);
        shapeScale *= accumulatedParentScale;

        float inflate = shape.Inflate / 16f;

        BendParams? effectiveBend = (shape.Bend && bendParams.HasValue) ? bendParams : null;

        bool planeBent = effectiveBend.HasValue &&
            (effectiveBend.Value.Angle.x != 0 || effectiveBend.Value.Angle.y != 0 || effectiveBend.Value.Angle.z != 0);

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
                        textureId, shape.TextureMirror, shape.Invert, inflate, shapeRotation, shapeScale,
                        shapePivot);
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
                    shape.HideFront, shape.HideBack, shapePivot);
            }
        }
        else
        {
            mesh = CreateBlockMesh(partName, shapeIndex, from, to, uvU, uvV, sizeX, sizeY, sizeZ,
                texWidth, texHeight, shape.TextureMirror, shape.Invert, inflate, effectiveBend,
                shapePosition, shapeRotation, shapeScale, bendStyle, shapePivot);
        }

        if (mesh != null)
        {
            if (textureId != 0) mesh.TextureId = textureId;
            if (partColorAlpha.HasValue) mesh.Alpha = partColorAlpha.Value;

            // Apply shapePosition as a pure translation (baked into vertices).
            // Rotation happens around shapePivot = (from+to)/2; shapePosition is separate.
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
        int texWidth, int texHeight, bool textureMirror, bool invert, float inflate = 0f,
        BendParams? bend = null, vec3 shapePosition = default, vec3 shapeRotation = default,
        vec3 shapeScale = default, BendStyle bendStyle = BendStyle.ProjectDefault,
        vec3 shapePivot = default)
    {
        var vertices = new List<vec3>();
        var normals  = new List<vec3>();
        var uvs      = new List<vec2>();
        var indices  = new List<uint>();

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
        float texSizeZ = sizeZ / texHeight;

        float texSizeFixX = (sizeX - 1f / 256f) / texWidth;
        float texSizeFixY = (sizeY - 1f / 256f) / texHeight;
        float texSizeFixZ = (sizeZ - 1f / 256f) / texHeight;

        // Face UV coords (see UV layout comment in original source)
        var texSouth1 = new vec2(texU,              texV);
        var texSouth2 = new vec2(texU + texSizeFixX, texV);
        var texSouth3 = new vec2(texU + texSizeFixX, texV + texSizeFixY);
        var texSouth4 = new vec2(texU,              texV + texSizeFixY);

        var texEast1 = new vec2(texU - texSizeZ,              texV);
        var texEast2 = new vec2(texU - texSizeZ + texSizeFixZ, texV);
        var texEast3 = new vec2(texU - texSizeZ + texSizeFixZ, texV + texSizeFixY);
        var texEast4 = new vec2(texU - texSizeZ,              texV + texSizeFixY);

        var texWest1 = new vec2(texU + texSizeZ,              texV);
        var texWest2 = new vec2(texU + texSizeZ + texSizeFixZ, texV);
        var texWest3 = new vec2(texU + texSizeZ + texSizeFixZ, texV + texSizeFixY);
        var texWest4 = new vec2(texU + texSizeZ,              texV + texSizeFixY);

        var texNorth1 = new vec2(texU + texSizeZ + texSizeX,              texV);
        var texNorth2 = new vec2(texU + texSizeZ + texSizeX + texSizeFixX, texV);
        var texNorth3 = new vec2(texU + texSizeZ + texSizeX + texSizeFixX, texV + texSizeFixY);
        var texNorth4 = new vec2(texU + texSizeZ + texSizeX,              texV + texSizeFixY);

        // Flip East and West face UVs horizontally
        (texEast1, texEast2) = (texEast2, texEast1);
        (texEast3, texEast4) = (texEast4, texEast3);
        (texWest1, texWest2) = (texWest2, texWest1);
        (texWest3, texWest4) = (texWest4, texWest3);

        float texUpHeight    = Math.Min(sizeY, sizeZ);
        float texUpHeightFix = (texUpHeight - 1f / 256f) / texHeight;
        var texUp1 = new vec2(texU,              texV - texUpHeightFix);
        var texUp2 = new vec2(texU + texSizeFixX, texV - texUpHeightFix);
        var texUp3 = new vec2(texU + texSizeFixX, texV - texUpHeightFix + texUpHeightFix);
        var texUp4 = new vec2(texU,              texV - texUpHeightFix + texUpHeightFix);

        var texDown4 = new vec2(texU + texSizeX,              texV - texUpHeightFix);
        var texDown3 = new vec2(texU + texSizeX + texSizeFixX, texV - texUpHeightFix);
        var texDown2 = new vec2(texU + texSizeX + texSizeFixX, texV - texUpHeightFix + texUpHeightFix);
        var texDown1 = new vec2(texU + texSizeX,              texV - texUpHeightFix + texUpHeightFix);

        if (textureMirror)
        {
            (texEast1, texWest1) = (texWest1, texEast1);
            (texEast2, texWest2) = (texWest2, texEast2);
            (texEast3, texWest3) = (texWest3, texEast3);
            (texEast4, texWest4) = (texWest4, texEast4);
            (texEast1, texEast2) = (texEast2, texEast1); (texEast3, texEast4) = (texEast4, texEast3);
            (texWest1, texWest2) = (texWest2, texWest1); (texWest3, texWest4) = (texWest4, texWest3);
            (texSouth1, texSouth2) = (texSouth2, texSouth1); (texSouth3, texSouth4) = (texSouth4, texSouth3);
            (texNorth1, texNorth2) = (texNorth2, texNorth1); (texNorth3, texNorth4) = (texNorth4, texNorth3);
            (texUp1, texUp2) = (texUp2, texUp1); (texUp3, texUp4) = (texUp4, texUp3);
            (texDown1, texDown2) = (texDown2, texDown1); (texDown3, texDown4) = (texDown4, texDown3);
        }

        bool isBent = bend.HasValue &&
            (bend.Value.Angle.x != 0 || bend.Value.Angle.y != 0 || bend.Value.Angle.z != 0);

        if (!isBent)
        {
            // Build shape rotation + scale transforms
            mat4 shapeRotMat   = BuildShapeRotMat(shapeRotation);
            mat4 shapeScaleMat = shapeScale != default && shapeScale != vec3.Ones
                ? mat4.Scale(shapeScale) : mat4.Identity;

            // Rotate/scale around the box centre (shapePivot = (from+to)/2).
            // shapePosition is a pure translation baked in after the mesh is built.
            vec3 pivot = shapePivot != default ? shapePivot : (min + max) * 0.5f;
            vec3 Rv(vec3 v) => BendHelper.TransformPoint(shapeRotMat * shapeScaleMat,
                v - pivot) + pivot;
            vec3 Rn(vec3 n) => BendHelper.TransformDirection(shapeRotMat * shapeScaleMat, n);

            float x1 = min.x, x2 = max.x, y1 = min.y, y2 = max.y, z1 = min.z, z2 = max.z;

            AddFaceWithUVs(vertices, normals, uvs, indices,
                Rv(new vec3(x1,y1,z2)), Rv(new vec3(x2,y1,z2)),
                Rv(new vec3(x2,y2,z2)), Rv(new vec3(x1,y2,z2)),
                Rn(new vec3(0,0,1)), texSouth4, texSouth3, texSouth2, texSouth1, invert);

            AddFaceWithUVs(vertices, normals, uvs, indices,
                Rv(new vec3(x2,y1,z2)), Rv(new vec3(x2,y1,z1)),
                Rv(new vec3(x2,y2,z1)), Rv(new vec3(x2,y2,z2)),
                Rn(new vec3(1,0,0)), texEast4, texEast3, texEast2, texEast1, invert);

            AddFaceWithUVs(vertices, normals, uvs, indices,
                Rv(new vec3(x1,y1,z1)), Rv(new vec3(x1,y1,z2)),
                Rv(new vec3(x1,y2,z2)), Rv(new vec3(x1,y2,z1)),
                Rn(new vec3(-1,0,0)), texWest4, texWest3, texWest2, texWest1, invert);

            AddFaceWithUVs(vertices, normals, uvs, indices,
                Rv(new vec3(x2,y1,z1)), Rv(new vec3(x1,y1,z1)),
                Rv(new vec3(x1,y2,z1)), Rv(new vec3(x2,y2,z1)),
                Rn(new vec3(0,0,-1)), texNorth4, texNorth3, texNorth2, texNorth1, invert);

            AddFaceWithUVs(vertices, normals, uvs, indices,
                Rv(new vec3(x1,y2,z2)), Rv(new vec3(x2,y2,z2)),
                Rv(new vec3(x2,y2,z1)), Rv(new vec3(x1,y2,z1)),
                Rn(new vec3(0,1,0)), texUp4, texUp3, texUp2, texUp1, invert);

            AddFaceWithUVs(vertices, normals, uvs, indices,
                Rv(new vec3(x1,y1,z1)), Rv(new vec3(x2,y1,z1)),
                Rv(new vec3(x2,y1,z2)), Rv(new vec3(x1,y1,z2)),
                Rn(new vec3(0,-1,0)), texDown4, texDown3, texDown2, texDown1, invert);
        }
        else
        {
            // Bent block — segmented geometry matching Modelbench's algorithm
            var b = bend.Value;

            int segAxis;
            switch (b.Part)
            {
                case BendPart.Right: case BendPart.Left:   segAxis = 0; break;
                case BendPart.Upper: case BendPart.Lower:  segAxis = 1; break;
                default:                                    segAxis = 2; break;
            }

            float x1 = min.x, x2 = max.x, y1 = min.y, y2 = max.y, z1 = min.z, z2 = max.z;
            float bendSize   = b.BendSize   / 16f;
            float bendOffset = b.BendOffset / 16f;

            BendStyle effectiveStyle = (bendStyle == BendStyle.ProjectDefault)
                ? ProjectBendStyle : bendStyle;

            bool singleXorZ = (b.AxisX && !b.AxisY && !b.AxisZ) || (!b.AxisX && !b.AxisY && b.AxisZ);
            bool sharpBend  = (effectiveStyle == BendStyle.Blocky) && !b.ExplicitBendSize && singleXorZ;

            float detail = BendHelper.CalculateSegmentCount(b.BendSize, sharpBend, b.Detail);
            if (b.ExplicitBendSize && b.BendSize >= 1 && shapeScale[segAxis] > 0.5f)
                detail /= shapeScale[segAxis];

            float segSize = bendSize / detail;

            bool invAngle = (b.Part == BendPart.Lower || b.Part == BendPart.Back || b.Part == BendPart.Left);

            float bendStart, bendEnd;
            switch (segAxis)
            {
                case 0:  bendStart = bendOffset - (shapePosition.x + x1) - bendSize/2f;
                         bendEnd   = bendOffset - (shapePosition.x + x1) + bendSize/2f; break;
                case 1:  bendStart = bendOffset - (shapePosition.y + y1) - bendSize/2f;
                         bendEnd   = bendOffset - (shapePosition.y + y1) + bendSize/2f; break;
                default: bendStart = bendOffset - (shapePosition.z + z1) - bendSize/2f;
                         bendEnd   = bendOffset - (shapePosition.z + z1) + bendSize/2f; break;
            }

            float totalSize = segAxis == 0 ? (x2-x1) : segAxis == 1 ? (y2-y1) : (z2-z1);

            float texpSide1, texpSide2, texpSide3;
            switch (segAxis)
            {
                case 0:  texpSide1 = texSouth1.x; texpSide2 = texNorth2.x; texpSide3 = texDown4.x; break;
                case 1:  texpSide1 = texSouth3.y; texpSide2 = texSouth3.y; texpSide3 = texSouth3.y; break;
                default: texpSide1 = texEast2.x;  texpSide2 = texWest1.x;  texpSide3 = texUp1.y;   break;
            }

            vec3 p1, p2, p3, p4;
            vec3 n1, n2, n3, n4;
            vec2 texStart1, texStart2, texStart3, texStart4;
            vec2 texEnd1,   texEnd2,   texEnd3,   texEnd4;

            switch (segAxis)
            {
                case 0:
                    p1 = new vec3(x1,y1,z2); p2 = new vec3(x1,y2,z2);
                    p3 = new vec3(x1,y2,z1); p4 = new vec3(x1,y1,z1);
                    n1 = new vec3(0,1,0); n2 = new vec3(0,-1,0); n3 = new vec3(0,0,1); n4 = new vec3(0,0,-1);
                    texStart1=texWest1; texStart2=texWest2; texStart3=texWest3; texStart4=texWest4;
                    texEnd1=texEast1;   texEnd2=texEast2;   texEnd3=texEast3;   texEnd4=texEast4; break;
                case 1:
                    p1 = new vec3(x2,y1,z2); p2 = new vec3(x1,y1,z2);
                    p3 = new vec3(x1,y1,z1); p4 = new vec3(x2,y1,z1);
                    n1 = new vec3(1,0,0); n2 = new vec3(-1,0,0); n3 = new vec3(0,0,1); n4 = new vec3(0,0,-1);
                    texStart1=texDown1; texStart2=texDown2; texStart3=texDown3; texStart4=texDown4;
                    texEnd1=texUp1;     texEnd2=texUp2;     texEnd3=texUp3;     texEnd4=texUp4; break;
                default:
                    p1 = new vec3(x1,y2,z1); p2 = new vec3(x2,y2,z1);
                    p3 = new vec3(x2,y1,z1); p4 = new vec3(x1,y1,z1);
                    n1 = new vec3(1,0,0); n2 = new vec3(-1,0,0); n3 = new vec3(0,1,0); n4 = new vec3(0,-1,0);
                    texStart1=texNorth1; texStart2=texNorth2; texStart3=texNorth3; texStart4=texNorth4;
                    texEnd1=texSouth1;   texEnd2=texSouth2;   texEnd3=texSouth3;   texEnd4=texSouth4; break;
            }

            mat4 shapeRotMat   = BuildShapeRotMat(shapeRotation);
            mat4 shapeScaleMat = shapeScale != default && shapeScale != vec3.Ones
                ? mat4.Scale(shapeScale) : mat4.Identity;
            mat4 rsm = shapeRotMat * shapeScaleMat;

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

            vec3 startBendVec       = BendHelper.GetBendVector(b.Angle, startP);
            vec3 startScaleCorr     = sharpBend ? BendHelper.GetBendScaleCorrection(bendStart, bendEnd, startP, 0, startBendVec, b) : vec3.Zero;
            mat4 startMat           = BendHelper.GetBendMatrix(b, startBendVec, shapePosition, shapeScale, vec3.Ones + startScaleCorr);

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
                    vec3 capNormal = segAxis == 0 ? new vec3(1,0,0) : segAxis == 1 ? new vec3(0,1,0) : new vec3(0,0,1);
                    if (segAxis == 0 || segAxis == 2)
                        AddFaceWithUVs(vertices, normals, uvs, indices, p2,p1,p4,p3, capNormal, texEnd1,texEnd2,texEnd3,texEnd4, invert);
                    else
                        AddFaceWithUVs(vertices, normals, uvs, indices, p4,p3,p2,p1, capNormal, texEnd1,texEnd2,texEnd3,texEnd4, invert);
                    break;
                }

                if (segPos == 0f)
                {
                    vec3 startCapNormal = segAxis == 0 ? new vec3(-1,0,0) : segAxis == 1 ? new vec3(0,-1,0) : new vec3(0,0,-1);
                    AddFaceWithUVs(vertices, normals, uvs, indices, p1,p2,p3,p4, startCapNormal, texStart1,texStart2,texStart3,texStart4, invert);
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
                        float fromCoord = segAxis == 0 ? (x1 + shapePosition.x)
                                        : segAxis == 1 ? (y1 + shapePosition.y)
                                        : (z1 + shapePosition.z);
                        curSegSize -= (fromCoord - bendStart) % segSize;
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
                        np1 = new vec3(x1+segPos,y1,z2); np2 = new vec3(x1+segPos,y2,z2);
                        np3 = new vec3(x1+segPos,y2,z1); np4 = new vec3(x1+segPos,y1,z1);
                        nn1 = new vec3(0,1,0); nn2 = new vec3(0,-1,0); nn3 = new vec3(0,0,1); nn4 = new vec3(0,0,-1);
                        { float toff = (segPos/totalSize)*texSizeFixX*(textureMirror?-1:1);
                          ntexpSide1=texSouth1.x+toff; ntexpSide2=texNorth2.x-toff; ntexpSide3=texDown4.x+toff; }
                        break;
                    case 1:
                        np1 = new vec3(x2,y1+segPos,z2); np2 = new vec3(x1,y1+segPos,z2);
                        np3 = new vec3(x1,y1+segPos,z1); np4 = new vec3(x2,y1+segPos,z1);
                        nn1 = new vec3(1,0,0); nn2 = new vec3(-1,0,0); nn3 = new vec3(0,0,1); nn4 = new vec3(0,0,-1);
                        { float toff = (segPos/totalSize)*texSizeFixY;
                          ntexpSide1=texSouth3.y-toff; ntexpSide2=ntexpSide1; ntexpSide3=ntexpSide1; }
                        break;
                    default:
                        np1 = new vec3(x1,y2,z1+segPos); np2 = new vec3(x2,y2,z1+segPos);
                        np3 = new vec3(x2,y1,z1+segPos); np4 = new vec3(x1,y1,z1+segPos);
                        nn1 = new vec3(1,0,0); nn2 = new vec3(-1,0,0); nn3 = new vec3(0,1,0); nn4 = new vec3(0,-1,0);
                        { float toff = (segPos/totalSize)*texSizeFixZ;
                          ntexpSide1=texEast2.x-toff*(textureMirror?-1:1);
                          ntexpSide2=texWest1.x+toff*(textureMirror?-1:1);
                          ntexpSide3=texUp1.y+toff; }
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

                float segP = segPos < bendStart ? 0f : segPos >= bendEnd ? 1f
                    : 1f - (bendEnd - segPos) / bendSize;
                if (invAngle) segP = 1f - segP;

                vec3 segBendVec = sharpBend ? b.Angle * segP : BendHelper.GetBendVector(b.Angle, segP);
                vec3 segScaleCorr = sharpBend ? BendHelper.GetBendScaleCorrection(bendStart, bendEnd, segP, segPos, segBendVec, b) : vec3.Zero;
                vec3 segMatScale  = vec3.Ones + segScaleCorr + new vec3(segP*scaleFactor);
                mat4 segMat = BendHelper.GetBendMatrix(b, segBendVec, shapePosition, shapeScale, segMatScale);

                np1 = BendHelper.TransformPoint(segMat, np1);
                np2 = BendHelper.TransformPoint(segMat, np2);
                np3 = BendHelper.TransformPoint(segMat, np3);
                np4 = BendHelper.TransformPoint(segMat, np4);
                nn1 = BendHelper.TransformDirection(segMat, nn1);
                nn2 = BendHelper.TransformDirection(segMat, nn2);
                nn3 = BendHelper.TransformDirection(segMat, nn3);
                nn4 = BendHelper.TransformDirection(segMat, nn4);

                switch (segAxis)
                {
                    case 0:
                        AddFaceWithUVs(vertices, normals, uvs, indices, p2,np2,np3,p3, n1,nn1,nn1,n1,
                            new vec2(texpSide1,texSouth1.y), new vec2(ntexpSide1,texSouth1.y), new vec2(ntexpSide1,texSouth3.y), new vec2(texpSide1,texSouth3.y), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, np1,p1,p4,np4, nn2,n2,n2,nn2,
                            new vec2(ntexpSide2,texNorth1.y), new vec2(texpSide2,texNorth1.y), new vec2(texpSide2,texNorth3.y), new vec2(ntexpSide2,texNorth3.y), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, p1,np1,np2,p2, n3,nn3,nn3,n3,
                            new vec2(texpSide1,texUp1.y), new vec2(ntexpSide1,texUp1.y), new vec2(ntexpSide1,texUp3.y), new vec2(texpSide1,texUp3.y), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, p3,np3,np4,p4, n4,nn4,nn4,n4,
                            new vec2(texpSide3,texDown1.y), new vec2(ntexpSide3,texDown1.y), new vec2(ntexpSide3,texDown3.y), new vec2(texpSide3,texDown3.y), invert);
                        texpSide1=ntexpSide1; texpSide2=ntexpSide2; texpSide3=ntexpSide3; break;
                    case 1:
                        AddFaceWithUVs(vertices, normals, uvs, indices, np1,p1,p4,np4, nn1,n1,n1,nn1,
                            new vec2(texEast1.x,ntexpSide1), new vec2(texEast2.x,ntexpSide1), new vec2(texEast2.x,texpSide1), new vec2(texEast1.x,texpSide1), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, p2,np2,np3,p3, n2,nn2,nn2,n2,
                            new vec2(texWest1.x,ntexpSide1), new vec2(texWest2.x,ntexpSide1), new vec2(texWest2.x,texpSide1), new vec2(texWest1.x,texpSide1), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, p2,p1,np1,np2, n3,n3,nn3,nn3,
                            new vec2(texSouth1.x,ntexpSide1), new vec2(texSouth2.x,ntexpSide1), new vec2(texSouth2.x,texpSide1), new vec2(texSouth1.x,texpSide1), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, np3,np4,p4,p3, nn4,nn4,n4,n4,
                            new vec2(texNorth1.x,ntexpSide1), new vec2(texNorth2.x,ntexpSide1), new vec2(texNorth2.x,texpSide1), new vec2(texNorth1.x,texpSide1), invert);
                        texpSide1=ntexpSide1; break;
                    default:
                        AddFaceWithUVs(vertices, normals, uvs, indices, np2,np3,p3,p2, nn1,n1,
                            new vec2(ntexpSide1,texEast1.y), new vec2(texpSide1,texEast1.y), new vec2(texpSide1,texEast3.y), new vec2(ntexpSide1,texEast3.y), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, np4,np1,p1,p4, nn2,n2,
                            new vec2(texpSide2,texWest1.y), new vec2(ntexpSide2,texWest1.y), new vec2(ntexpSide2,texWest3.y), new vec2(texpSide2,texWest3.y), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, np1,np2,p2,p1, nn3,n3,
                            new vec2(texUp1.x,texpSide3), new vec2(texUp2.x,texpSide3), new vec2(texUp2.x,ntexpSide3), new vec2(texUp1.x,ntexpSide3), invert);
                        AddFaceWithUVs(vertices, normals, uvs, indices, np3,np4,p4,p3, nn4,n4,
                            new vec2(texDown1.x,ntexpSide3), new vec2(texDown2.x,ntexpSide3), new vec2(texDown2.x,texpSide3), new vec2(texDown1.x,texpSide3), invert);
                        texpSide1=ntexpSide1; texpSide2=ntexpSide2; texpSide3=ntexpSide3; break;
                }

                p1=np1; p2=np2; p3=np3; p4=np4;
                n1=nn1; n2=nn2; n3=nn3; n4=nn4;
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
        bool hideFront = false, bool hideBack = false, vec3 shapePivot = default)
    {
        var vertices = new List<vec3>();
        var normals  = new List<vec3>();
        var uvs      = new List<vec2>();
        var indices  = new List<uint>();

        vec3 min = new vec3(Math.Min(from.x, to.x), Math.Min(from.y, to.y), Math.Min(from.z, to.z));
        vec3 max = new vec3(Math.Max(from.x, to.x), Math.Max(from.y, to.y), Math.Max(from.z, to.z));

        if (inflate != 0f) { min -= new vec3(inflate); max += new vec3(inflate); }

        float texU = uvU / texWidth;
        float texV = uvV / texHeight;
        float texSizeX = sizeX / texWidth;
        float texSizeZ = sizeY / texHeight;

        var tex1 = new vec2(texU,              texV);
        var tex2 = new vec2(texU + texSizeX,    texV);
        var tex3 = new vec2(texU + texSizeX,    texV + texSizeZ);
        var tex4 = new vec2(texU,              texV + texSizeZ);

        if (textureMirror) { (tex1, tex2) = (tex2, tex1); (tex3, tex4) = (tex4, tex3); }

        mat4 rsm = BuildShapeRotMat(shapeRotation) * (shapeScale != default && shapeScale != vec3.Ones ? mat4.Scale(shapeScale) : mat4.Identity);
        vec3 pivot = shapePivot != default ? shapePivot : (min + max) * 0.5f;
        vec3 Rv(vec3 v) => BendHelper.TransformPoint(rsm, v - pivot) + pivot;
        vec3 Rn(vec3 n) => BendHelper.TransformDirection(rsm, n);

        if (!hideFront)
        {
            int bv = vertices.Count;
            vertices.Add(Rv(new vec3(min.x, min.y, min.z))); vertices.Add(Rv(new vec3(max.x, min.y, min.z)));
            vertices.Add(Rv(new vec3(max.x, max.y, min.z))); vertices.Add(Rv(new vec3(min.x, max.y, min.z)));
            var bn = Rn(new vec3(0, 0, -1));
            normals.Add(bn); normals.Add(bn); normals.Add(bn); normals.Add(bn);
            if (invert) { uvs.Add(tex3); uvs.Add(tex4); uvs.Add(tex1); uvs.Add(tex2); }
            else        { uvs.Add(tex4); uvs.Add(tex3); uvs.Add(tex2); uvs.Add(tex1); }
            AddQuadIndices(indices, (uint)bv, invert: false);
        }

        if (!hideBack)
        {
            int bv = vertices.Count;
            vertices.Add(Rv(new vec3(min.x, min.y, max.z))); vertices.Add(Rv(new vec3(max.x, min.y, max.z)));
            vertices.Add(Rv(new vec3(max.x, max.y, max.z))); vertices.Add(Rv(new vec3(min.x, max.y, max.z)));
            var fn = Rn(new vec3(0, 0, 1));
            normals.Add(fn); normals.Add(fn); normals.Add(fn); normals.Add(fn);
            if (invert) { uvs.Add(tex3); uvs.Add(tex4); uvs.Add(tex1); uvs.Add(tex2); }
            else        { uvs.Add(tex4); uvs.Add(tex3); uvs.Add(tex2); uvs.Add(tex1); }
            AddQuadIndices(indices, (uint)bv, invert: true);
        }

        return BuildMesh(vertices, normals, uvs, indices);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Extruded plane mesh (per-pixel item-style)
    // ─────────────────────────────────────────────────────────────────────────

    private Mesh CreateExtrudedPlaneMesh(vec3 from, vec3 to, float uvU, float uvV, float sizeX, float sizeY,
        int texWidth, int texHeight, uint textureId, bool textureMirror, bool invert, float inflate = 0f,
        vec3 shapeRotation = default, vec3 shapeScale = default, vec3 shapePivot = default)
    {
        if (textureId == 0)
            return CreatePlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
                textureMirror, invert, inflate, shapeRotation, shapeScale, shapePivot: shapePivot);

        var pixels = TryGetPixels(textureId, texWidth, texHeight, out int imgW, out int imgH);
        if (pixels == null)
            return CreatePlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
                textureMirror, invert, inflate, shapeRotation, shapeScale);

        int uvStartX = Math.Max(0, Math.Min((int)uvU, texWidth - 1));
        int uvStartY = Math.Max(0, Math.Min((int)uvV, texHeight - 1));
        int uvEndX   = Math.Max(0, Math.Min((int)(uvU + sizeX), texWidth));
        int uvEndY   = Math.Max(0, Math.Min((int)(uvV + sizeY), texHeight));

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

        mat4 rsm = BuildShapeRotMat(shapeRotation) * (shapeScale != default && shapeScale != vec3.Ones ? mat4.Scale(shapeScale) : mat4.Identity);
        vec3 pivot = shapePivot != default ? shapePivot : (from + to) * 0.5f;
        vec3 Rv(vec3 v) => BendHelper.TransformPoint(rsm, v - pivot) + pivot;
        vec3 Rn(vec3 n) => BendHelper.TransformDirection(rsm, n);

        var vertices = new List<vec3>();
        var normals  = new List<vec3>();
        var uvs      = new List<vec2>();
        var indices  = new List<uint>();

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

                if (inflate != 0f) { posX -= inflate; posY -= inflate; }

                float adjPSX = pixScaleX + (inflate != 0f ? inflate * 2 : 0f);
                float adjPSY = pixScaleY + (inflate != 0f ? inflate * 2 : 0f);
                float centerZ = from.z + 0.5f * 0.0625f;

                float uvX = (texX + 0.5f) / texWidth;
                float uvY = (texY + 0.5f) / texHeight;

                int bv = vertices.Count;
                AddExtrudedQuad(vertices, normals, uvs, indices, (uint)bv,
                    Rv(new vec3(posX,       posY,       centerZ+halfThickness)),
                    Rv(new vec3(posX+adjPSX,posY,       centerZ+halfThickness)),
                    Rv(new vec3(posX+adjPSX,posY+adjPSY,centerZ+halfThickness)),
                    Rv(new vec3(posX,       posY+adjPSY,centerZ+halfThickness)),
                    Rn(new vec3(0,0,-1)), uvX, uvY, invert);

                bv = vertices.Count;
                AddExtrudedQuad(vertices, normals, uvs, indices, (uint)bv,
                    Rv(new vec3(posX+adjPSX,posY,       centerZ-halfThickness)),
                    Rv(new vec3(posX,       posY,       centerZ-halfThickness)),
                    Rv(new vec3(posX,       posY+adjPSY,centerZ-halfThickness)),
                    Rv(new vec3(posX+adjPSX,posY+adjPSY,centerZ-halfThickness)),
                    Rn(new vec3(0,0,1)), uvX, uvY, invert);

                bool leftEmpty   = px == 0           || GetAlpha(pixels, uvStartX+px-1, texY, imgW) <= 0.5f;
                bool rightEmpty  = px == regionW - 1 || GetAlpha(pixels, uvStartX+px+1, texY, imgW) <= 0.5f;
                bool topEmpty    = py == 0           || GetAlpha(pixels, texX, uvStartY+py-1, imgW) <= 0.5f;
                bool bottomEmpty = py == regionH - 1 || GetAlpha(pixels, texX, uvStartY+py+1, imgW) <= 0.5f;

                bool geoLeft  = textureMirror ? rightEmpty : leftEmpty;
                bool geoRight = textureMirror ? leftEmpty  : rightEmpty;

                if (geoLeft)
                {
                    bv = vertices.Count;
                    AddExtrudedQuad(vertices, normals, uvs, indices, (uint)bv,
                        Rv(new vec3(posX,posY,centerZ-halfThickness)), Rv(new vec3(posX,posY,centerZ+halfThickness)),
                        Rv(new vec3(posX,posY+adjPSY,centerZ+halfThickness)), Rv(new vec3(posX,posY+adjPSY,centerZ-halfThickness)),
                        Rn(new vec3(-1,0,0)), uvX, uvY, invert);
                }
                if (geoRight)
                {
                    bv = vertices.Count;
                    AddExtrudedQuad(vertices, normals, uvs, indices, (uint)bv,
                        Rv(new vec3(posX+adjPSX,posY,centerZ+halfThickness)), Rv(new vec3(posX+adjPSX,posY,centerZ-halfThickness)),
                        Rv(new vec3(posX+adjPSX,posY+adjPSY,centerZ-halfThickness)), Rv(new vec3(posX+adjPSX,posY+adjPSY,centerZ+halfThickness)),
                        Rn(new vec3(1,0,0)), uvX, uvY, invert);
                }
                if (topEmpty)
                {
                    bv = vertices.Count;
                    AddExtrudedQuad(vertices, normals, uvs, indices, (uint)bv,
                        Rv(new vec3(posX,posY+adjPSY,centerZ+halfThickness)), Rv(new vec3(posX+adjPSX,posY+adjPSY,centerZ+halfThickness)),
                        Rv(new vec3(posX+adjPSX,posY+adjPSY,centerZ-halfThickness)), Rv(new vec3(posX,posY+adjPSY,centerZ-halfThickness)),
                        Rn(new vec3(0,1,0)), uvX, uvY, invert);
                }
                if (bottomEmpty)
                {
                    bv = vertices.Count;
                    AddExtrudedQuad(vertices, normals, uvs, indices, (uint)bv,
                        Rv(new vec3(posX,posY,centerZ-halfThickness)), Rv(new vec3(posX+adjPSX,posY,centerZ-halfThickness)),
                        Rv(new vec3(posX+adjPSX,posY,centerZ+halfThickness)), Rv(new vec3(posX,posY,centerZ+halfThickness)),
                        Rn(new vec3(0,-1,0)), uvX, uvY, invert);
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
        var normals  = new List<vec3>();
        var uvs      = new List<vec2>();
        var indices  = new List<uint>();

        float x1 = Math.Min(from.x, to.x), x2 = Math.Max(from.x, to.x);
        float y1 = Math.Min(from.y, to.y), y2 = Math.Max(from.y, to.y);
        float z1 = from.z;

        if (inflate != 0f) { x1 -= inflate; x2 += inflate; y1 -= inflate; y2 += inflate; }

        float texU = uvU / texWidth, texV = uvV / texHeight;
        float texSX = sizeX / texWidth, texSY = sizeY / texHeight;

        var tex1 = new vec2(texU,      texV);
        var tex2 = new vec2(texU+texSX, texV);
        var tex3 = new vec2(texU+texSX, texV+texSY);
        var tex4 = new vec2(texU,      texV+texSY);

        if (textureMirror) { (tex1,tex2)=(tex2,tex1); (tex3,tex4)=(tex4,tex3); }

        var b = bend;
        int segAxis = (b.Part == BendPart.Right || b.Part == BendPart.Left) ? 0 : 1;

        float bendSize   = b.BendSize   / 16f;
        float bendOffset = b.BendOffset / 16f;

        BendStyle effectiveStyle = (bendStyle == BendStyle.ProjectDefault) ? ProjectBendStyle : bendStyle;
        bool singleXorZ = (b.AxisX&&!b.AxisY&&!b.AxisZ)||(!b.AxisX&&!b.AxisY&&b.AxisZ);
        bool sharpBend  = (effectiveStyle == BendStyle.Blocky) && !b.ExplicitBendSize && singleXorZ;

        float detail = BendHelper.CalculateSegmentCount(b.BendSize, sharpBend, b.Detail);
        if (b.BendSize >= 1 && shapeScale[segAxis] > 0.5f) detail /= shapeScale[segAxis];
        float segSize = bendSize / detail;

        bool invAngle = (b.Part==BendPart.Lower||b.Part==BendPart.Back||b.Part==BendPart.Left);
        float totalSize = segAxis == 0 ? (x2-x1) : (y2-y1);

        float bendStart = segAxis == 0
            ? bendOffset - (shapePosition.x+x1) - bendSize/2f
            : bendOffset - (shapePosition.y+y1) - bendSize/2f;
        float bendEnd = bendStart + bendSize;

        mat4 rsm = BuildShapeRotMat(shapeRotation) * (shapeScale!=default&&shapeScale!=vec3.Ones ? mat4.Scale(shapeScale) : mat4.Identity);

        vec3 p1 = segAxis==0 ? new vec3(x1,y2,z1) : new vec3(x1,y1,z1);
        vec3 p2 = segAxis==0 ? new vec3(x1,y1,z1) : new vec3(x2,y1,z1);
        float texp1 = segAxis==0 ? tex1.x : tex3.y;

        p1 = BendHelper.TransformPoint(rsm, p1);
        p2 = BendHelper.TransformPoint(rsm, p2);

        float startP = bendStart>0 ? 0f : bendEnd<0 ? 1f : 1f - bendEnd/bendSize;
        if (invAngle) startP = 1f-startP;
        vec3 startBendVec   = BendHelper.GetBendVector(b.Angle, startP);
        vec3 startScaleCorr = sharpBend ? BendHelper.GetBendScaleCorrection(bendStart, bendEnd, startP, 0, startBendVec, b) : vec3.Zero;
        mat4 startMat = BendHelper.GetBendMatrix(b, startBendVec, shapePosition, shapeScale, vec3.Ones+startScaleCorr);
        p1 = BendHelper.TransformPoint(startMat, p1);
        p2 = BendHelper.TransformPoint(startMat, p2);
        var n1 = BendHelper.TransformDirection(startMat * rsm, new vec3(0,0,1));
        var n2 = BendHelper.TransformDirection(startMat * rsm, new vec3(0,0,-1));

        float segPos = 0f;
        while (segPos < totalSize)
        {
            float curSegSize;
            if (segPos >= bendEnd)       curSegSize = totalSize - segPos;
            else if (segPos < bendStart) curSegSize = Math.Min(totalSize-segPos, bendStart);
            else
            {
                curSegSize = segSize;
                if (segPos == 0f)
                {
                    float fromCoord = segAxis==0 ? (x1+shapePosition.x) : (y1+shapePosition.y);
                    curSegSize -= (fromCoord - bendStart) % segSize;
                }
                curSegSize = Math.Min(totalSize-segPos, curSegSize);
            }
            segPos += Math.Max(curSegSize, 0.005f);

            vec3 np1, np2;
            float ntexp1;
            if (segAxis==0)
            {
                np1 = BendHelper.TransformPoint(rsm, new vec3(x1+segPos,y2,z1));
                np2 = BendHelper.TransformPoint(rsm, new vec3(x1+segPos,y1,z1));
                float toff = (segPos/totalSize)*texSX*(textureMirror?-1f:1f);
                ntexp1 = tex1.x + toff;
            }
            else
            {
                np1 = BendHelper.TransformPoint(rsm, new vec3(x1,y1+segPos,z1));
                np2 = BendHelper.TransformPoint(rsm, new vec3(x2,y1+segPos,z1));
                float toff = (segPos/totalSize)*texSY;
                ntexp1 = tex3.y - toff;
            }

            float segP = segPos<bendStart ? 0f : segPos>=bendEnd ? 1f : 1f-(bendEnd-segPos)/bendSize;
            if (invAngle) segP=1f-segP;

            vec3 segBendVec   = sharpBend ? b.Angle*segP : BendHelper.GetBendVector(b.Angle, segP);
            vec3 segScaleCorr = sharpBend ? BendHelper.GetBendScaleCorrection(bendStart,bendEnd,segP,segPos,segBendVec,b) : vec3.Zero;
            mat4 segMat = BendHelper.GetBendMatrix(b, segBendVec, shapePosition, shapeScale, vec3.Ones+segScaleCorr);

            np1 = BendHelper.TransformPoint(segMat, np1);
            np2 = BendHelper.TransformPoint(segMat, np2);
            var nn1 = BendHelper.TransformDirection(segMat * rsm, new vec3(0,0,1));
            var nn2 = BendHelper.TransformDirection(segMat * rsm, new vec3(0,0,-1));

            vec2 t1, t2, t3, t4;
            if (segAxis==0)
            {
                t1 = new vec2(texp1,tex1.y); t2 = new vec2(ntexp1,tex1.y);
                t3 = new vec2(ntexp1,tex3.y); t4 = new vec2(texp1,tex3.y);
                if (!hideFront) AddFaceWithUVs(vertices,normals,uvs,indices, p1,np1,np2,p2, n1,nn1,nn1,n1, t1,t2,t3,t4, invert);
                if (!hideBack)  AddFaceWithUVs(vertices,normals,uvs,indices, np1,p1,p2,np2, nn2,n2,n2,nn2, t2,t1,t4,t3, invert);
            }
            else
            {
                t1 = new vec2(tex1.x,ntexp1); t2 = new vec2(tex2.x,ntexp1);
                t3 = new vec2(tex2.x,texp1);  t4 = new vec2(tex1.x,texp1);
                if (!hideFront) AddFaceWithUVs(vertices,normals,uvs,indices, np1,np2,p2,p1, nn1,nn1,n1,n1, t1,t2,t3,t4, invert);
                if (!hideBack)  AddFaceWithUVs(vertices,normals,uvs,indices, np2,np1,p1,p2, nn2,nn2,n2,n2, t2,t1,t4,t3, invert);
            }

            p1=np1; p2=np2; n1=nn1; n2=nn2; texp1=ntexp1;
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

        int uvStartX = Math.Max(0, Math.Min((int)uvU, texWidth-1));
        int uvStartY = Math.Max(0, Math.Min((int)uvV, texHeight-1));
        int uvEndX   = Math.Max(0, Math.Min((int)(uvU+sizeX), texWidth));
        int uvEndY   = Math.Max(0, Math.Min((int)(uvV+sizeY), texHeight));
        int regionW  = uvEndX - uvStartX;
        int regionH  = uvEndY - uvStartY;

        if (regionW <= 0 || regionH <= 0)
            return CreateBentPlaneMesh(from, to, uvU, uvV, sizeX, sizeY, texWidth, texHeight,
                textureMirror, invert, inflate, bend, shapePosition, shapeRotation, shapeScale, bendStyle);

        float x1 = Math.Min(from.x, to.x), x2 = Math.Max(from.x, to.x);
        float y1 = Math.Min(from.y, to.y), y2 = Math.Max(from.y, to.y);
        float z1 = from.z + 0.5f * 0.0625f;

        float pixScaleX = (x2-x1) / regionW;
        float pixScaleY = (y2-y1) / regionH;

        const float thickness = 1f / 16f;
        float halfT = thickness / 2f + inflate;

        var b = bend;
        bool bendAlongX = (b.Part == BendPart.Left || b.Part == BendPart.Right);
        float bendSize   = b.BendSize   / 16f;
        float bendOffset = b.BendOffset / 16f;

        BendStyle effectiveStyle = (bendStyle == BendStyle.ProjectDefault) ? ProjectBendStyle : bendStyle;
        bool singleXorZ = (b.AxisX&&!b.AxisY&&!b.AxisZ)||(!b.AxisX&&!b.AxisY&&b.AxisZ);
        bool sharpBend  = (effectiveStyle == BendStyle.Blocky) && !b.ExplicitBendSize && singleXorZ;

        int segAxis = bendAlongX ? 0 : 1;
        float detail = BendHelper.CalculateSegmentCount(b.BendSize, sharpBend, b.Detail);
        if (b.BendSize >= 1 && shapeScale[segAxis] > 0.5f) detail /= shapeScale[segAxis];
        float segSize = bendSize / detail;

        bool invAngle = (b.Part==BendPart.Lower||b.Part==BendPart.Back||b.Part==BendPart.Left);

        float bendStart = bendAlongX
            ? bendOffset-(shapePosition.x+x1)-bendSize/2f
            : bendOffset-(shapePosition.y+y1)-bendSize/2f;
        float bendEnd = bendStart + bendSize;

        mat4 rsm = BuildShapeRotMat(shapeRotation) * (shapeScale!=default&&shapeScale!=vec3.Ones ? mat4.Scale(shapeScale) : mat4.Identity);

        int outerCount = bendAlongX ? regionH : regionW;
        int innerCount = bendAlongX ? regionW : regionH;

        var gridBot = new vec3[outerCount+1, innerCount+1];
        var gridTop = new vec3[outerCount+1, innerCount+1];

        for (int outer = 0; outer <= outerCount; outer++)
        {
            for (int inner = 0; inner <= innerCount; inner++)
            {
                vec3 pBot, pTop;
                if (bendAlongX)
                {
                    float px = x1 + inner*pixScaleX;
                    float py = y1 + outer*pixScaleY;
                    pBot = new vec3(px, py, z1-halfT);
                    pTop = new vec3(px, py, z1+halfT);
                }
                else
                {
                    float px = x1 + outer*pixScaleX;
                    float py = y1 + inner*pixScaleY;
                    pBot = new vec3(px, py, z1-halfT);
                    pTop = new vec3(px, py, z1+halfT);
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
                    float segIdx = Math.Min((float)Math.Floor(relPos / segSize), detail-1);
                    segP = segIdx / (detail-1);
                }
                if (invAngle) segP = 1f-segP;

                vec3 bendVec = sharpBend ? b.Angle*segP : BendHelper.GetBendVector(b.Angle, segP);
                vec3 scCorr  = sharpBend ? BendHelper.GetBendScaleCorrection(bendStart, bendEnd, segP, innerPos, bendVec, b) : vec3.Zero;
                mat4 mat = BendHelper.GetBendMatrix(b, bendVec, shapePosition, shapeScale, vec3.Ones+scCorr);
                gridBot[outer, inner] = BendHelper.TransformPoint(mat, pBot);
                gridTop[outer, inner] = BendHelper.TransformPoint(mat, pTop);
            }
        }

        var vertices = new List<vec3>();
        var normals  = new List<vec3>();
        var uvs      = new List<vec2>();
        var indices  = new List<uint>();

        float texNormW = 1f / texWidth;
        float texNormH = 1f / texHeight;

        for (int outer = 0; outer < outerCount; outer++)
        {
            for (int inner = 0; inner < innerCount; inner++)
            {
                int ax, ay;
                if (bendAlongX) { ax = textureMirror ? (regionW-1-inner) : inner; ay = regionH-1-outer; }
                else            { ax = textureMirror ? (regionW-1-outer) : outer;  ay = regionH-1-inner; }

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
                    p1 = gridBot[outer+1,inner];   p2 = gridTop[outer+1,inner];
                    p3 = gridTop[outer,inner];      p4 = gridBot[outer,inner];
                    np1 = gridBot[outer+1,inner+1]; np2 = gridTop[outer+1,inner+1];
                    np3 = gridTop[outer,inner+1];   np4 = gridBot[outer,inner+1];
                }
                else
                {
                    p1 = gridBot[outer,inner];    p2 = gridBot[outer+1,inner];
                    p3 = gridTop[outer+1,inner];  p4 = gridTop[outer,inner];
                    np1 = gridBot[outer,inner+1]; np2 = gridBot[outer+1,inner+1];
                    np3 = gridTop[outer+1,inner+1]; np4 = gridTop[outer,inner+1];
                }

                bool leftEmpty   = ax==0           || GetAlpha(pixels, uvStartX+ax-1, texY, imgW) <= 0.5f;
                bool rightEmpty  = ax==regionW-1   || GetAlpha(pixels, uvStartX+ax+1, texY, imgW) <= 0.5f;
                bool topEmpty    = ay==0           || GetAlpha(pixels, texX, uvStartY+ay-1, imgW) <= 0.5f;
                bool bottomEmpty = ay==regionH-1   || GetAlpha(pixels, texX, uvStartY+ay+1, imgW) <= 0.5f;

                bool wface = textureMirror ? rightEmpty : leftEmpty;
                bool eface = textureMirror ? leftEmpty  : rightEmpty;
                bool aface = topEmpty;
                bool bface = bottomEmpty;

                if (bendAlongX)
                {
                    if (eface) AddSimpleQuad(vertices, normals, uvs, indices, np3,np4,np1,np2, pixUV, invert);
                    if (wface) AddSimpleQuad(vertices, normals, uvs, indices, p4,p3,p2,p1,   pixUV, invert);
                    AddSimpleQuad(vertices, normals, uvs, indices, p3,np3,np2,p2, pixUV, invert);
                    AddSimpleQuad(vertices, normals, uvs, indices, np4,p4,p1,np1, pixUV, invert);
                    if (aface) AddSimpleQuad(vertices, normals, uvs, indices, p2,np2,np1,p1, pixUV, invert);
                    if (bface) AddSimpleQuad(vertices, normals, uvs, indices, p4,np4,np3,p3, pixUV, invert);
                }
                else
                {
                    if (eface) AddSimpleQuad(vertices, normals, uvs, indices, p3,p2,np2,np3, pixUV, invert);
                    if (wface) AddSimpleQuad(vertices, normals, uvs, indices, p1,p4,np4,np1, pixUV, invert);
                    AddSimpleQuad(vertices, normals, uvs, indices, p4,p3,np3,np4, pixUV, invert);
                    AddSimpleQuad(vertices, normals, uvs, indices, p2,p1,np1,np2, pixUV, invert);
                    if (aface) AddSimpleQuad(vertices, normals, uvs, indices, np4,np3,np2,np1, pixUV, invert);
                    if (bface) AddSimpleQuad(vertices, normals, uvs, indices, p1,p2,p3,p4,   pixUV, invert);
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
        // Rotations applied Z→X→Y (right-multiplication order matches Godot source)
        mat4 rz = mat4.RotateZ(rot.z);
        mat4 rx = mat4.RotateX(rot.x);
        mat4 ry = mat4.RotateY(rot.y);
        return ry * rx * rz;
    }

    // Adds a face quad with uniform normal and per-vertex UVs
    private static void AddFaceWithUVs(List<vec3> verts, List<vec3> normals, List<vec2> uvs, List<uint> indices,
        vec3 v0, vec3 v1, vec3 v2, vec3 v3, vec3 normal,
        vec2 uv0, vec2 uv1, vec2 uv2, vec2 uv3, bool invert)
        => AddFaceWithUVs(verts, normals, uvs, indices, v0, v1, v2, v3, normal, normal, normal, normal, uv0, uv1, uv2, uv3, invert);

    private static void AddFaceWithUVs(List<vec3> verts, List<vec3> normals, List<vec2> uvs, List<uint> indices,
        vec3 v0, vec3 v1, vec3 v2, vec3 v3, vec3 n01, vec3 n23,
        vec2 uv0, vec2 uv1, vec2 uv2, vec2 uv3, bool invert)
        => AddFaceWithUVs(verts, normals, uvs, indices, v0, v1, v2, v3, n01, n01, n23, n23, uv0, uv1, uv2, uv3, invert);

    private static void AddFaceWithUVs(List<vec3> verts, List<vec3> normals, List<vec2> uvs, List<uint> indices,
        vec3 v0, vec3 v1, vec3 v2, vec3 v3,
        vec3 n0, vec3 n1, vec3 n2, vec3 n3,
        vec2 uv0, vec2 uv1, vec2 uv2, vec2 uv3, bool invert)
    {
        uint bv = (uint)verts.Count;
        verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
        normals.Add(n0); normals.Add(n1); normals.Add(n2); normals.Add(n3);

        if (invert) { uvs.Add(uv2); uvs.Add(uv3); uvs.Add(uv0); uvs.Add(uv1); }
        else        { uvs.Add(uv0); uvs.Add(uv1); uvs.Add(uv2); uvs.Add(uv3); }

        // CCW winding for OpenGL front faces (FrontFace = CCW, CullFace = Back)
        indices.Add(bv+0); indices.Add(bv+1); indices.Add(bv+2);
        indices.Add(bv+0); indices.Add(bv+2); indices.Add(bv+3);
    }

    private static void AddQuadIndices(List<uint> indices, uint baseVertex, bool invert)
    {
        if (invert) { indices.Add(baseVertex+0); indices.Add(baseVertex+2); indices.Add(baseVertex+1); indices.Add(baseVertex+0); indices.Add(baseVertex+3); indices.Add(baseVertex+2); }
        else        { indices.Add(baseVertex+0); indices.Add(baseVertex+1); indices.Add(baseVertex+2); indices.Add(baseVertex+0); indices.Add(baseVertex+2); indices.Add(baseVertex+3); }
    }

    private static void AddSimpleQuad(List<vec3> verts, List<vec3> normals, List<vec2> uvs, List<uint> indices,
        vec3 v0, vec3 v1, vec3 v2, vec3 v3, vec2 uv, bool invert)
    {
        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var normal = vec3.Cross(edge1, edge2);
        if (normal.LengthSqr < 1e-10f) normal = vec3.UnitY;
        else normal = normal.Normalized;

        uint bv = (uint)verts.Count;
        verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
        uvs.Add(uv); uvs.Add(uv); uvs.Add(uv); uvs.Add(uv);

        if (invert) { indices.Add(bv+0); indices.Add(bv+2); indices.Add(bv+1); indices.Add(bv+0); indices.Add(bv+3); indices.Add(bv+2); }
        else        { indices.Add(bv+0); indices.Add(bv+1); indices.Add(bv+2); indices.Add(bv+0); indices.Add(bv+2); indices.Add(bv+3); }
    }

    private static void AddExtrudedQuad(List<vec3> verts, List<vec3> normals, List<vec2> uvs, List<uint> indices,
        uint baseVertex, vec3 v0, vec3 v1, vec3 v2, vec3 v3, vec3 normal, float uvX, float uvY, bool invert)
    {
        verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
        var uv = new vec2(invert ? 1f-uvX : uvX, uvY);
        uvs.Add(uv); uvs.Add(uv); uvs.Add(uv); uvs.Add(uv);
        indices.Add(baseVertex+0); indices.Add(baseVertex+1); indices.Add(baseVertex+2);
        indices.Add(baseVertex+0); indices.Add(baseVertex+2); indices.Add(baseVertex+3);
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
            imgW = cached.w; imgH = cached.h;
            return cached.pixels;
        }

        if (_gl == null) { imgW = imgH = 0; return null; }

        try
        {
            // We'll use the expected texWidth/texHeight since we know them from the model
            imgW = texWidth; imgH = texHeight;
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
    [JsonPropertyName("name")]          public string Name         { get; set; }
    [JsonPropertyName("texture")]       public string Texture      { get; set; }
    [JsonPropertyName("texture_size")]  public int[]  TextureSize  { get; set; }
    [JsonPropertyName("textures")]      public Dictionary<string, string> Textures { get; set; }
    [JsonPropertyName("parts")]         public List<MiPart> Parts  { get; set; }

    [JsonIgnore] public string DirectoryPath { get; set; }
    [JsonIgnore] public string FullPath      { get; set; }
    [JsonIgnore] public Dictionary<string, uint> LoadedTextures { get; set; } = new();

    public uint GetTexture(string textureName = null)
    {
        if (LoadedTextures == null || LoadedTextures.Count == 0) return 0;
        if (string.IsNullOrEmpty(textureName))
        {
            if (LoadedTextures.TryGetValue("skin",    out uint t1)) return t1;
            if (LoadedTextures.TryGetValue("texture", out uint t2)) return t2;
            return 0;
        }
        if (LoadedTextures.TryGetValue(textureName, out uint t3)) return t3;
        if (LoadedTextures.TryGetValue("texture",   out uint t4)) return t4;
        return 0;
    }
}

public class MiPart
{
    [JsonPropertyName("name")]          public string Name       { get; set; }
    [JsonPropertyName("texture")]       public string Texture    { get; set; }
    [JsonPropertyName("texture_size")]  public int[]  TextureSize { get; set; }
    [JsonPropertyName("textures")]      public Dictionary<string, string> Textures { get; set; }
    [JsonPropertyName("position")]      public float[] Position  { get; set; }
    [JsonPropertyName("rotation")]      public float[] Rotation  { get; set; }
    [JsonPropertyName("scale")]         public float[] Scale     { get; set; }
    [JsonPropertyName("bend")]          public MiBend  Bend      { get; set; }

    [JsonPropertyName("lock_bend")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float? LockBend { get; set; }

    [JsonPropertyName("locked")]    public bool Locked  { get; set; }
    [JsonPropertyName("depth")]     public int  Depth   { get; set; }

    [JsonPropertyName("color_alpha")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float? ColorAlpha { get; set; }

    [JsonPropertyName("shapes")]    public List<MiShape> Shapes  { get; set; }
    [JsonPropertyName("parts")]     public List<MiPart>  Parts   { get; set; }

    [JsonIgnore] public Dictionary<string, uint> LoadedTextures { get; set; } = new();
}

public class MiShape
{
    [JsonPropertyName("type")]           public string  Type         { get; set; } = "block";
    [JsonPropertyName("from")]           public float[] From         { get; set; }
    [JsonPropertyName("to")]             public float[] To           { get; set; }
    [JsonPropertyName("uv")]             public float[] Uv           { get; set; }
    [JsonPropertyName("position")]       public float[] Position     { get; set; }
    [JsonPropertyName("rotation")]       public float[] Rotation     { get; set; }
    [JsonPropertyName("scale")]          public float[] Scale        { get; set; }
    [JsonPropertyName("invert")]         public bool    Invert       { get; set; }
    [JsonPropertyName("texture_mirror")] public bool    TextureMirror { get; set; }
    [JsonPropertyName("texture")]        public string  Texture      { get; set; }
    [JsonPropertyName("3d")]             public bool    ThreeD       { get; set; }
    [JsonPropertyName("inflate")]        public float   Inflate      { get; set; }
    [JsonPropertyName("bend")]           public bool    Bend         { get; set; } = true;
    [JsonPropertyName("hide_front")]     public bool    HideFront    { get; set; }
    [JsonPropertyName("hide_back")]      public bool    HideBack     { get; set; }
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
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float? InheritBend { get; set; }

    [JsonPropertyName("part")]   public string Part  { get; set; }
    [JsonPropertyName("axis")]   public object Axis  { get; set; }

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
    [JsonPropertyName("format")]     public int    Format    { get; set; }
    [JsonPropertyName("created_in")] public string CreatedIn { get; set; }
    [JsonPropertyName("templates")]  public List<MiTemplate>  Templates  { get; set; }
    [JsonPropertyName("timelines")]  public List<MiTimeline>  Timelines  { get; set; }
    [JsonPropertyName("resources")]  public List<MiResource>  Resources  { get; set; }

    [JsonIgnore] public string DirectoryPath { get; set; }
    [JsonIgnore] public string FullPath      { get; set; }
}

public class MiTemplate
{
    [JsonPropertyName("id")]        public string Id       { get; set; }
    [JsonPropertyName("type")]      public string Type     { get; set; }
    [JsonPropertyName("name")]      public string Name     { get; set; }
    [JsonPropertyName("model")]     public string Model    { get; set; }
    [JsonPropertyName("model_tex")] public string ModelTex { get; set; }
}

public class MiTimeline
{
    [JsonPropertyName("id")]         public string Id          { get; set; }
    [JsonPropertyName("type")]       public string Type        { get; set; }
    [JsonPropertyName("name")]       public string Name        { get; set; }
    [JsonPropertyName("temp")]       public string Temp        { get; set; }
    [JsonPropertyName("hide")]       public bool   Hide        { get; set; }
    [JsonPropertyName("parent")]     public string Parent      { get; set; }
    [JsonPropertyName("model_part_name")] public string ModelPartName { get; set; }
    [JsonPropertyName("position")]   public float[] Position   { get; set; }
    [JsonPropertyName("rotation")]   public float[] Rotation   { get; set; }
    [JsonPropertyName("scale")]      public float[] Scale      { get; set; }
    [JsonPropertyName("keyframes")]  public Dictionary<string, MiKeyframe> Keyframes { get; set; }
}

public class MiKeyframe
{
    [JsonPropertyName("position")] public float[] Position { get; set; }
    [JsonPropertyName("POS_X")]    public float?  PosX { get; set; }
    [JsonPropertyName("POS_Y")]    public float?  PosY { get; set; }
    [JsonPropertyName("POS_Z")]    public float?  PosZ { get; set; }
    [JsonPropertyName("rotation")] public float[] Rotation { get; set; }
    [JsonPropertyName("scale")]    public float[] Scale    { get; set; }

    public float[] GetPosition()
    {
        if (Position is { Length: >= 3 }) return Position;
        if (PosX.HasValue || PosY.HasValue || PosZ.HasValue)
            return new[] { PosX??0, PosZ??0, PosY??0 }; // swap Y/Z (Mine Imator Z-up to Y-up)
        return null;
    }
}

public class MiResource
{
    [JsonPropertyName("id")]       public string Id       { get; set; }
    [JsonPropertyName("type")]     public string Type     { get; set; }
    [JsonPropertyName("filename")] public string Filename { get; set; }
}

public class MiInherit
{
    [JsonPropertyName("position")] public bool Position   { get; set; }
    [JsonPropertyName("rotation")] public bool Rotation   { get; set; }
    [JsonPropertyName("scale")]    public bool Scale      { get; set; }
    [JsonPropertyName("alpha")]    public bool Alpha      { get; set; }
    [JsonPropertyName("visibility")] public bool Visibility { get; set; }
}

// ── JSON converters ───────────────────────────────────────────────────────────

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
        if (value == null) { writer.WriteNullValue(); return; }
        if (value.Length == 1) { writer.WriteNumberValue(value[0]); return; }
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
            case JsonTokenType.False:  return new[] { reader.GetBoolean() };
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
        if (value == null) { writer.WriteNullValue(); return; }
        if (value.Length == 1) { writer.WriteBooleanValue(value[0]); return; }
        writer.WriteStartArray();
        foreach (var v in value) writer.WriteBooleanValue(v);
        writer.WriteEndArray();
    }
}

#endregion
