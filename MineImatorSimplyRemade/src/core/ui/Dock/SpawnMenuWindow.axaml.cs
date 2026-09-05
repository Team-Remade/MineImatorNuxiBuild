using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemade.core.ui.Panels;

namespace MineImatorSimplyRemade.core.ui.Dock;

/// <summary>
/// Avalonia port of the old ImGui floating "Spawn Menu" window
/// (<c>core.ui.Panels.SpawnMenu.Render</c>). Four columns
/// (Categories | Objects | Variants | Preview) + global search + Spawn button.
/// All state and spawn logic lives in the injected <see cref="SpawnMenu"/>
/// model; this class only builds the per-category column content and forwards
/// input. The 3-D preview is driven by a UI timer calling
/// <see cref="SpawnMenu.UpdatePreview"/> which renders off-screen via Veldrid
/// and returns a bitmap.
/// </summary>
public partial class SpawnMenuWindow : Window
{
    private readonly SpawnMenu _model;
    private readonly DispatcherTimer _previewTimer;

    // Tile thumbnails are tiny (usually 16x16) but numerous; cache them app-wide.
    private static readonly Dictionary<string, WriteableBitmap> TileThumbCache = new();

    // Selected-tile highlight bookkeeping for the Items grid.
    private readonly Dictionary<string, Border> _tileBorders = new();

    private Window? _retryDialog;
    private bool _suppressSelectionEvents;

    private Point _lastPreviewDragPoint;
    private bool _previewDragging;

    /// <summary>Parameterless constructor for the XAML previewer only.</summary>
    public SpawnMenuWindow() : this(new SpawnMenu())
    {
    }

    public SpawnMenuWindow(SpawnMenu model)
    {
        _model = model;
        InitializeComponent();

        SearchBox.TextChanged += (_, _) =>
        {
            _model.SearchQuery = SearchBox.Text ?? "";
            RebuildObjectsColumn();
            RebuildVariantsColumn();
        };
        ClearSearchButton.Click += (_, _) => { SearchBox.Text = ""; };

        CategoryList.SelectionChanged += (_, _) =>
        {
            if (_suppressSelectionEvents) return;
            if (CategoryList.SelectedItem is string category)
            {
                _model.SelectCategory(category);
                RebuildObjectsColumn();
                RebuildVariantsColumn();
            }
        };

        SpawnButton.Click += (_, _) => _model.TrySpawn();

        PreviewImage.PointerPressed += OnPreviewPointerPressed;
        PreviewImage.PointerMoved += OnPreviewPointerMoved;
        PreviewImage.PointerReleased += (_, _) => _previewDragging = false;

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _previewTimer.Tick += (_, _) => TickPreview();

        _model.CloseRequested += OnModelCloseRequested;
        _model.SchematicRetryPromptChanged += OnSchematicRetryPromptChanged;

        Opened += (_, _) =>
        {
            _model.OnMenuOpened();
            RebuildCategoryList();
            RebuildObjectsColumn();
            RebuildVariantsColumn();
            _previewTimer.Start();
        };

        Closed += (_, _) =>
        {
            _previewTimer.Stop();
            _model.CloseRequested -= OnModelCloseRequested;
            _model.SchematicRetryPromptChanged -= OnSchematicRetryPromptChanged;
        };
    }

    private void OnModelCloseRequested() => Close();

    // ── Categories ────────────────────────────────────────────────────────────

    private void RebuildCategoryList()
    {
        _suppressSelectionEvents = true;
        CategoryList.ItemsSource = _model.Categories;
        CategoryList.SelectedItem = _model.SelectedCategory;
        _suppressSelectionEvents = false;
    }

    // ── Preview ──────────────────────────────────────────────────────────────

    private void TickPreview()
    {
        var bitmap = _model.UpdatePreview(_previewTimer.Interval.TotalSeconds);
        bool hasGeometry = _model.PreviewHasGeometry && bitmap != null;

        PreviewImage.Source = hasGeometry ? bitmap : null;
        PreviewImage.IsVisible = hasGeometry;
        PreviewImage.InvalidateVisual();
        NoPreviewLabel.IsVisible = !hasGeometry;

        (string caption1, string caption2) = BuildPreviewCaptions();
        PreviewCaption1.Text = caption1;
        PreviewCaption2.Text = caption2;

        SpawnButton.IsEnabled = _model.CanSpawn();
    }

    private (string, string) BuildPreviewCaptions()
    {
        switch (_model.SelectedCategory)
        {
            case "Items":
            {
                if (string.IsNullOrEmpty(_model.SelectedTileKey))
                    return ("Select a tile to see a preview.", "");
                string atlasLabel = _model.ItemAtlasSourceSelection == ItemAtlasSource.ItemAtlas ? "ItemAtlas" : "BlockAtlas";
                return ($"{atlasLabel}[{_model.SelectedTileKey}]", _model.Item3DMode ? "3D extruded" : "Flat plane");
            }
            case "Blocks":
            {
                string? name = _model.SelectedBlockName;
                return name == null
                    ? ("Select a block to see a preview.", "")
                    : (name, _model.SelectedBlockVariantKey ?? "");
            }
            case "Characters":
                return (_model.SelectedCharacterEntry?.Name ?? "Select a character to see a preview.", "");
            case "Particle Spawners":
            {
                var selected = _model.GetParticleLibraryOptions().FirstOrDefault(o =>
                    string.Equals(o.Id, _model.SelectedParticleLibraryEntryId, StringComparison.OrdinalIgnoreCase));
                return ("Particle Spawner", selected != null
                    ? $"Source: {selected.Name}"
                    : "No source selected. You can assign one later in Properties > Particles.");
            }
            default:
                return (_model.SelectedObjectName ?? "Select an object to see a preview.", "");
        }
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(PreviewImage).Properties.IsLeftButtonPressed) return;
        _previewDragging = true;
        _lastPreviewDragPoint = e.GetPosition(PreviewImage);
        e.Pointer.Capture(PreviewImage);
    }

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_previewDragging) return;
        Point pos = e.GetPosition(PreviewImage);
        Point delta = pos - _lastPreviewDragPoint;
        _lastPreviewDragPoint = pos;
        _model.OrbitPreview((float)delta.X * 0.01f, -(float)delta.Y * 0.01f);
    }

    // ── Objects column ───────────────────────────────────────────────────────

    private void RebuildObjectsColumn()
    {
        switch (_model.SelectedCategory)
        {
            case "Items":
                ObjectsHeader.Text = "Tiles";
                ObjectsHost.Content = BuildItemsObjectsPanel();
                break;
            case "Blocks":
                ObjectsHeader.Text = "Blocks";
                ObjectsHost.Content = BuildBlocksObjectsPanel();
                break;
            case "Characters":
                ObjectsHeader.Text = "Characters";
                ObjectsHost.Content = BuildCharactersObjectsPanel();
                break;
            default:
                ObjectsHeader.Text = "Objects";
                ObjectsHost.Content = BuildStandardObjectsPanel();
                break;
        }
    }

    private Control BuildStandardObjectsPanel()
    {
        var list = new ListBox { Background = Brushes.Transparent };
        var objects = _model.GetFilteredObjects();
        list.ItemsSource = objects;
        if (_model.SelectedObjectIndex >= 0 && _model.SelectedObjectIndex < objects.Count)
            list.SelectedIndex = _model.SelectedObjectIndex;

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            _model.SelectObject(list.SelectedIndex);
            RebuildVariantsColumn();
        };
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedIndex >= 0)
                _model.ActivateObject(list.SelectedIndex);
        };
        return list;
    }

    private Control BuildBlocksObjectsPanel()
    {
        var panel = new DockPanel();

        panel.Children.Add(BuildSourceCombo("Source Mod:", _model.AvailableSourceModIds, _model.SpawnBlockSourceId, id =>
        {
            _model.SpawnBlockSourceId = id;
            RebuildObjectsColumn();
            RebuildVariantsColumn();
        }));

        var searchBox = new TextBox { Watermark = "Filter blocks...", Text = _model.BlockSearchQuery, Margin = new Thickness(0, 4) };
        DockPanel.SetDock(searchBox, global::Avalonia.Controls.Dock.Top);
        panel.Children.Add(searchBox);

        var list = new ListBox { Background = Brushes.Transparent };
        panel.Children.Add(list);

        void FillList()
        {
            var blocks = _model.GetFilteredBlocks();
            list.ItemsSource = blocks.Select(b => new ListBoxItem { Content = b.Name, Tag = b.Index }).ToList();
            int selIdx = blocks.FindIndex(b => b.Index == _model.SelectedObjectIndex);
            if (selIdx >= 0) list.SelectedIndex = selIdx;
        }

        FillList();

        searchBox.TextChanged += (_, _) =>
        {
            _model.BlockSearchQuery = searchBox.Text ?? "";
            FillList();
            RebuildVariantsColumn();
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: int idx })
            {
                _model.SelectBlock(idx);
                RebuildVariantsColumn();
            }
        };
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: int idx })
                _model.ActivateBlock(idx);
        };

        return panel;
    }

    private Control BuildCharactersObjectsPanel()
    {
        var panel = new DockPanel();

        var searchBox = new TextBox { Watermark = "Filter characters...", Text = _model.CharSearchQuery, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(searchBox, global::Avalonia.Controls.Dock.Top);
        panel.Children.Add(searchBox);

        var list = new ListBox { Background = Brushes.Transparent };
        panel.Children.Add(list);

        void FillList()
        {
            var chars = _model.GetFilteredCharacters();
            list.ItemsSource = chars.Select(c => new ListBoxItem { Content = c.Label, Tag = c.Index }).ToList();
            int selIdx = chars.FindIndex(c => c.Index == _model.SelectedObjectIndex);
            if (selIdx >= 0) list.SelectedIndex = selIdx;
            if (chars.Count == 0)
            {
                list.ItemsSource = new[]
                {
                    new ListBoxItem
                    {
                        Content = "(no characters found - place models in a 'characters/' folder)",
                        IsEnabled = false
                    }
                };
            }
        }

        FillList();

        searchBox.TextChanged += (_, _) =>
        {
            _model.CharSearchQuery = searchBox.Text ?? "";
            FillList();
            RebuildVariantsColumn();
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: int idx })
            {
                _model.SelectCharacter(idx);
                RebuildVariantsColumn();
            }
        };
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: int idx })
                _model.ActivateCharacter(idx);
        };

        return panel;
    }

    private Control BuildItemsObjectsPanel()
    {
        var panel = new DockPanel();

        // Atlas source
        var atlasCombo = new ComboBox
        {
            ItemsSource = new[] { "Item Atlas", "Block Atlas" },
            SelectedIndex = (int)_model.ItemAtlasSourceSelection,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 4)
        };
        DockPanel.SetDock(atlasCombo, global::Avalonia.Controls.Dock.Top);
        panel.Children.Add(atlasCombo);

        panel.Children.Add(BuildSourceCombo("Source:", _model.AvailableItemSourceIds, _model.SpawnItemSourceId, id =>
        {
            _model.SpawnItemSourceId = id;
            RebuildObjectsColumn();
        }));

        var searchBox = new TextBox
        {
            Watermark = "Filter tiles (e.g. grass)...",
            Text = _model.ItemSearchQuery,
            Margin = new Thickness(0, 4)
        };
        DockPanel.SetDock(searchBox, global::Avalonia.Controls.Dock.Top);
        panel.Children.Add(searchBox);

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        var scroll = new ScrollViewer { Content = wrap };
        panel.Children.Add(scroll);

        void FillGrid()
        {
            wrap.Children.Clear();
            _tileBorders.Clear();

            bool isItemAtlas = _model.ItemAtlasSourceSelection == ItemAtlasSource.ItemAtlas;
            foreach (string key in _model.GetFilteredItemTileKeys())
            {
                var thumb = GetTileThumbnail(key, isItemAtlas);
                if (thumb == null) continue;

                var border = new Border
                {
                    Width = 32,
                    Height = 32,
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(3),
                    BorderThickness = new Thickness(2),
                    BorderBrush = _model.SelectedTileKey == key ? Brushes.CornflowerBlue : Brushes.Transparent,
                    Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E)),
                    Child = new Image { Source = thumb, Stretch = Stretch.Uniform, Margin = new Thickness(1) },
                };
                ToolTip.SetTip(border, key);

                string tileKey = key;
                border.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
                    SelectTile(tileKey);
                };
                border.DoubleTapped += (_, _) => _model.ActivateTile(tileKey);

                _tileBorders[key] = border;
                wrap.Children.Add(border);
            }
        }

        FillGrid();

        atlasCombo.SelectionChanged += (_, _) =>
        {
            _model.ItemAtlasSourceSelection = (ItemAtlasSource)Math.Max(0, atlasCombo.SelectedIndex);
            FillGrid();
        };
        searchBox.TextChanged += (_, _) =>
        {
            _model.ItemSearchQuery = searchBox.Text ?? "";
            FillGrid();
        };

        return panel;
    }

    private void SelectTile(string key)
    {
        string previous = _model.SelectedTileKey;
        _model.SelectedTileKey = key;

        if (!string.IsNullOrEmpty(previous) && _tileBorders.TryGetValue(previous, out var prevBorder))
            prevBorder.BorderBrush = Brushes.Transparent;
        if (_tileBorders.TryGetValue(key, out var border))
            border.BorderBrush = Brushes.CornflowerBlue;
    }

    // ── Variants column ──────────────────────────────────────────────────────

    private void RebuildVariantsColumn()
    {
        switch (_model.SelectedCategory)
        {
            case "Items":
                VariantsHeader.Text = "Options";
                VariantsHost.Content = BuildItemsVariantsPanel();
                break;
            case "Characters":
                VariantsHeader.Text = "Texture";
                VariantsHost.Content = BuildCharactersVariantsPanel();
                break;
            case "Particle Spawners":
                VariantsHeader.Text = "Particle Source";
                VariantsHost.Content = BuildParticleVariantsPanel();
                break;
            default:
                VariantsHeader.Text = "Variants";
                VariantsHost.Content = BuildStandardVariantsPanel();
                break;
        }
    }

    private Control BuildStandardVariantsPanel()
    {
        var stack = new StackPanel { Spacing = 4 };

        if (_model.SelectedCategory == "Scenery")
        {
            stack.Children.Add(BuildSourceCombo("Resource Pack:", _model.AvailableSceneryResourcePackIds,
                _model.SpawnResourcePackId, id => _model.SpawnResourcePackId = id));
        }

        if (_model.SelectedCategory == "Blocks")
        {
            stack.Children.Add(BuildSourceCombo("Resource Pack:", _model.AvailableResourcePackIds,
                _model.SpawnResourcePackId, id => _model.SpawnResourcePackId = id));
        }

        if (_model.SelectedObjectIsSpherePrimitive)
        {
            stack.Children.Add(MutedText("Sphere Geometry"));

            var smooth = new CheckBox { Content = "Smooth Shading", IsChecked = _model.PrimitiveSphereSmooth };
            smooth.IsCheckedChanged += (_, _) => _model.PrimitiveSphereSmooth = smooth.IsChecked == true;
            stack.Children.Add(smooth);

            stack.Children.Add(LabeledNumeric("Segments", 3, 256, _model.PrimitiveSphereSegments,
                v => _model.PrimitiveSphereSegments = v));
            stack.Children.Add(LabeledNumeric("Rings", 2, 128, _model.PrimitiveSphereRings,
                v => _model.PrimitiveSphereRings = v));
        }

        if (_model.SelectedObjectSupportsPrimitiveTexture)
        {
            string primitiveName = _model.SelectedObjectName ?? "";
            if (primitiveName == "Plane")
            {
                stack.Children.Add(MutedText("Orientation"));
                var orientationCombo = new ComboBox
                {
                    ItemsSource = new[] { "XY", "XZ" },
                    SelectedIndex = _model.PrimitivePlaneOrientation == PlaneOrientation.XZ ? 1 : 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                orientationCombo.SelectionChanged += (_, _) =>
                    _model.PrimitivePlaneOrientation = orientationCombo.SelectedIndex == 1
                        ? PlaneOrientation.XZ
                        : PlaneOrientation.XY;
                stack.Children.Add(orientationCombo);
            }
            else if (primitiveName == "Cube")
            {
                stack.Children.Add(MutedText("UV Mapping"));
                var mapped = new CheckBox { Content = "Mapped", IsChecked = _model.PrimitiveCubeMapped };
                mapped.IsCheckedChanged += (_, _) => _model.PrimitiveCubeMapped = mapped.IsChecked == true;
                stack.Children.Add(mapped);

                var uvButton = new Button { Content = "Save UV map...", HorizontalAlignment = HorizontalAlignment.Stretch };
                uvButton.Click += (_, _) => SpawnMenu.SaveCubeUvMapGuide();
                stack.Children.Add(uvButton);
                stack.Children.Add(MutedText("Exports the cube layout reference image."));
            }

            stack.Children.Add(MutedText("Texture"));

            var currentLabel = MutedText(string.IsNullOrEmpty(_model.SelectedPrimitiveTexturePath)
                ? "(None)"
                : $"Current: {System.IO.Path.GetFileName(_model.SelectedPrimitiveTexturePath)}");
            stack.Children.Add(currentLabel);

            var loadButton = new Button { Content = "Load texture...", HorizontalAlignment = HorizontalAlignment.Stretch };
            loadButton.Click += (_, _) =>
            {
                _model.LoadPrimitiveTextureFromDialog();
                RebuildVariantsColumn();
            };
            stack.Children.Add(loadButton);

            var clearButton = new Button { Content = "Clear", HorizontalAlignment = HorizontalAlignment.Stretch };
            clearButton.Click += (_, _) =>
            {
                _model.ClearPrimitiveTexture();
                RebuildVariantsColumn();
            };
            stack.Children.Add(clearButton);
        }

        if (_model.CurrentVariants.Count > 0)
        {
            var list = new ListBox
            {
                Background = Brushes.Transparent,
                ItemsSource = _model.CurrentVariants,
                SelectedIndex = _model.SelectedVariantIndex
            };
            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedIndex >= 0)
                    _model.SelectedVariantIndex = list.SelectedIndex;
            };
            list.DoubleTapped += (_, _) =>
            {
                if (list.SelectedIndex >= 0)
                    _model.ActivateVariant(list.SelectedIndex);
            };
            stack.Children.Add(list);
        }
        else if (!_model.SelectedObjectSupportsPrimitiveTexture && !_model.SelectedObjectIsSpherePrimitive &&
                 _model.SelectedCategory != "Scenery")
        {
            stack.Children.Add(MutedText(_model.SelectedObjectIndex >= 0 ? "(no variants)" : "(not available)"));
        }

        return new ScrollViewer { Content = stack };
    }

    private Control BuildItemsVariantsPanel()
    {
        var stack = new StackPanel { Spacing = 6 };

        var mode3D = new CheckBox { Content = "3D (extruded)", IsChecked = _model.Item3DMode };
        var modeHint = MutedText(_model.Item3DMode
            ? "Each pixel is extruded to form a hull mesh."
            : "Flat double-sided plane with the tile texture.");
        mode3D.IsCheckedChanged += (_, _) =>
        {
            _model.Item3DMode = mode3D.IsChecked == true;
            modeHint.Text = _model.Item3DMode
                ? "Each pixel is extruded to form a hull mesh."
                : "Flat double-sided plane with the tile texture.";
        };
        stack.Children.Add(mode3D);
        stack.Children.Add(modeHint);

        var importButton = new Button { Content = "Load custom image...", HorizontalAlignment = HorizontalAlignment.Stretch };
        importButton.Click += (_, _) =>
        {
            if (_model.ImportCustomItemImage() != null)
                RebuildObjectsColumn();
        };
        stack.Children.Add(importButton);

        return stack;
    }

    private Control BuildCharactersVariantsPanel()
    {
        var stack = new StackPanel { Spacing = 4 };
        var entry = _model.SelectedCharacterEntry;

        if (entry == null)
        {
            stack.Children.Add(MutedText("Select a character to see textures."));
            return stack;
        }

        if (entry.TextureVariants.Count == 0)
        {
            stack.Children.Add(MutedText("(no texture variants)"));
            return stack;
        }

        stack.Children.Add(new TextBlock { Text = "Skin:", Foreground = Brushes.LightGray });

        int comboIndex = _model.SelectedCharTextureIndex >= 0 &&
                         _model.SelectedCharTextureIndex < entry.TextureVariants.Count
            ? _model.SelectedCharTextureIndex
            : 0;

        var list = new ListBox
        {
            Background = Brushes.Transparent,
            ItemsSource = entry.TextureVariants.Select(v => v.Name).ToList(),
            SelectedIndex = comboIndex
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            _model.SelectedCharTextureIndex = list.SelectedIndex;
            RebuildVariantsColumn();
        };
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedIndex >= 0)
                _model.ActivateCharTexture(list.SelectedIndex);
        };
        stack.Children.Add(list);

        var selectedVariant = entry.TextureVariants[comboIndex];
        if (selectedVariant.IsCustom)
        {
            var browseButton = new Button { Content = "Browse...", HorizontalAlignment = HorizontalAlignment.Stretch };
            browseButton.Click += (_, _) =>
            {
                if (_model.BrowseCustomCharTexture())
                    RebuildVariantsColumn();
            };
            stack.Children.Add(browseButton);

            if (!string.IsNullOrEmpty(_model.CustomCharTexturePath))
            {
                var fileLabel = MutedText(System.IO.Path.GetFileName(_model.CustomCharTexturePath));
                ToolTip.SetTip(fileLabel, _model.CustomCharTexturePath);
                stack.Children.Add(fileLabel);
            }
            else
            {
                stack.Children.Add(MutedText("No file chosen."));
            }
        }

        return new ScrollViewer { Content = stack };
    }

    private Control BuildParticleVariantsPanel()
    {
        var panel = new DockPanel();

        var searchBox = new TextBox
        {
            Watermark = "Search object library...",
            Text = _model.ParticleLibrarySearchQuery,
            Margin = new Thickness(0, 0, 0, 4)
        };
        DockPanel.SetDock(searchBox, global::Avalonia.Controls.Dock.Top);
        panel.Children.Add(searchBox);

        var list = new ListBox { Background = Brushes.Transparent };
        panel.Children.Add(list);

        void FillList()
        {
            var options = _model.GetParticleLibraryOptions();
            string query = _model.ParticleLibrarySearchQuery.Trim();

            var shown = options
                .Where(o => string.IsNullOrEmpty(query) ||
                            o.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            o.ObjectType.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            list.ItemsSource = shown
                .Select(o => new ListBoxItem { Content = $"{o.Name} [{o.ObjectType}]", Tag = o.Id })
                .ToList();

            int selIdx = shown.FindIndex(o =>
                string.Equals(o.Id, _model.SelectedParticleLibraryEntryId, StringComparison.OrdinalIgnoreCase));
            if (selIdx >= 0) list.SelectedIndex = selIdx;

            if (shown.Count == 0)
            {
                list.ItemsSource = new[]
                {
                    new ListBoxItem
                    {
                        Content = options.Count == 0 ? "No object library entries available." : "No matches.",
                        IsEnabled = false
                    }
                };
            }
        }

        FillList();

        searchBox.TextChanged += (_, _) =>
        {
            _model.ParticleLibrarySearchQuery = searchBox.Text ?? "";
            FillList();
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: string id })
                _model.SelectedParticleLibraryEntryId = id;
        };
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: string id } && _model.CanSpawn())
            {
                _model.SelectedParticleLibraryEntryId = id;
                _model.TrySpawn();
            }
        };

        return panel;
    }

    // ── Shared helpers ───────────────────────────────────────────────────────

    private static TextBlock MutedText(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
        TextWrapping = TextWrapping.Wrap
    };

    private static Control LabeledNumeric(string label, int min, int max, int value, Action<int> onChanged)
    {
        var numeric = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Increment = 1,
            Value = value,
            FormatString = "0",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        numeric.ValueChanged += (_, e) =>
        {
            if (e.NewValue.HasValue)
                onChanged((int)e.NewValue.Value);
        };

        return new DockPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = Brushes.Gray,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                    [DockPanel.DockProperty] = global::Avalonia.Controls.Dock.Left
                },
                numeric
            }
        };
    }

    /// <summary>Builds a labelled source/resource-pack dropdown (index 0 = "" shown as "Default").</summary>
    private static Control BuildSourceCombo(string label, IReadOnlyList<string> options, string selected,
        Action<string> onChanged)
    {
        var combo = new ComboBox
        {
            ItemsSource = options.Select((id, i) => i == 0 ? "Default" : id).ToList(),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        int selectedIndex = 0;
        for (int i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i], selected, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                break;
            }
        }
        combo.SelectedIndex = selectedIndex;

        combo.SelectionChanged += (_, _) =>
        {
            int idx = combo.SelectedIndex;
            if (idx >= 0 && idx < options.Count)
                onChanged(options[idx]);
        };

        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        DockPanel.SetDock(labelBlock, global::Avalonia.Controls.Dock.Left);
        panel.Children.Add(labelBlock);
        panel.Children.Add(combo);
        DockPanel.SetDock(panel, global::Avalonia.Controls.Dock.Top);
        return panel;
    }

    // ── Tile thumbnails ──────────────────────────────────────────────────────

    private static WriteableBitmap? GetTileThumbnail(string key, bool isItemAtlas)
    {
        string cacheKey = (isItemAtlas ? "item:" : "block:") + key;
        if (TileThumbCache.TryGetValue(cacheKey, out var cached))
            return cached;

        byte[]? pixels;
        int width;
        int height;

        if (isItemAtlas)
        {
            ItemsAtlas.TilePixels.TryGetValue(key, out pixels);
            ItemsAtlas.TryGetTileDimensions(key, out width, out height);
        }
        else
        {
            TerrainAtlas.TilePixels.TryGetValue(key, out pixels);
            width = height = 0;
        }

        if (pixels == null || pixels.Length < 4)
            return null;

        // Infer square dimensions when the atlas doesn't track them explicitly.
        if (width <= 0 || height <= 0 || pixels.Length < width * height * 4)
        {
            int side = (int)Math.Sqrt(pixels.Length / 4.0);
            if (side <= 0 || side * side * 4 != pixels.Length)
                return null;
            width = height = side;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);

        using (var fb = bitmap.Lock())
        {
            int srcStride = width * 4;
            for (int y = 0; y < height; y++)
                Marshal.Copy(pixels, y * srcStride, fb.Address + y * fb.RowBytes, srcStride);
        }

        TileThumbCache[cacheKey] = bitmap;
        return bitmap;
    }

    // ── Schematic retry prompt ───────────────────────────────────────────────

    private void OnSchematicRetryPromptChanged()
    {
        if (!_model.SchematicRetryPending)
        {
            _retryDialog?.Close();
            _retryDialog = null;
            return;
        }

        if (_retryDialog != null)
            return; // already showing; error text updates on retry failure below

        var message = new TextBlock
        {
            Text = $"Failed to load schematic '{_model.SchematicRetryFileName}'.\n" +
                   $"{_model.SchematicRetryError}\n\nDo you want to try loading it again?",
            TextWrapping = TextWrapping.Wrap
        };

        var retryButton = new Button { Content = "Retry", Width = 120 };
        var cancelButton = new Button { Content = "Cancel", Width = 120 };

        var dialog = new Window
        {
            Title = "Schematic Load Failed",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(14),
                Spacing = 12,
                Children =
                {
                    message,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { retryButton, cancelButton }
                    }
                }
            }
        };

        retryButton.Click += (_, _) =>
        {
            if (!_model.RetrySchematicLoad())
                message.Text = $"Failed to load schematic '{_model.SchematicRetryFileName}'.\n" +
                               $"{_model.SchematicRetryError}\n\nDo you want to try loading it again?";
        };
        cancelButton.Click += (_, _) => _model.CancelSchematicRetry();
        dialog.Closed += (_, _) =>
        {
            _retryDialog = null;
            if (_model.SchematicRetryPending)
                _model.CancelSchematicRetry();
        };

        _retryDialog = dialog;
        dialog.ShowDialog(this);
    }
}
