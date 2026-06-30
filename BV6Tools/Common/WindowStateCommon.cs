using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace BV6Tools.Common
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        [JsonInclude] public int X;
        [JsonInclude] public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        [JsonInclude] public int Left;
        [JsonInclude] public int Top;
        [JsonInclude] public int Right;
        [JsonInclude] public int Bottom;

        [JsonIgnore] public readonly int Width => Right - Left;
        [JsonIgnore] public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        [JsonInclude] public uint length;
        [JsonInclude] public uint flags;
        [JsonInclude] public uint showCmd;
        [JsonInclude] public POINT ptMinPosition;
        [JsonInclude] public POINT ptMaxPosition;
        [JsonInclude] public RECT rcNormalPosition;
    }

    public static partial class WindowStateCommon
    {
        public const uint SW_SHOWMAXIMIZED = 3;
        public const uint SW_SHOWMINIMIZED = 2;
        public const uint SW_SHOWNORMAL = 1;

        public static WINDOWPLACEMENT DefaultPlacement => new()
        {
            length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>(),
            showCmd = SW_SHOWNORMAL
        };

        public static WINDOWPLACEMENT ClampMinimized(WINDOWPLACEMENT state, WINDOWPLACEMENT? previous)
        {
            if (state.showCmd != SW_SHOWMINIMIZED) return state;

            var previousCmd = previous?.showCmd ?? SW_SHOWNORMAL;
            state.showCmd = previousCmd == SW_SHOWMINIMIZED ? SW_SHOWNORMAL : previousCmd;
            return state;
        }

        public static WINDOWPLACEMENT GetWindowState(IntPtr hWnd)
        {
            var placement = new WINDOWPLACEMENT
            {
                length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>()
            };

            if (!GetWindowPlacement(hWnd, out placement))
                throw new InvalidOperationException($"GetWindowPlacement failed. Error: {Marshal.GetLastWin32Error()}");

            return placement;
        }

        public static bool PositionEquals(WINDOWPLACEMENT a, WINDOWPLACEMENT b)
        {
            return a.flags == b.flags
                && a.rcNormalPosition.Left == b.rcNormalPosition.Left
                && a.rcNormalPosition.Top == b.rcNormalPosition.Top
                && a.rcNormalPosition.Right == b.rcNormalPosition.Right
                && a.rcNormalPosition.Bottom == b.rcNormalPosition.Bottom;
        }

        public static void SetWindowState(IntPtr hWnd, WINDOWPLACEMENT placement)
        {
            placement.length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>();

            if (!SetWindowPlacement(hWnd, in placement))
                throw new InvalidOperationException($"SetWindowPlacement failed. Error: {Marshal.GetLastWin32Error()}");
        }

        [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowPlacement")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetWindowPlacement(IntPtr hWnd, out WINDOWPLACEMENT lpwndpl);

        [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowPlacement")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWindowPlacement(IntPtr hWnd, in WINDOWPLACEMENT lpwndpl);
    }
}