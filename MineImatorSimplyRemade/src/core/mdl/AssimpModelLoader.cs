using System.Numerics;
using System.Text;
using GlmSharp;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Newtonsoft.Json.Linq;
using Silk.NET.OpenGL;
using StbImageSharp;

// Alias Silk.NET.Assimp types that conflict with names already in scope.
using AiAssimp   = Silk.NET.Assimp.Assimp;
using AiScene    = Silk.NET.Assimp.Scene;
using AiNode     = Silk.NET.Assimp.Node;
using AiMesh     = Silk.NET.Assimp.Mesh;
using AiFace     = Silk.NET.Assimp.Face;
using AiBone     = Silk.NET.Assimp.Bone;
using AiMaterial = Silk.NET.Assimp.Material;
using AiTexture  = Silk.NET.Assimp.Texture;
using AiTexel    = Silk.NET.Assimp.Texel;
using SysFile    = System.IO.File;

namespace MineImatorSimplyRemade.core.mdl;

/// <summary>
/// Loads a 3-D model file (GLB, GLTF, FBX, OBJ, DAE, …) using Assimp and converts
/// it into a tree of <see cref="SceneObject"/>/<see cref="BoneSceneObject"/> nodes.
///
/// Coordinate system
/// ─────────────────
/// Assimp stores aiMatrix4x4 row-major with translation in column 4 (M14,M24,M34).
/// System.Numerics.Matrix4x4 is row-major with translation in row 4 (M41,M42,M43).
/// These are transposed relative to each other — every MTransformation must be
/// Matrix4x4.Transpose()'d before decomposing.
///
/// Blender GLTF exports apply a -90° X rotation to convert Blender's Z-up space to
/// GLTF Y-up space.  To undo this and display the model in Blender's natural
/// orientation (Z-up, bones at their authored rotations), we apply a +90° X rotation
/// to the imported root SceneObject.  All child node transforms are then correct in
/// the editor's Y-up world without any further adjustments.
/// </summary>
public static class AssimpModelLoader
{
    // ── Public entry point ────────────────────────────────────────────────────

    public static SceneObject? Load(GL gl, string filePath)
    {
        if (!SysFile.Exists(filePath))
        {
            Console.Error.WriteLine($"[AssimpModelLoader] File not found: {filePath}");
            return null;
        }

        // Use nearest-neighbour filtering for models that come from a minecraft
        // namespace (identified by "minecraft" appearing anywhere in the path).
        bool nearestFilter = filePath.Contains("minecraft",
            StringComparison.OrdinalIgnoreCase);

        var assimp = AiAssimp.GetApi();

        uint flags =
            (uint)Silk.NET.Assimp.PostProcessSteps.Triangulate           |
            (uint)Silk.NET.Assimp.PostProcessSteps.GenerateSmoothNormals |
            (uint)Silk.NET.Assimp.PostProcessSteps.JoinIdenticalVertices |
            (uint)Silk.NET.Assimp.PostProcessSteps.LimitBoneWeights      |
            // GLTF/Blender UV V=0 is at the top; OpenGL expects V=0 at bottom.
            (uint)Silk.NET.Assimp.PostProcessSteps.FlipUVs;

        unsafe
        {
            AiScene* scene = assimp.ImportFile(filePath, flags);

            if (scene == null
                || (scene->MFlags & (uint)Silk.NET.Assimp.SceneFlags.Incomplete) != 0
                || scene->MRootNode == null)
            {
                string err = assimp.GetErrorStringS();
                Console.Error.WriteLine($"[AssimpModelLoader] Assimp error for '{filePath}': {err}");
                if (scene != null) assimp.ReleaseImport(scene);
                assimp.Dispose();
                return null;
            }

            // ── Collect bone names ─────────────────────────────────────────
            // For GLTF/GLB, Assimp reports 0 bones on rigid-body rigs (no
            // JOINTS_0/WEIGHTS_0 attributes), so we parse the skin joints
            // directly from the JSON.  For other formats we fall back to
            // Assimp's mesh bone list.
            var boneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            if (ext is ".gltf" or ".glb")
                CollectGltfBoneNames(filePath, boneNames);
            else
                CollectAssimpBoneNames(scene, boneNames);

            // ── Upload textures ────────────────────────────────────────────
            string modelDir = System.IO.Path.GetDirectoryName(filePath) ?? "";
            var texCache = new Dictionary<string, uint>();
            UploadTextures(assimp, gl, scene, modelDir, texCache, nearestFilter);

            // ── Build the node hierarchy ───────────────────────────────────
            SceneObject? root = BuildNodeTree(
                assimp, gl, scene, scene->MRootNode,
                boneNames, texCache, filePath);

            if (root == null)
            {
                assimp.ReleaseImport(scene);
                assimp.Dispose();
                return null;
            }

            assimp.ReleaseImport(scene);
            assimp.Dispose();
            return root;
        }
    }

    // ── Bone-name collection ──────────────────────────────────────────────────

    /// <summary>
    /// Reads the GLTF/GLB file directly to collect the names of all nodes
    /// listed as skin joints.  This works for rigid-body rigs that Assimp
    /// reports with 0 mesh bones (no JOINTS_0/WEIGHTS_0 vertex attributes).
    /// </summary>
    private static void CollectGltfBoneNames(string filePath, HashSet<string> names)
    {
        try
        {
            string json;
            string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();

            if (ext == ".glb")
            {
                // GLB: 12-byte header + chunk0 (JSON).
                // Header: magic(4) + version(4) + length(4)
                // Chunk:  chunkLength(4) + chunkType(4) + chunkData(chunkLength)
                using var fs = SysFile.OpenRead(filePath);
                using var br = new System.IO.BinaryReader(fs);
                br.ReadBytes(12);               // skip header
                int jsonLen  = (int)br.ReadUInt32();
                br.ReadUInt32();                // chunkType = 0x4E4F534A ("JSON")
                byte[] jsonBytes = br.ReadBytes(jsonLen);
                json = Encoding.UTF8.GetString(jsonBytes);
            }
            else
            {
                json = SysFile.ReadAllText(filePath);
            }

            var root = JObject.Parse(json);

            // nodes array: index → name
            var nodes = root["nodes"] as JArray;
            if (nodes == null) return;

            var skins = root["skins"] as JArray;
            if (skins == null) return;

            foreach (var skin in skins)
            {
                var joints = skin["joints"] as JArray;
                if (joints == null) continue;
                foreach (var joint in joints)
                {
                    int idx = joint.Value<int>();
                    if (idx < nodes.Count)
                    {
                        string? name = nodes[idx]["name"]?.Value<string>();
                        if (!string.IsNullOrEmpty(name))
                            names.Add(name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AssimpModelLoader] GLTF bone-name parse failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Fallback for non-GLTF formats: collect bone names from Assimp mesh bone lists.
    /// </summary>
    private static unsafe void CollectAssimpBoneNames(AiScene* scene, HashSet<string> names)
    {
        for (uint mi = 0; mi < scene->MNumMeshes; mi++)
        {
            AiMesh* aMesh = scene->MMeshes[mi];
            for (uint bi = 0; bi < aMesh->MNumBones; bi++)
            {
                string name = aMesh->MBones[bi]->MName.AsString;
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
        }
    }

    // ── Texture upload ────────────────────────────────────────────────────────

    private static unsafe void UploadTextures(
        AiAssimp assimp,
        GL gl,
        AiScene* scene,
        string modelDir,
        Dictionary<string, uint> cache,
        bool nearest = false)
    {
        for (uint mi = 0; mi < scene->MNumMaterials; mi++)
        {
            AiMaterial* mat = scene->MMaterials[mi];

            uint texCount = assimp.GetMaterialTextureCount(mat, Silk.NET.Assimp.TextureType.Diffuse);
            for (uint ti = 0; ti < texCount; ti++)
            {
                Silk.NET.Assimp.AssimpString path = default;
                assimp.GetMaterialTexture(mat, Silk.NET.Assimp.TextureType.Diffuse, ti,
                    ref path, null, null, null, null, null, null);

                string texPath = path.AsString;
                if (string.IsNullOrEmpty(texPath) || cache.ContainsKey(texPath))
                    continue;

                uint handle = texPath.StartsWith('*')
                    ? UploadEmbeddedTexture(gl, assimp.GetEmbeddedTexture(scene, texPath), nearest)
                    : UploadFileTexture(gl, System.IO.Path.IsPathRooted(texPath)
                        ? texPath
                        : System.IO.Path.Combine(modelDir, texPath), nearest);

                if (handle != 0)
                    cache[texPath] = handle;
            }
        }
    }

    private static unsafe uint UploadEmbeddedTexture(GL gl, AiTexture* tex, bool nearest = false)
    {
        if (tex == null || tex->PcData == null) return 0;

        byte[] pixels;
        int width, height;

        if (tex->MHeight == 0)
        {
            int len = (int)tex->MWidth;
            byte[] compressed = new byte[len];
            fixed (byte* dst = compressed)
                System.Buffer.MemoryCopy(tex->PcData, dst, len, len);
            ImageResult img;
            try { img = ImageResult.FromMemory(compressed, ColorComponents.RedGreenBlueAlpha); }
            catch { return 0; }
            pixels = img.Data; width = img.Width; height = img.Height;
        }
        else
        {
            width  = (int)tex->MWidth;
            height = (int)tex->MHeight;
            int count = width * height;
            pixels = new byte[count * 4];
            AiTexel* src = tex->PcData;
            for (int i = 0; i < count; i++)
            {
                pixels[i * 4 + 0] = src[i].R;
                pixels[i * 4 + 1] = src[i].G;
                pixels[i * 4 + 2] = src[i].B;
                pixels[i * 4 + 3] = src[i].A;
            }
        }

        return UploadRgbaPixels(gl, pixels, width, height, nearest);
    }

    private static uint UploadFileTexture(GL gl, string path, bool nearest = false)
    {
        if (!SysFile.Exists(path)) return 0;
        try
        {
            using var stream = SysFile.OpenRead(path);
            var img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            return UploadRgbaPixels(gl, img.Data, img.Width, img.Height, nearest);
        }
        catch { return 0; }
    }

    private static unsafe uint UploadRgbaPixels(GL gl, byte[] pixels, int width, int height,
                                                bool nearest = false)
    {
        uint tex = gl.GenTexture();
        gl.BindTexture(GLEnum.Texture2D, tex);
        fixed (byte* p = pixels)
            gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8,
                (uint)width, (uint)height, 0,
                PixelFormat.Rgba, GLEnum.UnsignedByte, p);

        if (nearest)
        {
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
            // No mipmaps — nearest filtering is used for pixel-art style textures
            // where mip-blending would blur the crisp edges.
        }
        else
        {
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
            gl.GenerateMipmap(GLEnum.Texture2D);
        }

        gl.BindTexture(GLEnum.Texture2D, 0);
        return tex;
    }

    // ── Node-tree traversal ───────────────────────────────────────────────────

    /// <summary>
    /// Builds a SceneObject for <paramref name="node"/> and recurses into children.
    /// Returns null for pure mesh-display nodes that are direct children of a bone
    /// — their meshes are absorbed into the parent bone instead of creating a
    /// separate scene object.
    /// </summary>
    private static unsafe SceneObject? BuildNodeTree(
        AiAssimp assimp,
        GL gl,
        AiScene* scene,
        AiNode* node,
        HashSet<string> boneNames,
        Dictionary<string, uint> texCache,
        string sourceFilePath,
        Quaternion parentBoneQuat = default,
        SceneObject? parentObj = null)
    {
        string nodeName = node->MName.AsString;
        bool isBone = boneNames.Contains(nodeName);
        bool hasMesh = node->MNumMeshes > 0;
        bool isMeshChildOfBone = hasMesh && !isBone && parentBoneQuat != default && parentObj != null;

        // Decompose local transform (transpose: Assimp col4-translation → row4-translation).
        Matrix4x4 local = Matrix4x4.Transpose(node->MTransformation);
        Matrix4x4.Decompose(local, out Vector3 lscale, out Quaternion lquat, out Vector3 ltrans);
        vec3 pos   = new vec3(ltrans.X, ltrans.Y, ltrans.Z);
        vec3 scale = new vec3(lscale.X, lscale.Y, lscale.Z);

        if (isMeshChildOfBone)
        {
            // This node exists only to hold the display mesh for its bone parent.
            // We keep it as a real child SceneObject so its rotation is applied
            // correctly through GetWorldMatrix, but hide it from the scene tree
            // and make it non-selectable so the user only interacts with the bone.
            var meshObj = new SceneObject
            {
                Name            = string.IsNullOrEmpty(nodeName) ? "Mesh" : nodeName,
                ObjectType      = "Mesh",
                SpawnCategory   = "Custom Models",
                SourceAssetPath = sourceFilePath,
                PivotOffset     = vec3.Zero,
                IsSelectable    = false,
                HideInSceneTree = true,
            };
            meshObj.AssignObjectId();

            // adjusted = Inverse(boneQuat) * meshQuat — absorbs the zeroed bone rotation.
            Quaternion adjusted = Quaternion.Multiply(Quaternion.Inverse(parentBoneQuat), lquat);
            meshObj.SetLocalPosition(pos);
            meshObj.SetLocalRotation(QuaternionToEulerXYZ(adjusted));
            meshObj.SetLocalScale(scale);

            for (uint mi = 0; mi < node->MNumMeshes; mi++)
            {
                Mesh? glMesh = BuildMesh(assimp, gl, scene, scene->MMeshes[node->MMeshes[mi]], texCache);
                if (glMesh != null)
                    meshObj.AddMesh(glMesh);
            }

            for (uint ci = 0; ci < node->MNumChildren; ci++)
            {
                SceneObject? child = BuildNodeTree(assimp, gl, scene, node->MChildren[ci],
                    boneNames, texCache, sourceFilePath);
                if (child != null) meshObj.AddChild(child);
            }

            return meshObj; // caller adds it as a hidden child of the bone
        }

        // ── Normal node or bone ────────────────────────────────────────────────

        SceneObject obj = isBone
            ? new BoneSceneObject { BoneName = nodeName }
            : new SceneObject();

        obj.Name            = string.IsNullOrEmpty(nodeName) ? "Node" : nodeName;
        obj.ObjectType      = isBone ? "Bone" : "Node";
        obj.SpawnCategory   = "Custom Models";
        obj.SourceAssetPath = sourceFilePath;
        obj.PivotOffset     = vec3.Zero;
        obj.AssignObjectId();

        Quaternion boneQuatForChildren = default;

        if (isBone)
        {
            // Bone: show at zero rotation.  Pass our raw quaternion down so
            // direct mesh children can absorb the compensation.
            obj.SetLocalPosition(pos);
            obj.SetLocalRotation(vec3.Zero);
            obj.SetLocalScale(scale);
            boneQuatForChildren = lquat;

            // Build the small octahedron visual indicator for this bone.
            ((BoneSceneObject)obj).CreateIndicator(gl);
        }
        else
        {
            obj.SetLocalPosition(pos);
            obj.SetLocalRotation(QuaternionToEulerXYZ(lquat));
            obj.SetLocalScale(scale);
        }

        // Meshes on non-bone nodes (e.g. body mesh parented directly to Body bone).
        for (uint mi = 0; mi < node->MNumMeshes; mi++)
        {
            Mesh? glMesh = BuildMesh(assimp, gl, scene, scene->MMeshes[node->MMeshes[mi]], texCache);
            if (glMesh != null)
                obj.AddMesh(glMesh);
        }

        // Recurse into children.
        for (uint ci = 0; ci < node->MNumChildren; ci++)
        {
            SceneObject? child = BuildNodeTree(
                assimp, gl, scene, node->MChildren[ci],
                boneNames, texCache, sourceFilePath,
                parentBoneQuat: isBone ? boneQuatForChildren : default,
                parentObj: isBone ? obj : null);

            if (child != null)
                obj.AddChild(child);
        }

        return obj;
    }

    // ── Mesh construction ─────────────────────────────────────────────────────

    private static unsafe Mesh? BuildMesh(
        AiAssimp assimp,
        GL gl,
        AiScene* scene,
        AiMesh* aMesh,
        Dictionary<string, uint> texCache)
    {
        if (aMesh->MNumVertices == 0) return null;

        var mesh = new Mesh(gl);

        bool hasNormals = aMesh->MNormals != null;
        bool hasUVs     = aMesh->MTextureCoords.Element0 != null;

        for (uint vi = 0; vi < aMesh->MNumVertices; vi++)
        {
            var v = aMesh->MVertices[vi];
            mesh.Vertices.Add(new vec3(v.X, v.Y, v.Z));

            if (hasNormals)
            {
                var n = aMesh->MNormals[vi];
                mesh.Normals.Add(new vec3(n.X, n.Y, n.Z));
            }

            if (hasUVs)
            {
                var uv = aMesh->MTextureCoords.Element0[vi];
                mesh.TexCoords.Add(new vec2(uv.X, uv.Y));
            }
        }

        var indices = new List<uint>((int)aMesh->MNumFaces * 3);
        for (uint fi = 0; fi < aMesh->MNumFaces; fi++)
        {
            ref AiFace face = ref aMesh->MFaces[fi];
            for (uint ii = 0; ii < face.MNumIndices; ii++)
                indices.Add(face.MIndices[ii]);
        }
        mesh.Indices = indices.ToArray();

        if (aMesh->MMaterialIndex < scene->MNumMaterials)
        {
            AiMaterial* mat = scene->MMaterials[aMesh->MMaterialIndex];
            uint texCount = assimp.GetMaterialTextureCount(mat, Silk.NET.Assimp.TextureType.Diffuse);
            if (texCount > 0)
            {
                Silk.NET.Assimp.AssimpString path = default;
                assimp.GetMaterialTexture(mat, Silk.NET.Assimp.TextureType.Diffuse, 0,
                    ref path, null, null, null, null, null, null);
                string key = path.AsString;
                if (!string.IsNullOrEmpty(key) && texCache.TryGetValue(key, out uint texId))
                    mesh.TextureId = texId;
            }
        }

        mesh.Upload();
        return mesh;
    }

    // ── Quaternion to Euler ───────────────────────────────────────────────────

    private static vec3 QuaternionToEulerXYZ(Quaternion q)
    {
        float sinRcosP = 2f * (q.W * q.X + q.Y * q.Z);
        float cosRcosP = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        float roll  = MathF.Atan2(sinRcosP, cosRcosP);

        float sinP  = 2f * (q.W * q.Y - q.Z * q.X);
        float pitch = MathF.Abs(sinP) >= 1f
            ? MathF.CopySign(MathF.PI / 2f, sinP)
            : MathF.Asin(sinP);

        float sinYcosP = 2f * (q.W * q.Z + q.X * q.Y);
        float cosYcosP = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
        float yaw   = MathF.Atan2(sinYcosP, cosYcosP);

        return new vec3(roll, pitch, yaw);
    }
}
