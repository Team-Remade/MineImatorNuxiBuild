using MineImatorSimplyRemade.core.ui.Panels;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>
/// Retained-mode replacement for <see cref="Menubar"/>'s ImGui rendering. Hosts its own
/// <see cref="RmlWindowHost"/> and draws only the top menu strip; everything below it is
/// left untouched so it can be composited on top of the still-ImGui-driven main window
/// while the rest of the editor shell migration is in progress.
/// </summary>
public sealed class RmlMenubarView : IDisposable
{
    public const float Height = 30f;

    private readonly RmlWindowHost _host;

    public RmlMenubarView(RmlWindowHost host, Menubar menu)
    {
        _host = host;
        ElementDocument document = host.LoadDocument(DocumentRml);

        Bind(document, "new-project", menu.NewProjectRequested);
        Bind(document, "open-project", menu.OpenProjectRequested);
        Bind(document, "open-recent", menu.OpenRecentRequested);
        Bind(document, "save-project", menu.SaveProjectRequested);
        Bind(document, "save-as", menu.SaveProjectAsRequested);
        Bind(document, "import-asset", menu.ImportAssetRequested);
        Bind(document, "import-pack", menu.ImportResourcePackRequested);
        Bind(document, "import-pack-folder", menu.ImportResourcePackFolderRequested);
        Bind(document, "exit", menu.ExitRequested);

        Bind(document, "undo", menu.UndoRequested);
        Bind(document, "redo", menu.RedoRequested);
        Bind(document, "duplicate", menu.DuplicateRequested);
        Bind(document, "delete", menu.DeleteRequested);
        Bind(document, "preferences", menu.PreferencesRequested);

        Bind(document, "render-image", () => menu.RenderRequested?.Invoke(Menubar.RenderRequestKind.Image));
        Bind(document, "render-video", () => menu.RenderRequested?.Invoke(Menubar.RenderRequestKind.Video));

        Bind(document, "reset-layout", menu.ResetLayoutRequested);
        Bind(document, "reset-camera", menu.ResetWorkCameraRequested);
        Bind(document, "home", menu.HomeScreenRequested);

        Bind(document, "updates", menu.CheckForUpdatesRequested);
        Bind(document, "about", menu.AboutRequested);
        Bind(document, "bugs", menu.ReportBugsRequested);
        Bind(document, "forums", menu.VisitForumsRequested);
        Bind(document, "support", menu.SupportUsRequested);
    }

    private static void Bind(ElementDocument document, string id, Action? action)
    {
        if (action == null) return;
        document.GetElementById(id)?.AddEventListener("click", _ => action());
    }

    /// <summary>Renders the RmlUi menu strip. Assumes this window's GL context is already current.
    /// The host context spans the full window (not just the menu strip) so hover dropdowns
    /// aren't clipped; everything below the strip stays transparent.</summary>
    public void Render(int windowWidth, int windowHeight) => _host.Render(windowWidth, windowHeight);

    public void Dispose() => _host.Dispose();

    private const string DocumentRml = """
        <rml>
        <head><style>
          * { box-sizing: border-box; }
          body { width: 100%; margin: 0; font-family: "Noto Sans"; font-size: 13px; color: #dedfe4; }
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
        </body></rml>
        """;
}
