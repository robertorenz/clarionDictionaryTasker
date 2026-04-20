using System;
using System.Runtime.InteropServices;

namespace ClarionDctAddin
{
    // Tiny Win32 shim: launch a child process, find its top-level HWND, strip
    // the decorations (title bar / borders / resize grip / system menu), set
    // our panel as its new parent via SetParent. Result: the external
    // application appears inside our dialog as if it were a native control.
    //
    // Works reliably for simple apps (TopScan is one). Modal dialogs launched
    // from the embedded process still pop out as top-level windows — that's
    // a shell-level limitation and is generally fine; if the user hits File ->
    // Open from inside TopScan, that dialog appears over the parent.
    internal static class Win32Embed
    {
        const int GWL_STYLE     = -16;
        const int GWL_EXSTYLE   = -20;

        const int WS_CHILD      = 0x40000000;
        const int WS_POPUP      = unchecked((int)0x80000000);
        const int WS_CAPTION    = 0x00C00000;
        const int WS_BORDER     = 0x00800000;
        const int WS_DLGFRAME   = 0x00400000;
        const int WS_THICKFRAME = 0x00040000;
        const int WS_SYSMENU    = 0x00080000;
        const int WS_MINIMIZEBOX= 0x00020000;
        const int WS_MAXIMIZEBOX= 0x00010000;

        const int WS_EX_DLGMODALFRAME    = 0x00000001;
        const int WS_EX_WINDOWEDGE       = 0x00000100;
        const int WS_EX_CLIENTEDGE       = 0x00000200;
        const int WS_EX_APPWINDOW        = 0x00040000;
        const int WS_EX_TOOLWINDOW       = 0x00000080;

        const int SW_SHOW = 5;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hwnd, int cmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        const uint SWP_NOZORDER     = 0x0004;
        const uint SWP_NOACTIVATE   = 0x0010;
        const uint SWP_FRAMECHANGED = 0x0020;
        const uint GW_OWNER         = 4;

        internal static void MakeChildOf(IntPtr childHwnd, IntPtr newParent)
        {
            int style   = GetWindowLong(childHwnd, GWL_STYLE);
            int exStyle = GetWindowLong(childHwnd, GWL_EXSTYLE);

            // Remove top-level decorations.
            style &= ~(WS_POPUP | WS_CAPTION | WS_BORDER | WS_DLGFRAME
                     | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
            style |= WS_CHILD;

            exStyle &= ~(WS_EX_DLGMODALFRAME | WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE
                       | WS_EX_APPWINDOW);
            exStyle |= WS_EX_TOOLWINDOW;

            SetWindowLong(childHwnd, GWL_STYLE, style);
            SetWindowLong(childHwnd, GWL_EXSTYLE, exStyle);
            SetParent(childHwnd, newParent);
            // Force a non-client redraw so the style changes take effect.
            SetWindowPos(childHwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        internal static void Resize(IntPtr hwnd, int x, int y, int w, int h)
        {
            if (hwnd == IntPtr.Zero) return;
            MoveWindow(hwnd, x, y, Math.Max(1, w), Math.Max(1, h), true);
        }

        internal static void Show(IntPtr hwnd) { ShowWindow(hwnd, SW_SHOW); }

        // Finds a visible, non-owned top-level HWND that belongs to the given
        // process. TopScan only exposes one such window, so taking the first
        // match is enough.
        internal static IntPtr FindMainWindowForProcess(int pid)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr hwnd, IntPtr lp)
            {
                uint owner;
                GetWindowThreadProcessId(hwnd, out owner);
                if ((int)owner != pid) return true;
                if (!IsWindowVisible(hwnd)) return true;
                if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return true;
                found = hwnd;
                return false;
            }, IntPtr.Zero);
            return found;
        }
    }
}
