using System.Net;
using System.Text;
using MineImatorSimplyRemade.core.project;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Retained-mode replacement for MainWindow's ImGui-drawn project home/splash screen
/// (see RenderProjectHomeScreen in the old ImGui MainWindow).</summary>
public sealed class RmlHomeController
{
    private static readonly string SplashImagePath = Path.Combine(AppContext.BaseDirectory, "data/splashes", "splash.png");
    private static readonly string SplashCreditPath = Path.Combine(AppContext.BaseDirectory, "data/splashes", "credit.txt");
    private static readonly string SplashTextPath = Path.Combine(AppContext.BaseDirectory, "data/splashes", "splash.txt");

    private readonly Element _overlay;
    private readonly Element _root;
    private readonly ProjectManager _projects;
    private readonly List<string> _splashPool = new();
    private string _splashText = "Splash Screen Placeholder";
    private string _splashCredit = "(unassigned)";

    public Action? NewProjectRequested { get; set; }
    public Action? LoadProjectRequested { get; set; }
    public Action<string>? OpenRecentRequested { get; set; }

    public RmlHomeController(Element overlay, Element root, ProjectManager projects)
    {
        _overlay = overlay;
        _root = root;
        _projects = projects;
        LoadSplashes();
        PickRandomSplash();
    }

    public void SetVisible(bool visible)
    {
        _overlay.SetProperty("display", visible ? "flex" : "none");
        if (visible) Build();
    }

    /// <summary>Rebuilds the recent-projects list while the overlay is on screen, so removing a
    /// recent entry or opening a project elsewhere is reflected without needing to hide/show.</summary>
    public void Update()
    {
        if (_overlay.GetProperty("display") == "flex") Build();
    }

    private void Build()
    {
        var html = new StringBuilder("""
            <style>
            #home-splash{position:relative;height:180px;margin-bottom:12px;background:#293038 no-repeat center/cover;border:1px #566072;border-radius:18px;overflow:hidden;}
            #home-splash-title{position:absolute;top:24px;left:24px;padding:6px 10px;background:#0d0f14aa;color:#eaf0f8;font-weight:bold;font-size:18px;border-radius:8px;}
            #home-splash-text{position:absolute;top:56px;left:24px;padding:5px 10px;background:#0d0f14aa;color:#b3c0d1;border-radius:8px;}
            #home-splash-credit{color:#7d818c;margin-bottom:8px;}
            .home-columns{display:flex;flex-direction:row;}
            #home-actions{width:34%;margin-right:10px;background:#191a1f;border:1px #393b44;padding:12px;}
            #home-actions button{display:block;width:100%;padding:9px;margin-bottom:6px;background:#30323a;border:1px #50525e;}
            #home-current{color:#8b8f9b;margin-top:10px;}
            #home-recent{flex:1;background:#191a1f;border:1px #393b44;padding:8px;overflow:auto;}
            .recent-grid{display:flex;flex-direction:row;flex-wrap:wrap;}
            .recent-card{width:200px;margin:4px;padding:8px;background:#202127;border:1px #393b44;}
            .recent-thumb{display:block;width:100%;height:96px;background:#2c2e36 no-repeat center/cover;border:1px #454854;margin-bottom:6px;}
            .recent-card .name{color:#dedfe4;}
            .recent-card .path,.recent-card .date{color:#83879169;color:#83879a;}
            .recent-card button{width:100%;margin-top:4px;background:#343640;border:1px #555865;}
            .recent-missing{color:#eb9271;}
            </style>
            """);

        html.Append("<div id='home-splash' style='background-image:url(").Append(Escape(SplashImagePath)).Append(");'>")
            .Append("<div id='home-splash-title'>Mine Imator Simply Remade</div>")
            .Append("<div id='home-splash-text'>").Append(Escape(_splashText)).Append("</div></div>")
            .Append("<div id='home-splash-credit'>Splash art credits: ").Append(Escape(_splashCredit)).Append("</div>")
            .Append("<div class='home-columns'>");

        html.Append("<div id='home-actions'><div>Create a fresh project, load an existing one, or reopen something recent.</div>")
            .Append("<button id='home-new'>New Project</button><button id='home-open'>Load Project</button>");
        if (_projects.HasProject)
        {
            html.Append("<div id='home-current'>Current project<br/>").Append(Escape(_projects.Manifest.ProjectName))
                .Append("<br/>").Append(Escape(_projects.ProjectFilePath)).Append("</div>");
        }
        else
        {
            html.Append("<div id='home-current'>No project currently open.</div>");
        }
        html.Append("</div>");

        html.Append("<div id='home-recent'><div>Recent Projects</div><div class='recent-grid'>");
        IReadOnlyList<RecentProjectEntry> recents = _projects.GetRecentProjects();
        if (recents.Count == 0)
        {
            html.Append("<div>No recent projects yet.</div>");
        }
        else
        {
            for (int i = 0; i < recents.Count; i++)
            {
                RecentProjectEntry recent = recents[i];
                bool exists = File.Exists(recent.ProjectFilePath);
                html.Append("<div class='recent-card'>");
                if (!string.IsNullOrWhiteSpace(recent.ThumbnailPath) && File.Exists(recent.ThumbnailPath))
                    html.Append("<div class='recent-thumb' style='background-image:url(").Append(Escape(recent.ThumbnailPath)).Append(");'/>");
                else
                    html.Append("<div class='recent-thumb'/>");
                html.Append("<div class='name'>").Append(Escape(recent.ProjectName)).Append("</div>")
                    .Append("<div class='path'>").Append(Escape(Path.GetFileName(recent.ProjectFilePath))).Append("</div>")
                    .Append("<div class='date'>").Append(Escape(recent.LastOpenedUtc)).Append("</div>");
                if (exists)
                    html.Append("<button id='recent-open-").Append(i).Append("'>Open</button>");
                else
                    html.Append("<div class='recent-missing'>Missing from disk</div>");
                html.Append("<button id='recent-remove-").Append(i).Append("'>Remove From Recent</button>");
                html.Append("</div>");
            }
        }
        html.Append("</div></div></div>");

        _root.SetInnerRml(html.ToString());

        Bind("home-new", () => NewProjectRequested?.Invoke());
        Bind("home-open", () => LoadProjectRequested?.Invoke());
        for (int i = 0; i < recents.Count; i++)
        {
            string path = recents[i].ProjectFilePath;
            Bind($"recent-open-{i}", () => OpenRecentRequested?.Invoke(path));
            Bind($"recent-remove-{i}", () => { _projects.RemoveRecentProject(path); Build(); });
        }
    }

    private void LoadSplashes()
    {
        try
        {
            if (File.Exists(SplashTextPath))
            {
                foreach (string raw in File.ReadAllLines(SplashTextPath))
                {
                    string line = raw.Trim();
                    if (!string.IsNullOrWhiteSpace(line)) _splashPool.Add(StripDecorations(line));
                }
            }
            if (File.Exists(SplashCreditPath))
            {
                string credit = File.ReadAllText(SplashCreditPath).Trim();
                if (!string.IsNullOrWhiteSpace(credit)) _splashCredit = credit;
            }
        }
        catch
        {
            // Keep fallback splash text/credit if loading fails.
        }
    }

    private void PickRandomSplash()
    {
        _splashText = _splashPool.Count > 0
            ? _splashPool[Random.Shared.Next(_splashPool.Count)]
            : "Splash Screen Placeholder";
    }

    /// <summary>Drops the ImGui-only "~strike~" / "*italic*" markers; RmlUi rendering of the
    /// splash line doesn't need the rich segments the old drawlist-based renderer used.</summary>
    private static string StripDecorations(string source) => source.Replace("~", "").Replace("*", "");

    private void Bind(string id, Action action) => _root.GetElementById(id)?.AddEventListener("click", _ => action());
    private static string Escape(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
