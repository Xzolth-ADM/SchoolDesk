using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SchoolDesk.Services;

public static class WindowBackdrop
{
    private const int SystemBackdropTypeAttribute = 38;
    private const int TransientBackdrop = 3;

    public static void Enable(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var backdrop = TransientBackdrop;
            _ = DwmSetWindowAttribute(handle, SystemBackdropTypeAttribute, ref backdrop, sizeof(int));
        };
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
