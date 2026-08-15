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
    private readonly Context _context;

    public EditorShell(RmlWindowHost host, Menubar menu)
    {
        _document = host.LoadDocument(DocumentRml);
        _context = host.Context;
        Bind("open-project", menu.OpenProjectRequested);
        Bind("open-recent", menu.OpenRecentRequested);
        Bind("save-project", menu.SaveProjectRequested);
        Bind("import-asset", menu.ImportAssetRequested);
        // "import-pack" / "import-pack-folder" are bound to RmlResourcePackImportController in RmlEditorController.
        Bind("exit", menu.ExitRequested);
        Bind("undo", menu.UndoRequested);
        Bind("redo", menu.RedoRequested);
        Bind("duplicate", menu.DuplicateRequested);
        Bind("delete", menu.DeleteRequested);
        // "render-image" / "render-video" are bound to RmlRenderController.Show(...) in RmlEditorController.
        Bind("reset-layout", menu.ResetLayoutRequested);
        Bind("reset-camera", menu.ResetWorkCameraRequested);
        Bind("home", menu.HomeScreenRequested);
        Bind("bugs", menu.ReportBugsRequested);
        Bind("forums", menu.VisitForumsRequested);
        Bind("support", menu.SupportUsRequested);
        WireMenuToggles();
    }

    public Element? GetRegionElement(string id) => _document.GetElementById(id);

    /// <summary>True while a text input/textarea in this document currently has keyboard focus.</summary>
    public bool HasFocusedTextInput => _context.GetFocusElement() is ElementFormControlInput or ElementFormControlTextArea;

    public void BindCommand(string id, Action action) => Bind(id, action);

    public Region GetRegion(string id)
    {
        Element? element = GetRegionElement(id);
        return element == null
            ? default
            : new Region(element.GetAbsoluteLeft(), element.GetAbsoluteTop(),
                element.GetClientWidth(), element.GetClientHeight());
    }

    public void SetStatus(string text) => _document.GetElementById("status-text")?.SetInnerRml(Escape(text));

    private void Bind(string id, Action? action)
    {
        if (action == null) return;
        _document.GetElementById(id)?.AddEventListener("click", _ => action());
    }

    /// <summary>Menu dropdowns used to open on plain CSS `:hover`, which also meant the mouse
    /// leaving the 1px gap between the menubar and its dropdown while moving down would close
    /// the menu before a click could land on an item. Wires up click-to-open/click-away-to-close
    /// instead: each top-level menu button toggles its own ".menu" wrapper's "open" class (see
    /// ".menu.open .menu-items" in DocumentRml) and stops the click from bubbling further, while
    /// any other click anywhere in the document (menu items included, so choosing a command
    /// closes the menu too) closes every open menu.</summary>
    private void WireMenuToggles()
    {
        string[] menuButtonIds = ["menu-file", "menu-edit", "menu-render", "menu-view", "menu-help"];
        List<Element> menus = new();
        // Tracked separately from the "open" CSS class (rather than re-reading it back off the
        // element) since AddClass/RemoveClass don't necessarily keep the "class" attribute string
        // in sync, so it can't be relied on as a source of truth for the current open/closed state.
        List<bool> openState = new();
        foreach (string id in menuButtonIds)
        {
            Element? button = _document.GetElementById(id);
            Element? menu = button?.GetParentNode();
            if (button == null || menu == null) continue;
            int index = menus.Count;
            menus.Add(menu);
            openState.Add(false);
            button.AddEventListener("click", e =>
            {
                bool wasOpen = openState[index];
                for (int i = 0; i < menus.Count; i++)
                {
                    menus[i].RemoveClass("open");
                    openState[i] = false;
                }
                if (!wasOpen)
                {
                    menu.AddClass("open");
                    openState[index] = true;
                }
                e.StopPropagation();
            });
        }
        _document.AddEventListener("click", _ =>
        {
            for (int i = 0; i < menus.Count; i++)
            {
                menus[i].RemoveClass("open");
                openState[i] = false;
            }
        });
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
          .menu.open .menu-items { display: block; }
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
          #center { flex: 1; min-width: 260px; display: flex; flex-direction: column; }
          #viewport { flex: 1; position: relative; background: #101115; overflow: hidden; }
          #viewport-surface { position: absolute; top: 0; bottom: 0; left: 0; right: 0; overflow: hidden; }
          #viewport-tools { height: 31px; display: flex; align-items: center; padding: 3px; background: #292a31; }
          #viewport-tools button { background: #33353d; border: 1px #4c4f5a; margin-right: 4px; }
          #spawn-object { margin-left: auto; margin-right: 0; display: flex; align-items: center; }
          #spawn-object img { width: 16px; height: 16px; margin-right: 5px; }
          #viewport-image { position: absolute; top: 31px; bottom: 0; left: 0; right: 0; width: 100%; height: 100%; }
          #right-column { width: 285px; min-width: 190px; display: flex; flex-direction: column; }
          #scene-tree { flex: 1; min-height: 120px; position: relative; border-bottom: 1px #111216; }
          #properties { flex: 1; min-height: 120px; position: relative; }
          #bottom-row { height: 225px; min-height: 100px; display: flex; flex-direction: row; }
          #content-browser { width: 300px; min-width: 150px; position: relative; border-right: 1px #111216; }
          #timeline { flex: 1; min-width: 200px; position: relative; }
          #statusbar { position: absolute; height: 23px; bottom: 0; left: 0; right: 0; padding: 3px 8px;
                       background: #25262c; border-top: 1px #111216; color: #9295a0; }
          #home-overlay { position: absolute; top: 0; bottom: 0; left: 0; right: 0; display: none;
                          flex-direction: column; padding: 24px; background: #191a1f; z-index: 10; }
          #home-splash{position:relative;height:180px;margin-bottom:12px;background:#293038;border:1px #566072;border-radius:18px;overflow:hidden;}
          #home-splash-title{position:absolute;top:24px;left:24px;padding:6px 10px;background:#0d0f14aa;color:#eaf0f8;font-weight:bold;font-size:18px;border-radius:8px;}
          #home-splash-text{position:absolute;top:56px;left:24px;padding:5px 10px;background:#0d0f14aa;color:#b3c0d1;border-radius:8px;}
          #home-splash-credit{color:#7d818c;margin-bottom:8px;}
          .home-columns{display:flex;flex-direction:row;}
          #home-actions{width:34%;margin-right:10px;background:#191a1f;border:1px #393b44;padding:12px;}
          #home-actions button{display:block;width:100%;padding:9px;margin-bottom:6px;background:#30323a;border:1px #50525e;}
          #home-actions button:hover{background:#3b3d46;}
          #home-current{color:#8b8f9b;margin-top:10px;}
          #home-recent{flex:1;background:#191a1f;border:1px #393b44;padding:8px;overflow:auto;}
          .recent-grid{display:flex;flex-direction:row;flex-wrap:wrap;}
          .recent-card{width:260px;margin:4px;padding:8px;background:#202127;border:1px #393b44;overflow:hidden;}
          .recent-thumb{display:block;width:100%;height:120px;background:#2c2e36;border:1px #454854;margin-bottom:6px;}
          .recent-card .name{color:#dedfe4;white-space:nowrap;overflow:hidden;}
          .recent-card .path,.recent-card .date{color:#83879a;white-space:nowrap;overflow:hidden;}
          .recent-card button{width:100%;margin-top:4px;padding:6px 4px;background:#343640;border:1px #555865;white-space:nowrap;}
          .recent-card button:hover{background:#3b3d46;}
          .recent-missing{color:#eb9271;}
          #preferences-overlay { position:absolute; top:45px; bottom:45px; left:20%; right:20%; display:none;
                                 background:#202127; border:1px #555864; z-index:30; }
          #spawn-overlay { position:absolute;top:9%;bottom:9%;left:9%;right:9%;display:none;flex-direction:column;
                           background:#202127;border:1px #555864;z-index:35; }
          #toast { position:absolute; top:20px; right:20px; width:280px; padding:12px 14px; display:none; z-index:40; }
          #toast.success { background:#1a291f; border:1px #478757; color:#b8f4c8; }
          #toast.error { background:#3d1a1a; border:1px #c24747; color:#ffd1d1; }
          #about-overlay { position:absolute; top:12%; bottom:12%; left:15%; right:15%; display:none;
                            background:#202127; border:1px #555864; z-index:32; }
          #update-overlay { position:absolute; top:12%; bottom:12%; left:15%; right:15%; display:none;
                             background:#202127; border:1px #555864; z-index:32; }
          #render-overlay { position:absolute; top:8%; bottom:8%; left:12%; right:12%; display:none;
                             background:#202127; border:1px #555864; z-index:32; }
          #import-pack-overlay { position:absolute; top:20%; bottom:20%; left:20%; right:20%; display:none;
                                  background:#202127; border:1px #555864; z-index:32; }
          #project-dialog-overlay { position:absolute; top:0; bottom:0; left:0; right:0; display:none;
                                     flex-direction:column; align-items:center; justify-content:center;
                                     background:#000000b0; z-index:33; }
          #project-dialog-body{flex-shrink:0;flex-grow:0;}
          #project-dialog-panel{width:300px;flex-shrink:0;flex-grow:0;padding:20px;background:#25262c;border:1px #454854;
                                 border-radius:8px;}
          #project-dialog-panel h3{margin:0 0 14px 0;padding-bottom:10px;color:#eaf0f8;font-size:15px;
                                    border-bottom:1px #393b44;}
          #project-dialog-panel input{width:100%;padding:7px;margin-bottom:8px;background:#191a1f;
                                       border:1px #50525e;border-radius:4px;color:#dedfe4;}
          #project-dialog-error{color:#eb9271;margin-bottom:8px;}
          #project-dialog-actions{display:flex;flex-direction:row;justify-content:flex-end;margin-top:6px;}
          #project-dialog-actions button{margin-left:6px;padding:7px 14px;background:#30323a;
                                          border:1px #50525e;border-radius:4px;}
          #project-dialog-actions button:hover{background:#3b3d46;}
          #unsaved-changes-overlay { position:absolute; top:0; bottom:0; left:0; right:0; display:none;
                                      flex-direction:column; z-index:34; }
        </style></head>
        <body>
          <div id="menubar">
            <div class="menu"><button id="menu-file">File</button><div class="menu-items">
              <button id="new-project">New Project <span class="shortcut">Ctrl+N</span></button>
              <button id="open-project">Open Project <span class="shortcut">Ctrl+O</span></button>
              <button id="open-recent">Open Recent...</button><div class="separator"/>
              <button id="save-project">Save Project <span class="shortcut">Ctrl+S</span></button>
              <button id="save-as">Save As <span class="shortcut">Ctrl+Shift+S</span></button><div class="separator"/>
              <button id="import-asset">Import Asset</button><button id="import-pack">Import Resource Pack</button>
              <button id="import-pack-folder">Import Resource Pack Folder</button><div class="separator"/>
              <button id="exit">Exit</button>
            </div></div>
            <div class="menu"><button id="menu-edit">Edit</button><div class="menu-items">
              <button id="undo">Undo <span class="shortcut">Ctrl+Z</span></button>
              <button id="redo">Redo <span class="shortcut">Ctrl+Y</span></button><div class="separator"/>
              <button id="duplicate">Duplicate <span class="shortcut">Ctrl+D</span></button>
              <button id="delete">Delete <span class="shortcut">Del</span></button><div class="separator"/>
              <button id="preferences">Preferences</button>
            </div></div>
            <div class="menu"><button id="menu-render">Render</button><div class="menu-items">
              <button id="render-image">Render Image <span class="shortcut">F7</span></button>
              <button id="render-video">Render Animation <span class="shortcut">F8</span></button>
            </div></div>
            <div class="menu"><button id="menu-view">View</button><div class="menu-items">
              <button id="reset-layout">Reset Layout</button><button id="reset-camera">Reset Work Camera</button>
              <button id="home">Home Screen</button>
            </div></div>
            <div class="menu"><button id="menu-help">Help</button><div class="menu-items">
              <button id="updates">Check for Updates</button><button id="about">About</button><div class="separator"/>
              <button id="bugs">Report Bugs</button><button id="forums">Visit the Forums</button>
              <button id="support">Support Us</button>
            </div></div>
          </div>
          <div id="workspace">
            <div id="upper">
              <div id="center"><div id="viewport" class="panel"><div id="viewport-surface"/></div></div>
              <div id="right-column">
                <div id="scene-tree" class="panel"><div class="panel-title">Scene Tree</div><div id="scene-tree-body" class="panel-body"/></div>
                <div id="properties" class="panel"><div class="panel-title">Properties</div><div id="properties-body" class="panel-body"/></div>
              </div>
            </div>
            <div id="bottom-row">
              <div id="content-browser" class="panel"><div class="panel-title">Content Browser</div><div id="content-browser-body" class="panel-body"/></div>
              <div id="timeline" class="panel"><div class="panel-title">Timeline</div><div id="timeline-body" class="panel-body"/></div>
            </div>
          </div>
          <div id="statusbar"><span id="status-text">Ready</span></div>
          <div id="home-overlay"><div id="home-body"/></div>
          <div id="preferences-overlay"><div class="panel-title">Preferences</div><div id="preferences-body" class="panel-body"/></div>
          <div id="spawn-overlay"><div class="panel-title">Add object</div><div id="spawn-body" class="panel-body"/></div>
          <div id="toast"><span id="toast-text"/></div>
          <div id="about-overlay"><div class="panel-title">About</div><div id="about-body" class="panel-body"/></div>
          <div id="update-overlay"><div class="panel-title">Check for Updates</div><div id="update-body" class="panel-body"/></div>
          <div id="render-overlay"><div class="panel-title">Render Output</div><div id="render-body" class="panel-body"/></div>
          <div id="import-pack-overlay"><div class="panel-title">Import Resource Pack</div><div id="import-pack-body" class="panel-body"/></div>
          <div id="project-dialog-overlay"><div id="project-dialog-body"/></div>
          <div id="unsaved-changes-overlay"><div id="unsaved-changes-body"/></div>
        </body></rml>
        """;
}
