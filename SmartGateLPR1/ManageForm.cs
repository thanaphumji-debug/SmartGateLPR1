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
    }
}