using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VideoTime
{
    public partial class Form1 : Form
    {
        private const int MaxDetailLines = 200;
        private const int TreeTop = 176;
        private const int TreeBottomGap = 25;
        private const int ProgressTreeGap = 8;
        private const int ProgressBarLabelGap = 2;
        private System.Threading.CancellationTokenSource _scanCts;
        private ScanResult _lastResult;

        private TreeNode _selNode;
        private int _selStart;
        private int _selEnd;
        private TreeNode _anchorNode;
        private int _anchorChar;
        private Point _downPoint;
        private bool _dragActive;
        private TreeNode _rightClickNode;
        private ToolStripMenuItem _copyNodeItem;
        private ToolStripMenuItem _exportItem;

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
            _exportItem = new ToolStripMenuItem("导出报表…");
            _exportItem.Click += DetailContextMenu_ExportReport_Click;
            DetailContextMenu.Items.Add(_exportItem);
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
                ShowTime.Text = "文件夹路径无效，请重新输入。";
                AppendLog("文件夹路径无效: " + folderPath, LogLevel.Warning);
                return;
            }

            DetailTree.Nodes.Clear();
            _lastResult = null;
            bool recursive = CbSubfolders.Checked;

            ShowTime.Text = "正在扫描文件夹…";
            Start.Enabled = false;
            BtnCancel.Enabled = true;
            Cursor = Cursors.WaitCursor;
            SetProgressVisible(true);
            ProgressBar.Style = ProgressBarStyle.Marquee;
            LblProgress.Text = "正在收集目录…";

            Stopwatch sw = Stopwatch.StartNew();
            string elapsedText = "";
            var cts = new CancellationTokenSource();
            _scanCts = cts;
            var progress = new UiProgress(this, UpdateProgress);
            try
            {
                ScanResult result = await Task.Run(() => VideoScanner.Run(folderPath, recursive, cts.Token, progress), cts.Token);
                if (cts.Token.IsCancellationRequested || IsDisposed) return;
                sw.Stop();
                elapsedText = string.Format("{0:N0} 毫秒", sw.ElapsedMilliseconds);
                _lastResult = result;

                string resultText = "总时间: " + VideoScanner.Format(result.TotalSeconds);
                var issues = new List<string>();
                if (result.FailCount > 0)
                    issues.Add(result.FailCount + " 个文件读取失败");
                if (result.DirFail > 0)
                    issues.Add(result.DirFail + " 个目录无法访问");
                if (result.DepthSkipped > 0)
                    issues.Add(VideoScanner.DepthSkippedLabel(VideoScanner.MaxDepth));
                if (issues.Count > 0)
                    AppendLog("扫描完成但存在缺失: " + string.Join("；", issues) + " | " + folderPath + " | 耗时: " + elapsedText, LogLevel.Warning);
                ShowTime.Text = resultText;
                AppendLog("文件夹: " + folderPath + " | 总时间: " + VideoScanner.Format(result.TotalSeconds) + " | 耗时: " + elapsedText);

                int totalFiles = result.FolderResults.Count > 0 ? result.FolderResults[0].FileCount : 0;
                UpdateProgress(new ScanProgress { Phase = "parse", Processed = totalFiles, Total = totalFiles });
                BuildTree(result);
                BtnCancel.Enabled = false;
                await Task.Delay(500);
                if (!IsDisposed)
                    SetProgressVisible(false);

                if (issues.Count > 0)
                {
                    LogFailureDetails(result);
                    MessageBox.Show("扫描完成，但存在缺失：\n\n" + string.Join("\n", issues) + "\n\n详细原因已写入日志文件。",
                        "扫描完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                ShowTime.Text = "扫描已取消。";
                AppendLog("扫描已取消: " + folderPath, LogLevel.Info);
                ResetProgressUI();
            }
            catch (Exception ex)
            {
                AppendLog("查询异常: " + folderPath + " | " + ex.Message, LogLevel.Error);
                MessageBox.Show("查询异常: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ResetProgressUI();
            }
            finally
            {
                Start.Enabled = true;
                BtnCancel.Enabled = false;
                Cursor = Cursors.Default;
                _scanCts = null;
                cts.Dispose();
            }
        }

        private void UpdateProgress(ScanProgress p)
        {
            if (IsDisposed) return;
            if (p.Phase == "collect")
            {
                ProgressBar.Style = ProgressBarStyle.Marquee;
                LblProgress.Text = "正在收集目录…";
                return;
            }
            if (p.Phase == "parse")
            {
                ProgressBar.Style = ProgressBarStyle.Blocks;
                int pct = p.Total > 0 ? (int)(p.Processed * 100L / p.Total) : 0;
                ProgressBar.Value = Math.Max(0, Math.Min(100, pct));
                LblProgress.Text = string.Format("正在读取 {0}% ({1}/{2})", pct, p.Processed, p.Total);
            }
        }

        private void SetProgressVisible(bool visible)
        {
            if (IsDisposed) return;
            ProgressBar.Location = new Point(ProgressBar.Left, ClientSize.Height - (TreeBottomGap - ProgressTreeGap) - ProgressBar.Height);
            LblProgress.Location = new Point(LblProgress.Left, ProgressBar.Top - ProgressBarLabelGap - LblProgress.Height);
            LblProgress.Visible = visible;
            ProgressBar.Visible = visible;
            int reserve = visible ? (ProgressBar.Height + ProgressBarLabelGap + LblProgress.Height) : 0;
            DetailTree.Location = new Point(DetailTree.Left, TreeTop);
            DetailTree.Height = ClientSize.Height - TreeTop - TreeBottomGap - reserve;
        }

        private void ResetProgressUI()
        {
            if (IsDisposed) return;
            ProgressBar.Style = ProgressBarStyle.Blocks;
            ProgressBar.Value = 0;
            LblProgress.Text = "";
            SetProgressVisible(false);
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            if (_scanCts != null)
            {
                ShowTime.Text = "正在取消…";
                _scanCts.Cancel();
            }
        }

        private void BtnSettings_Click(object sender, EventArgs e)
        {
            using (var dlg = new SettingsForm())
            {
                dlg.ShowDialog(this);
            }
        }

        private void BuildTree(ScanResult result)
        {
            _selNode = null;
            _selStart = 0;
            _selEnd = 0;
            _anchorNode = null;
            SetDragActive(false);
            _rightClickNode = null;

            DetailTree.BeginUpdate();
            try
            {
                var nodeMap = new Dictionary<string, TreeNode>();
                foreach (var r in result.FolderResults)
                {
                    string folderName = Path.GetFileName(r.FolderPath);
                    if (string.IsNullOrEmpty(folderName))
                        folderName = r.FolderPath;

                    TreeNode node = new TreeNode($"{folderName}  {VideoScanner.Format(r.TotalSeconds)}  [视频{r.FileCount}]");
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
            }
            finally
            {
                DetailTree.EndUpdate();
            }
        }

        private void ExpandToDepth(TreeNode node, int currentDepth)
        {
            node.Expand();
            if (currentDepth >= 2) return;
            foreach (TreeNode child in node.Nodes)
                ExpandToDepth(child, currentDepth + 1);
        }

        private void AppendLog(string line, LogLevel level = LogLevel.Info)
        {
            Log.Append(line, level);
        }

        private void LogFailureDetails(ScanResult result)
        {
            WriteFailures(result.FailedFiles, VideoScanner.LabelFileFailed);
            WriteFailures(result.FailedDirs, VideoScanner.LabelDirFailed);

            int shown = Math.Min(MaxDetailLines, result.SkippedDirs.Count);
            for (int i = 0; i < shown; i++)
                AppendLog(VideoScanner.DepthSkippedLabel(VideoScanner.MaxDepth) + ": " + result.SkippedDirs[i], LogLevel.Warning);
            if (result.SkippedDirs.Count > MaxDetailLines)
                AppendLog("…其余省略，共 " + result.SkippedDirs.Count + " 项", LogLevel.Warning);
        }

        private void WriteFailures(List<FailureRecord> list, string label)
        {
            int shown = Math.Min(MaxDetailLines, list.Count);
            for (int i = 0; i < shown; i++)
            {
                FailureRecord it = list[i];
                AppendLog(label + ": " + it.Path + (string.IsNullOrEmpty(it.Reason) ? "" : "（" + it.Reason + "）"), LogLevel.Warning);
            }
            if (list.Count > MaxDetailLines)
                AppendLog("…其余省略，共 " + list.Count + " 项", LogLevel.Warning);
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
            SetProgressVisible(false);

            Rectangle wa = Screen.FromControl(this).WorkingArea;
            this.MaximumSize = new Size(wa.Width, wa.Height);
            this.Size = new Size(Math.Min(this.Width, wa.Width), Math.Min(this.Height, wa.Height));
        }

private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_scanCts != null)
                _scanCts.Cancel();
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

            string text = node.Text;
            int len = text.Length;
            if (len == 0) return 0;

            int[] widths = new int[len];
            int total = 0;
            using (Graphics g = DetailTree.CreateGraphics())
            {
                for (int i = 0; i < len; i++)
                {
                    int w = TextRenderer.MeasureText(g, text[i].ToString(), DetailTree.Font, _maxSize, _textFlags).Width;
                    widths[i] = w;
                    total += w;
                }
            }

            int idx = len;
            int acc = 0;
            for (int i = 0; i < len; i++)
            {
                if (relX < acc + widths[i] / 2) { idx = i; break; }
                acc += widths[i];
            }

            if (idx < text.Length && text[idx] == ' ')
            {
                while (idx > 0 && text[idx - 1] == ' ') idx--;
            }
            else if (idx > 0 && text[idx - 1] == ' ' && relX < acc)
            {
                idx--;
                while (idx > 0 && text[idx - 1] == ' ') idx--;
            }
            return idx;
        }

        private void DetailTree_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            if (e.Node == null) return;
            try
            {
                Rectangle bounds = e.Bounds;
                bool selected = (e.State & TreeNodeStates.Selected) != 0;
                string nodeText = e.Node.Text ?? string.Empty;
                int textLen = nodeText.Length;
                int selStart = Math.Max(0, Math.Min(_selStart, textLen));
                int selEnd = Math.Max(selStart, Math.Min(_selEnd, textLen));
                bool hasSubSel = _selNode == e.Node && selStart < selEnd;

                TextFormatFlags flags = _textFlags;

                if (hasSubSel)
                {
                    int rowRight = Math.Max(bounds.Right, DetailTree.ClientSize.Width);
                    e.Graphics.FillRectangle(SystemBrushes.Window, new Rectangle(bounds.Left, bounds.Top, rowRight - bounds.Left, bounds.Height));
                    string text = nodeText;
                    int preW = TextRenderer.MeasureText(e.Graphics, text.Substring(0, selStart), DetailTree.Font, _maxSize, flags).Width;
                    int hlRight = TextRenderer.MeasureText(e.Graphics, text.Substring(0, selEnd), DetailTree.Font, _maxSize, flags).Width;

                    Rectangle pre = new Rectangle(bounds.Left, bounds.Top, preW, bounds.Height);
                    Rectangle hl = new Rectangle(bounds.Left + preW, bounds.Top, Math.Max(1, hlRight - preW), bounds.Height);
                    Rectangle suf = new Rectangle(bounds.Left + hlRight, bounds.Top, Math.Max(1, bounds.Width + 200 - hlRight), bounds.Height);

                    TextRenderer.DrawText(e.Graphics, text.Substring(0, selStart), DetailTree.Font, pre, SystemColors.WindowText, flags);
                    e.Graphics.FillRectangle(SystemBrushes.Highlight, hl);
                    TextRenderer.DrawText(e.Graphics, text.Substring(selStart, selEnd - selStart), DetailTree.Font, hl, SystemColors.HighlightText, flags);
                    TextRenderer.DrawText(e.Graphics, text.Substring(selEnd), DetailTree.Font, suf, SystemColors.WindowText, flags | TextFormatFlags.NoClipping);
                    return;
                }

                if (selected)
                {
                    Rectangle fullRow = new Rectangle(bounds.Left, bounds.Top, Math.Max(1, DetailTree.ClientSize.Width - bounds.Left), bounds.Height);
                    e.Graphics.FillRectangle(SystemBrushes.Highlight, fullRow);
                    TextRenderer.DrawText(e.Graphics, nodeText, DetailTree.Font, bounds, SystemColors.HighlightText, flags);
                }
                else
                {
                    e.Graphics.FillRectangle(SystemBrushes.Window, bounds);
                    TextRenderer.DrawText(e.Graphics, nodeText, DetailTree.Font, bounds, SystemColors.WindowText, flags);
                }
            }
            catch
            {
                try
                {
                    Rectangle b = e.Bounds;
                    e.Graphics.FillRectangle(SystemBrushes.Window,
                        new Rectangle(b.Left, b.Top, Math.Max(1, DetailTree.ClientSize.Width - b.Left), b.Height));
                    TextRenderer.DrawText(e.Graphics, e.Node.Text ?? string.Empty, DetailTree.Font, b, SystemColors.WindowText, _textFlags);
                }
                catch { }
            }
        }

        private void DetailContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _copyNodeItem.Enabled = (_rightClickNode != null && _rightClickNode == DetailTree.SelectedNode);
            _exportItem.Enabled = (_lastResult != null);
        }

        private void DetailContextMenu_CopyNode_Click(object sender, EventArgs e)
        {
            if (_rightClickNode != null)
                SafeSetClipboard(_rightClickNode.Text);
        }

        private void DetailContextMenu_ExportReport_Click(object sender, EventArgs e)
        {
            if (_lastResult == null) return;
            using (var saveDialog = new SaveFileDialog())
            {
                saveDialog.Filter = "CSV 文件 (*.csv)|*.csv|HTML 报告 (*.html)|*.html";
                saveDialog.FileName = "时间统计报表_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
                saveDialog.Title = "导出报表";
                saveDialog.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
                if (saveDialog.ShowDialog() != DialogResult.OK)
                    return;
                try
                {
                    string format = saveDialog.FilterIndex == 2 ? "html" : "csv";
                    ReportExporter.Export(saveDialog.FileName, _lastResult, format);
                    AppendLog("报表已导出: " + saveDialog.FileName);
                    MessageBox.Show("报表已导出到:\n" + saveDialog.FileName, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    AppendLog("导出失败: " + ex.Message, LogLevel.Error);
                    MessageBox.Show("导出失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

private void CopyAllText()
        {
            string text = GetVisibleTreeText();
            if (text.Length > 0)
                SafeSetClipboard(text);
        }

        private void SafeSetClipboard(string text)
        {
            try { Clipboard.SetText(text); }
            catch (Exception) { AppendLog("复制到剪贴板失败", LogLevel.Warning); }
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
                AppendLog("保存图片失败: 无可保存的文本内容", LogLevel.Warning);
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
                    AppendLog("图片已保存: " + saveDialog.FileName);
                    MessageBox.Show("图片已保存到:\n" + saveDialog.FileName, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    AppendLog("保存失败: " + ex.Message, LogLevel.Error);
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

        private sealed class UiProgress : IProgress<ScanProgress>
        {
            private readonly Control _owner;
            private readonly Action<ScanProgress> _update;

            public UiProgress(Control owner, Action<ScanProgress> update)
            {
                _owner = owner;
                _update = update;
            }

            public void Report(ScanProgress value)
            {
                try
                {
                    if (_owner.IsDisposed || !_owner.IsHandleCreated) return;
                    _owner.BeginInvoke(new Action(() => _update(value)));
                }
                catch { }
            }
        }
    }
}
