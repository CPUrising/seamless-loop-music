using System;
using System.Runtime.InteropServices;

namespace seamless_loop_music.Services
{
    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, int tbpFlags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, int dwReserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] pButtons);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray)] THUMBBUTTON[] pButtons);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);
        void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string pszTip);
        void SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct THUMBBUTTON
    {
        public uint dwMask;
        public uint iId;
        public uint iBitmap;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szTip;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static class TaskbarConstants
    {
        public const uint THB_BITMAP = 0x0001;
        public const uint THB_ICON = 0x0002;
        public const uint THB_TOOLTIP = 0x0004;
        public const uint THB_FLAGS = 0x0008;

        public const uint THBF_ENABLED = 0x0000;
        public const uint THBF_DISABLED = 0x0001;
        public const uint THBF_DISMISSONCLICK = 0x0002;

        public const int TBPF_NOPROGRESS = 0;
        public const int TBPF_INDETERMINATE = 1;
        public const int TBPF_NORMAL = 2;
        public const int TBPF_ERROR = 4;
        public const int TBPF_PAUSED = 8;
        
        public const int WM_COMMAND = 0x0111;
        public const int WM_DWMSENDICONICTHUMBNAIL = 0x0323;
        
        public const int DWMWA_FORCE_ICONIC_REPRESENTATION = 7;
        public const int DWMWA_HAS_ICONIC_BITMAP = 10;
    }
}
