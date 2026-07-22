using SmartGateLPR;
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace SmartGateLPR1
{
    public class RfidSettingsForm : Form
    {
        private TextBox txtIp = new TextBox();
        private TextBox txtPort = new TextBox();
        private TextBox txtUser = new TextBox();
        private TextBox txtPass = new TextBox();
        private Label lblStatus = new Label();
        private Button btnTest;

        private btnDisconnectRFID main;
        public RfidSettingsForm(btnDisconnectRFID mainForm)
        {
            main = mainForm;
            Text = "ตั้งค่าเครื่องอ่าน RFID";
            ClientSize = new System.Drawing.Size(430, 260);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            Controls.Add(new Label { Text = "IP Address:", Left = 20, Top = 25, Width = 90 });
            txtIp.SetBounds(120, 22, 200, 24);

            Controls.Add(new Label { Text = "Port:", Left = 20, Top = 60, Width = 90 });
            txtPort.SetBounds(120, 57, 80, 24);

            Controls.Add(new Label { Text = "Username:", Left = 20, Top = 95, Width = 90 });
            txtUser.SetBounds(120, 92, 200, 24);

            Controls.Add(new Label { Text = "Password:", Left = 20, Top = 130, Width = 90 });
            txtPass.SetBounds(120, 127, 200, 24);
            txtPass.UseSystemPasswordChar = true;

            btnTest = new Button { Text = "ทดสอบเชื่อมต่อ", Left = 120, Top = 160, Width = 120 };
            btnTest.Click += BtnTest_Click;
            var btnConnect = new Button { Text = "เชื่อมต่อ", Left = 245, Top = 160, Width = 80 };
            var btnDisconnect = new Button { Text = "ตัดการเชื่อมต่อ", Left = 330, Top = 160, Width = 90 };
            btnConnect.Click += (s, e) => { SaveOnly(); main?.ConnectRfid(); lblStatus.Text = "สั่งเชื่อมต่อแล้ว (ดูสถานะที่หน้าหลัก)"; lblStatus.ForeColor = Color.Green; };
            btnDisconnect.Click += (s, e) => { main?.DisconnectRfid(); lblStatus.Text = "สั่งตัดการเชื่อมต่อแล้ว"; lblStatus.ForeColor = Color.DarkOrange; };
            Controls.Add(btnConnect);
            Controls.Add(btnDisconnect);

            lblStatus.SetBounds(20, 192, 390, 22);

            var btnSave = new Button { Text = "บันทึก", Left = 225, Top = 218, Width = 85 };
            btnSave.Click += BtnSave_Click;
            var btnCancel = new Button { Text = "ยกเลิก", Left = 320, Top = 218, Width = 85 };
            btnCancel.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { txtIp, txtPort, txtUser, txtPass, btnTest, lblStatus, btnSave, btnCancel });

            var st = SettingsStore.Load();
            txtIp.Text = st.RfidIp;
            txtPort.Text = st.RfidPort > 0 ? st.RfidPort.ToString() : "23";
            txtUser.Text = st.RfidUser;
            txtPass.Text = st.RfidPassword;
        }

        private void BtnTest_Click(object sender, EventArgs e)
        {
            string ip = txtIp.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip))
            {
                lblStatus.Text = "กรอก IP ก่อน"; lblStatus.ForeColor = Color.Red; return;
            }
            if (!int.TryParse(txtPort.Text.Trim(), out int port)) port = 23;

            string user = txtUser.Text, pass = txtPass.Text;
            lblStatus.Text = "⏳ กำลังทดสอบ..."; lblStatus.ForeColor = Color.Gray;
            btnTest.Enabled = false;

            // ทดสอบใน Thread แยก จะได้ไม่ค้างหน้าจอ
            new Thread(() =>
            {
                var telnet = new SimpleTelnet();
                bool ok = false, login = false;
                try
                {
                    ok = telnet.Connect(ip, port);
                    if (ok) login = telnet.Login(user, pass);
                }
                catch { }
                finally { try { telnet.Disconnect(); } catch { } }

                this.Invoke(new Action(() =>
                {
                    if (!ok) { lblStatus.Text = "❌ ต่อไม่ติด — เช็ค IP / Port / สายแลน"; lblStatus.ForeColor = Color.Red; }
                    else if (!login) { lblStatus.Text = "⚠️ ต่อติดแต่ล็อกอินไม่ผ่าน — เช็ค user/password"; lblStatus.ForeColor = Color.DarkOrange; }
                    else { lblStatus.Text = "✅ เชื่อมต่อและล็อกอินสำเร็จ"; lblStatus.ForeColor = Color.Green; }
                    btnTest.Enabled = true;
                }));
            })
            { IsBackground = true }.Start();
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            var st = SettingsStore.Load();
            st.RfidIp = txtIp.Text.Trim();
            st.RfidPort = int.TryParse(txtPort.Text.Trim(), out int p) ? p : 23;
            st.RfidUser = txtUser.Text;
            st.RfidPassword = txtPass.Text;
            SettingsStore.Save(st);
            DialogResult = DialogResult.OK;
            Close();
        }
        private void SaveOnly()
        {
            var st = SettingsStore.Load();
            st.RfidIp = txtIp.Text.Trim();
            st.RfidPort = int.TryParse(txtPort.Text.Trim(), out int p) ? p : 23;
            st.RfidUser = txtUser.Text;
            st.RfidPassword = txtPass.Text;
            SettingsStore.Save(st);
        }
    }
}