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

    private bool _visible;
    private string _lastSignature = "";

    public void SetVisible(bool visible)
    {
        _overlay.SetProperty("display", visible ? "flex" : "none");
        if (visible && !_visible)
        {
            // Only (re)build when actually transitioning into view - rebuilding every frame
            // (as Update() used to do unconditionally) tore down and recreated the button
            // elements 60+ times a second, which meant a mousedown's target element was
            // routinely gone by the time the matching mouseup arrived, so RmlUi's click
            // detection never fired and none of the home-screen buttons appeared clickable.
            Build();
        }
        _visible = visible;
    }

    /// <summary>Rebuilds the recent-projects list while the overlay is on screen, so removing a
    /// recent entry or opening a project elsewhere is reflected without needing to hide/show.
    /// Only rebuilds when the underlying project state actually changed, to avoid recreating
    /// (and thus un-clicking) the home screen's buttons on every frame.</summary>
    public void Update()
    {
        if (!_visible) return;
        string signature = BuildSignature();
        if (signature == _lastSignature) return;
        Build();
    }

    private string BuildSignature()
    {
        var sb = new StringBuilder();
        sb.Append(_projects.HasProject ? _projects.ProjectFilePath : "none");
        foreach (RecentProjectEntry recent in _projects.GetRecentProjects())
            sb.Append('|').Append(recent.ProjectFilePath).Append(':').Append(recent.LastOpenedUtc);
        return sb.ToString();
    }

    private void Build()
    {
        // Note: SetInnerRml() only parses element markup for the fragment being inserted -
        // unlike a document's <head>, a <style> tag embedded here isn't picked up as a
        // stylesheet by RmlUi, so it would just render as raw literal text. All of the
        // home-overlay's CSS lives in EditorShell's document-level <style> block instead.
        var html = new StringBuilder();

        string splashStyle = File.Exists(SplashImagePath)
            ? "decorator: image(&quot;" + Escape(ToCssUrl(SplashImagePath)) + "&quot;);"
            : "";
        html.Append("<div id='home-splash' style='").Append(splashStyle).Append("'>")
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
                    html.Append("<div class='recent-thumb' style='decorator: image(&quot;").Append(Escape(ToCssUrl(recent.ThumbnailPath))).Append("&quot;);'/>");
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
            // Don't call Build() synchronously from within this click handler: RmlUi is still
            // walking the event-bubble chain on the very elements SetInnerRml() would destroy,
            // which corrupted the native element tree and made the home screen unresponsive
            // after a few removals. Just mutate the recent list here and let Update() pick up
            // the change (via the signature check) on the next frame instead.
            Bind($"recent-remove-{i}", () => _projects.RemoveRecentProject(path));
        }

        _lastSignature = BuildSignature();
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

    // Elements returned by GetElementById are cached by the RmlUi wrapper keyed on their native
    // pointer. Since Build() tears down and recreates the whole overlay via SetInnerRml every
    // rebuild, a freed native element's pointer address can get reused for a new element - and
    // when that happens, GetElementById hands back the *stale* cached wrapper, whose click
    // handler dictionary already thinks a listener is registered, so the real native listener
    // never gets attached to the new element. Using a fresh EventListener per bind (instead of
    // the Action<Event> overload, which relies on that cached per-wrapper dictionary) sidesteps
    // this entirely: it always performs the actual native registration.
    private void Bind(string id, Action action) => _root.GetElementById(id)?.AddEventListener("click", new ClickListener(action));

    private sealed class ClickListener(Action action) : EventListener
    {
        public override void ProcessEvent(Event ev) => action();
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>Converts a filesystem path to a CSS url()-safe form: forward slashes so RmlUi's
    /// CSS parser doesn't choke on backslash escape sequences or the drive-letter colon.</summary>
    private static string ToCssUrl(string path) => (path ?? string.Empty).Replace('\\', '/');
}
