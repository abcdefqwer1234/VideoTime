using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VideoTime
{
    public class BufferedTreeView : TreeView
    {
        private const int WM_PAINT = 0x000F;
        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_PRINTCLIENT = 0x0318;
        private const int PRF_CLIENT = 0x00000004;
        private const int PRF_ERASEBKGND = 0x00000008;
        private const int TVM_SETEXTENDEDSTYLE = 0x1144;
        private const int TVS_EX_DOUBLEBUFFER = 0x0004;
        private const uint SRCCOPY = 0x00CC0020;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct PAINTSTRUCT
        {
            public IntPtr hdc;
            public int fErase;
            public RECT rcPaint;
            public int fRestore;
            public int fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] rgbReserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll")]
        private static extern IntPtr BeginPaint(IntPtr hWnd, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")]
        private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObj);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObj);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")]
        private static extern bool SetWindowOrgEx(IntPtr hdc, int x, int y, out POINT pt);

        public bool DragActive { get; set; }

        public BufferedTreeView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!IsDisposed)
                SendMessage(Handle, TVM_SETEXTENDEDSTYLE, (IntPtr)TVS_EX_DOUBLEBUFFER, (IntPtr)TVS_EX_DOUBLEBUFFER);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_PAINT && DragActive)
            {
                PAINTSTRUCT ps = new PAINTSTRUCT();
                IntPtr hdc = BeginPaint(Handle, ref ps);
                if (hdc != IntPtr.Zero)
                {
                    const int margin = 16;
                    int w = ps.rcPaint.Right - ps.rcPaint.Left;
                    int h = ps.rcPaint.Bottom - ps.rcPaint.Top;
                    if (w > 0 && h > 0)
                    {
                        IntPtr memDC = CreateCompatibleDC(hdc);
                        IntPtr memBmp = IntPtr.Zero;
                        IntPtr oldBmp = IntPtr.Zero;
                        try
                        {
                            if (memDC != IntPtr.Zero)
                            {
                                memBmp = CreateCompatibleBitmap(hdc, w + margin, h + margin);
                                if (memBmp != IntPtr.Zero)
                                {
                                    oldBmp = SelectObject(memDC, memBmp);
                                    POINT oldOrg;
                                    SetWindowOrgEx(memDC, ps.rcPaint.Left, ps.rcPaint.Top, out oldOrg);
                                    SendMessage(Handle, WM_PRINTCLIENT, memDC, (IntPtr)(PRF_CLIENT | PRF_ERASEBKGND));
                                    SetWindowOrgEx(memDC, oldOrg.X, oldOrg.Y, out oldOrg);
                                    BitBlt(hdc, ps.rcPaint.Left, ps.rcPaint.Top, w, h, memDC, 0, 0, SRCCOPY);
                                }
                            }
                        }
                        finally
                        {
                            if (memDC != IntPtr.Zero)
                            {
                                if (oldBmp != IntPtr.Zero) SelectObject(memDC, oldBmp);
                                DeleteDC(memDC);
                            }
                            if (memBmp != IntPtr.Zero) DeleteObject(memBmp);
                        }
                    }
                    EndPaint(Handle, ref ps);
                }
                return;
            }
            if (m.Msg == WM_ERASEBKGND && DragActive)
            {
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }
    }
}