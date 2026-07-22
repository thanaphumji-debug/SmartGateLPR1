using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace SmartGateLPR1
{
    public class OwnerInfoForm : Form
    {
        private long userId;
        private DatabaseHelper db = new DatabaseHelper();
        private Dictionary<string, TextBox> boxes = new Dictionary<string, TextBox>();

        public OwnerInfoForm(long id)
        {
            userId = id;
            db.EnsureVehicleDetailsTable();

            Text = "ข้อมูลเจ้าของรถ (เล่มทะเบียน)";
            ClientSize = new Size(560, 640);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            Panel panel = new Panel { Left = 0, Top = 0, Width = 560, Height = 580, AutoScroll = true };
            Controls.Add(panel);

            int y = 10;
            AddHeader(panel, "ข้อมูลรถ", ref y);
            AddField(panel, "veh_type", "ประเภทรถ", ref y);
            AddField(panel, "brand", "ยี่ห้อรถ", ref y);
            AddField(panel, "model_year", "รุ่นปี (ค.ศ.)", ref y);
            AddField(panel, "chassis_no", "เลขตัวรถ", ref y);
            AddField(panel, "engine_brand", "ยี่ห้อเครื่องยนต์", ref y);
            AddField(panel, "engine_no", "เลขเครื่องยนต์", ref y);

            AddHeader(panel, "ผู้ถือกรรมสิทธิ์", ref y);
            AddField(panel, "owner_name", "ชื่อ", ref y);
            AddField(panel, "owner_addr", "ที่อยู่", ref y);
            AddField(panel, "owner_birth", "วันเกิด", ref y);
            AddField(panel, "owner_nationality", "สัญชาติ", ref y);

            AddHeader(panel, "ผู้ครอบครอง", ref y);
            AddField(panel, "holder_name", "ชื่อ", ref y);
            AddField(panel, "holder_addr", "ที่อยู่", ref y);
            AddField(panel, "holder_birth", "วันเกิด", ref y);
            AddField(panel, "holder_nationality", "สัญชาติ", ref y);

            Button btnSave = new Button { Text = "บันทึก", Width = 90, Left = 350, Top = 595 };
            btnSave.Click += BtnSave_Click;
            Button btnCancel = new Button { Text = "ปิด", Width = 90, Left = 450, Top = 595 };
            btnCancel.Click += (s, e) => Close();
            Controls.Add(btnSave);
            Controls.Add(btnCancel);

            LoadExisting();
        }

        private void AddHeader(Panel p, string text, ref int y)
        {
            y += 6;
            Label lbl = new Label
            {
                Text = "▎ " + text,
                Left = 6,
                Top = y,
                Width = 500,
                Font = new Font("Tahoma", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 60, 120)
            };
            p.Controls.Add(lbl);
            y += 30;
        }

        private void AddField(Panel p, string key, string label, ref int y)
        {
            Label lbl = new Label { Text = label + ":", Left = 12, Top = y + 3, Width = 130 };
            TextBox tb = new TextBox { Left = 150, Top = y, Width = 370 };
            p.Controls.Add(lbl);
            p.Controls.Add(tb);
            boxes[key] = tb;
            y += 32;
        }

        private void LoadExisting()
        {
            DataRow row = db.GetVehicleDetails(userId);
            if (row == null) return;
            foreach (var kv in boxes)
                if (row.Table.Columns.Contains(kv.Key) && row[kv.Key] != DBNull.Value)
                    kv.Value.Text = row[kv.Key].ToString();
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            var data = new Dictionary<string, string>();
            foreach (var kv in boxes) data[kv.Key] = kv.Value.Text.Trim();
            db.SaveVehicleDetails(userId, data);
            MessageBox.Show("บันทึกข้อมูลเจ้าของรถแล้ว", "ข้อมูลเจ้าของรถ",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }
}