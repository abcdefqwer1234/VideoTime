using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VideoTime
{
    /// <summary>把纯文本渲染为图片（高分辨率导出用），固定字体与像素预算约束，避免大结果时内存无界。</summary>
    public static class TextImageRenderer
    {
        public static Image Render(string text)
        {
            const float fontSize = 16f;
            const int pad = 20;
            const int lineSpacing = 28;
            const float maxScale = 3f;
            const int maxLines = 30000;
            const long maxPixels = 24_000_000L;

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                Array.Resize(ref lines, lines.Length - 1);

            if (lines.Length > maxLines)
            {
                int omitted = lines.Length - maxLines;
                string[] kept = new string[maxLines + 1];
                Array.Copy(lines, kept, maxLines);
                kept[maxLines] = "……（共 " + lines.Length + " 行，已省略 " + omitted + " 行，请改用“复制当前界面文本”）";
                lines = kept;
            }

            // 以 scale=1 测量自然宽高，再按像素预算确定实际缩放，避免大结果时位图内存无界
            float width1 = 0;
            using (Font measureFont = new Font("新宋体", fontSize, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Bitmap measureBmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(measureBmp))
            {
                foreach (string line in lines)
                {
                    float w = g.MeasureString(line, measureFont).Width;
                    if (w > width1) width1 = w;
                }
            }

            float h1 = lines.Length * lineSpacing;
            float scale = maxScale;
            float budgetScale = (float)Math.Sqrt((double)maxPixels / (double)(Math.Max(width1, 1f) * Math.Max(h1, 1f)));
            if (budgetScale < scale) scale = budgetScale;

            int scaledPad = (int)(pad * scale);
            int scaledSpacing = (int)(lineSpacing * scale);
            int width = Math.Max(1, (int)Math.Ceiling(width1 * scale) + scaledPad * 2);
            int height = Math.Max(1, (int)Math.Ceiling(h1 * scale) + scaledPad * 2);

            using (Font font = new Font("新宋体", fontSize * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                Bitmap bmp = new Bitmap(width, height);
                bmp.SetResolution(96f * scale, 96f * scale);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.White);
                    using (SolidBrush brush = new SolidBrush(Color.Black))
                    {
                        for (int i = 0; i < lines.Length; i++)
                            g.DrawString(lines[i], font, brush, scaledPad, scaledPad + i * scaledSpacing);
                    }
                }
                return bmp;
            }
        }
    }
}
