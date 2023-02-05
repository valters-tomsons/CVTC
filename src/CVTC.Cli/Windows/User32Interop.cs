using System.Text;
using System.Runtime.InteropServices;
using CVTC.Windows.Enums;

namespace CVTC.Windows;

static class User32Interop
{
    [DllImport("user32.dll")]
    static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern int GetWindowText(IntPtr hwnd, StringBuilder ss, int count);

    public static void LeftClick()
    {
        mouse_event((int)(MouseEventFlags.LEFTDOWN), 0, 0, 0, 0);
        mouse_event((int)(MouseEventFlags.LEFTUP), 0, 0, 0, 0);
    }

    public static string GetActiveWindowTitle()
    {
        const int nChar = 256;
        var strBuilder = new StringBuilder(nChar);

        var handle = (IntPtr)GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return string.Empty;
        }

        var charsRead = GetWindowText(handle, strBuilder, nChar);
        return charsRead > 0 ? strBuilder.ToString() : string.Empty;
    }
}