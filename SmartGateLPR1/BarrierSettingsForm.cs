using System;
using System.Drawing;
using System.IO.Ports;
using System.Threading;
using System.Windows.Forms;

namespace SmartGateLPR1
{
    /// <summary>
    /// ตั้งค่าวิธีเชื่อมต่อไม้กั้น — เลือกโหมด / พอร์ต COM / คำสั่งของบอร์ด
    /// ได้จากหน้าจอ โดยไม่ต้องไปแก้ settings.json เอง
    /// </summary>
    public class BarrierSettingsForm : Form
    {
        // ค่าที่เก็บใน settings คู่กับข้อความที่โชว์ในช่องเลือกโหมด
        private static readonly string[] ModeKeys = { "simulate", "serial" };
        private static readonly string[] ModeNames =
        {
            "จำลอง — ไม่ต่อฮาร์ดแวร์ (แสดงผลบนหน้าจอเท่านั้น)",
            "USB Relay — ต่อผ่านพอร์ตอนุกรม (COM)",
        };

        private static readonly int[] CommonBauds = { 9600, 19200, 38400, 57600, 115200 };

        private readonly ComboBox cboMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox cboCom = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        private readonly ComboBox cboBaud = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        private readonly TextBox txtOpenCmd = new TextBox();
        private readonly TextBox txtCloseCmd = new TextBox();
        private readonly NumericUpDown numGateOpen = new NumericUpDown { Minimum = 1, Maximum = 60 };
        private readonly Label lblStatus = new Label();
        private readonly GroupBox grpSerial = new GroupBox();
        private readonly Button btnTest;

        private readonly btnDisconnectRFID main;

        public BarrierSettingsForm(btnDisconnectRFID mainForm)
        {
            main = mainForm;
            Text = "ตั้งค่าไม้กั้น";
            ClientSize = new Size(480, 370);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;

            // ---------- เลือกโหมด ----------
            Controls.Add(new Label { Text = "วิธีเชื่อมต่อ:", Left = 20, Top = 20, Width = 80 });
            cboMode.SetBounds(105, 17, 355, 24);
            cboMode.Items.AddRange(ModeNames);
            cboMode.SelectedIndexChanged += (s, e) => ApplyModeToUi();
            Controls.Add(cboMode);

            // ---------- โหมด Serial ----------
            grpSerial.Text = "ตั้งค่าบอร์ดรีเลย์ USB (Serial)";
            grpSerial.SetBounds(20, 52, 440, 160);

            grpSerial.Controls.Add(new Label { Text = "พอร์ต COM:", Left = 15, Top = 30, Width = 80 });
            cboCom.SetBounds(100, 27, 150, 24);
            grpSerial.Controls.Add(cboCom);
            var btnRefresh = new Button { Text = "🔄", Left = 256, Top = 26, Width = 38, Height = 26 };
            btnRefresh.Click += (s, e) => { LoadComPorts(); ShowInfo("สแกนพอร์ตใหม่แล้ว"); };
            grpSerial.Controls.Add(btnRefresh);
            grpSerial.Controls.Add(new Label
            {
                Text = "(ดูเลขพอร์ตได้จาก Device Manager)",
                Left = 300, Top = 31, Width = 130, ForeColor = Color.Gray
            });

            grpSerial.Controls.Add(new Label { Text = "ความเร็ว:", Left = 15, Top = 65, Width = 80 });
            cboBaud.SetBounds(100, 62, 150, 24);
            foreach (int b in CommonBauds) cboBaud.Items.Add(b.ToString());
            grpSerial.Controls.Add(cboBaud);
            grpSerial.Controls.Add(new Label
            {
                Text = "bps (ปกติ 9600)", Left = 256, Top = 66, Width = 120, ForeColor = Color.Gray
            });

            grpSerial.Controls.Add(new Label { Text = "คำสั่งเปิด (hex):", Left = 15, Top = 100, Width = 100 });
            txtOpenCmd.SetBounds(120, 97, 180, 24);
            grpSerial.Controls.Add(txtOpenCmd);

            grpSerial.Controls.Add(new Label { Text = "คำสั่งปิด (hex):", Left = 15, Top = 130, Width = 100 });
            txtCloseCmd.SetBounds(120, 127, 180, 24);
            grpSerial.Controls.Add(txtCloseCmd);

            grpSerial.Controls.Add(new Label
            {
                Text = "ค่าเริ่มต้นเป็นของบอร์ด LCUS-1",
                Left = 308, Top = 114, Width = 125, Height = 30, ForeColor = Color.Gray
            });
            Controls.Add(grpSerial);

            // ---------- เวลาเปิดค้าง ----------
            Controls.Add(new Label { Text = "เปิดค้างก่อนปิดอัตโนมัติ:", Left = 20, Top = 232, Width = 145 });
            numGateOpen.SetBounds(170, 230, 60, 24);
            Controls.Add(numGateOpen);
            Controls.Add(new Label
            {
                Text = "วินาที (นับตั้งแต่ตอนอนุญาตให้ผ่าน)",
                Left = 238, Top = 232, Width = 230, ForeColor = Color.Gray
            });

            // ---------- ทดสอบ / บันทึก ----------
            btnTest = new Button { Text = "🔧 ทดสอบสั่งงาน", Left = 20, Top = 262, Width = 140, Height = 28 };
            btnTest.Click += BtnTest_Click;
            Controls.Add(btnTest);

            lblStatus.SetBounds(20, 296, 440, 32);
            Controls.Add(lblStatus);

            var btnSave = new Button { Text = "บันทึก", Left = 290, Top = 332, Width = 85, Height = 28 };
            btnSave.Click += BtnSave_Click;
            var btnCancel = new Button { Text = "ยกเลิก", Left = 383, Top = 332, Width = 85, Height = 28 };
            btnCancel.Click += (s, e) => Close();
            AcceptButton = btnSave; CancelButton = btnCancel;
            Controls.AddRange(new Control[] { btnSave, btnCancel });

            LoadFromSettings();
        }

        // ================= โหลด / บันทึกค่า =================

        private void LoadFromSettings()
        {
            var st = SettingsStore.Load();

            int modeIdx = Array.IndexOf(ModeKeys, (st.BarrierMode ?? "simulate").ToLower());
            cboMode.SelectedIndex = modeIdx >= 0 ? modeIdx : 0;

            LoadComPorts();
            SelectOrAdd(cboCom, st.BarrierComPort);
            SelectOrAdd(cboBaud, (st.BarrierBaudRate > 0 ? st.BarrierBaudRate : 9600).ToString());

            txtOpenCmd.Text = st.BarrierOpenCmd;
            txtCloseCmd.Text = st.BarrierCloseCmd;
            numGateOpen.Value = Math.Min(numGateOpen.Maximum,
                                Math.Max(numGateOpen.Minimum, st.GateOpenSec > 0 ? st.GateOpenSec : 3));

            ApplyModeToUi();
        }

        /// <summary>สแกนพอร์ต COM ที่มีอยู่จริงในเครื่องมาใส่ในช่องเลือก (คงค่าที่เลือกไว้เดิม)</summary>
        private void LoadComPorts()
        {
            string current = cboCom.Text;
            cboCom.Items.Clear();
            try
            {
                string[] ports = SerialPort.GetPortNames();
                Array.Sort(ports);
                cboCom.Items.AddRange(ports);
            }
            catch { /* สแกนไม่ได้ก็ยังพิมพ์เองได้ */ }

            if (!string.IsNullOrWhiteSpace(current)) SelectOrAdd(cboCom, current);
            else if (cboCom.Items.Count > 0) cboCom.SelectedIndex = 0;
        }

        /// <summary>เลือกค่าในช่อง ถ้าไม่มีในรายการก็เพิ่มเข้าไป (กันค่าที่ตั้งไว้เดิมหาย)</summary>
        private static void SelectOrAdd(ComboBox cbo, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            int i = cbo.Items.IndexOf(value);
            if (i < 0) i = cbo.Items.Add(value);
            cbo.SelectedIndex = i;
        }

        private string SelectedMode => ModeKeys[cboMode.SelectedIndex < 0 ? 0 : cboMode.SelectedIndex];

        /// <summary>เปิด-ปิดกลุ่มตั้งค่าให้ตรงกับโหมดที่เลือก จะได้ไม่งงว่าช่องไหนมีผล</summary>
        private void ApplyModeToUi()
        {
            string mode = SelectedMode;
            grpSerial.Enabled = mode == "serial";
            btnTest.Enabled = mode != "simulate";
            lblStatus.Text = "";
        }

        /// <summary>อ่านค่าจากหน้าจอ (ยังไม่เซฟ) มาใส่ AppSettings เพื่อเอาไปสร้างตัวควบคุมไม้กั้น</summary>
        private AppSettings ReadUi(AppSettings st)
        {
            st.BarrierMode = SelectedMode;
            st.BarrierComPort = cboCom.Text.Trim();
            st.BarrierBaudRate = int.TryParse(cboBaud.Text.Trim(), out int b) && b > 0 ? b : 9600;
            st.BarrierOpenCmd = txtOpenCmd.Text.Trim();
            st.BarrierCloseCmd = txtCloseCmd.Text.Trim();
            st.GateOpenSec = (int)numGateOpen.Value;
            return st;
        }

        // ================= ทดสอบ =================

        private void BtnTest_Click(object sender, EventArgs e)
        {
            string mode = SelectedMode;
            if (mode == "serial" && string.IsNullOrWhiteSpace(cboCom.Text))
            { ShowError("เลือกพอร์ต COM ก่อน"); return; }

            // ทดสอบตามค่าที่กรอกอยู่บนหน้าจอ (ยังไม่ต้องกดบันทึก)
            AppSettings probe = ReadUi(new AppSettings());
            lblStatus.Text = "⏳ กำลังสั่งทดสอบ..."; lblStatus.ForeColor = Color.Gray;
            btnTest.Enabled = false;

            // สั่งใน thread แยก เพราะ Test() มีการรอจังหวะเปิด-ปิด จะค้างหน้าจอ
            new Thread(() =>
            {
                string result;
                IBarrier? dev = null;
                try
                {
                    dev = BarrierFactory.Create(probe);
                    result = dev.Test();
                }
                catch (Exception ex) { result = "ERR|" + ex.Message; }
                finally { try { dev?.Dispose(); } catch { } }

                try
                {
                    this.Invoke(new Action(() =>
                    {
                        bool ok = result.StartsWith("OK|");
                        int bar = result.IndexOf('|');
                        string msg = bar >= 0 ? result.Substring(bar + 1) : result;
                        if (ok) ShowInfo("✅ " + msg);
                        else ShowError("❌ " + msg);
                        btnTest.Enabled = true;
                    }));
                }
                catch { /* ปิดหน้าต่างไปก่อนผลจะกลับมา */ }
            })
            { IsBackground = true }.Start();
        }

        private void ShowInfo(string msg) { lblStatus.Text = msg; lblStatus.ForeColor = Color.Green; }
        private void ShowError(string msg) { lblStatus.Text = msg; lblStatus.ForeColor = Color.Red; }

        // ================= บันทึก =================

        private void BtnSave_Click(object sender, EventArgs e)
        {
            string mode = SelectedMode;
            if (mode == "serial" && string.IsNullOrWhiteSpace(cboCom.Text))
            { ShowError("เลือกพอร์ต COM ก่อนบันทึก"); return; }

            SettingsStore.Save(ReadUi(SettingsStore.Load()));
            main?.ReloadBarrier();      // ใช้ค่าใหม่ทันที ไม่ต้องปิดเปิดโปรแกรม
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
