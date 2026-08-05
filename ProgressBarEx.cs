using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace VideoTime
{
    public class ProgressBarEx : ProgressBar
    {
        private bool _indeterminate;
        private int _animOffset;
        private readonly Timer _timer;

        public ProgressBarEx()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            _timer = new Timer { Interval = 33 };
            _timer.Tick += (s, e) =>
            {
                _animOffset += 4;
                Invalidate();
            };
        }

        public string ProgressText { get; set; } = "";

        public void SetIndeterminate(bool indeterminate)
        {
            if (_indeterminate == indeterminate) return;
            _indeterminate = indeterminate;
            _animOffset = 0;
            _timer.Enabled = indeterminate;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle bounds = ClientRectangle;
            e.Graphics.Clear(BackColor);

            if (ProgressBarRenderer.IsSupported)
                ProgressBarRenderer.DrawHorizontalBar(e.Graphics, bounds);

            Rectangle track = new Rectangle(
                bounds.Left + 2, bounds.Top + 2,
                Math.Max(0, bounds.Width - 4), Math.Max(0, bounds.Height - 4));

            if (_indeterminate)
            {
                int barWidth = Math.Max(30, track.Width / 3);
                int x = track.Left + (_animOffset % (track.Width + barWidth)) - barWidth;
                Rectangle fill = new Rectangle(x, track.Top, barWidth, track.Height);
                if (ProgressBarRenderer.IsSupported)
                    ProgressBarRenderer.DrawHorizontalChunks(e.Graphics, fill);
                else
                    e.Graphics.FillRectangle(SystemBrushes.Highlight, fill);
            }
            else if (Maximum > Minimum)
            {
                int fillWidth = (int)(track.Width * (double)(Value - Minimum) / (Maximum - Minimum));
                if (fillWidth > 0)
                {
                    Rectangle fill = new Rectangle(track.Left, track.Top, fillWidth, track.Height);
                    if (ProgressBarRenderer.IsSupported)
                        ProgressBarRenderer.DrawHorizontalChunks(e.Graphics, fill);
                    else
                        e.Graphics.FillRectangle(SystemBrushes.Highlight, fill);
                }
            }

            if (!string.IsNullOrEmpty(ProgressText))
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    ProgressText,
                    Font,
                    new Rectangle(bounds.Left + 6, bounds.Top, Math.Max(0, bounds.Width - 12), bounds.Height),
                    ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
