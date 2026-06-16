using System.Numerics;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.ui.Panels;

/// <summary>
/// Three-column spawn menu (Categories | Objects | Variants) displayed as a
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
                    "Cube", "Sphere", "Cylinder", "Cone", "Torus", "Plane", "Capsule"
                }
            },
            // Blocks / Items / Characters depend on Minecraft loaders not yet present.
            { "Custom Models", new List<string> { "Load..." } }
        };

        UpdateCustomModelsCategory();
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

    public override void Render()
    {
        if (!_isOpen) return;

        if (_nextWindowPos.HasValue)
        {
            // Clamp so the window doesn't fall off the right/bottom edge.
            var io = ImGui.GetIO();
            float wx = Math.Min(_nextWindowPos.Value.X, io.DisplaySize.X - 910f);
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

        ImGui.SetNextWindowSize(new Vector2(900, 440), ImGuiCond.Appearing);

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

    private void RenderMainColumns()
    {
        float totalHeight = ImGui.GetContentRegionAvail().Y - 40; // leave room for bottom bar

        ImGui.BeginChild("##cols", new Vector2(0, totalHeight));
        float columnWidth = ImGui.GetContentRegionAvail().X / 3f;

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

        // ── Objects ─────────────────────────────────────────────────────────
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

        // ── Variants ────────────────────────────────────────────────────────
        ImGui.BeginChild("##variants", new Vector2(0, 0), ImGuiChildFlags.Borders);
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

        ImGui.EndChild(); // ##cols
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
        if (_selectedObjectIndex < 0) return false;

        var filtered = GetFilteredObjects();
        if (_selectedObjectIndex >= filtered.Count) return false;

        var objectName = filtered[_selectedObjectIndex];

        if (_selectedCategory == "Custom Models")
            return objectName != "Load..." && _customModelPaths.ContainsKey(objectName);

        if (_selectedCategory == "Blocks")
            return _currentVariants.Count > 0 && _selectedVariantIndex >= 0;

        return true;
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void OnObjectSelected(string objectName)
    {
        if (_selectedCategory == "Custom Models" && objectName == "Load...")
        {
            _currentVariants.Clear();
            return;
        }

        if (_selectedCategory == "Blocks")
        {
            // TODO: populate _currentVariants from block-state JSON when that system exists
            _currentVariants.Clear();
            return;
        }

        _currentVariants.Clear();
    }

    private void OnObjectDoubleClicked(string objectName)
    {
        if (_selectedCategory == "Custom Models" && objectName == "Load...") return;
        if (_selectedCategory == "Blocks") return; // Blocks need a variant chosen first

        TrySpawn();
    }

    // ── Spawn logic ───────────────────────────────────────────────────────────

    private void TrySpawn()
    {
        var filtered = GetFilteredObjects();
        if (_selectedObjectIndex < 0 || _selectedObjectIndex >= filtered.Count) return;

        SpawnObject(filtered[_selectedObjectIndex]);
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
                // GLB/GLTF loading not yet implemented
                break;

            default:
                // Primitives and any future categories that use SceneObject
                SpawnPrimitiveObject(objectName, fullName);
                break;
        }

        // The SceneTree rebuilds itself every frame from Viewport.SceneObjects,
        // so no explicit refresh call is needed after a spawn.
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
            Position      = vec3.Zero
        };
        obj.AssignObjectId();
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
