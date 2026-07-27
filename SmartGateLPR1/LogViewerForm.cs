using ClosedXML.Excel;
using System;
using System.Data;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SmartGateLPR1
{
    /// <summary>หน้าประวัติการผ่านเข้า-ออก: ตาราง + ดูภาพ + ส่งออก CSV</summary>
    public class LogViewerForm : Form
    {
        private DatabaseHelper db = new DatabaseHelper();

        private DateTimePicker dtFrom = new DateTimePicker();
        private DateTimePicker dtTo = new DateTimePicker();
        private ComboBox cboResult = new ComboBox();
        private TextBox txtSearch = new TextBox();
        private DataGridView dgv = new DataGridView();
        private Label lblCount = new Label();

        public LogViewerForm()
        {
            db.EnsureAccessLogTable();

            Text = "ประวัติการผ่านเข้า-ออก";
            ClientSize = new Size(1180, 620);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(900, 480);

            // ---------- แถบเครื่องมือด้านบน ----------
            var top = new Panel { Dock = DockStyle.Top, Height = 84, Padding = new Padding(10, 8, 10, 4) };

            top.Controls.Add(new Label { Text = "ตั้งแต่วันที่:", Left = 12, Top = 14, Width = 75 });
            dtFrom.Format = DateTimePickerFormat.Short;
            dtFrom.SetBounds(90, 10, 110, 24);
            dtFrom.Value = DateTime.Today;

            top.Controls.Add(new Label { Text = "ถึงวันที่:", Left = 212, Top = 14, Width = 60 });
            dtTo.Format = DateTimePickerFormat.Short;
            dtTo.SetBounds(275, 10, 110, 24);
            dtTo.Value = DateTime.Today;

            top.Controls.Add(new Label { Text = "ผลลัพธ์:", Left = 400, Top = 14, Width = 55 });
            cboResult.DropDownStyle = ComboBoxStyle.DropDownList;
            cboResult.SetBounds(458, 10, 130, 24);
            cboResult.Items.AddRange(new object[] { "ทั้งหมด", "อนุญาต", "ปฏิเสธ" });
            cboResult.SelectedIndex = 0;

            top.Controls.Add(new Label { Text = "ค้นหา:", Left = 602, Top = 14, Width = 45 });
            txtSearch.SetBounds(650, 10, 180, 24);
            txtSearch.PlaceholderText = "ทะเบียน / แท็ก / ชื่อเจ้าของ";
            txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { LoadData(); e.SuppressKeyPress = true; } };

            var btnSearch = new Button { Text = "🔍 แสดงข้อมูล", Left = 842, Top = 9, Width = 110, Height = 26 };
            btnSearch.Click += (s, e) => LoadData();

            var btnToday = new Button { Text = "วันนี้", Left = 960, Top = 9, Width = 60, Height = 26 };
            btnToday.Click += (s, e) => { dtFrom.Value = DateTime.Today; dtTo.Value = DateTime.Today; LoadData(); };

            var btnAll = new Button { Text = "ทั้งหมด", Left = 1026, Top = 9, Width = 70, Height = 26 };
            btnAll.Click += (s, e) => { dtFrom.Value = new DateTime(2000, 1, 1); dtTo.Value = DateTime.Today; LoadData(); };

            var btnExcel = new Button
            {
                Text = "📊 บันทึกเป็น Excel (มีรูปภาพ)",
                Left = 12,
                Top = 46,
                Width = 205,
                Height = 28,
                BackColor = Color.FromArgb(222, 240, 226)
            };
            btnExcel.Click += BtnExcel_Click;

            var btnCsv = new Button
            {
                Text = "📄 .CSV",
                Left = 220,
                Top = 46,
                Width = 75,
                Height = 28,
                BackColor = Color.FromArgb(230, 240, 255)
            };
            btnCsv.Click += BtnCsv_Click;

            var btnOpenFolder = new Button { Text = "📁 เปิดโฟลเดอร์ภาพ", Left = 310, Top = 46, Width = 150, Height = 28 };
            btnOpenFolder.Click += (s, e) =>
            {
                try
                {
                    string dir = db.GetLogImageDir(dtTo.Value);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
                }
                catch (Exception ex) { MessageBox.Show("เปิดโฟลเดอร์ไม่ได้: " + ex.Message); }
            };

            var btnDelete = new Button { Text = "🗑 ลบรายการที่เลือก", Left = 466, Top = 46, Width = 150, Height = 28 };
            btnDelete.Click += BtnDelete_Click;

            lblCount.SetBounds(628, 52, 420, 20);
            lblCount.ForeColor = Color.DimGray;

            top.Controls.AddRange(new Control[] { dtFrom, dtTo, cboResult, txtSearch, btnSearch,
                                                  btnToday, btnAll, btnExcel, btnCsv, btnOpenFolder, btnDelete, lblCount });

            // ---------- ตาราง ----------
            dgv.Dock = DockStyle.Fill;
            dgv.AllowUserToAddRows = false;
            dgv.AllowUserToDeleteRows = false;
            dgv.ReadOnly = true;
            dgv.RowHeadersVisible = false;
            dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgv.MultiSelect = false;
            dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Tahoma", 9, FontStyle.Bold);

            dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colId", Visible = false });
            dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colW1", Visible = false });
            dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colW2", Visible = false });
            dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colC1", Visible = false });
            dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colC2", Visible = false });

            AddCol("colTs", "วันที่ / เวลา", 105);
            AddCol("colResult", "ผลลัพธ์", 62);
            AddCol("colReason", "เงื่อนไขที่ตรวจได้", 210);
            AddCol("colMode", "โหมด", 45);
            AddCol("colTag", "รหัสแท็ก RFID", 120);
            AddCol("colP1", "ทะเบียน (หน้า)", 90);
            AddCol("colP2", "ทะเบียน (หลัง)", 90);
            AddCol("colPdb", "ทะเบียนในระบบ", 95);
            AddCol("colProv", "จังหวัด", 80);
            AddCol("colOwner", "เจ้าของ", 90);
            AddCol("colPerm", "สิทธิ์", 75);

            dgv.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "colView",
                HeaderText = "ภาพ",
                Text = "ดูภาพ",
                UseColumnTextForButtonValue = true,
                FillWeight = 60
            });
            dgv.CellContentClick += Dgv_CellContentClick;
            dgv.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ShowImages(e.RowIndex); };

            Controls.Add(dgv);
            Controls.Add(top);

            LoadData();
        }

        private void AddCol(string name, string header, int weight)
        {
            dgv.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                FillWeight = weight
            });
        }

        private string ResultFilter()
        {
            if (cboResult.SelectedIndex == 1) return "ALLOWED";
            if (cboResult.SelectedIndex == 2) return "DENIED";
            return "";
        }

        private void LoadData()
        {
            try
            {
                DataTable dt = db.GetAccessLogs(dtFrom.Value, dtTo.Value, ResultFilter(), txtSearch.Text.Trim());
                dgv.Rows.Clear();

                foreach (DataRow r in dt.Rows)
                {
                    string res = r["result"]?.ToString() ?? "";
                    int i = dgv.Rows.Add(
                        r["id"],
                        r["img_wide1"], r["img_wide2"], r["img_plate1"], r["img_plate2"],
                        r["ts"],
                        res == "ALLOWED" ? "✅ อนุญาต" : "⛔ ปฏิเสธ",
                        r["reason"], r["mode"], r["rfid_tag"],
                        r["plate_cam1"], r["plate_cam2"], r["plate_db"],
                        r["province"], r["owner_name"], r["permission"]
                    );

                    var row = dgv.Rows[i];
                    row.Cells["colResult"].Style.ForeColor = res == "ALLOWED" ? Color.Green : Color.Red;
                    row.Cells["colResult"].Style.Font = new Font("Tahoma", 9, FontStyle.Bold);

                    bool hasImg = !string.IsNullOrWhiteSpace(r["img_wide1"]?.ToString()) ||
                                  !string.IsNullOrWhiteSpace(r["img_wide2"]?.ToString());
                    if (!hasImg) row.Cells["colView"].Style.ForeColor = Color.LightGray;
                }

                lblCount.Text = $"พบ {dt.Rows.Count} รายการ" +
                                (dt.Rows.Count > 0 ? "  (ดับเบิลคลิกที่แถวเพื่อดูภาพ)" : "");
            }
            catch (Exception ex)
            {
                MessageBox.Show("โหลดประวัติไม่ได้: " + ex.Message, "ผิดพลาด",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Dgv_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (dgv.Columns[e.ColumnIndex].Name == "colView") ShowImages(e.RowIndex);
        }

        private void ShowImages(int rowIndex)
        {
            var row = dgv.Rows[rowIndex];
            string w1 = row.Cells["colW1"].Value?.ToString() ?? "";
            string w2 = row.Cells["colW2"].Value?.ToString() ?? "";
            string c1 = row.Cells["colC1"].Value?.ToString() ?? "";
            string c2 = row.Cells["colC2"].Value?.ToString() ?? "";

            if (w1 == "" && w2 == "" && c1 == "" && c2 == "")
            {
                MessageBox.Show("รายการนี้ไม่ได้บันทึกภาพไว้\n(ระบบเก็บภาพเฉพาะรายการที่อนุญาตให้ผ่าน)",
                                "ไม่มีภาพ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string title = row.Cells["colTs"].Value?.ToString() ?? "";
            using (var f = new LogImageForm(title, w1, w2, c1, c2)) f.ShowDialog(this);
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            if (dgv.CurrentRow == null) { MessageBox.Show("เลือกแถวที่จะลบก่อน"); return; }
            if (MessageBox.Show("ลบรายการนี้ออกจากประวัติ?\n(ไฟล์ภาพจะยังอยู่ในโฟลเดอร์)", "ยืนยัน",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                db.DeleteAccessLog(Convert.ToInt64(dgv.CurrentRow.Cells["colId"].Value));
                LoadData();
            }
            catch (Exception ex) { MessageBox.Show("ลบไม่ได้: " + ex.Message); }
        }

        // ---------- ส่งออก Excel (.xlsx) พร้อมรูปภาพ ----------
        private void BtnExcel_Click(object sender, EventArgs e)
        {
            if (dgv.Rows.Count == 0)
            {
                MessageBox.Show("ยังไม่มีข้อมูลให้บันทึก\nเลือกช่วงวันที่แล้วกด 'แสดงข้อมูล' ก่อน",
                                "ส่งออก Excel", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string suggest = dtFrom.Value.Date == dtTo.Value.Date
                ? $"ประวัติเข้าออก_{dtFrom.Value:yyyy-MM-dd}.xlsx"
                : $"ประวัติเข้าออก_{dtFrom.Value:yyyy-MM-dd}_ถึง_{dtTo.Value:yyyy-MM-dd}.xlsx";

            using (var sfd = new SaveFileDialog
            {
                Title = "เลือกที่บันทึกไฟล์ Excel",
                Filter = "ไฟล์ Excel (*.xlsx)|*.xlsx",
                FileName = suggest,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            })
            {
                if (sfd.ShowDialog(this) != DialogResult.OK) return;

                Cursor = Cursors.WaitCursor;
                try
                {
                    BuildExcel(sfd.FileName);
                    Cursor = Cursors.Default;

                    if (MessageBox.Show($"บันทึกแล้ว {dgv.Rows.Count} รายการ\n\n{sfd.FileName}\n\nเปิดไฟล์เลยไหม?",
                        "ส่งออก Excel สำเร็จ", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    Cursor = Cursors.Default;
                    MessageBox.Show("บันทึกไฟล์ไม่ได้: " + ex.Message +
                                    "\n(ถ้าไฟล์เปิดค้างอยู่ใน Excel ให้ปิดก่อน)", "ผิดพลาด",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private const string THAI_FONT = "TH Sarabun New";
        private const int IMG_BOX_W = 128;      // กรอบรูปกว้างสุด (พิกเซล)
        private const int IMG_BOX_H = 92;       // กรอบรูปสูงสุด (พิกเซล)

        private void BuildExcel(string fileName)
        {
            string[] headers = {
                "วันที่ / เวลา", "ผลลัพธ์", "เงื่อนไขที่ตรวจได้", "โหมด", "รหัสแท็ก RFID",
                "ทะเบียนกล้องหน้า", "ทะเบียนกล้องหลัง", "ทะเบียนในระบบ", "จังหวัด",
                "เจ้าของ", "สิทธิ์", "ภาพมุมกว้างหน้า", "ภาพมุมกว้างหลัง", "ภาพป้ายหน้า", "ภาพป้ายหลัง" };
            double[] widths = { 17, 11, 34, 8, 20, 15, 15, 15, 15, 17, 13, 19, 19, 19, 19 };

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("ประวัติเข้า-ออก");

                // ---- หัวตาราง ----
                for (int i = 0; i < headers.Length; i++)
                {
                    ws.Cell(1, i + 1).Value = headers[i];
                    ws.Column(i + 1).Width = widths[i];
                }

                // คอลัมน์รหัสแท็ก/ทะเบียน บังคับเป็นข้อความ กันเลขล้วนถูกแปลงเป็นตัวเลข (เลข 0 นำหน้าหาย)
                for (int c = 5; c <= 8; c++) ws.Column(c).Style.NumberFormat.Format = "@";

                var head = ws.Range(1, 1, 1, headers.Length);
                head.Style.Font.Bold = true;
                head.Style.Fill.BackgroundColor = XLColor.FromArgb(219, 233, 246);
                head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                head.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                ws.Row(1).Height = 24;

                // ---- ข้อมูลทีละแถว ----
                int r = 2;
                foreach (DataGridViewRow row in dgv.Rows)
                {
                    string res = Clean(row.Cells["colResult"].Value);

                    ws.Cell(r, 1).Value = Clean(row.Cells["colTs"].Value);
                    ws.Cell(r, 2).Value = res;
                    ws.Cell(r, 3).Value = Clean(row.Cells["colReason"].Value);
                    ws.Cell(r, 4).Value = Clean(row.Cells["colMode"].Value);
                    ws.Cell(r, 5).Value = Clean(row.Cells["colTag"].Value);
                    ws.Cell(r, 6).Value = Clean(row.Cells["colP1"].Value);
                    ws.Cell(r, 7).Value = Clean(row.Cells["colP2"].Value);
                    ws.Cell(r, 8).Value = Clean(row.Cells["colPdb"].Value);
                    ws.Cell(r, 9).Value = Clean(row.Cells["colProv"].Value);
                    ws.Cell(r, 10).Value = Clean(row.Cells["colOwner"].Value);
                    ws.Cell(r, 11).Value = Clean(row.Cells["colPerm"].Value);

                    // ผลลัพธ์: เขียว = อนุญาต / แดง = ปฏิเสธ
                    var cRes = ws.Cell(r, 2).Style;
                    cRes.Font.Bold = true;
                    cRes.Font.FontColor = res.Contains("อนุญาต") ? XLColor.FromArgb(0, 128, 0) : XLColor.FromArgb(200, 0, 0);

                    ws.Row(r).Height = 72;    // ~96 พิกเซล พอดีกับรูป

                    AddPic(ws, row.Cells["colW1"].Value?.ToString(), r, 12);
                    AddPic(ws, row.Cells["colW2"].Value?.ToString(), r, 13);
                    AddPic(ws, row.Cells["colC1"].Value?.ToString(), r, 14);
                    AddPic(ws, row.Cells["colC2"].Value?.ToString(), r, 15);
                    r++;
                }

                // ---- ฟอนต์ + เส้นตาราง ทั้งผืน ----
                var all = ws.Range(1, 1, r - 1, headers.Length);
                all.Style.Font.FontName = THAI_FONT;
                all.Style.Font.FontSize = 14;
                all.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                all.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                all.Style.Border.InsideBorderColor = XLColor.FromArgb(170, 170, 170);
                all.Style.Border.OutsideBorderColor = XLColor.FromArgb(120, 120, 120);
                all.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                ws.Range(2, 3, r - 1, 3).Style.Alignment.WrapText = true;   // ช่องเงื่อนไขให้ตัดบรรทัด
                ws.Range(2, 1, r - 1, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Range(2, 4, r - 1, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.SheetView.FreezeRows(1);          // เลื่อนแล้วหัวตารางยังอยู่
                ws.Range(1, 1, r - 1, 11).SetAutoFilter();

                wb.SaveAs(fileName);
            }
        }

        /// <summary>วางรูปลงเซลล์ ย่อให้พอดีกรอบโดยคงสัดส่วนเดิม</summary>
        private static void AddPic(IXLWorksheet ws, string path, int row, int col)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var pic = ws.AddPicture(fs).MoveTo(ws.Cell(row, col), 4, 4);
                    double scale = Math.Min((double)IMG_BOX_W / pic.OriginalWidth,
                                            (double)IMG_BOX_H / pic.OriginalHeight);
                    if (scale > 1) scale = 1;        // รูปเล็กอยู่แล้ว ไม่ต้องขยายจนแตก
                    pic.WithSize((int)(pic.OriginalWidth * scale), (int)(pic.OriginalHeight * scale));
                }
            }
            catch { /* รูปเสีย/เปิดไม่ได้ ข้ามไป ไม่ให้ล้มทั้งไฟล์ */ }
        }

        /// <summary>ตัดอีโมจิออก เพราะฟอนต์ TH Sarabun New แสดงไม่ได้ จะกลายเป็นสี่เหลี่ยม</summary>
        private static string Clean(object v)
        {
            string s = v?.ToString() ?? "";
            foreach (string em in new[] { "✅", "⛔", "⚠️", "⚠", "✔", "🔄", "📋", "\uFE0F" })
                s = s.Replace(em, "");
            return s.Trim();
        }

        // ---------- ส่งออก CSV ----------
        private void BtnCsv_Click(object sender, EventArgs e)
        {
            if (dgv.Rows.Count == 0)
            {
                MessageBox.Show("ยังไม่มีข้อมูลให้บันทึก\nเลือกช่วงวันที่แล้วกด 'แสดงข้อมูล' ก่อน",
                                "ส่งออก CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string suggest = dtFrom.Value.Date == dtTo.Value.Date
                ? $"ประวัติเข้าออก_{dtFrom.Value:yyyy-MM-dd}.csv"
                : $"ประวัติเข้าออก_{dtFrom.Value:yyyy-MM-dd}_ถึง_{dtTo.Value:yyyy-MM-dd}.csv";

            using (var sfd = new SaveFileDialog
            {
                Title = "เลือกที่บันทึกไฟล์ CSV",
                Filter = "ไฟล์ CSV (*.csv)|*.csv",
                FileName = suggest,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            })
            {
                if (sfd.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("วันที่/เวลา,ผลลัพธ์,เงื่อนไขที่ตรวจได้,โหมด,รหัสแท็ก RFID," +
                                  "ทะเบียนกล้องหน้า,ทะเบียนกล้องหลัง,ทะเบียนในระบบ,จังหวัด,เจ้าของ,สิทธิ์," +
                                  "ภาพมุมกว้างหน้า,ภาพมุมกว้างหลัง,ภาพป้ายหน้า,ภาพป้ายหลัง");

                    foreach (DataGridViewRow row in dgv.Rows)
                    {
                        sb.AppendLine(string.Join(",", new[]
                        {
                            Esc(row.Cells["colTs"].Value),   Esc(row.Cells["colResult"].Value),
                            Esc(row.Cells["colReason"].Value), Esc(row.Cells["colMode"].Value),
                            Esc(row.Cells["colTag"].Value),  Esc(row.Cells["colP1"].Value),
                            Esc(row.Cells["colP2"].Value),   Esc(row.Cells["colPdb"].Value),
                            Esc(row.Cells["colProv"].Value), Esc(row.Cells["colOwner"].Value),
                            Esc(row.Cells["colPerm"].Value), Esc(row.Cells["colW1"].Value),
                            Esc(row.Cells["colW2"].Value),   Esc(row.Cells["colC1"].Value),
                            Esc(row.Cells["colC2"].Value)
                        }));
                    }

                    // UTF-8 พร้อม BOM — จำเป็นมาก ไม่งั้น Excel เปิดแล้วภาษาไทยเป็นตัวยึกยือ
                    File.WriteAllText(sfd.FileName, sb.ToString(), new UTF8Encoding(true));

                    if (MessageBox.Show($"บันทึกแล้ว {dgv.Rows.Count} รายการ\n\n{sfd.FileName}\n\nเปิดไฟล์เลยไหม?",
                        "ส่งออก CSV สำเร็จ", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("บันทึกไฟล์ไม่ได้: " + ex.Message +
                                    "\n(ถ้าไฟล์เปิดค้างอยู่ใน Excel ให้ปิดก่อน)", "ผิดพลาด",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>ครอบด้วยเครื่องหมายคำพูดเสมอ กันข้อความที่มีคอมมาทำคอลัมน์เพี้ยน</summary>
        private static string Esc(object v)
        {
            string s = v?.ToString() ?? "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}