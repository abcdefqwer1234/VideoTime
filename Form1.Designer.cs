namespace VideoTime
{
    partial class Form1
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.TextBox_Doc = new System.Windows.Forms.TextBox();
            this.请选择文件夹 = new System.Windows.Forms.Label();
            this.ShowTime = new System.Windows.Forms.Label();
            this.Start = new System.Windows.Forms.Button();
            this.BtnBrowse = new System.Windows.Forms.Button();
            this.CbSubfolders = new System.Windows.Forms.CheckBox();
            this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
            this.DetailTree = new VideoTime.BufferedTreeView();
            this.SuspendLayout();
            // 
            // TextBox_Doc
            // 
            this.TextBox_Doc.AllowDrop = true;
            this.TextBox_Doc.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.TextBox_Doc.Font = new System.Drawing.Font("新宋体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.TextBox_Doc.Location = new System.Drawing.Point(30, 65);
            this.TextBox_Doc.Name = "TextBox_Doc";
            this.TextBox_Doc.Size = new System.Drawing.Size(300, 23);
            this.TextBox_Doc.TabIndex = 0;
            this.TextBox_Doc.DragDrop += new System.Windows.Forms.DragEventHandler(this.TextBox_Doc_DragDrop);
            this.TextBox_Doc.DragEnter += new System.Windows.Forms.DragEventHandler(this.TextBox_Doc_DragEnter);
            // 
            // 请选择文件夹
            // 
            this.请选择文件夹.AutoSize = true;
            this.请选择文件夹.Font = new System.Drawing.Font("新宋体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.请选择文件夹.Location = new System.Drawing.Point(30, 30);
            this.请选择文件夹.Name = "请选择文件夹";
            this.请选择文件夹.Size = new System.Drawing.Size(91, 14);
            this.请选择文件夹.TabIndex = 1;
            this.请选择文件夹.Text = "请选择文件夹";
            // 
            // ShowTime
            // 
            this.ShowTime.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ShowTime.Font = new System.Drawing.Font("宋体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.ShowTime.Location = new System.Drawing.Point(30, 105);
            this.ShowTime.Name = "ShowTime";
            this.ShowTime.Size = new System.Drawing.Size(360, 25);
            this.ShowTime.TabIndex = 2;
            this.ShowTime.Text = "总时间: ";
            // 
            // Start
            // 
            this.Start.Font = new System.Drawing.Font("宋体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Start.Location = new System.Drawing.Point(30, 134);
            this.Start.Name = "Start";
            this.Start.Size = new System.Drawing.Size(75, 30);
            this.Start.TabIndex = 3;
            this.Start.Text = "查询";
            this.Start.UseVisualStyleBackColor = true;
            this.Start.Click += new System.EventHandler(this.Start_Click);
            // 
            // BtnBrowse
            // 
            this.BtnBrowse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.BtnBrowse.Font = new System.Drawing.Font("宋体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.BtnBrowse.Location = new System.Drawing.Point(336, 60);
            this.BtnBrowse.Name = "BtnBrowse";
            this.BtnBrowse.Size = new System.Drawing.Size(55, 30);
            this.BtnBrowse.TabIndex = 4;
            this.BtnBrowse.Text = "浏览";
            this.BtnBrowse.UseVisualStyleBackColor = true;
            this.BtnBrowse.Click += new System.EventHandler(this.BtnBrowse_Click);
            // 
            // CbSubfolders
            // 
            this.CbSubfolders.AutoSize = true;
            this.CbSubfolders.Font = new System.Drawing.Font("新宋体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.CbSubfolders.Location = new System.Drawing.Point(181, 29);
            this.CbSubfolders.Name = "CbSubfolders";
            this.CbSubfolders.Size = new System.Drawing.Size(110, 18);
            this.CbSubfolders.TabIndex = 5;
            this.CbSubfolders.Text = "包含子文件夹";
            this.CbSubfolders.UseVisualStyleBackColor = true;
            // 
            // DetailTree
            // 
            this.DetailTree.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DetailTree.Font = new System.Drawing.Font("新宋体", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.DetailTree.FullRowSelect = true;
            this.DetailTree.HideSelection = false;
            this.DetailTree.Location = new System.Drawing.Point(30, 181);
            this.DetailTree.Name = "DetailTree";
            this.DetailTree.Size = new System.Drawing.Size(360, 301);
            this.DetailTree.TabIndex = 6;
            this.DetailTree.KeyDown += new System.Windows.Forms.KeyEventHandler(this.DetailTree_KeyDown);
            // 
            // Form1
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(420, 512);
            this.Controls.Add(this.DetailTree);
            this.Controls.Add(this.CbSubfolders);
            this.Controls.Add(this.BtnBrowse);
            this.Controls.Add(this.Start);
            this.Controls.Add(this.ShowTime);
            this.Controls.Add(this.请选择文件夹);
            this.Controls.Add(this.TextBox_Doc);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "时间统计";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResizeEnd += new System.EventHandler(this.Form1_ResizeEnd);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox TextBox_Doc;
        private System.Windows.Forms.Label 请选择文件夹;
        private System.Windows.Forms.Label ShowTime;
        private System.Windows.Forms.Button Start;
        private System.Windows.Forms.Button BtnBrowse;
        private System.Windows.Forms.CheckBox CbSubfolders;
        private System.Windows.Forms.TreeView DetailTree;
        private System.Windows.Forms.ContextMenuStrip DetailContextMenu;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog1;
    }
}

