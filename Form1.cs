using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VideoTime
{
    public partial class Form1 : Form
    {
        private const int MaxDetailLines = 200;
        private const int RowHeight = 28;
        private const int FilterPanelHeight = 142;
        private const int ContentLeft = 30;
        private const int RowTopBase = 28;
        private const int BrowseWidth = 45;
        private const int RemoveWidth = 25;
        private const int ContentGap = 6;
        private const int ContentTop = 25;

        private readonly Font RowTextBoxFont = new Font("新宋体", 10.5F);
        private readonly Font RowButtonFont = new Font("宋体", 10.5F);
        private readonly Font RowRemoveButtonFont = new Font("宋体", 12F, FontStyle.Bold);

        private CancellationTokenSource _scanCts;
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
        private TreeNode _cachedWidthsNode;
        private int[] _cachedWidths;

        private readonly List<RowControls> _inputRows = new List<RowControls>();
        private bool _suppressRowEvents;
        private FilterState _filterState;
        private bool _filterActive;
        private string _filterSearchName;

        private class RowControls
        {
            public TextBox TextBox;
            public Button BrowseBtn;
            public Button RemoveBtn;
        }

        public Form1()
        {
            InitializeComponent();

            Disposed += (s, e) =>
            {
                RowTextBoxFont.Dispose();
                RowButtonFont.Dispose();
                RowRemoveButtonFont.Dispose();
            };

            this.ClientSizeChanged += (s, e) => AdjustUpperPanelHeight();

            Size chrome = new Size(this.Width - this.ClientSize.Width, this.Height - this.ClientSize.Height);
            this.MinimumSize = new Size(420 + chrome.Width, 570 + chrome.Height);

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

            _filterSearchName = "";

            txtSearchName.KeyDown += FilterKeyDown;
            txtDurMin.KeyDown += FilterKeyDown;
            txtDurMax.KeyDown += FilterKeyDown;
            txtCountMin.KeyDown += FilterKeyDown;
            txtCountMax.KeyDown += FilterKeyDown;

            _inputRows.Add(new RowControls
            {
                TextBox = TextBox_Doc,
                BrowseBtn = BtnBrowse,
                RemoveBtn = BtnRemoveRow1
            });

            TextBox_Doc.Leave += InputRow_Leave;
            TextBox_Doc.DragEnter += TextBox_Doc_DragEnter;
            TextBox_Doc.DragDrop += TextBox_Doc_DragDrop;

            this.MouseDown += (s, e) => ClearFocus();
            panelFileTab.MouseDown += (s, e) => ClearFocus();
            panelFilterTab.MouseDown += (s, e) => ClearFocus();
        }

        private void ClearFocus()
        {
            if (ActiveControl is TextBox || ActiveControl is Button || ActiveControl is ComboBox || ActiveControl is CheckBox)
                ActiveControl = null;
        }

        private void SetDragActive(bool value)
        {
            _dragActive = value;
            if (DetailTree is BufferedTreeView btv)
                btv.DragActive = value;
        }

        #region Menu Tab Switching

        private void MenuTab_Click(object sender, EventArgs e)
        {
            var clicked = sender as ToolStripMenuItem;
            if (clicked == null) return;

            bool file = clicked == fileMenu;
            fileMenu.Checked = file;
            filterMenu.Checked = !file;
            panelFileTab.Visible = file;
            panelFilterTab.Visible = !file;

            AdjustUpperPanelHeight();
        }

        #endregion

        #region Dynamic Input Rows

        private void BtnAddRow_Click(object sender, EventArgs e)
        {
            bool hasContent = _inputRows.Any(r => !string.IsNullOrWhiteSpace(r.TextBox.Text));
            if (!hasContent) return;

            AddInputRow();
        }

        private void AddInputRow(string text = "")
        {
            int y = _inputRows.Count * RowHeight + RowTopBase;
            var txt = new TextBox
            {
                Font = RowTextBoxFont,
                Location = new Point(ContentLeft, y),
                Size = new Size(1, 23),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Text = text
            };
            var btnBrowse = new Button
            {
                Font = RowButtonFont,
                Location = new Point(ContentLeft + 1 + ContentGap, y),
                Size = new Size(BrowseWidth, 23),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Text = "浏览"
            };
            var btnRemove = new Button
            {
                Font = RowRemoveButtonFont,
                Location = new Point(ContentLeft + 1 + ContentGap + BrowseWidth, y),
                Size = new Size(RemoveWidth, 23),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Text = "-"
            };

            btnBrowse.Click += (s, ev) =>
            {
                if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                    txt.Text = folderBrowserDialog1.SelectedPath;
            };
            btnRemove.Click += BtnRemoveRow_Click;
            txt.Leave += InputRow_Leave;

            panelFileTab.Controls.Add(txt);
            panelFileTab.Controls.Add(btnBrowse);
            panelFileTab.Controls.Add(btnRemove);

            _inputRows.Add(new RowControls
            {
                TextBox = txt,
                BrowseBtn = btnBrowse,
                RemoveBtn = btnRemove
            });

            ReindexRows();
            AdjustUpperPanelHeight();
        }

        private void BtnRemoveRow_Click(object sender, EventArgs e)
        {
            if (_inputRows.Count <= 1) return;

            var btn = sender as Button;
            if (btn == null) return;

            int idx = _inputRows.FindIndex(r => r.RemoveBtn == btn);
            if (idx < 0) return;

            var row = _inputRows[idx];
            panelFileTab.Controls.Remove(row.TextBox);
            panelFileTab.Controls.Remove(row.BrowseBtn);
            panelFileTab.Controls.Remove(row.RemoveBtn);
            row.TextBox.Leave -= InputRow_Leave;
            _inputRows.RemoveAt(idx);

            ReindexRows();
            AdjustUpperPanelHeight();
        }

        private void RemoveBlankRows()
        {
            bool changed = false;
            for (int i = _inputRows.Count - 1; i >= 0; i--)
            {
                if (_inputRows.Count <= 1) break;
                if (string.IsNullOrWhiteSpace(_inputRows[i].TextBox.Text))
                {
                    var row = _inputRows[i];
                    panelFileTab.Controls.Remove(row.TextBox);
                    panelFileTab.Controls.Remove(row.BrowseBtn);
                    panelFileTab.Controls.Remove(row.RemoveBtn);
                    row.TextBox.Leave -= InputRow_Leave;
                    _inputRows.RemoveAt(i);
                    changed = true;
                }
            }
            if (changed)
            {
                ReindexRows();
                AdjustUpperPanelHeight();
            }
        }

        private void InputRow_Leave(object sender, EventArgs e)
        {
            if (_suppressRowEvents) return;
            var txt = sender as TextBox;
            if (txt == null) return;

            if (_inputRows.Count <= 1) return;

            if (string.IsNullOrWhiteSpace(txt.Text))
            {
                int idx = _inputRows.FindIndex(r => r.TextBox == txt);
                if (idx >= 0 && idx < _inputRows.Count - 1)
                {
                    var row = _inputRows[idx];
                    panelFileTab.Controls.Remove(row.TextBox);
                    panelFileTab.Controls.Remove(row.BrowseBtn);
                    panelFileTab.Controls.Remove(row.RemoveBtn);
                    row.TextBox.Leave -= InputRow_Leave;
                    _inputRows.RemoveAt(idx);
                    ReindexRows();
                    AdjustUpperPanelHeight();
                }
            }
        }

        private void ReindexRows()
        {
            int panelW = panelFileTab.ClientSize.Width;
            int removeX = panelW - ContentLeft - RemoveWidth;
            int browseX = removeX - 6 - BrowseWidth;
            int textW = Math.Max(50, browseX - ContentLeft - 4);
            for (int i = 0; i < _inputRows.Count; i++)
            {
                int y = RowTopBase + i * RowHeight;
                _inputRows[i].TextBox.Location = new Point(ContentLeft, y);
                _inputRows[i].TextBox.Width = textW;
                _inputRows[i].BrowseBtn.Location = new Point(browseX, y);
                _inputRows[i].RemoveBtn.Location = new Point(removeX, y);
            }
        }

        private void LayoutFilterPanel()
        {
            int panelW = panelFilterTab.ClientSize.Width;
            int rightEdge = panelW - ContentLeft;

            txtSearchName.Width = Math.Max(100, rightEdge - 85);

            lblDurUnit.Location = new Point(rightEdge - lblDurUnit.Width, 63);

            int rowRightLimit = lblDurUnit.Left - 6;
            LayoutRangeRow(txtDurMin, lblDurSep, txtDurMax, rowRightLimit);
            LayoutRangeRow(txtCountMin, lblCountSep, txtCountMax, rowRightLimit);
        }

        private void LayoutRangeRow(TextBox minBox, Label sep, TextBox maxBox, int rightLimit)
        {
            const int left = 85;
            const int gap = 6;
            const int minBoxW = 60;

            int sepW = sep.Width;
            int usable = rightLimit - left;
            int freeTotal = usable - (sepW + gap * 2);
            int boxW = Math.Max(minBoxW, freeTotal / 2);

            int groupW = boxW * 2 + sepW + gap * 2;
            int groupLeft = left + Math.Max(0, (usable - groupW) / 2);
            int sepStart = groupLeft + boxW + gap;
            int maxStart = sepStart + sepW + gap;

            minBox.Location = new Point(left, minBox.Top);
            minBox.Width = boxW;
            maxBox.Location = new Point(maxStart, maxBox.Top);
            maxBox.Width = Math.Min(boxW, Math.Max(1, rightLimit - maxStart));
            sep.Location = new Point(sepStart, sep.Top);
        }

        private void AdjustUpperPanelHeight()
        {
            int clientW = Math.Max(ClientSize.Width, ContentLeft * 2);
            int contentW = clientW - ContentLeft * 2;

            int filePanelH = RowTopBase + _inputRows.Count * RowHeight + 4 + 28 + 6;
            panelFileTab.Width = clientW;
            panelFileTab.Height = filePanelH;
            panelFilterTab.Width = clientW;
            panelFilterTab.Height = FilterPanelHeight;

            ReindexRows();
            LayoutFilterPanel();

            int lastRowBottom = RowTopBase + _inputRows.Count * RowHeight;
            Start.Location = new Point(ContentLeft, lastRowBottom + 4);
            BtnCancel.Location = new Point(ContentLeft + 85, lastRowBottom + 4);

            int panelH = panelFileTab.Visible ? filePanelH : FilterPanelHeight;
            int showTimeTop = ContentTop + panelH + ContentGap;
            ShowTime.Location = new Point(ContentLeft, showTimeTop);

            int treeTop = showTimeTop + ShowTime.Height + ContentGap;

            // Always reserve just enough room at the bottom for the progress bar,
            // so the tree box never moves when the progress bar toggles.
            int progressH = ProgressBar.Height;
            int bottomReserve = ContentGap + progressH;
            int availTreeH = ClientSize.Height - treeTop - ContentGap - bottomReserve;
            int treeH = Math.Max(0, availTreeH);

            DetailTree.Location = new Point(ContentLeft, treeTop);
            DetailTree.Size = new Size(contentW, treeH);

            ProgressBar.Location = new Point(ContentLeft, treeTop + treeH + ContentGap);
            ProgressBar.Width = contentW;
        }

        #endregion

        #region Scanning

        private async void Start_Click(object sender, EventArgs e)
        {
            RemoveBlankRows();

            var validPaths = new List<string>();
            var invalidPaths = new List<string>();
            var overlapWarnings = new List<string>();

            foreach (var row in _inputRows)
            {
                string path = VideoScanner.NormalizePath(row.TextBox.Text);
                if (string.IsNullOrWhiteSpace(path)) continue;

                if (!Directory.Exists(path))
                    invalidPaths.Add(path);
                else
                    validPaths.Add(path);
            }

            if (validPaths.Count == 0 && invalidPaths.Count == 0)
            {
                ShowTime.Text = "文件夹路径无效，请重新输入。";
                Log.Append("文件夹路径为空", LogLevel.Warning);
                return;
            }

            if (validPaths.Count == 0 && invalidPaths.Count > 0)
            {
                ShowTime.Text = "文件夹路径无效，请重新输入。";
                Log.Append("文件夹路径无效: " + string.Join(", ", invalidPaths), LogLevel.Warning);
                return;
            }

            if (invalidPaths.Count > 0 && validPaths.Count + invalidPaths.Count > 1)
            {
                string msg = "以下路径无效:\n" + string.Join("\n", invalidPaths) + "\n是否忽略并继续扫描其余路径？";
                var result = Dialogs.Show("无效路径", msg, MessageBoxIcon.Warning, this, ("忽略", DialogResult.Yes), ("取消", DialogResult.No));
                if (result == DialogResult.No)
                {
                    ShowTime.Text = "扫描已取消。";
                    return;
                }
                Log.Append("部分路径无效: " + string.Join(", ", invalidPaths) + "，忽略后继续扫描", LogLevel.Warning);
            }

            if (validPaths.Count > 1)
                DetectOverlaps(validPaths, overlapWarnings);

            if (overlapWarnings.Count > 0)
            {
                string msg = "检测到以下重叠:\n\n" + string.Join("\n", overlapWarnings) + "\n\n仍继续扫描（允许重复计时）？";
                var result = Dialogs.Show("重叠提示", msg, MessageBoxIcon.Information, this, ("是", DialogResult.Yes), ("否", DialogResult.No));
                if (result == DialogResult.No)
                {
                    ShowTime.Text = "扫描已取消。";
                    return;
                }
            }

            ClearFilterInternal();

            DetailTree.Nodes.Clear();
            _lastResult = null;
            bool recursive = CbSubfolders.Checked;

            ShowTime.Text = "正在扫描文件夹…";
            Start.Enabled = false;
            BtnCancel.Enabled = true;
            Cursor = Cursors.WaitCursor;
            SetProgressVisible(true);
            ProgressBar.SetIndeterminate(true);
            ProgressBar.ProgressText = "正在收集目录…";

            Stopwatch sw = Stopwatch.StartNew();
            string elapsedText = "";
            var cts = new CancellationTokenSource();
            _scanCts = cts;
            var progress = new UiProgress(this, UpdateProgress);
            try
            {
                string[] roots = validPaths.ToArray();
                ScanResult result = await Task.Run(() => VideoScanner.RunMultiple(roots, recursive, cts.Token, progress), cts.Token);
                if (cts.Token.IsCancellationRequested || IsDisposed)
                {
                    ResetProgressUI();
                    if (!IsDisposed)
                        ShowTime.Text = "扫描已取消。";
                    Log.Append("扫描已取消", LogLevel.Info);
                    return;
                }
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
                {
                    Log.Append("扫描完成但存在缺失: " + string.Join("；", issues) + " | " + string.Join(", ", validPaths) + " | 耗时: " + elapsedText, LogLevel.Warning);
                    LogFailureDetails(result);
                }
                ShowTime.Text = resultText;
                Log.Append("文件夹: " + string.Join(", ", validPaths) + " | 总时间: " + VideoScanner.Format(result.TotalSeconds) + " | 耗时: " + elapsedText);

                int totalFiles = result.TotalFileCount;
                UpdateProgress(new ScanProgress { Phase = "parse", Processed = totalFiles, Total = totalFiles });
                BuildTree(result);
                BtnCancel.Enabled = false;
                await Task.Delay(500);
                if (!IsDisposed)
                    SetProgressVisible(false);

                if (issues.Count > 0)
                {
                    Dialogs.Show("扫描完成", "扫描完成，但存在缺失：\n\n" + string.Join("\n", issues) + "\n\n详细原因已写入日志文件。",
                        MessageBoxIcon.Warning, this);
                }
            }
            catch (OperationCanceledException)
            {
                ShowTime.Text = "扫描已取消。";
                Log.Append("扫描已取消", LogLevel.Info);
                ResetProgressUI();
            }
            catch (Exception ex)
            {
                Log.Append("查询异常: " + ex.Message, LogLevel.Error);
                Dialogs.Show("错误", "查询异常: " + ex.Message, MessageBoxIcon.Error, this);
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

        private void DetectOverlaps(List<string> paths, List<string> warnings)
        {
            for (int i = 0; i < paths.Count; i++)
            {
                for (int j = i + 1; j < paths.Count; j++)
                {
                    string a = VideoScanner.NormalizePath(paths[i]).TrimEnd('\\');
                    string b = VideoScanner.NormalizePath(paths[j]).TrimEnd('\\');

                    if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add("重复路径: " + paths[i]);
                    }
                    else if (a.StartsWith(b + "\\", StringComparison.OrdinalIgnoreCase) ||
                             b.StartsWith(a + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add("嵌套包含: " + paths[i] + " 与 " + paths[j]);
                    }
                }
            }
        }

        private void UpdateProgress(ScanProgress p)
        {
            if (IsDisposed) return;
            if (p.Phase == "collect")
            {
                ProgressBar.SetIndeterminate(true);
                ProgressBar.ProgressText = "正在收集目录…";
                return;
            }
            if (p.Phase == "parse")
            {
                ProgressBar.SetIndeterminate(false);
                int pct = p.Total > 0 ? (int)(p.Processed * 100L / p.Total) : 0;
                ProgressBar.Value = Math.Max(0, Math.Min(100, pct));
                ProgressBar.ProgressText = string.Format("正在读取 {0}% ({1}/{2})", pct, p.Processed, p.Total);
            }
        }

        private void SetProgressVisible(bool visible)
        {
            if (IsDisposed) return;
            ProgressBar.Visible = visible;
            if (!visible)
                ProgressBar.SetIndeterminate(false);
            AdjustUpperPanelHeight();
        }

        private void ResetProgressUI()
        {
            if (IsDisposed) return;
            ProgressBar.SetIndeterminate(false);
            ProgressBar.Value = 0;
            ProgressBar.ProgressText = "";
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

        #endregion

        #region Tree Building

        private void BuildTree(ScanResult result)
        {
            _selNode = null;
            _selStart = 0;
            _selEnd = 0;
            _anchorNode = null;
            _cachedWidthsNode = null;
            _cachedWidths = null;
            SetDragActive(false);
            _rightClickNode = null;

            DetailTree.BeginUpdate();
            try
            {
                VideoScanner.EnsureFolderSet(result);
                HashSet<string> folderSet = result.FolderSet;

                var roots = new List<FolderResult>();
                foreach (var r in result.FolderResults)
                {
                    string parentPath = Path.GetDirectoryName(r.FolderPath);
                    string normParent = parentPath == null ? null : VideoScanner.NormalizePath(parentPath).TrimEnd('\\');
                    if (normParent == null || !folderSet.Contains(normParent))
                        roots.Add(r);
                }

                roots.Sort((a, b) => string.Compare(
                    Path.GetFileName(a.FolderPath) ?? a.FolderPath,
                    Path.GetFileName(b.FolderPath) ?? b.FolderPath,
                    StringComparison.OrdinalIgnoreCase));

                var isRootSet = new HashSet<FolderResult>(roots);
                var nameCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var root in roots)
                {
                    string nm = Path.GetFileName(root.FolderPath) ?? root.FolderPath;
                    nameCount[nm] = nameCount.ContainsKey(nm) ? nameCount[nm] + 1 : 1;
                }

                var nodeMap = new Dictionary<string, TreeNode>();
                foreach (var r in result.FolderResults)
                {
                    string folderName = Path.GetFileName(r.FolderPath);
                    if (string.IsNullOrEmpty(folderName))
                        folderName = r.FolderPath;

                    if (isRootSet.Contains(r) && nameCount.ContainsKey(folderName) && nameCount[folderName] > 1)
                        folderName = r.FolderPath;

                    TreeNode node = new TreeNode($"{folderName}  {VideoScanner.Format(r.TotalSeconds)}  [视频{r.FileCount}]");
                    node.Tag = r.FolderPath;

                    string parentDir = Path.GetDirectoryName(r.FolderPath);
                    if (parentDir != null && nodeMap.TryGetValue(parentDir, out TreeNode parent))
                        parent.Nodes.Add(node);
                    else
                        DetailTree.Nodes.Add(node);

                    nodeMap[r.FolderPath] = node;
                }
                foreach (TreeNode node in DetailTree.Nodes)
                    ExpandToDepth(node, 0);
            }
            finally
            {
                DetailTree.EndUpdate();
            }
        }

        private void ExpandToDepth(TreeNode node, int currentDepth)
        {
            node.Expand();
            if (currentDepth >= 1) return;
            foreach (TreeNode child in node.Nodes)
                ExpandToDepth(child, currentDepth + 1);
        }

        #endregion

        #region Filter

        private void BtnFilter_Click(object sender, EventArgs e)
        {
            ApplyFilterInternal();
        }

        private void BtnFilterClear_Click(object sender, EventArgs e)
        {
            ClearFilterInternal();
        }

        private void ApplyFilterInternal()
        {
            if (_lastResult == null) return;

            var opt = new FilterOptions
            {
                Name = txtSearchName.Text.Trim(),
                DurationMinHours = ParseDoubleOrNull(txtDurMin.Text),
                DurationMaxHours = ParseDoubleOrNull(txtDurMax.Text),
                CountMin = ParseIntOrNull(txtCountMin.Text),
                CountMax = ParseIntOrNull(txtCountMax.Text)
            };

            var invalidFields = new List<string>();
            if (!string.IsNullOrWhiteSpace(txtDurMin.Text) && !opt.DurationMinHours.HasValue) invalidFields.Add("时长下限");
            if (!string.IsNullOrWhiteSpace(txtDurMax.Text) && !opt.DurationMaxHours.HasValue) invalidFields.Add("时长上限");
            if (!string.IsNullOrWhiteSpace(txtCountMin.Text) && !opt.CountMin.HasValue) invalidFields.Add("数量下限");
            if (!string.IsNullOrWhiteSpace(txtCountMax.Text) && !opt.CountMax.HasValue) invalidFields.Add("数量上限");
            if (invalidFields.Count > 0)
            {
                Dialogs.Show("筛选输入无效", "以下筛选条件无法识别，请输入数字：\n\n" + string.Join("\n", invalidFields.Select(f => "· " + f)), MessageBoxIcon.Warning, this);
                return;
            }

            if (!opt.IsActive)
            {
                ClearFilterInternal();
                return;
            }

            _filterActive = true;
            _filterSearchName = opt.Name;

            _filterState = TreeFilter.ApplyFilter(DetailTree, opt, _filterState);

            double filteredTotal = 0;
            int filteredCount = 0;
            TreeFilter.CollectFilteredStats(DetailTree.Nodes, ref filteredTotal, ref filteredCount);

            ShowTime.Text = "过滤合计: " + VideoScanner.Format(filteredTotal) + "  [视频" + filteredCount + "]";
        }

        private void ClearFilterInternal()
        {
            if (_filterState != null)
            {
                TreeFilter.ClearFilter(DetailTree, _filterState);
                _filterState = null;
            }
            _filterActive = false;
            _filterSearchName = "";

            if (_lastResult != null)
                ShowTime.Text = "总时间: " + VideoScanner.Format(_lastResult.TotalSeconds);
        }

        private void FilterKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                ApplyFilterInternal();
            }
        }

        private static double? ParseDoubleOrNull(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = text.Trim();
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out double val)) return val;
            if (t.IndexOf(',') >= 0 && double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out val)) return val;
            return null;
        }

        private static int? ParseIntOrNull(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = text.Trim();
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val)) return val;
            if (t.IndexOf(',') >= 0 && int.TryParse(t.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out val)) return val;
            return null;
        }

        #endregion

        #region Existing functionality

        private void LogFailureDetails(ScanResult result)
        {
            WriteFailures(result.FailedFiles, VideoScanner.LabelFileFailed);
            WriteFailures(result.FailedDirs, VideoScanner.LabelDirFailed);

            int shown = Math.Min(MaxDetailLines, result.SkippedDirs.Count);
            for (int i = 0; i < shown; i++)
                Log.Append(VideoScanner.DepthSkippedLabel(VideoScanner.MaxDepth) + ": " + result.SkippedDirs[i], LogLevel.Warning);
            if (result.SkippedDirs.Count > MaxDetailLines)
                Log.Append("…其余省略，共 " + result.SkippedDirs.Count + " 项", LogLevel.Warning);
        }

        private void WriteFailures(List<FailureRecord> list, string label)
        {
            int shown = Math.Min(MaxDetailLines, list.Count);
            for (int i = 0; i < shown; i++)
            {
                FailureRecord it = list[i];
                Log.Append(label + ": " + it.Path + (string.IsNullOrEmpty(it.Reason) ? "" : "（" + it.Reason + "）"), LogLevel.Warning);
            }
            if (list.Count > MaxDetailLines)
                Log.Append("…其余省略，共 " + list.Count + " 项", LogLevel.Warning);
        }

        private void BtnSettings_Click(object sender, EventArgs e)
        {
            using (var dlg = new SettingsForm())
            {
                dlg.ShowDialog(this);
            }
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                TextBox_Doc.Text = folderBrowserDialog1.SelectedPath;
                ResetTextBoxSelection();
            }
        }

        private void ResetTextBoxSelection()
        {
            TextBox_Doc.SelectionStart = 0;
            TextBox_Doc.SelectionLength = 0;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            string saved = Properties.Settings.Default.RootPaths;
            if (!string.IsNullOrWhiteSpace(saved))
            {
                string[] paths = saved.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                _suppressRowEvents = true;

                while (_inputRows.Count > 1)
                {
                    var row = _inputRows[_inputRows.Count - 1];
                    panelFileTab.Controls.Remove(row.TextBox);
                    panelFileTab.Controls.Remove(row.BrowseBtn);
                    panelFileTab.Controls.Remove(row.RemoveBtn);
                    row.TextBox.Leave -= InputRow_Leave;
                    _inputRows.RemoveAt(_inputRows.Count - 1);
                }

                if (paths.Length > 0)
                    _inputRows[0].TextBox.Text = paths[0];

                for (int i = 1; i < paths.Length; i++)
                    AddInputRow(paths[i]);

                _suppressRowEvents = false;
            }
            else
            {
                string oldPath = Properties.Settings.Default.FolderPath;
                if (!string.IsNullOrWhiteSpace(oldPath))
                    TextBox_Doc.Text = oldPath;
            }

            CbSubfolders.Checked = Properties.Settings.Default.IncludeSubfolders;
            ResetTextBoxSelection();
            SetProgressVisible(false);
            AdjustUpperPanelHeight();

            Rectangle wa = Screen.FromControl(this).WorkingArea;
            this.MaximumSize = new Size(wa.Width, wa.Height);
            this.Size = new Size(Math.Min(this.Width, wa.Width), Math.Min(this.Height, wa.Height));
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_scanCts != null)
                _scanCts.Cancel();

            var paths = new List<string>();
            foreach (var row in _inputRows.ToArray())
            {
                string t = row.TextBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(t))
                    paths.Add(t);
            }

            Properties.Settings.Default.RootPaths = string.Join(";", paths);
            Properties.Settings.Default.FolderPath = TextBox_Doc.Text;
            Properties.Settings.Default.IncludeSubfolders = CbSubfolders.Checked;
            Properties.Settings.Default.Save();
        }

        private void Form1_ResizeEnd(object sender, EventArgs e)
        {
            AdjustUpperPanelHeight();
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
                string text = _selNode.Text ?? string.Empty;
                int len = text.Length;
                int s = Math.Max(0, Math.Min(_selStart, len));
                int e = Math.Max(s, Math.Min(_selEnd, len));
                if (s < e)
                {
                    SafeSetClipboard(text.Substring(s, e - s));
                    return;
                }
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
                _cachedWidthsNode = null;
                _cachedWidths = null;
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

        private static readonly TextFormatFlags _textFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        private static readonly Size _maxSize = new Size(int.MaxValue, int.MaxValue);

        private int CharIndexAt(TreeNode node, int x)
        {
            Rectangle r = node.Bounds;
            int relX = x - r.Left;
            if (relX <= 0) return 0;

            string text = node.Text;
            int len = text.Length;
            if (len == 0) return 0;

            int[] widths;
            if (node == _cachedWidthsNode && _cachedWidths != null && _cachedWidths.Length == len)
            {
                widths = _cachedWidths;
            }
            else
            {
                widths = new int[len];
                using (Graphics g = DetailTree.CreateGraphics())
                {
                    for (int i = 0; i < len; i++)
                        widths[i] = TextRenderer.MeasureText(g, text[i].ToString(), DetailTree.Font, _maxSize, _textFlags).Width;
                }
                _cachedWidthsNode = node;
                _cachedWidths = widths;
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
                    int sufX = bounds.Left + hlRight;
                    int sufRight = Math.Max(sufX, DetailTree.ClientSize.Width);
                    Rectangle suf = new Rectangle(sufX, bounds.Top, Math.Max(1, sufRight - sufX), bounds.Height);

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
                else if (_filterActive && !string.IsNullOrEmpty(_filterSearchName))
                {
                    DrawFilterHighlight(e, bounds, nodeText, flags);
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

        private void DrawFilterHighlight(DrawTreeNodeEventArgs e, Rectangle bounds, string nodeText, TextFormatFlags flags)
        {
            string name = TreeFilter.ExtractName(nodeText);
            List<int> positions = TreeFilter.FindSubstringPositions(name, _filterSearchName);
            if (positions.Count == 0)
            {
                e.Graphics.FillRectangle(SystemBrushes.Window, bounds);
                TextRenderer.DrawText(e.Graphics, nodeText, DetailTree.Font, bounds, SystemColors.WindowText, flags);
                return;
            }

            int rowRight = Math.Max(bounds.Right, DetailTree.ClientSize.Width);
            e.Graphics.FillRectangle(SystemBrushes.Window, new Rectangle(bounds.Left, bounds.Top, rowRight - bounds.Left, bounds.Height));

            int cursor = 0;
            foreach (int pos in positions)
            {
                int matchLen = _filterSearchName.Length;
                if (pos > cursor)
                    DrawTextSegment(e.Graphics, nodeText, cursor, pos - cursor, bounds, false, flags);
                DrawTextSegment(e.Graphics, nodeText, pos, matchLen, bounds, true, flags);
                cursor = pos + matchLen;
            }
            if (cursor < nodeText.Length)
                DrawTextSegment(e.Graphics, nodeText, cursor, nodeText.Length - cursor, bounds, false, flags);
        }

        private void DrawTextSegment(Graphics g, string text, int start, int len, Rectangle bounds, bool highlight, TextFormatFlags flags)
        {
            int preW = TextRenderer.MeasureText(g, text.Substring(0, start), DetailTree.Font, _maxSize, flags).Width;
            int segW = TextRenderer.MeasureText(g, text.Substring(0, start + len), DetailTree.Font, _maxSize, flags).Width - preW;
            Rectangle seg = new Rectangle(bounds.Left + preW, bounds.Top, Math.Max(1, segW), bounds.Height);

            if (highlight)
            {
                g.FillRectangle(SystemBrushes.Highlight, seg);
                TextRenderer.DrawText(g, text.Substring(start, len), DetailTree.Font, seg, SystemColors.HighlightText, flags);
            }
            else
            {
                TextRenderer.DrawText(g, text.Substring(start, len), DetailTree.Font, seg, SystemColors.WindowText, flags);
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
                    Log.Append("报表已导出: " + saveDialog.FileName);
                    Dialogs.Show("完成", "报表已导出到:\n" + saveDialog.FileName, MessageBoxIcon.Information, this);
                }
                catch (Exception ex)
                {
                    Log.Append("导出失败: " + ex.Message, LogLevel.Error);
                    Dialogs.Show("错误", "导出失败: " + ex.Message, MessageBoxIcon.Error, this);
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
            catch (Exception) { Log.Append("复制到剪贴板失败", LogLevel.Warning); }
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
                Log.Append("保存图片失败: 无可保存的文本内容", LogLevel.Warning);
                Dialogs.Show("提示", "没有可保存的文本内容。", MessageBoxIcon.Information, this);
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
                    using (var img = TextImageRenderer.Render(text))
                        img.Save(saveDialog.FileName, format);
                    Log.Append("图片已保存: " + saveDialog.FileName);
                    Dialogs.Show("完成", "图片已保存到:\n" + saveDialog.FileName, MessageBoxIcon.Information, this);
                }
                catch (Exception ex)
                {
                    Log.Append("保存失败: " + ex.Message, LogLevel.Error);
                    Dialogs.Show("错误", "保存失败: " + ex.Message, MessageBoxIcon.Error, this);
                }
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

        #endregion
    }
}
