using System;
using System.Data;
using System.Data.SQLite; // อย่าลืม using SQLite
using System.Windows.Forms;

namespace SmartGateLPR1
{
    public partial class ManageForm : Form
    {
        DatabaseHelper db;

        public ManageForm()
        {
            InitializeComponent();
            db = new DatabaseHelper(); // เชื่อม Database
        }

        private void ManageForm_Load(object sender, EventArgs e)
        {
            LoadDataToGrid(); // เปิดหน้ามาปุ๊บ โหลดข้อมูลปั๊บ
            ReloadTable(); // <--- ใส่บรรทัดนี้เข้าไปครับ
        }

        // ฟังก์ชันดึงข้อมูลมาใส่ตาราง
        private void LoadDataToGrid()
        {
            try
            {
                DataTable dt = db.GetAllUsers(); // เดี๋ยวเราไปสร้างฟังก์ชันนี้ใน DB Helper กัน
                dgvUsers.DataSource = dt;
            }
            catch (Exception ex)
            {
                MessageBox.Show("โหลดข้อมูลไม่สำเร็จ: " + ex.Message);
            }
        }

        // ปุ่มบันทึก (Add)
        private void btnSave_Click(object sender, EventArgs e)
        {
            string plate = txtPlateInput.Text.Trim();  // ทะเบียน ต้องคู่กับ txtPlate
            string rfid = txtRFID_Manage.Text.Trim();  // RFID ต้องคู่กับ txtRFID
            string name = txtNameInput.Text.Trim();

            if (string.IsNullOrEmpty(plate) || string.IsNullOrEmpty(rfid))
            {
                MessageBox.Show("กรุณากรอกข้อมูลให้ครบ");
                return;
            }

            db.AddUser(plate, rfid, name); // เรียกใช้ฟังก์ชันเดิมที่มีอยู่แล้ว
            MessageBox.Show("บันทึกสำเร็จ!");

            LoadDataToGrid(); // บันทึกเสร็จ รีเฟรชตารางดูข้อมูลใหม่ทันที
            ReloadTable(); // <--- **สำคัญมาก! ต้องเติมบรรทัดนี้ครับ** }

            // เคลียร์ช่อง
            txtRFID_Manage.Text = "";
            txtPlateInput.Text = "";
            txtNameInput.Text = "";
        }

        // ปุ่มลบ (Delete)
        private void btnDelete_Click(object sender, EventArgs e)
        {
            if (dgvUsers.SelectedRows.Count > 0)
            {
                // ดึงค่า RFID จากแถวที่เลือก (สมมติ RFID อยู่ Column index 1)
                // หรือดึง ID ก็ได้ถ้ามี column id
                string rfidToDelete = dgvUsers.SelectedRows[0].Cells["rfid_tag"].Value.ToString();

                if (MessageBox.Show($"ยืนยันลบข้อมูล RFID: {rfidToDelete}?", "ยืนยัน", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    db.DeleteUser(rfidToDelete); // เดี๋ยวไปสร้างฟังก์ชันนี้
                    LoadDataToGrid(); // รีเฟรชตาราง
                }
            }
            else
            {
                MessageBox.Show("กรุณาเลือกแถวที่จะลบก่อนครับ");
            }
        }

        private void dgvUsers_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void ReloadTable()
        {
            try
            {
                DataTable dt = db.GetAllUsers();
                dgvUsers.DataSource = dt;

                // จัดหัวตารางให้เป็นภาษาไทย
                if (dgvUsers.Columns.Count > 0)
                {
                    dgvUsers.Columns["id"].Visible = false; // ซ่อน id ไว้ ไม่ต้องโชว์

                    dgvUsers.Columns["plate_number"].HeaderText = "เลขทะเบียน";
                    dgvUsers.Columns["rfid_tag"].HeaderText = "รหัส RFID";
                    dgvUsers.Columns["owner_name"].HeaderText = "ชื่อเจ้าของรถ";

                    dgvUsers.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                }
            }
            catch (Exception ex)
            {
                // กันเหนียวไว้ก่อน
            }
        }

        private void btnSelectPath_Click(object sender, EventArgs e)
        {
            // สร้างตัวเลือกโฟลเดอร์
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "กรุณาเลือกโฟลเดอร์สำหรับเก็บฐานข้อมูล";

                // ถ้าผู้ใช้กด OK
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    // เอา Path ที่เลือก + ชื่อไฟล์
                    string selectedPath = fbd.SelectedPath;
                    string fullPath = System.IO.Path.Combine(selectedPath, "smartgate.db");

                    // บันทึกลง Memory (Settings)
                    Properties.Settings.Default.DbPath = fullPath;
                    Properties.Settings.Default.Save(); // อย่าลืมบรรทัดนี้! เพื่อให้จำค่าถาวร

                    // อัปเดต Label ให้รู้ว่าเปลี่ยนแล้ว
                    lblCurrentPath.Text = "ที่เก็บข้อมูล: " + fullPath;

                    MessageBox.Show("เปลี่ยนที่เก็บข้อมูลเรียบร้อย! \nกรุณาปิดและเปิดโปรแกรมใหม่เพื่อเริ่มใช้งานที่อยู่ใหม่", "แจ้งเตือน");
                }
            }
        }

        private void ManageForm_Load_1(object sender, EventArgs e)
        {
            // 1. โหลดข้อมูลลงตาราง
            ReloadTable();

            // 2. โชว์ที่อยู่ไฟล์ Database ปัจจุบัน
            string currentPath = Properties.Settings.Default.DbPath;

            // เช็คว่าถ้ายังไม่เคยเลือก (ค่าว่าง) ให้บอกว่าเป็น Default
            if (string.IsNullOrEmpty(currentPath))
            {
                lblCurrentPath.Text = "ที่เก็บข้อมูล: ค่าเริ่มต้น (ภายในโฟลเดอร์โปรแกรม)";
            }
            else
            {
                lblCurrentPath.Text = "ที่เก็บข้อมูล: " + currentPath;
            }
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            // ดึงข้อมูลตารางมาไว้ในตัวแปร
            DataTable dt = (DataTable)dgvUsers.DataSource;

            if (dt != null)
            {
                string keyword = txtSearch.Text.Trim();

                // ถ้าช่องว่างเปล่า -> ให้โชว์ข้อมูลทั้งหมด
                if (string.IsNullOrEmpty(keyword))
                {
                    dt.DefaultView.RowFilter = "";
                }
                // ถ้ามีข้อความ -> ให้กรองหา (ทะเบียน หรือ RFID หรือ ชื่อเจ้าของ)
                else
                {
                    // ใช้คำสั่ง LIKE '%...%' เพื่อหาข้อความที่ "มีคำนี้ปนอยู่"
                    dt.DefaultView.RowFilter = string.Format(
                        "plate_number LIKE '%{0}%' OR rfid_tag LIKE '%{0}%' OR owner_name LIKE '%{0}%'",
                        keyword
                    );
                }
            }
        }
    }
}