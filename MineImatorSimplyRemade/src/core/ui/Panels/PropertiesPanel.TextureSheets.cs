using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using NativeFileDialogSharp;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class PropertiesPanel
{
    private void AssignBackgroundTexture(string path, bool sky)
    {
        path = ProjectManager.Instance.ToProjectRelativePath(path);
        if (sky)
        {
            SkyTexture = path;
            Viewport?.ReloadSkyTextures();
        }
        else
        {
            BackgroundImagePath = path;
            ApplyBackgroundSettingsToViewport();
        }
        WriteProjectSettingsToManifest(ProjectManager.Instance.Manifest);
        ProjectManager.Instance.SetDirty(true);
    }

    private void RequestTextureImport(Action<string> apply)
    {
        var result = Dialog.FileOpen(ProjectManager.ImageImportFilter);
        if (result.IsError)
        {
            _pbrImportError = Localization.T("text.texture_sheet.dialog_failed");
            Console.Error.WriteLine(_pbrImportError);
        }
        else if (result.IsOk && !string.IsNullOrWhiteSpace(result.Path))
            _textureImport.Request(result.Path, asset =>
                ApplySheetSelection(ProjectManager.Instance.GetAssetFullPath(asset), apply));
    }

    private void ApplySheetSelection(string path, Action<string> apply)
    {
        try
        {
            apply(TextureSheetPicker.ResolveSelection(path));
            ProjectManager.Instance.SetDirty(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or ImageMagick.MagickException)
        {
            _pbrImportError = Localization.Format("text.texture_sheet.failed", exception.Message);
            Console.Error.WriteLine(_pbrImportError);
        }
    }

    private void DrawTextureSheetPicker(string id, string path, Action<string> apply)
    {
        try
        {
            if (TextureSheetPicker.Render(id, path, out string selected))
            {
                apply(selected);
                ProjectManager.Instance.SetDirty(true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or ImageMagick.MagickException)
        {
            _pbrImportError = Localization.Format("text.texture_sheet.failed", exception.Message);
            Console.Error.WriteLine(_pbrImportError);
        }
    }
}
