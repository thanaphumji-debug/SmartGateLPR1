using System;
using System.Drawing;
using System.Windows.Forms;

namespace SmartGateLPR1
{
    public class AccessPolicyForm : Form
    {
        private btnDisconnectRFID main;
        private CheckBox chkRequireRfid = new CheckBox();
        private CheckBox chkAllowNoPlate = new CheckBox();

        public AccessPolicyForm(btnDisconnectRFID mainForm)
        {
            main = mainForm;
            Text = "ตั้งค่าเงื่อนไขการอนุญาตเข้า-ออก";
            ClientSize = new Size(560, 300);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            int y = 20;
            AddSwitch(chkRequireRfid, "อนุญาตให้รถที่ผ่านต้องมีแท็ก RFID เท่านั้น",
                "ปิด = รถที่อ่านป้ายทะเบียนตรงกับฐานข้อมูล ก็ผ่านได้แม้ไม่มีแท็ก RFID", ref y);
            AddSwitch(chkAllowNoPlate, "อนุญาตรถที่มีแท็ก RFID แต่ตรวจไม่พบป้ายทะเบียน (รถไม่ติดป้าย)",
                "ปิด = รถมีแท็กแต่ไม่เจอป้ายเลย จะไม่อนุญาต (บังคับต้องเห็นป้าย)", ref y);

            var btnSave = new Button { Text = "บันทึก", Left = 355, Top = 255, Width = 85 };
            btnSave.Click += BtnSave_Click;
            var btnCancel = new Button { Text = "ยกเลิก", Left = 450, Top = 255, Width = 85 };
            btnCancel.Click += (s, e) => Close();
            Controls.Add(btnSave);
            Controls.Add(btnCancel);

            var st = SettingsStore.Load();
            chkRequireRfid.Checked = st.RequireRfid;
            chkAllowNoPlate.Checked = st.AllowNoPlate;
        }

        private void AddSwitch(CheckBox chk, string title, string desc, ref int y)
        {
            chk.SetBounds(20, y, 520, 24);
            chk.Text = title;
            chk.Font = new Font("Tahoma", 10, FontStyle.Bold);
            var lbl = new Label { Left = 40, Top = y + 24, Width = 500, Height = 32, ForeColor = Color.Gray, Text = desc };
            Controls.Add(chk);
            Controls.Add(lbl);
            y += 62;
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            var st = SettingsStore.Load();
            st.RequireRfid = chkRequireRfid.Checked;
            st.AllowNoPlate = chkAllowNoPlate.Checked;
            SettingsStore.Save(st);
            main?.ReloadAccessPolicy();
            MessageBox.Show("บันทึกเงื่อนไขการอนุญาตแล้ว", "ตั้งค่า", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }
}