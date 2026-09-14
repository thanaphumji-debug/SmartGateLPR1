using System;
using System.Drawing;
using System.Windows.Forms;

namespace SmartGateLPR1
{
    public class EulaForm : Form
    {
        private CheckBox chkAccept = new CheckBox();
        private Button btnOk = new Button();

        public EulaForm()
        {
            Text = "ข้อตกลงการใช้งานซอฟต์แวร์";
            ClientSize = new Size(640, 520);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;

            var txt = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Left = 16,
                Top = 16,
                Width = 608,
                Height = 400,
                Font = new Font("TH Sarabun New", 13),
                BackColor = Color.White,
                Text = EulaText.Replace("\n", "\r\n")
            };

            chkAccept.SetBounds(16, 428, 480, 24);
            chkAccept.Text = "ข้าพเจ้าได้อ่านและยอมรับข้อตกลงการใช้งานข้างต้น";
            chkAccept.Font = new Font("TH Sarabun New", 13, FontStyle.Bold);
            chkAccept.CheckedChanged += (s, e) => btnOk.Enabled = chkAccept.Checked;

            btnOk.SetBounds(432, 464, 90, 32);
            btnOk.Text = "ยอมรับ";
            btnOk.Enabled = false;
            btnOk.DialogResult = DialogResult.OK;

            var btnCancel = new Button { Text = "ไม่ยอมรับ", Left = 532, Top = 464, Width = 92, Height = 32 };
            btnCancel.DialogResult = DialogResult.Cancel;

            AcceptButton = btnOk; CancelButton = btnCancel;
            Controls.AddRange(new Control[] { txt, chkAccept, btnOk, btnCancel });
        }

        private const string EulaText =
@"ข้อตกลงการใช้งานซอฟต์แวร์
การพัฒนาระบบต้นแบบตรวจจับทะเบียนรถโดยผสานเทคโนโลยีระบุตัวตนด้วยคลื่นความถี่วิทยุย่านยูเอชเอฟและระบบรู้จำแผ่นป้ายทะเบียนรถ

1. ลักษณะของซอฟต์แวร์
ซอฟต์แวร์นี้พัฒนาขึ้นเพื่อประกอบการศึกษาในระดับปริญญาตรี สาขาวิชาวิศวกรรมโทรคมนาคม คณะวิศวกรรมศาสตร์และเทคโนโลยี
มหาวิทยาลัยเทคโนโลยีราชมงคลอีสาน นครราชสีมา มีสถานะเป็นระบบต้นแบบ
เพื่อพิสูจน์แนวคิด มิใช่ผลิตภัณฑ์เชิงพาณิชย์

2. ข้อจำกัดความรับผิด
ผู้พัฒนาไม่รับผิดชอบต่อความเสียหายใด ๆ ที่เกิดจากการนำซอฟต์แวร์ไปใช้งาน
ผู้ใช้ต้องไม่ใช้ซอฟต์แวร์นี้เป็นระบบรักษาความปลอดภัยเพียงระบบเดียว
โดยไม่มีมาตรการสำรองอื่นประกอบ

3. ความแม่นยำของการรู้จำป้ายทะเบียน
ผลการรู้จำป้ายทะเบียนขึ้นอยู่กับสภาพแวดล้อม เช่น แสง มุมกล้อง และสภาพของป้าย
ผู้ใช้รับทราบว่าระบบอาจอ่านผิดพลาดได้ และไม่ควรใช้ผลลัพธ์เป็นหลักฐานทางกฎหมาย
โดยปราศจากการตรวจสอบจากเจ้าหน้าที่

4. ข้อมูลส่วนบุคคล
ซอฟต์แวร์มีการบันทึกภาพยานพาหนะ หมายเลขทะเบียน และข้อมูลผู้ครอบครอง
ผู้ติดตั้งมีหน้าที่ปฏิบัติตามกฎหมายคุ้มครองข้อมูลส่วนบุคคล
รวมถึงการแจ้งให้เจ้าของข้อมูลทราบ และการจำกัดสิทธิ์การเข้าถึงข้อมูลที่จัดเก็บ

5. ทรัพย์สินทางปัญญา
ซอฟต์แวร์นี้ใช้ไลบรารีของบุคคลที่สามซึ่งอยู่ภายใต้สัญญาอนุญาตของตนเอง
ผู้ใช้ต้องปฏิบัติตามสัญญาอนุญาตของไลบรารีเหล่านั้นด้วย

การกดปุ่ม ""ยอมรับ"" ถือว่าท่านได้อ่านและตกลงตามเงื่อนไขข้างต้นทั้งหมด

จัดทำโดย: นายธนภูมิ จิรวัฒน์ธนากุล 661721105219-1 และ นายภัทรพล จริงโพธิ์ 66172110542-7 
สาขาวิชาวิศวกรรมโทรคมนาคม คณะวิศวกรรมศาสตร์และเทคโนโลยี
มหาวิทยาลัยเทคโนโลยีราชมงคลอีสาน นครราชสีมา ปีการศึกษา 2569";
    }
}