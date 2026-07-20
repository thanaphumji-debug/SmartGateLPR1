using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SmartGateLPR1
{
    public class StorageSettingsForm : Form
    {
        private RadioButton rbLocal = new RadioButton { Text = "เก็บในคอมพิวเตอร์ (SQLite)", Left = 20, Top = 15, Width = 260, Checked = true };
        private RadioButton rbCloud = new RadioButton { Text = "เก็บบน Cloud ของตัวเอง", Left = 20, Top = 40, Width = 260 };
        private GroupBox grpLocal = new GroupBox { Text = "ที่เก็บในเครื่อง", Left = 20, Top = 72, Width = 470, Height = 95 };
        private GroupBox grpCloud = new GroupBox { Text = "การเชื่อมต่อ Cloud", Left = 20, Top = 177, Width = 470, Height = 315 };

        private TextBox txtPath = new TextBox();
        private ComboBox cboType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private Label[] lbl = new Label[6];
        private TextBox[] box = new TextBox[6];
        private Button btnBrowseRemote = new Button { Text = "เลือก...", Width = 70 };
        private CheckBox chkSsl = new CheckBox { Text = "ใช้ SSL/TLS (เข้ารหัสการเชื่อมต่อ)", Width = 250 };

        private static readonly string[] CloudTypes = {
            "MySQL / MariaDB  (ฐานข้อมูลบน server)",
            "PostgreSQL  (ฐานข้อมูลบน server)",
            "Nextcloud / WebDAV  (เก็บไฟล์)",
            "S3 / MinIO  (เก็บไฟล์แบบ Bucket)"
        };

        public StorageSettingsForm()
        {
            Text = "ตั้งค่าที่เก็บข้อมูล";
            ClientSize = new Size(510, 550);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            // --- กลุ่ม Local ---
            grpLocal.Controls.Add(new Label { Text = "ไฟล์ฐานข้อมูล (.db):", Left = 15, Top = 25, Width = 140 });
            txtPath.SetBounds(15, 50, 330, 24);
            var btnBrowse = new Button { Text = "เลือกโฟลเดอร์...", Left = 355, Top = 48, Width = 100 };
            btnBrowse.Click += (s, e) =>
            {
                using (var fb = new FolderBrowserDialog())
                    if (fb.ShowDialog() == DialogResult.OK)
                        txtPath.Text = Path.Combine(fb.SelectedPath, "smartgate.db");
            };
            grpLocal.Controls.Add(txtPath);
            grpLocal.Controls.Add(btnBrowse);

            // --- กลุ่ม Cloud ---
            grpCloud.Controls.Add(new Label { Text = "รูปแบบ:", Left = 15, Top = 30, Width = 60 });
            cboType.SetBounds(80, 27, 375, 24);
            cboType.Items.AddRange(CloudTypes);
            cboType.SelectedIndexChanged += (s, e) => ApplyType();
            grpCloud.Controls.Add(cboType);

            for (int i = 0; i < 6; i++)
            {
                lbl[i] = new Label { Left = 15, Top = 68 + i * 32, Width = 115 };
                box[i] = new TextBox();
                box[i].SetBounds(135, 65 + i * 32, 320, 24);
                grpCloud.Controls.Add(lbl[i]);
                grpCloud.Controls.Add(box[i]);
            }
            box[4].UseSystemPasswordChar = true;

            // แถวโฟลเดอร์ปลายทาง มีปุ่ม "เลือก..." ต่อท้าย
            box[5].Width = 240;
            btnBrowseRemote.Location = new Point(385, 65 + 5 * 32);
            btnBrowseRemote.Click += (s, e) => MessageBox.Show(
                "เมื่อตัวเชื่อมต่อจริงเสร็จ ปุ่มนี้จะดึงรายชื่อโฟลเดอร์จาก server มาให้คลิกเลือกเป็นลำดับชั้น\nตอนนี้พิมพ์ path เองไปก่อน เช่น /SmartGate/plates",
                "รอพัฒนา", MessageBoxButtons.OK, MessageBoxIcon.Information);
            grpCloud.Controls.Add(btnBrowseRemote);

            chkSsl.Location = new Point(135, 68 + 6 * 32);
            grpCloud.Controls.Add(chkSsl);

            EventHandler sync = (s, e) => { grpLocal.Enabled = rbLocal.Checked; grpCloud.Enabled = rbCloud.Checked; };
            rbLocal.CheckedChanged += sync;
            rbCloud.CheckedChanged += sync;

            var btnSave = new Button { Text = "บันทึก", Left = 305, Top = 508, Width = 85 };
            btnSave.Click += BtnSave_Click;
            var btnCancel = new Button { Text = "ยกเลิก", Left = 400, Top = 508, Width = 85 };
            btnCancel.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { rbLocal, rbCloud, grpLocal, grpCloud, btnSave, btnCancel });

            // --- โหลดค่าเดิม ---
            var st = SettingsStore.Load();
            rbCloud.Checked = st.StorageMode == "cloud";
            rbLocal.Checked = !rbCloud.Checked;
            txtPath.Text = st.DbLocalPath;
            int idx = Array.FindIndex(CloudTypes, t => t.StartsWith(st.CloudType));
            cboType.SelectedIndex = idx >= 0 ? idx : 0;
            box[0].Text = st.DbHost;
            box[1].Text = st.DbPort > 0 ? st.DbPort.ToString() : "";
            box[2].Text = st.DbName;
            box[3].Text = st.DbUser;
            box[4].Text = st.DbPassword;
            box[5].Text = st.CloudRemotePath;
            chkSsl.Checked = st.CloudUseSsl;
            sync(null, null);
            ApplyType();
        }

        // เปลี่ยนป้ายชื่อ/ช่อง ตามรูปแบบ cloud ที่เลือก
        private void ApplyType()
        {
            int t = cboType.SelectedIndex;
            bool isDb = (t == 0 || t == 1);

            lbl[0].Text = isDb ? "Host / IP:" : (t == 2 ? "Server URL:" : "Endpoint URL:");
            lbl[1].Text = "Port:";
            lbl[2].Text = isDb ? "ชื่อฐานข้อมูล:" : "Bucket:";
            lbl[3].Text = (t == 3) ? "Access Key:" : "Username:";
            lbl[4].Text = (t == 3) ? "Secret Key:" : (t == 2 ? "App Password:" : "Password:");
            lbl[5].Text = "โฟลเดอร์ปลายทาง:";

            // ซ่อนช่องที่ไม่เกี่ยวกับรูปแบบนั้น
            lbl[2].Visible = box[2].Visible = (isDb || t == 3);                   // db name / bucket
            lbl[5].Visible = box[5].Visible = btnBrowseRemote.Visible = !isDb;    // โฟลเดอร์ มีเฉพาะแบบเก็บไฟล์

            // เติม port มาตรฐานให้ ถ้าช่องยังว่าง
            if (string.IsNullOrWhiteSpace(box[1].Text))
                box[1].Text = (t == 0) ? "3306" : (t == 1) ? "5432" : (t == 2) ? "443" : "9000";
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            var st = SettingsStore.Load();
            st.StorageMode = rbCloud.Checked ? "cloud" : "local";
            st.DbLocalPath = txtPath.Text.Trim();
            st.CloudType = cboType.SelectedIndex == 0 ? "MySQL"
                         : cboType.SelectedIndex == 1 ? "PostgreSQL"
                         : cboType.SelectedIndex == 2 ? "Nextcloud" : "S3";
            st.DbHost = box[0].Text.Trim();
            st.DbPort = int.TryParse(box[1].Text.Trim(), out int p) ? p : 0;
            st.DbName = box[2].Text.Trim();
            st.DbUser = box[3].Text.Trim();
            st.DbPassword = box[4].Text;
            st.CloudRemotePath = box[5].Text.Trim();
            st.CloudUseSsl = chkSsl.Checked;
            SettingsStore.Save(st);

            MessageBox.Show(rbCloud.Checked
                ? "บันทึกแล้ว (ตัวเชื่อมต่อ Cloud อยู่ระหว่างพัฒนา โปรแกรมยังใช้ฐานข้อมูลในเครื่องไปก่อน)"
                : "บันทึกแล้ว — ปิดและเปิดโปรแกรมใหม่เพื่อให้ที่เก็บใหม่มีผล",
                "ตั้งค่าที่เก็บข้อมูล", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }
}