using System.Numerics;
using System.Text;
using GlmSharp;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using System.Text.Json.Nodes;
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

        // glTF/GLB morph targets are authored per-primitive and map 1:1 to the
        // original vertex list.  JoinIdenticalVertices welds duplicate vertices
        // and changes that order/count, so shape-key deltas end up applied to
        // the wrong vertices and the mesh tears apart.  Skip it for glTF/GLB.
        string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        bool isGltf = ext is ".gltf" or ".glb";

        uint flags =
            (uint)Silk.NET.Assimp.PostProcessSteps.Triangulate           |
            (uint)Silk.NET.Assimp.PostProcessSteps.GenerateSmoothNormals |
            (uint)Silk.NET.Assimp.PostProcessSteps.LimitBoneWeights      |
            // GLTF/Blender UV V=0 is at the top; OpenGL expects V=0 at bottom.
            (uint)Silk.NET.Assimp.PostProcessSteps.FlipUVs;

        if (!isGltf)
            flags |= (uint)Silk.NET.Assimp.PostProcessSteps.JoinIdenticalVertices;

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
            if (isGltf)
                CollectGltfBoneNames(filePath, boneNames);
            else
                CollectAssimpBoneNames(scene, boneNames);

            // ── Upload textures ────────────────────────────────────────────
            string modelDir = System.IO.Path.GetDirectoryName(filePath) ?? "";
            var texCache = new Dictionary<string, uint>();
            UploadTextures(assimp, gl, scene, modelDir, texCache, nearestFilter);

            // ── Detect whether this model uses GPU skinning ─────────────────
            // Skinned meshes carry bone weight attributes (AiMesh.MBones > 0).
            // When skinning is present we keep bones in their authored pose and
            // extract JOINTS_0/WEIGHTS_0/inverse-bind data instead of treating
            // the mesh as a rigid child of a bone.
            bool hasSkinnedMeshes = false;
            for (uint mi = 0; mi < scene->MNumMeshes; mi++)
            {
                if (scene->MMeshes[mi]->MNumBones > 0)
                {
                    hasSkinnedMeshes = true;
                    break;
                }
            }

            // ── Build the node hierarchy ───────────────────────────────────
            // Load glTF morph-target data (shape keys) for the meshes in this
            // scene.  Assimp's morph-target support is unreliable for glTF, so
            // we parse the JSON + BIN chunks ourselves and attach the per-vertex
            // deltas to each Mesh that was built from a glTF primitive.
            GltfShapeKeySource? shapeKeySource = LoadGltfShapeKeySource(filePath);

            SceneObject? root = BuildNodeTree(
                assimp, gl, scene, scene->MRootNode,
                boneNames, texCache, filePath, hasSkinnedMeshes, shapeKeySource);

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

            var root = JsonNode.Parse(json)?.AsObject();
            if (root == null) return;

            // nodes array: index → name
            var nodes = root["nodes"] as JsonArray;
            if (nodes == null) return;

            var skins = root["skins"] as JsonArray;
            if (skins == null) return;

            foreach (var skin in skins)
            {
                var joints = skin?["joints"] as JsonArray;
                if (joints == null) continue;
                foreach (var joint in joints)
                {
                    int idx = joint?.GetValue<int>() ?? -1;
                    if (idx >= 0 && idx < nodes.Count)
                    {
                        string? name = nodes[idx]?["name"]?.GetValue<string>();
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
    /// separate scene object.  This rigid-body behaviour is disabled when
    /// <paramref name="hasSkinnedMeshes"/> is true so that authored bone poses and
    /// GPU skinning are preserved.
    /// </summary>
    private static unsafe SceneObject? BuildNodeTree(
        AiAssimp assimp,
        GL gl,
        AiScene* scene,
        AiNode* node,
        HashSet<string> boneNames,
        Dictionary<string, uint> texCache,
        string sourceFilePath,
        bool hasSkinnedMeshes,
        GltfShapeKeySource? shapeKeySource,
        Quaternion parentBoneQuat = default,
        SceneObject? parentObj = null)
    {
        string nodeName = node->MName.AsString;
        bool isBone = boneNames.Contains(nodeName);
        bool hasMesh = node->MNumMeshes > 0;
        bool isMeshChildOfBone = !hasSkinnedMeshes && hasMesh && !isBone && parentBoneQuat != default && parentObj != null;

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
                uint meshIdx = node->MMeshes[mi];
                Mesh? glMesh = BuildMesh(assimp, gl, scene, scene->MMeshes[meshIdx], meshIdx, texCache, shapeKeySource);
                if (glMesh != null)
                    meshObj.AddMesh(glMesh);
            }

            for (uint ci = 0; ci < node->MNumChildren; ci++)
            {
                SceneObject? child = BuildNodeTree(assimp, gl, scene, node->MChildren[ci],
                    boneNames, texCache, sourceFilePath, hasSkinnedMeshes, shapeKeySource);
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
            if (hasSkinnedMeshes)
            {
                // Skinned meshes need the skeleton in its authored bind pose so the
                // inverse bind matrices and bone weights line up correctly.
                obj.SetLocalPosition(pos);
                obj.SetLocalRotation(QuaternionToEulerXYZ(lquat));
                obj.SetLocalScale(scale);
            }
            else
            {
                // Rigid-body rigs: show the bone at zero rotation and pass the
                // raw quaternion down so direct mesh children can absorb it.
                obj.SetLocalPosition(pos);
                obj.SetLocalRotation(vec3.Zero);
                obj.SetLocalScale(scale);
                boneQuatForChildren = lquat;
            }

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
            uint meshIdx = node->MMeshes[mi];
            Mesh? glMesh = BuildMesh(assimp, gl, scene, scene->MMeshes[meshIdx], meshIdx, texCache, shapeKeySource);
            if (glMesh != null)
                obj.AddMesh(glMesh);
        }

        // Recurse into children.
        for (uint ci = 0; ci < node->MNumChildren; ci++)
        {
            SceneObject? child = BuildNodeTree(
                assimp, gl, scene, node->MChildren[ci],
                boneNames, texCache, sourceFilePath, hasSkinnedMeshes, shapeKeySource,
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
        uint meshIndex,
        Dictionary<string, uint> texCache,
        GltfShapeKeySource? shapeKeySource)
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

        // ── Skinning data ───────────────────────────────────────────────────
        if (aMesh->MNumBones > 0)
        {
            if (aMesh->MNumBones > 64)
            {
                Console.Error.WriteLine(
                    $"[AssimpModelLoader] Mesh has {aMesh->MNumBones} bones; " +
                    "only the first 64 will be used by the GPU skinning shader.");
            }

            uint boneCount = aMesh->MNumBones;
            for (uint bi = 0; bi < boneCount; bi++)
            {
                AiBone* bone = aMesh->MBones[bi];
                mesh.BoneNames.Add(bone->MName.AsString);
                mesh.BoneInverseBindMatrices.Add(ToGlmMat4(Matrix4x4.Transpose(bone->MOffsetMatrix)));
            }

            int vertexCount = mesh.Vertices.Count;
            for (int vi = 0; vi < vertexCount; vi++)
            {
                mesh.BoneIndices.Add(new ivec4(0, 0, 0, 0));
                mesh.BoneWeights.Add(new vec4(0f, 0f, 0f, 0f));
            }

            for (uint bi = 0; bi < boneCount; bi++)
            {
                AiBone* bone = aMesh->MBones[bi];
                int boneIdx = (int)bi;
                for (uint wi = 0; wi < bone->MNumWeights; wi++)
                {
                    var weight = bone->MWeights[wi];
                    int vertexId = (int)weight.MVertexId;
                    float w = weight.MWeight;

                    for (int slot = 0; slot < 4; slot++)
                    {
                        if (mesh.BoneWeights[vertexId][slot] == 0f)
                        {
                            mesh.BoneIndices[vertexId] = SetBoneIndex(mesh.BoneIndices[vertexId], slot, boneIdx);
                            mesh.BoneWeights[vertexId] = SetBoneWeight(mesh.BoneWeights[vertexId], slot, w);
                            break;
                        }
                    }
                }
            }

            for (int vi = 0; vi < vertexCount; vi++)
            {
                vec4 w = mesh.BoneWeights[vi];
                float sum = w.x + w.y + w.z + w.w;
                if (sum > 0f)
                    mesh.BoneWeights[vi] = w / sum;
            }
        }

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

        // Attach glTF shape keys (morph targets) if this mesh was built from a
        // glTF primitive and the file declares morph targets.  Looked up by
        // Assimp's mesh index, which corresponds to the order of meshes in the
        // glTF document.
        if (shapeKeySource != null &&
            shapeKeySource.MeshIndexToShapeKeys.TryGetValue((int)meshIndex, out var keys))
        {
            string meshName = aMesh->MName.AsString;
            int added = 0;
            foreach (var (name, deltas, _) in keys)
            {
                if (deltas.Length / 3 != mesh.Vertices.Count)
                {
                    Console.Error.WriteLine(
                        $"[AssimpModelLoader] Mesh #{meshIndex} '{meshName}': shape key '{name}' has " +
                        $"{deltas.Length / 3} deltas but mesh has {mesh.Vertices.Count} vertices — skipped.");
                    continue;
                }

                mesh.AddShapeKey(name, deltas);
                added++;
            }

            if (added > 0)
                Console.WriteLine($"[AssimpModelLoader] Mesh #{meshIndex} '{meshName}': loaded {added} shape key(s).");
        }

        return mesh;
    }

    // ── Quaternion to Euler ───────────────────────────────────────────────────

    private static vec3 QuaternionToEulerXYZ(Quaternion q)
    {
        float sinP = 2f * (q.W * q.Y - q.Z * q.X);

        // Gimbal lock: when pitch is exactly (or very nearly) +/-90 degrees, roll and
        // yaw become coupled (only their combined effect is defined) and the general
        // formulas below degenerate — their numerator/denominator pairs both tend to
        // zero, making atan2 numerically unstable and, critically, NOT guaranteed to
        // reproduce the original rotation when recomposed as Rz*Ry*Rx. This is not a
        // rare edge case: cube/blocky rigs (e.g. Minecraft-style GLB exports) very
        // commonly author bone rotations that land exactly on a 90-degree pitch, which
        // previously caused those bones (and everything parented under them) to be
        // rotated completely wrong — scattering the skeleton. Fix it by pinning
        // roll = 0 and folding the remaining rotation entirely into yaw, which exactly
        // reproduces the original matrix at the pole (verified analytically and
        // numerically against Rz*Ry*Rx recomposition).
        const float PoleEpsilon = 1e-6f;
        if (MathF.Abs(sinP) >= 1f - PoleEpsilon)
        {
            float pitchAtPole = MathF.CopySign(MathF.PI / 2f, sinP);
            float yawAtPole = MathF.Atan2(
                2f * (q.W * q.Z - q.X * q.Y),
                1f - 2f * (q.X * q.X + q.Z * q.Z));
            return new vec3(0f, pitchAtPole, yawAtPole);
        }

        float sinRcosP = 2f * (q.W * q.X + q.Y * q.Z);
        float cosRcosP = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        float roll  = MathF.Atan2(sinRcosP, cosRcosP);

        float pitch = MathF.Asin(sinP);

        float sinYcosP = 2f * (q.W * q.Z + q.X * q.Y);
        float cosYcosP = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
        float yaw   = MathF.Atan2(sinYcosP, cosYcosP);

        return new vec3(roll, pitch, yaw);
    }

    // ── Matrix / skinning helpers ─────────────────────────────────────────────

    private static mat4 ToGlmMat4(Matrix4x4 m)
    {
        // System.Numerics.Matrix4x4 is row-major (DirectX-style).  GlmSharp mat4
        // is column-major (OpenGL-style).  Map each row of the System.Numerics
        // matrix into a column of the GlmSharp matrix.
        return new mat4(
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44);
    }

    private static ivec4 SetBoneIndex(ivec4 v, int slot, int value)
    {
        return slot switch
        {
            0 => new ivec4(value, v.y, v.z, v.w),
            1 => new ivec4(v.x, value, v.z, v.w),
            2 => new ivec4(v.x, v.y, value, v.w),
            3 => new ivec4(v.x, v.y, v.z, value),
            _ => v
        };
    }

    private static vec4 SetBoneWeight(vec4 v, int slot, float value)
    {
        return slot switch
        {
            0 => new vec4(value, v.y, v.z, v.w),
            1 => new vec4(v.x, value, v.z, v.w),
            2 => new vec4(v.x, v.y, value, v.w),
            3 => new vec4(v.x, v.y, v.z, value),
            _ => v
        };
    }

    // ── glTF shape-key (morph target) loading ─────────────────────────────────

    /// <summary>
    /// Per-file cache of morph-target data, indexed by Assimp's glTF-mesh
    /// index.  Each entry is a list of (name, raw morph-target values, flag)
    /// tuples.  <c>IsAbsolute</c> is true when the exporter stored absolute
    /// positions instead of the glTF-standard displacements; the actual
    /// delta is computed in <see cref="BuildMesh"/> by subtracting the base
    /// mesh positions.
    /// </summary>
    private sealed class GltfShapeKeySource
    {
        public Dictionary<int, List<(string Name, float[] Values, bool IsAbsolute)>> MeshIndexToShapeKeys = new();
    }

    /// <summary>
    /// Reads a glTF/GLB file directly and extracts morph-target (shape key)
    /// data.  Assimp's <c>AiAnimMesh</c> support is unreliable for glTF, so
    /// we parse the JSON + BIN chunks ourselves.  The returned source is
    /// null when the file is not a glTF variant, the file cannot be read, or
    /// no morph targets are declared.
    /// </summary>
    private static GltfShapeKeySource? LoadGltfShapeKeySource(string filePath)
    {
        try
        {
            string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            if (ext is not ".gltf" and not ".glb") return null;

            string json;
            byte[]? binChunk = null;

            if (ext == ".glb")
            {
                // GLB: 12-byte header + JSON chunk + BIN chunk.
                // Header: magic(4) + version(4) + length(4)
                // Each chunk: chunkLength(4) + chunkType(4) + chunkData(chunkLength)
                using var fs = SysFile.OpenRead(filePath);
                using var br = new System.IO.BinaryReader(fs);
                if (fs.Length < 12) return null;
                br.ReadBytes(12);
                int jsonLen = (int)br.ReadUInt32();
                uint chunkType = br.ReadUInt32();
                if (chunkType != 0x4E4F534A) return null; // "JSON"
                byte[] jsonBytes = br.ReadBytes(jsonLen);
                if (jsonBytes.Length != jsonLen) return null;
                json = Encoding.UTF8.GetString(jsonBytes);

                if (fs.Position < fs.Length)
                {
                    int binLen = (int)br.ReadUInt32();
                    uint binType = br.ReadUInt32();
                    if (binType == 0x004E4942) // "BIN\0"
                        binChunk = br.ReadBytes(binLen);
                }
            }
            else
            {
                json = SysFile.ReadAllText(filePath);
                // .gltf files reference an external .bin; resolve it relative
                // to the .gltf path.
                string? binUri = null;
                var rootNode = JsonNode.Parse(json)?.AsObject();
                if (rootNode != null && rootNode["buffers"] is JsonArray buffers && buffers.Count > 0)
                {
                    binUri = buffers[0]?["uri"]?.GetValue<string>();
                }
                if (!string.IsNullOrEmpty(binUri) &&
                    !binUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    string binPath = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(filePath) ?? "", binUri);
                    if (SysFile.Exists(binPath))
                        binChunk = SysFile.ReadAllBytes(binPath);
                }
            }

            var root = JsonNode.Parse(json)?.AsObject();
            if (root == null) return null;

            var meshesArr = root["meshes"] as JsonArray;
            if (meshesArr == null) return null;

            var accessors = root["accessors"] as JsonArray;
            var bufferViews = root["bufferViews"] as JsonArray;

            var source = new GltfShapeKeySource();

            // Walk every primitive in every mesh and extract POSITION deltas
            // from its morph targets.  Names are pulled from the mesh's
            // "extras.targetNames" array (standard glTF location), falling
            // back to the primitive's "extras.targetNames" if needed.
            int assimpMeshIndex = 0;
            for (int mi = 0; mi < meshesArr.Count; mi++)
            {
                var meshNode = meshesArr[mi]?.AsObject();
                var primitives = meshNode?["primitives"] as JsonArray;
                if (primitives == null) continue;

                // Read mesh-level morph target names (standard location)
                var meshTargetNames = (meshNode?["extras"]?["targetNames"] as JsonArray)?
                    .Select(n => n?.GetValue<string>() ?? "")
                    .ToList();

                for (int pi = 0; pi < primitives.Count; pi++)
                {
                    var prim = primitives[pi]?.AsObject();
                    if (prim == null) { assimpMeshIndex++; continue; }

                    var targets = prim["targets"] as JsonArray;
                    if (targets == null || targets.Count == 0)
                    {
                        assimpMeshIndex++;
                        continue;
                    }

                    // Fall back to primitive-level names if mesh-level not present
                    var targetNames = meshTargetNames ??
                        (prim["extras"]?["targetNames"] as JsonArray)?
                            .Select(n => n?.GetValue<string>() ?? "")
                            .ToList();

                    // Assimp's glTF2 importer does NOT keep each primitive's
                    // vertex attributes in raw accessor order. It compacts
                    // them into the order vertices are first referenced
                    // while walking the primitive's own index/face buffer —
                    // this happens unconditionally, independent of the
                    // JoinIdenticalVertices post-process flag. Morph-target
                    // deltas are authored in the raw accessor order, so they
                    // must be permuted the same way before being handed to
                    // Mesh.AddShapeKey, or a shape key ends up displacing
                    // whatever vertex happens to occupy that slot in
                    // Assimp's reordered array instead of the intended one —
                    // "deforms the wrong vertices" rather than the right
                    // ones by the right (or even wrong) amount.
                    int? basePositionAccessor = (prim["attributes"]?.AsObject())?["POSITION"]?.GetValue<int>();
                    int primVertexCount = (accessors != null && basePositionAccessor.HasValue &&
                        basePositionAccessor.Value >= 0 && basePositionAccessor.Value < accessors.Count)
                        ? (accessors[basePositionAccessor.Value]?.AsObject()?["count"]?.GetValue<int>() ?? 0)
                        : 0;

                    int[]? rawToAssimpIndex = (primVertexCount > 0 && accessors != null && bufferViews != null)
                        ? BuildGltfVertexPermutation(accessors, bufferViews, prim, binChunk, primVertexCount)
                        : null;

                    if (primVertexCount > 0 && rawToAssimpIndex == null)
                    {
                        Console.Error.WriteLine(
                            $"[AssimpModelLoader] glTF mesh/primitive at assimpMeshIndex={assimpMeshIndex}: " +
                            "could not reconstruct Assimp's vertex reordering; shape keys for this " +
                            "primitive may deform the wrong vertices.");
                    }

                    var keyList = new List<(string, float[], bool)>();
                    for (int ti = 0; ti < targets.Count; ti++)
                    {
                        var target = targets[ti]?.AsObject();
                        if (target == null || !target.ContainsKey("POSITION")) continue;

                        int positionAccessor = target["POSITION"]?.GetValue<int>() ?? -1;
                        if (positionAccessor < 0) continue;

                        string name = (targetNames != null && ti < targetNames.Count && !string.IsNullOrEmpty(targetNames[ti]))
                            ? targetNames[ti]
                            : $"Morph {ti}";

                        float[]? morphPositions = ReadVec3Accessor(root, binChunk, positionAccessor);
                        if (morphPositions == null) continue;

                        // Remap from raw glTF accessor order into Assimp's
                        // actual per-vertex order so the delta at index v
                        // lands on the same vertex as mesh.Vertices[v].
                        if (rawToAssimpIndex != null && morphPositions.Length / 3 == rawToAssimpIndex.Length)
                        {
                            float[] remapped = new float[morphPositions.Length];
                            for (int rawIdx = 0; rawIdx < rawToAssimpIndex.Length; rawIdx++)
                            {
                                int newIdx = rawToAssimpIndex[rawIdx];
                                remapped[newIdx * 3 + 0] = morphPositions[rawIdx * 3 + 0];
                                remapped[newIdx * 3 + 1] = morphPositions[rawIdx * 3 + 1];
                                remapped[newIdx * 3 + 2] = morphPositions[rawIdx * 3 + 2];
                            }
                            morphPositions = remapped;
                        }

                        // glTF morph targets are always per-vertex displacements (deltas).
                        keyList.Add((name, morphPositions, false));
                    }

                    if (keyList.Count > 0)
                        source.MeshIndexToShapeKeys[assimpMeshIndex] = keyList;

                    assimpMeshIndex++;
                }
            }

            return source.MeshIndexToShapeKeys.Count > 0 ? source : null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AssimpModelLoader] glTF shape-key parse failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reconstructs the permutation Assimp's glTF2 importer applies to a
    /// primitive's per-vertex attribute arrays: rather than keeping the raw
    /// accessor order, Assimp assigns output vertex index 0 to whichever
    /// input vertex is <em>first referenced</em> while scanning the
    /// primitive's index buffer, index 1 to the next distinct vertex
    /// referenced, and so on. This happens unconditionally (it is not tied
    /// to the JoinIdenticalVertices post-process step), so any per-vertex
    /// data authored in raw accessor order — such as morph-target deltas —
    /// must be run through this same mapping before it lines up with
    /// <c>mesh.Vertices</c>. Returns a <c>rawIndex -> assimpIndex</c> map of
    /// length <paramref name="vertexCount"/>, or <c>null</c> if it can't be
    /// determined (e.g. non-indexed primitive, unreadable index buffer, or
    /// any input vertex that is never referenced by a face — in which case
    /// Assimp's own behaviour can't be reliably reproduced from this data
    /// alone).
    /// </summary>
    private static int[]? BuildGltfVertexPermutation(
        JsonArray accessors, JsonArray bufferViews, JsonObject prim, byte[]? binChunk, int vertexCount)
    {
        try
        {
            if (binChunk == null) return null;

            var indicesNode = prim["indices"];
            if (indicesNode == null) return null; // non-indexed: Assimp uses accessor order as-is

            int indicesAccessorIndex = indicesNode.GetValue<int>();
            if (indicesAccessorIndex < 0 || indicesAccessorIndex >= accessors.Count) return null;

            var idxAcc = accessors[indicesAccessorIndex]?.AsObject();
            if (idxAcc == null) return null;

            int idxCount = idxAcc["count"]?.GetValue<int>() ?? 0;
            if (idxCount <= 0) return null;

            int idxComponentType = idxAcc["componentType"]?.GetValue<int>() ?? 5123;
            int idxBufferViewIndex = idxAcc["bufferView"]?.GetValue<int>() ?? -1;
            int idxByteOffset = idxAcc["byteOffset"]?.GetValue<int>() ?? 0;
            if (idxBufferViewIndex < 0 || idxBufferViewIndex >= bufferViews.Count) return null;

            var idxBv = bufferViews[idxBufferViewIndex]?.AsObject();
            if (idxBv == null) return null;
            int idxBvByteOffset = idxBv["byteOffset"]?.GetValue<int>() ?? 0;
            int idxBufferIndex = idxBv["buffer"]?.GetValue<int>() ?? 0;
            if (idxBufferIndex != 0) return null;

            int idxElemSize = GltfComponentByteSize(idxComponentType);
            long idxBase = (long)idxBvByteOffset + idxByteOffset;

            int[] rawToNew = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++) rawToNew[i] = -1;

            int nextNewIndex = 0;
            for (int i = 0; i < idxCount; i++)
            {
                long pos = idxBase + (long)i * idxElemSize;
                if (pos + idxElemSize > binChunk.Length) return null;
                int rawIdx = (int)ReadGltfComponent(binChunk, pos, idxComponentType);
                if (rawIdx < 0 || rawIdx >= vertexCount) return null;
                if (rawToNew[rawIdx] == -1)
                    rawToNew[rawIdx] = nextNewIndex++;
            }

            // Every vertex must be referenced by at least one face for this
            // reconstruction to be complete; if not, bail out rather than
            // guess (leaving some entries at -1 would corrupt the delta
            // remap) — callers fall back to unpermuted deltas with a warning.
            if (nextNewIndex != vertexCount) return null;

            return rawToNew;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a VEC3 accessor from the glTF document and returns its values
    /// as a flat float array of length <c>count * 3</c>.  Supports
    /// <c>5126</c> (FLOAT) and <c>5121</c>/<c>5122</c>/<c>5123</c>
    /// (unsigned byte/short/int) component types, and both the dense
    /// (<c>bufferView</c>) and <c>sparse</c> accessor encodings defined by
    /// the glTF spec.  Morph-target <c>POSITION</c> accessors are commonly
    /// exported as sparse-only (no top-level <c>bufferView</c> at all)
    /// because each individual shape key usually only displaces a handful
    /// of vertices — failing to handle that encoding silently drops almost
    /// every shape key in a typical export.  Missing/implicit elements
    /// default to (0,0,0) as required by the spec.  Only handles the single
    /// embedded BIN chunk that this loader actually decodes.
    /// </summary>
    private static float[]? ReadVec3Accessor(JsonObject root, byte[]? binChunk, int accessorIndex)
    {
        try
        {
            var accessors = root["accessors"] as JsonArray;
            var bufferViews = root["bufferViews"] as JsonArray;
            if (accessors == null || bufferViews == null) return null;
            if (accessorIndex < 0 || accessorIndex >= accessors.Count) return null;

            var acc = accessors[accessorIndex]?.AsObject();
            if (acc == null) return null;
            string type = acc["type"]?.GetValue<string>() ?? "";
            if (type != "VEC3") return null;

            int count = acc["count"]?.GetValue<int>() ?? 0;
            if (count <= 0) return null;

            int componentType = acc["componentType"]?.GetValue<int>() ?? 5126;
            int bufferViewIndex = acc["bufferView"]?.GetValue<int>() ?? -1;
            int byteOffset = acc["byteOffset"]?.GetValue<int>() ?? 0;

            // Every element defaults to (0,0,0); a dense bufferView (if any)
            // fills these in below, and a sparse overlay (if any) patches a
            // subset of them afterwards. This matches the glTF spec, under
            // which an accessor may have neither, either, or both.
            float[] result = new float[count * 3];

            if (bufferViewIndex >= 0)
            {
                if (bufferViewIndex >= bufferViews.Count) return null;
                if (!ReadVec3Dense(bufferViews, binChunk, bufferViewIndex, byteOffset,
                        componentType, count, result, 0))
                    return null;
            }

            var sparse = acc["sparse"]?.AsObject();
            if (sparse != null)
            {
                if (!ApplyVec3Sparse(bufferViews, binChunk, sparse, componentType, result))
                    return null;
            }
            else if (bufferViewIndex < 0)
            {
                // Neither dense data nor a sparse overlay — nothing to read.
                return null;
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Byte size of one glTF accessor component of <paramref name="componentType"/>.</summary>
    private static int GltfComponentByteSize(int componentType) => componentType switch
    {
        5120 => 1, // BYTE
        5121 => 1, // UNSIGNED_BYTE
        5122 => 2, // SHORT
        5123 => 2, // UNSIGNED_SHORT
        5125 => 4, // UNSIGNED_INT
        5126 => 4, // FLOAT
        _ => 4
    };

    /// <summary>Reads one scalar component value at a byte offset for a VEC3/scalar accessor.</summary>
    private static float ReadGltfComponent(byte[] buf, long pos, int componentType) => componentType switch
    {
        5120 => (sbyte)buf[pos],                          // BYTE (signed)
        5121 => buf[pos],                                 // UNSIGNED_BYTE
        5122 => BitConverter.ToInt16(buf, (int)pos),       // SHORT (signed)
        5123 => BitConverter.ToUInt16(buf, (int)pos),      // UNSIGNED_SHORT
        5125 => BitConverter.ToUInt32(buf, (int)pos),      // UNSIGNED_INT
        _    => BitConverter.ToSingle(buf, (int)pos)       // FLOAT
    };

    /// <summary>
    /// Reads <paramref name="count"/> tightly-strided VEC3 elements starting
    /// at <paramref name="destStartIndex"/> (in element units, not floats)
    /// of <paramref name="destination"/> from a dense bufferView.
    /// </summary>
    private static bool ReadVec3Dense(
        JsonArray bufferViews, byte[]? binChunk, int bufferViewIndex, int byteOffset,
        int componentType, int count, float[] destination, int destStartIndex)
    {
        var bv = bufferViews[bufferViewIndex]?.AsObject();
        if (bv == null) return false;
        int bvByteOffset = bv["byteOffset"]?.GetValue<int>() ?? 0;
        int bvByteStride = bv["byteStride"]?.GetValue<int>() ?? 0;
        int bufferIndex = bv["buffer"]?.GetValue<int>() ?? 0;
        if (bufferIndex != 0 || binChunk == null) return false;

        int elementSize = GltfComponentByteSize(componentType);
        int tupleSize = elementSize * 3;
        int stride = bvByteStride > 0 ? bvByteStride : tupleSize;

        long baseOffset = (long)bvByteOffset + byteOffset;
        for (int i = 0; i < count; i++)
        {
            long elemStart = baseOffset + (long)i * stride;
            for (int c = 0; c < 3; c++)
            {
                long pos = elemStart + c * elementSize;
                if (pos + elementSize > binChunk.Length) return false;
                destination[(destStartIndex + i) * 3 + c] = ReadGltfComponent(binChunk, pos, componentType);
            }
        }
        return true;
    }

    /// <summary>
    /// Applies a glTF <c>accessor.sparse</c> overlay to <paramref name="destination"/>
    /// (a VEC3 array already sized <c>accessor.count * 3</c>).  <c>sparse.indices</c>
    /// gives the element index of each of the <c>sparse.count</c> overridden
    /// elements (as UNSIGNED_BYTE/SHORT/INT); <c>sparse.values</c> gives the
    /// replacement VEC3 values themselves, tightly packed using the
    /// accessor's own <paramref name="componentType"/> as required by the spec.
    /// </summary>
    private static bool ApplyVec3Sparse(
        JsonArray bufferViews, byte[]? binChunk, JsonObject sparse, int componentType, float[] destination)
    {
        int sparseCount = sparse["count"]?.GetValue<int>() ?? 0;
        if (sparseCount <= 0) return true; // nothing to overlay, not an error

        var indicesObj = sparse["indices"]?.AsObject();
        var valuesObj = sparse["values"]?.AsObject();
        if (indicesObj == null || valuesObj == null || binChunk == null) return false;

        int indicesBufferViewIndex = indicesObj["bufferView"]?.GetValue<int>() ?? -1;
        int indicesByteOffset = indicesObj["byteOffset"]?.GetValue<int>() ?? 0;
        int indicesComponentType = indicesObj["componentType"]?.GetValue<int>() ?? 5123;
        if (indicesBufferViewIndex < 0 || indicesBufferViewIndex >= bufferViews.Count) return false;

        int valuesBufferViewIndex = valuesObj["bufferView"]?.GetValue<int>() ?? -1;
        int valuesByteOffset = valuesObj["byteOffset"]?.GetValue<int>() ?? 0;
        if (valuesBufferViewIndex < 0 || valuesBufferViewIndex >= bufferViews.Count) return false;

        var indicesBv = bufferViews[indicesBufferViewIndex]?.AsObject();
        var valuesBv = bufferViews[valuesBufferViewIndex]?.AsObject();
        if (indicesBv == null || valuesBv == null) return false;

        int indicesBvOffset = indicesBv["byteOffset"]?.GetValue<int>() ?? 0;
        int indicesBufferIndex = indicesBv["buffer"]?.GetValue<int>() ?? 0;
        int valuesBvOffset = valuesBv["byteOffset"]?.GetValue<int>() ?? 0;
        int valuesBufferIndex = valuesBv["buffer"]?.GetValue<int>() ?? 0;
        if (indicesBufferIndex != 0 || valuesBufferIndex != 0) return false;

        int indexElemSize = GltfComponentByteSize(indicesComponentType);
        long indicesBase = (long)indicesBvOffset + indicesByteOffset;

        int valueElemSize = GltfComponentByteSize(componentType);
        int valueTupleSize = valueElemSize * 3;
        long valuesBase = (long)valuesBvOffset + valuesByteOffset;

        for (int i = 0; i < sparseCount; i++)
        {
            long idxPos = indicesBase + (long)i * indexElemSize;
            if (idxPos + indexElemSize > binChunk.Length) return false;
            int elementIndex = (int)ReadGltfComponent(binChunk, idxPos, indicesComponentType);
            if (elementIndex < 0 || elementIndex * 3 + 2 >= destination.Length) return false;

            long valStart = valuesBase + (long)i * valueTupleSize;
            for (int c = 0; c < 3; c++)
            {
                long pos = valStart + c * valueElemSize;
                if (pos + valueElemSize > binChunk.Length) return false;
                destination[elementIndex * 3 + c] = ReadGltfComponent(binChunk, pos, componentType);
            }
        }
        return true;
    }
}
