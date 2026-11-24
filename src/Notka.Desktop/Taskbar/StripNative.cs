using System.Runtime.InteropServices;

namespace Notka.Desktop.Taskbar
{
    internal static class StripNative
    {
        public const uint WsPopup = 0x80000000;
        public const uint WsExTopmost = 0x8;
        public const uint WsExToolWindow = 0x80;
        public const uint WsExLayered = 0x80000;
        public const uint WsExNoActivate = 0x08000000;
        public const uint SwpNoSize = 0x1;
        public const uint SwpNoMove = 0x2;
        public const uint SwpNoActivate = 0x10;
        public const int SwHide = 0;
        public const int SwShowNoActivate = 4;
        public const uint GwHwndPrev = 3;
        public const uint TmeLeave = 0x2;
        public const uint UlwAlpha = 2;
        public const int IdcArrow = 32512;

        public static readonly IntPtr HwndTopmost = new(-1);

        public delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Size
        {
            public int Width;
            public int Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TrackMouseEvent
        {
            public uint Size;
            public uint Flags;
            public IntPtr Window;
            public uint HoverTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Message
        {
            public IntPtr Window;
            public uint Id;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public Point Position;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WindowClass
        {
            public uint Size;
            public uint Style;
            public IntPtr Procedure;
            public int ClassExtra;
            public int WindowExtra;
            public IntPtr Instance;
            public IntPtr Icon;
            public IntPtr Cursor;
            public IntPtr Background;
            public string? MenuName;
            public string ClassName;
            public IntPtr SmallIcon;
        }

        [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClass(ref WindowClass windowClass);

        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindow(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

        [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
        public static extern IntPtr DefaultWindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "GetMessageW")]
        public static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref Message message);

        [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
        public static extern IntPtr DispatchMessage(ref Message message);

        [DllImport("user32.dll", EntryPoint = "PostMessageW")]
        public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern void PostQuitMessage(int exitCode);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr window);

        [DllImport("user32.dll")]
        public static extern UIntPtr SetTimer(IntPtr window, UIntPtr id, uint milliseconds, IntPtr procedure);

        [DllImport("user32.dll")]
        public static extern bool KillTimer(IntPtr window, UIntPtr id);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination, ref Point position, ref Size size, IntPtr source, ref Point sourcePosition, uint key, ref BlendFunction blend, uint flags);

        [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string className, string? title);

        [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", EntryPoint = "TrackMouseEvent")]
        public static extern bool TrackMouse(ref TrackMouseEvent track);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr window, IntPtr dc);

        [DllImport("user32.dll", EntryPoint = "LoadCursorW")]
        public static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr window);

        [DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int state);

        [DllImport("gdi32.dll", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);
    }
}
