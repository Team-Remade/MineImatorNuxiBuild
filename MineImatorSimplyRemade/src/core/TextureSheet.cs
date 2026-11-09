using ImageMagick;

namespace MineImatorSimplyRemade.core;

public static class TextureSheet
{
    public static (int Width, int Height) Validate(string path, int tileWidth, int tileHeight)
    {
        if (tileWidth <= 0 || tileHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(tileWidth), "Tile width and height must be positive.");
        var image = new MagickImageInfo(path);
        int width = checked((int)image.Width), height = checked((int)image.Height);
        if (width > 8192 || height > 8192 || (long)width * height > 32 * 1024 * 1024)
            throw new InvalidDataException("Texture sheets are limited to 8192 pixels per edge and 32 megapixels.");
        if (tileWidth > width || tileHeight > height || width % tileWidth != 0 || height % tileHeight != 0)
            throw new ArgumentException("Tile dimensions must divide the image width and height exactly.");
        return (width, height);
    }

    public static void WriteTile(string source, string target, int tileWidth, int tileHeight, int column, int row)
    {
        var (width, height) = Validate(source, tileWidth, tileHeight);
        if (column < 0 || row < 0 || column >= width / tileWidth || row >= height / tileHeight)
            throw new ArgumentOutOfRangeException(nameof(column));
        using var image = new MagickImage(source);
        image.Crop(new MagickGeometry(column * tileWidth, row * tileHeight, (uint)tileWidth, (uint)tileHeight));
        image.ResetPage();
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            image.Write(temporary, MagickFormat.Png32);
            File.Move(temporary, target);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
