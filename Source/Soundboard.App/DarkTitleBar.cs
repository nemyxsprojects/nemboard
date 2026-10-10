using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Soundboard.App;

internal static class DarkTitleBar
{
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeLegacy = 19;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var enabled = 1;
            if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, UseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
            }

            var caption = 0x00101010;
            var text = 0x00F4F4F4;
            DwmSetWindowAttribute(handle, CaptionColor, ref caption, sizeof(int));
            DwmSetWindowAttribute(handle, TextColor, ref text, sizeof(int));
        };
    }
}
