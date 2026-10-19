#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace JunaidIRF.SyncedInteractions.EditorCapture
{
    /// <summary>
    /// Automation suite for capturing Unity Editor UI, resizing panels, and generating
    /// documentation screenshots with zero occlusion and exact pixel dimensions.
    /// </summary>
    public static class EditorUICapture
    {
        #region Win32 P/Invoke & DPI Scaling

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, [Out] byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int pt_x;
            public int pt_y;
        }

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        public static void PumpMessages(int milliseconds)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < milliseconds)
            {
                while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, 1)) // PM_REMOVE = 1
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
                EditorApplication.Step();
                Thread.Sleep(10);
            }
        }

        private const int SRCCOPY = 0x00CC0020;
        private const int SW_RESTORE = 9;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        #endregion

        /// <summary>
        /// Forces Unity's main window out of minimization and elevates it above background windows.
        /// </summary>
        public static void ForceElevateWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;

            IntPtr foreWnd = GetForegroundWindow();
            uint foreThread = GetWindowThreadProcessId(foreWnd, IntPtr.Zero);
            uint appThread = GetCurrentThreadId();

            if (foreThread != appThread)
            {
                AttachThreadInput(foreThread, appThread, true);
                ShowWindow(hWnd, SW_RESTORE);
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                AttachThreadInput(foreThread, appThread, false);
            }
            else
            {
                ShowWindow(hWnd, SW_RESTORE);
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            }
        }

        /// <summary>
        /// Restores normal z-order for Unity's window after capturing.
        /// </summary>
        public static void RestoreWindowElevation(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            SetWindowPos(hWnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        }

        /// <summary>
        /// Gets the display scaling ratio for Unity's window (1.0 = 100%, 1.25 = 125%, 1.5 = 150%).
        /// </summary>
        public static float GetDpiScale(IntPtr hWnd)
        {
            try
            {
                uint dpi = GetDpiForWindow(hWnd);
                if (dpi > 0) return dpi / 96f;
            }
            catch { }
            return 1.0f;
        }

        /// <summary>
        /// Captures a screen rectangle into a PNG file with DPI conversion.
        /// </summary>
        public static string CaptureScreenRect(int x, int y, int width, int height, string outputPath)
        {
            IntPtr hDesk = GetDesktopWindow();
            IntPtr hdcSrc = GetDC(hDesk);
            IntPtr hdcDest = CreateCompatibleDC(hdcSrc);
            IntPtr hBmp = CreateCompatibleBitmap(hdcSrc, width, height);
            IntPtr hOldBmp = SelectObject(hdcDest, hBmp);

            BitBlt(hdcDest, 0, 0, width, height, hdcSrc, x, y, SRCCOPY);
            SelectObject(hdcDest, hOldBmp);

            BITMAPINFO bi = new BITMAPINFO();
            bi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bi.bmiHeader.biWidth = width;
            bi.bmiHeader.biHeight = height;
            bi.bmiHeader.biPlanes = 1;
            bi.bmiHeader.biBitCount = 32;
            bi.bmiHeader.biCompression = 0;

            byte[] pixels = new byte[width * height * 4];
            GetDIBits(hdcDest, hBmp, 0, (uint)height, pixels, ref bi, 0);

            DeleteObject(hBmp);
            DeleteDC(hdcDest);
            ReleaseDC(hDesk, hdcSrc);

            Texture2D tex = new Texture2D(width, height, TextureFormat.BGRA32, false);
            tex.LoadRawTextureData(pixels);
            tex.Apply();

            byte[] pngBytes = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            string fullPath = Path.IsPathRooted(outputPath)
                ? outputPath
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", outputPath));

            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllBytes(fullPath, pngBytes);
            return fullPath;
        }

        /// <summary>
        /// Captures any open Unity EditorWindow by type name (e.g. "InspectorWindow", "AnimationWindow", "TimelineWindow").
        /// </summary>
        public static string CaptureWindow(string windowTypeName, string outputPath, int cropTop = 0, int cropBottom = 0, int cropLeft = 0, int cropRight = 0)
        {
            IntPtr mainWnd = Process.GetCurrentProcess().MainWindowHandle;
            ForceElevateWindow(mainWnd);

            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            EditorWindow target = null;
            foreach (var win in windows)
            {
                if (win != null && (win.GetType().Name == windowTypeName || win.GetType().FullName == windowTypeName))
                {
                    target = win;
                    break;
                }
            }

            if (target == null)
            {
                RestoreWindowElevation(mainWnd);
                throw new InvalidOperationException($"EditorWindow of type '{windowTypeName}' not found.");
            }

            target.Show();
            target.Focus();
            target.Repaint();
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            ActiveEditorTracker.sharedTracker.RebuildIfNecessary();
            PumpMessages(250);

            float scale = GetDpiScale(mainWnd);
            Rect pos = target.position;

            int rawX = (int)(pos.x * scale);
            int rawY = (int)(pos.y * scale);
            int rawW = (int)(pos.width * scale);
            int rawH = (int)(pos.height * scale);

            int x = rawX + (int)(cropLeft * scale);
            int y = rawY + (int)(cropTop * scale);
            int w = Mathf.Max(10, rawW - (int)((cropLeft + cropRight) * scale));
            int h = Mathf.Max(10, rawH - (int)((cropTop + cropBottom) * scale));

            string savedPath = CaptureScreenRect(x, y, w, h, outputPath);
            RestoreWindowElevation(mainWnd);
            return savedPath;
        }

        /// <summary>
        /// Studio Popout Capture: Opens a borderless utility window for a target asset at exact dimensions,
        /// renders its CustomEditor cleanly, captures the frame, and closes automatically.
        /// </summary>
        public static string CaptureAssetInspectorStudio(UnityEngine.Object asset, int width, int height, string outputPath)
        {
            if (asset == null)
                throw new ArgumentNullException(nameof(asset), "Target asset cannot be null.");

            IntPtr mainWnd = Process.GetCurrentProcess().MainWindowHandle;
            ForceElevateWindow(mainWnd);

            // Open dedicated Studio window
            var studio = EditorStudioWindow.Open(asset, width, height);

            // Pump message queue and repaint
            studio.Repaint();
            PumpMessages(300);

            float scale = GetDpiScale(mainWnd);
            Rect pos = studio.position;

            // Crop window title bar (typically 24px on Windows utility windows)
            int titleBarHeight = (int)(24 * scale);
            int x = (int)(pos.x * scale);
            int y = (int)(pos.y * scale) + titleBarHeight;
            int w = (int)(pos.width * scale);
            int h = (int)(pos.height * scale) - titleBarHeight;

            string savedPath = CaptureScreenRect(x, y, w, h, outputPath);

            studio.Close();
            RestoreWindowElevation(mainWnd);
            return savedPath;
        }
    }
}
#endif
