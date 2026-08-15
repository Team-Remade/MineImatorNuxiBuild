using MineImatorSimplyRemade.core.ui.Panels;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>The retained-mode document that replaces ImGui's main dockspace.</summary>
public sealed class EditorShell
{
    public readonly record struct Region(float X, float Y, float Width, float Height)
    {
        public bool IsValid => Width > 0 && Height > 0;
    }

    private readonly ElementDocument _document;

    public EditorShell(RmlWindowHost host, Menubar menu)
    {
        _document = host.LoadDocument(DocumentRml);
        Bind("new-project", menu.NewProjectRequested);
        Bind("home-new", menu.NewProjectRequested);
        Bind("open-project", menu.OpenProjectRequested);
        Bind("home-open", menu.OpenProjectRequested);
        Bind("open-recent", menu.OpenRecentRequested);
        Bind("save-project", menu.SaveProjectRequested);
        Bind("save-as", menu.SaveProjectAsRequested);
        Bind("import-asset", menu.ImportAssetRequested);
        Bind("import-pack", menu.ImportResourcePackRequested);
        Bind("import-pack-folder", menu.ImportResourcePackFolderRequested);
        Bind("exit", menu.ExitRequested);
        Bind("undo", menu.UndoRequested);
        Bind("redo", menu.RedoRequested);
        Bind("duplicate", menu.DuplicateRequested);
        Bind("delete", menu.DeleteRequested);
        Bind("render-image", () => menu.RenderRequested?.Invoke(Menubar.RenderRequestKind.Image));
        Bind("render-video", () => menu.RenderRequested?.Invoke(Menubar.RenderRequestKind.Video));
        Bind("reset-layout", menu.ResetLayoutRequested);
        Bind("reset-camera", menu.ResetWorkCameraRequested);
        Bind("home", menu.HomeScreenRequested);
        Bind("updates", menu.CheckForUpdatesRequested);
        Bind("bugs", menu.ReportBugsRequested);
        Bind("forums", menu.VisitForumsRequested);
        Bind("support", menu.SupportUsRequested);
    }

    public Element? GetRegionElement(string id) => _document.GetElementById(id);

    public void BindCommand(string id, Action action) => Bind(id, action);

    public Region GetRegion(string id)
    {
        Element? element = GetRegionElement(id);
        return element == null
            ? default
            : new Region(element.GetAbsoluteLeft(), element.GetAbsoluteTop(),
                element.GetClientWidth(), element.GetClientHeight());
    }

    public void SetHomeVisible(bool visible)
    {
        _document.GetElementById("home-overlay")?.SetProperty("display", visible ? "flex" : "none");
    }

    public void SetStatus(string text) => _document.GetElementById("status-text")?.SetInnerRml(Escape(text));

    private void Bind(string id, Action? action)
    {
        if (action == null) return;
        _document.GetElementById(id)?.AddEventListener("click", _ => action());
    }

    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    private const string DocumentRml = """
        <rml>
        <head><style>
          * { box-sizing: border-box; }
          body { width: 100%; height: 100%; margin: 0; overflow: hidden; background: #17181c;
                 color: #dedfe4; font-family: "Noto Sans"; font-size: 13px; }
          button { font-family: "Noto Sans"; font-size: 13px; color: #dedfe4; background: transparent;
                   border-width: 0; padding: 5px 9px; }
          button:hover { background: #3b3d46; }
          #menubar { height: 30px; display: flex; flex-direction: row; background: #25262c;
                     border-bottom: 1px #111216; z-index: 20; }
          .menu { position: relative; }
          .menu-items { display: none; position: absolute; top: 29px; left: 0; width: 220px;
                        padding: 5px; background: #292a31; border: 1px #4b4d58; }
          .menu:hover .menu-items { display: block; }
          .menu-items button { display: block; width: 100%; text-align: left; padding: 6px 9px; }
          .shortcut { float: right; color: #898c98; }
          .separator { height: 1px; margin: 4px 3px; background: #454750; }
          #workspace { position: absolute; top: 30px; bottom: 23px; left: 0; right: 0; display: flex;
                       flex-direction: column; }
          #upper { flex: 1; min-height: 180px; display: flex; flex-direction: row; }
          .panel { background: #202127; border: 1px #111216; overflow: hidden; }
          .panel-title { height: 27px; padding: 5px 8px; color: #b9bbc4; background: #292a31;
                         border-bottom: 1px #111216; font-weight: bold; }
          .panel-body { position: absolute; top: 27px; bottom: 0; left: 0; right: 0; overflow: auto; }
          #scene-tree { width: 235px; min-width: 150px; position: relative; }
          #center { flex: 1; min-width: 260px; display: flex; flex-direction: column; }
          #viewport { flex: 1; position: relative; background: #101115; overflow: hidden; }
          #content-browser { height: 180px; min-height: 80px; position: relative; }
          #properties { width: 285px; min-width: 190px; position: relative; }
          #timeline { height: 225px; min-height: 100px; position: relative; }
          #statusbar { position: absolute; height: 23px; bottom: 0; left: 0; right: 0; padding: 3px 8px;
                       background: #25262c; border-top: 1px #111216; color: #9295a0; }
          #home-overlay { position: absolute; top: 0; bottom: 0; left: 0; right: 0; display: none;
                          flex-direction: column; align-items: center; justify-content: center;
                          background: #191a1f; z-index: 10; }
          #home-overlay h1 { color: #e5b94c; font-size: 27px; margin-bottom: 6px; }
          #home-actions { display: flex; flex-direction: row; margin-top: 16px; }
          #home-actions button { margin: 4px; padding: 9px 16px; background: #30323a; border: 1px #50525e; }
          #preferences-overlay { position:absolute; top:45px; bottom:45px; left:20%; right:20%; display:none;
                                 background:#202127; border:1px #555864; z-index:30; }
          #spawn-object { position:absolute;top:7px;left:8px;background:#343640;border:1px #555865;z-index:3; }
          #spawn-overlay { position:absolute;top:9%;bottom:9%;left:9%;right:9%;display:none;flex-direction:column;
                           background:#202127;border:1px #555864;z-index:35; }
          #toast { position:absolute; top:20px; right:20px; width:280px; padding:12px 14px; display:none; z-index:40; }
          #toast.success { background:#1a291f; border:1px #478757; color:#b8f4c8; }
          #toast.error { background:#3d1a1a; border:1px #c24747; color:#ffd1d1; }
          #about-overlay { position:absolute; top:12%; bottom:12%; left:15%; right:15%; display:none;
                            background:#202127; border:1px #555864; z-index:32; }
        </style></head>
        <body>
          <div id="menubar">
            <div class="menu"><button>File</button><div class="menu-items">
              <button id="new-project">New Project <span class="shortcut">Ctrl+N</span></button>
              <button id="open-project">Open Project <span class="shortcut">Ctrl+O</span></button>
              <button id="open-recent">Open Recent...</button><div class="separator"/>
              <button id="save-project">Save Project <span class="shortcut">Ctrl+S</span></button>
              <button id="save-as">Save As <span class="shortcut">Ctrl+Shift+S</span></button><div class="separator"/>
              <button id="import-asset">Import Asset</button><button id="import-pack">Import Resource Pack</button>
              <button id="import-pack-folder">Import Resource Pack Folder</button><div class="separator"/>
              <button id="exit">Exit</button>
            </div></div>
            <div class="menu"><button>Edit</button><div class="menu-items">
              <button id="undo">Undo <span class="shortcut">Ctrl+Z</span></button>
              <button id="redo">Redo <span class="shortcut">Ctrl+Y</span></button><div class="separator"/>
              <button id="duplicate">Duplicate <span class="shortcut">Ctrl+D</span></button>
              <button id="delete">Delete <span class="shortcut">Del</span></button><div class="separator"/>
              <button id="preferences">Preferences</button>
            </div></div>
            <div class="menu"><button>Render</button><div class="menu-items">
              <button id="render-image">Render Image <span class="shortcut">F7</span></button>
              <button id="render-video">Render Animation <span class="shortcut">F8</span></button>
            </div></div>
            <div class="menu"><button>View</button><div class="menu-items">
              <button id="reset-layout">Reset Layout</button><button id="reset-camera">Reset Work Camera</button>
              <button id="home">Home Screen</button>
            </div></div>
            <div class="menu"><button>Help</button><div class="menu-items">
              <button id="updates">Check for Updates</button><button id="about">About</button><div class="separator"/>
              <button id="bugs">Report Bugs</button><button id="forums">Visit the Forums</button>
              <button id="support">Support Us</button>
            </div></div>
          </div>
          <div id="workspace">
            <div id="upper">
              <div id="scene-tree" class="panel"><div class="panel-title">Scene Tree</div><div id="scene-tree-body" class="panel-body"/></div>
              <div id="center"><div id="viewport" class="panel"><div id="viewport-surface" class="panel-body"/><button id="spawn-object">+ Add object</button></div>
                <div id="content-browser" class="panel"><div class="panel-title">Content Browser</div><div id="content-browser-body" class="panel-body"/></div></div>
              <div id="properties" class="panel"><div class="panel-title">Properties</div><div id="properties-body" class="panel-body"/></div>
            </div>
            <div id="timeline" class="panel"><div class="panel-title">Timeline</div><div id="timeline-body" class="panel-body"/></div>
          </div>
          <div id="statusbar"><span id="status-text">Ready</span></div>
          <div id="home-overlay"><h1>Mine Imator Nuxi</h1><div>Create and animate Minecraft worlds.</div>
            <div id="home-actions"><button id="home-new">New Project</button><button id="home-open">Open Project</button></div></div>
          <div id="preferences-overlay"><div class="panel-title">Preferences</div><div id="preferences-body" class="panel-body"/></div>
          <div id="spawn-overlay"><div class="panel-title">Add object</div><div id="spawn-body" class="panel-body"/></div>
          <div id="toast"><span id="toast-text"/></div>
          <div id="about-overlay"><div class="panel-title">About</div><div id="about-body" class="panel-body"/></div>
        </body></rml>
        """;
}
