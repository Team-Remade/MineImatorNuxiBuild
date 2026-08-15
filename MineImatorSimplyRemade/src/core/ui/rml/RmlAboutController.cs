using System.Diagnostics;
using System.Net;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Retained-mode replacement for MainWindow's ImGui-drawn About dialog
/// (see OpenAboutPopup/RenderAboutPopup in the old ImGui MainWindow).</summary>
public sealed class RmlAboutController
{
    private const string DonateUrl = "https://ko-fi.com/forestw";
    private const string DiscordInviteUrl = "https://discord.gg/eswvppFuAD";

    private readonly Element _overlay;
    private readonly Element _root;
    private readonly string _version;
    public bool Visible { get; private set; }

    public RmlAboutController(Element overlay, Element root, string version)
    {
        _overlay = overlay;
        _root = root;
        _version = version;
        Refresh();
    }

    public void Toggle()
    {
        Visible = !Visible;
        _overlay.SetProperty("display", Visible ? "block" : "none");
    }

    public void Show()
    {
        Visible = true;
        _overlay.SetProperty("display", "block");
    }

    public void Hide()
    {
        Visible = false;
        _overlay.SetProperty("display", "none");
    }

    private void Refresh()
    {
        string html = """
            <style>
              #about-scroll{position:absolute;top:0;bottom:42px;left:0;right:0;overflow:auto;padding:12px;}
              #about-scroll p{color:#aeb4c2;margin:0 0 6px 0;}
              #about-footer{position:absolute;height:42px;bottom:0;left:0;right:0;padding:6px;border-top:1px #111216;text-align:right;}
              #about-footer button{background:#343640;border:1px #555865;margin-left:5px;}
            </style>
            <div id="about-scroll">
              <h2>Mine Imator Nuxi Build</h2>
            """ + $"""<p style="color:#898c98;">Version {Escape(_version)}</p>""" + """
              <hr/>
              <h3>Credits</h3>
              <p>Mine Imator: David Andrei</p>
              <p>Mine Imator Development: David, Nimi, Marvin, Mbanders</p>
              <p>Mine Imator Beta Testing: 9redwoods, AnxiousCynic, Hozq, Jossamations, Rollo, SoundsDotZip, UpgradedMoon, _Mine_, Randi(11x)Stress, Alpha Toostrr, Cade [CaZaKoJa], Jnick, KeepOnChucking, SKIBBZ, Swooplezz, Vash, Nirwandra, Azaron</p>
              <p>Mine Imator Branding: Voxy</p>
              <p>Nuxi Project Management: frosty boi, AshFX</p>
              <p>Nuxi Development: frosty boi, Zandar, &amp; Github Contributors</p>
              <p>Nuxi Beta Testing: AshFX, Pikan, Evelyn, Lolin</p>
              <p>Nuxi Branding: AshFX</p>
            </div>
            <div id="about-footer">
              <button id="about-donate">Donate (Ko-fi)</button>
              <button id="about-discord">Join Discord</button>
              <button id="about-close">Close</button>
            </div>
            """;
        _root.SetInnerRml(html);

        _root.GetElementById("about-donate")?.AddEventListener("click", _ => OpenLink(DonateUrl));
        _root.GetElementById("about-discord")?.AddEventListener("click", _ => OpenLink(DiscordInviteUrl));
        _root.GetElementById("about-close")?.AddEventListener("click", _ => Hide());
    }

    private static void OpenLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore browser launch failures and keep the editor responsive.
        }
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
