using System;
using System.Drawing;
using System.Windows.Forms;

namespace SmartGateLPR1
{
    public class CameraSettingsForm : Form
    {
        private TextBox txtCam1 = new TextBox();
        private TextBox txtCam2 = new TextBox();
        private Label lblStatus = new Label();
        private btnDisconnectRFID main;   // อ้างถึง Form หลัก (คลาสฟอร์มหลักของคุณชื่อนี้)

        public CameraSettingsForm(btnDisconnectRFID mainForm)
        {
            main = mainForm;
            Text = "ตั้งค่ากล้อง";
            ClientSize = new Size(520, 250);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            // --- กล้อง 1 ---
            Controls.Add(new Label { Text = "RTSP Camera 1:", Left = 20, Top = 22, Width = 100 });
            txtCam1.SetBounds(125, 20, 370, 24);
            var btnC1 = new Button { Text = "Connect", Left = 125, Top = 50, Width = 90 };
            var btnD1 = new Button { Text = "Disconnect", Left = 222, Top = 50, Width = 90 };
            btnC1.Click += (s, e) => Connect(1, txtCam1.Text.Trim());
            btnD1.Click += (s, e) => { main?.StopCamera(1); SetStatus("⛔ ตัดการเชื่อมต่อกล้อง 1 แล้ว", Color.DarkOrange); };

            // --- กล้อง 2 ---
            Controls.Add(new Label { Text = "RTSP Camera 2:", Left = 20, Top = 95, Width = 100 });
            txtCam2.SetBounds(125, 93, 370, 24);
            var btnC2 = new Button { Text = "Connect", Left = 125, Top = 123, Width = 90 };
            var btnD2 = new Button { Text = "Disconnect", Left = 222, Top = 123, Width = 90 };
            btnC2.Click += (s, e) => Connect(2, txtCam2.Text.Trim());
            btnD2.Click += (s, e) => { main?.StopCamera(2); SetStatus("⛔ ตัดการเชื่อมต่อกล้อง 2 แล้ว", Color.DarkOrange); };

            lblStatus.SetBounds(20, 160, 480, 22);

            var btnSave = new Button { Text = "บันทึก", Left = 310, Top = 200, Width = 85 };
            btnSave.Click += (s, e) => { SaveUrls(); DialogResult = DialogResult.OK; Close(); };
            var btnCancel = new Button { Text = "ยกเลิก", Left = 405, Top = 200, Width = 85 };
            btnCancel.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { txtCam1, btnC1, btnD1, txtCam2, btnC2, btnD2, lblStatus, btnSave, btnCancel });

            var st = SettingsStore.Load();
            txtCam1.Text = st.RtspCamera1;
            txtCam2.Text = st.RtspCamera2;
        }

        private void Connect(int camId, string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                SetStatus($"กรอกลิงก์กล้อง {camId} ก่อน", Color.Red);
                return;
            }
            SaveUrls();                    // เซฟ URL ลง settings ก่อน เพราะ StartCamera อ่านจาก settings
            main?.StartCamera(camId);
            SetStatus($"✅ กำลังเชื่อมต่อกล้อง {camId}... (ดูภาพที่หน้าหลัก)", Color.Green);
        }

        private void SaveUrls()
        {
            var st = SettingsStore.Load();
            st.RtspCamera1 = txtCam1.Text.Trim();
            st.RtspCamera2 = txtCam2.Text.Trim();
            SettingsStore.Save(st);
        }

        private void SetStatus(string msg, Color c)
        {
            lblStatus.Text = msg;
            lblStatus.ForeColor = c;
        }
    }
}