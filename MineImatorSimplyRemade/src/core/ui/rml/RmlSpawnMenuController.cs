using System.Net;
using System.Text;
using MineImatorSimplyRemade.core.ui.Panels;
using NativeFileDialogSharp;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Retained-mode spawn catalog backed by the existing scene creation API.</summary>
public sealed class RmlSpawnMenuController
{
    private readonly Element _overlay;
    private readonly Element _root;
    private readonly SpawnMenu _menu;
    private string _category = "Primitives";
    private string _search = string.Empty;
    private string? _object;
    private int _variant = -1;
    private ItemAtlasSource _itemAtlas = ItemAtlasSource.ItemAtlas;
    private string _sourceId = string.Empty;
    private bool _itemExtruded = true;
    private string _error = string.Empty;

    public RmlSpawnMenuController(Element overlay, Element root, SpawnMenu menu)
    {
        _overlay = overlay;
        _root = root;
        _menu = menu;
        Build();
    }

    public void Toggle()
    {
        bool visible = _overlay.GetProperty("display") != "flex";
        _overlay.SetProperty("display", visible ? "flex" : "none");
        if (visible) Build();
    }

    private void Build()
    {
        if (_category == "Items") { BuildItems(); return; }
        if (_category == "Particle Spawners") { BuildParticles(); return; }
        if (_category is "Scenery" or "Custom Models") { BuildFileLoader(); return; }

        IReadOnlyList<string> objects = _menu.GetSpawnObjects(_category, _search);
        if (_object != null && !objects.Contains(_object)) _object = null;
        IReadOnlyList<string> variants = _object == null
            ? Array.Empty<string>()
            : _menu.GetSpawnVariants(_category, _object);
        if (_variant >= variants.Count) _variant = -1;

        var html = new StringBuilder("""
            <style>
            #spawn-search{display:block;width:100%;height:30px;margin-bottom:6px;background:#17181d;color:#eee;border:1px #50535f;}
            .spawn-columns{display:flex;flex-direction:row;position:absolute;top:42px;bottom:48px;left:8px;right:8px;}
            .spawn-column{flex:1;overflow:auto;margin-right:6px;background:#191a1f;border:1px #393b44;padding:4px;}
            .spawn-entry{display:block;width:100%;text-align:left;margin-bottom:2px;padding:6px;}
            .spawn-entry.selected{background:#46516b}.spawn-footer{position:absolute;bottom:0;left:0;right:0;height:44px;padding:7px;text-align:right;border-top:1px #111216;}
            .spawn-footer button{background:#343640;border:1px #555865;margin-left:5px;}
            </style>
            """);
        html.Append("<input id='spawn-search' value='").Append(Escape(_search)).Append("' placeholder='Search objects...'/><div class='spawn-columns'><div class='spawn-column'>");
        AppendButtons(html, "category", _menu.GetSpawnCategories(), _category);
        html.Append("</div><div class='spawn-column'>");
        AppendButtons(html, "object", objects, _object);
        html.Append("</div><div class='spawn-column'>");
        AppendButtons(html, "variant", variants, _variant >= 0 ? variants[_variant] : null);
        html.Append("</div></div><div class='spawn-footer'><button id='spawn-cancel'>Cancel</button><button id='spawn-confirm'>Spawn</button></div>");
        _root.SetInnerRml(html.ToString());

        if (_root.GetElementById("spawn-search") is ElementFormControlInput search)
            search.AddEventListener("change", _ => { _search = search.GetValue(); _object = null; _variant = -1; Build(); });
        Bind("spawn-back", BackToCategories); Bind("spawn-cancel", Toggle);
        Bind("spawn-confirm", Spawn);
        BindChoices("category", _menu.GetSpawnCategories(), value => { _category = value; _object = null; _variant = -1; Build(); });
        BindChoices("object", objects, value => { _object = value; _variant = -1; Build(); });
        BindChoices("variant", variants, value => { _variant = variants.ToList().IndexOf(value); Build(); });
    }

    private void BuildItems()
    {
        IReadOnlyList<string> tiles = _menu.GetItemTiles(_itemAtlas, _sourceId, _search);
        IReadOnlyList<string> sources = _menu.GetItemSourceIds();
        if (_object != null && !tiles.Contains(_object)) _object = null;
        var html = BeginSpecial("Item tiles");
        html.Append("<div class='spawn-columns'><div class='spawn-column'><button id='item-atlas' class='spawn-entry'>Atlas: ")
            .Append(_itemAtlas == ItemAtlasSource.ItemAtlas ? "Items" : "Blocks").Append("</button><button id='item-mode' class='spawn-entry'>Mode: ")
            .Append(_itemExtruded ? "3D extruded" : "Flat").Append("</button><div style='margin:8px 4px;color:#9296a2'>Sources</div>");
        AppendButtons(html, "source", sources.Select(DisplaySource).ToArray(), DisplaySource(_sourceId));
        html.Append("</div><div class='spawn-column'>");
        AppendButtons(html, "tile", tiles, _object);
        html.Append("</div><div class='spawn-column'><div style='padding:8px'>").Append(_object == null ? "Select a tile." : Escape(_object))
            .Append("</div></div></div>");
        AppendFooter(html, _object != null);
        _root.SetInnerRml(html.ToString());
        BindSearch(); Bind("item-atlas", () => { _itemAtlas = _itemAtlas == ItemAtlasSource.ItemAtlas ? ItemAtlasSource.BlockAtlas : ItemAtlasSource.ItemAtlas; _object = null; Build(); });
        Bind("item-mode", () => { _itemExtruded = !_itemExtruded; Build(); });
        BindChoices("source", sources.Select(DisplaySource).ToArray(), value => { _sourceId = ParseSource(value); _object = null; Build(); });
        BindChoices("tile", tiles, value => { _object = value; Build(); });
        BindCommon(() => _object != null && _menu.TrySpawnItemSelection(_object, _itemAtlas, _itemExtruded));
    }

    private void BuildParticles()
    {
        var options = _menu.GetParticleSources(_search);
        var html = BeginSpecial("Particle source");
        html.Append("<div class='spawn-columns'><div class='spawn-column'>");
        AppendButtons(html, "particle", options.Select(static option => option.Name).ToArray(),
            options.FirstOrDefault(option => option.Id == _object).Name);
        html.Append("</div><div class='spawn-column'><div style='padding:8px'>A source is optional; it can also be assigned later in Properties.</div></div></div>");
        AppendFooter(html, true); _root.SetInnerRml(html.ToString()); BindSearch();
        BindChoices("particle", options.Select(static option => option.Name).ToArray(), name => { _object = options.First(option => option.Name == name).Id; Build(); });
        BindCommon(() => _menu.TrySpawnParticleSelection(_object ?? string.Empty));
    }

    private void BuildFileLoader()
    {
        bool schematic = _category == "Scenery";
        var html = BeginSpecial(schematic ? "Load schematic" : "Load custom model");
        html.Append("<div style='padding:18px'><p>").Append(schematic ? "Load a .schem or .schematic file." : "Load a supported Mine-imator model file.").Append("</p>");
        if (schematic) { html.Append("<p>Resource pack</p>"); AppendButtons(html, "source", _menu.GetResourcePackIds(true).Select(DisplaySource).ToArray(), DisplaySource(_sourceId)); }
        if (!string.IsNullOrWhiteSpace(_error)) html.Append("<p style='color:#ff8d8d'>").Append(Escape(_error)).Append("</p>");
        html.Append("<button id='choose-file' class='spawn-entry'>Choose file...</button></div>"); AppendFooter(html, false); _root.SetInnerRml(html.ToString());
        if (schematic) BindChoices("source", _menu.GetResourcePackIds(true).Select(DisplaySource).ToArray(), value => { _sourceId = ParseSource(value); Build(); });
        Bind("choose-file", () => { var result = Dialog.FileOpen(schematic ? "schem,schematic" : "mimodel,miobject"); if (!result.IsOk) return;
            bool ok = schematic ? _menu.TrySpawnSchematic(result.Path, _sourceId, out _error) : _menu.TrySpawnCustomModel(result.Path);
            if (ok) Toggle(); else { if (!schematic) _error = "The model could not be loaded."; Build(); } });
        Bind("spawn-cancel", Toggle);
    }

    private StringBuilder BeginSpecial(string placeholder) => new($$"""
      <style>#spawn-search{display:block;width:100%;height:30px;margin-bottom:6px;background:#17181d;color:#eee;border:1px #50535f;}.spawn-columns{display:flex;flex-direction:row;position:absolute;top:42px;bottom:48px;left:8px;right:8px;}.spawn-column{flex:1;overflow:auto;margin-right:6px;background:#191a1f;border:1px #393b44;padding:4px;}.spawn-entry{display:block;width:100%;text-align:left;margin-bottom:2px;padding:6px;}.spawn-entry.selected{background:#46516b}.spawn-footer{position:absolute;bottom:0;left:0;right:0;height:44px;padding:7px;text-align:right;border-top:1px #111216;}.spawn-footer button{background:#343640;border:1px #555865;margin-left:5px;}</style><input id='spawn-search' value='{{Escape(_search)}}' placeholder='{{placeholder}}...'/>
      """);
    private static void AppendFooter(StringBuilder html, bool canSpawn) => html.Append("<div class='spawn-footer'><button id='spawn-back'>Categories</button><button id='spawn-cancel'>Cancel</button>").Append(canSpawn ? "<button id='spawn-confirm'>Spawn</button>" : "").Append("</div>");
    private void BindSearch() { if (_root.GetElementById("spawn-search") is ElementFormControlInput search) search.AddEventListener("change", _ => { _search = search.GetValue(); Build(); }); }
    private void BindCommon(Func<bool> spawn) { Bind("spawn-back", BackToCategories); Bind("spawn-cancel", Toggle); Bind("spawn-confirm", () => { if (spawn()) Toggle(); else { _error = "The object could not be spawned."; Build(); } }); }
    private void BackToCategories() { _category = "Primitives"; _object = null; _variant = -1; _search = string.Empty; _error = string.Empty; Build(); }
    private static string DisplaySource(string value) => string.IsNullOrWhiteSpace(value) ? "Default / vanilla" : value;
    private static string ParseSource(string value) => value == "Default / vanilla" ? string.Empty : value;

    private void Spawn()
    {
        if (_object != null && _menu.TrySpawnSelection(_category, _object, _variant)) Toggle();
    }

    private void BindChoices(string prefix, IReadOnlyList<string> values, Action<string> select)
    {
        for (int i = 0; i < values.Count; i++) { string value = values[i]; Bind($"{prefix}-{i}", () => select(value)); }
    }

    private static void AppendButtons(StringBuilder html, string prefix, IReadOnlyList<string> values, string? selected)
    {
        for (int i = 0; i < values.Count; i++) html.Append("<button id='").Append(prefix).Append('-').Append(i).Append("' class='spawn-entry")
            .Append(values[i] == selected ? " selected" : "").Append("'>").Append(Escape(values[i])).Append("</button>");
    }

    private void Bind(string id, Action action) => _root.GetElementById(id)?.AddEventListener("click", _ => action());
    private static string Escape(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
