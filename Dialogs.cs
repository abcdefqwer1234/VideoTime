using System;
using System.Drawing;
using System.Windows.Forms;

namespace VideoTime
{
    public static class Dialogs
    {
        public static DialogResult Show(string title, string message, MessageBoxIcon icon, params (string Text, DialogResult Result)[] buttons)
        {
            if (buttons == null || buttons.Length == 0)
                buttons = new[] { ("确定", DialogResult.OK) };

            using (var textFont = new Font("新宋体", 11F))
            using (var btnFont = new Font("新宋体", 10.5F))
            {
                const int padding = 16;
                const int iconSize = 32;
                const int buttonW = 80;
                const int buttonH = 30;
                const int gap = 24;
                const int verticalGap = 8;
                const int dialogWidth = 340;

                Image iconImage = GetIcon(icon);
                int labelX = padding + (iconImage != null ? iconSize + 16 : 0);
                int labelWidth = dialogWidth - labelX - padding;

                Size textSize = TextRenderer.MeasureText(
                    message, textFont, new Size(labelWidth, int.MaxValue),
                    TextFormatFlags.WordBreak);
                int labelHeight = textSize.Height;
                int iconTop = padding + Math.Max(0, (labelHeight - iconSize) / 2);

                int groupW = buttonW * buttons.Length + gap * (buttons.Length - 1);
                int buttonY = padding + labelHeight + verticalGap;
                int contentH = buttonY + buttonH + 14;
                int screenH = Screen.PrimaryScreen != null ? Screen.PrimaryScreen.WorkingArea.Height - 40 : 700;
                int maxDialogHeight = Math.Max(200, Math.Min(700, screenH));
                bool scroll = contentH > maxDialogHeight;
                int dialogHeight = scroll ? maxDialogHeight : contentH;

                using (var dlg = new Form
                {
                    Text = title,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    ShowInTaskbar = false,
                    ClientSize = new Size(dialogWidth, dialogHeight),
                    AutoScroll = true,
                    AutoScrollMinSize = scroll ? new Size(0, contentH) : Size.Empty
                })
                {
                    if (iconImage != null)
                    {
                        dlg.Controls.Add(new PictureBox
                        {
                            Image = iconImage,
                            SizeMode = PictureBoxSizeMode.StretchImage,
                            Location = new Point(padding, iconTop),
                            Size = new Size(iconSize, iconSize)
                        });
                    }

                    dlg.Controls.Add(new Label
                    {
                        Text = message,
                        Font = textFont,
                        Location = new Point(labelX, padding),
                        Size = new Size(labelWidth, labelHeight),
                        TextAlign = ContentAlignment.TopLeft
                    });

                    var buttonControls = new Button[buttons.Length];
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        buttonControls[i] = new Button
                        {
                            Text = buttons[i].Text,
                            DialogResult = buttons[i].Result,
                            Font = btnFont,
                            Size = new Size(buttonW, buttonH)
                        };
                        dlg.Controls.Add(buttonControls[i]);
                    }

                    if (buttons.Length > 1)
                    {
                        int x = (dialogWidth - groupW) / 2;
                        for (int i = 0; i < buttonControls.Length; i++)
                        {
                            buttonControls[i].Location = new Point(x, buttonY);
                            x += buttonControls[i].Width + gap;
                        }
                    }
                    else
                    {
                        buttonControls[0].Location = new Point(dialogWidth - padding - buttonControls[0].Width, buttonY);
                    }

                    dlg.AcceptButton = buttonControls[0];
                    dlg.CancelButton = buttonControls[buttonControls.Length - 1];
                    return dlg.ShowDialog();
                }
            }
        }

        private static readonly Bitmap IconWarning = SystemIcons.Warning.ToBitmap();
        private static readonly Bitmap IconError = SystemIcons.Error.ToBitmap();
        private static readonly Bitmap IconInformation = SystemIcons.Information.ToBitmap();
        private static readonly Bitmap IconQuestion = SystemIcons.Question.ToBitmap();

        private static Image GetIcon(MessageBoxIcon icon)
        {
            switch (icon)
            {
                case MessageBoxIcon.Warning: return IconWarning;
                case MessageBoxIcon.Error: return IconError;
                case MessageBoxIcon.Information: return IconInformation;
                case MessageBoxIcon.Question: return IconQuestion;
                default: return null;
            }
        }
    }
}
