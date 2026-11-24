using System;
using System.Windows.Forms;

namespace TideCombat
{
    internal sealed class EnhanceStatusPanel : TableLayoutPanel
    {
        private readonly Label verdict = Theme.Text("等待副词条识别", Theme.Muted);
        private readonly LinkLabel evidence = new LinkLabel
        {
            Text = "打开本次截图", AutoSize = true, LinkColor = Theme.Accent,
            ActiveLinkColor = Theme.Accent, Anchor = AnchorStyles.Right, Enabled = false
        };
        private readonly ListView rows = new ListView
        {
            View = View.Details, HeaderStyle = ColumnHeaderStyle.Nonclickable, FullRowSelect = true,
            BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, ForeColor = Theme.Ink,
            BackColor = Theme.White
        };
        private EnhanceStageReadout lastStage;
        private string evidenceDirectory;

        internal EnhanceStatusPanel()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(18, 16, 18, 14);
            Margin = Padding.Empty;
            BackColor = Theme.White;
            ColumnCount = 1;
            RowCount = 3;
            RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(Theme.Text("副词条与判定", Theme.Ink, true), 0, 0);
            header.Controls.Add(evidence, 1, 0);
            Controls.Add(header, 0, 0);
            verdict.AutoSize = false;
            verdict.AutoEllipsis = true;
            verdict.Dock = DockStyle.Fill;
            Controls.Add(verdict, 0, 1);
            rows.Columns.Add("副词条", 230);
            rows.Columns.Add("数值", 110);
            rows.Columns.Add("计入有效词条", 150);
            rows.Resize += delegate { ResizeColumns(); };
            Controls.Add(rows, 0, 2);
            evidence.LinkClicked += delegate { EnhanceLoop.OpenEvidence(evidenceDirectory); };
        }

        internal void ShowResult(EnhanceReport report)
        {
            evidenceDirectory = report == null ? null : report.EvidenceDirectory;
            evidence.Enabled = !string.IsNullOrEmpty(evidenceDirectory);
            EnhanceStageReadout stage = report == null ? null : report.LastStage;
            if (ReferenceEquals(stage, lastStage))
                return;
            lastStage = stage;
            rows.BeginUpdate();
            try
            {
                rows.Items.Clear();
                if (stage != null)
                {
                    foreach (EnhanceStatRow stat in stage.Rows)
                    {
                        var item = new ListViewItem(new[] { stat.Name, stat.Value, stat.Valid ? "计入" : "无效" });
                        if (!stat.Valid)
                            item.ForeColor = Theme.Muted;
                        rows.Items.Add(item);
                    }
                }
            }
            finally { rows.EndUpdate(); }
            verdict.Text = stage == null ? "等待副词条识别" : string.Format("第 {0} 档 · 无效 {1} 条 · 判定为{2}{3}",
                stage.Round, stage.InvalidCount, stage.Verdict,
                string.IsNullOrEmpty(stage.Reason) ? string.Empty : " · " + stage.Reason.Replace('_', ' '));
            verdict.ForeColor = stage != null && !string.IsNullOrEmpty(stage.Reason) ? Theme.Warning : Theme.Muted;
        }

        private void ResizeColumns()
        {
            int width = rows.ClientSize.Width;
            if (width < 360)
                return;
            rows.Columns[0].Width = width * 47 / 100;
            rows.Columns[1].Width = width * 20 / 100;
            rows.Columns[2].Width = width - rows.Columns[0].Width - rows.Columns[1].Width - 6;
        }
    }
}
