using System;
using System.Drawing;
using System.Windows.Forms;

namespace TideCombat
{
    internal sealed class VariantsForm : Form
    {
        internal VariantsForm(CombatSettings settings)
        {
            Text = "角色变体";
            Font = Theme.FontAt(9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(360, 223);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            BackColor = Theme.White;
            var card = Theme.Row();
            card.FlowDirection = FlowDirection.TopDown;
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(18, 13, 18, 12);
            Controls.Add(card);
            var iuno = Add(card, "尤诺六链", settings.IunoC6);
            var roccia = Add(card, "洛可可六链", settings.RocciaC6);
            var chisa = Add(card, "千咲作为主输出", settings.ChisaDps);
            var suoming = Add(card, "Suoming 作为主输出", settings.SuomingDps);
            var buttons = Theme.Row();
            buttons.Margin = new Padding(0, 10, 0, 0);
            var ok = Theme.Button("保存", true);
            var cancel = Theme.Button("取消");
            cancel.DialogResult = DialogResult.Cancel;
            ok.Click += delegate
            {
                settings.IunoC6 = iuno.Checked;
                settings.RocciaC6 = roccia.Checked;
                settings.ChisaDps = chisa.Checked;
                settings.SuomingDps = suoming.Checked;
                DialogResult = DialogResult.OK;
            };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            card.Controls.Add(buttons);
            CancelButton = cancel;
            AcceptButton = ok;
        }

        private static CheckBox Add(Control parent, string label, bool enabled)
        {
            var box = new CheckBox { Text = label, Checked = enabled, AutoSize = true, Margin = new Padding(0, 3, 0, 3) };
            parent.Controls.Add(box);
            return box;
        }
    }
}
