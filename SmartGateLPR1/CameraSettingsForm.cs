using System;
using System.Drawing;
using System.Windows.Forms;
using OpenCvSharp;

namespace SmartGateLPR1
{
    public class CameraSettingsForm : Form
    {
        private TextBox txtCam1 = new TextBox();
        private TextBox txtCam2 = new TextBox();
        private Label lblStatus = new Label();

        public CameraSettingsForm()
        {
            Text = "ตั้งค่ากล้อง";
            ClientSize = new System.Drawing.Size(520, 230);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            var lbl1 = new Label { Text = "RTSP Camera 1:", Left = 20, Top = 22, Width = 100 };
            txtCam1.SetBounds(130, 20, 360, 24);
            var btnTest1 = new Button { Text = "Connect", Left = 130, Top = 50, Width = 80 };
            btnTest1.Click += (s, e) => TestRtsp(txtCam1.Text);

            var lbl2 = new Label { Text = "RTSP Camera 2:", Left = 20, Top = 92, Width = 100 };
            txtCam2.SetBounds(130, 90, 360, 24);
            var btnTest2 = new Button { Text = "Connect", Left = 130, Top = 120, Width = 80 };
            btnTest2.Click += (s, e) => TestRtsp(txtCam2.Text);

            lblStatus.SetBounds(20, 155, 480, 22);

            var btnSave = new Button { Text = "บันทึก", Left = 310, Top = 185, Width = 85 };
            btnSave.Click += BtnSave_Click;
            var btnCancel = new Button { Text = "ยกเลิก", Left = 405, Top = 185, Width = 85 };
            btnCancel.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { lbl1, txtCam1, btnTest1, lbl2, txtCam2, btnTest2, lblStatus, btnSave, btnCancel });

            var st = SettingsStore.Load();
            txtCam1.Text = st.RtspCamera1;
            txtCam2.Text = st.RtspCamera2;
        }

        private void TestRtsp(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) { lblStatus.Text = "กรอก URL ก่อน"; lblStatus.ForeColor = Color.Red; return; }
            lblStatus.Text = "⏳ กำลังทดสอบการเชื่อมต่อ... (อาจใช้เวลาหลายวินาที)";
            lblStatus.ForeColor = Color.Gray;
            lblStatus.Refresh();
            using (var cap = new VideoCapture(url))
            {
                if (cap.IsOpened()) { lblStatus.Text = "✅ เชื่อมต่อได้"; lblStatus.ForeColor = Color.Green; }
                else { lblStatus.Text = "❌ เชื่อมต่อไม่ได้ — เช็ค URL / รหัสผ่าน / เครือข่าย"; lblStatus.ForeColor = Color.Red; }
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            var st = SettingsStore.Load();
            st.RtspCamera1 = txtCam1.Text.Trim();
            st.RtspCamera2 = txtCam2.Text.Trim();
            SettingsStore.Save(st);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}