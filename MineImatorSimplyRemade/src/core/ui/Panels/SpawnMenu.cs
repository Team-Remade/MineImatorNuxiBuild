using System.Numerics;
using Cyotek.Data.Nbt;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.material.materials;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using NativeFileDialogSharp;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace MineImatorSimplyRemade.core.ui.Panels;

// ── Atlas source enum ─────────────────────────────────────────────────────────

/// <summary>Which Minecraft texture atlas to source tiles from in the Items spawn tab.</summary>
public enum ItemAtlasSource
{
    ItemAtlas,
    BlockAtlas
}

/// <summary>
/// Four-column spawn menu (Categories | Objects | Variants | Preview) displayed as a
/// floating ImGui window.  Ported from the Nuxi reference project.
///
/// Usage
/// ─────
///  1. Create an instance and assign <see cref="Viewport"/> (the target viewport)
///     and <see cref="Gl"/> (the OpenGL context, inherited from <see cref="UiPanel"/>).
///  2. Call <see cref="Render"/> every frame from the main render loop.
///  3. Call <see cref="Toggle"/> (e.g. from a button handler) to open/close.
/// </summary>
public class SpawnMenu : UiPanel
{
    private const string SceneryLoadLabel = "Load schematic...";

    private static readonly string[] WoolColors =
    {
        "white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray",
        "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black"
    };

    // ── State ────────────────────────────────────────────────────────────────
    private bool _isOpen = false;

    private string _selectedCategory  = "Primitives";
    private int    _selectedObjectIndex  = -1;
    private int    _selectedVariantIndex = -1;

    private string _searchQuery  = "";
    private string _searchBuffer = "";

    // ── Items category state ─────────────────────────────────────────────────

    /// <summary>Which atlas is currently selected in the Items tab.</summary>
    private ItemAtlasSource _itemAtlasSource = ItemAtlasSource.ItemAtlas;

    /// <summary>
    /// Currently selected tile key (e.g. <c>"3,2"</c>), or empty string for none.
    /// </summary>
    private string _selectedTileKey = "";

    /// <summary>When true (default) the spawned mesh is extruded; otherwise flat.</summary>
    private bool _item3DMode = true;

    /// <summary>Search filter applied to the tile grid list.</summary>
    private string _itemSearchBuffer = "";
    private string _itemSearchQuery  = "";

    /// <summary>Counter used to generate unique custom-item keys.</summary>
    private int _customItemTextureCounter = 1;

    // Category → list of object names
    private readonly Dictionary<string, List<string>> _categories;

    // Variant data per-object (populated for Blocks when that system is added)
    private List<string> _currentVariants = new();

    // Custom-model history (in-memory; load/save not yet implemented)
    private readonly List<string>             _customModelHistory = new();
    private readonly Dictionary<string, string> _customModelPaths = new(); // displayName → full path

    // ── Owner reference ──────────────────────────────────────────────────────
    /// <summary>The viewport whose child list receives newly spawned objects.</summary>
    public Viewport? Viewport { get; set; }
    /// <summary>
    /// Optional project manager used to copy externally loaded schematics into
    /// the active project so scene save/load remains portable.
    /// </summary>
    public ProjectManager? ProjectManager { get; set; }
    /// <summary>
    /// Optional preferences panel reference to access spawn behavior preferences
    /// like CopyWorkCameraIntoNewCameras.
    /// </summary>
    public PreferencesPanel? PreferencesPanel { get; set; }

    // ── Window positioning ───────────────────────────────────────────────────
    private Vector2? _nextWindowPos;

    // ── Blocks category state ─────────────────────────────────────────────────

    /// <summary>Search filter applied to the blocks object list.</summary>
    private string _blockSearchBuffer = "";
    private string _blockSearchQuery  = "";

    /// <summary>
    /// Selected resourcepack ID for block/schematic spawning.
    /// Empty means default (base game textures).
    /// </summary>
    private string _spawnResourcePackId = "";

    /// <summary>
    /// Selected source ID for block list filtering.
    /// Empty means default/vanilla block list.
    /// </summary>
    private string _spawnBlockSourceId = "";

    /// <summary>
    /// Selected source ID for item spawning.
    /// Empty means default/base atlas keys (non-external).
    /// </summary>
    private string _spawnItemSourceId = "";

    private readonly List<string> _availableResourcePackIds = new();
    private readonly List<string> _availableSceneryResourcePackIds = new();
    private readonly List<string> _availableSourceModIds = new();
    private readonly List<string> _availableItemSourceIds = new();

    // ── Characters category state ──────────────────────────────────────────────

    /// <summary>Search filter applied to the characters object list.</summary>
    private string _charSearchBuffer = "";
    private string _charSearchQuery  = "";

    /// <summary>
    /// Index into the selected character's <see cref="CharacterEntry.TextureVariants"/> list.
    /// -1 means no explicit selection (use the model's built-in default texture).
    /// </summary>
    private int _selectedCharTextureIndex = -1;

    /// <summary>
    /// Absolute path chosen by the user when the "Custom" texture variant is selected.
    /// Reset whenever the character selection changes.
    /// </summary>
    private string _customCharTexturePath = "";

    // ── Primitive texture state ──────────────────────────────────────────────────

    /// <summary>
    /// Absolute path to the selected texture for spawning textured primitives.
    /// Reset whenever the object selection changes away from textured primitives.
    /// </summary>
    private string _selectedPrimitiveTexturePath = "";

    /// <summary>
    /// OpenGL texture ID for the currently selected primitive texture.
    /// 0 means no texture (use default material).
    /// </summary>
    private uint _selectedPrimitiveTextureId = 0;

    // ── Preview renderer ──────────────────────────────────────────────────────

    /// <summary>Off-screen FBO renderer that draws the preview column content.</summary>
    private PreviewRenderer? _previewRenderer;

    /// <summary>
    /// Meshes currently loaded for the preview.  Rebuilt whenever the selection
    /// key changes.  Disposed with the old meshes before rebuilding.
    /// </summary>
    private List<Mesh> _previewMeshes = new();

    /// <summary>
    /// When the selected category is "Characters" this holds the temporary
    /// <see cref="CharacterSceneObject"/> built purely for the preview FBO.
    /// Its meshes are disposed and it is recreated whenever the selection key changes.
    /// </summary>
    private SceneObject? _previewCharacter;

    /// <summary>
    /// Opaque string identifying the last selection rendered.  When it changes
    /// <see cref="_previewMeshes"/> is rebuilt.
    /// </summary>
    private string _previewKey = "";

    // ── Constructor ──────────────────────────────────────────────────────────
    public SpawnMenu()
    {
        _categories = new Dictionary<string, List<string>>
        {
            { "Camera",     new List<string> { "Camera" } },
            { "Light",      new List<string> { "Point Light" } },
            {
                "Primitives", new List<string>
                {
                    "Empty", "Cube", "Sphere", "Cylinder", "Cone", "Torus", "Plane", "Capsule"
                }
            },
            // Items renders its own custom UI in the objects/variants columns.
            { "Items",        new List<string>() },
            // Blocks: populated from BlockRegistry at render time.
            { "Blocks",       new List<string>() },
            // Characters: populated from CharacterRegistry at render time.
            { "Characters",   new List<string>() },
            { "Scenery",      new List<string> { SceneryLoadLabel } },
            { "Custom Models", new List<string> { "Load..." } }
        };

        UpdateCustomModelsCategory();
        RefreshBlocksCategory();
        RefreshCharactersCategory();
        RefreshResourcePackOptions();
    }

    // ── Blocks category helpers ───────────────────────────────────────────────

    /// <summary>Rebuilds the Blocks category list from <see cref="BlockRegistry"/>.</summary>
    public void RefreshBlocksCategory()
    {
        _categories["Blocks"] = BlockRegistry.Blocks.ToList();
    }

    // ── Characters category helpers ────────────────────────────────────────────

    /// <summary>Rebuilds the Characters category list from <see cref="CharacterRegistry"/>.</summary>
    public void RefreshCharactersCategory()
    {
        _categories["Characters"] = CharacterRegistry.Characters
            .Select(c => c.Name)
            .ToList();
    }

    public void RefreshExternalAssetOptions()
    {
        RefreshBlocksCategory();
        RefreshResourcePackOptions();
    }

    private void RefreshResourcePackOptions()
    {
        _availableResourcePackIds.Clear();
        _availableResourcePackIds.Add("");

        foreach (string id in MinecraftDataLoader.GetAvailableResourcePackIds())
            _availableResourcePackIds.Add(id);

        _availableSceneryResourcePackIds.Clear();
        _availableSceneryResourcePackIds.Add("");

        foreach (string id in MinecraftDataLoader.GetAvailableStandaloneResourcePackIds())
            _availableSceneryResourcePackIds.Add(id);

        _availableSourceModIds.Clear();
        _availableSourceModIds.Add("");

        foreach (string id in MinecraftDataLoader.GetAvailableJavaModIds())
            _availableSourceModIds.Add(id);

        _availableItemSourceIds.Clear();
        _availableItemSourceIds.Add("");

        foreach (string id in MinecraftDataLoader.GetAvailableResourcePackIds())
            _availableItemSourceIds.Add(id);

        _spawnResourcePackId = MinecraftDataLoader.NormalizeResourcePackId(_spawnResourcePackId);
        if (!_availableResourcePackIds.Contains(_spawnResourcePackId, StringComparer.OrdinalIgnoreCase))
            _spawnResourcePackId = "";

        _spawnBlockSourceId = MinecraftDataLoader.NormalizeResourcePackId(_spawnBlockSourceId);
        if (!_availableSourceModIds.Contains(_spawnBlockSourceId, StringComparer.OrdinalIgnoreCase))
            _spawnBlockSourceId = "";

        _spawnItemSourceId = MinecraftDataLoader.NormalizeResourcePackId(_spawnItemSourceId);
        if (!_availableItemSourceIds.Contains(_spawnItemSourceId, StringComparer.OrdinalIgnoreCase))
            _spawnItemSourceId = "";
    }

    private bool RenderResourcePackSelector(
        string idSuffix,
        ref string selectedSourceId,
        string label = "Resource Pack:",
        IReadOnlyList<string>? availableIds = null)
    {
        ImGui.Text(label);
        ImGui.SetNextItemWidth(-1);

        bool changed = false;
        string normalizedSelected = MinecraftDataLoader.NormalizeResourcePackId(selectedSourceId);
        var options = availableIds ?? _availableResourcePackIds;

        int selectedIndex = 0;
        for (int i = 0; i < options.Count; i++)
        {
            if (!string.Equals(options[i], normalizedSelected, StringComparison.OrdinalIgnoreCase))
                continue;

            selectedIndex = i;
            break;
        }

        string selectedLabel = selectedIndex == 0
            ? "Default"
            : options[selectedIndex];

        if (ImGui.BeginCombo($"##resourcePack{idSuffix}", selectedLabel))
        {
            for (int i = 0; i < options.Count; i++)
            {
                string value = options[i];
                string optionLabel = i == 0 ? "Default" : value;
                bool selected = i == selectedIndex;

                if (ImGui.Selectable(optionLabel, selected))
                {
                    selectedSourceId = value;
                    changed = true;
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Toggles the spawn menu open or closed.  When opening, the window is
    /// positioned so its top-left corner is at <paramref name="screenPos"/>.
    /// Pass the bottom of the button that triggered it.
    /// If <paramref name="screenPos"/> is null the window is centred on screen.
    /// </summary>
    public void Toggle(Vector2? screenPos = null)
    {
        if (_isOpen)
        {
            _isOpen = false;
            return;
        }

        RefreshResourcePackOptions();
        _isOpen       = true;
        _nextWindowPos = screenPos;
    }

    public override unsafe void Render()
    {
        if (!_isOpen) return;

        if (_nextWindowPos.HasValue)
        {
            // Clamp so the window doesn't fall off the right/bottom edge.
            var io = ImGui.GetIO();
            float wx = Math.Min(_nextWindowPos.Value.X, io.DisplaySize.X - 1160f);
            float wy = Math.Min(_nextWindowPos.Value.Y, io.DisplaySize.Y - 450f);
            ImGui.SetNextWindowPos(new Vector2(wx, wy), ImGuiCond.Always);
            _nextWindowPos = null; // only force position on the first frame
        }
        else
        {
            // Centre on screen the first time (no explicit anchor given).
            var io = ImGui.GetIO();
            ImGui.SetNextWindowPos(
                new Vector2(io.DisplaySize.X * 0.5f, io.DisplaySize.Y * 0.5f),
                ImGuiCond.Appearing,
                new Vector2(0.5f, 0.5f));
        }

        ImGui.SetNextWindowSize(new Vector2(1150, 440), ImGuiCond.Appearing);

        // Update the off-screen 3-D preview before the window draws
        UpdatePreview(Mesh.DeltaTime);

        if (ImGui.Begin("Spawn Menu##SpawnMenuWindow", ref _isOpen))
        {
            RenderSearchBar();
            ImGui.Separator();
            RenderMainColumns();
            ImGui.Separator();
            RenderBottomBar();
        }

        ImGui.End();
    }

    // ── Private rendering helpers ────────────────────────────────────────────

    private void RenderSearchBar()
    {
        ImGui.Text("Search:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-80);
        if (ImGui.InputText("##search", ref _searchBuffer, 256))
        {
            _searchQuery         = _searchBuffer;
            _selectedObjectIndex  = -1;
            _selectedVariantIndex = -1;
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            _searchBuffer        = "";
            _searchQuery         = "";
            _selectedObjectIndex  = -1;
            _selectedVariantIndex = -1;
        }
    }

    // ── Preview mesh management ───────────────────────────────────────────────

    /// <summary>
    /// Ensures the <see cref="PreviewRenderer"/> is created and initialised,
    /// computes the current selection key, rebuilds <see cref="_previewMeshes"/>
    /// when the selection has changed, and renders them into the preview FBO.
    /// Must be called each frame <em>before</em> the ImGui column that displays
    /// the result, so the FBO texture is ready for <c>ImGui.Image</c>.
    /// </summary>
    private void UpdatePreview(double deltaTime)
    {
        if (Gl == null) return;

        // Lazy-init the renderer
        if (_previewRenderer == null)
        {
            _previewRenderer = new PreviewRenderer(Gl);
            _previewRenderer.Initialize();
        }

        // Compute a key that uniquely identifies what should be previewed
        string newKey = ComputePreviewKey();

        if (newKey != _previewKey)
        {
            _previewKey = newKey;

            // Dispose old preview meshes
            foreach (var m in _previewMeshes) m.Dispose();
            _previewMeshes.Clear();

            // Dispose old preview character meshes
            DisposePreviewCharacter();

            // Build fresh meshes for the new selection
            if (!string.IsNullOrEmpty(newKey))
                _previewMeshes = BuildPreviewMeshes();

            // Tune camera distance to fit the object type
            _previewRenderer.Distance = GetPreviewDistance();
            _previewRenderer.Yaw      = 0.75f;
            _previewRenderer.Pitch    = 0.35f;
        }

        // Render into FBO every frame (so auto-rotation plays)
        _previewRenderer.Render(_previewMeshes, _previewKey, deltaTime, sceneRoot: _previewCharacter);
    }

    /// <summary>Returns a string that uniquely identifies the current selection.</summary>
    private string ComputePreviewKey()
    {
        return _selectedCategory switch
        {
            "Items"  => string.IsNullOrEmpty(_selectedTileKey) ? ""
                        : $"item:{(int)_itemAtlasSource}:{_selectedTileKey}:{_item3DMode}",
            "Blocks" => _selectedObjectIndex < 0 ||
                        _selectedObjectIndex >= BlockRegistry.Blocks.Count ? "" :
                        $"block:{BlockRegistry.Blocks[_selectedObjectIndex]}:" +
                        $"{(_selectedVariantIndex >= 0 ? _selectedVariantIndex : 0)}:" +
                        $"rp:{GetEffectiveBlockTextureSourceId()}",
            "Characters" => _selectedObjectIndex < 0 ||
                            _selectedObjectIndex >= CharacterRegistry.Characters.Count ? "" :
                            $"char:{CharacterRegistry.Characters[_selectedObjectIndex].FilePath}" +
                            $":{_selectedCharTextureIndex}",
            _ => _selectedObjectIndex < 0 ? "" :
                  $"std:{_selectedCategory}:{GetFilteredObjects().ElementAtOrDefault(_selectedObjectIndex) ?? ""}:" +
                  $"rp:{(_selectedCategory == "Scenery" ? MinecraftDataLoader.NormalizeResourcePackId(_spawnResourcePackId) : "")}" 
        };
    }

    /// <summary>Returns a camera distance appropriate for the selected object type.</summary>
    private float GetPreviewDistance()
    {
        return _selectedCategory switch
        {
            "Blocks"     => 2.2f,
            "Items"      => 2.2f,
            "Camera"     => 3.5f,
            "Primitives" => 2.2f,
            "Characters" => 3.5f,
            _            => 2.5f
        };
    }

    /// <summary>
    /// Builds and returns GL meshes for the currently selected item.
    /// Returns an empty list for categories that have no geometry (Camera, Light, Empty, Custom).
    /// </summary>
    private List<Mesh> BuildPreviewMeshes()
    {
        if (Gl == null) return new List<Mesh>();

        switch (_selectedCategory)
        {
            case "Items":
            {
                if (string.IsNullOrEmpty(_selectedTileKey)) return new List<Mesh>();

                uint tileTexId = 0;
                byte[]? tilePixels = null;
                int tileSize;

                if (_itemAtlasSource == ItemAtlasSource.ItemAtlas)
                {
                    ItemsAtlas.Textures.TryGetValue(_selectedTileKey, out tileTexId);
                    ItemsAtlas.TilePixels.TryGetValue(_selectedTileKey, out tilePixels);
                    tileSize = InferTileSizeFromPixels(tilePixels, ItemsAtlas.TileSize);
                }
                else
                {
                    TerrainAtlas.Textures.TryGetValue(_selectedTileKey, out tileTexId);
                    TerrainAtlas.TilePixels.TryGetValue(_selectedTileKey, out tilePixels);
                    tileSize = InferTileSizeFromPixels(tilePixels, TerrainAtlas.TileSize);
                }

                if (tileTexId == 0 || tilePixels == null) return new List<Mesh>();

                var mesh = new ExtrudedItemMesh(
                    Gl, tileTexId, tilePixels,
                    is3D: _item3DMode, tileSize: tileSize, extrudeDepth: 1f / 16f);
                return new List<Mesh> { mesh };
            }

            case "Blocks":
            {
                if (_selectedObjectIndex < 0 ||
                    _selectedObjectIndex >= BlockRegistry.Blocks.Count)
                    return new List<Mesh>();

                string blockName = BlockRegistry.Blocks[_selectedObjectIndex];
                int variantIdx   = _selectedVariantIndex >= 0 ? _selectedVariantIndex : 0;
                var variants     = BlockRegistry.GetVariants(blockName);
                if (variants.Count == 0) return new List<Mesh>();
                if (variantIdx >= variants.Count) variantIdx = 0;

                var variant  = variants[variantIdx];
                var meshes   = new List<Mesh>();

                AppendBlockMeshesForPreview(meshes, variant);
                if (variant.TopHalf != null)
                {
                    // Centre two-block-tall objects vertically (shift down by -0.5)
                    var topMeshes = new List<Mesh>();
                    AppendBlockMeshesForPreview(topMeshes, variant.TopHalf);
                    var shift = new vec3(variant.PartOffsetX,
                                        variant.PartOffsetY - 0.5f,
                                        variant.PartOffsetZ);
                    foreach (var m in topMeshes)
                    {
                        for (int i = 0; i < m.Vertices.Count; i++)
                            m.Vertices[i] += shift;
                        m.Upload();
                    }
                    meshes.AddRange(topMeshes);
                }

                // Centre single-block meshes so they orbit around (0,0,0) nicely:
                // offset vertices down by 0.5 (block origin is at base, centre at 0.5)
                if (variant.TopHalf == null)
                {
                    var downShift = new vec3(0f, -0.5f, 0f);
                    foreach (var m in meshes)
                    {
                        for (int i = 0; i < m.Vertices.Count; i++)
                            m.Vertices[i] += downShift;
                        m.Upload();
                    }
                }

                return meshes;
            }

            case "Primitives":
            {
                var filtered = GetFilteredObjects();
                if (_selectedObjectIndex < 0 || _selectedObjectIndex >= filtered.Count)
                    return new List<Mesh>();

                string name = filtered[_selectedObjectIndex];
                if (name == "Empty") return new List<Mesh>();

                if (name == "Cube")   return new List<Mesh> { new CubeMesh(Gl) };
                if (name == "Plane")  return new List<Mesh> { new PlaneMesh(Gl, 1f, 1f, PlaneOrientation.XY) };

                // For shapes not yet implemented, show a cube placeholder
                return new List<Mesh> { new CubeMesh(Gl) };
            }

            case "Characters":
            {
                if (_selectedObjectIndex < 0 ||
                    _selectedObjectIndex >= CharacterRegistry.Characters.Count)
                    return new List<Mesh>();

                var entry = CharacterRegistry.Characters[_selectedObjectIndex];
                if (string.IsNullOrEmpty(entry.FilePath)) return new List<Mesh>();

                string ext = Path.GetExtension(entry.FilePath).ToLowerInvariant();

                SceneObject? character;

                if (ext == ".mimodel")
                {
                    // Mine Imator native format — load via MineImatorLoader.
                    string? textureOverridePath = ResolveCharacterTextureOverride(entry);

                    var loader = MineImatorLoader.Instance;
                    var model  = loader.LoadModel(entry.FilePath);
                    if (model == null) return new List<Mesh>();

                    var miChar = loader.CreateCharacterFromModel(model);
                    if (miChar == null) return new List<Mesh>();

                    // Apply texture variant if one was selected.
                    if (!string.IsNullOrEmpty(textureOverridePath) && File.Exists(textureOverridePath))
                    {
                        uint overrideTexId = loader.LoadTextureFromFile(textureOverridePath);
                        if (overrideTexId != 0)
                            ApplyTextureOverrideToCharacter(miChar, overrideTexId);
                    }

                    character = miChar;
                }
                else
                {
                    // Binary / standard 3-D format (.glb, .gltf, .fbx, .obj, …) — use Assimp.
                    if (Gl == null) return new List<Mesh>();
                    character = AssimpModelLoader.Load(Gl, entry.FilePath);
                    if (character == null) return new List<Mesh>();
                }

                // Store the hierarchy so PreviewRenderer can render it with proper world matrices.
                _previewCharacter = character;

                // Return an empty flat mesh list; the renderer will walk the hierarchy.
                return new List<Mesh>();
            }

            default:
                return new List<Mesh>();
        }
    }

    /// <summary>
    /// Resolves the texture override path for the currently selected character variant.
    /// Returns null when no override should be applied (use the model's built-in default).
    /// </summary>
    private string? ResolveCharacterTextureOverride(CharacterEntry entry)
    {
        if (_selectedCharTextureIndex < 0 ||
            _selectedCharTextureIndex >= entry.TextureVariants.Count)
            return null;

        var variant = entry.TextureVariants[_selectedCharTextureIndex];
        if (variant.IsCustom)
            return string.IsNullOrEmpty(_customCharTexturePath) ? null : _customCharTexturePath;

        return string.IsNullOrEmpty(variant.FilePath) ? null : variant.FilePath;
    }

    /// <summary>
    /// Disposes all meshes attached to <see cref="_previewCharacter"/> and clears it.
    /// </summary>
    private void DisposePreviewCharacter()
    {
        if (_previewCharacter == null) return;

        foreach (var mesh in _previewCharacter.GetMeshInstancesRecursively())
            mesh.Dispose();

        _previewCharacter = null;
    }

    /// <summary>
    /// Builds block meshes from a <see cref="BlockVariantEntry"/> and appends them
    /// to <paramref name="meshes"/>.  Mirrors <see cref="AddBlockMeshes"/> but does
    /// not attach anything to a SceneObject.
    /// </summary>
    private void AppendBlockMeshesForPreview(List<Mesh> meshes, BlockVariantEntry variant)
    {
        if (Gl == null) return;

        string textureSourceId = GetEffectiveBlockTextureSourceId();

        ResolvedBlockModel? resolved = null;
        if (!string.IsNullOrEmpty(variant.ModelPath))
            resolved = BlockRegistry.ResolveModel(variant.ModelPath);

        List<Mesh> built;
        if (!string.IsNullOrEmpty(variant.CemPath))
            built = CemLoader.Load(Gl, variant.CemPath, BlockRegistry.VersionRoot, textureSourceId);
        else if (resolved != null)
            built = MinecraftModelMesh.Build(Gl, resolved, variant.RotationX, variant.RotationY, textureSourceId);
        else
            built = new List<Mesh>
            {
                MinecraftModelMesh.BuildTexturedFallbackCube(Gl, null, blockNameHint: "", resourcePackId: textureSourceId)
            };

        ApplyVariantRotationToCemMeshes(built, variant);

        meshes.AddRange(built);
    }

    private static void ApplyVariantRotationToCemMeshes(List<Mesh> meshes, BlockVariantEntry variant)
    {
        if (string.IsNullOrEmpty(variant.CemPath))
            return;

        int turnsX = NormalizeQuarterTurns(variant.RotationX);
        int turnsY = NormalizeQuarterTurns(variant.RotationY);
        if (turnsX == 0 && turnsY == 0)
            return;

        bool hasAnyVertex = false;
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float minZ = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        float maxZ = float.MinValue;

        foreach (var mesh in meshes)
        {
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                hasAnyVertex = true;
                var v = mesh.Vertices[i];
                if (v.x < minX) minX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.z < minZ) minZ = v.z;
                if (v.x > maxX) maxX = v.x;
                if (v.y > maxY) maxY = v.y;
                if (v.z > maxZ) maxZ = v.z;
            }
        }

        if (!hasAnyVertex)
            return;

        var pivot = new vec3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);

        foreach (var mesh in meshes)
        {
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                vec3 v = mesh.Vertices[i] - pivot;
                v = RotateQuarterTurnsX(v, turnsX);
                v = RotateQuarterTurnsY(v, turnsY);
                mesh.Vertices[i] = v + pivot;

                if (i < mesh.Normals.Count)
                {
                    vec3 n = mesh.Normals[i];
                    n = RotateQuarterTurnsX(n, turnsX);
                    n = RotateQuarterTurnsY(n, turnsY);
                    mesh.Normals[i] = n;
                }
            }

            mesh.Upload();
        }
    }

    private static int NormalizeQuarterTurns(int degrees)
    {
        int normalized = ((degrees % 360) + 360) % 360;
        return normalized / 90;
    }

    private static vec3 RotateQuarterTurnsX(vec3 v, int turns)
    {
        return turns switch
        {
            1 => new vec3(v.x, -v.z, v.y),
            2 => new vec3(v.x, -v.y, -v.z),
            3 => new vec3(v.x, v.z, -v.y),
            _ => v
        };
    }

    private static vec3 RotateQuarterTurnsY(vec3 v, int turns)
    {
        return turns switch
        {
            1 => new vec3(v.z, v.y, -v.x),
            2 => new vec3(-v.x, v.y, -v.z),
            3 => new vec3(-v.z, v.y, v.x),
            _ => v
        };
    }

    private void RenderMainColumns()
    {
        float totalHeight = ImGui.GetContentRegionAvail().Y - 40; // leave room for bottom bar

        ImGui.BeginChild("##cols", new Vector2(0, totalHeight));
        float columnWidth = ImGui.GetContentRegionAvail().X / 4f;

        // ── Categories ──────────────────────────────────────────────────────
        ImGui.BeginChild("##categories", new Vector2(columnWidth, 0), ImGuiChildFlags.Borders);
        ImGui.TextDisabled("Categories");
        ImGui.Separator();

        foreach (var category in _categories.Keys)
        {
            bool selected = _selectedCategory == category;
            if (ImGui.Selectable(category, selected))
            {
                if (_selectedCategory != category)
                {
                    _selectedCategory          = category;
                    _selectedObjectIndex        = -1;
                    _selectedVariantIndex       = -1;
                    _selectedCharTextureIndex   = -1;
                    _customCharTexturePath      = "";
                    _currentVariants.Clear();
                }
            }
        }

        ImGui.EndChild();
        ImGui.SameLine();

        // ── Objects / Items / Blocks custom UI ───────────────────────────────
        if (_selectedCategory == "Items")
        {
            RenderItemsObjectsColumn(columnWidth);
            ImGui.SameLine();
            RenderItemsVariantsColumn(columnWidth);
            ImGui.SameLine();
            RenderItemsPreviewColumn();
        }
        else if (_selectedCategory == "Blocks")
        {
            RenderBlocksObjectsColumn(columnWidth);
            ImGui.SameLine();
            RenderBlocksVariantsColumn(columnWidth);
            ImGui.SameLine();
            RenderBlocksPreviewColumn();
        }
        else if (_selectedCategory == "Characters")
        {
            RenderCharactersObjectsColumn(columnWidth);
            ImGui.SameLine();
            RenderCharactersVariantsColumn(columnWidth);
            ImGui.SameLine();
            RenderStandardPreviewColumn();
        }
        else
        {
            // Standard objects column
            ImGui.BeginChild("##objects", new Vector2(columnWidth, 0), ImGuiChildFlags.Borders);
            ImGui.TextDisabled("Objects");
            ImGui.Separator();

            if (_categories.TryGetValue(_selectedCategory, out var objectList))
            {
                var filtered = string.IsNullOrEmpty(_searchQuery)
                    ? objectList
                    : objectList
                        .Where(o => o.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                for (int i = 0; i < filtered.Count; i++)
                {
                    bool sel = _selectedObjectIndex == i;
                    if (ImGui.Selectable(filtered[i] + "##obj" + i, sel))
                    {
                        _selectedObjectIndex  = i;
                        _selectedVariantIndex = -1;
                        OnObjectSelected(filtered[i]);
                    }

                    // Double-click spawns immediately (except "Load...")
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        _selectedObjectIndex = i;
                        OnObjectDoubleClicked(filtered[i]);
                    }
                }
            }

            ImGui.EndChild();
            ImGui.SameLine();

            // ── Variants ────────────────────────────────────────────────────
            ImGui.BeginChild("##variants", new Vector2(columnWidth, 0), ImGuiChildFlags.Borders);
            ImGui.TextDisabled("Variants");
            ImGui.Separator();

            if (_selectedCategory == "Scenery")
            {
                RenderResourcePackSelector(
                    "Scenery",
                    ref _spawnResourcePackId,
                    "Resource Pack:",
                    _availableSceneryResourcePackIds);
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            // ── Primitive texture selection ──────────────────────────────────
            var filteredObjects = GetFilteredObjects();
            if (_selectedCategory == "Primitives" &&
                _selectedObjectIndex >= 0 && _selectedObjectIndex < filteredObjects.Count &&
                filteredObjects[_selectedObjectIndex] == "Plane")
            {
                ImGui.TextDisabled("Texture");
                ImGui.Spacing();

                // Show current texture
                if (!string.IsNullOrEmpty(_selectedPrimitiveTexturePath))
                {
                    string fileName = Path.GetFileName(_selectedPrimitiveTexturePath);
                    ImGui.Text($"Current: {fileName}");
                }
                else
                {
                    ImGui.TextDisabled("(None)");
                }

                ImGui.Spacing();

                // Texture buttons
                if (ImGui.Button("Load texture...", new Vector2(-1, 0)))
                {
                    var result = Dialog.FileOpen("png,jpg,jpeg,bmp,tga,gif,webp,tiff");
                    if (result.IsOk && !string.IsNullOrWhiteSpace(result.Path) && File.Exists(result.Path))
                    {
                        // Clean up old texture
                        if (_selectedPrimitiveTextureId != 0)
                            Gl?.DeleteTexture(_selectedPrimitiveTextureId);

                        // Load new texture
                        _selectedPrimitiveTexturePath = result.Path;
                        _selectedPrimitiveTextureId = LoadPrimitiveTextureFromFile(result.Path);

                        if (_selectedPrimitiveTextureId != 0)
                        {
                            _selectedVariantIndex = 1; // Select "Load texture..."
                        }
                        else
                        {
                            _selectedPrimitiveTexturePath = "";
                            _selectedVariantIndex = 0; // Reset to "None"
                        }
                    }
                }

                if (ImGui.Button("Clear", new Vector2(-1, 0)))
                {
                    if (_selectedPrimitiveTextureId != 0)
                        Gl?.DeleteTexture(_selectedPrimitiveTextureId);
                    _selectedPrimitiveTextureId = 0;
                    _selectedPrimitiveTexturePath = "";
                    _selectedVariantIndex = 0;
                }

                ImGui.Separator();
                ImGui.Spacing();
            }

            if (_currentVariants.Count > 0)
            {
                for (int i = 0; i < _currentVariants.Count; i++)
                {
                    bool sel = _selectedVariantIndex == i;
                    if (ImGui.Selectable(_currentVariants[i] + "##var" + i, sel))
                        _selectedVariantIndex = i;

                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        _selectedVariantIndex = i;
                        TrySpawn();
                    }
                }
            }
            else if (!(_selectedCategory == "Primitives" &&
                       _selectedObjectIndex >= 0 && _selectedObjectIndex < filteredObjects.Count &&
                       filteredObjects[_selectedObjectIndex] == "Plane"))
            {
                ImGui.TextDisabled("(not available)");
            }

            ImGui.EndChild();
            ImGui.SameLine();

            // ── Preview ─────────────────────────────────────────────────────
            RenderStandardPreviewColumn();
        }

        ImGui.EndChild(); // ##cols
    }

    // ── Standard (non-Items, non-Blocks) preview column ──────────────────────

    private unsafe void RenderStandardPreviewColumn()
    {
        ImGui.BeginChild("##stdPreview", new Vector2(0, 0), ImGuiChildFlags.Borders);
        ImGui.TextDisabled("Preview");
        ImGui.Separator();

        var filtered = GetFilteredObjects();
        if (_selectedObjectIndex >= 0 && _selectedObjectIndex < filtered.Count)
        {
            string objectName = filtered[_selectedObjectIndex];

            bool hasGeometry = _previewMeshes.Count > 0 &&
                               _previewRenderer != null &&
                               _previewRenderer.ColorTexture != 0;

            ImGui.Spacing();
            RenderPreviewImage(hasGeometry);
            ImGui.Spacing();

            // Centred name label
            CentreText(objectName);
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Select an object\nto see a preview.");
        }

        ImGui.EndChild();
    }

    // ── Items category UI ─────────────────────────────────────────────────────

    private unsafe void RenderItemsObjectsColumn(float columnWidth)
    {
        ImGui.BeginChild("##itemsObjects", new Vector2(columnWidth, 0), ImGuiChildFlags.Borders);
        ImGui.TextDisabled("Tiles");
        ImGui.Separator();

        // Atlas source dropdown
        ImGui.Text("Atlas:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        int atlasIdx = (int)_itemAtlasSource;
        string[] atlasNames = ["Item Atlas", "Block Atlas"];
        if (ImGui.Combo("##atlasSource", ref atlasIdx, atlasNames, atlasNames.Length))
        {
            _itemAtlasSource = (ItemAtlasSource)atlasIdx;
            _selectedTileKey = ""; // reset selection when switching atlas
        }

        ImGui.Spacing();

        bool itemSourceChanged = RenderResourcePackSelector(
            "ItemsSource",
            ref _spawnItemSourceId,
            "Source:",
            _availableItemSourceIds);
        if (itemSourceChanged &&
            !string.IsNullOrWhiteSpace(_selectedTileKey) &&
            !IsTextureKeyFromSelectedSource(_selectedTileKey, _spawnItemSourceId))
        {
            _selectedTileKey = "";
        }

        ImGui.Spacing();

        // Search filter
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##itemSearch", "Filter tiles (e.g. grass)...", ref _itemSearchBuffer, 64))
            _itemSearchQuery = _itemSearchBuffer;

        ImGui.Separator();

        // Tile grid – show all available tiles as icon buttons in a grid
        const float iconSize = 28f;
        float availWidth = ImGui.GetContentRegionAvail().X;
        int   cols       = Math.Max(1, (int)(availWidth / (iconSize + 4f)));

        ImGui.BeginChild("##tileGrid", new Vector2(0, 0));

        if (_itemAtlasSource == ItemAtlasSource.ItemAtlas)
            ItemsAtlas.EnsureProjectCustomTexturesLoaded();

        var textures = _itemAtlasSource == ItemAtlasSource.ItemAtlas
            ? ItemsAtlas.Textures
            : TerrainAtlas.Textures;

        var filteredTextures = textures
            .Where(static kvp => kvp.Value != 0)
            .Where(kvp => IsTextureKeyFromSelectedSource(kvp.Key, _spawnItemSourceId))
            .Where(kvp => string.IsNullOrEmpty(_itemSearchQuery) || kvp.Key.Contains(_itemSearchQuery, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int col = 0;
        foreach (var kvp in filteredTextures)
        {
            string key    = kvp.Key;
            uint   texId  = kvp.Value;

            // Apply search filter
            if (!string.IsNullOrEmpty(_itemSearchQuery) &&
                !key.Contains(_itemSearchQuery, StringComparison.OrdinalIgnoreCase))
                continue;

            bool isSel = _selectedTileKey == key;

            if (col > 0) ImGui.SameLine();
            if (col >= cols) { col = 0; }

            // Highlight selected tile
            if (isSel)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.9f, 0.8f));

            bool clicked = ImGui.ImageButton(
                $"##tile_{key}",
                new ImTextureRef(texId: (ulong)texId),
                new Vector2(iconSize, iconSize),
                new Vector2(0, 0),
                new Vector2(1, 1));

            if (isSel)
                ImGui.PopStyleColor();

            if (clicked)
                _selectedTileKey = key;

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(key);

            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                _selectedTileKey = key;
                TrySpawnItem();
            }

            col++;
            if (col >= cols) col = 0;
        }

        ImGui.EndChild(); // ##tileGrid
        ImGui.EndChild(); // ##itemsObjects
    }

    private void RenderItemsVariantsColumn(float columnWidth)
    {
        ImGui.BeginChild("##itemsVariants", new Vector2(columnWidth, 0), ImGuiChildFlags.Borders);
        ImGui.TextDisabled("Options");
        ImGui.Separator();

        // 3D toggle
        ImGui.Checkbox("3D (extruded)", ref _item3DMode);
        ImGui.Spacing();
        ImGui.TextDisabled(_item3DMode
            ? "Each pixel is extruded\nto form a hull mesh."
            : "Flat double-sided plane\nwith the tile texture.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Load custom image...##itemCustom", new Vector2(-1, 0)))
            ImportCustomItemImageFromDialog(selectInSpawnMenu: true);

        ImGui.EndChild();
    }

    private string? ImportCustomItemImageFromDialog(bool selectInSpawnMenu)
    {
        if (Gl == null)
            return null;

        var result = Dialog.FileOpen("png,jpg,jpeg,bmp,tga,gif,webp,tiff");
        if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path))
            return null;

        string sourcePath = result.Path;
        if (!File.Exists(sourcePath))
            return null;

        string resolvedPath = ResolveItemImagePathForProject(sourcePath, out string? projectRelativePath);

        string key;
        if (!string.IsNullOrWhiteSpace(projectRelativePath))
        {
            key = ItemsAtlas.BuildProjectCustomTextureKey(projectRelativePath);
        }
        else
        {
            string keyBase = Path.GetFileNameWithoutExtension(resolvedPath);
            if (string.IsNullOrWhiteSpace(keyBase))
                keyBase = "custom_item";

            key = $"custom:{SanitizeCustomItemKey(keyBase)}";
            while (ItemsAtlas.Textures.ContainsKey(key))
                key = $"custom:{SanitizeCustomItemKey(keyBase)}_{_customItemTextureCounter++}";
        }

        if (!ItemsAtlas.TryRegisterCustomTextureFromFile(key, resolvedPath))
            return null;

        if (selectInSpawnMenu)
        {
            _itemAtlasSource = ItemAtlasSource.ItemAtlas;
            _selectedTileKey = key;
        }

        return key;
    }

    public string? ImportCustomItemImageFromDialogForProperties()
    {
        return ImportCustomItemImageFromDialog(selectInSpawnMenu: false);
    }

    private string ResolveItemImagePathForProject(string sourcePath, out string? projectRelativePath)
    {
        projectRelativePath = null;

        string fullSourcePath = Path.GetFullPath(sourcePath);
        var projectManager = ProjectManager ?? core.project.ProjectManager.Instance;

        if (projectManager == null || !projectManager.HasProject)
            return fullSourcePath;

        try
        {
            var existing = projectManager.GetProjectAssets().FirstOrDefault(a =>
                a.AssetType == ProjectAssetType.Image &&
                string.Equals(Path.GetFullPath(a.SourcePath), fullSourcePath, StringComparison.OrdinalIgnoreCase));

            var asset = existing ?? projectManager.AddAsset(fullSourcePath, ProjectAssetType.Image);
            projectRelativePath = asset.StoredInProject && !string.IsNullOrWhiteSpace(asset.RelativePath)
                ? asset.RelativePath
                : Path.GetFileName(projectManager.GetAssetFullPath(asset));

            return projectManager.GetAssetFullPath(asset);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SpawnMenu] Could not register custom item image in project assets: {ex.Message}");
            return fullSourcePath;
        }
    }

    private static string SanitizeCustomItemKey(string key)
    {
        var chars = key
            .Trim()
            .ToLowerInvariant()
            .Select(ch =>
                (ch >= 'a' && ch <= 'z') ||
                (ch >= '0' && ch <= '9') ||
                ch == '_' || ch == '-'
                    ? ch
                    : '_')
            .ToArray();

        string sanitized = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "custom_item" : sanitized;
    }

    private unsafe void RenderItemsPreviewColumn()
    {
        ImGui.BeginChild("##itemsPreview", new Vector2(0, 0), ImGuiChildFlags.Borders);
        ImGui.TextDisabled("Preview");
        ImGui.Separator();

        if (!string.IsNullOrEmpty(_selectedTileKey))
        {
            bool hasGeometry = _previewMeshes.Count > 0 &&
                               _previewRenderer != null &&
                               _previewRenderer.ColorTexture != 0;

            ImGui.Spacing();
            RenderPreviewImage(hasGeometry);
            ImGui.Spacing();

            string atlasLabel = _itemAtlasSource == ItemAtlasSource.ItemAtlas ? "ItemAtlas" : "BlockAtlas";
            CentreText($"{atlasLabel}[{_selectedTileKey}]");
            ImGui.Spacing();
            CentreText(_item3DMode ? "3D extruded" : "Flat plane");
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Select a tile\nto see a preview.");
        }

        ImGui.EndChild();
    }

    // ── Blocks category UI ────────────────────────────────────────────────────

    private void RenderBlocksObjectsColumn(float columnWidth)
    {
        ImGui.BeginChild("##blocksObjects", new Vector2(columnWidth, 0), ImGuiChildFlags.Borders);
        ImGui.TextDisabled("Blocks");
        ImGui.Separator();

        bool blockSourceChanged = RenderResourcePackSelector(
            "BlocksSource",
            ref _spawnBlockSourceId,
            "Source Mod:",
            _availableSourceModIds);
        if (blockSourceChanged && _selectedObjectIndex >= 0 && _selectedObjectIndex < BlockRegistry.Blocks.Count)
        {
            string selectedBlock = BlockRegistry.Blocks[_selectedObjectIndex];
            if (!IsBlockFromSelectedSource(selectedBlock, _spawnBlockSourceId))
            {
                _selectedObjectIndex = -1;
                _selectedVariantIndex = -1;
                _currentVariants.Clear();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Per-column search (overrides the global search for this column)
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##blockSearch", "Filter blocks...", ref _blockSearchBuffer, 128))
        {
            _blockSearchQuery    = _blockSearchBuffer;
            _selectedObjectIndex  = -1;
            _selectedVariantIndex = -1;
            _currentVariants.Clear();
        }

        ImGui.Separator();

        ImGui.BeginChild("##blockList", new Vector2(0, 0));

        var blockList = BlockRegistry.Blocks;
        string query  = string.IsNullOrEmpty(_blockSearchQuery) ? _searchQuery : _blockSearchQuery;

        int displayIndex = 0;
        for (int i = 0; i < blockList.Count; i++)
        {
            string name = blockList[i];
            if (!IsBlockFromSelectedSource(name, _spawnBlockSourceId))
                continue;

            if (!string.IsNullOrEmpty(query) &&
                !name.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            bool sel = _selectedObjectIndex == i;
            if (ImGui.Selectable(name + "##blk" + i, sel))
            {
                _selectedObjectIndex  = i;
                _selectedVariantIndex = -1;
                OnBlockSelected(name);
            }

            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                _selectedObjectIndex = i;
                OnBlockSelected(name);
                // Auto-select first variant
                if (_currentVariants.Count > 0)
                    _selectedVariantIndex = 0;
                TrySpawn();
            }

            displayIndex++;
        }

        if (displayIndex == 0)
            ImGui.TextDisabled("(no blocks found)");

        ImGui.EndChild(); // ##blockList
        ImGui.EndChild(); // ##blocksObjects
    }

    private void RenderBlocksVariantsColumn(float columnWidth)
    {
        ImGui.BeginChild("##blocksVariants", new Vector2(columnWidth, 0), ImGuiChildFlags.Borders);
        ImGui.TextDisabled("Variants");
        ImGui.Separator();

        RenderResourcePackSelector("BlocksResourcePack", ref _spawnResourcePackId, "Resource Pack:");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (_currentVariants.Count > 0)
        {
            for (int i = 0; i < _currentVariants.Count; i++)
            {
                bool sel = _selectedVariantIndex == i;
                if (ImGui.Selectable(_currentVariants[i] + "##bvar" + i, sel))
                    _selectedVariantIndex = i;

                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    _selectedVariantIndex = i;
                    TrySpawn();
                }
            }
        }
        else if (_selectedObjectIndex >= 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("(no variants)");
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Select a block\nto see its variants.");
        }

        ImGui.EndChild();
    }

    private unsafe void RenderBlocksPreviewColumn()
    {
        ImGui.BeginChild("##blocksPreview", new Vector2(0, 0), ImGuiChildFlags.Borders);
        ImGui.TextDisabled("Preview");
        ImGui.Separator();

        if (_selectedObjectIndex >= 0 && _selectedObjectIndex < BlockRegistry.Blocks.Count)
        {
            string blockName = BlockRegistry.Blocks[_selectedObjectIndex];
            int variantIdx   = _selectedVariantIndex >= 0 ? _selectedVariantIndex : 0;
            var variants     = BlockRegistry.GetVariants(blockName);

            if (variants.Count > 0 && variantIdx < variants.Count)
            {
            bool hasGeometry = (_previewMeshes.Count > 0 || _previewCharacter != null) &&
                               _previewRenderer != null &&
                               _previewRenderer.ColorTexture != 0;

                ImGui.Spacing();
                RenderPreviewImage(hasGeometry);
                ImGui.Spacing();
                CentreText(blockName);

                string varKey = variants[variantIdx].VariantKey;
                if (!string.IsNullOrEmpty(varKey))
                {
                    ImGui.Spacing();
                    CentreText(varKey);
                }
            }
            else
            {
                ImGui.Spacing();
                ImGui.TextDisabled("(no variants)");
            }
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Select a block\nto see a preview.");
        }

        ImGui.EndChild();
    }

    // ── Characters category UI ────────────────────────────────────────────────

    private void RenderCharactersObjectsColumn(float columnWidth)
    {
        ImGui.BeginChild("##charObjects", new Vector2(columnWidth, 0), ImGuiChildFlags.Borders);
        ImGui.TextDisabled("Characters");
        ImGui.Separator();

        // Per-column search
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##charSearch", "Filter characters...", ref _charSearchBuffer, 128))
        {
            _charSearchQuery     = _charSearchBuffer;
            _selectedObjectIndex  = -1;
            _selectedVariantIndex = -1;
        }

        ImGui.Separator();

        ImGui.BeginChild("##charList", new Vector2(0, 0));

        var chars = CharacterRegistry.Characters;
        string query = string.IsNullOrEmpty(_charSearchQuery) ? _searchQuery : _charSearchQuery;

        int displayIndex = 0;
        for (int i = 0; i < chars.Count; i++)
        {
            var entry = chars[i];

            if (!string.IsNullOrEmpty(query) &&
                !entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !entry.Group.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            // Show a group prefix when the character lives in a sub-folder
            string label = string.IsNullOrEmpty(entry.Group)
                ? entry.Name
                : $"[{entry.Group}] {entry.Name}";

            bool sel = _selectedObjectIndex == i;
            if (ImGui.Selectable(label + "##char" + i, sel))
            {
                _selectedObjectIndex       = i;
                _selectedVariantIndex      = -1;
                _selectedCharTextureIndex  = -1;
                _customCharTexturePath     = "";
                _currentVariants.Clear();
            }

            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                _selectedObjectIndex = i;
                TrySpawn();
            }

            displayIndex++;
        }

        if (displayIndex == 0)
            ImGui.TextDisabled(chars.Count == 0
                ? "(no characters found –\nplace models in a\n'characters/' folder)"
                : "(no matches)");

        ImGui.EndChild(); // ##charList
        ImGui.EndChild(); // ##charObjects
    }

    // ── Characters variants column ────────────────────────────────────────────

    /// <summary>
    /// Renders the Variants column for the Characters category.
    /// Shows a texture selector when the selected character has a
    /// <c>textures.nux</c> manifest; otherwise shows a "(not available)" notice.
    /// </summary>
    private void RenderCharactersVariantsColumn(float columnWidth)
    {
        ImGui.BeginChild("##charVariants", new Vector2(columnWidth, 0), ImGuiChildFlags.Borders);
        ImGui.TextDisabled("Texture");
        ImGui.Separator();

        if (_selectedObjectIndex >= 0 &&
            _selectedObjectIndex < CharacterRegistry.Characters.Count)
        {
            var entry = CharacterRegistry.Characters[_selectedObjectIndex];

            if (entry.TextureVariants.Count > 0)
            {
                ImGui.Spacing();
                ImGui.Text("Skin:");

                // Build the names array for the combo box
                string[] variantNames = entry.TextureVariants
                    .Select(v => v.Name)
                    .ToArray();

                // Default to first variant (index 0) when nothing is explicitly chosen
                int comboIndex = _selectedCharTextureIndex >= 0 &&
                                 _selectedCharTextureIndex < variantNames.Length
                    ? _selectedCharTextureIndex
                    : 0;

                ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo("##charTexture", ref comboIndex, variantNames, variantNames.Length))
                    _selectedCharTextureIndex = comboIndex;

                ImGui.Spacing();

                // Show each variant as a selectable list entry as well
                for (int i = 0; i < entry.TextureVariants.Count; i++)
                {
                    bool sel = (i == comboIndex);
                    if (ImGui.Selectable(entry.TextureVariants[i].Name + "##ctex" + i, sel))
                        _selectedCharTextureIndex = i;

                    if (ImGui.IsItemHovered() &&
                        ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        _selectedCharTextureIndex = i;
                        TrySpawn();
                    }
                }

                // ── Custom variant file picker ─────────────────────────────
                var selectedVariant = entry.TextureVariants[comboIndex];
                if (selectedVariant.IsCustom)
                {
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();

                    if (ImGui.Button("Browse...##charTexBrowse", new Vector2(-1, 0)))
                    {
                        var result = Dialog.FileOpen("png,jpg,jpeg,tga,bmp");
                        if (result.IsOk && !string.IsNullOrEmpty(result.Path))
                            _customCharTexturePath = result.Path;
                    }

                    ImGui.Spacing();

                    if (!string.IsNullOrEmpty(_customCharTexturePath))
                    {
                        // Show just the filename — full path is too long for the column
                        string fileName = Path.GetFileName(_customCharTexturePath);
                        ImGui.TextDisabled(fileName);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip(_customCharTexturePath);
                    }
                    else
                    {
                        ImGui.TextDisabled("No file chosen.");
                    }
                }
            }
            else
            {
                ImGui.Spacing();
                ImGui.TextDisabled("(no texture variants)");
            }
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Select a character\nto see textures.");
        }

        ImGui.EndChild();
    }

    // ── Shared preview helpers ────────────────────────────────────────────────

    /// <summary>
    /// Draws the preview FBO texture (if <paramref name="hasGeometry"/> is true) or a
    /// "no preview" placeholder.  The image is centred in the available horizontal space
    /// and sized to fit both the column width and a maximum of 180 px.
    /// </summary>
    private unsafe void RenderPreviewImage(bool hasGeometry)
    {
        float avail = ImGui.GetContentRegionAvail().X - 8f;
        float size  = Math.Min(avail, 180f);

        float indent = (ImGui.GetContentRegionAvail().X - size) / 2f;
        if (indent > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);

        if (hasGeometry && _previewRenderer != null)
        {
            // OpenGL textures are stored bottom-up; flip UV Y so the image is right-side-up.
            ImGui.Image(
                new ImTextureRef(texId: (ulong)_previewRenderer.ColorTexture),
                new Vector2(size, size),
                new Vector2(0, 1),   // uv0: (0,1) = top-left in GL coords
                new Vector2(1, 0));  // uv1: (1,0) = bottom-right in GL coords

            // Allow dragging on the preview image to orbit the camera
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var delta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left);
                ImGui.ResetMouseDragDelta(ImGuiMouseButton.Left);
                _previewRenderer.Orbit(delta.X * 0.01f, -delta.Y * 0.01f);
            }
        }
        else
        {
            // Placeholder rectangle
            var drawList = ImGui.GetWindowDrawList();
            var pos      = ImGui.GetCursorScreenPos();
            drawList.AddRectFilled(pos, new Vector2(pos.X + size, pos.Y + size),
                                   0xFF222226, 4f);
            drawList.AddRect(pos, new Vector2(pos.X + size, pos.Y + size),
                             0xFF555566, 4f, 0, 1.5f);

            // "no preview" text centred inside the box
            const string msg = "no preview";
            var ts = ImGui.CalcTextSize(msg);
            ImGui.SetCursorScreenPos(new Vector2(
                pos.X + (size - ts.X) * 0.5f,
                pos.Y + (size - ts.Y) * 0.5f));
            ImGui.TextDisabled(msg);
            // Advance cursor past the rectangle
            ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + size));
            ImGui.Dummy(new Vector2(size, 0));
        }
    }

    /// <summary>Renders <paramref name="text"/> centred in the current column.</summary>
    private static void CentreText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        float lw     = ImGui.CalcTextSize(text).X;
        float indent = (ImGui.GetContentRegionAvail().X - lw) / 2f;
        if (indent > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);
        ImGui.TextDisabled(text);
    }

    private void RenderBottomBar()
    {
        float buttonWidth = 110f;
        ImGui.SetCursorPosX(ImGui.GetContentRegionAvail().X - buttonWidth + ImGui.GetCursorPosX());

        bool canSpawn = CanSpawn();
        if (!canSpawn) ImGui.BeginDisabled();

        if (ImGui.Button("Spawn", new Vector2(buttonWidth, 28)))
            TrySpawn();

        if (!canSpawn) ImGui.EndDisabled();
    }

    private bool CanSpawn()
    {
        if (_selectedCategory == "Items")
            return !string.IsNullOrEmpty(_selectedTileKey);

        if (_selectedObjectIndex < 0) return false;

        var filtered = GetFilteredObjects();
        if (_selectedObjectIndex >= filtered.Count) return false;

        var objectName = filtered[_selectedObjectIndex];

        if (_selectedCategory == "Custom Models")
            return objectName == "Load..." || _customModelPaths.ContainsKey(objectName);

        if (_selectedCategory == "Scenery")
            return objectName == SceneryLoadLabel;

        if (_selectedCategory == "Blocks")
        {
            // A block is spawnable as soon as one is selected; variant defaults to first if not chosen
            var blockList = BlockRegistry.Blocks;
            return _selectedObjectIndex >= 0 &&
                   _selectedObjectIndex < blockList.Count &&
                   BlockRegistry.GetVariants(blockList[_selectedObjectIndex]).Count > 0;
        }

        if (_selectedCategory == "Characters")
        {
            var chars = CharacterRegistry.Characters;
            if (_selectedObjectIndex < 0 || _selectedObjectIndex >= chars.Count)
                return false;

            // If the selected texture variant is Custom, a file must have been picked.
            var charEntry = chars[_selectedObjectIndex];
            if (charEntry.TextureVariants.Count > 0)
            {
                int texIdx = _selectedCharTextureIndex >= 0 &&
                             _selectedCharTextureIndex < charEntry.TextureVariants.Count
                    ? _selectedCharTextureIndex : 0;
                if (charEntry.TextureVariants[texIdx].IsCustom &&
                    string.IsNullOrEmpty(_customCharTexturePath))
                    return false;
            }

            return true;
        }

        return true;
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void OnObjectSelected(string objectName)
    {
        if (_selectedCategory == "Scenery" && objectName == SceneryLoadLabel)
        {
            _currentVariants.Clear();
            OpenSchematicFileDialog();
            return;
        }

        if (_selectedCategory == "Custom Models" && objectName == "Load...")
        {
            _currentVariants.Clear();
            OpenCustomModelFileDialog();
            return;
        }

        // Handle textured primitive selection: add texture option to variants
        if (_selectedCategory == "Primitives" && objectName == "Plane")
        {
            _currentVariants.Clear();
            _currentVariants.Add("None");
            _currentVariants.Add("Load texture...");
            // Reset texture if switching away from textured primitive
            if (_selectedPrimitiveTextureId != 0)
            {
                Gl?.DeleteTexture(_selectedPrimitiveTextureId);
                _selectedPrimitiveTextureId = 0;
            }
            _selectedPrimitiveTexturePath = "";
            _selectedVariantIndex = 0; // Select "None" by default
        }
        else
        {
            _currentVariants.Clear();
            // Clean up texture if switching to non-textured primitive
            if (_selectedPrimitiveTextureId != 0)
            {
                Gl?.DeleteTexture(_selectedPrimitiveTextureId);
                _selectedPrimitiveTextureId = 0;
            }
            _selectedPrimitiveTexturePath = "";
        }
    }

    private void OnBlockSelected(string blockName)
    {
        _currentVariants.Clear();
        var variants = BlockRegistry.GetVariants(blockName);
        foreach (var v in variants)
            _currentVariants.Add(v.VariantKey);
    }

    private void OnObjectDoubleClicked(string objectName)
    {
        if (_selectedCategory == "Scenery" && objectName == SceneryLoadLabel)
            return; // single-click already handled in OnObjectSelected

        if (_selectedCategory == "Custom Models" && objectName == "Load...")
            return; // single-click already handled in OnObjectSelected

        TrySpawn();
    }

    /// <summary>
    /// Opens a native file-open dialog filtered to common 3-D model formats.
    /// On success the model is imported via <see cref="AssimpModelLoader"/>,
    /// added to the scene, and the entry is stored in the custom-model history.
    /// </summary>
    private void OpenCustomModelFileDialog()
    {
        if (Viewport == null || Gl == null) return;

        var result = Dialog.FileOpen(
            "glb,gltf,fbx,obj,dae,3ds,blend,ply,stl,x3d,mimodel,miobject");

        if (result.IsOk && !string.IsNullOrEmpty(result.Path))
            SpawnCustomModelFromPath(result.Path);
    }

    /// <summary>
    /// Opens a native file-open dialog for Minecraft schematic files.
    /// </summary>
    private void OpenSchematicFileDialog()
    {
        if (Viewport == null || Gl == null) return;

        var result = Dialog.FileOpen("schematic,schem");
        if (!result.IsOk || string.IsNullOrEmpty(result.Path)) return;

        string pathToSpawn = ResolveSchematicPathForProject(result.Path);
        var root = SpawnSchematicFromPath(pathToSpawn, _spawnResourcePackId);
        if (root == null)
            Console.Error.WriteLine($"[SpawnMenu] Failed to load schematic: {pathToSpawn}");
        else
            _isOpen = false;
    }

    private string ResolveSchematicPathForProject(string sourcePath)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);

        if (ProjectManager == null || !ProjectManager.HasProject)
            return fullSourcePath;

        try
        {
            var existing = ProjectManager.GetProjectAssets().FirstOrDefault(a =>
                string.Equals(Path.GetFullPath(a.SourcePath), fullSourcePath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                return ProjectManager.GetAssetFullPath(existing);

            var added = ProjectManager.AddAsset(fullSourcePath, ProjectAssetType.Other);
            return ProjectManager.GetAssetFullPath(added);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SpawnMenu] Could not register schematic in project assets: {ex.Message}");
            return fullSourcePath;
        }
    }

    /// <summary>
    /// Loads the model at <paramref name="filePath"/> and spawns the resulting
    /// hierarchy as a child of the scene root.
    ///
    /// Supports:
    ///  - .mimodel / .miobject  — Mine Imator model files (via MineImatorLoader)
    ///  - .glb / .gltf and all Assimp-supported formats (via AssimpModelLoader)
    ///
    /// <paramref name="textureOverridePath"/> — when not null and the model is a
    /// <c>.mimodel</c>, overrides the model's default texture with this PNG path.
    ///
    /// Returns the root <see cref="SceneObject"/> on success, or <c>null</c> on error.
    /// </summary>
    public SceneObject? SpawnCustomModelFromPath(string filePath, string? textureOverridePath = null)
    {
        if (Viewport == null || Gl == null) return null;

        string pathToSpawn = ResolveModelPathForProject(filePath);

        string ext = Path.GetExtension(pathToSpawn).ToLowerInvariant();

        SceneObject? root;

        if (ext == ".mimodel")
        {
            root = SpawnMineImatorModel(pathToSpawn, textureOverridePath);
        }
        else if (ext == ".miobject")
        {
            root = SpawnMineImatorObject(pathToSpawn);
        }
        else
        {
            root = AssimpModelLoader.Load(Gl, pathToSpawn);
        }

        if (root == null)
        {
            Console.Error.WriteLine($"[SpawnMenu] Failed to load model: {pathToSpawn}");
            return null;
        }

        string displayName = Path.GetFileNameWithoutExtension(pathToSpawn);
        if (string.IsNullOrEmpty(root.Name)) root.Name = displayName;

        AddToCustomModelHistory(pathToSpawn, displayName);
        Viewport.SceneObjects.Add(root);
        return root;
    }

    private string ResolveModelPathForProject(string sourcePath)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);

        if (ProjectManager == null || !ProjectManager.HasProject)
            return fullSourcePath;

        try
        {
            var existing = ProjectManager.GetProjectAssets().FirstOrDefault(a =>
                string.Equals(Path.GetFullPath(a.SourcePath), fullSourcePath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFullPath(ProjectManager.GetAssetFullPath(a)), fullSourcePath,
                    StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                return ProjectManager.GetAssetFullPath(existing);

            var added = ProjectManager.AddAsset(fullSourcePath, ProjectAssetType.Model);
            return ProjectManager.GetAssetFullPath(added);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SpawnMenu] Could not register model in project assets: {ex.Message}");
            return fullSourcePath;
        }
    }

    /// <summary>
    /// Loads a Mine Imator .mimodel file and creates a CharacterSceneObject.
    /// When <paramref name="textureOverridePath"/> is non-null the skin texture on
    /// every bone mesh is replaced with the texture loaded from that path.
    /// </summary>
    private SceneObject? SpawnMineImatorModel(string filePath, string? textureOverridePath = null)
    {
        var loader = MineImatorLoader.Instance;
        var model  = loader.LoadModel(filePath);
        if (model == null) return null;

        var character = loader.CreateCharacterFromModel(model);
        if (character == null) return null;

        character.Name = model.Name ?? Path.GetFileNameWithoutExtension(filePath);
        character.SourceAssetPath = filePath;
        character.AssignObjectId();

        // Apply texture override: replace every bone mesh's TextureId with the
        // chosen skin texture so the correct variant is visible from spawn.
        if (!string.IsNullOrEmpty(textureOverridePath) && File.Exists(textureOverridePath))
        {
            uint overrideTexId = loader.LoadTextureFromFile(textureOverridePath);
            if (overrideTexId != 0)
                ApplyTextureOverrideToCharacter(character, overrideTexId);
        }

        return character;
    }

    /// <summary>
    /// Walks the full scene-object hierarchy of <paramref name="root"/> and
    /// replaces the skin texture on every bone/mesh that already carries a
    /// non-zero <c>TextureId</c>.
    ///
    /// For <see cref="MiBoneSceneObject"/> nodes the override is also written
    /// into the stored shape data so it survives bend-angle regeneration.
    /// </summary>
    private static void ApplyTextureOverrideToCharacter(SceneObject root, uint textureId)
    {
        if (root is MiBoneSceneObject miBone)
        {
            miBone.OverrideTexture(textureId);
        }
        else
        {
            foreach (var mesh in root.Visuals)
            {
                if (mesh.TextureId != 0)
                    mesh.TextureId = textureId;
            }
        }

        foreach (var child in root.Children)
            ApplyTextureOverrideToCharacter(child, textureId);
    }

    /// <summary>
    /// Loads a Mine Imator .miobject file and creates a scene hierarchy.
    /// </summary>
    private SceneObject? SpawnMineImatorObject(string filePath)
    {
        var loader   = MineImatorLoader.Instance;
        var miObject = loader.LoadMiObject(filePath);
        if (miObject == null) return null;

        var scene = loader.CreateSceneFromMiObject(miObject);
        if (scene == null) return null;

        scene.Name = Path.GetFileNameWithoutExtension(filePath);
        scene.SourceAssetPath = filePath;
        scene.AssignObjectId();
        return scene;
    }

    private void SpawnCustomModel(string objectName)
    {
        if (objectName == "Load...")
        {
            OpenCustomModelFileDialog();
            return;
        }

        // Re-spawn a model from the history list.
        if (_customModelPaths.TryGetValue(objectName, out string? path))
        {
            SpawnCustomModelFromPath(path);
            _isOpen = false;
        }
    }

    private void SpawnScenery(string objectName)
    {
        if (objectName != SceneryLoadLabel) return;
        OpenSchematicFileDialog();
    }

    /// <summary>
    /// Loads legacy <c>.schematic</c> and Sponge/WorldEdit <c>.schem</c> files and
    /// spawns them as a merged scenery object.
    /// </summary>
    public SceneObject? SpawnSchematicFromPath(string filePath, string resourcePackId = "")
    {
        if (Viewport == null || Gl == null) return null;

        string normalizedResourcePackId = MinecraftDataLoader.NormalizeResourcePackId(resourcePackId);
        string previousResourcePackId = _spawnResourcePackId;
        _spawnResourcePackId = normalizedResourcePackId;

        try
        {

        NbtDocument doc;
        try
        {
            doc = NbtDocument.LoadDocument(filePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SpawnMenu] Failed reading NBT schematic '{filePath}': {ex.Message}");
            return null;
        }

        var rootTag = doc.DocumentRoot;
        if (rootTag == null) return null;

        var schematic = rootTag.GetCompound("Schematic") ?? rootTag;

        int width = GetDimension(schematic, "Width");
        int height = GetDimension(schematic, "Height");
        int length = GetDimension(schematic, "Length");

        if (width <= 0 || height <= 0 || length <= 0)
        {
            Console.Error.WriteLine($"[SpawnMenu] Invalid schematic dimensions in '{filePath}'.");
            return null;
        }

        int total = width * height * length;
        var variantCache = new Dictionary<string, VariantRenderInfo>(StringComparer.OrdinalIgnoreCase);
        var voxelInfos = new VariantRenderInfo?[total];
        var availableBlocks = new HashSet<string>(BlockRegistry.Blocks, StringComparer.OrdinalIgnoreCase);

        bool modernLoaded = TryLoadModernPaletteBlocks(
            schematic,
            total,
            availableBlocks,
            variantCache,
            voxelInfos);

        if (!modernLoaded)
        {
            if (!TryLoadLegacyBlocks(
                    schematic,
                    width,
                    height,
                    length,
                    total,
                    availableBlocks,
                    variantCache,
                    voxelInfos))
                return null;
        }

        string baseName = Path.GetFileNameWithoutExtension(filePath);
        int nextNum = GetNextAvailableObjectNumber(baseName);
        string fullName = nextNum > 1 ? $"{baseName}{nextNum}" : baseName;

        var root = new SceneObject
        {
            Name = fullName,
            ObjectType = "Schematic",
            SpawnCategory = "Scenery",
            ResourcePackId = normalizedResourcePackId,
            SourceAssetPath = filePath,
            Position = vec3.Zero,
            PivotOffset = vec3.Zero,
            InheritPivotOffset = false
        };
        root.AssignObjectId();

        float originX = (width - 1) * 0.5f;
        float originZ = (length - 1) * 0.5f;

        var largeChestPlacements = BuildLargeChestPlacements(
            voxelInfos,
            width,
            height,
            length,
            originX,
            originZ,
            variantCache);

        var merged = new Dictionary<uint, MeshAccumulator>();
        int placed = 0;

        for (int y = 0; y < height; y++)
        {
            for (int z = 0; z < length; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width * length + z * width + x;

                    if (largeChestPlacements.SkippedIndices.Contains(index))
                        continue;

                    if (largeChestPlacements.ByAnchorIndex.TryGetValue(index, out var largePlacement))
                    {
                        placed++;

                        foreach (var template in largePlacement.Info.Templates)
                        {
                            var acc = GetOrCreateAccumulator(merged, template.TextureId);
                            AppendTemplate(acc, template, largePlacement.Px, largePlacement.Py, largePlacement.Pz);
                        }

                        continue;
                    }

                    var info = voxelInfos[index];
                    if (info == null) continue;

                    placed++;
                    float px = x - originX;
                    float py = y + 0.5f;
                    float pz = z - originZ;

                    if (info.IsCullableCube && info.CubeFaces != null)
                    {
                        EmitCubeFacesWithCulling(
                            merged,
                            info.CubeFaces,
                            voxelInfos,
                            width,
                            height,
                            length,
                            x,
                            y,
                            z,
                            px,
                            py,
                            pz);
                        continue;
                    }

                    foreach (var template in info.Templates)
                    {
                        var acc = GetOrCreateAccumulator(merged, template.TextureId);
                        AppendTemplate(acc, template, px, py, pz);
                    }
                }
            }
        }

        if (placed == 0 || merged.Count == 0)
        {
            Console.Error.WriteLine($"[SpawnMenu] Schematic had no spawnable blocks: {filePath}");
            return null;
        }

        foreach (var kv in merged)
        {
            var acc = kv.Value;
            if (acc.Vertices.Count == 0) continue;

            var mesh = new Mesh(Gl)
            {
                TextureId = kv.Key
            };
            mesh.Vertices.AddRange(acc.Vertices);
            mesh.Normals.AddRange(acc.Normals);
            mesh.TexCoords.AddRange(acc.TexCoords);
            mesh.Upload();
            root.AddMesh(mesh);
        }

        if (root.Visuals.Count == 0)
        {
            Console.Error.WriteLine($"[SpawnMenu] Schematic produced no renderable geometry: {filePath}");
            return null;
        }

            Viewport.SceneObjects.Add(root);
            return root;
        }
        finally
        {
            _spawnResourcePackId = previousResourcePackId;
        }
    }

    private sealed class LargeChestPlacement
    {
        public required VariantRenderInfo Info;
        public float Px;
        public float Py;
        public float Pz;
    }

    private sealed class LargeChestPlacementSet
    {
        public readonly Dictionary<int, LargeChestPlacement> ByAnchorIndex = new();
        public readonly HashSet<int> SkippedIndices = new();
    }

    private LargeChestPlacementSet BuildLargeChestPlacements(
        VariantRenderInfo?[] voxelInfos,
        int width,
        int height,
        int length,
        float originX,
        float originZ,
        Dictionary<string, VariantRenderInfo> variantCache)
    {
        var result = new LargeChestPlacementSet();
        int layerSize = width * length;
        int total = voxelInfos.Length;
        var processed = new HashSet<int>();

        for (int index = 0; index < total; index++)
        {
            if (processed.Contains(index))
                continue;

            var info = voxelInfos[index];
            if (info == null)
                continue;

            if (!IsChestType(info.BlockName))
                continue;

            if (!TryGetVariantFacing(info.Variant, out string? facing) || string.IsNullOrEmpty(facing))
                continue;

            int y = index / layerSize;
            int rem = index % layerSize;
            int z = rem / width;
            int x = rem % width;

            (int dx, int dz)[] pairAxis = facing switch
            {
                "north" or "south" => new[] { (1, 0), (-1, 0) },
                "east" or "west" => new[] { (0, 1), (0, -1) },
                _ => Array.Empty<(int, int)>()
            };

            int pairIndex = -1;
            int pairX = 0;
            int pairZ = 0;

            foreach (var (dx, dz) in pairAxis)
            {
                int nx = x + dx;
                int nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= width || nz >= length)
                    continue;

                int nIndex = y * layerSize + nz * width + nx;
                var other = voxelInfos[nIndex];
                if (other == null || !IsChestType(other.BlockName))
                    continue;

                if (!string.Equals(other.BlockName, info.BlockName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryGetVariantFacing(other.Variant, out string? otherFacing) ||
                    !string.Equals(otherFacing, facing, StringComparison.OrdinalIgnoreCase))
                    continue;

                pairIndex = nIndex;
                pairX = nx;
                pairZ = nz;
                break;
            }

            if (pairIndex < 0)
                continue;

            processed.Add(index);
            processed.Add(pairIndex);

            if (!TryCreateLargeChestRenderInfo(info.BlockName, facing, variantCache, out var largeInfo) || largeInfo == null)
                continue;

            int anchor = Math.Min(index, pairIndex);
            float cx = (x + pairX) * 0.5f;
            float cz = (z + pairZ) * 0.5f;

            result.ByAnchorIndex[anchor] = new LargeChestPlacement
            {
                Info = largeInfo,
                Px = cx - originX,
                Py = y + 0.5f,
                Pz = cz - originZ
            };

            result.SkippedIndices.Add(index);
            result.SkippedIndices.Add(pairIndex);
            result.SkippedIndices.Remove(anchor);
        }

        return result;
    }

    private static bool IsChestType(string blockName)
    {
        return string.Equals(blockName, "chest", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(blockName, "trapped_chest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetVariantFacing(BlockVariantEntry variant, out string? facing)
    {
        var props = ParseVariantKeyProperties(variant.VariantKey);
        if (props.TryGetValue("facing", out string? v) && !string.IsNullOrWhiteSpace(v))
        {
            facing = v;
            return true;
        }

        facing = null;
        return false;
    }

    private bool TryCreateLargeChestRenderInfo(
        string blockName,
        string facing,
        Dictionary<string, VariantRenderInfo> variantCache,
        out VariantRenderInfo? info)
    {
        info = null;

        var largeVariant = BlockRegistry.GetVariants(blockName)
            .FirstOrDefault(v => string.Equals(v.VariantKey, "large", StringComparison.OrdinalIgnoreCase));
        if (largeVariant == null)
            return false;

        int rotationY = facing switch
        {
            "east" => 90,
            "south" => 180,
            "west" => 270,
            _ => 0
        };

        var orientedLarge = new BlockVariantEntry
        {
            VariantKey = $"large,facing={facing}",
            ModelPath = largeVariant.ModelPath,
            RotationX = largeVariant.RotationX,
            RotationY = rotationY,
            CemPath = largeVariant.CemPath,
            TopHalf = largeVariant.TopHalf,
            PartOffsetX = largeVariant.PartOffsetX,
            PartOffsetY = largeVariant.PartOffsetY,
            PartOffsetZ = largeVariant.PartOffsetZ
        };

        info = GetOrCreateVariantRenderInfo(blockName, orientedLarge, variantCache);
        return true;
    }

    private sealed class MeshAccumulator
    {
        public readonly List<vec3> Vertices = new();
        public readonly List<vec3> Normals = new();
        public readonly List<vec2> TexCoords = new();
    }

    private sealed class MeshTemplate
    {
        public uint TextureId;
        public vec3[] Vertices = Array.Empty<vec3>();
        public vec3[] Normals = Array.Empty<vec3>();
        public vec2[] TexCoords = Array.Empty<vec2>();
    }

    private sealed class CubeFaceInfo
    {
        public required uint TextureId;
        public required vec2[] Uv; // TL, TR, BR, BL
    }

    private sealed class CubeFaceSet
    {
        public CubeFaceInfo? Up;
        public CubeFaceInfo? Down;
        public CubeFaceInfo? North;
        public CubeFaceInfo? South;
        public CubeFaceInfo? West;
        public CubeFaceInfo? East;
    }

    private sealed class VariantRenderInfo
    {
        public required string BlockName;
        public required BlockVariantEntry Variant;
        public bool IsCullableCube;
        public CubeFaceSet? CubeFaces;
        public List<MeshTemplate> Templates = new();
    }

    private static int GetDimension(TagCompound root, string key)
    {
        int value = root.GetShortValue(key, 0);
        if (value == 0)
            value = root.GetIntValue(key, 0);
        return value;
    }

    private bool TryLoadModernPaletteBlocks(
        TagCompound schematic,
        int total,
        HashSet<string> availableBlocks,
        Dictionary<string, VariantRenderInfo> variantCache,
        VariantRenderInfo?[] outVoxels)
    {
        var palette = schematic.GetCompound("Palette");
        byte[] blockData = schematic.GetByteArrayValue("BlockData", Array.Empty<byte>());
        if (palette == null || palette.Count == 0 || blockData.Length == 0)
            return false;

        if (!TryDecodeVarIntArray(blockData, total, out var paletteIndices))
        {
            Console.Error.WriteLine("[SpawnMenu] Failed to decode BlockData varints from .schem file.");
            return false;
        }

        var paletteLookup = new Dictionary<int, string>();
        foreach (Tag tag in palette.Value)
        {
            if (tag is TagInt ti)
                paletteLookup[ti.Value] = ti.Name;
        }

        var idToInfo = new Dictionary<int, VariantRenderInfo?>();
        for (int i = 0; i < total; i++)
        {
            int paletteId = paletteIndices[i];
            if (!idToInfo.TryGetValue(paletteId, out var info))
            {
                if (!paletteLookup.TryGetValue(paletteId, out string? stateText) || string.IsNullOrEmpty(stateText))
                {
                    idToInfo[paletteId] = null;
                    continue;
                }

                if (!TryParsePaletteState(stateText, out var blockName, out var props))
                {
                    idToInfo[paletteId] = null;
                    continue;
                }

                if (string.Equals(blockName, "air", StringComparison.OrdinalIgnoreCase) ||
                    !availableBlocks.Contains(blockName))
                {
                    idToInfo[paletteId] = null;
                    continue;
                }

                var variant = PickBestVariantForProperties(BlockRegistry.GetVariants(blockName), props, null);
                if (variant == null)
                {
                    idToInfo[paletteId] = null;
                    continue;
                }

                info = GetOrCreateVariantRenderInfo(blockName, variant, variantCache);
                idToInfo[paletteId] = info;
            }

            outVoxels[i] = info;
        }

        return true;
    }

    private bool TryLoadLegacyBlocks(
        TagCompound schematic,
        int width,
        int height,
        int length,
        int total,
        HashSet<string> availableBlocks,
        Dictionary<string, VariantRenderInfo> variantCache,
        VariantRenderInfo?[] outVoxels)
    {
        byte[] blocks = schematic.GetByteArrayValue("Blocks", Array.Empty<byte>());
        byte[] data = schematic.GetByteArrayValue("Data", Array.Empty<byte>());
        byte[] addBlocks = schematic.GetByteArrayValue("AddBlocks", Array.Empty<byte>());

        if (blocks.Length < total)
        {
            Console.Error.WriteLine($"[SpawnMenu] Schematic block array is truncated ({blocks.Length} < {total}).");
            return false;
        }

        var legacyCache = new Dictionary<int, VariantRenderInfo?>();

        for (int i = 0; i < total; i++)
        {
            int blockId = blocks[i];
            if (addBlocks.Length > 0)
            {
                int add = addBlocks[i >> 1];
                int highBits = (i & 1) == 0 ? (add & 0x0F) : ((add >> 4) & 0x0F);
                blockId |= highBits << 8;
            }

            int blockData = i < data.Length ? data[i] & 0x0F : 0;

            if (blockId == 64 || blockId == 71)
            {
                if (!TryResolveLegacyDoor(
                        blockId,
                        blockData,
                        blocks,
                        data,
                        i,
                        width,
                        height,
                        length,
                        out var doorName,
                        out var doorHint,
                        out bool isUpperHalf))
                {
                    outVoxels[i] = null;
                    continue;
                }

                // Door variants are compressed into a single two-block mesh in BlockRegistry.
                // Spawn only from the lower half to avoid duplicate full-door geometry.
                if (isUpperHalf)
                {
                    outVoxels[i] = null;
                    continue;
                }

                if (!availableBlocks.Contains(doorName))
                {
                    outVoxels[i] = null;
                    continue;
                }

                Dictionary<string, string>? doorProps =
                    TryParseVariantHintProperties(doorHint, out var parsedDoorProps) ? parsedDoorProps : null;

                var doorVariant = PickBestVariantForProperties(BlockRegistry.GetVariants(doorName), doorProps, doorHint);
                outVoxels[i] = doorVariant == null
                    ? null
                    : GetOrCreateVariantRenderInfo(doorName, doorVariant, variantCache);
                continue;
            }

            if (blockId == 85)
            {
                if (!TryResolveLegacyBlock(blockId, blockData, out var fenceBlockName, out _))
                {
                    outVoxels[i] = null;
                    continue;
                }

                if (!availableBlocks.Contains(fenceBlockName))
                {
                    outVoxels[i] = null;
                    continue;
                }

                string fenceHint = BuildLegacyFenceVariantHint(blocks, i, width, height, length);
                Dictionary<string, string>? fenceProps =
                    TryParseVariantHintProperties(fenceHint, out var parsedFenceProps) ? parsedFenceProps : null;

                outVoxels[i] = GetOrCreateFenceRenderInfo(fenceBlockName, fenceProps, variantCache);
                continue;
            }

            int legacyKey = (blockId << 8) | blockData;

            if (!legacyCache.TryGetValue(legacyKey, out var info))
            {
                if (!TryResolveLegacyBlock(blockId, blockData, out var blockName, out var variantHint))
                {
                    legacyCache[legacyKey] = null;
                    continue;
                }

                if (string.Equals(blockName, "air", StringComparison.OrdinalIgnoreCase) ||
                    !availableBlocks.Contains(blockName))
                {
                    legacyCache[legacyKey] = null;
                    continue;
                }

                Dictionary<string, string>? props =
                    TryParseVariantHintProperties(variantHint, out var parsedProps) ? parsedProps : null;

                var variant = PickBestVariantForProperties(BlockRegistry.GetVariants(blockName), props, variantHint);
                if (variant == null)
                {
                    legacyCache[legacyKey] = null;
                    continue;
                }

                info = GetOrCreateVariantRenderInfo(blockName, variant, variantCache);
                legacyCache[legacyKey] = info;
            }

            outVoxels[i] = info;
        }

        return true;
    }

    private VariantRenderInfo? GetOrCreateFenceRenderInfo(
        string blockName,
        Dictionary<string, string>? props,
        Dictionary<string, VariantRenderInfo> cache)
    {
        bool north = props != null && props.TryGetValue("north", out string? n) && string.Equals(n, "true", StringComparison.OrdinalIgnoreCase);
        bool east = props != null && props.TryGetValue("east", out string? e) && string.Equals(e, "true", StringComparison.OrdinalIgnoreCase);
        bool south = props != null && props.TryGetValue("south", out string? s) && string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
        bool west = props != null && props.TryGetValue("west", out string? w) && string.Equals(w, "true", StringComparison.OrdinalIgnoreCase);

        string cacheKey = $"fence|{blockName}|n={(north ? 1 : 0)}|e={(east ? 1 : 0)}|s={(south ? 1 : 0)}|w={(west ? 1 : 0)}";
        if (cache.TryGetValue(cacheKey, out var existing))
            return existing;

        var variants = BlockRegistry.GetVariants(blockName);
        if (variants.Count == 0)
            return null;

        bool IsPost(BlockVariantEntry v) =>
            !string.IsNullOrEmpty(v.ModelPath) && v.ModelPath.Contains("_fence_post", StringComparison.OrdinalIgnoreCase);

        bool IsSideWithY(BlockVariantEntry v, int y) =>
            !string.IsNullOrEmpty(v.ModelPath) &&
            v.ModelPath.Contains("_fence_side", StringComparison.OrdinalIgnoreCase) &&
            v.RotationY == y;

        var post = variants.FirstOrDefault(IsPost) ?? variants[0];

        var parts = new List<BlockVariantEntry> { post };
        if (north)
        {
            var part = variants.FirstOrDefault(v => IsSideWithY(v, 0));
            if (part != null) parts.Add(part);
        }
        if (east)
        {
            var part = variants.FirstOrDefault(v => IsSideWithY(v, 270));
            if (part != null) parts.Add(part);
        }
        if (south)
        {
            var part = variants.FirstOrDefault(v => IsSideWithY(v, 180));
            if (part != null) parts.Add(part);
        }
        if (west)
        {
            var part = variants.FirstOrDefault(v => IsSideWithY(v, 90));
            if (part != null) parts.Add(part);
        }

        var info = new VariantRenderInfo
        {
            BlockName = blockName,
            Variant = post,
            IsCullableCube = false,
            CubeFaces = null,
            Templates = BuildVariantTemplates(parts)
        };

        cache[cacheKey] = info;
        return info;
    }

    private VariantRenderInfo GetOrCreateVariantRenderInfo(
        string blockName,
        BlockVariantEntry variant,
        Dictionary<string, VariantRenderInfo> cache)
    {
        string cacheKey = $"{blockName}|{variant.VariantKey}|{variant.ModelPath}|{variant.RotationX}|{variant.RotationY}|{variant.CemPath}";
        if (cache.TryGetValue(cacheKey, out var existing))
            return existing;

        var info = new VariantRenderInfo
        {
            BlockName = blockName,
            Variant = variant
        };

        info.CubeFaces = TryBuildCullableCubeFaces(variant);
        info.IsCullableCube = info.CubeFaces != null;

        if (!info.IsCullableCube)
            info.Templates = BuildVariantTemplates(variant);

        cache[cacheKey] = info;
        return info;
    }

    private CubeFaceSet? TryBuildCullableCubeFaces(BlockVariantEntry variant)
    {
        if (!string.IsNullOrEmpty(variant.CemPath) || variant.TopHalf != null)
            return null;

        if (string.IsNullOrEmpty(variant.ModelPath))
            return null;

        var resolved = BlockRegistry.ResolveModel(variant.ModelPath);
        if (resolved == null || resolved.Elements.Count != 1)
            return null;

        var element = resolved.Elements[0];
        if (element.Rotation != null)
            return null;

        if (element.From.Length < 3 || element.To.Length < 3)
            return null;

        if (element.From[0] != 0f || element.From[1] != 0f || element.From[2] != 0f ||
            element.To[0] != 16f || element.To[1] != 16f || element.To[2] != 16f)
            return null;

        CubeFaceInfo? BuildFace(string faceName)
        {
            if (!element.Faces.TryGetValue(faceName, out var face)) return null;

            string? texKey = BlockRegistry.ResolveTextureKey(resolved, face.Texture);
            if (string.IsNullOrEmpty(texKey)) return null;

            string resolvedTexKey = ResolveTerrainTextureKeyForPack(texKey, _spawnResourcePackId);
            if (!TerrainAtlas.Textures.TryGetValue(resolvedTexKey, out uint texId) || texId == 0) return null;

            var uv = GetFaceUv(faceName, face.Uv, face.Rotation);
            return new CubeFaceInfo { TextureId = texId, Uv = uv };
        }

        var set = new CubeFaceSet
        {
            Up = BuildFace("up"),
            Down = BuildFace("down"),
            North = BuildFace("north"),
            South = BuildFace("south"),
            West = BuildFace("west"),
            East = BuildFace("east")
        };

        if (set.Up == null || set.Down == null || set.North == null ||
            set.South == null || set.West == null || set.East == null)
            return null;

        int turnsY = NormalizeQuarterTurns(variant.RotationY);
        if (turnsY != 0)
            RotateCubeFacesY(set, turnsY);

        return set;
    }

    private static void RotateCubeFacesY(CubeFaceSet set, int turns)
    {
        turns = ((turns % 4) + 4) % 4;
        for (int i = 0; i < turns; i++)
        {
            var oldNorth = set.North;
            var oldEast = set.East;
            var oldSouth = set.South;
            var oldWest = set.West;

            set.North = oldEast;
            set.East = oldSouth;
            set.South = oldWest;
            set.West = oldNorth;
        }
    }

    private static vec2[] GetFaceUv(string faceName, float[]? uvTag, int rotation)
    {
        float uMin;
        float vMin;
        float uMax;
        float vMax;

        if (uvTag != null && uvTag.Length == 4)
        {
            uMin = uvTag[0] / 16f;
            vMin = uvTag[1] / 16f;
            uMax = uvTag[2] / 16f;
            vMax = uvTag[3] / 16f;
        }
        else
        {
            (uMin, vMin, uMax, vMax) = faceName switch
            {
                "down" => (0f, 0f, 1f, 1f),
                "up" => (0f, 1f, 1f, 0f),
                "north" => (1f, 0f, 0f, 1f),
                "south" => (0f, 0f, 1f, 1f),
                "west" => (0f, 0f, 1f, 1f),
                "east" => (1f, 0f, 0f, 1f),
                _ => (0f, 0f, 1f, 1f)
            };
        }

        (float u, float v)[] corners = rotation switch
        {
            90 => new[] { (uMin, vMax), (uMin, vMin), (uMax, vMin), (uMax, vMax) },
            180 => new[] { (uMax, vMax), (uMin, vMax), (uMin, vMin), (uMax, vMin) },
            270 => new[] { (uMax, vMin), (uMax, vMax), (uMin, vMax), (uMin, vMin) },
            _ => new[] { (uMin, vMin), (uMax, vMin), (uMax, vMax), (uMin, vMax) }
        };

        return new[]
        {
            new vec2(corners[0].u, corners[0].v),
            new vec2(corners[1].u, corners[1].v),
            new vec2(corners[2].u, corners[2].v),
            new vec2(corners[3].u, corners[3].v)
        };
    }

    private static string ResolveTerrainTextureKeyForPack(string baseTextureKey, string resourcePackId)
    {
        string normalizedPackId = MinecraftDataLoader.NormalizeResourcePackId(resourcePackId);
        if (string.IsNullOrWhiteSpace(normalizedPackId))
            return baseTextureKey;

        string namespaced = MinecraftDataLoader.BuildResourcePackTextureKeyFromId(normalizedPackId, baseTextureKey);
        return TerrainAtlas.Textures.ContainsKey(namespaced) ? namespaced : baseTextureKey;
    }

    private List<MeshTemplate> BuildVariantTemplates(BlockVariantEntry variant)
    {
        var templates = new List<MeshTemplate>();
        var meshes = BuildVariantMeshes(variant);

        foreach (var mesh in meshes)
        {
            templates.Add(new MeshTemplate
            {
                TextureId = mesh.TextureId,
                Vertices = mesh.Vertices.ToArray(),
                Normals = mesh.Normals.ToArray(),
                TexCoords = mesh.TexCoords.ToArray()
            });
            mesh.Dispose();
        }

        return templates;
    }

    private List<MeshTemplate> BuildVariantTemplates(IEnumerable<BlockVariantEntry> variants)
    {
        var templates = new List<MeshTemplate>();

        foreach (var variant in variants)
            templates.AddRange(BuildVariantTemplates(variant));

        return templates;
    }

    private List<Mesh> BuildVariantMeshes(BlockVariantEntry variant)
    {
        var meshes = new List<Mesh>();

        AppendBlockMeshesForPreview(meshes, variant);
        if (variant.TopHalf != null)
        {
            var top = new List<Mesh>();
            AppendBlockMeshesForPreview(top, variant.TopHalf);
            var shift = new vec3(variant.PartOffsetX, variant.PartOffsetY, variant.PartOffsetZ);
            foreach (var m in top)
            {
                for (int i = 0; i < m.Vertices.Count; i++)
                    m.Vertices[i] += shift;
                m.Upload();
            }
            meshes.AddRange(top);
        }

        return meshes;
    }

    private static bool TryDecodeVarIntArray(byte[] data, int expectedCount, out int[] values)
    {
        values = new int[expectedCount];
        int index = 0;
        int offset = 0;

        while (offset < data.Length && index < expectedCount)
        {
            int numRead = 0;
            int result = 0;
            byte read;

            do
            {
                if (offset >= data.Length) return false;
                read = data[offset++];
                int value = read & 0x7F;
                result |= value << (7 * numRead);
                numRead++;
                if (numRead > 5) return false;
            }
            while ((read & 0x80) != 0);

            values[index++] = result;
        }

        return index == expectedCount;
    }

    private static bool TryParsePaletteState(
        string paletteState,
        out string blockName,
        out Dictionary<string, string> properties)
    {
        properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string rawName = paletteState;
        int bracket = paletteState.IndexOf('[');
        if (bracket >= 0)
            rawName = paletteState[..bracket];

        int colon = rawName.IndexOf(':');
        blockName = (colon >= 0 ? rawName[(colon + 1)..] : rawName).Trim();
        if (string.IsNullOrEmpty(blockName)) return false;

        if (bracket < 0) return true;

        int endBracket = paletteState.LastIndexOf(']');
        if (endBracket <= bracket) return true;

        string body = paletteState[(bracket + 1)..endBracket];
        foreach (string token in body.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = token.IndexOf('=');
            if (eq <= 0 || eq >= token.Length - 1) continue;

            string key = token[..eq].Trim();
            string value = token[(eq + 1)..].Trim();
            if (key.Length > 0)
                properties[key] = value;
        }

        return true;
    }

    private static bool TryParseVariantHintProperties(string? hint, out Dictionary<string, string> props)
    {
        props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(hint)) return false;

        foreach (string token in hint.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = token.IndexOf('=');
            if (eq <= 0 || eq >= token.Length - 1) continue;

            string key = token[..eq].Trim();
            string value = token[(eq + 1)..].Trim();
            if (key.Length > 0)
                props[key] = value;
        }

        return props.Count > 0;
    }

    private static Dictionary<string, string> ParseVariantKeyProperties(string variantKey)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(variantKey) ||
            string.Equals(variantKey, "default", StringComparison.OrdinalIgnoreCase))
            return props;

        foreach (string token in variantKey.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = token.IndexOf('=');
            if (eq <= 0 || eq >= token.Length - 1) continue;

            string key = token[..eq].Trim();
            string value = token[(eq + 1)..].Trim();
            if (key.Length > 0)
                props[key] = value;
        }

        return props;
    }

    private static BlockVariantEntry? PickBestVariantForProperties(
        IReadOnlyList<BlockVariantEntry> variants,
        Dictionary<string, string>? desiredProps,
        string? variantHint)
    {
        if (variants.Count == 0) return null;

        if (desiredProps == null || desiredProps.Count == 0)
            return PickBestVariant(variants, variantHint);

        BlockVariantEntry? best = null;
        int bestScore = -1;

        foreach (var variant in variants)
        {
            var props = ParseVariantKeyProperties(variant.VariantKey);
            if (props.Count == 0)
            {
                if (best == null)
                    best = variant;
                continue;
            }

            int score = 0;
            bool invalid = false;
            foreach (var kv in props)
            {
                if (!desiredProps.TryGetValue(kv.Key, out string? desiredValue))
                    continue;

                if (!string.Equals(desiredValue, kv.Value, StringComparison.OrdinalIgnoreCase))
                {
                    invalid = true;
                    break;
                }

                score++;
            }

            if (invalid) continue;
            if (score > bestScore)
            {
                bestScore = score;
                best = variant;
            }
        }

        return best ?? PickBestVariant(variants, variantHint);
    }

    private static BlockVariantEntry PickBestVariant(IReadOnlyList<BlockVariantEntry> variants, string? variantHint)
    {
        if (variants.Count == 0)
            throw new InvalidOperationException("Cannot pick a variant from an empty list.");

        if (string.IsNullOrWhiteSpace(variantHint))
            return variants[0];

        var exact = variants.FirstOrDefault(v =>
            string.Equals(v.VariantKey, variantHint, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var containing = variants.FirstOrDefault(v =>
            v.VariantKey.Contains(variantHint, StringComparison.OrdinalIgnoreCase));
        return containing ?? variants[0];
    }

    private static MeshAccumulator GetOrCreateAccumulator(Dictionary<uint, MeshAccumulator> merged, uint textureId)
    {
        if (!merged.TryGetValue(textureId, out var acc))
        {
            acc = new MeshAccumulator();
            merged[textureId] = acc;
        }

        return acc;
    }

    private static void AppendTemplate(MeshAccumulator acc, MeshTemplate template, float px, float py, float pz)
    {
        var shift = new vec3(px, py, pz);
        for (int i = 0; i < template.Vertices.Length; i++)
        {
            acc.Vertices.Add(template.Vertices[i] + shift);
            acc.Normals.Add(i < template.Normals.Length ? template.Normals[i] : vec3.UnitY);
            acc.TexCoords.Add(i < template.TexCoords.Length ? template.TexCoords[i] : vec2.Zero);
        }
    }

    private static void EmitCubeFacesWithCulling(
        Dictionary<uint, MeshAccumulator> merged,
        CubeFaceSet faces,
        VariantRenderInfo?[] voxels,
        int width,
        int height,
        int length,
        int x,
        int y,
        int z,
        float px,
        float py,
        float pz)
    {
        bool IsOccluded(int nx, int ny, int nz)
        {
            if (nx < 0 || ny < 0 || nz < 0 || nx >= width || ny >= height || nz >= length)
                return false;

            int nIndex = ny * width * length + nz * width + nx;
            var n = voxels[nIndex];
            return n != null && n.IsCullableCube;
        }

        if (!IsOccluded(x, y + 1, z) && faces.Up != null)
            EmitFace(merged, faces.Up, px, py, pz, "up");
        if (!IsOccluded(x, y - 1, z) && faces.Down != null)
            EmitFace(merged, faces.Down, px, py, pz, "down");
        if (!IsOccluded(x, y, z - 1) && faces.North != null)
            EmitFace(merged, faces.North, px, py, pz, "north");
        if (!IsOccluded(x, y, z + 1) && faces.South != null)
            EmitFace(merged, faces.South, px, py, pz, "south");
        if (!IsOccluded(x - 1, y, z) && faces.West != null)
            EmitFace(merged, faces.West, px, py, pz, "west");
        if (!IsOccluded(x + 1, y, z) && faces.East != null)
            EmitFace(merged, faces.East, px, py, pz, "east");
    }

    private static void EmitFace(
        Dictionary<uint, MeshAccumulator> merged,
        CubeFaceInfo face,
        float px,
        float py,
        float pz,
        string faceName)
    {
        var acc = GetOrCreateAccumulator(merged, face.TextureId);

        vec3 v0;
        vec3 v1;
        vec3 v2;
        vec3 v3;
        vec3 n;

        switch (faceName)
        {
            case "up":
                v0 = new vec3(px - 0.5f, py + 0.5f, pz + 0.5f);
                v1 = new vec3(px + 0.5f, py + 0.5f, pz + 0.5f);
                v2 = new vec3(px + 0.5f, py + 0.5f, pz - 0.5f);
                v3 = new vec3(px - 0.5f, py + 0.5f, pz - 0.5f);
                n = new vec3(0f, 1f, 0f);
                break;
            case "down":
                v0 = new vec3(px - 0.5f, py - 0.5f, pz - 0.5f);
                v1 = new vec3(px + 0.5f, py - 0.5f, pz - 0.5f);
                v2 = new vec3(px + 0.5f, py - 0.5f, pz + 0.5f);
                v3 = new vec3(px - 0.5f, py - 0.5f, pz + 0.5f);
                n = new vec3(0f, -1f, 0f);
                break;
            case "north":
                v0 = new vec3(px - 0.5f, py + 0.5f, pz - 0.5f);
                v1 = new vec3(px + 0.5f, py + 0.5f, pz - 0.5f);
                v2 = new vec3(px + 0.5f, py - 0.5f, pz - 0.5f);
                v3 = new vec3(px - 0.5f, py - 0.5f, pz - 0.5f);
                n = new vec3(0f, 0f, -1f);
                break;
            case "south":
                v0 = new vec3(px + 0.5f, py + 0.5f, pz + 0.5f);
                v1 = new vec3(px - 0.5f, py + 0.5f, pz + 0.5f);
                v2 = new vec3(px - 0.5f, py - 0.5f, pz + 0.5f);
                v3 = new vec3(px + 0.5f, py - 0.5f, pz + 0.5f);
                n = new vec3(0f, 0f, 1f);
                break;
            case "west":
                v0 = new vec3(px - 0.5f, py + 0.5f, pz + 0.5f);
                v1 = new vec3(px - 0.5f, py + 0.5f, pz - 0.5f);
                v2 = new vec3(px - 0.5f, py - 0.5f, pz - 0.5f);
                v3 = new vec3(px - 0.5f, py - 0.5f, pz + 0.5f);
                n = new vec3(-1f, 0f, 0f);
                break;
            default: // east
                v0 = new vec3(px + 0.5f, py + 0.5f, pz - 0.5f);
                v1 = new vec3(px + 0.5f, py + 0.5f, pz + 0.5f);
                v2 = new vec3(px + 0.5f, py - 0.5f, pz + 0.5f);
                v3 = new vec3(px + 0.5f, py - 0.5f, pz - 0.5f);
                n = new vec3(1f, 0f, 0f);
                break;
        }

        // Tri 1
        acc.Vertices.Add(v0); acc.Normals.Add(n); acc.TexCoords.Add(face.Uv[0]);
        acc.Vertices.Add(v1); acc.Normals.Add(n); acc.TexCoords.Add(face.Uv[1]);
        acc.Vertices.Add(v2); acc.Normals.Add(n); acc.TexCoords.Add(face.Uv[2]);
        // Tri 2
        acc.Vertices.Add(v0); acc.Normals.Add(n); acc.TexCoords.Add(face.Uv[0]);
        acc.Vertices.Add(v2); acc.Normals.Add(n); acc.TexCoords.Add(face.Uv[2]);
        acc.Vertices.Add(v3); acc.Normals.Add(n); acc.TexCoords.Add(face.Uv[3]);
    }

    private static bool TryResolveLegacyBlock(
        int blockId,
        int blockData,
        out string blockName,
        out string? variantHint)
    {
        variantHint = null;

        switch (blockId)
        {
            case 0: blockName = "air"; return true;
            case 1: blockName = "stone"; return true;
            case 2: blockName = "grass_block"; return true;
            case 3: blockName = "dirt"; return true;
            case 4: blockName = "cobblestone"; return true;
            case 5:
                blockName = (blockData & 0x3) switch
                {
                    1 => "spruce_planks",
                    2 => "birch_planks",
                    3 => "jungle_planks",
                    _ => "oak_planks"
                };
                return true;
            case 6:
                blockName = (blockData & 0x7) switch
                {
                    1 => "spruce_sapling",
                    2 => "birch_sapling",
                    3 => "jungle_sapling",
                    4 => "acacia_sapling",
                    5 => "dark_oak_sapling",
                    _ => "oak_sapling"
                };
                return true;
            case 7: blockName = "bedrock"; return true;
            case 8: blockName = "water"; variantHint = "level=0"; return true;
            case 9: blockName = "water"; variantHint = "level=0"; return true;
            case 10: blockName = "lava"; variantHint = "level=0"; return true;
            case 11: blockName = "lava"; variantHint = "level=0"; return true;
            case 12: blockName = (blockData & 0x1) == 1 ? "red_sand" : "sand"; return true;
            case 13: blockName = "gravel"; return true;
            case 14: blockName = "gold_ore"; return true;
            case 15: blockName = "iron_ore"; return true;
            case 16: blockName = "coal_ore"; return true;
            case 17:
                blockName = (blockData & 0x3) switch
                {
                    1 => "spruce_log",
                    2 => "birch_log",
                    3 => "jungle_log",
                    _ => "oak_log"
                };
                variantHint = (blockData & 0xC) switch
                {
                    0x4 => "axis=x",
                    0x8 => "axis=z",
                    _ => "axis=y"
                };
                return true;
            case 18:
                blockName = (blockData & 0x3) switch
                {
                    1 => "spruce_leaves",
                    2 => "birch_leaves",
                    3 => "jungle_leaves",
                    _ => "oak_leaves"
                };
                return true;
            case 19: blockName = "sponge"; return true;
            case 20: blockName = "glass"; return true;
            case 21: blockName = "lapis_ore"; return true;
            case 22: blockName = "lapis_block"; return true;
            case 23: blockName = "dispenser"; return true;
            case 24: blockName = "sandstone"; return true;
            case 25: blockName = "note_block"; return true;
            case 26: blockName = "red_bed"; return true;
            case 27: blockName = "powered_rail"; return true;
            case 28: blockName = "detector_rail"; return true;
            case 29: blockName = "sticky_piston"; return true;
            case 30: blockName = "cobweb"; return true;
            case 31:
                blockName = (blockData & 0x3) switch
                {
                    1 => "grass",
                    2 => "fern",
                    _ => "dead_bush"
                };
                return true;
            case 32: blockName = "dead_bush"; return true;
            case 33: blockName = "piston"; return true;
            case 35:
                blockName = WoolColors[blockData & 0x0F] + "_wool";
                return true;
            case 37: blockName = "dandelion"; return true;
            case 38: blockName = "poppy"; return true;
            case 39: blockName = "brown_mushroom"; return true;
            case 40: blockName = "red_mushroom"; return true;
            case 41: blockName = "gold_block"; return true;
            case 42: blockName = "iron_block"; return true;
            case 43:
            {
                // Legacy double slabs by type metadata.
                int type = blockData & 0x7;
                blockName = type switch
                {
                    1 => "sandstone_slab",
                    2 => "oak_slab",
                    3 => "cobblestone_slab",
                    4 => "brick_slab",
                    _ => "smooth_stone_slab"
                };
                variantHint = "type=double";
                return true;
            }
            case 44:
            {
                // Legacy half slabs by type + top-bit metadata.
                int type = blockData & 0x7;
                bool isTop = (blockData & 0x8) != 0;
                blockName = type switch
                {
                    1 => "sandstone_slab",
                    2 => "oak_slab",
                    3 => "cobblestone_slab",
                    4 => "brick_slab",
                    _ => "smooth_stone_slab"
                };
                variantHint = isTop ? "type=top" : "type=bottom";
                return true;
            }
            case 45: blockName = "bricks"; return true;
            case 46: blockName = "tnt"; return true;
            case 47: blockName = "bookshelf"; return true;
            case 48: blockName = "mossy_cobblestone"; return true;
            case 49: blockName = "obsidian"; return true;
            case 50:
            {
                // 1-4 are wall-mounted; 5 (and 0) are standing torches.
                if (TryLegacyHorizontalFacingFromTorchData(blockData, out string? torchFacing))
                {
                    blockName = "wall_torch";
                    variantHint = $"facing={torchFacing}";
                }
                else
                {
                    blockName = "torch";
                }
                return true;
            }
            case 53:
                blockName = "oak_stairs";
                variantHint = LegacyStairsVariantHint(blockData);
                return true;
            case 54:
            {
                blockName = "chest";
                if (TryLegacyHorizontalFacingFrom2To5(blockData, out string? chestFacing))
                    variantHint = $"facing={chestFacing}";
                return true;
            }
            case 56: blockName = "diamond_ore"; return true;
            case 57: blockName = "diamond_block"; return true;
            case 58: blockName = "crafting_table"; return true;
            case 60: blockName = "farmland"; return true;
            case 61:
            {
                blockName = "furnace";
                if (TryLegacyFurnaceFacing(blockData, out string? furnaceFacing))
                    variantHint = $"facing={furnaceFacing},lit=false";
                else
                    variantHint = "lit=false";
                return true;
            }
            case 62:
            {
                blockName = "furnace";
                if (TryLegacyFurnaceFacing(blockData, out string? furnaceFacing))
                    variantHint = $"facing={furnaceFacing},lit=true";
                else
                    variantHint = "lit=true";
                return true;
            }
            case 63: blockName = "oak_sign"; return true;
            case 64: blockName = "oak_door"; return true; // contextual state resolved in TryLoadLegacyBlocks
            case 65: blockName = "ladder"; return true;
            case 67:
                blockName = "cobblestone_stairs";
                variantHint = LegacyStairsVariantHint(blockData);
                return true;
            case 68:
            {
                if (TryLegacyHorizontalFacingFrom2To5(blockData, out string? signFacing))
                {
                    blockName = "oak_wall_sign";
                    variantHint = $"facing={OppositeFacing(signFacing ?? "north")}";
                }
                else
                {
                    blockName = "oak_sign";
                }
                return true;
            }
            case 69: blockName = "lever"; return true;
            case 70: blockName = "stone_pressure_plate"; return true;
            case 71: blockName = "iron_door"; return true; // contextual state resolved in TryLoadLegacyBlocks
            case 72: blockName = "oak_pressure_plate"; return true;
            case 73: blockName = "redstone_ore"; return true;
            case 74: blockName = "redstone_ore"; return true;
            case 76:
            {
                if (TryLegacyHorizontalFacingFromTorchData(blockData, out string? redTorchFacing))
                {
                    blockName = "redstone_wall_torch";
                    variantHint = $"facing={redTorchFacing},lit=true";
                }
                else
                {
                    blockName = "redstone_torch";
                    variantHint = "lit=true";
                }
                return true;
            }
            case 77:
            {
                blockName = "stone_button";
                bool powered = (blockData & 0x8) != 0;
                int orient = blockData & 0x7;
                if (TryLegacyHorizontalFacingFromButtonData(orient, out string? buttonFacing))
                    variantHint = $"face=wall,facing={buttonFacing},powered={(powered ? "true" : "false")}";
                else
                    variantHint = $"face=wall,facing=north,powered={(powered ? "true" : "false")}";
                return true;
            }
            case 78: blockName = "snow"; return true;
            case 79: blockName = "ice"; return true;
            case 80: blockName = "snow_block"; return true;
            case 81: blockName = "cactus"; return true;
            case 82: blockName = "clay"; return true;
            case 84: blockName = "jukebox"; return true;
            case 85: blockName = "oak_fence"; return true;
            case 86: blockName = "carved_pumpkin"; return true;
            case 87: blockName = "netherrack"; return true;
            case 88: blockName = "soul_sand"; return true;
            case 89: blockName = "glowstone"; return true;
            case 91: blockName = "jack_o_lantern"; return true;
            case 96:
            {
                // Legacy trapdoor data: lower bits = facing, bit2=open, bit3=top-half.
                blockName = "oak_trapdoor";
                bool open = (blockData & 0x4) != 0;
                bool top = (blockData & 0x8) != 0;
                string facing = (blockData & 0x3) switch
                {
                    0 => "north",
                    1 => "south",
                    2 => "east",
                    3 => "west",
                    _ => "north"
                };
                variantHint = $"facing={facing},half={(top ? "top" : "bottom")},open={(open ? "true" : "false")}";
                return true;
            }
            case 95: blockName = WoolColors[blockData & 0x0F] + "_stained_glass"; return true;
            case 98: blockName = "stone_bricks"; return true;
            case 103: blockName = "melon"; return true;
            case 107: blockName = "oak_fence_gate"; return true;
            case 108:
                blockName = "brick_stairs";
                variantHint = LegacyStairsVariantHint(blockData);
                return true;
            case 109:
                blockName = "stone_brick_stairs";
                variantHint = LegacyStairsVariantHint(blockData);
                return true;
            case 112: blockName = "nether_bricks"; return true;
            case 114: blockName = "nether_brick_fence"; return true;
            case 121: blockName = "end_stone"; return true;
            case 123: blockName = "redstone_lamp"; return true;
            case 125: blockName = "double_oak_slab"; return true;
            case 126: blockName = "oak_slab"; return true;
            case 128:
                blockName = "sandstone_stairs";
                variantHint = LegacyStairsVariantHint(blockData);
                return true;
            case 129: blockName = "emerald_ore"; return true;
            case 133: blockName = "emerald_block"; return true;
            case 134:
                blockName = "spruce_stairs";
                variantHint = LegacyStairsVariantHint(blockData);
                return true;
            case 135:
                blockName = "birch_stairs";
                variantHint = LegacyStairsVariantHint(blockData);
                return true;
            case 136:
                blockName = "jungle_stairs";
                variantHint = LegacyStairsVariantHint(blockData);
                return true;
            case 146:
            {
                blockName = "trapped_chest";
                if (TryLegacyHorizontalFacingFrom2To5(blockData, out string? trappedFacing))
                    variantHint = $"facing={trappedFacing}";
                return true;
            }
            case 152: blockName = "redstone_block"; return true;
            case 155: blockName = "quartz_block"; return true;
            case 156:
                blockName = "quartz_stairs";
                variantHint = LegacyStairsVariantHint(blockData);
                return true;
            case 159: blockName = WoolColors[blockData & 0x0F] + "_terracotta"; return true;
            case 161:
                blockName = (blockData & 0x1) == 1 ? "dark_oak_leaves" : "acacia_leaves";
                return true;
            case 162:
                blockName = (blockData & 0x1) == 1 ? "dark_oak_log" : "acacia_log";
                variantHint = (blockData & 0xC) switch
                {
                    0x4 => "axis=x",
                    0x8 => "axis=z",
                    _ => "axis=y"
                };
                return true;
            case 163:
                blockName = "acacia_stairs";
                variantHint = LegacyStairsVariantHint(blockData);
                return true;
            case 164:
                blockName = "dark_oak_stairs";
                variantHint = LegacyStairsVariantHint(blockData);
                return true;
            case 170: blockName = "hay_block"; return true;
            case 172: blockName = "terracotta"; return true;
            case 173: blockName = "coal_block"; return true;
            case 174: blockName = "packed_ice"; return true;
            case 175: blockName = "sunflower"; return true;
            default:
                blockName = string.Empty;
                return false;
        }
    }

    private static string LegacyStairsVariantHint(int data)
    {
        // Legacy stair metadata stores the ascending direction; modern facing is opposite.
        string facing = (data & 0x3) switch
        {
            0 => "west",
            1 => "east",
            2 => "north",
            3 => "south",
            _ => "north"
        };

        string half = (data & 0x4) != 0 ? "top" : "bottom";
        return $"facing={facing},half={half},shape=straight";
    }

    private static bool TryLegacyHorizontalFacingFrom2To5(int data, out string? facing)
    {
        facing = (data & 0x7) switch
        {
            2 => "south",
            3 => "north",
            4 => "west",
            5 => "east",
            _ => null
        };
        return facing != null;
    }

    private static bool TryLegacyHorizontalFacingFromTorchData(int data, out string? facing)
    {
        facing = (data & 0x7) switch
        {
            1 => "east",
            2 => "west",
            3 => "north",
            4 => "south",
            _ => null
        };
        return facing != null;
    }

    private static bool TryLegacyHorizontalFacingFromButtonData(int data, out string? facing)
    {
        facing = data switch
        {
            1 => "east",
            2 => "west",
            3 => "north",
            4 => "south",
            _ => null
        };
        return facing != null;
    }

    private static bool TryLegacyFurnaceFacing(int data, out string? facing)
    {
        facing = (data & 0x7) switch
        {
            2 => "north",
            3 => "south",
            4 => "west",
            5 => "east",
            _ => null
        };
        return facing != null;
    }

    private static string OppositeFacing(string facing)
    {
        return facing switch
        {
            "north" => "south",
            "south" => "north",
            "east" => "west",
            "west" => "east",
            _ => facing
        };
    }

    private static bool TryResolveLegacyDoor(
        int blockId,
        int blockData,
        byte[] blocks,
        byte[] data,
        int index,
        int width,
        int height,
        int length,
        out string blockName,
        out string variantHint,
        out bool isUpperHalf)
    {
        blockName = blockId == 71 ? "iron_door" : "oak_door";

        int layerSize = width * length;
        bool isUpper = (blockData & 0x8) != 0;
        isUpperHalf = isUpper;

        int lowerData = blockData;
        int upperData = 0;

        if (isUpper)
        {
            int below = index - layerSize;
            if (below >= 0 && below < blocks.Length && blocks[below] == blockId)
                lowerData = below < data.Length ? data[below] & 0x0F : 0;
            upperData = blockData;
        }
        else
        {
            int above = index + layerSize;
            if (above >= 0 && above < blocks.Length && blocks[above] == blockId)
                upperData = above < data.Length ? data[above] & 0x0F : 0;
        }

        string facing = (lowerData & 0x3) switch
        {
            0 => "west",
            1 => "north",
            2 => "east",
            3 => "south",
            _ => "north"
        };

        bool open = (lowerData & 0x4) != 0;
        bool hingeRight = (upperData & 0x1) != 0;

        string half = isUpper ? "upper" : "lower";
        string hinge = hingeRight ? "right" : "left";
        variantHint = $"facing={facing},half={half},hinge={hinge},open={(open ? "true" : "false")}";
        return true;
    }

    private static string BuildLegacyFenceVariantHint(byte[] blocks, int index, int width, int height, int length)
    {
        int layerSize = width * length;
        int y = index / layerSize;
        int rem = index % layerSize;
        int z = rem / width;
        int x = rem % width;

        bool IsFenceLike(int nx, int ny, int nz)
        {
            if (nx < 0 || ny < 0 || nz < 0 || nx >= width || ny >= height || nz >= length)
                return false;

            int nIndex = ny * layerSize + nz * width + nx;
            int id = blocks[nIndex];
            return id == 85 || id == 107; // oak_fence and oak_fence_gate
        }

        bool north = IsFenceLike(x, y, z - 1);
        bool east = IsFenceLike(x + 1, y, z);
        bool south = IsFenceLike(x, y, z + 1);
        bool west = IsFenceLike(x - 1, y, z);

        return $"north={(north ? "true" : "false")},east={(east ? "true" : "false")},south={(south ? "true" : "false")},west={(west ? "true" : "false")}";
    }

    /// <summary>
    /// Loads a texture from the given file path and creates an OpenGL texture.
    /// Supports PNG, JPG, BMP, TGA, GIF, WebP, and TIFF formats with RGBA color components.
    /// Images are flipped vertically on load to match OpenGL conventions.
    /// </summary>
    private unsafe uint LoadPrimitiveTextureFromFile(string filePath)
    {
        if (Gl == null || !File.Exists(filePath))
            return 0;

        try
        {
            // Flip image vertically on load for OpenGL Y-axis convention
            StbImage.stbi_set_flip_vertically_on_load(1);
            
            var bytes = File.ReadAllBytes(filePath);
            ImageResult img = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
            
            // Reset to default behavior
            StbImage.stbi_set_flip_vertically_on_load(0);

            uint tex = Gl.GenTexture();
            Gl.BindTexture(GLEnum.Texture2D, tex);

            fixed (byte* p = img.Data)
                Gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba,
                    (uint)img.Width, (uint)img.Height,
                    0, GLEnum.Rgba, GLEnum.UnsignedByte, p);

            // Use linear filtering for better image quality
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
            // Allow wrapping for tileable textures
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.Repeat);
            Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.Repeat);
            Gl.BindTexture(GLEnum.Texture2D, 0);

            return tex;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SpawnMenu] Failed to load primitive texture '{filePath}': {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Copies a texture file to the project's assets folder and returns the project-relative path.
    /// If no project is active, returns the original absolute path.
    /// </summary>
    private string CopyTextureToProject(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            return "";

        if (ProjectManager == null || !ProjectManager.HasProject)
            return sourcePath;

        try
        {
            string fullSourcePath = Path.GetFullPath(sourcePath);
            var existing = ProjectManager.GetProjectAssets().FirstOrDefault(a =>
                a.AssetType == ProjectAssetType.Image &&
                string.Equals(Path.GetFullPath(a.SourcePath), fullSourcePath, StringComparison.OrdinalIgnoreCase));

            var asset = existing ?? ProjectManager.AddAsset(fullSourcePath, ProjectAssetType.Image);

            if (asset.StoredInProject && !string.IsNullOrWhiteSpace(asset.RelativePath))
                return asset.RelativePath;

            return ProjectManager.GetAssetFullPath(asset);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SpawnMenu] Failed to copy texture to project: {ex.Message}");
            return sourcePath;
        }
    }

    // ── Spawn logic ───────────────────────────────────────────────────────────

    private void TrySpawn()
    {
        if (_selectedCategory == "Items")
        {
            TrySpawnItem();
            return;
        }

        if (_selectedCategory == "Scenery")
        {
            var filteredScenery = GetFilteredObjects();
            if (_selectedObjectIndex < 0 || _selectedObjectIndex >= filteredScenery.Count) return;
            SpawnScenery(filteredScenery[_selectedObjectIndex]);
            return;
        }

        if (_selectedCategory == "Blocks")
        {
            TrySpawnBlock();
            return;
        }

        if (_selectedCategory == "Characters")
        {
            TrySpawnCharacter();
            return;
        }

        var filtered = GetFilteredObjects();
        if (_selectedObjectIndex < 0 || _selectedObjectIndex >= filtered.Count) return;

        SpawnObject(filtered[_selectedObjectIndex]);
        _isOpen = false;
    }

    private void TrySpawnBlock()
    {
        if (_selectedObjectIndex < 0) return;

        // Get the block name from the registry list (not the filtered category list,
        // since blocks uses its own per-column search)
        var blockList = BlockRegistry.Blocks;
        if (_selectedObjectIndex >= blockList.Count) return;

        string blockName = blockList[_selectedObjectIndex];

        // Default to first variant if none explicitly selected
        int variantIndex = _selectedVariantIndex >= 0 ? _selectedVariantIndex : 0;
        var variants = BlockRegistry.GetVariants(blockName);
        if (variants.Count == 0) return;
        if (variantIndex >= variants.Count) variantIndex = 0;

        var variant = variants[variantIndex];
        SpawnBlockObject(blockName, variant, GetEffectiveBlockTextureSourceId());
        _isOpen = false;
    }

    private string GetEffectiveBlockTextureSourceId()
    {
        string normalizedResourcePackId = MinecraftDataLoader.NormalizeResourcePackId(_spawnResourcePackId);
        if (!string.IsNullOrWhiteSpace(normalizedResourcePackId))
            return normalizedResourcePackId;

        return MinecraftDataLoader.NormalizeResourcePackId(_spawnBlockSourceId);
    }

    private void TrySpawnCharacter()
    {
        var chars = CharacterRegistry.Characters;
        if (_selectedObjectIndex < 0 || _selectedObjectIndex >= chars.Count) return;

        var entry = chars[_selectedObjectIndex];

        // Resolve optional texture override from the variants list
        string? textureOverride = null;
        if (entry.TextureVariants.Count > 0)
        {
            int texIdx = _selectedCharTextureIndex >= 0 &&
                         _selectedCharTextureIndex < entry.TextureVariants.Count
                ? _selectedCharTextureIndex
                : 0;

            var variant = entry.TextureVariants[texIdx];
            if (variant.IsCustom)
            {
                // Custom variant: require the user to have picked a file first.
                if (string.IsNullOrEmpty(_customCharTexturePath)) return;
                textureOverride = _customCharTexturePath;
            }
            else
            {
                textureOverride = variant.FilePath;
            }
        }

        SpawnCustomModelFromPath(entry.FilePath, textureOverride);
        _isOpen = false;
    }

    private void TrySpawnItem()
    {
        if (string.IsNullOrEmpty(_selectedTileKey)) return;
        SpawnItemObject(_selectedTileKey, _itemAtlasSource, _item3DMode);
        _isOpen = false;
    }

    private void SpawnObject(string objectName)
    {
        if (Viewport == null) return;

        int nextNum = GetNextAvailableObjectNumber(objectName);
        string fullName = nextNum > 1 ? $"{objectName}{nextNum}" : objectName;

        switch (_selectedCategory)
        {
            case "Camera":
                SpawnCameraObject(fullName);
                break;

            case "Light":
                SpawnLightObject(fullName);
                break;

            case "Custom Models":
                SpawnCustomModel(objectName);
                break;

            case "Scenery":
                SpawnScenery(objectName);
                break;

            default:
                // Primitives and any future categories that use SceneObject
                // For textured primitives, pass the selected texture
                if (_selectedCategory == "Primitives" && objectName == "Plane")
                {
                    string texturePath = "";
                    if (_selectedPrimitiveTextureId != 0 && !string.IsNullOrEmpty(_selectedPrimitiveTexturePath))
                    {
                        texturePath = CopyTextureToProject(_selectedPrimitiveTexturePath);
                    }
                    SpawnPrimitiveObject(objectName, fullName, _selectedPrimitiveTextureId, texturePath);
                }
                else
                {
                    SpawnPrimitiveObject(objectName, fullName);
                }
                break;
        }

        // The SceneTree rebuilds itself every frame from Viewport.SceneObjects,
        // so no explicit refresh call is needed after a spawn.
    }

    /// <summary>
    /// Creates and registers a <see cref="SceneObject"/> carrying an
    /// <see cref="ExtrudedItemMesh"/> built from the selected atlas tile.
    /// </summary>
    public SceneObject? SpawnItemObject(string tileKey, ItemAtlasSource atlasSource, bool is3D)
    {
        if (Viewport == null || Gl == null) return null;

        if (atlasSource == ItemAtlasSource.ItemAtlas)
            ItemsAtlas.EnsureProjectCustomTexturesLoaded();

        // Resolve texture ID and pixel data from the appropriate atlas
        uint tileTexId = 0;
        byte[]? tilePixels = null;

        if (atlasSource == ItemAtlasSource.ItemAtlas)
        {
            ItemsAtlas.Textures.TryGetValue(tileKey, out tileTexId);
            ItemsAtlas.TilePixels.TryGetValue(tileKey, out tilePixels);
        }
        else
        {
            TerrainAtlas.Textures.TryGetValue(tileKey, out tileTexId);
            TerrainAtlas.TilePixels.TryGetValue(tileKey, out tilePixels);
        }

        if (tileTexId == 0 || tilePixels == null) return null;

        string atlasLabel = atlasSource == ItemAtlasSource.ItemAtlas ? "Item" : "Block";
        string baseName   = $"{atlasLabel}[{tileKey}]";
        int nextNum       = GetNextAvailableObjectNumber(baseName);
        string fullName   = nextNum > 1 ? $"{baseName}{nextNum}" : baseName;

        var obj = new SceneObject
        {
            Name          = fullName,
            ObjectType    = baseName,
            SpawnCategory = "Items",
            TextureType   = atlasSource == ItemAtlasSource.ItemAtlas ? "item" : "block",
            ResourcePackId = GetSourceIdFromTextureKey(tileKey),
            Position      = vec3.Zero
        };
        obj.AssignObjectId();

        int tileSize = InferTileSizeFromPixels(
            tilePixels,
            atlasSource == ItemAtlasSource.ItemAtlas ? ItemsAtlas.TileSize : TerrainAtlas.TileSize);
        var mesh = new ExtrudedItemMesh(
            Gl,
            tileTexId,
            tilePixels,
            is3D: is3D,
            tileSize: tileSize,
            extrudeDepth: 1f / 16f);

        obj.AddMesh(mesh);
        Viewport.SceneObjects.Add(obj);
        return obj;
    }

    private static int InferTileSizeFromPixels(byte[]? pixels, int fallback)
    {
        if (pixels == null || pixels.Length < 4)
            return fallback;

        int pixelCount = pixels.Length / 4;
        int side = (int)Math.Sqrt(pixelCount);
        return side > 0 && side * side == pixelCount ? side : fallback;
    }

    private static bool IsTextureKeyFromSelectedSource(string textureKey, string selectedSourceId)
    {
        string selected = MinecraftDataLoader.NormalizeResourcePackId(selectedSourceId);
        string keySource = GetSourceIdFromTextureKey(textureKey);

        // Default source shows base/non-external keys.
        if (string.IsNullOrWhiteSpace(selected))
            return string.IsNullOrWhiteSpace(keySource);

        return string.Equals(keySource, selected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlockFromSelectedSource(string blockName, string selectedSourceId)
    {
        string selected = MinecraftDataLoader.NormalizeResourcePackId(selectedSourceId);
        string blockSource = MinecraftDataLoader.NormalizeResourcePackId(BlockRegistry.GetBlockSourceId(blockName));

        // Default source shows vanilla/non-namespaced blocks.
        if (string.IsNullOrWhiteSpace(selected))
            return string.IsNullOrWhiteSpace(blockSource);

        return string.Equals(blockSource, selected, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSourceIdFromTextureKey(string textureKey)
    {
        if (string.IsNullOrWhiteSpace(textureKey))
            return "";

        const string prefix = "resourcepack:";
        if (!textureKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return "";

        string rest = textureKey[prefix.Length..];
        int sep = rest.IndexOf(':');
        if (sep <= 0)
            return "";

        return MinecraftDataLoader.NormalizeResourcePackId(rest[..sep]);
    }

    // ── Public spawn helpers ─────────────────────────────────────────────────

    /// <summary>Creates and registers a <see cref="CameraSceneObject"/> in the viewport.</summary>
    public CameraSceneObject? SpawnCameraObject(string objectName)
    {
        if (Viewport == null) return null;

        // Place the new camera at the work camera's current eye position and orientation.
        var workCam = Viewport.Camera;
        vec3 spawnPos = workCam.Position;
        float spawnYaw   = workCam.Yaw;
        float spawnPitch = workCam.Pitch;

        var obj = new CameraSceneObject
        {
            Name          = objectName,
            ObjectType    = "Camera",
            SpawnCategory = "Camera",
            Position      = spawnPos,
            // Rotation stored in radians (engine convention).
            // Yaw  → Rotation.y (same sign).
            // Pitch → Rotation.x negated: mesh RotateX and camera pitch are opposite sign.
            Rotation      = new vec3(-spawnPitch, spawnYaw, 0f),
            PivotOffset   = vec3.Zero
        };
        obj.AssignObjectId();

        // Sync the embedded Camera to match the scene-object transform.
        obj.SyncCameraToTransform();

        // If the preference is enabled, copy the full work camera state into the new camera.
        if (PreferencesPanel != null && PreferencesPanel.CopyWorkCameraIntoNewCameras)
        {
            // Copy camera view parameters (Target, Yaw, Pitch, Distance, FovY)
            obj.ViewCamera.Target   = workCam.Target;
            obj.ViewCamera.Yaw      = workCam.Yaw;
            obj.ViewCamera.Pitch    = workCam.Pitch;
            obj.ViewCamera.Distance = workCam.Distance;
            obj.ViewCamera.FovY     = workCam.FovY;
            obj.ViewCamera.Near     = workCam.Near;
            obj.ViewCamera.Far      = workCam.Far;
        }

        // Load the Camera.glb mesh from embedded resources and attach it as the
        // visual representation.  We extract to a temp file because AssimpModelLoader
        // requires a file-system path.
        if (Gl != null)
        {
            var cameraModelRoot = LoadEmbeddedCameraModel(Gl);
            if (cameraModelRoot != null)
            {
                // Flatten visuals from the loaded hierarchy into the camera object.
                FlattenVisualsInto(cameraModelRoot, obj);

                // Mark every camera mesh as unlit (flat colour, no shading) and as
                // an overlay (renders in front of all scene geometry).
                foreach (var mesh in obj.Visuals)
                {
                    mesh.Unlit             = true;
                    mesh.DepthTestDisabled = true;
                }
            }

            // Add an invisible cube for object picking (same approach as lights).
            var pickMesh = new CubeMesh(Gl)
            {
                Alpha    = 0f,
                Albedo   = vec3.Zero,
                PickOnly = true
            };
            obj.AddMesh(pickMesh);
        }

        Viewport.SceneObjects.Add(obj);
        return obj;
    }

    /// <summary>
    /// Extracts the embedded <c>Camera.glb</c> to a temporary file and loads it
    /// via <see cref="AssimpModelLoader"/>.  Returns null on failure.
    /// </summary>
    private static SceneObject? LoadEmbeddedCameraModel(Silk.NET.OpenGL.GL gl)
    {
        const string resourceName = "MineImatorSimplyRemade.assets.mesh.Camera.glb";
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            Console.Error.WriteLine("[SpawnMenu] Embedded Camera.glb not found.");
            return null;
        }

        // Write to a temp file so Assimp can load it.
        string tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "MineImatorSimplyRemade_Camera.glb");
        using (var fs = System.IO.File.Create(tempPath))
            stream.CopyTo(fs);

        return MineImatorSimplyRemade.core.mdl.AssimpModelLoader.Load(gl, tempPath);
    }

    /// <summary>
    /// Recursively collects all <see cref="Mesh"/> visuals from <paramref name="source"/>
    /// and its children and adds them to <paramref name="target"/>'s Visuals list.
    /// </summary>
    private static void FlattenVisualsInto(
        MineImatorSimplyRemadeNuxi.core.objs.SceneObject source,
        MineImatorSimplyRemadeNuxi.core.objs.SceneObject target)
    {
        foreach (var mesh in source.Visuals)
            target.AddMesh(mesh);

        foreach (var child in source.Children)
            FlattenVisualsInto(child, target);
    }

    /// <summary>Creates and registers a <see cref="LightSceneObject"/> in the viewport.</summary>
    public LightSceneObject? SpawnLightObject(string objectName)
    {
        if (Viewport == null) return null;

        var obj = new LightSceneObject
        {
            Name          = objectName,
            ObjectType    = "Point Light",
            SpawnCategory = "Light",
            Position      = vec3.Zero,
            PivotOffset   = vec3.Zero,
        };
        obj.AssignObjectId();

        // Add a fully-transparent cube so the pick pass can detect clicks on the
        // light (the billboard geometry lives outside the normal Visuals pipeline).
        // Alpha = 0 → invisible in normal/transparent render passes;
        // the flat-colour pick shader ignores alpha so it still works for selection.
        if (Gl != null)
        {
            var pickMesh = new CubeMesh(Gl)
            {
                Alpha    = 0f,       // invisible in normal rendering
                Albedo   = vec3.Zero,
                PickOnly = true
            };
            obj.AddMesh(pickMesh);
        }

        Viewport.SceneObjects.Add(obj);
        return obj;
    }

    /// <summary>Creates and registers a primitive <see cref="SceneObject"/> in the viewport.</summary>
    public SceneObject? SpawnPrimitiveObject(string primitiveType, string objectName, uint textureId = 0, string texturePath = "")
    {
        if (Viewport == null) return null;

        var obj = new SceneObject
        {
            Name          = objectName,
            ObjectType    = primitiveType,
            SpawnCategory = "Primitives",
            Position      = vec3.Zero,
            PivotOffset   = new vec3(0f, 0.5f, 0f),
            AlbedoTexturePath = texturePath
        };
        obj.AssignObjectId();

        // Create mesh geometry for supported primitive types.
        if (primitiveType == "Plane" && Gl != null)
        {
            // 1-unit × 1-unit vertical (XY) plane
            var mesh = new PlaneMesh(Gl, 1f, 1f, PlaneOrientation.XY);
            
            // Apply texture if provided
            if (textureId != 0)
            {
                mesh.TextureId = textureId;
                // Enable backface culling for proper rendering
                mesh.DoubleSided = false;
                
                // Configure material for transparency
                if (mesh.GetSurfaceCount() > 0)
                {
                    var material = mesh.SurfaceGetMaterial(0);
                    if (material is StandardMaterial stdMat)
                    {
                        stdMat.AlbedoColor = new vec4(1f, 1f, 1f, 1f); // White for full color pass-through
                        stdMat.DoubleSided = false;
                    }
                }
            }
            
            obj.AddMesh(mesh);
        }

        if (primitiveType == "Cube" && Gl != null)
        {
            obj.AddMesh(new CubeMesh(Gl));
        }

        Viewport.SceneObjects.Add(obj);
        return obj;
    }

    /// <summary>
    /// Creates and registers a block <see cref="SceneObject"/> whose geometry is
    /// built from the Minecraft model JSON for the chosen variant.
    /// For two-block-tall blocks (doors), the top half's meshes are added to the
    /// same object with their vertices offset +1 in Y so the door is a single unit.
    /// </summary>
    public SceneObject? SpawnBlockObject(string blockName, BlockVariantEntry variant, string resourcePackId = "")
    {
        if (Viewport == null || Gl == null) return null;

        string normalizedResourcePackId = MinecraftDataLoader.NormalizeResourcePackId(resourcePackId);

        int nextNum     = GetNextAvailableObjectNumber(blockName);
        string fullName = nextNum > 1 ? $"{blockName}{nextNum}" : blockName;

        var obj = new SceneObject
        {
            Name          = fullName,
            ObjectType    = blockName,
            SpawnCategory = "Blocks",
            BlockVariant  = variant.VariantKey,
            TextureType   = "block",
            ResourcePackId = normalizedResourcePackId,
            Position      = GlmSharp.vec3.Zero,
            PivotOffset   = new GlmSharp.vec3(0f, 0.5f, 0f)
        };
        obj.AssignObjectId();

        // Bottom/foot part (or full single-block)
        AddBlockMeshes(obj, variant, normalizedResourcePackId);

        // Second part — bake offset directly into the mesh vertices
        if (variant.TopHalf != null)
            AddBlockMeshes(obj, variant.TopHalf,
                           normalizedResourcePackId,
                           variant.PartOffsetX, variant.PartOffsetY, variant.PartOffsetZ);

        Viewport.SceneObjects.Add(obj);
        return obj;
    }

    public bool ApplyResourcePackToSpawnedObject(SceneObject target, string resourcePackId)
    {
        if (target == null || Viewport == null)
            return false;

        string normalizedResourcePackId = MinecraftDataLoader.NormalizeResourcePackId(resourcePackId);

        if (string.Equals(target.SpawnCategory, "Blocks", StringComparison.Ordinal))
        {
            var variants = BlockRegistry.GetVariants(target.ObjectType);
            var variant = variants.FirstOrDefault(v => string.Equals(v.VariantKey, target.BlockVariant, StringComparison.Ordinal))
                          ?? variants.FirstOrDefault();
            if (variant == null)
                return false;

            var temp = SpawnBlockObject(target.ObjectType, variant, normalizedResourcePackId);
            return temp != null && ReplaceObjectMeshesFromTempSpawn(target, temp, normalizedResourcePackId);
        }

        if (string.Equals(target.SpawnCategory, "Scenery", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(target.SourceAssetPath) || !File.Exists(target.SourceAssetPath))
                return false;

            var temp = SpawnSchematicFromPath(target.SourceAssetPath, normalizedResourcePackId);
            return temp != null && ReplaceObjectMeshesFromTempSpawn(target, temp, normalizedResourcePackId);
        }

        return false;
    }

    public bool ApplyItemTextureToSpawnedObject(SceneObject target, ItemAtlasSource atlasSource, string tileKey)
    {
        if (target == null || Viewport == null || string.IsNullOrWhiteSpace(tileKey))
            return false;

        bool is3D = target.Visuals.OfType<ExtrudedItemMesh>().FirstOrDefault()?.Is3D ?? true;
        var temp = SpawnItemObject(tileKey, atlasSource, is3D);
        if (temp == null)
            return false;

        bool ok = ReplaceObjectMeshesFromTempSpawn(target, temp, target.ResourcePackId ?? "");
        if (!ok)
            return false;

        target.ObjectType = temp.ObjectType;
        target.TextureType = atlasSource == ItemAtlasSource.BlockAtlas ? "block" : "item";
        return true;
    }

    private bool ReplaceObjectMeshesFromTempSpawn(SceneObject target, SceneObject temp, string normalizedResourcePackId)
    {
        foreach (Mesh mesh in target.Visuals.ToList())
        {
            target.RemoveMesh(mesh);
            mesh.Dispose();
        }

        foreach (Mesh mesh in temp.Visuals.ToList())
        {
            temp.RemoveMesh(mesh);
            target.AddMesh(mesh);
        }

        Viewport?.SceneObjects.Remove(temp);
        target.ResourcePackId = normalizedResourcePackId;
        target.ApplyMaterialSettingsToMeshes();
        return target.Visuals.Count > 0;
    }

    /// <summary>
    /// Builds meshes for <paramref name="variant"/> and adds them to <paramref name="obj"/>,
    /// shifting every vertex by the given block-unit offsets.
    /// </summary>
    private void AddBlockMeshes(SceneObject obj, BlockVariantEntry variant,
                                string resourcePackId = "",
                                float offsetX = 0f, float offsetY = 0f, float offsetZ = 0f)
    {
        ResolvedBlockModel? resolved = null;
        if (!string.IsNullOrEmpty(variant.ModelPath))
            resolved = BlockRegistry.ResolveModel(variant.ModelPath);

        List<Mesh> meshes;
        if (!string.IsNullOrEmpty(variant.CemPath))
            meshes = CemLoader.Load(Gl!, variant.CemPath, BlockRegistry.VersionRoot, resourcePackId);
        else if (resolved != null)
            meshes = MinecraftModelMesh.Build(Gl!, resolved, variant.RotationX, variant.RotationY, resourcePackId);
        else
            meshes = new List<Mesh> { MinecraftModelMesh.BuildTexturedFallbackCube(Gl!, null,
                blockNameHint: obj.ObjectType, resourcePackId: resourcePackId) };

        if (offsetX != 0f || offsetY != 0f || offsetZ != 0f)
        {
            var shift = new GlmSharp.vec3(offsetX, offsetY, offsetZ);
            foreach (var mesh in meshes)
            {
                for (int i = 0; i < mesh.Vertices.Count; i++)
                    mesh.Vertices[i] += shift;
                mesh.Upload();
            }
        }

        foreach (var mesh in meshes)
            obj.AddMesh(mesh);
    }

    // ── Utility helpers ───────────────────────────────────────────────────────

    private List<string> GetFilteredObjects()
    {
        if (!_categories.TryGetValue(_selectedCategory, out var all))
            return new List<string>();

        return string.IsNullOrEmpty(_searchQuery)
            ? all
            : all.Where(o => o.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Returns the lowest positive integer N such that neither
    /// "<paramref name="objectType"/>" (treated as N=1) nor
    /// "<paramref name="objectType"/>N" (N≥2) is already used in the scene.
    /// </summary>
    private int GetNextAvailableObjectNumber(string objectType)
    {
        var usedNumbers = new HashSet<int>();

        if (Viewport != null)
            foreach (var root in Viewport.SceneObjects)
                ScanNode(root);

        int next = 1;
        while (usedNumbers.Contains(next)) next++;
        return next;

        void ScanNode(SceneObject node)
        {
            var name = node.GetDisplayName();
            if (name == objectType)
            {
                usedNumbers.Add(1);
            }
            else if (name.StartsWith(objectType) && name.Length > objectType.Length)
            {
                var suffix = name[objectType.Length..];
                if (int.TryParse(suffix, out int num))
                    usedNumbers.Add(num);
            }

            foreach (var child in node.Children)
                ScanNode(child);
        }
    }

    private void UpdateCustomModelsCategory()
    {
        var list = new List<string> { "Load..." };
        list.AddRange(_customModelPaths.Select(kvp => kvp.Key));
        _categories["Custom Models"] = list;
    }

    private void AddToCustomModelHistory(string modelPath, string displayName)
    {
        if (_customModelHistory.Contains(modelPath))
        {
            _customModelHistory.Remove(modelPath);
            var oldKey = _customModelPaths.FirstOrDefault(x => x.Value == modelPath).Key;
            if (!string.IsNullOrEmpty(oldKey))
                _customModelPaths.Remove(oldKey);
        }

        _customModelHistory.Insert(0, modelPath);
        _customModelPaths[displayName] = modelPath;
        UpdateCustomModelsCategory();

        if (_selectedCategory == "Custom Models")
            _selectedObjectIndex = -1;
    }
}
