using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace SmartGateLPR1
{
    public class PlateTableForm : Form
    {
        private DataGridView dgv = new DataGridView();
        private Button btnAdd = new Button();
        private Button btnSave = new Button();
        private Button btnDelete = new Button();
        private DatabaseHelper db = new DatabaseHelper();

        private static readonly string[] Provinces = {
            "กรุงเทพมหานคร","กระบี่","กาญจนบุรี","กาฬสินธุ์","กำแพงเพชร","ขอนแก่น",
            "จันทบุรี","ฉะเชิงเทรา","ชลบุรี","ชัยนาท","ชัยภูมิ","ชุมพร",
            "เชียงราย","เชียงใหม่","ตรัง","ตราด","ตาก","นครนายก",
            "นครปฐม","นครพนม","นครราชสีมา","นครศรีธรรมราช","นครสวรรค์","นนทบุรี",
            "นราธิวาส","น่าน","บึงกาฬ","บุรีรัมย์","ปทุมธานี","ประจวบคีรีขันธ์",
            "ปราจีนบุรี","ปัตตานี","พระนครศรีอยุธยา","พะเยา","พังงา","พัทลุง",
            "พิจิตร","พิษณุโลก","เพชรบุรี","เพชรบูรณ์","แพร่","ภูเก็ต",
            "มหาสารคาม","มุกดาหาร","แม่ฮ่องสอน","ยโสธร","ยะลา","ร้อยเอ็ด",
            "ระนอง","ระยอง","ราชบุรี","ลพบุรี","ลำปาง","ลำพูน",
            "เลย","ศรีสะเกษ","สกลนคร","สงขลา","สตูล","สมุทรปราการ",
            "สมุทรสงคราม","สมุทรสาคร","สระแก้ว","สระบุรี","สิงห์บุรี","สุโขทัย",
            "สุพรรณบุรี","สุราษฎร์ธานี","สุรินทร์","หนองคาย","หนองบัวลำภู","อ่างทอง",
            "อำนาจเจริญ","อุดรธานี","อุตรดิตถ์","อุทัยธานี","อุบลราชธานี"
        };

        public PlateTableForm()
        {
            Text = "บันทึกป้ายทะเบียน";
            ClientSize = new Size(920, 520);
            StartPosition = FormStartPosition.CenterParent;

            // ---------- ตาราง ----------
            dgv.SetBounds(15, 50, 890, 400);
            dgv.AllowUserToAddRows = false;      // เพิ่มแถวผ่านปุ่ม ➕ เท่านั้น
            dgv.RowHeadersVisible = false;
            dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgv.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            var colId = new DataGridViewTextBoxColumn { Name = "colId", Visible = false };

            var colLetters = new DataGridViewTextBoxColumn
            { Name = "colLetters", HeaderText = "ตัวอักษร", FillWeight = 70 };

            var colDigits = new DataGridViewTextBoxColumn
            { Name = "colDigits", HeaderText = "ตัวเลข", FillWeight = 70 };

            var colProvince = new DataGridViewComboBoxColumn
            {
                Name = "colProvince",
                HeaderText = "จังหวัด",
                FillWeight = 130,
                FlatStyle = FlatStyle.Flat
            };
            colProvince.Items.AddRange(Provinces);

            var colPermission = new DataGridViewComboBoxColumn
            {
                Name = "colPermission",
                HeaderText = "สิทธิ์การใช้งาน",
                FillWeight = 110,
                FlatStyle = FlatStyle.Flat
            };
            colPermission.Items.AddRange("ลูกค้าทั่วไป", "บุคลากร");

            var colRfid = new DataGridViewTextBoxColumn
            { Name = "colRfid", HeaderText = "รหัส RFID Tag", FillWeight = 130 };

            var colOwner = new DataGridViewButtonColumn
            {
                Name = "colOwner",
                HeaderText = "ข้อมูลเจ้าของรถ",
                Text = "เปิดดู",
                UseColumnTextForButtonValue = true,
                FillWeight = 90
            };

            dgv.Columns.AddRange(colId, colLetters, colDigits, colProvince, colPermission, colRfid, colOwner);
            dgv.CellContentClick += Dgv_CellContentClick;
            dgv.DataError += (s, e) => { e.ThrowException = false; };  // กัน error ค่า combobox ไม่ตรง

            // ---------- ปุ่ม ➕ มุมขวาบนของตาราง ----------
            btnAdd.Text = "➕";
            btnAdd.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            btnAdd.SetBounds(870, 15, 35, 30);
            btnAdd.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnAdd.Click += (s, e) =>
            {
                int i = dgv.Rows.Add();
                dgv.Rows[i].Cells["colPermission"].Value = "ลูกค้าทั่วไป";  // ค่าเริ่มต้น
                dgv.CurrentCell = dgv.Rows[i].Cells["colLetters"];
                dgv.BeginEdit(true);
            };

            var lblHint = new Label
            {
                Text = "กด ➕ เพื่อเพิ่มแถว แล้วกรอกข้อมูลในตาราง เสร็จแล้วกดบันทึก",
                Location = new Point(15, 22),
                AutoSize = true,
                ForeColor = Color.Gray
            };

            // ---------- ปุ่มล่าง ----------
            btnSave.Text = "💾 บันทึกทั้งหมด";
            btnSave.SetBounds(15, 465, 130, 32);
            btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnSave.Click += BtnSave_Click;

            btnDelete.Text = "🗑 ลบแถวที่เลือก";
            btnDelete.SetBounds(155, 465, 130, 32);
            btnDelete.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnDelete.Click += BtnDelete_Click;

            Controls.AddRange(new Control[] { lblHint, btnAdd, dgv, btnSave, btnDelete });

            LoadData();
        }

        private void LoadData()
        {
            dgv.Rows.Clear();
            DataTable dt = db.GetAllUsers();
            foreach (DataRow r in dt.Rows)
            {
                string letters = dt.Columns.Contains("plate_letters") ? (r["plate_letters"] as string ?? "") : "";
                string digits = dt.Columns.Contains("plate_digits") ? (r["plate_digits"] as string ?? "") : "";
                string prov = dt.Columns.Contains("province") ? (r["province"] as string ?? "") : "";
                string perm = dt.Columns.Contains("permission") ? (r["permission"] as string ?? "") : "";

                int i = dgv.Rows.Add();
                dgv.Rows[i].Cells["colId"].Value = r["id"];
                dgv.Rows[i].Cells["colLetters"].Value = letters;
                dgv.Rows[i].Cells["colDigits"].Value = digits;
                if (Array.IndexOf(Provinces, prov) >= 0) dgv.Rows[i].Cells["colProvince"].Value = prov;
                if (perm == "ลูกค้าทั่วไป" || perm == "บุคลากร") dgv.Rows[i].Cells["colPermission"].Value = perm;
                dgv.Rows[i].Cells["colRfid"].Value = r["rfid_tag"] as string ?? "";
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            dgv.EndEdit();
            int saved = 0, skipped = 0;
            foreach (DataGridViewRow row in dgv.Rows)
            {
                if (row.Cells["colId"].Value != null) continue;   // แถวเก่าในฐานข้อมูลแล้ว ข้าม

                string letters = (row.Cells["colLetters"].Value ?? "").ToString().Trim();
                string digits = (row.Cells["colDigits"].Value ?? "").ToString().Trim();
                string prov = (row.Cells["colProvince"].Value ?? "").ToString();
                string perm = (row.Cells["colPermission"].Value ?? "").ToString();
                string rfid = (row.Cells["colRfid"].Value ?? "").ToString().Trim();

                if (letters == "" || digits == "" || prov == "" || rfid == "")
                { skipped++; continue; }   // กรอกไม่ครบ ข้าม (แถวยังอยู่ให้กรอกต่อ)

                try { db.AddUserFull(letters, digits, prov, perm, rfid); saved++; }
                catch (Exception ex)
                {
                    MessageBox.Show($"บันทึกแถว {letters}{digits} ไม่ได้: {ex.Message}\n(RFID ซ้ำกับที่มีอยู่หรือไม่?)");
                }
            }

            string msg = $"บันทึกสำเร็จ {saved} รายการ";
            if (skipped > 0) msg += $" (ข้าม {skipped} แถวที่กรอกไม่ครบ)";
            MessageBox.Show(msg);
            LoadData();
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            if (dgv.SelectedCells.Count == 0) { MessageBox.Show("เลือกแถวก่อนครับ"); return; }
            var row = dgv.Rows[dgv.SelectedCells[0].RowIndex];

            if (row.Cells["colId"].Value == null)   // แถวใหม่ยังไม่ได้บันทึก ลบออกจากตารางเฉย ๆ
            { dgv.Rows.Remove(row); return; }

            string plate = $"{row.Cells["colLetters"].Value}{row.Cells["colDigits"].Value}";
            if (MessageBox.Show($"ยืนยันลบทะเบียน {plate}?", "ยืนยัน", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                db.DeleteUserById(Convert.ToInt64(row.Cells["colId"].Value));
                LoadData();
            }
        }

        private void Dgv_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (dgv.Columns[e.ColumnIndex].Name == "colOwner")
            {
                MessageBox.Show("🚧 หน้าข้อมูลเจ้าของรถ — รอพัฒนา", "ข้อมูลเจ้าของรถ");
            }
        }
    }
}