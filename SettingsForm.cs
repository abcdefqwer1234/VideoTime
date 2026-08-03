using System;
using System.Windows.Forms;

namespace VideoTime
{
    public partial class SettingsForm : Form
    {
        public LogLevel SelectedLevel { get; private set; }

        public SettingsForm()
        {
            InitializeComponent();
            LoadLevels();
            SelectLevel(Properties.Settings.Default.LogOutputLevel);
        }

        private void LoadLevels()
        {
            cboLevel.Items.Add(new LevelItem("全部（信息 / 警告 / 错误）", LogLevel.Info));
            cboLevel.Items.Add(new LevelItem("仅警告及以上", LogLevel.Warning));
            cboLevel.Items.Add(new LevelItem("仅错误", LogLevel.Error));
            cboLevel.Items.Add(new LevelItem("关闭日志", LogLevel.Off));
        }

        private void SelectLevel(LogLevel level)
        {
            foreach (LevelItem item in cboLevel.Items)
            {
                if (item.Value == level)
                {
                    cboLevel.SelectedItem = item;
                    break;
                }
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            var item = cboLevel.SelectedItem as LevelItem;
            if (item != null)
            {
                SelectedLevel = item.Value;
                Properties.Settings.Default.LogOutputLevel = item.Value;
                Properties.Settings.Default.Save();
                Log.InvalidateLevelCache();
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private class LevelItem
        {
            public string Text { get; private set; }
            public LogLevel Value { get; private set; }

            public LevelItem(string text, LogLevel value)
            {
                Text = text;
                Value = value;
            }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
