using System.Numerics;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.world;
using MineImatorSimplyRemade.gizmo;
using MineImatorSimplyRemadeNuxi.core;
using MineImatorSimplyRemadeNuxi.core.objs;
using NativeFileDialogSharp;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui.Panels;

internal sealed unsafe class WorldImporter(SpawnMenu spawnMenu, GL gl) : IDisposable
{
    private const int TileSize = 64;
    private const int ActiveTileRadius = 1;
    private const int RetainedTileRadius = 2;
    private readonly Dictionary<(int X, int Z), PreviewTile> _tiles = [];
    private (int X, int Z)? _loadedCenter;
    private (int X, int Z) _requestedCenter;
    private Task<List<WorldVolume>>? _previewReadTask;
    private readonly Queue<WorldVolume> _pendingTiles = [];
    private int _previewTileCount, _previewTilesCompleted;
    private readonly PreviewRenderer _renderer = new(gl, 512) { AutoRotateSpeed = 0, Distance = 2.2f };
    private readonly Camera _camera = new() { Far = 4096 };
    private readonly Input _flightInput = new(24);
    private WindowHandle* _flightWindow;
    private vec3 _worldOrigin;
    private bool _cameraInitialized;
    private bool _streamingPaused;
    private Vector2 _viewportPosition, _viewportSize;
    public Glfw? GlfwApi { get; set; }
    public bool IsOpen => _open;
    private bool _open;
    private List<MinecraftWorld> _worlds = [];
    private List<string> _warnings = [];
    private MinecraftWorld? _world;
    private int _dimensionIndex;
    private int _centerX, _centerZ;
    private int _minX, _minY, _minZ, _maxX, _maxY, _maxZ;
    private string _resourcePack = "";
    private string? _error;
    private string? _notice;
    private bool _allowUnknown;
    private string[] _unknown = [];
    private SceneObject? _preview;
    private CancellationTokenSource? _cancellation;
    private Task<WorldVolume>? _readTask;
    private WorldVolume? _pendingImport;
    private WorldSpecialBlockPlan? _specialBlocks;
    private bool _convertSpecialBlocks;
    private Task<(List<MinecraftWorld> Worlds, List<string> Warnings)>? _discoveryTask;
    private string _importProjectPath = "";
    private float _progress;
    private bool _selectionDragging;
    private (int X, int Y, int Z) _selectionStart;
    private SelectionResize? _selectionResize;
    private int? _hoveredSelectionFace;
    private readonly WorldBlockFilter _blockFilter = new();
    private string _filterSearchText = "";
    private WorldBounds Selection => new(_minX, _minY, _minZ, _maxX, _maxY, _maxZ);
    private WorldDimension Dimension => _world!.Dimensions[_dimensionIndex];
    private bool PreviewBusy => _previewReadTask != null || _pendingTiles.Count > 0;
    private bool Busy => _readTask != null || _pendingImport != null || PreviewBusy || _discoveryTask != null;

    public void Open()
    {
        _open = true;
        if (_tiles.Count > 0 && _preview == null) TryAction(BuildPreview);
        if (_worlds.Count == 0 && _discoveryTask == null)
            Refresh();
    }

    private void Refresh()
    {
        _error = null;
        _discoveryTask = Task.Run(MinecraftWorld.Discover);
    }

    public void Render()
    {
        CompleteTasks();
        if (!_open) return;
        var mainViewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowSize(mainViewport.WorkSize * 0.96f, ImGuiCond.Appearing);
        ImGui.SetNextWindowPos(mainViewport.WorkPos + mainViewport.WorkSize * 0.5f,
            ImGuiCond.Appearing, new Vector2(0.5f));
        bool visible = ImGui.Begin("Import from world", ref _open,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!visible)
        {
            StopFlying();
            ImGui.End();
            if (!_open) Close();
            return;
        }
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            if (_selectionResize is { } resize)
            {
                var b = resize.Bounds;
                SelectBlocks((b.MinX, b.MinY, b.MinZ), (b.MaxX, b.MaxY, b.MaxZ));
                StopSelectionDrag();
            }
            else if (_flightInput.IsFreeFlyActive) StopFlying();
            else _open = false;
        }
        RenderToolbar();
        ImGui.Separator();
        float footerHeight = ImGui.GetTextLineHeightWithSpacing() *
                             (3 + (_error != null ? 2 : 0) + (_notice != null ? 2 : 0)) + 12;
        _viewportPosition = ImGui.GetCursorScreenPos();
        _viewportSize = new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X),
            Math.Max(1, ImGui.GetContentRegionAvail().Y - footerHeight));
        if (_world != null)
            RenderPreview();
        else
        {
            ImGui.BeginChild("##worldEmpty", _viewportSize, ImGuiChildFlags.Borders);
            ImGui.TextWrapped("Select a world in the toolbar or browse to a level.dat (Java) or .mcworld (Bedrock) file.");
            ImGui.TextWrapped("Close the world in Minecraft first. World files are read-only.");
            ImGui.EndChild();
        }
        RenderStatus();
        RenderImportOptions();
        ImGui.End();
        if (!_open) Close();
    }

    private static void NextToolbarItem(float width)
    {
        float right = ImGui.GetWindowPos().X + ImGui.GetWindowSize().X - ImGui.GetStyle().WindowPadding.X;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width < right)
            ImGui.SameLine();
    }

    private void RenderToolbar()
    {
        if (ImGui.Button("Cancel")) _open = false;
        NextToolbarItem(240);
        ImGui.BeginDisabled(Busy);
        ImGui.SetNextItemWidth(230);
        if (ImGui.BeginCombo("##World", _world?.Name ?? "Select world..."))
        {
            foreach (var world in _worlds)
                if (ImGui.Selectable($"{world.Name} ({(world.IsBedrock ? "Bedrock" : "Java")})##{world.Folder}",
                        world.Folder == _world?.Folder))
                    TryAction(() => ChooseWorld(world));
            ImGui.EndCombo();
        }
        NextToolbarItem(160);
        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("##Dimension", _world == null ? "Dimension" : Dimension.Name))
        {
            if (_world != null)
            for (int i = 0; i < _world.Dimensions.Count; i++)
            {
                if (!ImGui.Selectable(_world.Dimensions[i].Name, i == _dimensionIndex)) continue;
                _dimensionIndex = i;
                ResetArea(useSpawn: Dimension.Id == _world.SpawnDimension);
                StartRead(preview: true);
            }
            ImGui.EndCombo();
        }
        NextToolbarItem(150);
        if (ImGui.Button("Browse world..."))
        {
            var result = Dialog.FileOpen("dat,mcworld");
            if (result.IsOk && !string.IsNullOrEmpty(result.Path))
                TryAction(() => ChooseWorld(MinecraftWorld.Open(result.Path)));
        }
        NextToolbarItem(65);
        if (ImGui.Button("Reload"))
        {
            if (_world == null) Refresh();
            else StartRead(preview: true, reload: true);
        }
        NextToolbarItem(100);
        if (ImGui.Button("World spawn") && _world != null)
            TryAction(GoToSpawn);
        NextToolbarItem(80);
        if (ImGui.Button("Position...")) ImGui.OpenPopup("##worldPosition");
        RenderPositionPopup();
        ImGui.EndDisabled();
        NextToolbarItem(85);
        if (ImGui.Button("Settings...")) ImGui.OpenPopup("##worldSettings");
        RenderSettingsPopup();
        NextToolbarItem(110);
        string filterLabel = _blockFilter.IsActive
            ? $"Filter ({_blockFilter.FilteredBlocks.Count})..."
            : "Filter blocks...";
        if (ImGui.Button(filterLabel)) ImGui.OpenPopup("##worldBlockFilter");
        RenderBlockFilterPopup();
        ImGui.Separator();
        ImGui.TextUnformatted("Selection");
        NextToolbarItem(55);
        ImGui.BeginDisabled(_world == null || _readTask != null);
        if (ImGui.Button("Small")) SetSelectionSize(16);
        NextToolbarItem(65);
        if (ImGui.Button("Medium")) SetSelectionSize(32);
        NextToolbarItem(55);
        if (ImGui.Button("Large")) SetSelectionSize(64);
        NextToolbarItem(105);
        if (ImGui.Button("Edit bounds...")) ImGui.OpenPopup("##worldSelection");
        RenderSelectionPopup();
        NextToolbarItem(125);
        if (ImGui.Button("Frame selection")) FrameSelection();
        ImGui.EndDisabled();
        if (_world != null)
        {
            NextToolbarItem(220);
            string? invalid = SelectionError();
            ImGui.TextUnformatted(invalid == null
                ? $"{Selection.Width} x {Selection.Height} x {Selection.Length} ({Selection.Count:N0} blocks)"
                : "Invalid selection");
        }
    }

    private void RenderPositionPopup()
    {
        if (!ImGui.BeginPopup("##worldPosition")) return;
        ImGui.TextUnformatted("Go to Minecraft coordinates");
        ImGui.SetNextItemWidth(180);
        ImGui.InputInt("X", ref _centerX);
        ImGui.SetNextItemWidth(180);
        ImGui.InputInt("Z", ref _centerZ);
        ImGui.BeginDisabled(_world == null);
        if (ImGui.Button("Go"))
        {
            _cameraInitialized = false;
            StartRead(preview: true);
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndDisabled();
        ImGui.EndPopup();
    }

    private void RenderSettingsPopup()
    {
        if (!ImGui.BeginPopup("##worldSettings")) return;
        ImGui.TextUnformatted("World import settings");
        ImGui.TextWrapped("World files are read-only. Close the world in Minecraft before browsing.");
        ImGui.BeginDisabled(Busy);
        if (ImGui.Button("Refresh save list")) Refresh();
        if (ImGui.BeginCombo("Resource pack", string.IsNullOrEmpty(_resourcePack) ? "Default" : _resourcePack))
        {
            if (ImGui.Selectable("Default", _resourcePack.Length == 0)) SetResourcePack("");
            foreach (string id in MinecraftDataLoader.GetAvailableStandaloneResourcePackIds())
                if (ImGui.Selectable(id, _resourcePack == id)) SetResourcePack(id);
            ImGui.EndCombo();
        }
        ImGui.EndDisabled();
        float speed = _flightInput.FreeFlySpeed;
        if (ImGui.SliderFloat("Fly speed (blocks/s)", ref speed, 0.1f, 500))
            _flightInput.SetFreeFlySpeed(speed);
        ImGui.Separator();
        string filterSummary = _blockFilter.IsActive
            ? $"Block filter ({_blockFilter.FilteredBlocks.Count} filtered, {_blockFilter.Mode})"
            : "Block filter (Disabled)";
        ImGui.TextUnformatted(filterSummary);
        if (ImGui.Button("Configure block filter..."))
            ImGui.OpenPopup("##worldBlockFilter");
        RenderUnavailableBlocks();
        if (_warnings.Count > 0 && ImGui.TreeNode($"Save discovery warnings ({_warnings.Count})"))
        {
            foreach (string warning in _warnings) ImGui.TextWrapped(warning);
            ImGui.TreePop();
        }
        ImGui.EndPopup();
    }

    private void RenderBlockFilterPopup()
    {
        ImGui.SetNextWindowSize(new Vector2(440, 520), ImGuiCond.FirstUseEver);
        if (!ImGui.BeginPopup("##worldBlockFilter")) return;

        ImGui.TextUnformatted("World block filter");
        ImGui.TextWrapped("Filter out or isolate specific block types from the imported terrain.");

        bool enabled = _blockFilter.Enabled;
        if (ImGui.Checkbox("Enable block filter", ref enabled))
        {
            _blockFilter.Enabled = enabled;
            TryAction(BuildPreview);
        }

        ImGui.BeginDisabled(!_blockFilter.Enabled);

        int mode = (int)_blockFilter.Mode;
        if (ImGui.RadioButton("Remove filtered blocks", mode == 0))
        {
            _blockFilter.Mode = BlockFilterMode.Remove;
            TryAction(BuildPreview);
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Keep filtered blocks only", mode == 1))
        {
            _blockFilter.Mode = BlockFilterMode.Keep;
            TryAction(BuildPreview);
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Quick actions");
        bool hasWater = _blockFilter.FilteredBlocks.Contains("water");
        if (ImGui.Button(hasWater ? "Unfilter water" : "Filter water"))
        {
            if (hasWater) _blockFilter.FilteredBlocks.Remove("water");
            else _blockFilter.FilteredBlocks.Add("water");
            _blockFilter.Enabled = true;
            TryAction(BuildPreview);
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear all filters"))
        {
            _blockFilter.FilteredBlocks.Clear();
            TryAction(BuildPreview);
        }

        if (_blockFilter.IsWaterFilteredOut())
        {
            ImGui.TextColored(new Vector4(0.35f, 0.75f, 1f, 1f),
                "Water is filtered: underwater blocks will have their waterlogged property removed.");
        }

        if (_blockFilter.FilteredBlocks.Count > 0)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"Filtered blocks ({_blockFilter.FilteredBlocks.Count}):");
            if (ImGui.BeginChild("##filteredBlockTags", new Vector2(0, 60), ImGuiChildFlags.Borders))
            {
                string? toRemove = null;
                foreach (string b in _blockFilter.FilteredBlocks.OrderBy(x => x))
                {
                    if (ImGui.SmallButton($"X##rem_{b}"))
                        toRemove = b;
                    ImGui.SameLine();
                    ImGui.TextUnformatted(b);
                    ImGui.SameLine();
                }
                if (toRemove != null)
                {
                    _blockFilter.FilteredBlocks.Remove(toRemove);
                    TryAction(BuildPreview);
                }
                ImGui.EndChild();
            }
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Select blocks to filter");
        ImGui.InputTextWithHint("##filterSearch", "Search block name...", ref _filterSearchText, 64);

        if (ImGui.BeginChild("##blockFilterList", new Vector2(0, 180), ImGuiChildFlags.Borders))
        {
            var allBlocks = BlockRegistry.Blocks;
            string search = _filterSearchText.Trim();
            bool hasSearch = !string.IsNullOrEmpty(search);

            var loadedBlockNames = _tiles.Values
                .SelectMany(t => t.Volume.Palette)
                .Where(s => !WorldVolume.IsAir(s))
                .Select(WorldBlockFilter.GetBlockNameFromState)
                .Where(n => !string.IsNullOrEmpty(n) && n != "air");

            var candidates = allBlocks
                .Concat(loadedBlockNames)
                .Concat(["water", "lava"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x);

            int shown = 0;
            foreach (string blockName in candidates)
            {
                if (hasSearch && !blockName.Contains(search, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isFiltered = _blockFilter.FilteredBlocks.Contains(blockName);
                if (ImGui.Checkbox($"{blockName}##chk_{blockName}", ref isFiltered))
                {
                    if (isFiltered)
                        _blockFilter.FilteredBlocks.Add(blockName);
                    else
                        _blockFilter.FilteredBlocks.Remove(blockName);
                    _blockFilter.Enabled = true;
                    TryAction(BuildPreview);
                }
                shown++;
                if (shown >= 200 && hasSearch)
                {
                    ImGui.TextDisabled("... (refine search to see more)");
                    break;
                }
            }
            ImGui.EndChild();
        }

        ImGui.EndDisabled();

        ImGui.Separator();
        if (ImGui.Button("Done", new Vector2(80, 0)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private void RenderUnavailableBlocks()
    {
        if (_unknown.Length == 0) return;
        ImGui.TextWrapped($"{_unknown.Length} block types are unavailable in the installed catalog.");
        if (ImGui.TreeNode("Unavailable blocks"))
        {
            foreach (string name in _unknown) ImGui.TextUnformatted(name);
            ImGui.TreePop();
        }
        ImGui.Checkbox("Allow import with unavailable blocks omitted", ref _allowUnknown);
    }

    private void RenderStatus()
    {
        if (Busy && _pendingImport == null)
        {
            ImGui.ProgressBar(_progress, new Vector2(300, 0),
                _discoveryTask != null ? "Finding worlds..." : _readTask != null ? "Reading selection..." : "Loading nearby terrain...");
            if (_readTask != null || PreviewBusy)
            {
                ImGui.SameLine();
                if (ImGui.Button("Cancel reading")) _cancellation?.Cancel();
            }
        }
        else if (_streamingPaused)
            ImGui.TextUnformatted("Terrain streaming paused. Use Reload to retry.");
        else if (_world != null && _cameraInitialized)
        {
            var eye = _camera.Position + _worldOrigin;
            ImGui.TextUnformatted($"{Dimension.Name} | X {eye.x:F1}  Y {eye.y:F1}  Z {eye.z:F1} | " +
                                  $"{_flightInput.FreeFlySpeed:F0} blocks/s | 192 x 192 blocks | {_tiles.Count} cached tiles");
        }
        ImGui.TextWrapped("RMB + WASD: fly | Q/E: down/up | Space: faster | Shift: slower | Wheel: speed | LMB: resize face/select | Ctrl+LMB: new box | Esc: cancel/release/close");
        if (_error != null) ImGui.TextWrapped($"Error: {_error}");
        if (_notice != null) ImGui.TextWrapped(_notice);
    }

    private void RenderImportOptions()
    {
        if (_pendingImport == null || _specialBlocks == null) return;
        const string title = "Special blocks detected";
        if (!ImGui.IsPopupOpen(title)) ImGui.OpenPopup(title);
        bool open = true;
        if (ImGui.BeginPopupModal(title, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted($"{_specialBlocks.Blocks.Count:N0} animatable blocks detected in this selection.");
            if (ImGui.TreeNode("Detected types"))
            {
                foreach (var group in _specialBlocks.Blocks.GroupBy(b => b.Name).OrderBy(g => g.Key))
                    ImGui.TextUnformatted($"{group.Key}: {group.Count():N0}");
                ImGui.TreePop();
            }
            ImGui.Checkbox("Convert special blocks to animatable objects", ref _convertSpecialBlocks);
            ImGui.TextUnformatted("Off: import ordinary merged scenery. On: add editable blocks and model parts.");
            if (_specialBlocks.IncompleteBlocks > 0)
                ImGui.TextUnformatted($"{_specialBlocks.IncompleteBlocks:N0} incomplete multi-block parts stay in the scenery.");
            ImGui.TextUnformatted("Sign text, banner patterns, custom head skins and pot decorations are not read.");
            RenderUnavailableBlocks();
            if (_error != null) ImGui.TextWrapped(_error);
            if (ImGui.Button("Import"))
                TryAction(() => FinishImport(_convertSpecialBlocks));
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) open = false;
            if (!open || _pendingImport == null) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        if (!open)
        {
            _pendingImport = null;
            _specialBlocks = null;
        }
    }

    private void RenderSelectionPopup()
    {
        if (!ImGui.BeginPopup("##worldSelection")) return;
        ImGui.TextUnformatted("Inclusive Minecraft block coordinates");
        Span<int> minimum = stackalloc int[] { _minX, _minY, _minZ };
        Span<int> maximum = stackalloc int[] { _maxX, _maxY, _maxZ };
        ImGui.SetNextItemWidth(210);
        if (ImGui.InputInt3("Minimum X / Y / Z", ref minimum[0]))
        {
            _minX = minimum[0]; _minY = minimum[1]; _minZ = minimum[2];
        }
        ImGui.SetNextItemWidth(210);
        if (ImGui.InputInt3("Maximum X / Y / Z", ref maximum[0]))
        {
            _maxX = maximum[0]; _maxY = maximum[1]; _maxZ = maximum[2];
        }
        if (ImGui.Button("Select preview area") && _loadedCenter is { } center)
        {
            _minX = (center.X - ActiveTileRadius) * TileSize;
            _maxX = (center.X + ActiveTileRadius + 1) * TileSize - 1;
            _minZ = (center.Z - ActiveTileRadius) * TileSize;
            _maxZ = (center.Z + ActiveTileRadius + 1) * TileSize - 1;
        }
        string? invalid = SelectionError();
        if (invalid != null) ImGui.TextWrapped(invalid);
        if (ImGui.Button("Done")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private string? SelectionError()
    {
        try { Selection.Validate(); }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException) { return ex.Message; }
        return null;
    }

    private unsafe void RenderPreview()
    {
        _renderer.Resize((int)_viewportSize.X, (int)_viewportSize.Y);
        double previousDelta = Mesh.DeltaTime, previousSpeed = Mesh.AnimatedTextureSpeedScale;
        bool previousAdvance = Mesh.AdvanceAnimatedTextures;
        try
        {
            Mesh.DeltaTime = ImGui.GetIO().DeltaTime;
            Mesh.AdvanceAnimatedTextures = true;
            Mesh.AnimatedTextureSpeedScale = 1;
            _renderer.Render(Array.Empty<Mesh>(), "world", 0, sceneRoot: _preview, camera: _camera);
        }
        finally
        {
            Mesh.DeltaTime = previousDelta;
            Mesh.AdvanceAnimatedTextures = previousAdvance;
            Mesh.AnimatedTextureSpeedScale = previousSpeed;
        }
        ImGui.Image(new ImTextureRef(texId: (ulong)_renderer.ColorTexture), _viewportSize,
            new Vector2(0, 1), new Vector2(1, 0));
        Vector2 importPosition = _viewportPosition + new Vector2(Math.Max(0, (_viewportSize.X - 240) * 0.5f),
            Math.Max(0, _viewportSize.Y - 68));
        Vector2 mouse = ImGui.GetIO().MousePos;
        bool overImport = mouse.X >= importPosition.X && mouse.X <= importPosition.X + 240 &&
                          mouse.Y >= importPosition.Y && mouse.Y <= importPosition.Y + 42;
        bool hovered = ImGui.IsItemHovered() && !overImport;
        Vector2 afterImage = ImGui.GetCursorScreenPos();
        HandleFlight(hovered);
        var io = ImGui.GetIO();
        bool canSelect = !_flightInput.IsFreeFlyActive && _readTask == null && _tiles.Count > 0 &&
                         !io.WantTextInput && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        if (!canSelect || !ImGui.IsMouseDown(ImGuiMouseButton.Left)) StopSelectionDrag();
        var ray = PointerRay(mouse, _viewportPosition, _viewportSize);
        bool resizable = _minY >= Dimension.MinY && _maxY <= Dimension.MaxY &&
                         _minX >= -30_000_000 && _maxX <= 30_000_000 &&
                         _minZ >= -30_000_000 && _maxZ <= 30_000_000;
        float edgeTolerance = 2 * MathF.Tan(_camera.FovY * 0.5f) * 5 / _viewportSize.Y;
        var face = hovered && canSelect && resizable && !io.KeyCtrl
            ? HitSelectionFace(Selection, ray.Origin, ray.Direction, edgeTolerance) : null;
        if (face is { } candidate)
        {
            var clip = _renderer.ViewProjection * new vec4(candidate.Point - _worldOrigin, 1);
            if (clip.w <= 0 || clip.z < -clip.w || clip.z > clip.w) face = null;
        }
        _hoveredSelectionFace = _selectionResize?.Face ?? (!_selectionDragging ? face?.Face : null);
        if (hovered && canSelect && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (face is { } hit)
                BeginSelectionResize(hit.Face, hit.Point, ray.Direction, mouse);
            else if (Pick(mouse, _viewportPosition, _viewportSize) is { } start)
            {
                _selectionStart = start;
                _selectionDragging = true;
            }
        }
        if (_selectionResize is { } activeResize)
        {
            UpdateSelectionResize(activeResize, mouse, ray.Origin, ray.Direction);
            ImGui.SetNextFrameWantCaptureMouse(true);
        }
        else if (hovered && _selectionDragging && Pick(mouse, _viewportPosition, _viewportSize) is { } end)
            SelectBlocks(_selectionStart, end);
        if (_hoveredSelectionFace != null)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            if (_selectionResize == null)
                ImGui.SetTooltip("Drag this face to resize. Ctrl+drag starts a new box.");
        }
        DrawSelection(_viewportPosition, _viewportSize);
        ImGui.SetCursorScreenPos(importPosition);
        ImGui.BeginDisabled(Busy || SelectionError() != null || !ProjectManager.Instance.HasProject ||
                            (_unknown.Length > 0 && !_allowUnknown));
        if (ImGui.Button("Import selected area", new Vector2(240, 42))) StartRead(preview: false);
        ImGui.EndDisabled();
        if (!ProjectManager.Instance.HasProject)
            ImGui.TextUnformatted("Open a project before importing.");
        else if (_unknown.Length > 0 && !_allowUnknown)
            ImGui.TextUnformatted("Review unavailable blocks in Settings.");
        ImGui.SetCursorScreenPos(afterImage);
        UpdateStreaming();
    }

    private void DrawSelection(Vector2 position, Vector2 size)
    {
        if (_tiles.Count == 0) return;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(position, position + size, true);
        foreach (var (start, end) in ProjectSelectionEdges(Selection, _worldOrigin, _renderer.ViewProjection, position, size))
            draw.AddLine(start, end, 0xff00ccff, 2);
        if (_hoveredSelectionFace is { } face)
            foreach (var (start, end) in ProjectSelectionEdges(Selection, _worldOrigin, _renderer.ViewProjection, position, size, face))
                draw.AddLine(start, end, 0xff40c0ff, 4);
        draw.PopClipRect();
    }

    internal static List<(Vector2 Start, Vector2 End)> ProjectSelectionEdges(WorldBounds bounds,
        vec3 worldOrigin, mat4 viewProjection, Vector2 position, Vector2 size, int? face = null)
    {
        Span<vec4> corners = stackalloc vec4[8];
        for (int i = 0; i < corners.Length; i++)
        {
            vec3 p = new vec3((i & 1) == 0 ? bounds.MinX : (float)bounds.MaxX + 1,
                (i & 2) == 0 ? bounds.MinY : (float)bounds.MaxY + 1,
                (i & 4) == 0 ? bounds.MinZ : (float)bounds.MaxZ + 1) - worldOrigin;
            corners[i] = viewProjection * new vec4(p, 1);
        }
        List<(Vector2 Start, Vector2 End)> edges = [];
        for (int i = 0; i < 8; i++)
        for (int bit = 1; bit <= 4; bit <<= 1)
        {
            if ((i & bit) != 0) continue;
            if (face is { } selectedFace &&
                (bit == (1 << (selectedFace / 2)) || ((i >> (selectedFace / 2)) & 1) != selectedFace % 2))
                continue;
            var a = corners[i];
            var b = corners[i | bit];
            if (!ClipSelectionEdge(ref a, ref b)) continue;
            var start = position + new Vector2(a.x / a.w + 1, 1 - a.y / a.w) * size * 0.5f;
            var end = position + new Vector2(b.x / b.w + 1, 1 - b.y / b.w) * size * 0.5f;
            edges.Add((start, end));
        }
        return edges;
    }

    private static bool ClipSelectionEdge(ref vec4 a, ref vec4 b)
    {
        // Clip in homogeneous space before dividing by W, including edges crossing behind the eye.
        for (int plane = 0; plane < 6; plane++)
        {
            int axis = plane / 2;
            int sign = (plane & 1) == 0 ? 1 : -1;
            float da = a.w + sign * a[axis], db = b.w + sign * b[axis];
            if (da < 0 && db < 0) return false;
            if (da < 0 || db < 0)
            {
                var intersection = a + (b - a) * (da / (da - db));
                if (da < 0) a = intersection;
                else b = intersection;
            }
        }
        return a.w > 0 && b.w > 0;
    }

    private (int X, int Y, int Z)? Pick(Vector2 mouse, Vector2 position, Vector2 size)
    {
        if (_tiles.Count == 0) return null;
        var b = new WorldBounds(_tiles.Values.Min(t => t.Volume.Bounds.MinX), Dimension.MinY,
            _tiles.Values.Min(t => t.Volume.Bounds.MinZ), _tiles.Values.Max(t => t.Volume.Bounds.MaxX),
            Dimension.MaxY, _tiles.Values.Max(t => t.Volume.Bounds.MaxZ));
        var (origin, direction) = PointerRay(mouse, position, size);
        float enter = 0, exit = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            float min = axis == 0 ? b.MinX : axis == 1 ? b.MinY : b.MinZ;
            float max = (axis == 0 ? b.MaxX : axis == 1 ? b.MaxY : b.MaxZ) + 1;
            if (MathF.Abs(direction[axis]) < 0.00001f)
            {
                if (origin[axis] < min || origin[axis] > max) return null;
                continue;
            }
            float a = (min - origin[axis]) / direction[axis], c = (max - origin[axis]) / direction[axis];
            enter = Math.Max(enter, Math.Min(a, c));
            exit = Math.Min(exit, Math.Max(a, c));
        }
        if (enter >= exit) return null;
        vec3 start = origin + direction * (enter + 0.001f);
        int[] cell = [(int)MathF.Floor(start.x), (int)MathF.Floor(start.y), (int)MathF.Floor(start.z)];
        int[] step = [Math.Sign(direction.x), Math.Sign(direction.y), Math.Sign(direction.z)];
        float[] next = new float[3], delta = new float[3];
        for (int axis = 0; axis < 3; axis++)
        {
            delta[axis] = step[axis] == 0 ? float.PositiveInfinity : MathF.Abs(1 / direction[axis]);
            next[axis] = step[axis] == 0 ? float.PositiveInfinity :
                (cell[axis] + (step[axis] > 0 ? 1 : 0) - origin[axis]) / direction[axis];
        }
        for (float t = enter; t < exit;)
        {
            int x = cell[0], y = cell[1], z = cell[2];
            if (HasBlock(x, y, z))
                return (x, y, z);
            t = Math.Min(next[0], Math.Min(next[1], next[2]));
            for (int axis = 0; axis < 3; axis++)
            {
                if (next[axis] > t) continue;
                cell[axis] += step[axis];
                next[axis] += delta[axis];
            }
        }
        return null;
    }

    private (vec3 Origin, vec3 Direction) PointerRay(Vector2 mouse, Vector2 position, Vector2 size)
    {
        Vector2 ndc = (mouse - position) / size * 2 - Vector2.One;
        mat4 inverse = _renderer.ViewProjection.Inverse;
        vec4 near = inverse * new vec4(ndc.X, -ndc.Y, -1, 1);
        vec4 far = inverse * new vec4(ndc.X, -ndc.Y, 1, 1);
        return (near.xyz / near.w + _worldOrigin, (far.xyz / far.w - near.xyz / near.w).Normalized);
    }

    internal static (int Face, vec3 Point)? HitSelectionFace(WorldBounds bounds, vec3 origin, vec3 direction,
        float edgeTolerance = 0)
    {
        if (bounds.MinX > bounds.MaxX || bounds.MinY > bounds.MaxY || bounds.MinZ > bounds.MaxZ) return null;
        var minimum = new vec3(bounds.MinX, bounds.MinY, bounds.MinZ);
        var maximum = new vec3((float)bounds.MaxX + 1, (float)bounds.MaxY + 1, (float)bounds.MaxZ + 1);
        (int Face, vec3 Point)? hit = null;
        float nearest = float.PositiveInfinity;
        for (int face = 0; face < 6; face++)
        {
            int axis = face / 2;
            if (MathF.Abs(direction[axis]) < 1e-6f) continue;
            float boundary = face % 2 == 0 ? minimum[axis] : maximum[axis];
            float distance = (boundary - origin[axis]) / direction[axis];
            if (distance < 0 || distance >= nearest) continue;
            var point = origin + direction * distance;
            float tolerance = 0.0001f + distance * edgeTolerance;
            bool inside = true;
            for (int other = 0; other < 3; other++)
                if (other != axis && (point[other] < minimum[other] - tolerance || point[other] > maximum[other] + tolerance))
                    inside = false;
            if (!inside) continue;
            nearest = distance;
            hit = (face, point);
        }
        return hit;
    }

    private void BeginSelectionResize(int face, vec3 point, vec3 rayDirection, Vector2 mouse)
    {
        int axis = face / 2;
        var normal = rayDirection;
        normal[axis] = 0;
        bool endOn = normal.Length < 0.05f;
        var eye = _camera.Position + _worldOrigin;
        float unitsPerPixel = 2 * (point - eye).Length * MathF.Tan(_camera.FovY * 0.5f) / _viewportSize.Y;
        _selectionResize = new SelectionResize(face, Selection, point,
            endOn ? vec3.Zero : normal.Normalized, mouse, unitsPerPixel);
        _selectionDragging = false;
    }

    private void UpdateSelectionResize(SelectionResize resize, Vector2 mouse, vec3 rayOrigin, vec3 rayDirection)
    {
        float movement;
        if (resize.PlaneNormal == vec3.Zero)
            movement = (resize.Mouse.Y - mouse.Y) * resize.UnitsPerPixel;
        else
        {
            var point = GizmoMath.PlaneIntersectsRay(resize.PlaneNormal, resize.Point, rayOrigin, rayDirection);
            if (point == null) return;
            movement = point.Value[resize.Face / 2] - resize.Point[resize.Face / 2];
        }
        var bounds = ResizeSelection(resize.Bounds, resize.Face, (int)Math.Clamp(
            MathF.Round(movement, MidpointRounding.AwayFromZero), -60_000_000, 60_000_000),
            Dimension.MinY, Dimension.MaxY);
        SelectBlocks((bounds.MinX, bounds.MinY, bounds.MinZ), (bounds.MaxX, bounds.MaxY, bounds.MaxZ));
    }

    internal static WorldBounds ResizeSelection(WorldBounds bounds, int face, int movement, int minY, int maxY)
    {
        Span<int> minimum = stackalloc int[] { bounds.MinX, bounds.MinY, bounds.MinZ };
        Span<int> maximum = stackalloc int[] { bounds.MaxX, bounds.MaxY, bounds.MaxZ };
        int axis = face / 2;
        int low = axis == 1 ? minY : -30_000_000, high = axis == 1 ? maxY : 30_000_000;
        if (face % 2 == 0)
            minimum[axis] = (int)Math.Clamp((long)minimum[axis] + movement, low, maximum[axis]);
        else
            maximum[axis] = (int)Math.Clamp((long)maximum[axis] + movement, minimum[axis], high);
        return new WorldBounds(minimum[0], minimum[1], minimum[2], maximum[0], maximum[1], maximum[2]);
    }

    private void StopSelectionDrag()
    {
        _selectionDragging = false;
        _selectionResize = null;
        _hoveredSelectionFace = null;
    }

    private void HandleFlight(bool hovered)
    {
        var io = ImGui.GetIO();
        bool focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        if (!focused ||
            !ImGui.IsMouseDown(ImGuiMouseButton.Right) || io.WantTextInput || !_cameraInitialized)
            StopFlying();
        if (!focused || io.WantTextInput || GlfwApi == null || !_cameraInitialized) return;
        var handle = (WindowHandle*)ImGui.GetWindowViewport().PlatformHandle;
        if (handle == null) return;
        if (_flightInput.IsFreeFlyActive && _flightWindow != handle) StopFlying();
        _flightWindow = handle;
        _flightInput.ProcessFreeFlyInput(_camera, GlfwApi, handle, _viewportPosition, _viewportSize, io.MousePos,
            hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right), ImGui.IsMouseReleased(ImGuiMouseButton.Right),
            ImGui.IsKeyDown(ImGuiKey.W), ImGui.IsKeyDown(ImGuiKey.S),
            ImGui.IsKeyDown(ImGuiKey.A), ImGui.IsKeyDown(ImGuiKey.D),
            ImGui.IsKeyDown(ImGuiKey.E), ImGui.IsKeyDown(ImGuiKey.Q),
            ImGui.IsKeyDown(ImGuiKey.Space), ImGui.IsKeyDown(ImGuiKey.ModShift), false,
            io.MouseWheel, Math.Min(io.DeltaTime, 0.1f), hovered);
        var eye = _camera.Position + _worldOrigin;
        var clamped = new vec3(Math.Clamp(eye.x, -29_999_968, 29_999_968),
            Math.Clamp(eye.y, -2048, 4096), Math.Clamp(eye.z, -29_999_968, 29_999_968));
        _camera.Target += clamped - eye;
        if (_flightInput.IsFreeFlyActive)
        {
            ImGui.SetNextFrameWantCaptureMouse(true);
            ImGui.SetNextFrameWantCaptureKeyboard(true);
            _selectionDragging = false;
        }
    }

    private void StopFlying()
    {
        _flightInput.StopFreeFly(GlfwApi, _flightWindow);
        _flightWindow = null;
    }

    private void GoToSpawn()
    {
        int index = FindSpawnDimension(_world!);
        if (index < 0)
            throw new InvalidDataException($"The spawn dimension '{_world!.SpawnDimension}' has no generated terrain.");
        _dimensionIndex = index;
        ResetArea(useSpawn: true);
        StartRead(preview: true);
    }

    private void SetSelectionSize(int size)
    {
        var eye = _camera.Position + _worldOrigin;
        var aim = eye + (_camera.Target - _camera.Position).Normalized * 16;
        var hit = _tiles.Count == 0 ? null : Pick(_viewportPosition + _viewportSize * 0.5f,
            _viewportPosition, _viewportSize);
        SetSelectionCube(hit ?? ((int)MathF.Floor(aim.x), (int)MathF.Floor(aim.y), (int)MathF.Floor(aim.z)), size);
    }

    private void SelectBlocks((int X, int Y, int Z) start, (int X, int Y, int Z) end)
    {
        _minX = Math.Min(start.X, end.X); _maxX = Math.Max(start.X, end.X);
        _minY = Math.Min(start.Y, end.Y); _maxY = Math.Max(start.Y, end.Y);
        _minZ = Math.Min(start.Z, end.Z); _maxZ = Math.Max(start.Z, end.Z);
    }

    private void SetSelectionCube((int X, int Y, int Z) center, int size)
    {
        int height = Math.Min(size, Dimension.MaxY - Dimension.MinY + 1);
        _minX = Math.Clamp(center.X - size / 2, -30_000_000, 30_000_001 - size);
        _minY = Math.Clamp(center.Y - height / 2, Dimension.MinY, Dimension.MaxY - height + 1);
        _minZ = Math.Clamp(center.Z - size / 2, -30_000_000, 30_000_001 - size);
        _maxX = _minX + size - 1; _maxY = _minY + height - 1; _maxZ = _minZ + size - 1;
    }

    private void FrameSelection()
    {
        TryAction(() =>
        {
            Selection.Validate();
            var extent = new vec3(Selection.Width, Selection.Height, Selection.Length);
            var center = new vec3(_minX, _minY, _minZ) + extent * 0.5f;
            float aspect = Math.Max(1, _viewportSize.X) / Math.Max(1, _viewportSize.Y);
            float halfFov = Math.Min(_camera.FovY * 0.5f, MathF.Atan(MathF.Tan(_camera.FovY * 0.5f) * aspect));
            FrameCamera(center, Math.Max(24, extent.Length * 0.55f / MathF.Sin(halfFov)));
            _streamingPaused = false;
        });
    }

    private float SurfaceHeight(int worldX, int worldZ)
    {
        if (_tiles.TryGetValue((worldX >> 6, worldZ >> 6), out var tile))
        {
            var volume = tile.Volume;
            var b = volume.Bounds;
            int x = worldX - b.MinX, z = worldZ - b.MinZ;
            if (x >= 0 && x < b.Width && z >= 0 && z < b.Length)
            for (int y = b.Height - 1; y >= 0; y--)
            {
                string state = volume.Get(x, y, z);
                if (!WorldVolume.IsAir(state) && (!_blockFilter.IsActive || !_blockFilter.ShouldRemoveBlock(WorldBlockFilter.GetBlockNameFromState(state))))
                    return b.MinY + y + 1;
            }
            for (int i = 0; i < volume.Blocks.Length; i++)
                if (volume.Blocks[i] != 0)
                {
                    string state = volume.Palette[volume.Blocks[i]];
                    if (!_blockFilter.IsActive || !_blockFilter.ShouldRemoveBlock(WorldBlockFilter.GetBlockNameFromState(state)))
                        return b.MinY + i / (b.Width * b.Length) + 1;
                }
        }
        return _world!.SpawnDimension == Dimension.Id ? _world.SpawnY : Math.Clamp(64, Dimension.MinY, Dimension.MaxY);
    }

    private void FrameCamera(vec3 worldTarget, float distance)
    {
        _camera.Yaw = 0.75f;
        _camera.Pitch = 0.55f;
        _camera.Distance = Camera.DefaultDistance;
        var offset = new vec3(MathF.Cos(_camera.Pitch) * MathF.Sin(_camera.Yaw),
            MathF.Sin(_camera.Pitch), MathF.Cos(_camera.Pitch) * MathF.Cos(_camera.Yaw));
        _camera.Target = worldTarget - _worldOrigin + offset * (distance - _camera.Distance);
        _cameraInitialized = true;
    }

    private void UpdateStreaming()
    {
        if (_world == null || _readTask != null || PreviewBusy || !_cameraInitialized || _streamingPaused ||
            _loadedCenter == null)
            return;
        var aim = _camera.Position + _worldOrigin + (_camera.Target - _camera.Position).Normalized * 16;
        int x = (int)MathF.Floor(aim.x), z = (int)MathF.Floor(aim.z);
        if (PreviewCenter(x, z) == _loadedCenter) return;
        _centerX = x;
        _centerZ = z;
        StartRead(preview: true);
    }

    private void ChooseWorld(MinecraftWorld world)
    {
        _world = world;
        int spawnDimension = FindSpawnDimension(world);
        _dimensionIndex = Math.Max(0, spawnDimension);
        if (!_worlds.Any(w => w.Folder == world.Folder)) _worlds.Add(world);
        ResetArea(useSpawn: spawnDimension >= 0);
        StartRead(preview: true);
        if (spawnDimension < 0)
            _notice = $"Spawn dimension '{world.SpawnDimension}' has no generated terrain. Showing {Dimension.Name} around the origin.";
    }

    private static int FindSpawnDimension(MinecraftWorld world)
    {
        for (int i = 0; i < world.Dimensions.Count; i++)
            if (world.Dimensions[i].Id == world.SpawnDimension)
                return i;
        return -1;
    }
    private void ResetArea(bool useSpawn)
    {
        StopFlying();
        StopSelectionDrag();
        ClearPreview();
        _tiles.Clear();
        _loadedCenter = null;
        _unknown = [];
        _allowUnknown = false;
        _centerX = useSpawn ? _world!.SpawnX : 0;
        _centerZ = useSpawn ? _world!.SpawnZ : 0;
        _worldOrigin = new vec3(_centerX, 0, _centerZ);
        _cameraInitialized = false;
        _streamingPaused = false;
        int y = useSpawn ? _world!.SpawnY : Math.Clamp(64, Dimension.MinY, Dimension.MaxY);
        SetSelectionCube((_centerX, y, _centerZ), 16);
    }

    private void StartRead(bool preview, bool reload = false)
    {
        TryAction(() =>
        {
            if (_world == null || _readTask != null || PreviewBusy) return;
            if (!preview) Selection.Validate();
            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            var world = _world;
            var dimension = Dimension;
            _progress = 0;
            _importProjectPath = preview ? "" : ProjectManager.Instance.ProjectFilePath;
            _notice = null;
            if (preview)
            {
                _streamingPaused = false;
                _requestedCenter = PreviewCenter(_centerX, _centerZ);
                List<WorldBounds> missing = [];
                for (int z = _requestedCenter.Z - ActiveTileRadius; z <= _requestedCenter.Z + ActiveTileRadius; z++)
                for (int x = _requestedCenter.X - ActiveTileRadius; x <= _requestedCenter.X + ActiveTileRadius; x++)
                    if (reload || !_tiles.ContainsKey((x, z)))
                        missing.Add(new WorldBounds(x * TileSize, dimension.MinY, z * TileSize,
                            (x + 1) * TileSize - 1, dimension.MaxY, (z + 1) * TileSize - 1));
                missing = missing.OrderBy(b => Math.Abs((b.MinX >> 6) - _requestedCenter.X) +
                    Math.Abs((b.MinZ >> 6) - _requestedCenter.Z)).ToList();
                if (reload)
                    missing.AddRange(_tiles.Values.Select(t => t.Volume.Bounds).Where(b =>
                        !missing.Any(m => m.MinX == b.MinX && m.MinZ == b.MinZ)));
                _previewTileCount = missing.Count;
                _previewTilesCompleted = 0;
                if (missing.Count == 0) FinishPreview(true);
                else
                    _previewReadTask = Task.Run(() => WorldImportService.ReadTiles(world, dimension, missing, token,
                        new ReadProgress(value => Volatile.Write(ref _progress, value * 0.5f))), token);
            }
            else
            {
                var bounds = Selection;
                _readTask = Task.Run(() => WorldImportService.Read(world, dimension, bounds, token,
                    new ReadProgress(value => Volatile.Write(ref _progress, value))), token);
            }
        });
    }

    private void CompleteTasks()
    {
        if (_discoveryTask is { IsCompleted: true } discovery)
        {
            _discoveryTask = null;
            TryAction(() =>
            {
                var result = discovery.GetAwaiter().GetResult();
                _worlds = result.Worlds; _warnings = result.Warnings;
                foreach (string warning in _warnings) Console.Error.WriteLine($"World discovery: {warning}");
            });
        }
        CompletePreviewTasks();
        if (_readTask is not { IsCompleted: true } task) return;
        _readTask = null;
        bool cancelled = _cancellation!.IsCancellationRequested;
        _cancellation.Dispose();
        _cancellation = null;
        if (cancelled || !_open)
        {
            if (task.IsFaulted) Console.Error.WriteLine($"World read: {task.Exception!.GetBaseException().Message}");
            _notice = "World reading cancelled.";
            return;
        }
        TryAction(() =>
        {
            var volume = task.GetAwaiter().GetResult();
            var filtered = volume.ApplyFilter(_blockFilter);
            var plan = WorldSpecialBlocks.Detect(filtered);
            if (plan.Blocks.Count > 0)
            {
                _pendingImport = filtered;
                _specialBlocks = plan;
                StopFlying();
            }
            else ImportSelection(filtered, null);
        });
    }

    private void FinishImport(bool convert)
    {
        if (_pendingImport == null) throw new InvalidOperationException("There is no pending world selection.");
        ImportSelection(_pendingImport, convert ? _specialBlocks : null);
    }

    private void ImportSelection(WorldVolume volume, WorldSpecialBlockPlan? conversion)
    {
        var acknowledged = _allowUnknown ? _unknown : [];
        _unknown = FindUnknown(conversion?.Terrain(volume) ?? volume);
        if (!string.Equals(_importProjectPath, ProjectManager.Instance.ProjectFilePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The active project changed while terrain was loading. Import the selection again in the intended project.");
        if (_unknown.Except(acknowledged, StringComparer.Ordinal).Any())
        {
            _allowUnknown = false;
            throw new InvalidDataException("The selected area contains unavailable blocks. Review the list and explicitly allow omission before importing.");
        }
        Import(volume, conversion);
        Close();
    }

    private static (int X, int Z) PreviewCenter(int x, int z)
    {
        const int worldTileLimit = 30_000_000 / TileSize;
        return (Math.Clamp(x >> 6, -worldTileLimit + ActiveTileRadius, worldTileLimit - ActiveTileRadius - 1),
            Math.Clamp(z >> 6, -worldTileLimit + ActiveTileRadius, worldTileLimit - ActiveTileRadius - 1));
    }

    private bool HasBlock(int x, int y, int z)
    {
        if (!_tiles.TryGetValue((x >> 6, z >> 6), out var tile)) return false;
        var b = tile.Volume.Bounds;
        if (x < b.MinX || x > b.MaxX || y < b.MinY || y > b.MaxY || z < b.MinZ || z > b.MaxZ) return false;
        string state = tile.Volume.Get(x - b.MinX, y - b.MinY, z - b.MinZ);
        if (WorldVolume.IsAir(state)) return false;
        if (_blockFilter.IsActive && _blockFilter.ShouldRemoveBlock(WorldBlockFilter.GetBlockNameFromState(state)))
            return false;
        return true;
    }

    private void CompletePreviewTasks()
    {
        if (!PreviewBusy) return;
        if (_previewReadTask is { IsCompleted: true } task)
        {
            _previewReadTask = null;
            if (_cancellation!.IsCancellationRequested || !_open)
            {
                if (task.IsFaulted) Console.Error.WriteLine($"World preview: {task.Exception!.GetBaseException().Message}");
                _notice = "World reading cancelled.";
                FinishPreview(false);
                return;
            }
            TryAction(() =>
            {
                foreach (var volume in task.GetAwaiter().GetResult())
                    _pendingTiles.Enqueue(volume);
            });
            if (_error != null)
            {
                FinishPreview(false);
                return;
            }
            if (_pendingTiles.Count == 0)
            {
                FinishPreview(true);
                return;
            }
        }
        if (_previewReadTask != null) return;
        if (_cancellation!.IsCancellationRequested || !_open)
        {
            _notice = "World reading cancelled.";
            FinishPreview(false);
            return;
        }
        // Keep existing meshes while gradually uploading the newly reached edge.
        var started = System.Diagnostics.Stopwatch.StartNew();
        for (int count = 0; count < 2 && _pendingTiles.Count > 0; count++)
        {
            var volume = _pendingTiles.Dequeue();
            TryAction(() => InstallTile(volume));
            if (_error != null)
            {
                FinishPreview(false);
                return;
            }
            _progress = 0.5f + 0.5f * ++_previewTilesCompleted / _previewTileCount;
            if (started.ElapsedMilliseconds >= 8) break;
        }
        if (_pendingTiles.Count == 0) FinishPreview(true);
    }

    private void FinishPreview(bool completed)
    {
        _pendingTiles.Clear();
        _cancellation?.Dispose();
        _cancellation = null;
        _streamingPaused = !completed;
        if (!completed) return;
        _loadedCenter = _requestedCenter;
        PruneTiles();
        if (!_cameraInitialized)
            FrameCamera(new vec3(_centerX, SurfaceHeight(_centerX, _centerZ), _centerZ), 36);
        _unknown = _tiles.Values.Select(t => t.Volume.ApplyFilter(_blockFilter)).SelectMany(FindUnknown).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        if (_tiles.Values.All(t => t.Volume.ApplyFilter(_blockFilter).NonAirCount == 0))
            _notice = "No generated terrain in this area. Fly back or go to another position.";
    }

    private void PruneTiles()
    {
        foreach (var key in _tiles.Keys.Where(k => Math.Abs(k.X - _requestedCenter.X) > RetainedTileRadius ||
                     Math.Abs(k.Z - _requestedCenter.Z) > RetainedTileRadius).ToArray())
        {
            var tile = _tiles[key];
            if (tile.Scene != null)
            {
                _preview?.RemoveChild(tile.Scene);
                DisposePreviewScene(tile.Scene);
            }
            _tiles.Remove(key);
        }
    }

    private void InstallTile(WorldVolume volume)
    {
        var key = (volume.Bounds.MinX >> 6, volume.Bounds.MinZ >> 6);
        var scene = CreateTilePreview(volume);
        if (_tiles.TryGetValue(key, out var previous) && previous.Scene != null)
        {
            _preview?.RemoveChild(previous.Scene);
            DisposePreviewScene(previous.Scene);
        }
        _tiles[key] = new PreviewTile(volume, scene);
        _preview ??= new SceneObject { Name = "World preview" };
        if (scene != null) _preview.AddChild(scene);
        PruneTiles();
        _allowUnknown = false;
        if (!_cameraInitialized)
            FrameCamera(new vec3(_centerX, SurfaceHeight(_centerX, _centerZ), _centerZ), 36);
    }

    private static string[] FindUnknown(WorldVolume volume)
    {
        var available = new HashSet<string>(BlockRegistry.Blocks, StringComparer.OrdinalIgnoreCase);
        var missing = new HashSet<string>(StringComparer.Ordinal);
        foreach (string state in volume.Blocks.Distinct().Select(id => volume.Palette[id]))
        {
            if (WorldVolume.IsAir(state)) continue;
            if (state.StartsWith("legacy:", StringComparison.Ordinal))
            {
                string[] parts = state.Split(':');
                if (parts.Length != 3 || !int.TryParse(parts[1], out int id) || !int.TryParse(parts[2], out int data) ||
                    !SpawnMenu.TryResolveLegacyBlock(id, data, out string name, out _) || !available.Contains(name))
                    missing.Add(state);
            }
            else
            {
                string name = state.Split('[')[0];
                if (!available.Contains(name.StartsWith("minecraft:", StringComparison.Ordinal) ? name[10..] : name))
                    missing.Add(name);
            }
        }
        return missing.Order(StringComparer.Ordinal).ToArray();
    }

    private void BuildPreview()
    {
        Dictionary<(int X, int Z), PreviewTile> rebuilt = [];
        try
        {
            foreach (var (key, tile) in _tiles)
                rebuilt.Add(key, new PreviewTile(tile.Volume, CreateTilePreview(tile.Volume)));
        }
        catch
        {
            foreach (var tile in rebuilt.Values)
                if (tile.Scene != null) DisposePreviewScene(tile.Scene);
            throw;
        }
        ClearPreview();
        _preview = new SceneObject { Name = "World preview" };
        foreach (var (key, tile) in rebuilt)
        {
            _tiles[key] = tile;
            if (tile.Scene != null) _preview.AddChild(tile.Scene);
        }
    }

    private SceneObject? CreateTilePreview(WorldVolume volume)
    {
        var filtered = volume.ApplyFilter(_blockFilter);
        if (filtered.NonAirCount == 0) return null;
        string path = Path.Combine(Path.GetTempPath(), $"nuxi-world-preview-{Guid.NewGuid():N}.schem");
        try
        {
            filtered.SaveSchematic(path);
            var preview = spawnMenu.CreateSchematicPreview(path, _resourcePack);
            var bounds = filtered.Bounds;
            preview.Position = new vec3(bounds.MinX + bounds.Width * 0.5f, bounds.MinY,
                bounds.MinZ + bounds.Length * 0.5f) - _worldOrigin;
            return preview;
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private void SetResourcePack(string id)
    {
        string previous = _resourcePack;
        _resourcePack = id;
        TryAction(() =>
        {
            try { BuildPreview(); }
            catch
            {
                _resourcePack = previous;
                throw;
            }
        });
    }

    private void Import(WorldVolume volume, WorldSpecialBlockPlan? conversion = null)
    {
        var manager = ProjectManager.Instance;
        if (!manager.HasProject) throw new InvalidOperationException("Open a project before importing terrain.");
        string path = Path.Combine(Path.GetTempPath(), $"World-{Guid.NewGuid():N}.schem");
        SceneObject? root = null;
        var addedAssets = new List<ProjectAssetEntry>();
        bool committed = false;
        try
        {
            var terrain = conversion?.Terrain(volume) ?? volume;
            if (terrain.NonAirCount == 0 && (conversion == null || conversion.Blocks.Count == 0))
                throw new InvalidDataException("The selected area has no spawnable blocks.");
            if (terrain.NonAirCount > 0)
            {
                terrain.SaveSchematic(path);
                root = spawnMenu.CreateSchematicPreview(path, _resourcePack);
                var asset = manager.AddAsset(path, ProjectAssetType.Other);
                addedAssets.Add(asset);
                root.SourceAssetPath = manager.GetAssetFullPath(asset);
            }
            else
            {
                root = new SceneObject { ObjectType = "WorldImportGroup", PivotOffset = vec3.Zero };
                root.AssignObjectId();
            }
            if (conversion != null)
            {
                using var specialImporter = new WorldSpecialBlockImporter(spawnMenu, _resourcePack, addedAssets);
                foreach (var block in conversion.Blocks)
                    root.AddChild(specialImporter.Create(block));
            }
            root.Name = $"{_world!.Name} - {Dimension.Name} ({volume.Bounds.MinX}, {volume.Bounds.MinY}, {volume.Bounds.MinZ})";
            spawnMenu.Viewport!.SceneObjects.Add(root);
            SelectionManager.Instance.SelectObject(root);
            manager.SetDirty(true);
            _notice = $"Imported selection ({volume.NonAirCount:N0} source blocks) into the project." +
                      (conversion != null ? $" Converted {conversion.Blocks.Count:N0} animatable blocks." : "") +
                      (_blockFilter.IsActive ? $" Applied block filter ({_blockFilter.FilteredBlocks.Count} types)." : "") +
                      (_unknown.Length > 0 ? $" Omitted {_unknown.Length} unavailable block types." : "");
            committed = true;
            root = null;
        }
        finally
        {
            if (root != null)
            {
                spawnMenu.Viewport?.SceneObjects.Remove(root);
                DisposePreviewScene(root);
            }
            if (!committed)
                foreach (var asset in addedAssets.AsEnumerable().Reverse()) manager.RemoveAsset(asset);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private void TryAction(Action action)
    {
        _error = null;
        try { action(); }
        catch (Exception ex)
        {
            _error = ex.Message;
            Console.Error.WriteLine($"World importer: {ex}");
        }
    }

    private void ClearPreview()
    {
        if (_preview == null) return;
        DisposePreviewScene(_preview);
        _preview = null;
        foreach (var key in _tiles.Keys.ToArray())
            _tiles[key] = _tiles[key] with { Scene = null };
    }

    private static void DisposePreviewScene(SceneObject scene)
    {
        foreach (var child in scene.Children) DisposePreviewScene(child);
        foreach (var mesh in scene.Visuals) mesh.Dispose();
    }

    private void Close()
    {
        _open = false;
        StopFlying();
        StopSelectionDrag();
        _cancellation?.Cancel();
        _pendingImport = null;
        _specialBlocks = null;
        ClearPreview();
        _renderer.Dispose();
    }

    public void Dispose() => Close();

    private sealed record PreviewTile(WorldVolume Volume, SceneObject? Scene);
    private sealed record SelectionResize(int Face, WorldBounds Bounds, vec3 Point, vec3 PlaneNormal,
        Vector2 Mouse, float UnitsPerPixel);

    private sealed class ReadProgress(Action<float> report) : IProgress<float>
    {
        public void Report(float value) => report(value);
    }
}
