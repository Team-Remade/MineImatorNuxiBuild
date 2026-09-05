using System.Numerics;
using System.Reflection;
using Avalonia.Media.Imaging;
using Cyotek.Data.Nbt;
using GlmSharp;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.render;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using NativeFileDialogSharp;
using StbImageSharp;
using Veldrid;

namespace MineImatorSimplyRemade.core.ui.Panels;

// ── Atlas source enum ─────────────────────────────────────────────────────────

/// <summary>Which Minecraft texture atlas to source tiles from in the Items spawn tab.</summary>
public enum ItemAtlasSource
{
    ItemAtlas,
    BlockAtlas,
    LocalAtlas
}

/// <summary>
/// UI-agnostic model behind the four-column spawn menu
/// (Categories | Objects | Variants | Preview). The old ImGui rendering was
/// removed in the Avalonia+Veldrid port; the Avalonia view
/// (<c>core.ui.Dock.SpawnMenuWindow</c>) reads/writes the public state and
/// selection APIs on this class, and this class owns all spawn/mesh-building
/// logic (also used headlessly by <c>ProjectSceneSerializer</c> and
/// <c>PropertiesPanel</c>).
/// </summary>
public class SpawnMenu
{
    private const string SceneryLoadLabel = "Load schematic...";

    private static readonly string[] WoolColors =
    {
        "white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray",
        "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black"
    };

    // ── State ────────────────────────────────────────────────────────────────

    // Retry-prompt state for schematic loads that fail on invalid/corrupt data.
    private string? _pendingSchematicRetryPath;
    private string  _pendingSchematicRetryResourcePackId = "";
    private string? _pendingSchematicRetryError;
    private string? _lastSchematicLoadError;

    private string _selectedCategory  = "Primitives";
    private int    _selectedObjectIndex  = -1;
    private int    _selectedVariantIndex = -1;

    private string _searchQuery  = "";

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
    private string _itemSearchQuery  = "";

    /// <summary>Counter used to generate unique custom-item keys.</summary>
    private int _customItemTextureCounter = 1;

    // Category → list of object names
    private readonly Dictionary<string, List<string>> _categories;

    // Variant data per-object (populated for Blocks when that system is added)
    private List<string> _currentVariants = new();

    // Particle spawner category state
    private string _particleLibrarySearchQuery = "";
    private string _selectedParticleLibraryEntryId = "";

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

    // ── Blocks category state ─────────────────────────────────────────────────

    /// <summary>Search filter applied to the blocks object list.</summary>
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
    /// Orientation used when spawning the Plane primitive.
    /// </summary>
    private PlaneOrientation _selectedPrimitivePlaneOrientation = PlaneOrientation.XY;

    /// <summary>
    /// Cube UV mode for textured cube spawning. False maps each face to the
    /// full texture; true uses a 3x2 cubemap unwrap.
    /// </summary>
    private bool _selectedPrimitiveCubeMapped = false;
    private bool _selectedPrimitiveSphereSmooth = true;
    private int _selectedPrimitiveSphereSegments = 32;
    private int _selectedPrimitiveSphereRings = 16;

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
    private List<VeldridMesh> _previewMeshes = new();

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
            { "Light",      new List<string> { "Point Light", "Spot Light" } },
            {
                "Primitives", new List<string>
                {
                    "Empty", "Cube", "Sphere", "Cylinder", "Cone", "Torus", "Plane", "Capsule", "Text Mesh"
                }
            },
            // Items renders its own custom UI in the objects/variants columns.
            { "Items",        new List<string>() },
            // Blocks: populated from BlockRegistry at render time.
            { "Blocks",       new List<string>() },
            // Characters: populated from CharacterRegistry at render time.
            { "Characters",   new List<string>() },
            { "Particle Spawners", new List<string> { "Particle Spawner" } },
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

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Resource pack ids for block/schematic texture selection (index 0 = "" = Default).</summary>
    public IReadOnlyList<string> AvailableResourcePackIds => _availableResourcePackIds;

    /// <summary>Standalone resource pack ids for the Scenery category (index 0 = "" = Default).</summary>
    public IReadOnlyList<string> AvailableSceneryResourcePackIds => _availableSceneryResourcePackIds;

    /// <summary>Java-mod source ids for filtering the block list (index 0 = "" = Default).</summary>
    public IReadOnlyList<string> AvailableSourceModIds => _availableSourceModIds;

    /// <summary>Source ids for filtering the item tile grid (index 0 = "" = Default).</summary>
    public IReadOnlyList<string> AvailableItemSourceIds => _availableItemSourceIds;

    /// <summary>Selected resource pack for block/schematic spawning ("" = default).</summary>
    public string SpawnResourcePackId
    {
        get => _spawnResourcePackId;
        set => _spawnResourcePackId = MinecraftDataLoader.NormalizeResourcePackId(value);
    }

    /// <summary>Selected source mod for the block list ("" = vanilla). Clears an
    /// out-of-source block selection.</summary>
    public string SpawnBlockSourceId
    {
        get => _spawnBlockSourceId;
        set
        {
            _spawnBlockSourceId = MinecraftDataLoader.NormalizeResourcePackId(value);
            if (_selectedObjectIndex >= 0 && _selectedObjectIndex < BlockRegistry.Blocks.Count &&
                !IsBlockFromSelectedSource(BlockRegistry.Blocks[_selectedObjectIndex], _spawnBlockSourceId))
            {
                _selectedObjectIndex = -1;
                _selectedVariantIndex = -1;
                _currentVariants.Clear();
            }
        }
    }

    /// <summary>Selected source for the item tile grid ("" = base atlases). Clears an
    /// out-of-source tile selection.</summary>
    public string SpawnItemSourceId
    {
        get => _spawnItemSourceId;
        set
        {
            _spawnItemSourceId = MinecraftDataLoader.NormalizeResourcePackId(value);
            if (!string.IsNullOrWhiteSpace(_selectedTileKey) &&
                !IsTextureKeyFromSelectedSource(_selectedTileKey, _spawnItemSourceId))
            {
                _selectedTileKey = "";
            }
        }
    }

    /// <summary>
    /// Raised when a spawn action completed and the spawn-menu window should close.
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>Raised after a root object is added to the viewport scene.</summary>
    public event Action? SceneChanged;

    /// <summary>Suppresses intermediate scene notifications while project deserialization restores object state.</summary>
    public bool IsRestoringScene { get; set; }

    /// <summary>Called by the view whenever the spawn menu window is (re)opened.</summary>
    public void OnMenuOpened() => RefreshResourcePackOptions();

    private void RequestClose() => CloseRequested?.Invoke();

    private void AddToScene(SceneObject obj)
    {
        if (Viewport == null)
            return;

        Viewport.SceneObjects.Add(obj);
        if (!IsRestoringScene)
            SceneChanged?.Invoke();
    }

    /// <summary>Notifies views that a batch scene change has completed.</summary>
    public void NotifySceneChanged() => SceneChanged?.Invoke();

    /// <summary>Global search text; setting it resets the object/variant selection.</summary>
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            string normalized = value ?? "";
            if (_searchQuery == normalized) return;
            _searchQuery          = normalized;
            _selectedObjectIndex  = -1;
            _selectedVariantIndex = -1;
        }
    }

    // ── Preview mesh management ───────────────────────────────────────────────

    /// <summary>
    /// Ensures the <see cref="PreviewRenderer"/> is created and initialised,
    /// computes the current selection key, rebuilds <see cref="_previewMeshes"/>
    /// when the selection has changed, and renders them off-screen.
    /// Called by the view on a UI timer; returns the rendered preview bitmap
    /// (or null when there is nothing to show).
    /// </summary>
    public WriteableBitmap? UpdatePreview(double deltaTime)
    {
        // Lazy-init the renderer
        if (_previewRenderer == null)
        {
            _previewRenderer = new PreviewRenderer();
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

        // Render every frame (so auto-rotation plays)
        return _previewRenderer.Render(_previewMeshes, _previewKey, deltaTime, sceneRoot: _previewCharacter);
    }

    /// <summary>True when the current selection produced preview geometry.</summary>
    public bool PreviewHasGeometry => _previewMeshes.Count > 0 || _previewCharacter != null;

    /// <summary>Orbits the preview camera (called from view drag input).</summary>
    public void OrbitPreview(float deltaYaw, float deltaPitch) => _previewRenderer?.Orbit(deltaYaw, deltaPitch);

    /// <summary>Forces the preview meshes to rebuild on the next <see cref="UpdatePreview"/>.</summary>
    public void InvalidatePreview() => _previewKey = "";

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
    /// Builds and returns Veldrid meshes for the currently selected item.
    /// Returns an empty list for categories that have no geometry (Camera, Light, Empty, Custom).
    /// </summary>
    private List<VeldridMesh> BuildPreviewMeshes()
    {
        switch (_selectedCategory)
        {
            case "Items":
            {
                if (string.IsNullOrEmpty(_selectedTileKey)) return new List<VeldridMesh>();

                Texture? tileTex = null;
                byte[]? tilePixels = null;
                int tileSize;
                int tileWidth;
                int tileHeight;

                if (_itemAtlasSource == ItemAtlasSource.ItemAtlas)
                {
                    ItemsAtlas.Textures.TryGetValue(_selectedTileKey, out tileTex);
                    ItemsAtlas.TilePixels.TryGetValue(_selectedTileKey, out tilePixels);
                    ItemsAtlas.TryGetTileDimensions(_selectedTileKey, out tileWidth, out tileHeight);
                    tileSize = Math.Max(tileWidth, tileHeight);
                }
                else
                {
                    TerrainAtlas.Textures.TryGetValue(_selectedTileKey, out tileTex);
                    TerrainAtlas.TilePixels.TryGetValue(_selectedTileKey, out tilePixels);
                    tileWidth = TerrainAtlas.TileSize;
                    tileHeight = TerrainAtlas.TileSize;
                    tileSize = InferTileSizeFromPixels(tilePixels, TerrainAtlas.TileSize);
                }

                if (tileTex == null || tilePixels == null) return new List<VeldridMesh>();

                var mesh = new ExtrudedItemMesh(
                    tileTex, tilePixels,
                    is3D: _item3DMode, tileSize: tileSize, tileWidth: tileWidth, tileHeight: tileHeight,
                    extrudeDepth: 1f / 16f);
                return new List<VeldridMesh> { mesh };
            }

            case "Blocks":
            {
                if (_selectedObjectIndex < 0 ||
                    _selectedObjectIndex >= BlockRegistry.Blocks.Count)
                    return new List<VeldridMesh>();

                string blockName = BlockRegistry.Blocks[_selectedObjectIndex];
                int variantIdx   = _selectedVariantIndex >= 0 ? _selectedVariantIndex : 0;
                var variants     = BlockRegistry.GetVariants(blockName);
                if (variants.Count == 0) return new List<VeldridMesh>();
                if (variantIdx >= variants.Count) variantIdx = 0;

                var variant  = variants[variantIdx];
                var meshes   = new List<VeldridMesh>();

                AppendBlockMeshesForPreview(meshes, variant, blockName);
                if (variant.TopHalf != null)
                {
                    // Centre combined two-block object at the origin
                    var topMeshes = new List<VeldridMesh>();
                    AppendBlockMeshesForPreview(topMeshes, variant.TopHalf, blockName);
                    var combinedCenter = new Vector3(
                        variant.PartOffsetX * 0.5f,
                        variant.PartOffsetY * 0.5f,
                        variant.PartOffsetZ * 0.5f);
                    Vector3 topShift = new Vector3(
                        variant.PartOffsetX,
                        variant.PartOffsetY,
                        variant.PartOffsetZ) - combinedCenter;
                    foreach (var m in meshes)
                    {
                        for (int i = 0; i < m.Vertices.Count; i++)
                            m.Vertices[i] -= combinedCenter;
                        m.Upload(VeldridContext.StandardOutputDescription);
                    }
                    foreach (var m in topMeshes)
                    {
                        for (int i = 0; i < m.Vertices.Count; i++)
                            m.Vertices[i] += topShift;
                        m.Upload(VeldridContext.StandardOutputDescription);
                    }
                    meshes.AddRange(topMeshes);
                }
                else
                {
                    // Centre single-block meshes so they orbit around (0,0,0) nicely
                    var downShift = new Vector3(0f, -0.5f, 0f);
                    foreach (var m in meshes)
                    {
                        for (int i = 0; i < m.Vertices.Count; i++)
                            m.Vertices[i] += downShift;
                        m.Upload(VeldridContext.StandardOutputDescription);
                    }
                }

                return meshes;
            }

            case "Primitives":
            {
                var filtered = GetFilteredObjects();
                if (_selectedObjectIndex < 0 || _selectedObjectIndex >= filtered.Count)
                    return new List<VeldridMesh>();

                string name = filtered[_selectedObjectIndex];
                if (name == "Empty") return new List<VeldridMesh>();

                if (name == "Cube")   return new List<VeldridMesh> { new CubeMesh(_selectedPrimitiveCubeMapped) };
                if (name == "Sphere") return new List<VeldridMesh> { new SphereMesh(0.5f,
                    _selectedPrimitiveSphereSegments, _selectedPrimitiveSphereRings, _selectedPrimitiveSphereSmooth) };
                if (name == "Plane")  return new List<VeldridMesh> { new PlaneMesh(1f, 1f, _selectedPrimitivePlaneOrientation) };

                // For shapes not yet implemented, show a cube placeholder
                return new List<VeldridMesh> { new CubeMesh() };
            }

            case "Characters":
            {
                if (_selectedObjectIndex < 0 ||
                    _selectedObjectIndex >= CharacterRegistry.Characters.Count)
                    return new List<VeldridMesh>();

                var entry = CharacterRegistry.Characters[_selectedObjectIndex];
                if (string.IsNullOrEmpty(entry.FilePath)) return new List<VeldridMesh>();

                string ext = Path.GetExtension(entry.FilePath).ToLowerInvariant();

                SceneObject? character;

                if (ext == ".mimodel")
                {
                    // Mine Imator native format — load via MineImatorLoader.
                    string? textureOverridePath = ResolveCharacterTextureOverride(entry);

                    var loader = MineImatorLoader.Instance;
                    var model  = loader.LoadModel(entry.FilePath);
                    if (model == null) return new List<VeldridMesh>();

                    var miChar = loader.CreateCharacterFromModel(model);
                    if (miChar == null) return new List<VeldridMesh>();

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
                    character = AssimpModelLoader.Load(entry.FilePath);
                    if (character == null) return new List<VeldridMesh>();

                    // Apply the selected texture variant, same as the .mimodel branch above —
                    // Assimp always loads whatever texture is embedded/referenced by the
                    // model's own material, so the override must be stomped on afterwards.
                    string? textureOverridePath = ResolveCharacterTextureOverride(entry);
                    if (!string.IsNullOrEmpty(textureOverridePath) && File.Exists(textureOverridePath))
                    {
                        uint overrideTexId = MineImatorLoader.Instance.LoadTextureFromFile(textureOverridePath);
                        if (overrideTexId != 0)
                            ApplyTextureOverrideToCharacter(character, overrideTexId);
                    }
                }

                // Store the hierarchy so PreviewRenderer can render it with proper world matrices.
                _previewCharacter = character;

                // Return an empty flat mesh list; the renderer will walk the hierarchy.
                return new List<VeldridMesh>();
            }

            default:
                return new List<VeldridMesh>();
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
    private void AppendBlockMeshesForPreview(List<VeldridMesh> meshes, BlockVariantEntry variant, string blockName = "")
    {
        string textureSourceId = GetEffectiveBlockTextureSourceId();

        ResolvedBlockModel? resolved = null;
        if (!string.IsNullOrEmpty(variant.ModelPath))
            resolved = BlockRegistry.ResolveModel(variant.ModelPath);

        List<VeldridMesh> built;
        if (!string.IsNullOrEmpty(variant.CemPath))
            built = CemLoader.Load(variant.CemPath, BlockRegistry.VersionRoot, textureSourceId);
        else if (resolved != null)
            built = MinecraftModelMesh.Build(resolved, variant.RotationX, variant.RotationY, textureSourceId, blockName);
        else
            built = new List<VeldridMesh>
            {
                MinecraftModelMesh.BuildTexturedFallbackCube(null, blockNameHint: "", resourcePackId: textureSourceId)
            };

        ApplyVariantRotationToCemMeshes(built, variant);

        meshes.AddRange(built);
    }

    private static void ApplyVariantRotationToCemMeshes(List<VeldridMesh> meshes, BlockVariantEntry variant)
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
                if (v.X < minX) minX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Z < minZ) minZ = v.Z;
                if (v.X > maxX) maxX = v.X;
                if (v.Y > maxY) maxY = v.Y;
                if (v.Z > maxZ) maxZ = v.Z;
            }
        }

        if (!hasAnyVertex)
            return;

        var pivot = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);

        foreach (var mesh in meshes)
        {
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Vector3 v = mesh.Vertices[i] - pivot;
                v = RotateQuarterTurnsX(v, turnsX);
                v = RotateQuarterTurnsY(v, turnsY);
                mesh.Vertices[i] = v + pivot;

                if (i < mesh.Normals.Count)
                {
                    Vector3 n = mesh.Normals[i];
                    n = RotateQuarterTurnsX(n, turnsX);
                    n = RotateQuarterTurnsY(n, turnsY);
                    mesh.Normals[i] = n;
                }
            }

            mesh.Upload(VeldridContext.StandardOutputDescription);
        }
    }

    private static int NormalizeQuarterTurns(int degrees)
    {
        int normalized = ((degrees % 360) + 360) % 360;
        return normalized / 90;
    }

    private static Vector3 RotateQuarterTurnsX(Vector3 v, int turns)
    {
        return turns switch
        {
            1 => new Vector3(v.X, -v.Z, v.Y),
            2 => new Vector3(v.X, -v.Y, -v.Z),
            3 => new Vector3(v.X, v.Z, -v.Y),
            _ => v
        };
    }

    private static Vector3 RotateQuarterTurnsY(Vector3 v, int turns)
    {
        return turns switch
        {
            1 => new Vector3(v.Z, v.Y, -v.X),
            2 => new Vector3(-v.X, v.Y, -v.Z),
            3 => new Vector3(-v.Z, v.Y, v.X),
            _ => v
        };
    }

    // ── Selection API (categories / objects / variants) ────────────────────

    /// <summary>Category names in display order.</summary>
    public IReadOnlyList<string> Categories => _categories.Keys.ToList();

    /// <summary>The currently selected category name.</summary>
    public string SelectedCategory => _selectedCategory;

    /// <summary>
    /// Currently selected object index, or -1. For the standard categories this
    /// indexes <see cref="GetFilteredObjects"/>; for Blocks it indexes
    /// <see cref="BlockRegistry.Blocks"/>; for Characters it indexes
    /// <see cref="CharacterRegistry.Characters"/>.
    /// </summary>
    public int SelectedObjectIndex => _selectedObjectIndex;

    /// <summary>Variant labels for the current object selection.</summary>
    public IReadOnlyList<string> CurrentVariants => _currentVariants;

    public int SelectedVariantIndex
    {
        get => _selectedVariantIndex;
        set => _selectedVariantIndex = value;
    }

    /// <summary>Selects a category, resetting the object/variant selection when it changes.</summary>
    public void SelectCategory(string category)
    {
        if (!_categories.ContainsKey(category) || _selectedCategory == category)
            return;

        _selectedCategory          = category;
        _selectedObjectIndex        = -1;
        _selectedVariantIndex       = -1;
        _selectedCharTextureIndex   = -1;
        _customCharTexturePath      = "";
        _currentVariants.Clear();
    }

    /// <summary>Single-click selection of a standard-category object (index into <see cref="GetFilteredObjects"/>).</summary>
    public void SelectObject(int index)
    {
        var filtered = GetFilteredObjects();
        if (index < 0 || index >= filtered.Count) return;

        _selectedObjectIndex  = index;
        _selectedVariantIndex = -1;
        OnObjectSelected(filtered[index]);
    }

    /// <summary>Double-click activation of a standard-category object (spawns immediately, except "Load...").</summary>
    public void ActivateObject(int index)
    {
        var filtered = GetFilteredObjects();
        if (index < 0 || index >= filtered.Count) return;

        _selectedObjectIndex = index;
        OnObjectDoubleClicked(filtered[index]);
    }

    /// <summary>Double-click activation of a variant entry (selects it and spawns).</summary>
    public void ActivateVariant(int index)
    {
        if (index < 0 || index >= _currentVariants.Count) return;
        _selectedVariantIndex = index;
        TrySpawn();
    }

    /// <summary>Display name of the current standard-category selection, or null.</summary>
    public string? SelectedObjectName
    {
        get
        {
            var filtered = GetFilteredObjects();
            return _selectedObjectIndex >= 0 && _selectedObjectIndex < filtered.Count
                ? filtered[_selectedObjectIndex]
                : null;
        }
    }

    // ── Primitive options (Variants column state for the Primitives category) ──

    public bool PrimitiveSphereSmooth
    {
        get => _selectedPrimitiveSphereSmooth;
        set { if (_selectedPrimitiveSphereSmooth != value) { _selectedPrimitiveSphereSmooth = value; InvalidatePreview(); } }
    }

    public int PrimitiveSphereSegments
    {
        get => _selectedPrimitiveSphereSegments;
        set
        {
            int clamped = Math.Clamp(value, 3, 256);
            if (_selectedPrimitiveSphereSegments != clamped) { _selectedPrimitiveSphereSegments = clamped; InvalidatePreview(); }
        }
    }

    public int PrimitiveSphereRings
    {
        get => _selectedPrimitiveSphereRings;
        set
        {
            int clamped = Math.Clamp(value, 2, 128);
            if (_selectedPrimitiveSphereRings != clamped) { _selectedPrimitiveSphereRings = clamped; InvalidatePreview(); }
        }
    }

    public PlaneOrientation PrimitivePlaneOrientation
    {
        get => _selectedPrimitivePlaneOrientation;
        set => _selectedPrimitivePlaneOrientation = value;
    }

    public bool PrimitiveCubeMapped
    {
        get => _selectedPrimitiveCubeMapped;
        set { if (_selectedPrimitiveCubeMapped != value) { _selectedPrimitiveCubeMapped = value; InvalidatePreview(); } }
    }

    /// <summary>Path of the texture chosen for textured primitives ("" = none).</summary>
    public string SelectedPrimitiveTexturePath => _selectedPrimitiveTexturePath;

    /// <summary>True when the selected object is the Plane or Cube primitive
    /// (which support texture selection in the Variants column).</summary>
    public bool SelectedObjectSupportsPrimitiveTexture =>
        _selectedCategory == "Primitives" &&
        (SelectedObjectName == "Plane" || SelectedObjectName == "Cube");

    /// <summary>True when the selected object is the Sphere primitive.</summary>
    public bool SelectedObjectIsSpherePrimitive =>
        _selectedCategory == "Primitives" && SelectedObjectName == "Sphere";

    /// <summary>
    /// Opens a native file dialog and loads the chosen image as the primitive
    /// texture. Returns true when a texture was successfully loaded.
    /// </summary>
    public bool LoadPrimitiveTextureFromDialog()
    {
        var result = Dialog.FileOpen("png,jpg,jpeg,bmp,tga,gif,webp,tiff");
        if (!result.IsOk || string.IsNullOrWhiteSpace(result.Path) || !File.Exists(result.Path))
            return false;

        _selectedPrimitiveTexturePath = result.Path;
        _selectedPrimitiveTextureId = LoadPrimitiveTextureFromFile(result.Path);

        if (_selectedPrimitiveTextureId != 0)
        {
            _selectedVariantIndex = 1; // Select "Load texture..."
            return true;
        }

        _selectedPrimitiveTexturePath = "";
        _selectedVariantIndex = 0; // Reset to "None"
        return false;
    }

    public void ClearPrimitiveTexture()
    {
        _selectedPrimitiveTextureId = 0;
        _selectedPrimitiveTexturePath = "";
        _selectedVariantIndex = 0;
    }

    // ── Items category UI ─────────────────────────────────────────────────────

    /// <summary>Which atlas the Items tab sources tiles from; changing it resets the tile selection.</summary>
    public ItemAtlasSource ItemAtlasSourceSelection
    {
        get => _itemAtlasSource;
        set
        {
            if (_itemAtlasSource == value) return;
            _itemAtlasSource = value;
            _selectedTileKey = ""; // reset selection when switching atlas
        }
    }

    /// <summary>Search filter for the tile grid.</summary>
    public string ItemSearchQuery
    {
        get => _itemSearchQuery;
        set => _itemSearchQuery = value ?? "";
    }

    /// <summary>Currently selected tile key ("" = none).</summary>
    public string SelectedTileKey
    {
        get => _selectedTileKey;
        set => _selectedTileKey = value ?? "";
    }

    /// <summary>When true the spawned item mesh is extruded; otherwise flat.</summary>
    public bool Item3DMode
    {
        get => _item3DMode;
        set => _item3DMode = value;
    }

    /// <summary>
    /// Returns the tile keys shown in the Items tab grid for the current atlas,
    /// source and search filter, sorted alphabetically.
    /// </summary>
    public List<string> GetFilteredItemTileKeys()
    {
        if (_itemAtlasSource == ItemAtlasSource.ItemAtlas)
            ItemsAtlas.EnsureProjectCustomTexturesLoaded();

        var textures = _itemAtlasSource == ItemAtlasSource.ItemAtlas
            ? ItemsAtlas.Textures
            : TerrainAtlas.Textures;

        return textures
            .Where(static kvp => kvp.Value != null)
            .Where(kvp => IsTextureKeyFromSelectedSource(kvp.Key, _spawnItemSourceId))
            .Where(kvp => string.IsNullOrEmpty(_itemSearchQuery) || kvp.Key.Contains(_itemSearchQuery, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>Double-click activation of a tile (selects it and spawns).</summary>
    public void ActivateTile(string tileKey)
    {
        _selectedTileKey = tileKey ?? "";
        TrySpawnItem();
    }

    /// <summary>
    /// Opens a native file dialog and registers the chosen image as a custom item
    /// tile, selecting it in the Items tab. Returns the new tile key or null.
    /// </summary>
    public string? ImportCustomItemImage() => ImportCustomItemImageFromDialog(selectInSpawnMenu: true);




    private string? ImportCustomItemImageFromDialog(bool selectInSpawnMenu)
    {
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
            Console.Error.WriteLine($"Could not register custom item image in project assets: {ex.Message}");
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

    // ── Blocks category API ────────────────────────────────────────────────────

    /// <summary>Per-column search for the block list; setting it resets the block selection.</summary>
    public string BlockSearchQuery
    {
        get => _blockSearchQuery;
        set
        {
            string normalized = value ?? "";
            if (_blockSearchQuery == normalized) return;
            _blockSearchQuery    = normalized;
            _selectedObjectIndex  = -1;
            _selectedVariantIndex = -1;
            _currentVariants.Clear();
        }
    }

    /// <summary>
    /// Returns the blocks shown in the Blocks tab for the current source mod and
    /// search filters, as (registry index, block name) pairs.
    /// </summary>
    public List<(int Index, string Name)> GetFilteredBlocks()
    {
        var result = new List<(int, string)>();
        var blockList = BlockRegistry.Blocks;
        string query = string.IsNullOrEmpty(_blockSearchQuery) ? _searchQuery : _blockSearchQuery;

        for (int i = 0; i < blockList.Count; i++)
        {
            string name = blockList[i];
            if (!IsBlockFromSelectedSource(name, _spawnBlockSourceId))
                continue;

            if (!string.IsNullOrEmpty(query) &&
                !name.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add((i, name));
        }

        return result;
    }

    /// <summary>Single-click selection of a block (index into <see cref="BlockRegistry.Blocks"/>).</summary>
    public void SelectBlock(int registryIndex)
    {
        if (registryIndex < 0 || registryIndex >= BlockRegistry.Blocks.Count) return;

        _selectedObjectIndex  = registryIndex;
        _selectedVariantIndex = -1;
        OnBlockSelected(BlockRegistry.Blocks[registryIndex]);
    }

    /// <summary>Double-click activation of a block (selects it, defaults to the first variant, spawns).</summary>
    public void ActivateBlock(int registryIndex)
    {
        if (registryIndex < 0 || registryIndex >= BlockRegistry.Blocks.Count) return;

        _selectedObjectIndex = registryIndex;
        OnBlockSelected(BlockRegistry.Blocks[registryIndex]);
        if (_currentVariants.Count > 0)
            _selectedVariantIndex = 0;
        TrySpawn();
    }

    /// <summary>Name of the currently selected block, or null.</summary>
    public string? SelectedBlockName =>
        _selectedObjectIndex >= 0 && _selectedObjectIndex < BlockRegistry.Blocks.Count
            ? BlockRegistry.Blocks[_selectedObjectIndex]
            : null;

    /// <summary>Variant key of the current block selection (first variant when none chosen), or null.</summary>
    public string? SelectedBlockVariantKey
    {
        get
        {
            string? blockName = SelectedBlockName;
            if (blockName == null) return null;
            var variants = BlockRegistry.GetVariants(blockName);
            int variantIdx = _selectedVariantIndex >= 0 ? _selectedVariantIndex : 0;
            return variants.Count > 0 && variantIdx < variants.Count
                ? variants[variantIdx].VariantKey
                : null;
        }
    }

    // ── Characters category API ──────────────────────────────────────────────

    /// <summary>Per-column search for the character list; setting it resets the selection.</summary>
    public string CharSearchQuery
    {
        get => _charSearchQuery;
        set
        {
            string normalized = value ?? "";
            if (_charSearchQuery == normalized) return;
            _charSearchQuery     = normalized;
            _selectedObjectIndex  = -1;
            _selectedVariantIndex = -1;
        }
    }

    /// <summary>
    /// Returns the characters shown in the Characters tab for the current search
    /// filters, as (registry index, display label) pairs. Labels include a
    /// [group] prefix when the character lives in a sub-folder.
    /// </summary>
    public List<(int Index, string Label)> GetFilteredCharacters()
    {
        var result = new List<(int, string)>();
        var chars = CharacterRegistry.Characters;
        string query = string.IsNullOrEmpty(_charSearchQuery) ? _searchQuery : _charSearchQuery;

        for (int i = 0; i < chars.Count; i++)
        {
            var entry = chars[i];

            if (!string.IsNullOrEmpty(query) &&
                !entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !entry.Group.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            string label = string.IsNullOrEmpty(entry.Group)
                ? entry.Name
                : $"[{entry.Group}] {entry.Name}";

            result.Add((i, label));
        }

        return result;
    }

    /// <summary>Single-click selection of a character (index into <see cref="CharacterRegistry.Characters"/>).</summary>
    public void SelectCharacter(int registryIndex)
    {
        if (registryIndex < 0 || registryIndex >= CharacterRegistry.Characters.Count) return;

        _selectedObjectIndex       = registryIndex;
        _selectedVariantIndex      = -1;
        _selectedCharTextureIndex  = -1;
        _customCharTexturePath     = "";
        _currentVariants.Clear();
    }

    /// <summary>Double-click activation of a character (selects it and spawns).</summary>
    public void ActivateCharacter(int registryIndex)
    {
        if (registryIndex < 0 || registryIndex >= CharacterRegistry.Characters.Count) return;

        _selectedObjectIndex = registryIndex;
        TrySpawn();
    }

    /// <summary>The currently selected character entry, or null.</summary>
    public CharacterEntry? SelectedCharacterEntry =>
        _selectedObjectIndex >= 0 && _selectedObjectIndex < CharacterRegistry.Characters.Count
            ? CharacterRegistry.Characters[_selectedObjectIndex]
            : null;

    /// <summary>Index into the selected character's texture variants (-1 = default/first).</summary>
    public int SelectedCharTextureIndex
    {
        get => _selectedCharTextureIndex;
        set => _selectedCharTextureIndex = value;
    }

    /// <summary>Path picked for the "Custom" texture variant ("" = none chosen).</summary>
    public string CustomCharTexturePath => _customCharTexturePath;

    /// <summary>
    /// Opens a native file dialog to pick the custom character texture.
    /// Returns true when a file was chosen.
    /// </summary>
    public bool BrowseCustomCharTexture()
    {
        var result = Dialog.FileOpen("png,jpg,jpeg,tga,bmp");
        if (!result.IsOk || string.IsNullOrEmpty(result.Path))
            return false;

        _customCharTexturePath = result.Path;
        return true;
    }

    /// <summary>Double-click activation of a character texture variant (selects it and spawns).</summary>
    public void ActivateCharTexture(int variantIndex)
    {
        _selectedCharTextureIndex = variantIndex;
        TrySpawn();
    }

    // ── Particle Spawners category API ───────────────────────────────────

    public sealed class ParticleLibraryOption
    {
        public string Id = "";
        public string Name = "";
        public string ObjectType = "";
    }

    /// <summary>Search filter for the particle source (object library) list.</summary>
    public string ParticleLibrarySearchQuery
    {
        get => _particleLibrarySearchQuery;
        set => _particleLibrarySearchQuery = value ?? "";
    }

    /// <summary>Selected object-library entry id used as the particle source ("" = none).</summary>
    public string SelectedParticleLibraryEntryId
    {
        get => _selectedParticleLibraryEntryId;
        set => _selectedParticleLibraryEntryId = value ?? "";
    }

    public List<ParticleLibraryOption> GetParticleLibraryOptions()
    {
        EnsureObjectLibraryInitializedForSpawn();

        var result = new List<ParticleLibraryOption>();
        if (ProjectManager?.Manifest?.ObjectLibrary == null)
            return result;

        CollectParticleLibraryOptions(ProjectManager.Manifest.ObjectLibrary, result);

        result = result
            .GroupBy(static x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static g => g.First())
            .OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(_selectedParticleLibraryEntryId) &&
            !result.Any(x => string.Equals(x.Id, _selectedParticleLibraryEntryId, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedParticleLibraryEntryId = "";
        }

        return result;
    }

    private static void CollectParticleLibraryOptions(IEnumerable<ProjectSceneObjectEntry> nodes, List<ParticleLibraryOption> output)
    {
        foreach (var node in nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.LibraryEntryId) &&
                !string.Equals(node.SpawnCategory, "Particle Spawners", StringComparison.OrdinalIgnoreCase))
            {
                output.Add(new ParticleLibraryOption
                {
                    Id = node.LibraryEntryId,
                    Name = string.IsNullOrWhiteSpace(node.Name)
                        ? (string.IsNullOrWhiteSpace(node.ObjectType) ? "Object" : node.ObjectType)
                        : node.Name,
                    ObjectType = string.IsNullOrWhiteSpace(node.ObjectType) ? "Object" : node.ObjectType
                });
            }

            CollectParticleLibraryOptions(node.Children, output);
        }
    }

    private void EnsureObjectLibraryInitializedForSpawn()
    {
        if (ProjectManager?.Manifest == null || Viewport == null)
            return;

        ProjectManager.Manifest.ObjectLibrary ??= new List<ProjectSceneObjectEntry>();

        foreach (var root in Viewport.SceneObjects)
        {
            if (root.IsRuntimeTransient)
                continue;

            EnsureSceneLibrarySourceIdsForSpawn(root);

            if (ContainsLibraryEntryId(ProjectManager.Manifest.ObjectLibrary, root.LibrarySourceId))
                continue;

            var entry = ProjectSceneSerializer.SerializeObjectForLibrary(root);
            entry.LibraryEntryId = root.LibrarySourceId;
            entry.LibrarySourceId = root.LibrarySourceId;
            if (string.IsNullOrWhiteSpace(entry.Name))
                entry.Name = string.IsNullOrWhiteSpace(entry.ObjectType) ? "Object" : entry.ObjectType;

            ProjectManager.Manifest.ObjectLibrary.Add(entry);
        }
    }

    private static void EnsureSceneLibrarySourceIdsForSpawn(SceneObject obj)
    {
        if (obj.IsRuntimeTransient)
            return;

        if (string.IsNullOrWhiteSpace(obj.LibrarySourceId))
            obj.LibrarySourceId = string.IsNullOrWhiteSpace(obj.ObjectId) ? Guid.NewGuid().ToString("N") : obj.ObjectId;

        foreach (var child in obj.Children)
            EnsureSceneLibrarySourceIdsForSpawn(child);
    }

    private static bool ContainsLibraryEntryId(IEnumerable<ProjectSceneObjectEntry> nodes, string libraryEntryId)
    {
        if (string.IsNullOrWhiteSpace(libraryEntryId))
            return false;

        foreach (var node in nodes)
        {
            if (string.Equals(node.LibraryEntryId, libraryEntryId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (ContainsLibraryEntryId(node.Children, libraryEntryId))
                return true;
        }

        return false;
    }

    // ── Shared preview helpers ────────────────────────────────────────────────

    /// <summary>True when the current selection can be spawned (enables the Spawn button).</summary>
    public bool CanSpawn()
    {
        if (_selectedCategory == "Items")
            return !string.IsNullOrEmpty(_selectedTileKey);

        if (_selectedCategory == "Particle Spawners")
            return _selectedObjectIndex >= 0;

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
        if (_selectedCategory == "Primitives" && (objectName == "Plane" || objectName == "Cube"))
        {
            _currentVariants.Clear();
            _currentVariants.Add("None");
            _currentVariants.Add("Load texture...");
            _selectedPrimitiveCubeMapped = false;
            // Reset texture if switching away from textured primitive
            _selectedPrimitiveTextureId = 0;
            _selectedPrimitiveTexturePath = "";
            _selectedVariantIndex = 0; // Select "None" by default
        }
        else
        {
            _currentVariants.Clear();
            // Clean up texture if switching to non-textured primitive
            _selectedPrimitiveTextureId = 0;
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
        if (Viewport == null) return;

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
        if (Viewport == null) return;

        var result = Dialog.FileOpen("schematic,schem");
        if (!result.IsOk || string.IsNullOrEmpty(result.Path)) return;

        string pathToSpawn = ResolveSchematicPathForProject(result.Path);
        var root = SpawnSchematicFromPathInteractive(pathToSpawn, _spawnResourcePackId);
        if (root != null)
            RequestClose();
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
            Console.Error.WriteLine($"Could not register schematic in project assets: {ex.Message}");
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
    /// <paramref name="textureOverridePath"/> — when not null, overrides the
    /// model's default/embedded texture with this PNG path. Applied directly
    /// during load for <c>.mimodel</c>, and applied afterwards by walking the
    /// resulting hierarchy for Assimp-loaded formats (.glb/.gltf/.fbx/etc.).
    ///
    /// Returns the root <see cref="SceneObject"/> on success, or <c>null</c> on error.
    /// </summary>
    public SceneObject? SpawnCustomModelFromPath(string filePath, string? textureOverridePath = null)
    {
        if (Viewport == null) return null;

        string pathToSpawn = ResolveModelPathForProject(filePath);

        string ext = Path.GetExtension(pathToSpawn).ToLowerInvariant();

        SceneObject? root;

        switch (ext)
        {
            case ".mimodel":
                root = SpawnMineImatorModel(pathToSpawn, textureOverridePath);
                break;
            case ".miobject":
                root = SpawnMineImatorObject(pathToSpawn);
                break;
            default:
            {
                root = AssimpModelLoader.Load(pathToSpawn);

                // AssimpModelLoader always uses whatever diffuse/embedded texture
                // is baked into the GLB/GLTF/etc. material — it has no concept of
                // a texture-variant override. Apply the selected variant here by
                // stomping mesh.TextureId across the loaded hierarchy, mirroring
                // what SpawnMineImatorModel does internally for .mimodel.
                if (root != null && !string.IsNullOrEmpty(textureOverridePath) && File.Exists(textureOverridePath))
                {
                    uint overrideTexId = MineImatorLoader.Instance.LoadTextureFromFile(textureOverridePath);
                    if (overrideTexId != 0)
                        ApplyTextureOverrideToCharacter(root, overrideTexId);
                }

                break;
            }
        }

        if (root == null)
        {
            Console.Error.WriteLine($"Failed to load model: {pathToSpawn}");
            return null;
        }

        // Remember which texture variant was selected (if any) so that
        // ProjectSceneSerializer can persist it and re-apply it the next time
        // this object is re-imported from SourceAssetPath (e.g. on project
        // load) — otherwise a reload always falls back to the source file's
        // own default/embedded texture.
        root.TextureOverridePath = textureOverridePath ?? "";

        string displayName = Path.GetFileNameWithoutExtension(pathToSpawn);
        if (string.IsNullOrEmpty(root.Name)) root.Name = displayName;

        AddToCustomModelHistory(pathToSpawn, displayName);
        AddToScene(root);
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
            {
                if (existing.AssetType == ProjectAssetType.Model)
                    ProjectManager.EnsureModelAssetIntegrity(existing);
                return ProjectManager.GetAssetFullPath(existing);
            }

            var added = ProjectManager.AddAsset(fullSourcePath, ProjectAssetType.Model);
            return ProjectManager.GetAssetFullPath(added);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not register model in project assets: {ex.Message}");
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
            foreach (var mesh in root.Visuals.Where(mesh => mesh.TextureId != 0))
            {
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

        var scene = loader.CreateSceneFromMiObject(miObject, SpawnMiObjectItemViaSpawnMenu);
        if (scene == null) return null;

        scene.Name = Path.GetFileNameWithoutExtension(filePath);
        scene.SourceAssetPath = filePath;
        scene.AssignObjectId();
        return scene;
    }

    private SceneObject? SpawnMiObjectItemViaSpawnMenu(MiTemplate template, MiTimeline timeline,
        IReadOnlyDictionary<string, MiResource> resourceInfoById, string objectDirectory)
    {
        if (Viewport == null || template?.Item == null || string.IsNullOrWhiteSpace(template.Item.Tex))
            return null;
        if (!resourceInfoById.TryGetValue(template.Item.Tex, out var resource) || string.IsNullOrWhiteSpace(resource?.Filename))
            return null;

        string texturePath = Path.IsPathRooted(resource.Filename)
            ? resource.Filename
            : Path.Combine(objectDirectory, resource.Filename);
        if (!File.Exists(texturePath))
            return null;

        ImageResult image;
        try
        {
            image = ImageResult.FromMemory(File.ReadAllBytes(texturePath), ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load MIObject item texture '{texturePath}': {ex.Message}");
            return null;
        }

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

        string sheetKey = ItemsAtlas.BuildTemporaryItemSheetKey(texturePath, columns, rows);
        if (!ItemsAtlas.TryRegisterTemporaryItemSheet(sheetKey, texturePath, columns, rows))
            return null;

        string keyBase = Path.GetFileNameWithoutExtension(texturePath);
        if (string.IsNullOrWhiteSpace(keyBase))
            keyBase = timeline.Name;
        if (string.IsNullOrWhiteSpace(keyBase))
            keyBase = "miobject_item";

        string key = $"miobject:{SanitizeCustomItemKey(keyBase)}_{columnIndex}_{rowIndex}";
        while (ItemsAtlas.Textures.ContainsKey(key))
            key = $"miobject:{SanitizeCustomItemKey(keyBase)}_{columnIndex}_{rowIndex}_{_customItemTextureCounter++}";

        if (!ItemsAtlas.TryRegisterTemporaryItemTile(sheetKey, key, columnIndex, rowIndex))
            return null;

        var obj = SpawnItemObject(key, ItemAtlasSource.ItemAtlas, template.Item.ThreeD);
        if (obj == null)
            return null;

        Viewport.SceneObjects.Remove(obj);
        obj.Name = !string.IsNullOrWhiteSpace(timeline.Name) ? timeline.Name : "Item";
        obj.ObjectType = "Item";
        obj.PrimitivePlaneFaceCamera = template.Item.FaceCamera;
        obj.ResourcePackId = "";
        obj.TemporaryItemSheetPath = texturePath;
        obj.TemporaryItemSheetCacheKey = sheetKey;
        obj.TemporaryItemSheetColumns = columns;
        obj.TemporaryItemSheetRows = rows;
        obj.TemporaryItemSheetColumnIndex = columnIndex;
        obj.TemporaryItemSheetRowIndex = rowIndex;
        return obj;
    }

    public bool EnsureTemporaryItemSheetTile(ProjectSceneObjectEntry entry, string tileKey)
    {
        if (string.IsNullOrWhiteSpace(tileKey) || ItemsAtlas.Textures.ContainsKey(tileKey))
            return true;
        if (string.IsNullOrWhiteSpace(entry.TemporaryItemSheetPath) ||
            entry.TemporaryItemSheetColumns <= 0 || entry.TemporaryItemSheetRows <= 0)
            return false;

        string sheetPath = ProjectManager?.ResolveProjectPath(entry.TemporaryItemSheetPath) ?? entry.TemporaryItemSheetPath;
        string sheetKey = !string.IsNullOrWhiteSpace(entry.TemporaryItemSheetCacheKey)
            ? entry.TemporaryItemSheetCacheKey
            : ItemsAtlas.BuildTemporaryItemSheetKey(sheetPath, entry.TemporaryItemSheetColumns, entry.TemporaryItemSheetRows);

        if (!ItemsAtlas.TryRegisterTemporaryItemSheet(sheetKey, sheetPath, entry.TemporaryItemSheetColumns, entry.TemporaryItemSheetRows))
            return false;

        return ItemsAtlas.TryRegisterTemporaryItemTile(sheetKey, tileKey,
            entry.TemporaryItemSheetColumnIndex, entry.TemporaryItemSheetRowIndex);
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
            RequestClose();
        }
    }

    private void SpawnScenery(string objectName)
    {
        if (objectName != SceneryLoadLabel) return;
        OpenSchematicFileDialog();
    }

    /// <summary>
    /// Loads a schematic file in response to a direct user action (the file-open
    /// dialog, or double-clicking it in the content browser). Unlike
    /// <see cref="SpawnSchematicFromPath"/>, if the file contains invalid/corrupt
    /// data and loading fails, this shows a Retry/Cancel dialog asking the user
    /// whether they want to try loading it again, instead of silently giving up.
    /// </summary>
    public SceneObject? SpawnSchematicFromPathInteractive(string filePath, string resourcePackId = "")
    {
        var root = SpawnSchematicFromPath(filePath, resourcePackId);
        if (root == null)
        {
            _pendingSchematicRetryPath = filePath;
            _pendingSchematicRetryResourcePackId = resourcePackId;
            _pendingSchematicRetryError = _lastSchematicLoadError ?? "The file could not be loaded.";
            SchematicRetryPromptChanged?.Invoke();
        }
        return root;
    }

    // ── Schematic retry prompt API ──────────────────────────────────────────

    /// <summary>Raised when a schematic load failure prompt should be shown or hidden.</summary>
    public event Action? SchematicRetryPromptChanged;

    /// <summary>True while a failed schematic load is awaiting a Retry/Cancel decision.</summary>
    public bool SchematicRetryPending => _pendingSchematicRetryPath != null;

    /// <summary>File name of the schematic that failed to load, or null.</summary>
    public string? SchematicRetryFileName =>
        _pendingSchematicRetryPath != null ? Path.GetFileName(_pendingSchematicRetryPath) : null;

    /// <summary>Error description for the pending schematic load failure, or null.</summary>
    public string? SchematicRetryError => _pendingSchematicRetryError;

    /// <summary>
    /// Retries the pending schematic load. Returns true on success (prompt cleared,
    /// menu close requested); false when it failed again (prompt stays, error updated).
    /// </summary>
    public bool RetrySchematicLoad()
    {
        if (_pendingSchematicRetryPath == null)
            return false;

        var root = SpawnSchematicFromPath(_pendingSchematicRetryPath, _pendingSchematicRetryResourcePackId);
        if (root == null)
        {
            _pendingSchematicRetryError = _lastSchematicLoadError ?? "The file could not be loaded.";
            SchematicRetryPromptChanged?.Invoke();
            return false;
        }

        _pendingSchematicRetryPath = null;
        _pendingSchematicRetryError = null;
        SchematicRetryPromptChanged?.Invoke();
        RequestClose();
        return true;
    }

    /// <summary>Dismisses the pending schematic retry prompt.</summary>
    public void CancelSchematicRetry()
    {
        if (_pendingSchematicRetryPath == null)
            return;

        Console.Error.WriteLine($"Gave up loading schematic: {_pendingSchematicRetryPath}");
        _pendingSchematicRetryPath = null;
        _pendingSchematicRetryError = null;
        SchematicRetryPromptChanged?.Invoke();
    }

    /// <summary>
    /// Loads legacy <c>.schematic</c> and Sponge/WorldEdit <c>.schem</c> files and
    /// spawns them as a merged scenery object.
    /// </summary>
    public SceneObject? SpawnSchematicFromPath(string filePath, string resourcePackId = "")
    {
        if (Viewport == null) return null;

        _lastSchematicLoadError = null;

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
                _lastSchematicLoadError = $"The NBT data could not be read: {ex.Message}";
                Console.Error.WriteLine($"Failed reading NBT schematic '{filePath}': {ex.Message}");
                return null;
            }

            var rootTag = doc.DocumentRoot;
            if (rootTag == null)
            {
                _lastSchematicLoadError = "The file has no NBT root tag.";
                return null;
            }

            var schematic = rootTag.GetCompound("Schematic") ?? rootTag;

            int width = GetDimension(schematic, "Width");
            int height = GetDimension(schematic, "Height");
            int length = GetDimension(schematic, "Length");

            if (width <= 0 || height <= 0 || length <= 0)
            {
                _lastSchematicLoadError = "The schematic dimensions are invalid.";
                Console.Error.WriteLine($"Invalid schematic dimensions in '{filePath}'.");
                return null;
            }

            int total = width * height * length;
            var variantCache = new Dictionary<string, VariantRenderInfo>(StringComparer.OrdinalIgnoreCase);
            var voxelInfos = new VariantRenderInfo?[total];
            // Minecraft fluid "level" state (0-7 = flowing amount, 8-15 = falling) per voxel.
            // Only meaningful when the voxel's VariantRenderInfo.BlockName is "water"/"lava";
            // populated by both loaders below. Kept out of VariantRenderInfo/variantCache since
            // fluid geometry depends on neighbouring voxels, not just the block's own variant,
            // so it can't be precomputed/shared the way cube-face geometry is.
            var liquidLevels = new int[total];
            var availableBlocks = new HashSet<string>(BlockRegistry.Blocks, StringComparer.OrdinalIgnoreCase);

            bool modernLoaded = TryLoadModernPaletteBlocks(
                schematic,
                total,
                availableBlocks,
                variantCache,
                voxelInfos,
                liquidLevels);

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
                        voxelInfos,
                        liquidLevels))
                {
                    _lastSchematicLoadError = "The block data could not be decoded in either the modern (.schem) or legacy (.schematic) format.";
                    return null;
                }
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

            var merged = new Dictionary<MeshBatchKey, MeshAccumulator>();
            // Texture -> TerrainAtlas animation key, populated by EmitLiquidVoxel whenever it
            // resolves an animated fluid texture (e.g. "water_still"), so the merged mesh for that
            // texture can be marked animated once assembly finishes below.
            var liquidAnimKeys = new Dictionary<Texture, string>();
            // Texture -> tint colour, populated by EmitLiquidVoxel for fluid textures that
            // need a biome-independent default tint (currently just water) since the merged
            // mesh assembly below has no other per-block way to apply Mesh.Albedo.
            var liquidTintColors = new Dictionary<Texture, Vector3>();
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
                                var acc = GetOrCreateAccumulator(merged, template.Texture, largePlacement.Info.AutoEmissionLevel);
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

                        // Fluids get their own neighbour-aware, height-blended mesh instead of a
                        // plain cube: Minecraft water/lava blocks carry a "level" state (0-7 flowing
                        // amounts, 8-15 falling) and slope their top surface toward lower neighbours,
                        // so a fixed full-height cube is only correct for an isolated source block.
                        if (string.Equals(info.BlockName, "water", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(info.BlockName, "lava", StringComparison.OrdinalIgnoreCase))
                        {
                            EmitLiquidVoxel(
                                merged,
                                liquidAnimKeys,
                                liquidTintColors,
                                voxelInfos,
                                liquidLevels,
                                width,
                                height,
                                length,
                                x,
                                y,
                                z,
                                px,
                                py,
                                pz,
                                info.BlockName,
                                liquidLevels[index],
                                info.AutoEmissionLevel,
                                normalizedResourcePackId);
                            continue;
                        }

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
                                pz,
                                info.AutoEmissionLevel);
                            continue;
                        }

                        foreach (var template in info.Templates)
                        {
                            var acc = GetOrCreateAccumulator(merged, template.Texture, info.AutoEmissionLevel);
                            AppendTemplate(acc, template, px, py, pz);
                        }
                    }
                }
            }

            if (placed == 0 || merged.Count == 0)
            {
                _lastSchematicLoadError = "The schematic had no spawnable blocks.";
                Console.Error.WriteLine($"Schematic had no spawnable blocks: {filePath}");
                return null;
            }

            // Reverse lookup so every merged mesh (not just liquids) can be classified as
            // alpha-blend vs cutout/opaque from its texture alone — see Mesh.IsTranslucent.
            // Built once here rather than per-mesh since TerrainAtlas.Textures has no
            // texture -> key index of its own.
            var texToKey = new Dictionary<Texture, string>();
            foreach (var kv in TerrainAtlas.Textures)
                texToKey[kv.Value] = kv.Key;

            foreach (var kv in merged)
            {
                var acc = kv.Value;
                if (acc.Vertices.Count == 0) continue;

                Texture? texture = kv.Key.Texture;
                byte autoEmissionLevel = kv.Key.AutoEmissionLevel;

                var mesh = new VeldridMesh(VeldridContext.Device)
                {
                    AlbedoTexture = texture,
                    AnimationKey = texture != null && liquidAnimKeys.TryGetValue(texture, out string? animKey) ? animKey : "",
                    IsTranslucent = texture != null && texToKey.TryGetValue(texture, out string? texKey) &&
                                    TerrainAtlas.IsTextureTranslucent(texKey)
                };
                if (texture != null && liquidTintColors.TryGetValue(texture, out Vector3 tint))
                    mesh.Albedo = tint;
                mesh.AutoEmissionLevel = autoEmissionLevel;
                mesh.Vertices.AddRange(acc.Vertices.Select(v => new Vector3(v.x, v.y, v.z)));
                mesh.Normals.AddRange(acc.Normals.Select(v => new Vector3(v.x, v.y, v.z)));
                mesh.TexCoords.AddRange(acc.TexCoords.Select(v => new Vector2(v.x, v.y)));
                mesh.Upload(VeldridContext.StandardOutputDescription);
                root.AddMesh(mesh);
            }

            root.ApplyMaterialSettingsToMeshes();

            if (root.Visuals.Count == 0)
            {
                _lastSchematicLoadError = "The schematic produced no renderable geometry.";
                Console.Error.WriteLine($"Schematic produced no renderable geometry: {filePath}");
                return null;
            }

            AddToScene(root);
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

    private readonly struct MeshBatchKey : IEquatable<MeshBatchKey>
    {
        public readonly Texture? Texture;
        public readonly byte AutoEmissionLevel;

        public MeshBatchKey(Texture? texture, byte autoEmissionLevel)
        {
            Texture = texture;
            AutoEmissionLevel = autoEmissionLevel;
        }

        public bool Equals(MeshBatchKey other) =>
            ReferenceEquals(Texture, other.Texture) && AutoEmissionLevel == other.AutoEmissionLevel;

        public override bool Equals(object? obj) => obj is MeshBatchKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Texture, AutoEmissionLevel);
    }

    private sealed class MeshTemplate
    {
        public Texture? Texture;
        public vec3[] Vertices = Array.Empty<vec3>();
        public vec3[] Normals = Array.Empty<vec3>();
        public vec2[] TexCoords = Array.Empty<vec2>();
    }

    private sealed class CubeFaceInfo
    {
        public required Texture Texture;
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
        public byte AutoEmissionLevel;

        /// <summary>
        /// Whether this block should hide the touching face of an adjacent block
        /// (i.e. count as an occluder in <see cref="EmitCubeFacesWithCulling"/>'s
        /// and <see cref="EmitLiquidVoxel"/>'s neighbour checks).
        /// Distinct from <see cref="IsCullableCube"/>: that flag only says the
        /// block's *own* geometry is a plain full cube eligible for the fast
        /// per-face-culling path — it says nothing about whether the block is
        /// actually opaque. Glass, leaves, ice and liquids are all full cubes
        /// (<c>IsCullableCube == true</c>) but must never occlude a neighbour,
        /// since you can see through/past them to the neighbour's face.
        /// Defaults to false so non-cube render paths (fences, templates, etc.)
        /// never accidentally occlude anything.
        /// </summary>
        public bool IsOpaque;
    }

    /// <summary>
    /// Vanilla-block name patterns that are geometrically a full cube (so
    /// <see cref="TryBuildCullableCubeFaces"/> succeeds and <c>IsCullableCube</c>
    /// is true) but are not actually opaque, and therefore must not occlude a
    /// neighbouring block's face. Matched case-insensitively as a substring so
    /// coloured/variant names (stained_glass, azalea_leaves, packed_ice, …) are
    /// all covered by one entry. Over-including here only costs a few extra
    /// triangles on the neighbour; under-including causes missing faces, so this
    /// list errs on the side of including anything remotely translucent.
    /// </summary>
    private static readonly string[] NonOccludingBlockNamePatterns =
    {
        "glass", "leaves", "ice",
    };

    private static readonly Dictionary<string, byte> AutoEmissiveBlockLightLevels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["lava"] = 15,
            ["glowstone"] = 15,
            ["sea_lantern"] = 15,
            ["beacon"] = 15,
            ["jack_o_lantern"] = 15,
            ["shroomlight"] = 15,
            ["campfire"] = 15,
            ["soul_campfire"] = 10,
            ["lantern"] = 15,
            ["soul_lantern"] = 10,
            ["redstone_torch"] = 7,
            ["torch"] = 14,
            ["soul_torch"] = 10,
            ["end_rod"] = 14,
            ["respawn_anchor"] = 15,
            ["furnace"] = 13,
            ["blast_furnace"] = 13,
            ["smoker"] = 13,
            ["crying_obsidian"] = 10,
            ["magma_block"] = 3,
            ["redstone_ore"] = 9,
            ["deepslate_redstone_ore"] = 9,
            ["ochre_froglight"] = 15,
            ["pearlescent_froglight"] = 15,
            ["verdant_froglight"] = 15,
            ["glow_lichen"] = 7,
            ["light"] = 15,
            ["fire"] = 15,
            ["soul_fire"] = 10,
            ["nether_portal"] = 11,
            ["end_portal"] = 15,
            ["end_gateway"] = 15,
            ["sculk_catalyst"] = 6,
            ["sculk_sensor"] = 1,
            ["calibrated_sculk_sensor"] = 1,
            ["sculk_shrieker"] = 2,
            ["copper_bulb"] = 15,
            ["lightning_rod"] = 14
        };

    private static byte GetFallbackAutoEmissionLevel(string blockName)
    {
        if (AutoEmissiveBlockLightLevels.TryGetValue(blockName, out byte level))
            return level;

        if (blockName.EndsWith("_torch", StringComparison.OrdinalIgnoreCase))
            return 14;
        if (blockName.EndsWith("_lantern", StringComparison.OrdinalIgnoreCase))
            return 15;
        if (blockName.Contains("portal", StringComparison.OrdinalIgnoreCase))
            return 11;
        if (blockName.Contains("candle", StringComparison.OrdinalIgnoreCase))
            return 12;

        return 0;
    }

    private static byte GetModelEmissionLevel(ResolvedBlockModel? model)
    {
        if (model == null)
            return 0;

        int maxLevel = 0;
        foreach (var element in model.Elements)
        {
            if (element.LightEmission >= 0)
                maxLevel = Math.Max(maxLevel, Math.Clamp(element.LightEmission, 0, 15));

            foreach (var face in element.Faces.Values)
            {
                if (face.LightEmission >= 0)
                    maxLevel = Math.Max(maxLevel, Math.Clamp(face.LightEmission, 0, 15));
            }
        }

        return (byte)Math.Clamp(maxLevel, 0, 15);
    }

    private byte ComputeAutoEmissionLevel(string blockName, BlockVariantEntry variant)
    {
        byte maxLevel = GetFallbackAutoEmissionLevel(blockName);

        if (!string.IsNullOrEmpty(variant.ModelPath))
        {
            var resolved = BlockRegistry.ResolveModel(variant.ModelPath);
            maxLevel = Math.Max(maxLevel, GetModelEmissionLevel(resolved));
        }

        if (variant.TopHalf != null && !string.IsNullOrEmpty(variant.TopHalf.ModelPath))
        {
            var topResolved = BlockRegistry.ResolveModel(variant.TopHalf.ModelPath);
            maxLevel = Math.Max(maxLevel, GetModelEmissionLevel(topResolved));
        }

        return maxLevel;
    }

    private static bool IsNonOccludingBlock(string blockName)
    {
        foreach (string pattern in NonOccludingBlockNamePatterns)
        {
            if (blockName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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
        VariantRenderInfo?[] outVoxels,
        int[] outLiquidLevels)
    {
        var palette = schematic.GetCompound("Palette");
        byte[] blockData = schematic.GetByteArrayValue("BlockData", Array.Empty<byte>());
        if (palette == null || palette.Count == 0 || blockData.Length == 0)
            return false;

        if (!TryDecodeVarIntArray(blockData, total, out var paletteIndices))
        {
            Console.Error.WriteLine("Failed to decode BlockData varints from .schem file.");
            return false;
        }

        var paletteLookup = new Dictionary<int, string>();
        foreach (Tag tag in palette.Value)
        {
            if (tag is TagInt ti)
                paletteLookup[ti.Value] = ti.Name;
        }

        var idToInfo = new Dictionary<int, VariantRenderInfo?>();
        var idToLevel = new Dictionary<int, int>();
        for (int i = 0; i < total; i++)
        {
            int paletteId = paletteIndices[i];
            if (!idToInfo.TryGetValue(paletteId, out var info))
            {
                if (!paletteLookup.TryGetValue(paletteId, out string? stateText) || string.IsNullOrEmpty(stateText) ||
                    !TryParsePaletteState(stateText, out var blockName, out var props) ||
                    string.Equals(blockName, "air", StringComparison.OrdinalIgnoreCase) ||
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

                // Fluid "level" state (0-7 flowing amount, 8-15 falling) — carried separately
                // from the variant since our water/lava blockstate has no per-level variants and
                // fluid geometry is generated fresh per-voxel from neighbour data (see EmitLiquidVoxel).
                if ((string.Equals(blockName, "water", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(blockName, "lava", StringComparison.OrdinalIgnoreCase)) &&
                    props.TryGetValue("level", out string? levelStr) &&
                    int.TryParse(levelStr, out int parsedLevel))
                {
                    idToLevel[paletteId] = Math.Clamp(parsedLevel, 0, 15);
                }
            }

            outVoxels[i] = info;
            if (idToLevel.TryGetValue(paletteId, out int level))
                outLiquidLevels[i] = level;
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
        VariantRenderInfo?[] outVoxels,
        int[] outLiquidLevels)
    {
        byte[] blocks = schematic.GetByteArrayValue("Blocks", Array.Empty<byte>());
        byte[] data = schematic.GetByteArrayValue("Data", Array.Empty<byte>());
        byte[] addBlocks = schematic.GetByteArrayValue("AddBlocks", Array.Empty<byte>());

        if (blocks.Length < total)
        {
            Console.Error.WriteLine($"Schematic block array is truncated ({blocks.Length} < {total}).");
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

            // Legacy fluid metadata is the "level" state directly (0-7 flowing amount,
            // 8-15 falling) for both the flowing (8/10) and stationary/source (9/11) block IDs.
            if (blockId == 8 || blockId == 9 || blockId == 10 || blockId == 11)
                outLiquidLevels[i] = blockData;

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
                if (!TryResolveLegacyBlock(blockId, blockData, out var fenceBlockName, out _) || !availableBlocks.Contains(fenceBlockName))
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
                if (!TryResolveLegacyBlock(blockId, blockData, out var blockName, out var variantHint) ||
                    string.Equals(blockName, "air", StringComparison.OrdinalIgnoreCase) ||
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

        byte emissionLevel = ComputeAutoEmissionLevel(blockName, post);
        foreach (var part in parts)
            emissionLevel = Math.Max(emissionLevel, ComputeAutoEmissionLevel(blockName, part));
        info.AutoEmissionLevel = emissionLevel;

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

        info.AutoEmissionLevel = ComputeAutoEmissionLevel(blockName, variant);

        info.CubeFaces = TryBuildCullableCubeFaces(variant);
        info.IsCullableCube = info.CubeFaces != null;
        // Water/lava are always full cubes geometrically but are handled entirely by
        // EmitLiquidVoxel and must never occlude a neighbour's face (see IsOpaque doc).
        info.IsOpaque = info.IsCullableCube &&
                        !string.Equals(blockName, "water", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(blockName, "lava", StringComparison.OrdinalIgnoreCase) &&
                        !IsNonOccludingBlock(blockName);

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
            if (!TerrainAtlas.Textures.TryGetValue(resolvedTexKey, out Texture? texId) || texId == null) return null;

            var uv = GetFaceUv(faceName, face.Uv, face.Rotation);
            return new CubeFaceInfo { Texture = texId, Uv = uv };
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
                Texture = mesh.AlbedoTexture,
                Vertices = mesh.Vertices.Select(v => new vec3(v.X, v.Y, v.Z)).ToArray(),
                Normals = mesh.Normals.Select(v => new vec3(v.X, v.Y, v.Z)).ToArray(),
                TexCoords = mesh.TexCoords.Select(v => new vec2(v.X, v.Y)).ToArray()
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

    private List<VeldridMesh> BuildVariantMeshes(BlockVariantEntry variant)
    {
        var meshes = new List<VeldridMesh>();

        AppendBlockMeshesForPreview(meshes, variant);
        if (variant.TopHalf != null)
        {
            var top = new List<VeldridMesh>();
            AppendBlockMeshesForPreview(top, variant.TopHalf);
            var shift = new Vector3(variant.PartOffsetX, variant.PartOffsetY, variant.PartOffsetZ);
            foreach (var m in top)
            {
                for (int i = 0; i < m.Vertices.Count; i++)
                    m.Vertices[i] += shift;
                m.Upload(VeldridContext.StandardOutputDescription);
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

    private static MeshAccumulator GetOrCreateAccumulator(
        Dictionary<MeshBatchKey, MeshAccumulator> merged,
        Texture? texture,
        byte autoEmissionLevel)
    {
        var key = new MeshBatchKey(texture, autoEmissionLevel);
        if (!merged.TryGetValue(key, out var acc))
        {
            acc = new MeshAccumulator();
            merged[key] = acc;
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
        Dictionary<MeshBatchKey, MeshAccumulator> merged,
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
        float pz,
        byte autoEmissionLevel)
    {
        bool IsOccluded(int nx, int ny, int nz)
        {
            if (nx < 0 || ny < 0 || nz < 0 || nx >= width || ny >= height || nz >= length)
                return false;

            int nIndex = ny * width * length + nz * width + nx;
            var n = voxels[nIndex];
            // IsOpaque (not IsCullableCube): glass/leaves/ice/liquids are full cubes
            // geometrically but must never hide a neighbour's face — see VariantRenderInfo.IsOpaque.
            return n != null && n.IsOpaque;
        }

        if (!IsOccluded(x, y + 1, z) && faces.Up != null)
            EmitFace(merged, faces.Up, px, py, pz, "up", autoEmissionLevel);
        if (!IsOccluded(x, y - 1, z) && faces.Down != null)
            EmitFace(merged, faces.Down, px, py, pz, "down", autoEmissionLevel);
        if (!IsOccluded(x, y, z - 1) && faces.North != null)
            EmitFace(merged, faces.North, px, py, pz, "north", autoEmissionLevel);
        if (!IsOccluded(x, y, z + 1) && faces.South != null)
            EmitFace(merged, faces.South, px, py, pz, "south", autoEmissionLevel);
        if (!IsOccluded(x - 1, y, z) && faces.West != null)
            EmitFace(merged, faces.West, px, py, pz, "west", autoEmissionLevel);
        if (!IsOccluded(x + 1, y, z) && faces.East != null)
            EmitFace(merged, faces.East, px, py, pz, "east", autoEmissionLevel);
    }

    private static void EmitFace(
        Dictionary<MeshBatchKey, MeshAccumulator> merged,
        CubeFaceInfo face,
        float px,
        float py,
        float pz,
        string faceName,
        byte autoEmissionLevel)
    {
        var acc = GetOrCreateAccumulator(merged, face.Texture, autoEmissionLevel);

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

    // ── Liquid (water/lava) mesh generation ─────────────────────────────────────
    //
    // Vanilla fluid blocks carry a "level" state — 0-7 for flowing amount (0 = a
    // full/source-height block, 7 = the thinnest visible trickle) and 8-15 for
    // "falling" (waterfalls/lavafalls, always rendered as a full-height cube).
    // Neighbouring fluid blocks of a lower level pull this block's *corners*
    // (not just a single flat top) down to blend smoothly between them, which is
    // why a fixed full cube looks wrong once more than one fluid block is placed
    // next to another — this mirrors Mine-imator's `block_generate_liquid` script.
    //
    // Corner order used throughout (looking down the +Y axis, our world is X/Z
    // horizontal, Y up): 0=(x-,z-) 1=(x+,z-) 2=(x+,z+) 3=(x-,z+).

    /// <summary>Vanilla's fluid height curve: source (level 0) caps at 14/16, the
    /// lowest flowing level (7) is almost flat, and any "falling" level (8-15) is
    /// always a full-height block.</summary>
    private static float LiquidLevelHeight(int level) =>
        level >= 8 ? 1f : (14f - (level / 7f) * 13.5f) / 16f;

    /// <summary>
    /// Appends a triangle to <paramref name="acc"/>, automatically reordering
    /// (a,b,c) so the winding faces <paramref name="expectedDir"/> and storing the
    /// triangle's own (possibly sloped) geometric normal rather than a fixed one,
    /// so sloped liquid surfaces still shade correctly.
    /// </summary>
    private static void AddLiquidTri(
        MeshAccumulator acc,
        vec3 a, vec3 b, vec3 c,
        vec3 expectedDir,
        vec2 ua, vec2 ub, vec2 uc)
    {
        vec3 cross = vec3.Cross(b - a, c - a);
        if (vec3.Dot(cross, expectedDir) < 0f)
        {
            (b, c) = (c, b);
            (ub, uc) = (uc, ub);
            cross = vec3.Cross(b - a, c - a);
        }

        vec3 normal = cross.LengthSqr > 1e-12f ? cross.Normalized : expectedDir.Normalized;

        acc.Vertices.Add(a); acc.Normals.Add(normal); acc.TexCoords.Add(ua);
        acc.Vertices.Add(b); acc.Normals.Add(normal); acc.TexCoords.Add(ub);
        acc.Vertices.Add(c); acc.Normals.Add(normal); acc.TexCoords.Add(uc);
    }

    /// <summary>
    /// Emits one side face of a liquid voxel between two adjacent top corners at
    /// (<paramref name="xA"/>,<paramref name="zA"/>) and (<paramref name="xB"/>,<paramref name="zB"/>),
    /// whose heights (0-1 fraction of a block) may differ. The shared, lower
    /// portion (up to the shorter corner) is a plain quad; any extra height on the
    /// taller corner is filled with a triangle, matching how Minecraft crops the
    /// flow texture from the bottom up rather than stretching it.
    /// </summary>
    private static void EmitLiquidSideFace(
        MeshAccumulator acc,
        vec3 normal,
        float baseY,
        float xA, float zA, float heightA, float uA,
        float xB, float zB, float heightB, float uB)
    {
        float minH = MathF.Min(heightA, heightB);
        var topA = new vec3(xA, baseY + minH, zA);
        var topB = new vec3(xB, baseY + minH, zB);
        var botA = new vec3(xA, baseY, zA);
        var botB = new vec3(xB, baseY, zB);

        float vTop = 1f - minH;
        const float vBot = 1f;

        AddLiquidTri(acc, topA, topB, botB, normal, new vec2(uA, vTop), new vec2(uB, vTop), new vec2(uB, vBot));
        AddLiquidTri(acc, topA, botB, botA, normal, new vec2(uA, vTop), new vec2(uB, vBot), new vec2(uA, vBot));

        if (MathF.Abs(heightA - heightB) > 1e-4f)
        {
            if (heightA > heightB)
            {
                var extra = new vec3(xA, baseY + heightA, zA);
                AddLiquidTri(acc, topA, topB, extra, normal,
                    new vec2(uA, vTop), new vec2(uB, vTop), new vec2(uA, 1f - heightA));
            }
            else
            {
                var extra = new vec3(xB, baseY + heightB, zB);
                AddLiquidTri(acc, topA, topB, extra, normal,
                    new vec2(uA, vTop), new vec2(uB, vTop), new vec2(uB, 1f - heightB));
            }
        }
    }

    /// <summary>
    /// Builds a neighbour-aware liquid mesh (sloped/blended top surface, cropped
    /// flow-textured sides, falling-column full cubes) for one water/lava voxel
    /// during schematic import, appending geometry directly into <paramref name="merged"/>.
    /// Face culling matches same-fluid neighbours (no internal faces between two
    /// touching water blocks) and fully opaque neighbours (no faces buried inside
    /// solid terrain).
    /// </summary>
    private static void EmitLiquidVoxel(
        Dictionary<MeshBatchKey, MeshAccumulator> merged,
        Dictionary<Texture, string> animKeysOut,
        Dictionary<Texture, Vector3> tintColorsOut,
        VariantRenderInfo?[] voxels,
        int[] liquidLevels,
        int width, int height, int length,
        int x, int y, int z,
        float px, float py, float pz,
        string blockName,
        int level,
        byte autoEmissionLevel,
        string resourcePackId)
    {
        int GetIndex(int nx, int ny, int nz)
        {
            if (nx < 0 || ny < 0 || nz < 0 || nx >= width || ny >= height || nz >= length)
                return -1;
            return ny * width * length + nz * width + nx;
        }

        bool SameFluid(int nx, int ny, int nz)
        {
            int idx = GetIndex(nx, ny, nz);
            if (idx < 0) return false;
            var n = voxels[idx];
            return n != null && string.Equals(n.BlockName, blockName, StringComparison.OrdinalIgnoreCase);
        }

        int NeighborLevel(int nx, int ny, int nz)
        {
            int idx = GetIndex(nx, ny, nz);
            return idx < 0 ? 0 : liquidLevels[idx];
        }

        bool IsSolid(int nx, int ny, int nz)
        {
            int idx = GetIndex(nx, ny, nz);
            if (idx < 0) return false;
            var n = voxels[idx];
            return n != null && n.IsOpaque;
        }

        bool matchXp = SameFluid(x + 1, y, z);
        bool matchXn = SameFluid(x - 1, y, z);
        bool matchZp = SameFluid(x, y, z + 1);
        bool matchZn = SameFluid(x, y, z - 1);
        bool matchUp = SameFluid(x, y + 1, z);
        bool matchDown = SameFluid(x, y - 1, z);

        bool solidXp = IsSolid(x + 1, y, z);
        bool solidXn = IsSolid(x - 1, y, z);
        bool solidZp = IsSolid(x, y, z + 1);
        bool solidZn = IsSolid(x, y, z - 1);
        bool solidDown = IsSolid(x, y - 1, z);

        // Fully enclosed by the same fluid / solid terrain on every side — nothing visible.
        if ((matchXp || solidXp) && (matchXn || solidXn) &&
            (matchZp || solidZp) && (matchZn || solidZn) &&
            matchUp && (matchDown || solidDown))
            return;

        string stillKey = ResolveTerrainTextureKeyForPack(blockName + "_still", resourcePackId);
        string flowKey = ResolveTerrainTextureKeyForPack(blockName + "_flow", resourcePackId);

        if (!TerrainAtlas.Textures.TryGetValue(stillKey, out Texture? stillTex) || stillTex == null)
            return;
        if (!TerrainAtlas.Textures.TryGetValue(flowKey, out Texture? flowTex) || flowTex == null)
            flowTex = stillTex;

        if (TerrainAtlas.AnimatedTextures.ContainsKey(stillKey)) animKeysOut[stillTex] = stillKey;
        if (TerrainAtlas.AnimatedTextures.ContainsKey(flowKey)) animKeysOut[flowTex] = flowKey;

        // Water's real texture is a light desaturated grey vanilla tints blue at render
        // time; apply the same biome-independent default used for spawn-menu-placed water
        // (see MinecraftModelMesh.TryGetDefaultBlockTint) since this schematic path builds
        // its own merged Mesh instances instead of going through MinecraftModelMesh.Build.
        if (MinecraftModelMesh.TryGetDefaultBlockTint(blockName, out Vector3 tint))
        {
            tintColorsOut[stillTex] = tint;
            tintColorsOut[flowTex] = tint;
        }

        var stillAcc = GetOrCreateAccumulator(merged, stillTex, autoEmissionLevel);
        var flowAcc = GetOrCreateAccumulator(merged, flowTex, autoEmissionLevel);

        bool falling = level >= 8;
        float myHeight = LiquidLevelHeight(level);

        float corner0z, corner1z, corner2z, corner3z;

        // A block with fluid directly above it never shows its own top surface, so its
        // exact height doesn't matter visually — treat it (and falling columns) as full
        // height, matching Mine-imator's "fix wave gaps" handling.
        if (falling || matchUp)
        {
            corner0z = corner1z = corner2z = corner3z = 1f;
        }
        else
        {
            int SideOrCornerLevel(int nx, int nz)
            {
                if (!SameFluid(nx, y, nz)) return level;
                if (SameFluid(nx, y + 1, nz)) return 8; // stacked fluid above -> treat as full
                return NeighborLevel(nx, y, nz);
            }

            float sideXp = LiquidLevelHeight(SideOrCornerLevel(x + 1, z));
            float sideXn = LiquidLevelHeight(SideOrCornerLevel(x - 1, z));
            float sideZp = LiquidLevelHeight(SideOrCornerLevel(x, z + 1));
            float sideZn = LiquidLevelHeight(SideOrCornerLevel(x, z - 1));

            float c0 = LiquidLevelHeight(SideOrCornerLevel(x - 1, z - 1));
            float c1 = LiquidLevelHeight(SideOrCornerLevel(x + 1, z - 1));
            float c2 = LiquidLevelHeight(SideOrCornerLevel(x + 1, z + 1));
            float c3 = LiquidLevelHeight(SideOrCornerLevel(x - 1, z + 1));

            corner0z = MathF.Max(myHeight, MathF.Max(c0, MathF.Max(sideXn, sideZn)));
            corner1z = MathF.Max(myHeight, MathF.Max(c1, MathF.Max(sideXp, sideZn)));
            corner2z = MathF.Max(myHeight, MathF.Max(c2, MathF.Max(sideXp, sideZp)));
            corner3z = MathF.Max(myHeight, MathF.Max(c3, MathF.Max(sideXn, sideZp)));
        }

        float baseY = py - 0.5f;
        float xn = px - 0.5f, xp = px + 0.5f;
        float zn = pz - 0.5f, zp = pz + 0.5f;

        // ── Top face: a 4-triangle fan (not a flat quad) so each corner can sit at
        //    its own blended height. ──────────────────────────────────────────────
        if (!matchUp)
        {
            float avgZ = (corner0z + corner1z + corner2z + corner3z) * 0.25f;
            var mid = new vec3(px, baseY + avgZ, pz);
            var t0 = new vec3(xn, baseY + corner0z, zn);
            var t1 = new vec3(xp, baseY + corner1z, zn);
            var t2 = new vec3(xp, baseY + corner2z, zp);
            var t3 = new vec3(xn, baseY + corner3z, zp);
            var uMid = new vec2(0.5f, 0.5f);
            var u0 = new vec2(0f, 0f);
            var u1 = new vec2(1f, 0f);
            var u2 = new vec2(1f, 1f);
            var u3 = new vec2(0f, 1f);

            vec3 up = new vec3(0f, 1f, 0f);
            AddLiquidTri(stillAcc, mid, t0, t1, up, uMid, u0, u1);
            AddLiquidTri(stillAcc, mid, t1, t2, up, uMid, u1, u2);
            AddLiquidTri(stillAcc, mid, t2, t3, up, uMid, u2, u3);
            AddLiquidTri(stillAcc, mid, t3, t0, up, uMid, u3, u0);
        }

        // ── Bottom face (flat; fluids always touch the block's floor). ───────────
        if (!matchDown && !solidDown)
        {
            var b0 = new vec3(xn, baseY, zn);
            var b1 = new vec3(xp, baseY, zn);
            var b2 = new vec3(xp, baseY, zp);
            var b3 = new vec3(xn, baseY, zp);
            vec3 down = new vec3(0f, -1f, 0f);
            AddLiquidTri(stillAcc, b0, b2, b1, down, new vec2(0, 0), new vec2(1, 1), new vec2(1, 0));
            AddLiquidTri(stillAcc, b0, b3, b2, down, new vec2(0, 0), new vec2(0, 1), new vec2(1, 1));
        }

        // ── Side faces, cropped from the flow texture's bottom edge upward. ──────
        if (!matchXp && !solidXp)
            EmitLiquidSideFace(flowAcc, new vec3(1f, 0f, 0f), baseY, xp, zn, corner1z, 0f, xp, zp, corner2z, 1f);
        if (!matchXn && !solidXn)
            EmitLiquidSideFace(flowAcc, new vec3(-1f, 0f, 0f), baseY, xn, zp, corner3z, 0f, xn, zn, corner0z, 1f);
        if (!matchZn && !solidZn)
            EmitLiquidSideFace(flowAcc, new vec3(0f, 0f, -1f), baseY, xn, zn, corner0z, 0f, xp, zn, corner1z, 1f);
        if (!matchZp && !solidZp)
            EmitLiquidSideFace(flowAcc, new vec3(0f, 0f, 1f), baseY, xp, zp, corner2z, 0f, xn, zp, corner3z, 1f);
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
    /// Loads a texture file into the shared Veldrid texture registry and returns
    /// its id (0 on failure). Supports PNG, JPG, BMP, TGA, GIF, WebP, and TIFF.
    /// </summary>
    private static uint LoadPrimitiveTextureFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return 0;

        return MineImatorLoader.Instance.LoadTextureFromFile(filePath);
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
            Console.Error.WriteLine($"Failed to copy texture to project: {ex.Message}");
            return sourcePath;
        }
    }

    // ── Spawn logic ───────────────────────────────────────────────────────────

    /// <summary>Spawns whatever the current selection is (the "Spawn" button action).</summary>
    public void TrySpawn()
    {
        if (_selectedCategory == "Items")
        {
            TrySpawnItem();
            return;
        }

        if (_selectedCategory == "Particle Spawners")
        {
            TrySpawnParticleSpawner();
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
        RequestClose();
    }

    private void TrySpawnParticleSpawner()
    {
        if (_selectedObjectIndex < 0)
            return;

        string baseName = "Particle Spawner";
        int nextNum = GetNextAvailableObjectNumber(baseName);
        string fullName = nextNum > 1 ? $"{baseName}{nextNum}" : baseName;

        SpawnParticleSpawnerObject(fullName, _selectedParticleLibraryEntryId);
        RequestClose();
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
        RequestClose();
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
        RequestClose();
    }

    private void TrySpawnItem()
    {
        if (string.IsNullOrEmpty(_selectedTileKey)) return;
        SpawnItemObject(_selectedTileKey, _itemAtlasSource, _item3DMode);
        RequestClose();
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
                SpawnLightObject(fullName, objectName);
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
                if (_selectedCategory == "Primitives" && (objectName == "Plane" || objectName == "Cube"))
                {
                    string texturePath = "";
                    if (_selectedPrimitiveTextureId != 0 && !string.IsNullOrEmpty(_selectedPrimitiveTexturePath))
                    {
                        texturePath = CopyTextureToProject(_selectedPrimitiveTexturePath);
                    }
                    SpawnPrimitiveObject(objectName, fullName, _selectedPrimitiveTextureId, texturePath,
                                         _selectedPrimitivePlaneOrientation, _selectedPrimitiveCubeMapped);
                }
                else
                {
                    SpawnPrimitiveObject(objectName, fullName, 0, "", PlaneOrientation.XY, false,
                        _selectedPrimitiveSphereSmooth, _selectedPrimitiveSphereSegments, _selectedPrimitiveSphereRings);
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
        if (Viewport == null) return null;

        if (atlasSource == ItemAtlasSource.ItemAtlas)
            ItemsAtlas.EnsureProjectCustomTexturesLoaded();

        // Resolve texture and pixel data from the appropriate atlas
        Texture? tileTex = null;
        byte[]? tilePixels = null;
        int tileWidth;
        int tileHeight;

        if (atlasSource == ItemAtlasSource.ItemAtlas)
        {
            ItemsAtlas.Textures.TryGetValue(tileKey, out tileTex);
            ItemsAtlas.TilePixels.TryGetValue(tileKey, out tilePixels);
            ItemsAtlas.TryGetTileDimensions(tileKey, out tileWidth, out tileHeight);
        }
        else
        {
            TerrainAtlas.Textures.TryGetValue(tileKey, out tileTex);
            TerrainAtlas.TilePixels.TryGetValue(tileKey, out tilePixels);
            tileWidth = TerrainAtlas.TileSize;
            tileHeight = TerrainAtlas.TileSize;
        }

        if (tileTex == null || tilePixels == null) return null;

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
            ItemTileKey   = tileKey,
            ResourcePackId = GetSourceIdFromTextureKey(tileKey),
            Position      = vec3.Zero
        };
        obj.AssignObjectId();

        int tileSize = Math.Max(tileWidth, tileHeight);
        var mesh = new ExtrudedItemMesh(
            tileTex,
            tilePixels,
            is3D: is3D,
            tileSize: tileSize,
            tileWidth: tileWidth,
            tileHeight: tileHeight,
            extrudeDepth: 1f / 16f);

        obj.AddMesh(mesh);
        AddToScene(obj);
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

    public ParticleSpawnerSceneObject? SpawnParticleSpawnerObject(
        string objectName,
        string libraryEntryId,
        string? libraryDisplayName = null)
    {
        if (Viewport == null)
            return null;

        string displayName = libraryDisplayName ?? "";
        if (string.IsNullOrWhiteSpace(displayName))
        {
            var selected = GetParticleLibraryOptions().FirstOrDefault(x =>
                string.Equals(x.Id, libraryEntryId, StringComparison.OrdinalIgnoreCase));
            if (selected != null)
                displayName = selected.Name;
        }

        var obj = new ParticleSpawnerSceneObject
        {
            Name = objectName,
            ObjectType = "Particle Spawner",
            SpawnCategory = "Particle Spawners",
            Position = vec3.Zero,
            PivotOffset = vec3.Zero
        };
        obj.SetParticleSource(libraryEntryId, displayName);
        obj.AssignObjectId();

        var particlePickMesh = new CubeMesh
        {
            Alpha = 0f,
            Albedo = Vector3.Zero,
            PickOnly = true
        };
        obj.AddMesh(particlePickMesh);

        AddToScene(obj);
        return obj;
    }

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
        var cameraModelRoot = LoadEmbeddedCameraModel("Camera.glb");
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
                obj.InactiveVisuals.Add(mesh);
            }
        }

        // Load the active variant mesh and attach it as a separate visual set.
        // Only one set is visible at a time, controlled by obj.Active.
        int visualCountBeforeActive = obj.Visuals.Count;
        var activeModelRoot = LoadEmbeddedCameraModel("CameraActive.glb");
        if (activeModelRoot != null)
        {
            FlattenVisualsInto(activeModelRoot, obj);
            for (int i = visualCountBeforeActive; i < obj.Visuals.Count; i++)
            {
                var mesh = obj.Visuals[i];
                mesh.Unlit             = true;
                mesh.DepthTestDisabled = true;
                obj.ActiveVisuals.Add(mesh);
            }
        }

        // Apply the initial visibility state: a freshly spawned camera is
        // inactive, so show the inactive mesh set and hide the active one.
        obj.RefreshActiveMesh();

        // Add an invisible cube for object picking (same approach as lights).
        var cameraPickMesh = new CubeMesh
        {
            Alpha    = 0f,
            Albedo   = Vector3.Zero,
            PickOnly = true
        };
        obj.AddMesh(cameraPickMesh);

        AddToScene(obj);
        return obj;
    }

    /// <summary>
    /// Extracts an embedded <c>*.glb</c> camera model to a temporary file and
    /// loads it via <see cref="AssimpModelLoader"/>.  Returns null on failure.
    /// <paramref name="fileName"/> is the bare file name inside
    /// <c>MineImatorSimplyRemade.assets.mesh</c> (e.g. <c>Camera.glb</c>).
    /// </summary>
    private static SceneObject? LoadEmbeddedCameraModel(string fileName)
    {
        string resourceName = $"MineImatorSimplyRemade.assets.mesh.{fileName}";
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            Console.Error.WriteLine($"Embedded {fileName} not found.");
            return null;
        }

        // Write to a temp file so Assimp can load it.
        string tempName = $"MineImatorSimplyRemade_{Path.GetFileNameWithoutExtension(fileName)}.glb";
        string tempPath = Path.Combine(Path.GetTempPath(), tempName);
        using (var fs = File.Create(tempPath))
            stream.CopyTo(fs);

        return AssimpModelLoader.Load(tempPath);
    }

    public static void SaveCubeUvMapGuide()
    {
        const string embeddedName = "MineImatorSimplyRemade.assets.img.map.cube.png";
        const string defaultFileName = "cube-uv-map.png";

        string defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            defaultFileName);

        var saveResult = Dialog.FileSave("png", defaultPath);
        if (!saveResult.IsOk || string.IsNullOrWhiteSpace(saveResult.Path))
            return;

        string outputPath = saveResult.Path;
        if (!string.Equals(Path.GetExtension(outputPath), ".png", StringComparison.OrdinalIgnoreCase))
            outputPath = Path.ChangeExtension(outputPath, ".png");

        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using Stream? sourceStream = asm.GetManifestResourceStream(embeddedName);
            if (sourceStream == null)
            {
                Console.Error.WriteLine($"Cube UV map resource not found: {embeddedName}");
                return;
            }

            string? outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDir))
                Directory.CreateDirectory(outputDir);

            using var output = File.Create(outputPath);
            sourceStream.CopyTo(output);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to save cube UV map guide: {ex.Message}");
        }
    }

    /// <summary>
    /// Recursively collects all <see cref="Mesh"/> visuals from <paramref name="source"/>
    /// and its children and adds them to <paramref name="target"/>'s Visuals list.
    /// </summary>
    private static void FlattenVisualsInto(
        SceneObject source,
        SceneObject target)
    {
        foreach (var mesh in source.Visuals)
            target.AddMesh(mesh);

        foreach (var child in source.Children)
            FlattenVisualsInto(child, target);
    }

    /// <summary>Creates and registers a <see cref="LightSceneObject"/> in the viewport.</summary>
    public LightSceneObject? SpawnLightObject(string objectName, string lightKind = "Point Light")
    {
        if (Viewport == null) return null;

        var type = lightKind == "Spot Light" ? LightType.Spot : LightType.Point;

        var obj = new LightSceneObject
        {
            Name          = objectName,
            ObjectType    = lightKind,
            SpawnCategory = "Light",
            Type          = type,
            Position      = vec3.Zero,
            PivotOffset   = vec3.Zero,
        };
        obj.AssignObjectId();

        // Add a fully-transparent cube so the pick pass can detect clicks on the
        // light (the billboard geometry lives outside the normal Visuals pipeline).
        // Alpha = 0 → invisible in normal/transparent render passes;
        // the flat-colour pick shader ignores alpha so it still works for selection.
        var lightPickMesh = new CubeMesh
        {
            Alpha    = 0f,       // invisible in normal rendering
            Albedo   = Vector3.Zero,
            PickOnly = true
        };
        obj.AddMesh(lightPickMesh);

        AddToScene(obj);
        return obj;
    }

    /// <summary>Creates and registers a primitive <see cref="SceneObject"/> in the viewport.</summary>
    public SceneObject? SpawnPrimitiveObject(string primitiveType, string objectName, uint textureId = 0,
                                             string texturePath = "", PlaneOrientation planeOrientation = PlaneOrientation.XY,
                                             bool cubeMapped = false, bool sphereSmooth = true,
                                             int sphereSegments = 32, int sphereRings = 16)
    {
        if (Viewport == null) return null;

        var obj = new SceneObject
        {
            Name          = objectName,
            ObjectType    = primitiveType,
            SpawnCategory = "Primitives",
            Position      = vec3.Zero,
            PivotOffset   = new vec3(0f, 0.5f, 0f),
            AlbedoTexturePath = texturePath,
            PrimitiveCubeMapped = cubeMapped,
            PrimitiveSphereSmooth = sphereSmooth,
            PrimitiveSphereSegments = Math.Clamp(sphereSegments, 3, 256),
            PrimitiveSphereRings = Math.Clamp(sphereRings, 2, 128)
        };
        obj.AssignObjectId();

        // Create mesh geometry for supported primitive types.
        if (primitiveType == "Plane")
        {
            // 1-unit × 1-unit plane using the selected orientation.
            var mesh = new PlaneMesh(1f, 1f, planeOrientation);

            if (textureId != 0)
            {
                mesh.TextureId = textureId;
                mesh.AlbedoTexture = MineImatorLoader.ResolveVeldridTexture(textureId);
                mesh.DoubleSided = false;
            }

            obj.AddMesh(mesh);
        }

        if (primitiveType == "Cube")
        {
            var mesh = new CubeMesh(cubeMapped);

            if (textureId != 0)
            {
                mesh.TextureId = textureId;
                mesh.AlbedoTexture = MineImatorLoader.ResolveVeldridTexture(textureId);
            }

            obj.AddMesh(mesh);
        }

        if (primitiveType == "Sphere")
        {
            var mesh = new SphereMesh(0.5f, obj.PrimitiveSphereSegments,
                obj.PrimitiveSphereRings, obj.PrimitiveSphereSmooth);

            if (textureId != 0)
            {
                mesh.TextureId = textureId;
                mesh.AlbedoTexture = MineImatorLoader.ResolveVeldridTexture(textureId);
            }

            obj.AddMesh(mesh);
        }

        if (primitiveType == "Text Mesh")
            TextMeshFactory.Rebuild(obj);

        AddToScene(obj);
        return obj;
    }

    /// <summary>
    /// Creates and registers a block <see cref="SceneObject"/> whose geometry is
    /// built from the Minecraft model JSON for the chosen variant.
    /// For two-block-tall blocks (doors), the top half's meshes are added to the
    /// same object with their vertices offset +1 in Y so the door is a single unit.
    /// </summary>
    /// <param name="tileX">Tile count along +X (≥1, ≤ <see cref="SceneObject.MaxTilesPerAxis"/>).</param>
    /// <param name="tileY">Tile count along +Y (≥1, ≤ <see cref="SceneObject.MaxTilesPerAxis"/>).</param>
    /// <param name="tileZ">Tile count along +Z (≥1, ≤ <see cref="SceneObject.MaxTilesPerAxis"/>).</param>
    public SceneObject? SpawnBlockObject(string blockName, BlockVariantEntry variant, string resourcePackId = "",
                                         int tileX = 1, int tileY = 1, int tileZ = 1)
    {
        if (Viewport == null) return null;

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
            Position      = vec3.Zero,
            PivotOffset   = new vec3(0f, 0.5f, 0f),
            TileX         = tileX,
            TileY         = tileY,
            TileZ         = tileZ
        };
        obj.AssignObjectId();

        int effTileX = obj.GetEffectiveTileX();
        int effTileY = obj.GetEffectiveTileY();
        int effTileZ = obj.GetEffectiveTileZ();

        // Bottom/foot part (or full single-block)
        AddBlockMeshes(obj, variant, normalizedResourcePackId,
                       0f, 0f, 0f,
                       effTileX, effTileY, effTileZ);

        // Second part — bake offset directly into the mesh vertices
        if (variant.TopHalf != null)
            AddBlockMeshes(obj, variant.TopHalf,
                           normalizedResourcePackId,
                           variant.PartOffsetX, variant.PartOffsetY, variant.PartOffsetZ,
                           effTileX, effTileY, effTileZ);

        obj.ApplyMaterialSettingsToMeshes();

        AddToScene(obj);
        return obj;
    }

    /// <summary>
    /// Regenerates the meshes on an already-spawned block to reflect the current
    /// tile, variant, or resource-pack state.  Preserves object identity, name,
    /// transform, and other properties.
    /// </summary>
    public bool RebuildBlockMeshes(SceneObject target)
    {
        if (target == null || Viewport == null)
            return false;

        if (!string.Equals(target.SpawnCategory, "Blocks", StringComparison.Ordinal))
            return false;

        string normalizedResourcePackId = MinecraftDataLoader.NormalizeResourcePackId(target.ResourcePackId);

        var variants = BlockRegistry.GetVariants(target.ObjectType);
        var variant = variants.FirstOrDefault(v => string.Equals(v.VariantKey, target.BlockVariant, StringComparison.Ordinal))
                      ?? variants.FirstOrDefault();
        if (variant == null)
            return false;

        var temp = SpawnBlockObject(target.ObjectType, variant, normalizedResourcePackId,
                                    target.GetEffectiveTileX(),
                                    target.GetEffectiveTileY(),
                                    target.GetEffectiveTileZ());
        return temp != null && ReplaceObjectMeshesFromTempSpawn(target, temp, normalizedResourcePackId);
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

            var temp = SpawnBlockObject(target.ObjectType, variant, normalizedResourcePackId,
                target.GetEffectiveTileX(), target.GetEffectiveTileY(), target.GetEffectiveTileZ());
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
        target.ItemTileKey = tileKey;
        return true;
    }

    public bool ApplyTemporaryItemSheetSlotToSpawnedObject(SceneObject target, int columnIndex, int rowIndex)
    {
        if (target == null || Viewport == null)
            return false;
        if (string.IsNullOrWhiteSpace(target.TemporaryItemSheetPath) ||
            target.TemporaryItemSheetColumns <= 0 || target.TemporaryItemSheetRows <= 0)
            return false;

        string sheetPath = ProjectManager?.ResolveProjectPath(target.TemporaryItemSheetPath) ?? target.TemporaryItemSheetPath;
        string sheetKey = !string.IsNullOrWhiteSpace(target.TemporaryItemSheetCacheKey)
            ? target.TemporaryItemSheetCacheKey
            : ItemsAtlas.BuildTemporaryItemSheetKey(sheetPath, target.TemporaryItemSheetColumns, target.TemporaryItemSheetRows);

        if (!ItemsAtlas.TryRegisterTemporaryItemSheet(sheetKey, sheetPath, target.TemporaryItemSheetColumns, target.TemporaryItemSheetRows))
            return false;

        int clampedColumn = Math.Clamp(columnIndex, 0, target.TemporaryItemSheetColumns - 1);
        int clampedRow = Math.Clamp(rowIndex, 0, target.TemporaryItemSheetRows - 1);
        string tileKey = BuildTemporaryItemSheetTileKey(target, clampedColumn, clampedRow);

        if (!ItemsAtlas.Textures.ContainsKey(tileKey) &&
            !ItemsAtlas.TryRegisterTemporaryItemTile(sheetKey, tileKey, clampedColumn, clampedRow))
            return false;

        bool is3D = target.Visuals.OfType<ExtrudedItemMesh>().FirstOrDefault()?.Is3D ?? true;
        var temp = SpawnItemObject(tileKey, ItemAtlasSource.ItemAtlas, is3D);
        if (temp == null)
            return false;

        if (!ReplaceObjectMeshesFromTempSpawn(target, temp, ""))
            return false;

        target.ItemTileKey = tileKey;
        target.TextureType = "local";
        target.ResourcePackId = "";
        target.TemporaryItemSheetCacheKey = sheetKey;
        target.TemporaryItemSheetColumnIndex = clampedColumn;
        target.TemporaryItemSheetRowIndex = clampedRow;
        return true;
    }

    public string? EnsureTemporaryItemSheetTile(SceneObject target, int columnIndex, int rowIndex)
    {
        if (target == null)
            return null;
        if (string.IsNullOrWhiteSpace(target.TemporaryItemSheetPath) ||
            target.TemporaryItemSheetColumns <= 0 || target.TemporaryItemSheetRows <= 0)
            return null;

        string sheetPath = ProjectManager?.ResolveProjectPath(target.TemporaryItemSheetPath) ?? target.TemporaryItemSheetPath;
        string sheetKey = !string.IsNullOrWhiteSpace(target.TemporaryItemSheetCacheKey)
            ? target.TemporaryItemSheetCacheKey
            : ItemsAtlas.BuildTemporaryItemSheetKey(sheetPath, target.TemporaryItemSheetColumns, target.TemporaryItemSheetRows);

        if (!ItemsAtlas.TryRegisterTemporaryItemSheet(sheetKey, sheetPath, target.TemporaryItemSheetColumns, target.TemporaryItemSheetRows))
            return null;

        int clampedColumn = Math.Clamp(columnIndex, 0, target.TemporaryItemSheetColumns - 1);
        int clampedRow = Math.Clamp(rowIndex, 0, target.TemporaryItemSheetRows - 1);
        string tileKey = BuildTemporaryItemSheetTileKey(target, clampedColumn, clampedRow);

        if (!ItemsAtlas.Textures.ContainsKey(tileKey) &&
            !ItemsAtlas.TryRegisterTemporaryItemTile(sheetKey, tileKey, clampedColumn, clampedRow))
            return null;

        target.TemporaryItemSheetCacheKey = sheetKey;
        return tileKey;
    }

    private static string BuildTemporaryItemSheetTileKey(SceneObject target, int columnIndex, int rowIndex)
    {
        string keyBase = Path.GetFileNameWithoutExtension(target.TemporaryItemSheetPath);
        if (string.IsNullOrWhiteSpace(keyBase))
            keyBase = !string.IsNullOrWhiteSpace(target.Name) ? target.Name : "miobject_item";

        return $"miobject:{SanitizeCustomItemKey(keyBase)}_{target.ObjectId}_{columnIndex}_{rowIndex}";
    }

    private bool ReplaceObjectMeshesFromTempSpawn(SceneObject target, SceneObject temp, string normalizedResourcePackId)
    {
        foreach (var mesh in target.Visuals.ToList())
        {
            target.RemoveMesh(mesh);
            mesh.Dispose();
        }

        foreach (var mesh in temp.Visuals.ToList())
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
    /// shifting every vertex by the given block-unit offsets and replicating the
    /// geometry according to the tile counts.
    /// </summary>
    private void AddBlockMeshes(SceneObject obj, BlockVariantEntry variant,
                                string resourcePackId = "",
                                float offsetX = 0f, float offsetY = 0f, float offsetZ = 0f,
                                int tileX = 1, int tileY = 1, int tileZ = 1)
    {
        ResolvedBlockModel? resolved = null;
        if (!string.IsNullOrEmpty(variant.ModelPath))
            resolved = BlockRegistry.ResolveModel(variant.ModelPath);

        List<VeldridMesh> meshes;
        if (!string.IsNullOrEmpty(variant.CemPath))
            meshes = CemLoader.Load(variant.CemPath, BlockRegistry.VersionRoot, resourcePackId);
        else if (resolved != null)
            meshes = MinecraftModelMesh.Build(resolved, variant.RotationX, variant.RotationY, resourcePackId,
                                              obj.ObjectType,
                                              tileX, tileY, tileZ);
        else
            meshes = new List<VeldridMesh> { MinecraftModelMesh.BuildTexturedFallbackCube(null,
                blockNameHint: obj.ObjectType, resourcePackId: resourcePackId,
                tileX: tileX, tileY: tileY, tileZ: tileZ) };

        if (offsetX != 0f || offsetY != 0f || offsetZ != 0f)
        {
            var shift = new Vector3(offsetX, offsetY, offsetZ);
            foreach (var mesh in meshes)
            {
                for (int i = 0; i < mesh.Vertices.Count; i++)
                    mesh.Vertices[i] += shift;
                mesh.Upload(VeldridContext.StandardOutputDescription);
            }
        }

        byte autoEmissionLevel = ComputeAutoEmissionLevel(obj.ObjectType, variant);
        foreach (var mesh in meshes)
            mesh.AutoEmissionLevel = autoEmissionLevel;

        foreach (var mesh in meshes)
            obj.AddMesh(mesh);
    }

    // ── Utility helpers ───────────────────────────────────────────────────────

    public List<string> GetFilteredObjects()
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
