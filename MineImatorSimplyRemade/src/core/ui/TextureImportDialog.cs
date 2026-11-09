using System.Numerics;
using Hexa.NET.ImGui;
using ImageMagick;
using MineImatorSimplyRemade.core.project;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui;

public sealed class TextureImportDialog
{
    private readonly Queue<(string Path, string Project, Action<ProjectAssetEntry> Apply)> _pending = new();
    private bool _useSheet;
    private int _tileWidth = 16;
    private int _tileHeight = 16;
    private string _error = "";
    private bool _open;
    private GL? _previewGl;
    private uint _previewTexture;
    private int _imageWidth;
    private int _imageHeight;
    private bool _previewAttempted;
    private string _previewError = "";

    public void Request(string path, Action<ProjectAssetEntry> apply)
        => _pending.Enqueue((path, ProjectManager.Instance.ProjectFolder, apply));

    public void Render(GL? gl = null)
    {
        var manager = ProjectManager.Instance;
        if (_pending.Count == 0) return;
        var request = _pending.Peek();
        if (!manager.HasProject || request.Project != manager.ProjectFolder)
        {
            ReleasePreview();
            _pending.Clear();
            _open = false;
            return;
        }
        string title = Localization.Label("text.texture_sheet.import", "Import Texture") + "###textureImport";
        if (!_open)
        {
            _useSheet = false;
            _tileWidth = _tileHeight = 16;
            _error = "";
            ReleasePreview();
            ImGui.OpenPopup(title);
            _open = true;
        }
        bool open = true;
        if (ImGui.BeginPopupModal(title, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted(Path.GetFileName(request.Path));
            ImGui.Checkbox(Localization.Label("text.texture_sheet.use", "Use as sheet"), ref _useSheet);
            if (_useSheet)
            {
                ImGui.SetNextItemWidth(180f * UiTheme.Scale);
                ImGui.InputInt(Localization.Label("text.texture_sheet.width", "Tile width (pixels)"), ref _tileWidth);
                ImGui.SetNextItemWidth(180f * UiTheme.Scale);
                ImGui.InputInt(Localization.Label("text.texture_sheet.height", "Tile height (pixels)"), ref _tileHeight);
                ImGui.TextWrapped(Localization.T("text.texture_sheet.size_hint"));
            }
            RenderPreview(gl, request.Path);
            if (_error.Length > 0) ImGui.TextWrapped(_error);
            if (ImGui.Button(Localization.Label("text.common.import", "Import"), new Vector2(120f * UiTheme.Scale, 0)))
            {
                try
                {
                    if (_useSheet) TextureSheet.Validate(request.Path, _tileWidth, _tileHeight);
                    var asset = manager.AddAsset(request.Path, ProjectAssetType.Image,
                        _useSheet ? _tileWidth : 0, _useSheet ? _tileHeight : 0);
                    request.Apply(asset);
                    open = false;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                    ArgumentException or InvalidOperationException or ImageMagick.MagickException)
                {
                    _error = Localization.Format("text.texture_sheet.failed", exception.Message);
                    Console.Error.WriteLine(_error);
                }
            }
            ImGui.SameLine();
            if (ImGui.Button(Localization.Label("text.common.cancel", "Cancel"), new Vector2(120f * UiTheme.Scale, 0)))
                open = false;
            if (!open) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        if (!open)
        {
            ReleasePreview();
            _pending.Dequeue();
            _open = false;
        }
    }

    private unsafe void RenderPreview(GL? gl, string path)
    {
        if (!_previewAttempted && gl != null)
        {
            _previewAttempted = true;
            _previewGl = gl;
            try
            {
                var info = new MagickImageInfo(path);
                if (info.Width == 0 || info.Height == 0 || info.Width > 8192 || info.Height > 8192 ||
                    (long)info.Width * info.Height > 32 * 1024 * 1024)
                    throw new InvalidDataException("Image preview is limited to 8192 pixels per edge and 32 megapixels.");
                _imageWidth = (int)info.Width;
                _imageHeight = (int)info.Height;
                using var image = new MagickImage(path);
                float resize = Math.Min(1f, Math.Min(512f / _imageWidth, 320f / _imageHeight));
                image.Resize((uint)Math.Max(1, (int)(_imageWidth * resize)), (uint)Math.Max(1, (int)(_imageHeight * resize)));
                byte[] pixels = image.ToByteArray(MagickFormat.Rgba);
                _previewTexture = gl.GenTexture();
                gl.BindTexture(GLEnum.Texture2D, _previewTexture);
                fixed (byte* pointer = pixels)
                    gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba, image.Width, image.Height, 0,
                        PixelFormat.Rgba, PixelType.UnsignedByte, pointer);
                gl.TexParameter(GLEnum.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                gl.TexParameter(GLEnum.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                gl.TexParameter(GLEnum.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                gl.TexParameter(GLEnum.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                gl.BindTexture(GLEnum.Texture2D, 0);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                ArgumentException or InvalidOperationException or MagickException)
            {
                if (_previewTexture != 0) gl.DeleteTexture(_previewTexture);
                _previewTexture = 0;
                _previewError = Localization.Format("text.texture_sheet.preview_error", exception.Message);
                Console.Error.WriteLine(_previewError);
            }
        }
        if (_previewTexture == 0)
        {
            ImGui.TextWrapped(_previewError.Length > 0 ? _previewError : Localization.T("text.texture_sheet.preview_failed"));
            return;
        }

        ImGui.TextUnformatted(Localization.Format("text.texture_sheet.preview_size", _imageWidth, _imageHeight));
        Vector2 available = ImGui.GetIO().DisplaySize;
        float maxWidth = Math.Max(1, Math.Min(512f * UiTheme.Scale, available.X * 0.75f));
        float maxHeight = Math.Max(1, Math.Min(320f * UiTheme.Scale, available.Y * 0.4f));
        float scale = Math.Min(maxWidth / _imageWidth, maxHeight / _imageHeight);
        Vector2 size = new(_imageWidth * scale, _imageHeight * scale);
        ImGui.Image(new ImTextureRef(texId: _previewTexture), size);
        RenderTileCuts(ImGui.GetItemRectMin(), size);
    }

    private void RenderTileCuts(Vector2 imageMin, Vector2 imageSize)
    {
        var drawList = ImGui.GetWindowDrawList();
        Vector2 imageMax = imageMin + imageSize;
        uint shadow = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.85f));
        uint guide = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f));
        drawList.PushClipRect(imageMin, imageMax, true);
        if (_useSheet)
        {
            if (_tileWidth > 0)
            {
                float lastX = imageMin.X;
                for (long x = _tileWidth; x < _imageWidth; x += _tileWidth)
                {
                    float screenX = imageMin.X + imageSize.X * x / _imageWidth;
                    // Subpixel cuts share a screen pixel; avoid overdraw on dense sheets.
                    if (screenX - lastX < 1f) continue;
                    lastX = screenX;
                    DrawCut(new Vector2(screenX, imageMin.Y), new Vector2(screenX, imageMax.Y));
                }
            }
            if (_tileHeight > 0)
            {
                float lastY = imageMin.Y;
                for (long y = _tileHeight; y < _imageHeight; y += _tileHeight)
                {
                    float screenY = imageMin.Y + imageSize.Y * y / _imageHeight;
                    if (screenY - lastY < 1f) continue;
                    lastY = screenY;
                    DrawCut(new Vector2(imageMin.X, screenY), new Vector2(imageMax.X, screenY));
                }
            }
        }
        drawList.AddRect(imageMin, imageMax, guide);
        drawList.PopClipRect();

        void DrawCut(Vector2 start, Vector2 end)
        {
            drawList.AddLine(start, end, shadow, 3f * UiTheme.Scale);
            drawList.AddLine(start, end, guide, UiTheme.Scale);
        }
    }

    private void ReleasePreview()
    {
        if (_previewTexture != 0) _previewGl?.DeleteTexture(_previewTexture);
        _previewTexture = 0;
        _previewGl = null;
        _previewAttempted = false;
        _previewError = "";
    }
}
