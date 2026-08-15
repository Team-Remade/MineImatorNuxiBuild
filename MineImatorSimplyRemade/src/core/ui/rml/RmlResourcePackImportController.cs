using System.Globalization;
using System.Net;
using RmlUiNet;

namespace MineImatorSimplyRemade.core.ui.rml;

/// <summary>Retained-mode replacement for MainWindow's ImGui-drawn Import Resource Pack dialog
/// (see AdvanceResourcePackImportJob/RenderResourcePackImportPopup in the old ImGui MainWindow).
/// This is scaffolding only: it mirrors the progress dialog shape but simulates the import stages
/// locally instead of driving the real ProjectManager/BlockRegistry/TerrainAtlas/ItemsAtlas pipeline,
/// which still lives on MainWindow.</summary>
public sealed class RmlResourcePackImportController
{
    private enum ImportStage
    {
        None,
        CopyPack,
        ReloadBlocks,
        ReloadTerrain,
        ReloadItems,
        RefreshUi,
        Complete
    }

    private readonly Element _overlay;
    private readonly Element _root;
    public bool Visible { get; private set; }

    private bool _importActive;
    private bool _importFinished;
    private ImportStage _stage = ImportStage.None;
    private string _sourcePath = "";
    private string _importedPath = "";
    private string _status = "";
    private string _detail = "";
    private string _error = "";
    private float _progress;

    private string _lastRenderedSignature = string.Empty;

    public RmlResourcePackImportController(Element overlay, Element root)
    {
        _overlay = overlay;
        _root = root;
        Refresh();
    }

    public void Show(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return;

        if (_importActive)
            return;

        _sourcePath = sourcePath;
        _importedPath = "";
        _error = "";
        _progress = 0f;
        _status = "Preparing import...";
        _detail = Path.GetFileName(sourcePath);
        _importFinished = false;
        _importActive = true;
        _stage = ImportStage.CopyPack;

        Visible = true;
        _overlay.SetProperty("display", "block");
        Refresh(force: true);
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

        if (_importActive)
            AdvanceImportJob();

        Refresh();
    }

    private void AdvanceImportJob()
    {
        if (!_importActive)
            return;

        switch (_stage)
        {
            case ImportStage.CopyPack:
                _status = "Copying resource pack into project...";
                _detail = Path.GetFileName(_sourcePath);
                _progress = 0.20f;
                _importedPath = _sourcePath;
                _stage = ImportStage.ReloadBlocks;
                break;

            case ImportStage.ReloadBlocks:
                _status = "Reloading block registry...";
                _detail = "Loading block definitions";
                _progress = 0.42f;
                _stage = ImportStage.ReloadTerrain;
                break;

            case ImportStage.ReloadTerrain:
                _status = "Reloading terrain textures...";
                _detail = "Rebuilding terrain atlas";
                _progress = 0.70f;
                _stage = ImportStage.ReloadItems;
                break;

            case ImportStage.ReloadItems:
                _status = "Reloading item textures...";
                _detail = "Rebuilding item atlas";
                _progress = 0.94f;
                _stage = ImportStage.RefreshUi;
                break;

            case ImportStage.RefreshUi:
                _status = "Refreshing spawn menu options...";
                _detail = "Syncing source selectors";
                _progress = 1f;
                _stage = ImportStage.Complete;
                break;

            case ImportStage.Complete:
                _importActive = false;
                _importFinished = true;
                _status = "Resource pack imported successfully.";
                _detail = Path.GetFileName(_importedPath);
                _error = "";
                break;
        }
    }

    private void CloseAndReset()
    {
        _importFinished = false;
        _stage = ImportStage.None;
        _sourcePath = "";
        _importedPath = "";
        _status = "";
        _detail = "";
        _error = "";
        _progress = 0f;
        Hide();
    }

    private void Refresh(bool force = false)
    {
        string signature = $"{_importActive}|{_importFinished}|{_stage}|{_status}|{_detail}|{_error}|{_progress:F3}";
        if (!force && signature == _lastRenderedSignature)
            return;
        _lastRenderedSignature = signature;

        float clampedProgress = Math.Clamp(_progress, 0f, 1f);
        string detailBlock = string.IsNullOrWhiteSpace(_detail)
            ? ""
            : $"""<p id="import-detail">{Escape(_detail)}</p>""";
        string errorBlock = string.IsNullOrWhiteSpace(_error)
            ? ""
            : $"""<p id="import-error">{Escape(_error)}</p>""";

        string footer = _importActive
            ? """<p id="import-waiting">Please wait while assets are reloaded...</p>"""
            : $"""
                {errorBlock}
                <button id="import-close">Close</button>
                """;

        string progressText = (clampedProgress * 100f).ToString("F1", CultureInfo.InvariantCulture);
        string html = $"""
            <div id="import-scroll">
              <p id="import-status">{Escape(_status)}</p>
              <div id="import-progress-bar"><div id="import-progress-fill" style="width:{progressText}%;"/></div>
              <p>{progressText}%</p>
              {detailBlock}
            </div>
            <div id="import-footer">
              {footer}
            </div>
            """;
        _root.SetInnerRml(html);

        _root.GetElementById("import-close")?.AddEventListener("click", _ => CloseAndReset());
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
