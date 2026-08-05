namespace VideoTime
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.fileMenu = new System.Windows.Forms.ToolStripMenuItem();
            this.filterMenu = new System.Windows.Forms.ToolStripMenuItem();
            this.panelFileTab = new System.Windows.Forms.Panel();
            this.labelHint = new System.Windows.Forms.Label();
            this.btnAddRow = new System.Windows.Forms.Button();
            this.TextBox_Doc = new System.Windows.Forms.TextBox();
            this.BtnBrowse = new System.Windows.Forms.Button();
            this.BtnRemoveRow1 = new System.Windows.Forms.Button();
            this.Start = new System.Windows.Forms.Button();
            this.BtnCancel = new System.Windows.Forms.Button();
            this.CbSubfolders = new System.Windows.Forms.CheckBox();
            this.BtnSettings = new System.Windows.Forms.Button();
            this.panelFilterTab = new System.Windows.Forms.Panel();
            this.labelFilterHint = new System.Windows.Forms.Label();
            this.labelSearchName = new System.Windows.Forms.Label();
            this.txtSearchName = new System.Windows.Forms.TextBox();
            this.labelDur = new System.Windows.Forms.Label();
            this.txtDurMin = new System.Windows.Forms.TextBox();
            this.lblDurSep = new System.Windows.Forms.Label();
            this.txtDurMax = new System.Windows.Forms.TextBox();
            this.lblDurUnit = new System.Windows.Forms.Label();
            this.labelCount = new System.Windows.Forms.Label();
            this.txtCountMin = new System.Windows.Forms.TextBox();
            this.lblCountSep = new System.Windows.Forms.Label();
            this.txtCountMax = new System.Windows.Forms.TextBox();
            this.btnFilter = new System.Windows.Forms.Button();
            this.btnFilterClear = new System.Windows.Forms.Button();
            this.ShowTime = new System.Windows.Forms.TextBox();
            this.DetailTree = new VideoTime.BufferedTreeView();
            this.ProgressBar = new VideoTime.ProgressBarEx();
            this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
            this.DetailContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.menuStrip1.SuspendLayout();
            this.panelFileTab.SuspendLayout();
            this.panelFilterTab.SuspendLayout();
            this.SuspendLayout();
            // 
            // menuStrip1
            // 
            this.menuStrip1.GripMargin = new System.Windows.Forms.Padding(2, 2, 0, 2);
            this.menuStrip1.ImageScalingSize = new System.Drawing.Size(40, 40);
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileMenu,
            this.filterMenu});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(420, 47);
            this.menuStrip1.TabIndex = 0;
            // 
            // fileMenu
            // 
            this.fileMenu.Checked = true;
            this.fileMenu.CheckState = System.Windows.Forms.CheckState.Checked;
            this.fileMenu.Name = "fileMenu";
            this.fileMenu.Size = new System.Drawing.Size(101, 43);
            this.fileMenu.Text = "文件";
            this.fileMenu.Click += new System.EventHandler(this.MenuTab_Click);
            // 
            // filterMenu
            // 
            this.filterMenu.Name = "filterMenu";
            this.filterMenu.Size = new System.Drawing.Size(101, 43);
            this.filterMenu.Text = "筛选";
            this.filterMenu.Click += new System.EventHandler(this.MenuTab_Click);
            // 
            // panelFileTab
            // 
            this.panelFileTab.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
            this.panelFileTab.Controls.Add(this.labelHint);
            this.panelFileTab.Controls.Add(this.btnAddRow);
            this.panelFileTab.Controls.Add(this.TextBox_Doc);
            this.panelFileTab.Controls.Add(this.BtnBrowse);
            this.panelFileTab.Controls.Add(this.BtnRemoveRow1);
            this.panelFileTab.Controls.Add(this.Start);
            this.panelFileTab.Controls.Add(this.BtnCancel);
            this.panelFileTab.Controls.Add(this.CbSubfolders);
            this.panelFileTab.Controls.Add(this.BtnSettings);
            this.panelFileTab.Location = new System.Drawing.Point(0, 25);
            this.panelFileTab.Name = "panelFileTab";
            this.panelFileTab.Size = new System.Drawing.Size(420, 90);
            this.panelFileTab.TabIndex = 1;
            // 
            // labelHint
            // 
            this.labelHint.AutoSize = true;
            this.labelHint.Font = new System.Drawing.Font("新宋体", 10.5F);
            this.labelHint.Location = new System.Drawing.Point(30, 7);
            this.labelHint.Name = "labelHint";
            this.labelHint.Size = new System.Drawing.Size(231, 35);
            this.labelHint.TabIndex = 0;
            this.labelHint.Text = "请选择文件夹";
            // 
            // btnAddRow
            // 
            this.btnAddRow.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnAddRow.Font = new System.Drawing.Font("宋体", 12F, System.Drawing.FontStyle.Bold);
            this.btnAddRow.Location = new System.Drawing.Point(285, 4);
            this.btnAddRow.Name = "btnAddRow";
            this.btnAddRow.Size = new System.Drawing.Size(25, 22);
            this.btnAddRow.TabIndex = 1;
            this.btnAddRow.Text = "+";
            this.btnAddRow.UseVisualStyleBackColor = true;
            this.btnAddRow.Click += new System.EventHandler(this.BtnAddRow_Click);
            // 
            // TextBox_Doc
            // 
            this.TextBox_Doc.AllowDrop = true;
            this.TextBox_Doc.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
            this.TextBox_Doc.Font = new System.Drawing.Font("新宋体", 10.5F);
            this.TextBox_Doc.Location = new System.Drawing.Point(30, 28);
            this.TextBox_Doc.Name = "TextBox_Doc";
            this.TextBox_Doc.Size = new System.Drawing.Size(280, 47);
            this.TextBox_Doc.TabIndex = 2;
            this.TextBox_Doc.DragDrop += new System.Windows.Forms.DragEventHandler(this.TextBox_Doc_DragDrop);
            this.TextBox_Doc.DragEnter += new System.Windows.Forms.DragEventHandler(this.TextBox_Doc_DragEnter);
            // 
            // BtnBrowse
            // 
            this.BtnBrowse.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
            this.BtnBrowse.Font = new System.Drawing.Font("宋体", 10.5F);
            this.BtnBrowse.Location = new System.Drawing.Point(314, 28);
            this.BtnBrowse.Name = "BtnBrowse";
            this.BtnBrowse.Size = new System.Drawing.Size(45, 23);
            this.BtnBrowse.TabIndex = 3;
            this.BtnBrowse.Text = "浏览";
            this.BtnBrowse.UseVisualStyleBackColor = true;
            this.BtnBrowse.Click += new System.EventHandler(this.BtnBrowse_Click);
            // 
            // BtnRemoveRow1
            // 
            this.BtnRemoveRow1.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
            this.BtnRemoveRow1.Font = new System.Drawing.Font("宋体", 12F, System.Drawing.FontStyle.Bold);
            this.BtnRemoveRow1.Location = new System.Drawing.Point(365, 28);
            this.BtnRemoveRow1.Name = "BtnRemoveRow1";
            this.BtnRemoveRow1.Size = new System.Drawing.Size(25, 23);
            this.BtnRemoveRow1.TabIndex = 4;
            this.BtnRemoveRow1.Text = "-";
            this.BtnRemoveRow1.UseVisualStyleBackColor = true;
            this.BtnRemoveRow1.Click += new System.EventHandler(this.BtnRemoveRow_Click);
            // 
            // Start
            // 
            this.Start.Font = new System.Drawing.Font("宋体", 10.5F);
            this.Start.Location = new System.Drawing.Point(30, 55);
            this.Start.Name = "Start";
            this.Start.Size = new System.Drawing.Size(75, 28);
            this.Start.TabIndex = 5;
            this.Start.Text = "查询";
            this.Start.UseVisualStyleBackColor = true;
            this.Start.Click += new System.EventHandler(this.Start_Click);
            // 
            // BtnCancel
            // 
            this.BtnCancel.Enabled = false;
            this.BtnCancel.Font = new System.Drawing.Font("宋体", 10.5F);
            this.BtnCancel.Location = new System.Drawing.Point(115, 55);
            this.BtnCancel.Name = "BtnCancel";
            this.BtnCancel.Size = new System.Drawing.Size(75, 28);
            this.BtnCancel.TabIndex = 6;
            this.BtnCancel.Text = "取消";
            this.BtnCancel.UseVisualStyleBackColor = true;
            this.BtnCancel.Click += new System.EventHandler(this.BtnCancel_Click);
            // 
            // CbSubfolders
            // 
            this.CbSubfolders.AutoSize = true;
            this.CbSubfolders.Font = new System.Drawing.Font("新宋体", 10.5F);
            this.CbSubfolders.Location = new System.Drawing.Point(140, 7);
            this.CbSubfolders.Name = "CbSubfolders";
            this.CbSubfolders.Size = new System.Drawing.Size(269, 39);
            this.CbSubfolders.TabIndex = 7;
            this.CbSubfolders.Text = "包含子文件夹";
            this.CbSubfolders.UseVisualStyleBackColor = true;
            // 
            // BtnSettings
            // 
            this.BtnSettings.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.BtnSettings.Font = new System.Drawing.Font("宋体", 10.5F);
            this.BtnSettings.Location = new System.Drawing.Point(335, 4);
            this.BtnSettings.Name = "BtnSettings";
            this.BtnSettings.Size = new System.Drawing.Size(55, 22);
            this.BtnSettings.TabIndex = 8;
            this.BtnSettings.Text = "设置";
            this.BtnSettings.UseVisualStyleBackColor = true;
            this.BtnSettings.Click += new System.EventHandler(this.BtnSettings_Click);
            // 
            // panelFilterTab
            // 
            this.panelFilterTab.Controls.Add(this.labelFilterHint);
            this.panelFilterTab.Controls.Add(this.labelSearchName);
            this.panelFilterTab.Controls.Add(this.txtSearchName);
            this.panelFilterTab.Controls.Add(this.labelDur);
            this.panelFilterTab.Controls.Add(this.txtDurMin);
            this.panelFilterTab.Controls.Add(this.lblDurSep);
            this.panelFilterTab.Controls.Add(this.txtDurMax);
            this.panelFilterTab.Controls.Add(this.lblDurUnit);
            this.panelFilterTab.Controls.Add(this.labelCount);
            this.panelFilterTab.Controls.Add(this.txtCountMin);
            this.panelFilterTab.Controls.Add(this.lblCountSep);
            this.panelFilterTab.Controls.Add(this.txtCountMax);
            this.panelFilterTab.Controls.Add(this.btnFilter);
            this.panelFilterTab.Controls.Add(this.btnFilterClear);
            this.panelFilterTab.Location = new System.Drawing.Point(0, 25);
            this.panelFilterTab.Name = "panelFilterTab";
            this.panelFilterTab.Size = new System.Drawing.Size(420, 142);
            this.panelFilterTab.TabIndex = 2;
            this.panelFilterTab.Visible = false;
            // 
            // labelFilterHint
            // 
            this.labelFilterHint.AutoSize = true;
            this.labelFilterHint.Font = new System.Drawing.Font("新宋体", 9F);
            this.labelFilterHint.ForeColor = System.Drawing.SystemColors.GrayText;
            this.labelFilterHint.Location = new System.Drawing.Point(30, 8);
            this.labelFilterHint.Name = "labelFilterHint";
            this.labelFilterHint.Size = new System.Drawing.Size(343, 30);
            this.labelFilterHint.TabIndex = 12;
            this.labelFilterHint.Text = "筛选仅作用于叶子文件夹";
            // 
            // labelSearchName
            // 
            this.labelSearchName.AutoSize = true;
            this.labelSearchName.Font = new System.Drawing.Font("新宋体", 10.5F);
            this.labelSearchName.Location = new System.Drawing.Point(30, 34);
            this.labelSearchName.Name = "labelSearchName";
            this.labelSearchName.Size = new System.Drawing.Size(105, 35);
            this.labelSearchName.TabIndex = 0;
            this.labelSearchName.Text = "名称:";
            // 
            // txtSearchName
            // 
            this.txtSearchName.Font = new System.Drawing.Font("新宋体", 10.5F);
            this.txtSearchName.Location = new System.Drawing.Point(85, 31);
            this.txtSearchName.Name = "txtSearchName";
            this.txtSearchName.Size = new System.Drawing.Size(295, 47);
            this.txtSearchName.TabIndex = 1;
            // 
            // labelDur
            // 
            this.labelDur.AutoSize = true;
            this.labelDur.Font = new System.Drawing.Font("新宋体", 10.5F);
            this.labelDur.Location = new System.Drawing.Point(30, 62);
            this.labelDur.Name = "labelDur";
            this.labelDur.Size = new System.Drawing.Size(105, 35);
            this.labelDur.TabIndex = 2;
            this.labelDur.Text = "时长:";
            // 
            // txtDurMin
            // 
            this.txtDurMin.Font = new System.Drawing.Font("新宋体", 10.5F);
            this.txtDurMin.Location = new System.Drawing.Point(85, 59);
            this.txtDurMin.Name = "txtDurMin";
            this.txtDurMin.Size = new System.Drawing.Size(100, 47);
            this.txtDurMin.TabIndex = 3;
            // 
            // lblDurSep
            // 
            this.lblDurSep.AutoSize = true;
            this.lblDurSep.Font = new System.Drawing.Font("新宋体", 10.5F);
            this.lblDurSep.Location = new System.Drawing.Point(190, 62);
            this.lblDurSep.Name = "lblDurSep";
            this.lblDurSep.Size = new System.Drawing.Size(33, 35);
            this.lblDurSep.TabIndex = 4;
            this.lblDurSep.Text = "~";
            // 
            // txtDurMax
            // 
            this.txtDurMax.Font = new System.Drawing.Font("新宋体", 10.5F);
            this.txtDurMax.Location = new System.Drawing.Point(210, 59);
            this.txtDurMax.Name = "txtDurMax";
            this.txtDurMax.Size = new System.Drawing.Size(100, 47);
            this.txtDurMax.TabIndex = 5;
            // 
            // lblDurUnit
            // 
            this.lblDurUnit.AutoSize = true;
            this.lblDurUnit.Font = new System.Drawing.Font("新宋体", 9F);
            this.lblDurUnit.ForeColor = System.Drawing.SystemColors.GrayText;
            this.lblDurUnit.Location = new System.Drawing.Point(315, 63);
            this.lblDurUnit.Name = "lblDurUnit";
            this.lblDurUnit.Size = new System.Drawing.Size(133, 30);
            this.lblDurUnit.TabIndex = 13;
            this.lblDurUnit.Text = "（小时）";
            // 
            // labelCount
            // 
            this.labelCount.AutoSize = true;
            this.labelCount.Font = new System.Drawing.Font("新宋体", 10.5F);
            this.labelCount.Location = new System.Drawing.Point(30, 90);
            this.labelCount.Name = "labelCount";
            this.labelCount.Size = new System.Drawing.Size(105, 35);
            this.labelCount.TabIndex = 6;
            this.labelCount.Text = "数量:";
            // 
            // txtCountMin
            // 
            this.txtCountMin.Font = new System.Drawing.Font("新宋体", 10.5F);
            this.txtCountMin.Location = new System.Drawing.Point(85, 87);
            this.txtCountMin.Name = "txtCountMin";
            this.txtCountMin.Size = new System.Drawing.Size(100, 47);
            this.txtCountMin.TabIndex = 7;
            // 
            // lblCountSep
            // 
            this.lblCountSep.AutoSize = true;
            this.lblCountSep.Font = new System.Drawing.Font("新宋体", 10.5F);
            this.lblCountSep.Location = new System.Drawing.Point(190, 90);
            this.lblCountSep.Name = "lblCountSep";
            this.lblCountSep.Size = new System.Drawing.Size(33, 35);
            this.lblCountSep.TabIndex = 8;
            this.lblCountSep.Text = "~";
            // 
            // txtCountMax
            // 
            this.txtCountMax.Font = new System.Drawing.Font("新宋体", 10.5F);
            this.txtCountMax.Location = new System.Drawing.Point(210, 87);
            this.txtCountMax.Name = "txtCountMax";
            this.txtCountMax.Size = new System.Drawing.Size(100, 47);
            this.txtCountMax.TabIndex = 9;
            // 
            // btnFilter
            // 
            this.btnFilter.Font = new System.Drawing.Font("宋体", 10.5F);
            this.btnFilter.Location = new System.Drawing.Point(30, 112);
            this.btnFilter.Name = "btnFilter";
            this.btnFilter.Size = new System.Drawing.Size(75, 28);
            this.btnFilter.TabIndex = 10;
            this.btnFilter.Text = "过滤";
            this.btnFilter.UseVisualStyleBackColor = true;
            this.btnFilter.Click += new System.EventHandler(this.BtnFilter_Click);
            // 
            // btnFilterClear
            // 
            this.btnFilterClear.Font = new System.Drawing.Font("宋体", 10.5F);
            this.btnFilterClear.Location = new System.Drawing.Point(115, 112);
            this.btnFilterClear.Name = "btnFilterClear";
            this.btnFilterClear.Size = new System.Drawing.Size(75, 28);
            this.btnFilterClear.TabIndex = 11;
            this.btnFilterClear.Text = "清除";
            this.btnFilterClear.UseVisualStyleBackColor = true;
            this.btnFilterClear.Click += new System.EventHandler(this.BtnFilterClear_Click);
            // 
            // ShowTime
            // 
            this.ShowTime.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
            this.ShowTime.BackColor = System.Drawing.SystemColors.Control;
            this.ShowTime.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.ShowTime.Font = new System.Drawing.Font("宋体", 10.5F);
            this.ShowTime.Location = new System.Drawing.Point(30, 125);
            this.ShowTime.Name = "ShowTime";
            this.ShowTime.ReadOnly = true;
            this.ShowTime.Size = new System.Drawing.Size(360, 20);
            this.ShowTime.TabIndex = 10;
            this.ShowTime.TabStop = false;
            this.ShowTime.Text = "总时间: ";
            // 
            // DetailTree
            // 
            this.DetailTree.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
            this.DetailTree.DragActive = false;
            this.DetailTree.Font = new System.Drawing.Font("新宋体", 10.5F);
            this.DetailTree.FullRowSelect = true;
            this.DetailTree.HideSelection = false;
            this.DetailTree.Location = new System.Drawing.Point(30, 151);
            this.DetailTree.Name = "DetailTree";
            this.DetailTree.Size = new System.Drawing.Size(360, 320);
            this.DetailTree.TabIndex = 11;
            this.DetailTree.KeyDown += new System.Windows.Forms.KeyEventHandler(this.DetailTree_KeyDown);
            // 
            // ProgressBar
            // 
            this.ProgressBar.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
            this.ProgressBar.Location = new System.Drawing.Point(30, 544);
            this.ProgressBar.Name = "ProgressBar";
            this.ProgressBar.ProgressText = "";
            this.ProgressBar.Size = new System.Drawing.Size(360, 20);
            this.ProgressBar.TabIndex = 13;
            // 
            // DetailContextMenu
            // 
            this.DetailContextMenu.ImageScalingSize = new System.Drawing.Size(40, 40);
            this.DetailContextMenu.Name = "DetailContextMenu";
            this.DetailContextMenu.Size = new System.Drawing.Size(61, 4);
            // 
            // Form1
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(420, 570);
            this.Controls.Add(this.ProgressBar);
            this.Controls.Add(this.DetailTree);
            this.Controls.Add(this.ShowTime);
            this.Controls.Add(this.panelFilterTab);
            this.Controls.Add(this.panelFileTab);
            this.Controls.Add(this.menuStrip1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "时间统计";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResizeEnd += new System.EventHandler(this.Form1_ResizeEnd);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.panelFileTab.ResumeLayout(false);
            this.panelFileTab.PerformLayout();
            this.panelFilterTab.ResumeLayout(false);
            this.panelFilterTab.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem fileMenu;
        private System.Windows.Forms.ToolStripMenuItem filterMenu;
        private System.Windows.Forms.Panel panelFileTab;
        private System.Windows.Forms.Label labelHint;
        private System.Windows.Forms.Button btnAddRow;
        private System.Windows.Forms.TextBox TextBox_Doc;
        private System.Windows.Forms.Button BtnBrowse;
        private System.Windows.Forms.Button BtnRemoveRow1;
        private System.Windows.Forms.Button Start;
        private System.Windows.Forms.Button BtnCancel;
        private System.Windows.Forms.CheckBox CbSubfolders;
        private System.Windows.Forms.Button BtnSettings;
        private System.Windows.Forms.Panel panelFilterTab;
        private System.Windows.Forms.Label labelFilterHint;
        private System.Windows.Forms.Label labelSearchName;
        private System.Windows.Forms.TextBox txtSearchName;
        private System.Windows.Forms.Label labelDur;
        private System.Windows.Forms.TextBox txtDurMin;
        private System.Windows.Forms.Label lblDurSep;
        private System.Windows.Forms.TextBox txtDurMax;
        private System.Windows.Forms.Label lblDurUnit;
        private System.Windows.Forms.Label labelCount;
        private System.Windows.Forms.TextBox txtCountMin;
        private System.Windows.Forms.Label lblCountSep;
        private System.Windows.Forms.TextBox txtCountMax;
        private System.Windows.Forms.Button btnFilter;
        private System.Windows.Forms.Button btnFilterClear;
        private System.Windows.Forms.TextBox ShowTime;
        private VideoTime.BufferedTreeView DetailTree;
        private VideoTime.ProgressBarEx ProgressBar;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog1;
        private System.Windows.Forms.ContextMenuStrip DetailContextMenu;
    }
}
