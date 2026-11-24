using System.Runtime.InteropServices;

namespace MineImatorSimplyRemade.core.graphics;

public static class NuxTDenoiser
{
    private const string LibraryName = "OpenImageDenoise";
    private const int CpuDeviceType = 1;
    private const int Float3Format = 3;

    public static unsafe bool TryDenoiseLinearRgb(float[] linearRgb, int width, int height,
        out float[] denoisedLinearRgb, out string? error)
    {
        ArgumentNullException.ThrowIfNull(linearRgb);
        if (width <= 0 || height <= 0 || linearRgb.Length != checked(width * height * 3))
            throw new ArgumentException("The linear RGB image dimensions do not match its pixel data.", nameof(linearRgb));

        denoisedLinearRgb = linearRgb;
        error = null;
        IntPtr device = IntPtr.Zero;
        IntPtr filter = IntPtr.Zero;
        try
        {
            device = NewDevice(CpuDeviceType);
            if (device == IntPtr.Zero)
                throw new InvalidOperationException(GetDeviceError(IntPtr.Zero) ?? "OIDN could not create a CPU device.");
            CommitDevice(device);
            ThrowIfDeviceError(device);

            filter = NewFilter(device, "RT");
            if (filter == IntPtr.Zero)
                throw new InvalidOperationException(GetDeviceError(device) ?? "OIDN could not create an RT filter.");

            float[] output = new float[linearRgb.Length];
            fixed (float* inputPointer = linearRgb)
            fixed (float* outputPointer = output)
            {
                nuint pixelStride = 3 * sizeof(float);
                nuint rowStride = checked((nuint)(width * 3 * sizeof(float)));
                SetSharedFilterImage(filter, "color", (IntPtr)inputPointer, Float3Format,
                    (nuint)width, (nuint)height, 0, pixelStride, rowStride);
                SetSharedFilterImage(filter, "output", (IntPtr)outputPointer, Float3Format,
                    (nuint)width, (nuint)height, 0, pixelStride, rowStride);
                SetFilterBool(filter, "hdr", true);
            }
            ThrowIfDeviceError(device);

            CommitFilter(filter);
            ThrowIfDeviceError(device);
            ExecuteFilter(filter);
            ThrowIfDeviceError(device);
            denoisedLinearRgb = output;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
        finally
        {
            try
            {
                if (filter != IntPtr.Zero)
                    ReleaseFilter(filter);
                if (device != IntPtr.Zero)
                    ReleaseDevice(device);
            }
            catch (Exception) { }
        }
    }

    private static void ThrowIfDeviceError(IntPtr device)
    {
        string? error = GetDeviceError(device);
        if (error is not null)
            throw new InvalidOperationException(error);
    }

    private static string? GetDeviceError(IntPtr device)
    {
        int errorCode = GetDeviceErrorCode(device, out IntPtr message);
        if (errorCode == 0)
            return null;
        string description = Marshal.PtrToStringUTF8(message) ?? "Unknown OIDN error.";
        return $"OIDN error {errorCode}: {description}";
    }

    [DllImport(LibraryName, EntryPoint = "oidnNewDevice", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NewDevice(int type);

    [DllImport(LibraryName, EntryPoint = "oidnCommitDevice", CallingConvention = CallingConvention.Cdecl)]
    private static extern void CommitDevice(IntPtr device);

    [DllImport(LibraryName, EntryPoint = "oidnNewFilter", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NewFilter(IntPtr device, [MarshalAs(UnmanagedType.LPUTF8Str)] string type);

    [DllImport(LibraryName, EntryPoint = "oidnSetSharedFilterImage", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetSharedFilterImage(IntPtr filter,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr pointer, int format, nuint width, nuint height,
        nuint byteOffset, nuint bytePixelStride, nuint byteRowStride);

    [DllImport(LibraryName, EntryPoint = "oidnSetFilterBool", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetFilterBool(IntPtr filter, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(LibraryName, EntryPoint = "oidnCommitFilter", CallingConvention = CallingConvention.Cdecl)]
    private static extern void CommitFilter(IntPtr filter);

    [DllImport(LibraryName, EntryPoint = "oidnExecuteFilter", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ExecuteFilter(IntPtr filter);

    [DllImport(LibraryName, EntryPoint = "oidnGetDeviceError", CallingConvention = CallingConvention.Cdecl)]
    private static extern int GetDeviceErrorCode(IntPtr device, out IntPtr message);

    [DllImport(LibraryName, EntryPoint = "oidnReleaseFilter", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ReleaseFilter(IntPtr filter);

    [DllImport(LibraryName, EntryPoint = "oidnReleaseDevice", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ReleaseDevice(IntPtr device);
}