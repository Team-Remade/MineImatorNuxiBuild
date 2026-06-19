using System.Numerics;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using NativeFileDialogSharp;
using Silk.NET.OpenGL;

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

    // ── Window positioning ───────────────────────────────────────────────────
    private Vector2? _nextWindowPos;

    // ── Blocks category state ─────────────────────────────────────────────────

    /// <summary>Search filter applied to the blocks object list.</summary>
    private string _blockSearchBuffer = "";
    private string _blockSearchQuery  = "";

    // ── Characters category state ──────────────────────────────────────────────

    /// <summary>Search filter applied to the characters object list.</summary>
    private string _charSearchBuffer = "";
    private string _charSearchQuery  = "";

    // ── Preview renderer ──────────────────────────────────────────────────────

    /// <summary>Off-screen FBO renderer that draws the preview column content.</summary>
    private PreviewRenderer? _previewRenderer;

    /// <summary>
    /// Meshes currently loaded for the preview.  Rebuilt whenever the selection
    /// key changes.  Disposed with the old meshes before rebuilding.
    /// </summary>
    private List<Mesh> _previewMeshes = new();

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
            { "Custom Models", new List<string> { "Load..." } }
        };

        UpdateCustomModelsCategory();
        RefreshBlocksCategory();
        RefreshCharactersCategory();
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

            // Build fresh meshes for the new selection
            if (!string.IsNullOrEmpty(newKey))
                _previewMeshes = BuildPreviewMeshes();

            // Tune camera distance to fit the object type
            _previewRenderer.Distance = GetPreviewDistance();
            _previewRenderer.Yaw      = 0.75f;
            _previewRenderer.Pitch    = 0.35f;
        }

        // Render into FBO every frame (so auto-rotation plays)
        _previewRenderer.Render(_previewMeshes, _previewKey, deltaTime);
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
                        $"{(_selectedVariantIndex >= 0 ? _selectedVariantIndex : 0)}",
            "Characters" => _selectedObjectIndex < 0 ||
                            _selectedObjectIndex >= CharacterRegistry.Characters.Count ? "" :
                            $"char:{CharacterRegistry.Characters[_selectedObjectIndex].FilePath}",
            _ => _selectedObjectIndex < 0 ? "" :
                 $"std:{_selectedCategory}:{GetFilteredObjects().ElementAtOrDefault(_selectedObjectIndex) ?? ""}"
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
                    tileSize = ItemsAtlas.TileSize;
                }
                else
                {
                    TerrainAtlas.Textures.TryGetValue(_selectedTileKey, out tileTexId);
                    TerrainAtlas.TilePixels.TryGetValue(_selectedTileKey, out tilePixels);
                    tileSize = TerrainAtlas.TileSize;
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

            default:
                return new List<Mesh>();
        }
    }

    /// <summary>
    /// Builds block meshes from a <see cref="BlockVariantEntry"/> and appends them
    /// to <paramref name="meshes"/>.  Mirrors <see cref="AddBlockMeshes"/> but does
    /// not attach anything to a SceneObject.
    /// </summary>
    private void AppendBlockMeshesForPreview(List<Mesh> meshes, BlockVariantEntry variant)
    {
        if (Gl == null) return;

        ResolvedBlockModel? resolved = null;
        if (!string.IsNullOrEmpty(variant.ModelPath))
            resolved = BlockRegistry.ResolveModel(variant.ModelPath);

        List<Mesh> built;
        if (!string.IsNullOrEmpty(variant.CemPath))
            built = CemLoader.Load(Gl, variant.CemPath, BlockRegistry.VersionRoot);
        else if (resolved != null)
            built = MinecraftModelMesh.Build(Gl, resolved, variant.RotationX, variant.RotationY);
        else
            built = new List<Mesh>
            {
                MinecraftModelMesh.BuildTexturedFallbackCube(Gl, null, blockNameHint: "")
            };

        meshes.AddRange(built);
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
                    _selectedCategory    = category;
                    _selectedObjectIndex  = -1;
                    _selectedVariantIndex = -1;
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
            // Characters have no sub-variants — show an empty variants column for consistency.
            ImGui.BeginChild("##charVariants", new Vector2(columnWidth, 0), ImGuiChildFlags.Borders);
            ImGui.TextDisabled("Variants");
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextDisabled("(not available)");
            ImGui.EndChild();
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
            else
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

        var textures = _itemAtlasSource == ItemAtlasSource.ItemAtlas
            ? ItemsAtlas.Textures
            : TerrainAtlas.Textures;

        int col = 0;
        foreach (var kvp in textures)
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
        ImGui.Spacing();
        ImGui.Checkbox("3D (extruded)", ref _item3DMode);
        ImGui.Spacing();
        ImGui.TextDisabled(_item3DMode
            ? "Each pixel is extruded\nto form a hull mesh."
            : "Flat double-sided plane\nwith the tile texture.");

        ImGui.EndChild();
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
                bool hasGeometry = _previewMeshes.Count > 0 &&
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
                _selectedObjectIndex  = i;
                _selectedVariantIndex = -1;
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
            return _selectedObjectIndex >= 0 && _selectedObjectIndex < chars.Count;
        }

        return true;
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void OnObjectSelected(string objectName)
    {
        if (_selectedCategory == "Custom Models" && objectName == "Load...")
        {
            _currentVariants.Clear();
            OpenCustomModelFileDialog();
            return;
        }

        _currentVariants.Clear();
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
            "glb,gltf,fbx,obj,dae,3ds,blend,ply,stl,x3d");

        if (result.IsOk && !string.IsNullOrEmpty(result.Path))
            SpawnCustomModelFromPath(result.Path);
    }

    /// <summary>
    /// Loads the model at <paramref name="filePath"/> via Assimp and spawns the
    /// resulting hierarchy as a child of the scene root.
    /// Returns the root <see cref="SceneObject"/> on success, or <c>null</c> on error.
    /// </summary>
    public SceneObject? SpawnCustomModelFromPath(string filePath)
    {
        if (Viewport == null || Gl == null) return null;

        SceneObject? root = AssimpModelLoader.Load(Gl, filePath);
        if (root == null)
        {
            Console.Error.WriteLine($"[SpawnMenu] Failed to load model: {filePath}");
            return null;
        }

        string displayName = Path.GetFileNameWithoutExtension(filePath);
        root.Name = displayName;

        AddToCustomModelHistory(filePath, displayName);
        Viewport.SceneObjects.Add(root);
        return root;
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

    // ── Spawn logic ───────────────────────────────────────────────────────────

    private void TrySpawn()
    {
        if (_selectedCategory == "Items")
        {
            TrySpawnItem();
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
        SpawnBlockObject(blockName, variant);
        _isOpen = false;
    }

    private void TrySpawnCharacter()
    {
        var chars = CharacterRegistry.Characters;
        if (_selectedObjectIndex < 0 || _selectedObjectIndex >= chars.Count) return;

        var entry = chars[_selectedObjectIndex];
        SpawnCustomModelFromPath(entry.FilePath);
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

            default:
                // Primitives and any future categories that use SceneObject
                SpawnPrimitiveObject(objectName, fullName);
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
            Position      = vec3.Zero
        };
        obj.AssignObjectId();

        int tileSize = atlasSource == ItemAtlasSource.ItemAtlas ? ItemsAtlas.TileSize : TerrainAtlas.TileSize;
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

    // ── Public spawn helpers ─────────────────────────────────────────────────

    /// <summary>Creates and registers a <see cref="CameraSceneObject"/> in the viewport.</summary>
    public CameraSceneObject? SpawnCameraObject(string objectName)
    {
        if (Viewport == null) return null;

        var obj = new CameraSceneObject
        {
            Name          = objectName,
            ObjectType    = "Camera",
            SpawnCategory = "Camera",
            Position      = vec3.Zero
        };
        obj.AssignObjectId();
        Viewport.SceneObjects.Add(obj);
        return obj;
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
                Alpha  = 0f,       // invisible in normal rendering
                Albedo = vec3.Zero
            };
            obj.AddMesh(pickMesh);
        }

        Viewport.SceneObjects.Add(obj);
        return obj;
    }

    /// <summary>Creates and registers a primitive <see cref="SceneObject"/> in the viewport.</summary>
    public SceneObject? SpawnPrimitiveObject(string primitiveType, string objectName)
    {
        if (Viewport == null) return null;

        var obj = new SceneObject
        {
            Name          = objectName,
            ObjectType    = primitiveType,
            SpawnCategory = "Primitives",
            Position      = vec3.Zero,
            PivotOffset   = new vec3(0f, 0.5f, 0f)
        };
        obj.AssignObjectId();

        // Create mesh geometry for supported primitive types.
        if (primitiveType == "Plane" && Gl != null)
        {
            // 1-unit × 1-unit vertical (XY) plane
            obj.AddMesh(new PlaneMesh(Gl, 1f, 1f, PlaneOrientation.XY));
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
    public SceneObject? SpawnBlockObject(string blockName, BlockVariantEntry variant)
    {
        if (Viewport == null || Gl == null) return null;

        int nextNum     = GetNextAvailableObjectNumber(blockName);
        string fullName = nextNum > 1 ? $"{blockName}{nextNum}" : blockName;

        var obj = new SceneObject
        {
            Name          = fullName,
            ObjectType    = blockName,
            SpawnCategory = "Blocks",
            BlockVariant  = variant.VariantKey,
            TextureType   = "block",
            Position      = GlmSharp.vec3.Zero,
            PivotOffset   = new GlmSharp.vec3(0f, 0.5f, 0f)
        };
        obj.AssignObjectId();

        // Bottom/foot part (or full single-block)
        AddBlockMeshes(obj, variant);

        // Second part — bake offset directly into the mesh vertices
        if (variant.TopHalf != null)
            AddBlockMeshes(obj, variant.TopHalf,
                           variant.PartOffsetX, variant.PartOffsetY, variant.PartOffsetZ);

        Viewport.SceneObjects.Add(obj);
        return obj;
    }

    /// <summary>
    /// Builds meshes for <paramref name="variant"/> and adds them to <paramref name="obj"/>,
    /// shifting every vertex by the given block-unit offsets.
    /// </summary>
    private void AddBlockMeshes(SceneObject obj, BlockVariantEntry variant,
                                float offsetX = 0f, float offsetY = 0f, float offsetZ = 0f)
    {
        ResolvedBlockModel? resolved = null;
        if (!string.IsNullOrEmpty(variant.ModelPath))
            resolved = BlockRegistry.ResolveModel(variant.ModelPath);

        List<Mesh> meshes;
        if (!string.IsNullOrEmpty(variant.CemPath))
            meshes = CemLoader.Load(Gl!, variant.CemPath, BlockRegistry.VersionRoot);
        else if (resolved != null)
            meshes = MinecraftModelMesh.Build(Gl!, resolved, variant.RotationX, variant.RotationY);
        else
            meshes = new List<Mesh> { MinecraftModelMesh.BuildTexturedFallbackCube(Gl!, null,
                blockNameHint: obj.ObjectType) };

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
