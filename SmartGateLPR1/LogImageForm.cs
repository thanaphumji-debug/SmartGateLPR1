using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SmartGateLPR1
{
    /// <summary>หน้าต่างดูภาพของประวัติ 1 รายการ (มุมกว้าง + ซูมป้าย ทั้งกล้องหน้า-หลัง)</summary>
    public class LogImageForm : Form
    {
        public LogImageForm(string title, string wide1, string wide2, string plate1, string plate2)
        {
            Text = "ภาพบันทึก — " + title;
            ClientSize = new Size(1000, 660);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(700, 500);

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(8)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 62));   // มุมกว้างให้พื้นที่มากกว่า
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 38));

            grid.Controls.Add(MakePanel("กล้องหน้า — มุมกว้าง", wide1), 0, 0);
            grid.Controls.Add(MakePanel("กล้องหลัง — มุมกว้าง", wide2), 1, 0);
            grid.Controls.Add(MakePanel("กล้องหน้า — ป้ายทะเบียน", plate1), 0, 1);
            grid.Controls.Add(MakePanel("กล้องหลัง — ป้ายทะเบียน", plate2), 1, 1);

            Controls.Add(grid);
        }

        private Control MakePanel(string caption, string path)
        {
            var box = new GroupBox
            {
                Text = caption,
                Dock = DockStyle.Fill,
                Font = new Font("Tahoma", 9, FontStyle.Bold)
            };

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                box.Controls.Add(new Label
                {
                    Text = string.IsNullOrWhiteSpace(path) ? "— ไม่ได้บันทึกภาพ —" : "— ไม่พบไฟล์ภาพ —",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.Gray,
                    Font = new Font("Tahoma", 10)
                });
                return box;
            }

            var pic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
            try
            {
                // อ่านผ่าน stream แล้วก๊อป กันไฟล์ถูกล็อกค้าง (เปิดดูแล้วยังลบไฟล์ได้)
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var tmp = Image.FromStream(fs))
                    pic.Image = new Bitmap(tmp);
            }
            catch (Exception ex)
            {
                box.Controls.Add(new Label
                {
                    Text = "เปิดภาพไม่ได้: " + ex.Message,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.Red
                });
                return box;
            }

            pic.DoubleClick += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
                catch { }
            };
            var tip = new ToolTip();
            tip.SetToolTip(pic, "ดับเบิลคลิกเพื่อเปิดด้วยโปรแกรมดูรูปของ Windows");

            box.Controls.Add(pic);
            return box;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            foreach (Control c in Controls) DisposeImages(c);
            base.OnFormClosed(e);
        }

        private void DisposeImages(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is PictureBox p && p.Image != null) { p.Image.Dispose(); p.Image = null; }
                if (c.HasChildren) DisposeImages(c);
            }
        }
    }
}