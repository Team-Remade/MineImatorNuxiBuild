using System.Diagnostics;
using System.Net;
using MineImatorSimplyRemade.core.update;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Retained-mode replacement for MainWindow's ImGui-drawn Check for Updates dialog
/// (see OpenUpdatePopup/RenderUpdatePopup in the old ImGui MainWindow).</summary>
public sealed class RmlUpdateController
{
    private const string ReleasesUrl = "https://github.com/Team-Remade/MineImatorNuxiBuild/releases";

    private readonly Element _overlay;
    private readonly Element _root;
    public bool Visible { get; private set; }

    private bool _checkInProgress;
    private Task? _checkTask;
    private UpdateChecker.UpdateCheckResult? _lastResult;

    private bool _downloadInProgress;
    private Task? _downloadTask;
    private float _downloadProgress;
    private string _downloadStatus = string.Empty;

    private string _lastRenderedSignature = string.Empty;

    public RmlUpdateController(Element overlay, Element root)
    {
        _overlay = overlay;
        _root = root;
        Refresh();
    }

    public void Toggle()
    {
        if (Visible)
            Hide();
        else
            Show();
    }

    public void Show()
    {
        Visible = true;
        _overlay.SetProperty("display", "block");
        StartCheck();
        Refresh();
    }

    public void Hide()
    {
        Visible = false;
        _overlay.SetProperty("display", "none");
    }

    public void Update()
    {
        if (!Visible)
            return;

        Refresh();
    }

    private void StartCheck()
    {
        if (_checkInProgress || _checkTask != null)
            return;

        _checkInProgress = true;
        _lastResult = null;
        _checkTask = Task.Run(async () =>
        {
            try
            {
                _lastResult = await UpdateChecker.CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                _lastResult = new UpdateChecker.UpdateCheckResult
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                };
            }
            finally
            {
                _checkInProgress = false;
            }
        });
    }

    private void StartInstall(string downloadUrl)
    {
        if (_downloadInProgress)
            return;

        _downloadInProgress = true;
        _downloadProgress = 0f;
        _downloadStatus = "Installing update...";
        _downloadTask = Task.Run(async () =>
        {
            try
            {
                var (success, message, needsRestart) = await UpdateChecker.InstallUpdateWhileRunningAsync(
                    downloadUrl,
                    (downloaded, total) =>
                    {
                        _downloadProgress = total > 0 ? (float)downloaded / total : 0f;
                        _downloadStatus = $"Progress: {FormatBytes(downloaded)} / {FormatBytes(total)}";
                    });

                _downloadStatus = success switch
                {
                    true when needsRestart => message,
                    true => $"Success: {message}",
                    _ => $"Error: {message}"
                };
            }
            catch (Exception ex)
            {
                _downloadStatus = $"Error: {ex.Message}";
            }
            finally
            {
                _downloadInProgress = false;
            }
        });
    }

    private void Refresh()
    {
        bool checking = _checkInProgress || (_checkTask != null && !_checkTask.IsCompleted);
        bool downloading = _downloadInProgress || (_downloadTask != null && !_downloadTask.IsCompleted);

        string signature = $"{checking}|{downloading}|{_downloadProgress:F2}|{_downloadStatus}|" +
            $"{_lastResult?.Success}|{_lastResult?.UpdateAvailable}|{_lastResult?.Message}|{_lastResult?.AvailableVersion}";
        if (signature == _lastRenderedSignature)
            return;
        _lastRenderedSignature = signature;

        string body;
        if (checking)
        {
            body = """<p id="update-status">Checking for updates...</p>""";
        }
        else if (_lastResult != null)
        {
            if (!_lastResult.Success)
            {
                body = $"""
                    <p class="update-error">Check Failed</p>
                    <p>{Escape(_lastResult.Message ?? "Unknown error")}</p>
                    """;
            }
            else if (_lastResult.UpdateAvailable)
            {
                string changelog = string.IsNullOrWhiteSpace(_lastResult.ChangeLog)
                    ? ""
                    : $"""
                        <p class="update-label">Changelog:</p>
                        <div id="update-changelog"><p>{Escape(_lastResult.ChangeLog)}</p></div>
                        """;
                string releaseName = string.IsNullOrWhiteSpace(_lastResult.AvailableVersionName)
                    ? ""
                    : $"""
                        <p class="update-label">Release Name:</p>
                        <p>{Escape(_lastResult.AvailableVersionName)}</p>
                        """;

                string action;
                if (downloading)
                {
                    action = $"""
                        <p>{_downloadProgress * 100:F1}%</p>
                        <p>{Escape(_downloadStatus)}</p>
                        """;
                }
                else
                {
                    action = """<button id="update-install">Install Update</button><button id="update-visit">Visit Release</button>""";
                }

                body = $"""
                    <p class="update-available">Update Available!</p>
                    <p>Current Version: {Escape(UpdateChecker.GetCurrentVersion())}</p>
                    <p>Available Version: {Escape(_lastResult.AvailableVersion ?? "Unknown")}</p>
                    {releaseName}
                    {changelog}
                    {action}
                    """;
            }
            else
            {
                body = $"""
                    <p class="update-available">Up to Date!</p>
                    <p>You are running the latest version ({Escape(UpdateChecker.GetCurrentVersion())})</p>
                    """;
            }
        }
        else
        {
            body = """<p id="update-status">Checking for updates...</p>""";
        }

        string html = """
            <style>
              #update-scroll{position:absolute;top:0;bottom:42px;left:0;right:0;overflow:auto;padding:12px;}
              #update-scroll p{color:#aeb4c2;margin:0 0 6px 0;}
              #update-scroll .update-error{color:#ff8080;}
              #update-scroll .update-available{color:#80ff80;}
              #update-scroll .update-label{color:#898c98;}
              #update-changelog{max-height:200px;overflow:auto;padding:8px;border:1px #454750;margin-bottom:6px;}
              #update-footer{position:absolute;height:42px;bottom:0;left:0;right:0;padding:6px;border-top:1px #111216;text-align:right;}
              #update-footer button{background:#343640;border:1px #555865;margin-left:5px;}
              #update-scroll button{background:#343640;border:1px #555865;margin-right:5px;}
            </style>
            """ + $"""<div id="update-scroll">{body}</div>""" + """
            <div id="update-footer">
              <button id="update-close">Close</button>
            </div>
            """;
        _root.SetInnerRml(html);

        _root.GetElementById("update-install")?.AddEventListener("click", _ =>
        {
            if (!string.IsNullOrWhiteSpace(_lastResult?.DownloadUrl))
                StartInstall(_lastResult.DownloadUrl);
        });
        _root.GetElementById("update-visit")?.AddEventListener("click", _ => OpenLink(ReleasesUrl));
        _root.GetElementById("update-close")?.AddEventListener("click", _ => Hide());
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
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
