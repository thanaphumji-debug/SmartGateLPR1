using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SmartGateLPR1
{
    /// <summary>ตั้งค่าที่เก็บข้อมูล — ในเครื่อง (SQLite) หรือบน server (MySQL/MariaDB)</summary>
    public class StorageSettingsForm : Form
    {
        private RadioButton rbLocal = new RadioButton
        { Text = "เก็บในคอมพิวเตอร์เครื่องนี้  (SQLite)", Left = 22, Top = 44, Width = 300, Checked = true };
        private RadioButton rbCloud = new RadioButton
        { Text = "เก็บบนเซิร์ฟเวอร์ / Cloud  (MySQL หรือ MariaDB)", Left = 22, Top = 68, Width = 340 };

        private GroupBox grpLocal = new GroupBox
        { Text = "ฐานข้อมูลในเครื่อง", Left = 20, Top = 96, Width = 500, Height = 86 };
        private GroupBox grpCloud = new GroupBox
        { Text = "การเชื่อมต่อเซิร์ฟเวอร์", Left = 20, Top = 188, Width = 500, Height = 224 };
        private GroupBox grpImg = new GroupBox
        { Text = "โฟลเดอร์เก็บภาพประวัติ (ใช้ได้ทั้งสองแบบ)", Left = 20, Top = 418, Width = 500, Height = 84 };

        private TextBox txtDbPath = new TextBox { Left = 15, Top = 48, Width = 375 };
        private TextBox txtImgDir = new TextBox { Left = 15, Top = 46, Width = 375 };

        private ComboBox cboType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 110, Top = 26, Width = 200 };
        private TextBox txtHost = new TextBox { Left = 110, Top = 58, Width = 220 };
        private TextBox txtPort = new TextBox { Left = 400, Top = 58, Width = 70, Text = "3306" };
        private TextBox txtDbName = new TextBox { Left = 110, Top = 90, Width = 220 };
        private TextBox txtUser = new TextBox { Left = 110, Top = 122, Width = 220 };
        private TextBox txtPass = new TextBox { Left = 110, Top = 154, Width = 220, UseSystemPasswordChar = true };
        private CheckBox chkSsl = new CheckBox
        { Text = "เข้ารหัสการเชื่อมต่อด้วย SSL/TLS (แนะนำเมื่อออกอินเทอร์เน็ต)", Left = 15, Top = 188, Width = 420, Checked = true };
        private CheckBox chkShowPass = new CheckBox { Text = "แสดงรหัส", Left = 345, Top = 156, Width = 90 };

        private Label lblNow = new Label
        { Left = 22, Top = 14, Width = 500, Height = 22, ForeColor = Color.FromArgb(30, 90, 60) };

        public StorageSettingsForm()
        {
            Text = "ตั้งค่าที่เก็บข้อมูล";
            ClientSize = new Size(542, 596);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            lblNow.Font = new Font("Tahoma", 8.5f, FontStyle.Bold);

            // ---------- ในเครื่อง ----------
            grpLocal.Controls.Add(new Label
            { Text = "ไฟล์ฐานข้อมูล (.db) — เว้นว่างไว้ = เก็บข้างตัวโปรแกรม", Left = 15, Top = 24, Width = 460, ForeColor = Color.Gray });
            var btnBrowseDb = new Button { Text = "เลือก...", Left = 398, Top = 46, Width = 78, Height = 25 };
            btnBrowseDb.Click += (s, e) =>
            {
                using (var sfd = new SaveFileDialog
                {
                    Title = "เลือกที่เก็บไฟล์ฐานข้อมูล",
                    Filter = "ไฟล์ฐานข้อมูล SQLite (*.db)|*.db",
                    FileName = string.IsNullOrWhiteSpace(txtDbPath.Text) ? "smartgate.db" : Path.GetFileName(txtDbPath.Text),
                    OverwritePrompt = false
                })
                { if (sfd.ShowDialog(this) == DialogResult.OK) txtDbPath.Text = sfd.FileName; }
            };
            grpLocal.Controls.Add(txtDbPath);
            grpLocal.Controls.Add(btnBrowseDb);

            // ---------- เซิร์ฟเวอร์ ----------
            cboType.Items.AddRange(new object[] { "MySQL", "MariaDB" });
            cboType.SelectedIndex = 0;
            AddLbl(grpCloud, "ชนิดฐานข้อมูล:", 15, 30);
            AddLbl(grpCloud, "Host / IP:", 15, 62);
            AddLbl(grpCloud, "Port:", 350, 62, 45);
            AddLbl(grpCloud, "ชื่อฐานข้อมูล:", 15, 94);
            AddLbl(grpCloud, "Username:", 15, 126);
            AddLbl(grpCloud, "Password:", 15, 158);

            chkShowPass.CheckedChanged += (s, e) => txtPass.UseSystemPasswordChar = !chkShowPass.Checked;

            grpCloud.Controls.AddRange(new Control[]
            { cboType, txtHost, txtPort, txtDbName, txtUser, txtPass, chkShowPass, chkSsl });

            // ---------- โฟลเดอร์ภาพ ----------
            grpImg.Controls.Add(new Label
            {
                Text = "เว้นว่าง = เก็บข้างไฟล์ฐานข้อมูล  |  ใส่ path เครือข่ายได้ เช่น \\\\server\\photos",
                Left = 15,
                Top = 22,
                Width = 470,
                ForeColor = Color.Gray
            });
            var btnBrowseImg = new Button { Text = "เลือก...", Left = 398, Top = 44, Width = 78, Height = 25 };
            btnBrowseImg.Click += (s, e) =>
            {
                using (var fbd = new FolderBrowserDialog { Description = "เลือกโฟลเดอร์เก็บภาพประวัติ" })
                { if (fbd.ShowDialog(this) == DialogResult.OK) txtImgDir.Text = fbd.SelectedPath; }
            };
            grpImg.Controls.Add(txtImgDir);
            grpImg.Controls.Add(btnBrowseImg);

            // ---------- ปุ่มล่าง ----------
            var btnTest = new Button
            {
                Text = "🔌  ทดสอบการเชื่อมต่อ",
                Left = 20,
                Top = 514,
                Width = 170,
                Height = 32,
                BackColor = Color.FromArgb(232, 240, 254)
            };
            btnTest.Click += BtnTest_Click;

            var btnSave = new Button { Text = "บันทึก", Left = 340, Top = 514, Width = 88, Height = 32 };
            btnSave.Click += BtnSave_Click;
            var btnCancel = new Button { Text = "ยกเลิก", Left = 434, Top = 514, Width = 88, Height = 32 };
            btnCancel.Click += (s, e) => Close();

            var lblHint = new Label
            {
                Text = "ข้อมูลจะไม่ย้ายตามเมื่อสลับที่เก็บ — แต่ละที่เก็บมีข้อมูลของตัวเอง",
                Left = 22,
                Top = 556,
                Width = 500,
                ForeColor = Color.FromArgb(150, 90, 0)
            };

            EventHandler sync = (s, e) =>
            {
                grpLocal.Enabled = rbLocal.Checked;
                grpCloud.Enabled = rbCloud.Checked;
            };
            rbLocal.CheckedChanged += sync;
            rbCloud.CheckedChanged += sync;

            Controls.AddRange(new Control[]
            { lblNow, rbLocal, rbCloud, grpLocal, grpCloud, grpImg, btnTest, btnSave, btnCancel, lblHint });

            LoadCurrent();
            sync(null, null);
        }

        private void AddLbl(Control parent, string text, int left, int top, int width = 95)
        {
            parent.Controls.Add(new Label { Text = text, Left = left, Top = top + 3, Width = width });
        }

        private void LoadCurrent()
        {
            var st = SettingsStore.Load();

            rbCloud.Checked = st.StorageMode == "cloud";
            rbLocal.Checked = !rbCloud.Checked;

            txtDbPath.Text = st.DbLocalPath;
            txtImgDir.Text = st.LogImageDir;

            cboType.SelectedIndex = st.CloudType == "MariaDB" ? 1 : 0;
            txtHost.Text = st.DbHost;
            txtPort.Text = (st.DbPort > 0 ? st.DbPort : 3306).ToString();
            txtDbName.Text = st.DbName;
            txtUser.Text = st.DbUser;
            txtPass.Text = st.DbPassword;
            chkSsl.Checked = st.CloudUseSsl;

            lblNow.Text = "ตอนนี้ใช้อยู่:  " + (string.IsNullOrEmpty(Db.Describe) ? "(ยังไม่ได้ตั้งค่า)" : Db.Describe);
        }

        /// <summary>อ่านค่าจากหน้าจอมาเป็น AppSettings (ยังไม่บันทึกลงไฟล์)</summary>
        private AppSettings ReadForm()
        {
            var st = SettingsStore.Load();

            st.StorageMode = rbCloud.Checked ? "cloud" : "local";
            st.DbLocalPath = txtDbPath.Text.Trim();
            st.LogImageDir = txtImgDir.Text.Trim();

            st.CloudType = cboType.SelectedIndex == 1 ? "MariaDB" : "MySQL";
            st.DbHost = txtHost.Text.Trim();
            st.DbName = txtDbName.Text.Trim();
            st.DbUser = txtUser.Text.Trim();
            st.DbPassword = txtPass.Text;
            st.CloudUseSsl = chkSsl.Checked;

            int port;
            st.DbPort = int.TryParse(txtPort.Text.Trim(), out port) && port > 0 ? port : 3306;

            return st;
        }

        private void BtnTest_Click(object sender, EventArgs e)
        {
            Cursor = Cursors.WaitCursor;
            string result = Db.TestConnection(ReadForm());
            Cursor = Cursors.Default;

            bool ok = result.StartsWith("OK|");
            MessageBox.Show(result.Substring(result.IndexOf('|') + 1),
                            ok ? "เชื่อมต่อได้" : "เชื่อมต่อไม่ได้",
                            MessageBoxButtons.OK,
                            ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            var st = ReadForm();

            if (st.StorageMode == "cloud")
            {
                if (st.DbHost == "" || st.DbName == "" || st.DbUser == "")
                {
                    MessageBox.Show("โหมดเซิร์ฟเวอร์ต้องกรอก Host, ชื่อฐานข้อมูล และ Username ให้ครบ",
                                    "ข้อมูลไม่ครบ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // กันบันทึกค่าที่ต่อไม่ได้ทิ้งไว้จนโปรแกรมเปิดมาแล้วใช้งานไม่ได้
                string test = Db.TestConnection(st);
                if (!test.StartsWith("OK|"))
                {
                    var ans = MessageBox.Show(
                        test.Substring(test.IndexOf('|') + 1) +
                        "\n\nยังต้องการบันทึกค่านี้ไว้ไหม?\n(ถ้าบันทึก โปรแกรมจะใช้ฐานข้อมูลในเครื่องไปก่อนจนกว่าจะต่อได้)",
                        "ต่อเซิร์ฟเวอร์ไม่ได้", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (ans != DialogResult.Yes) return;
                }
            }

            try
            {
                SettingsStore.Save(st);
                Db.Configure(st);
                DatabaseHelper.ResetSchema();

                new DatabaseHelper();                  // สร้าง/ตรวจตารางตามที่เก็บใหม่
                if (!string.IsNullOrEmpty(DatabaseHelper.LastSchemaError))
                {
                    MessageBox.Show("บันทึกค่าแล้ว แต่สร้างตารางไม่สำเร็จ:\n\n" + DatabaseHelper.LastSchemaError,
                                    "เตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show("บันทึกแล้ว — ตอนนี้ระบบใช้:\n\n" + Db.Describe +
                                    "\n\nโฟลเดอร์ภาพ: " + Db.ImageBaseDir,
                                    "ตั้งค่าที่เก็บข้อมูล", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("บันทึกไม่สำเร็จ: " + Db.Explain(ex), "ผิดพลาด",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}