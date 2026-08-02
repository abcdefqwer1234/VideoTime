using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VideoTime
{
    public partial class Form1 : Form
    {
        private const int MaxDepth = 50;
        private int _failCount = 0;
        private List<FolderResult> _folderResults = new List<FolderResult>();

        private TreeNode _selNode;
        private int _selStart;
        private int _selEnd;
        private TreeNode _anchorNode;
        private int _anchorChar;
        private Point _downPoint;
        private bool _dragActive;
        private TreeNode _rightClickNode;
        private ToolStripMenuItem _copyNodeItem;

        private class FolderResult
        {
            public string FolderPath { get; set; }
            public double TotalSeconds { get; set; }
            public int FileCount { get; set; }
        }

        private class FolderItem
        {
            public string FolderPath { get; set; }
            public string[] Files { get; set; }
            public string[] SubDirs { get; set; }
        }

        public Form1()
        {
            InitializeComponent();

            EnableTreeDoubleBuffering();

            DetailContextMenu = new ContextMenuStrip();
            var copyItem = new ToolStripMenuItem("复制当前界面文本");
            copyItem.Click += DetailContextMenu_Copy_Click;
            DetailContextMenu.Items.Add(copyItem);
            var saveImageItem = new ToolStripMenuItem("保存当前界面文本为图片");
            saveImageItem.Click += DetailContextMenu_SaveImage_Click;
            DetailContextMenu.Items.Add(saveImageItem);
            _copyNodeItem = new ToolStripMenuItem("复制");
            _copyNodeItem.Click += DetailContextMenu_CopyNode_Click;
            DetailContextMenu.Items.Add(_copyNodeItem);
            DetailContextMenu.Opening += DetailContextMenu_Opening;
            DetailTree.ContextMenuStrip = DetailContextMenu;

            DetailTree.DrawMode = TreeViewDrawMode.OwnerDrawText;
            DetailTree.DrawNode += DetailTree_DrawNode;
            DetailTree.MouseDown += DetailTree_MouseDown;
            DetailTree.MouseMove += DetailTree_MouseMove;
            DetailTree.MouseUp += DetailTree_MouseUp;
        }

        private void EnableTreeDoubleBuffering()
        {
            const ControlStyles styles = ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer;
            var setStyle = typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (setStyle != null)
                setStyle.Invoke(DetailTree, new object[] { styles, true });
            var updateStyles = typeof(Control).GetMethod("UpdateStyles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (updateStyles != null)
                updateStyles.Invoke(DetailTree, null);
        }

        private void SetDragActive(bool value)
        {
            _dragActive = value;
            if (DetailTree is BufferedTreeView btv)
                btv.DragActive = value;
        }

        private async void Start_Click(object sender, EventArgs e)
        {
            string folderPath = TextBox_Doc.Text.Trim().Trim('"');

            if (!Directory.Exists(folderPath))
            {
                MessageBox.Show("文件夹路径无效，请重新输入。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DetailTree.Nodes.Clear();

            _failCount = 0;
            _folderResults.Clear();
            bool recursive = CbSubfolders.Checked;

            ShowTime.Text = "正在扫描文件夹…";
            Start.Enabled = false;
            Cursor = Cursors.WaitCursor;

            Stopwatch sw = Stopwatch.StartNew();
            string elapsedText = "";
            try
            {
                double totalSeconds = await Task.Run(() =>
                {
                    var list = new List<FolderItem>();
                    CollectFoldersRecursive(folderPath, recursive, 0, list);
                    return ProcessFolderItems(list);
                });
                sw.Stop();
                elapsedText = string.Format("{0:N0} 毫秒", sw.ElapsedMilliseconds);

                string result = "总时间: " + FormatTime(totalSeconds);
                if (_failCount > 0)
                    result += $" ({_failCount}个文件读取失败)";
                ShowTime.Text = result;
                AppendLog("文件夹: " + folderPath + " | 耗时: " + elapsedText);

                _folderResults.Reverse();
                DetailTree.BeginUpdate();
                var nodeMap = new Dictionary<string, TreeNode>();
                foreach (var r in _folderResults)
                {
                    string folderName = Path.GetFileName(r.FolderPath);
                    if (string.IsNullOrEmpty(folderName))
                        folderName = r.FolderPath;

                    TreeNode node = new TreeNode($"{folderName}  {FormatTime(r.TotalSeconds)}  [视频{r.FileCount}]");
                    node.Tag = r.FolderPath;

                    string parentPath = Path.GetDirectoryName(r.FolderPath);
                    if (parentPath != null && nodeMap.TryGetValue(parentPath, out TreeNode parent))
                        parent.Nodes.Add(node);
                    else
                        DetailTree.Nodes.Add(node);

                    nodeMap[r.FolderPath] = node;
                }
                foreach (TreeNode node in DetailTree.Nodes)
                    ExpandToDepth(node, 1);
                DetailTree.EndUpdate();

                _selNode = null;
                _selStart = 0;
                _selEnd = 0;
                _anchorNode = null;
                SetDragActive(false);
                _rightClickNode = null;
            }
            catch (Exception ex)
            {
                AppendLog("查询异常: " + folderPath + " | " + ex.Message);
                MessageBox.Show("查询异常: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Start.Enabled = true;
                Cursor = Cursors.Default;
            }
        }

        private void ExpandToDepth(TreeNode node, int currentDepth)
        {
            node.Expand();
            if (currentDepth >= 2) return;
            foreach (TreeNode child in node.Nodes)
                ExpandToDepth(child, currentDepth + 1);
        }

        private void AppendLog(string line)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");
                File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
}

        private static void CollectFoldersRecursive(string path, bool recursive, int depth, List<FolderItem> items)
        {
            if (depth > MaxDepth) return;

            try
            {
                string[] files = SafeGetFiles(path);
                string[] subDirs = recursive ? SafeGetDirectories(path) : new string[0];

                items.Add(new FolderItem
                {
                    FolderPath = path,
                    Files = files,
                    SubDirs = subDirs
                });

                foreach (string dir in subDirs)
                    CollectFoldersRecursive(dir, recursive, depth + 1, items);
            }
            catch
            {
                // 单个目录枚举出错不影响其余部分
            }
        }

        private static string[] SafeGetFiles(string path)
        {
            try { return Directory.GetFiles(path, "*.mp4", SearchOption.TopDirectoryOnly); }
            catch { return new string[0]; }
        }

        private static string[] SafeGetDirectories(string path)
        {
            try
            {
                string[] dirs = Directory.GetDirectories(path);
                if (dirs.Length == 0) return dirs;
                return Array.FindAll(dirs, d => !IsReparsePoint(d));
            }
            catch { return new string[0]; }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                FileAttributes attr = File.GetAttributes(path);
                return (attr & FileAttributes.ReparsePoint) != 0;
            }
            catch { return true; }
        }

        private double ProcessFolderItems(List<FolderItem> items)
        {
            var files = new List<string>();
            foreach (var it in items) files.AddRange(it.Files);

            int threads = Math.Max(2, Environment.ProcessorCount);
            Dictionary<string, double> perFile = Mp4Parse.ReadAll(files, out int fail, threads);
            if (fail > 0) _failCount += fail;

            var totals = new Dictionary<string, double>();
            var counts = new Dictionary<string, int>();

            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];

                double localTotal = 0;
                foreach (string file in item.Files)
                {
                    if (perFile.TryGetValue(file, out double sec))
                        localTotal += sec;
                }

                double subTotal = 0;
                int subCount = 0;
                foreach (string subDir in item.SubDirs)
                {
                    if (totals.TryGetValue(subDir, out double st))
                        subTotal += st;
                    if (counts.TryGetValue(subDir, out int sc))
                        subCount += sc;
                }

                double grandTotal = localTotal + subTotal;
                int grandCount = item.Files.Length + subCount;
                totals[item.FolderPath] = grandTotal;
                counts[item.FolderPath] = grandCount;

                _folderResults.Add(new FolderResult
                {
                    FolderPath = item.FolderPath,
                    TotalSeconds = grandTotal,
                    FileCount = grandCount
                });
            }

            return items.Count > 0 ? totals[items[0].FolderPath] : 0;
        }

        private static string FormatTime(double totalSeconds)
        {
            double h = totalSeconds / 3600;
            double m = (totalSeconds % 3600) / 60;
            double s = totalSeconds % 60;
            return $"{(int)h}时{(int)m}分{(int)s}秒";
        }

        private void ResetTextBoxSelection()
        {
            TextBox_Doc.SelectionStart = 0;
            TextBox_Doc.SelectionLength = 0;
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                TextBox_Doc.Text = folderBrowserDialog1.SelectedPath;
                ResetTextBoxSelection();
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            TextBox_Doc.Text = Properties.Settings.Default.FolderPath;
            ResetTextBoxSelection();
            CbSubfolders.Checked = Properties.Settings.Default.IncludeSubfolders;

            Rectangle wa = Screen.FromControl(this).WorkingArea;
            this.MaximumSize = new Size(wa.Width, wa.Height);
            this.Size = new Size(Math.Min(this.Width, wa.Width), Math.Min(this.Height, wa.Height));
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            Properties.Settings.Default.FolderPath = TextBox_Doc.Text;
            Properties.Settings.Default.IncludeSubfolders = CbSubfolders.Checked;
            Properties.Settings.Default.Save();
        }

        private void Form1_ResizeEnd(object sender, EventArgs e)
        {
            ResetTextBoxSelection();
            TextBox_Doc.ScrollToCaret();
        }

        private void TextBox_Doc_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void TextBox_Doc_DragDrop(object sender, DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null && paths.Length > 0 && Directory.Exists(paths[0]))
            {
                TextBox_Doc.Text = paths[0];
                ResetTextBoxSelection();
            }
        }

        private void DetailContextMenu_Copy_Click(object sender, EventArgs e)
        {
            CopyAllText();
        }

        private void DetailTree_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                CopyTreeSelection();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void CopyTreeSelection()
        {
            if (_selNode != null && _selStart < _selEnd)
            {
                SafeSetClipboard(_selNode.Text.Substring(_selStart, _selEnd - _selStart));
                return;
            }
            if (DetailTree.SelectedNode != null)
            {
                SafeSetClipboard(DetailTree.SelectedNode.Text);
                return;
            }
            CopyAllText();
        }

        private void InvalidateNodeRow(TreeNode node)
        {
            if (node == null || DetailTree.IsDisposed) return;
            Rectangle b = node.Bounds;
            int w = Math.Max(b.Width, DetailTree.ClientSize.Width - b.Left);
            DetailTree.Invalidate(new Rectangle(b.Left, b.Top, w, b.Height));
        }

        private void DetailTree_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _downPoint = e.Location;
                TreeNode node = DetailTree.GetNodeAt(e.Location);
                TreeNode prevSel = _selNode;
                if (node != null)
                {
                    _anchorNode = node;
                    _anchorChar = CharIndexAt(node, e.Location.X);
                }
                else
                {
                    _anchorNode = null;
                }
                SetDragActive(false);
                _selNode = null;
                _selStart = 0;
                _selEnd = 0;
                if (prevSel != null)
                    InvalidateNodeRow(prevSel);
            }
            else if (e.Button == MouseButtons.Right)
            {
                _rightClickNode = DetailTree.GetNodeAt(e.Location);
            }
        }

        private void DetailTree_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _anchorNode == null) return;
            if (!_dragActive)
            {
                Size dragSize = SystemInformation.DragSize;
                if (Math.Abs(e.X - _downPoint.X) < dragSize.Width && Math.Abs(e.Y - _downPoint.Y) < dragSize.Height)
                    return;
                SetDragActive(true);
            }
            int cur = CharIndexAt(_anchorNode, e.X);
            _selNode = _anchorNode;
            int s = Math.Min(_anchorChar, cur);
            int en = Math.Max(_anchorChar, cur);
            string t = _anchorNode.Text;
            if (s < t.Length && t[s] != ' ' && s > 0 && t[s - 1] == ' ')
            {
                while (s > 0 && t[s - 1] == ' ') s--;
            }
            _selStart = s;
            _selEnd = en;
            InvalidateNodeRow(_anchorNode);
        }

        private void DetailTree_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (!_dragActive)
                {
                    _selNode = null;
                    _selStart = 0;
                    _selEnd = 0;
                    if (DetailTree.GetNodeAt(e.Location) == null)
                        DetailTree.SelectedNode = null;
                }
                if (_anchorNode != null)
                    InvalidateNodeRow(_anchorNode);
                if (_dragActive && _selNode != null && _selStart < _selEnd)
                    AppendLog(string.Format("拖选结束: [{0},{1}) \"{2}\"", _selStart, _selEnd, _selNode.Text.Substring(_selStart, _selEnd - _selStart)));
                _anchorNode = null;
                SetDragActive(false);
            }
        }

        private static TextFormatFlags _textFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        private static readonly Size _maxSize = new Size(int.MaxValue, int.MaxValue);

        private int CharIndexAt(TreeNode node, int x)
        {
            Rectangle r = node.Bounds;
            int relX = x - r.Left;
            if (relX <= 0) return 0;
            int acc = 0;
            int idx = node.Text.Length;
            using (Graphics g = DetailTree.CreateGraphics())
            {
                for (int i = 0; i < node.Text.Length; i++)
                {
                    int w = TextRenderer.MeasureText(g, node.Text.Substring(0, i + 1), DetailTree.Font, _maxSize, _textFlags).Width
                         - TextRenderer.MeasureText(g, node.Text.Substring(0, i), DetailTree.Font, _maxSize, _textFlags).Width;
                    if (relX < acc + w / 2) { idx = i; break; }
                    acc += w;
                }
            }
            if (idx < node.Text.Length && node.Text[idx] == ' ')
            {
                while (idx > 0 && node.Text[idx - 1] == ' ') idx--;
            }
            else if (idx > 0 && node.Text[idx - 1] == ' ' && relX < acc)
            {
                idx--;
                while (idx > 0 && node.Text[idx - 1] == ' ') idx--;
            }
            return idx;
        }

        private void DetailTree_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            if (e.Node == null) return;
            Rectangle bounds = e.Bounds;
            bool selected = (e.State & TreeNodeStates.Selected) != 0;
            bool hasSubSel = _selNode == e.Node && _selStart < _selEnd;

            TextFormatFlags flags = _textFlags;

            if (hasSubSel)
            {
                int rowRight = Math.Max(bounds.Right, DetailTree.ClientSize.Width);
                e.Graphics.FillRectangle(SystemBrushes.Window, new Rectangle(bounds.Left, bounds.Top, rowRight - bounds.Left, bounds.Height));
                string text = e.Node.Text;
                int preW = TextRenderer.MeasureText(e.Graphics, text.Substring(0, _selStart), DetailTree.Font, _maxSize, flags).Width;
                int hlRight = TextRenderer.MeasureText(e.Graphics, text.Substring(0, _selEnd), DetailTree.Font, _maxSize, flags).Width;

                Rectangle pre = new Rectangle(bounds.Left, bounds.Top, preW, bounds.Height);
                Rectangle hl = new Rectangle(bounds.Left + preW, bounds.Top, Math.Max(1, hlRight - preW), bounds.Height);
                Rectangle suf = new Rectangle(bounds.Left + hlRight, bounds.Top, Math.Max(1, bounds.Width + 200 - hlRight), bounds.Height);

                TextRenderer.DrawText(e.Graphics, text.Substring(0, _selStart), DetailTree.Font, pre, SystemColors.WindowText, flags);
                e.Graphics.FillRectangle(SystemBrushes.Highlight, hl);
                TextRenderer.DrawText(e.Graphics, text.Substring(_selStart, _selEnd - _selStart), DetailTree.Font, hl, SystemColors.HighlightText, flags);
                TextRenderer.DrawText(e.Graphics, text.Substring(_selEnd), DetailTree.Font, suf, SystemColors.WindowText, flags | TextFormatFlags.NoClipping);
                return;
            }

            if (selected)
            {
                Rectangle fullRow = new Rectangle(bounds.Left, bounds.Top, Math.Max(1, DetailTree.ClientSize.Width - bounds.Left), bounds.Height);
                e.Graphics.FillRectangle(SystemBrushes.Highlight, fullRow);
                TextRenderer.DrawText(e.Graphics, e.Node.Text, DetailTree.Font, bounds, SystemColors.HighlightText, flags);
            }
            else
            {
                e.Graphics.FillRectangle(SystemBrushes.Window, bounds);
                TextRenderer.DrawText(e.Graphics, e.Node.Text, DetailTree.Font, bounds, SystemColors.WindowText, flags);
            }
        }

        private void DetailContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _copyNodeItem.Enabled = (_rightClickNode != null && _rightClickNode == DetailTree.SelectedNode);
        }

        private void DetailContextMenu_CopyNode_Click(object sender, EventArgs e)
        {
            if (_rightClickNode != null)
                SafeSetClipboard(_rightClickNode.Text);
        }

private void CopyAllText()
        {
            string text = GetVisibleTreeText();
            if (text.Length > 0)
                SafeSetClipboard(text);
        }

        private static void SafeSetClipboard(string text)
        {
            try { Clipboard.SetText(text); }
            catch (Exception) { }
        }

        private string GetVisibleTreeText()
        {
            var sb = new StringBuilder();
            foreach (TreeNode node in DetailTree.Nodes)
                AppendNodeTextVisible(node, 0, sb);
            return sb.ToString();
        }

        private static void AppendNodeTextVisible(TreeNode node, int depth, StringBuilder sb)
        {
            sb.AppendLine(new string(' ', depth * 4) + node.Text);
            if (!node.IsExpanded) return;
            foreach (TreeNode child in node.Nodes)
                AppendNodeTextVisible(child, depth + 1, sb);
        }

        private void DetailContextMenu_SaveImage_Click(object sender, EventArgs e)
        {
            SaveTreeToImage();
        }

        private void SaveTreeToImage()
        {
            string text = GetVisibleTreeText();
            if (text.Length == 0)
            {
                MessageBox.Show("没有可保存的文本内容。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var saveDialog = new SaveFileDialog())
            {
                saveDialog.Filter = "PNG 图片 (*.png)|*.png|位图 (*.bmp)|*.bmp";
                saveDialog.FileName = "时间统计_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                saveDialog.Title = "保存界面文本为图片";
                if (saveDialog.ShowDialog() != DialogResult.OK)
                    return;

                ImageFormat format = saveDialog.FilterIndex == 2 ? ImageFormat.Bmp : ImageFormat.Png;
                try
                {
                    using (var img = RenderTextToImage(text))
                        img.Save(saveDialog.FileName, format);
                    MessageBox.Show("图片已保存到:\n" + saveDialog.FileName, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("保存失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static Image RenderTextToImage(string text)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length > 0 && lines[lines.Length - 1].Length == 0)
                Array.Resize(ref lines, lines.Length - 1);

            const float fontSize = 16f;
            const int pad = 20;
            const int lineSpacing = 28;
            const float scale = 3f;
            int scaledPad = (int)(pad * scale);
            int scaledSpacing = (int)(lineSpacing * scale);
            using (Font font = new Font("新宋体", fontSize * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                float maxWidth = 0;
                using (Bitmap measureBmp = new Bitmap(1, 1))
                using (Graphics g = Graphics.FromImage(measureBmp))
                {
                    foreach (string line in lines)
                    {
                        float w = g.MeasureString(line, font).Width;
                        if (w > maxWidth) maxWidth = w;
                    }
                }

                int width = (int)Math.Ceiling(maxWidth) + scaledPad * 2;
                int height = lines.Length * scaledSpacing + scaledPad * 2;

                Bitmap bmp = new Bitmap(width, height);
                bmp.SetResolution(288f, 288f);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
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

    public static class Mp4Parse
    {
        public static Dictionary<string, double> ReadAll(List<string> files, out int fail, int threads)
        {
            var result = new ConcurrentDictionary<string, double>();
            int f = 0;
            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = threads }, path =>
            {
                double d = ParseFile(path);
                if (d >= 0) result[path] = d;
                else Interlocked.Increment(ref f);
            });
            fail = f;
            return new Dictionary<string, double>(result);
        }

        public static double ParseFile(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long fileLen = fs.Length;
                    if (fileLen < 16) return -1;

                    long tailSize = Math.Min(4L << 20, fileLen);
                    byte[] buf = new byte[tailSize];
                    while (true)
                    {
                        fs.Position = fileLen - tailSize;
                        int read = fs.Read(buf, 0, buf.Length);
                        for (int i = 0; i + 8 <= read; i++)
                        {
                            if (buf[i + 4] == 'm' && buf[i + 5] == 'o' && buf[i + 6] == 'o' && buf[i + 7] == 'v')
                            {
                                long boxStart = fileLen - tailSize + i;
                                long boxSize = BE32(buf, i);
                                if (boxSize >= 8 && boxStart + boxSize <= fileLen)
                                {
                                    double d = ParseMoov(fs, boxStart, boxSize);
                                    if (d >= 0) return d;
                                }
                            }
                        }
                        if (tailSize >= fileLen) break;
                        tailSize = Math.Min(tailSize * 2, fileLen);
                        buf = new byte[tailSize];
                    }
                    return -1;
                }
            }
            catch { return -1; }
        }

        private static double ParseMoov(FileStream fs, long boxStart, long boxSize)
        {
            try
            {
                long toRead = Math.Min(boxSize, 16L << 20);
                if (toRead < 16) return -1;
                byte[] moov = new byte[toRead];
                fs.Position = boxStart;
                int read = fs.Read(moov, 0, moov.Length);
                if (read < 16) return -1;

                long p = 8;
                while (p + 8 <= read)
                {
                    long csize = BE32(moov, p);
                    if (csize < 8) break;
                    if (moov[p + 4] == 'm' && moov[p + 5] == 'v' && moov[p + 6] == 'h' && moov[p + 7] == 'd')
                        return ParseMvhd(moov, p, csize);
                    if (p + csize > read) break;
                    p += csize;
                }
                return -1;
            }
            catch { return -1; }
        }

        private static double ParseMvhd(byte[] b, long off, long size)
        {
            try
            {
                if (size < 32) return -1;
                int version = b[off + 8];
                if (version == 0)
                {
                    uint timescale = BE32(b, off + 20);
                    uint duration = BE32(b, off + 24);
                    if (timescale == 0) return -1;
                    return duration / (double)timescale;
                }
                else if (version == 1)
                {
                    uint timescale = BE32(b, off + 28);
                    ulong duration = BE64(b, off + 32);
                    if (timescale == 0) return -1;
                    return duration / (double)timescale;
                }
                return -1;
            }
            catch { return -1; }
        }

        private static uint BE32(byte[] b, long off)
        {
            return (uint)((b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3]);
        }

        private static ulong BE64(byte[] b, long off)
        {
            return ((ulong)BE32(b, off) << 32) | (ulong)BE32(b, off + 4);
        }
    }
}
