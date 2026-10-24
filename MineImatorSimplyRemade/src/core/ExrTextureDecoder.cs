using System;
using System.Buffers.Binary;
using System.IO;
using ImageMagick;

namespace MineImatorSimplyRemade.core;

/// <summary>Decodes linear material values without gamma correction or tone mapping.</summary>
public static class ExrTextureDecoder
{
    /// <summary>
    /// Returns tightly packed, row-major RGBA8. Finite samples are clamped to [0,1]
    /// and rounded to the nearest byte; non-finite samples become zero.
    /// </summary>
    public static (int Width, int Height, byte[] Data) Decode(byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (encoded.Length < 4 || encoded[0] != 0x76 || encoded[1] != 0x2f ||
            encoded[2] != 0x31 || encoded[3] != 0x01)
            throw new InvalidDataException("Invalid EXR material texture signature.");

        try
        {
            var settings = new MagickReadSettings { Format = MagickFormat.Exr };
            using var image = new MagickImage();
            image.Ping(encoded, settings);
            uint width = image.Width;
            uint height = image.Height;
            if (width == 0 || height == 0 || width > 8192 || height > 8192 ||
                (ulong)width * height > 32UL * 1024 * 1024)
                throw new InvalidDataException("EXR texture exceeds the 8192-pixel edge or 32-megapixel limit.");

            image.Read(encoded, settings);
            if (image.Width != width || image.Height != height)
                throw new InvalidDataException("EXR dimensions changed while decoding.");

            // Read raw quantum values, not a display conversion: normal/data maps stay linear.
            using var pixels = image.GetPixels();
            int red = (int)(pixels.GetChannelIndex(PixelChannel.Red) ?? 0);
            bool gray = HasGrayscaleChannels(encoded);
            int[] channels =
            [
                red,
                gray ? red : (int)(pixels.GetChannelIndex(PixelChannel.Green) ?? (uint)red),
                gray ? red : (int)(pixels.GetChannelIndex(PixelChannel.Blue) ?? (uint)red),
                (int?)pixels.GetChannelIndex(PixelChannel.Alpha) ?? -1
            ];
            var data = new byte[checked((int)(width * height * 4))];
            for (int y = 0; y < (int)height; y++)
            {
                float[] row = pixels.GetArea(0, y, width, 1)
                    ?? throw new InvalidDataException("Missing EXR pixel data.");
                for (int x = 0; x < (int)width; x++)
                for (int c = 0; c < 4; c++)
                {
                    float value = channels[c] < 0 ? 1f : row[x * pixels.Channels + channels[c]] / Quantum.Max;
                    data[(y * (int)width + x) * 4 + c] = float.IsFinite(value)
                        ? (byte)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f)
                        : (byte)0;
                }
            }

            return ((int)width, (int)height, data);
        }
        catch (MagickException exception)
        {
            throw new InvalidDataException("Invalid or unsupported EXR material texture.", exception);
        }
    }

    private static bool HasGrayscaleChannels(ReadOnlySpan<byte> encoded)
    {
        // Magick's EXR reader leaves G/B undefined for Y+A; replicate Y explicitly.
        ReadOnlySpan<byte> header = encoded[8..];
        while (!header.IsEmpty)
        {
            int nameLength = header.IndexOf((byte)0);
            if (nameLength <= 0) return false;
            bool channels = header[..nameLength].SequenceEqual("channels"u8);
            header = header[(nameLength + 1)..];
            int typeLength = header.IndexOf((byte)0);
            if (typeLength < 0) return false;
            header = header[(typeLength + 1)..];
            if (header.Length < 4) return false;
            int length = BinaryPrimitives.ReadInt32LittleEndian(header);
            header = header[4..];
            if (length < 0 || length > header.Length) return false;
            if (channels)
            {
                ReadOnlySpan<byte> list = header[..length];
                bool hasY = false, hasRgb = false;
                while (!list.IsEmpty)
                {
                    int channelLength = list.IndexOf((byte)0);
                    if (channelLength <= 0) break;
                    ReadOnlySpan<byte> name = list[..channelLength];
                    hasY |= name.SequenceEqual("Y"u8);
                    hasRgb |= name.SequenceEqual("R"u8) || name.SequenceEqual("G"u8) || name.SequenceEqual("B"u8);
                    int entryLength = channelLength + 1 + 16;
                    if (entryLength > list.Length) return false;
                    list = list[entryLength..];
                }
                return hasY && !hasRgb;
            }
            header = header[length..];
        }
        return false;
    }
}
