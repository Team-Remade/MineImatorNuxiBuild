using System.Buffers.Binary;
using ImageMagick;
using MineImatorSimplyRemade.core.startup;

const string nativeResourceName = "Magick.Native-Q16-HDRI-x64.dll";
var applicationAssembly = typeof(NativeLibraryBootstrap).Assembly;
if (!applicationAssembly.GetManifestResourceNames().Contains(nativeResourceName, StringComparer.Ordinal))
    throw new InvalidOperationException($"Missing embedded resource: {nativeResourceName}");

if (File.Exists(Path.Combine(AppContext.BaseDirectory, nativeResourceName)))
    throw new InvalidOperationException($"The native library must not be loose beside the executable: {nativeResourceName}");

NativeLibraryBootstrap.Initialize();

byte[] bitmap = new byte[58];
bitmap[0] = (byte)'B';
bitmap[1] = (byte)'M';
BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(2), bitmap.Length);
BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(10), 54);
BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(14), 40);
BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(18), 1);
BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(22), 1);
BinaryPrimitives.WriteInt16LittleEndian(bitmap.AsSpan(26), 1);
BinaryPrimitives.WriteInt16LittleEndian(bitmap.AsSpan(28), 24);
BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(34), 4);
bitmap[56] = 255;

using var image = new MagickImage(bitmap);
if (image.Width != 1 || image.Height != 1)
    throw new InvalidOperationException($"Unexpected decoded image size: {image.Width}x{image.Height}");

Console.WriteLine("Magick decoded a 1x1 BMP using the embedded native library.");