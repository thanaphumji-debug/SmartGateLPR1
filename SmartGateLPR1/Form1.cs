using OpenCvSharp;
using OpenCvSharp.Extensions;
using SmartGateLPR;
using SmartGateLPR1;
using System;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Net.Sockets; // สำหรับ TCP
using System.Text;        // สำหรับแปลง bytes เป็น string
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace SmartGateLPR1
{
    public partial class btnDisconnectRFID : Form
    {

        // --- 1. ประกาศตัวแปรแยกสำหรับกล้อง 2 ตัว ---
        private Thread threadCam1;
        private Thread threadCam2;
        // --- ส่วนประกาศตัวแปร RFID ---
        private SimpleTelnet rfidTelnet;
        private NetworkStream rfidStream;
        private Thread rfidThread;
        private System.Windows.Forms.Timer timerAutoLPR; // ตัวนับเวลาสำหรับส่งภาพไปตรวจ
        private bool isProcessingLPR = false; // ตัวเช็คว่า Python กำลังทำงานอยู่ไหม (กันงานชนกัน)

        private bool isRfidRunning = false;
        private bool isCam1Running = false;
        private bool isCam2Running = false;

        private DatabaseHelper db;

        public btnDisconnectRFID()
        {
            InitializeComponent();
            try { db = new DatabaseHelper(); }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        // --- Class สำหรับรับค่าจาก Python ---
        public class LprData
        {
            public string text { get; set; }      // เลขทะเบียน
            public string raw_text { get; set; }  // ค่าดิบ
            public double confidence { get; set; } // ความมั่นใจ
        }

        // ฟังก์ชันสำหรับเรียก Python ให้ช่วยอ่านรูป
        private string RunPythonLPR(string imagePath)
        {
            // 1. ตั้งค่า process
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = "python"; // หรือใส่ path เต็มของ python.exe ถ้ามันหาไม่เจอ

            // ใส่ชื่อไฟล์ python script ของเรา และ path รูปที่จะให้อ่าน
            // ** อย่าลืมแก้ path ของไฟล์ .py ให้ตรงกับที่คุณเซฟไว้นะครับ **
            string pythonScriptPath = @"C:\Users\YOURNAME\Desktop\lpr_service.py";

            start.Arguments = string.Format("\"{0}\" \"{1}\"", pythonScriptPath, imagePath);
            start.UseShellExecute = false;
            start.RedirectStandardOutput = true; // ดึงค่าที่ Python สั่ง print()
            start.CreateNoWindow = true; // ไม่ต้องเด้งจอดำๆ ขึ้นมา
            start.StandardOutputEncoding = System.Text.Encoding.UTF8; // อ่านภาษาไทยให้ออก

            // 2. สั่งรัน
            using (Process process = Process.Start(start))
            {
                // อ่านผลลัพธ์ที่ Python ส่งกลับมา
                using (StreamReader reader = process.StandardOutput)
                {
                    string result = reader.ReadToEnd();
                    return result.Trim(); // ส่งเลขทะเบียนกลับไป
                }
            }
        }

        // ฟังก์ชันสำหรับเรียก Python
        private string RunPythonScript(string cmd, string args)
        {
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = cmd; // path ของ python.exe
            start.Arguments = args; // path ของไฟล์ .py และ รูปภาพ
            start.UseShellExecute = false;
            start.RedirectStandardOutput = true; // ดักจับค่าที่ Python พิมพ์ออกมา
            start.RedirectStandardError = true;  // ดักจับ Error
            start.CreateNoWindow = true; // ไม่ต้องโชว์จอดำ
            start.StandardOutputEncoding = System.Text.Encoding.UTF8; // รองรับภาษาไทย
            start.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            using (Process process = Process.Start(start))
            {
                string result = process.StandardOutput.ReadToEnd(); // อ่านค่า JSON
                string error = process.StandardError.ReadToEnd(); // อ่าน Error (ถ้ามี)
                process.WaitForExit();

                if (!string.IsNullOrEmpty(error))
                {
                    // ถ้ามี Error จากฝั่ง Python ให้โชว์ใน Output ของ VS
                    System.Diagnostics.Debug.WriteLine("Python Error: " + error);
                }

                return result; // ส่งค่า JSON กลับไปให้โปรแกรมหลัก
            }
        }

        // --- 2. ปุ่มเปิดกล้อง (สั่งเปิดทั้ง 2 ตัวพร้อมกัน) ---
        private void btnStartCamera_Click(object sender, EventArgs e)
        {
            // -- เริ่มกล้องตัวที่ 1 --
            if (!isCam1Running)
            {
                string url1 = txtRTSP.Text; // ดึง Link กล้อง 1
                if (string.IsNullOrEmpty(url1)) url1 = "rtsp://admin:pass@192.168.1.64:554/stream1"; // แก้ IP ตรงนี้

                isCam1Running = true;
                // ส่ง pbCamera1 และ ตัวแปรเช็คสถานะ เข้าไปในฟังก์ชัน
                threadCam1 = new Thread(() => CaptureCamera(url1, pbCamera1, 1));
                threadCam1.IsBackground = true;
                threadCam1.Start();
            }

            // -- เริ่มกล้องตัวที่ 2 --
            if (!isCam2Running)
            {
                string url2 = txtRTSP2.Text; // ดึง Link กล้อง 2
                if (string.IsNullOrEmpty(url2)) url2 = "rtsp://admin:pass@192.168.1.65:554/stream1"; // แก้ IP ตรงนี้

                isCam2Running = true;
                // ส่ง pbCamera2 และ เลข id 2 เข้าไป
                threadCam2 = new Thread(() => CaptureCamera(url2, pbCamera2, 2));
                threadCam2.IsBackground = true;
                threadCam2.Start();
            }
        }

        // --- 3. ฟังก์ชันดึงภาพ (ใช้ร่วมกันได้ โดยดูจาก ID) ---
        private void CaptureCamera(string url, PictureBox displayBox, int camId)
        {
            VideoCapture capture = new VideoCapture(url);

            if (!capture.IsOpened())
            {
                // ถ้าเชื่อมต่อไม่ได้ ให้แจ้งเตือน (ต้อง Invoke เพราะมาจาก Thread อื่น)
                this.Invoke(new Action(() =>
                {
                    MessageBox.Show($"กล้อง {camId} เชื่อมต่อไม่ได้!");
                }));

                // ปิดสถานะตาม ID
                if (camId == 1) isCam1Running = false;
                else isCam2Running = false;

                return;
            }

            Mat frame = new Mat();

            // วนลูปโดยเช็คสถานะของใครของมัน
            while ((camId == 1 && isCam1Running) || (camId == 2 && isCam2Running))
            {
                try
                {
                    capture.Read(frame);
                    if (!frame.Empty())
                    {
                        Bitmap image = BitmapConverter.ToBitmap(frame);

                        // อัปเดตภาพไปที่ PictureBox ที่ส่งเข้ามา
                        if (displayBox.Image != null) displayBox.Image.Dispose();

                        displayBox.Invoke(new Action(() =>
                        {
                            displayBox.Image = image;
                        }));
                    }
                }
                catch
                {
                    // กรณี Error ข้ามไปก่อน
                }

                Thread.Sleep(30);
            }

            capture.Release();
        }
        // --- ปุ่มที่ 2: เพิ่มข้อมูลทดสอบลง Database ---
        private void btnTestAddData_Click(object sender, EventArgs e)
        {
            try
            {
                // เพิ่มรถตัวอย่าง: ทะเบียน 1กข-9999, RFID 1234567890
                db.AddUser("1กข-9999", "1234567890", "คุณสมชาย ใจดี");
                MessageBox.Show("บันทึกข้อมูลรถตัวอย่างเรียบร้อย!");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
        }

        // --- ปุ่มที่ 3: จำลองการเช็คสิทธิ์ (Check Access) ---
        private void btnCheckData_Click(object sender, EventArgs e)
        {
            {
                // สมมติสถานการณ์: กล้องอ่านได้ทะเบียนนี้
                string plateRead = "1กข-9999";
                string rfidRead = ""; // RFID ยังไม่ได้ต่อ

                // ถาม Database ว่าคนนี้มีสิทธิ์ไหม?
                string owner = db.CheckPermission(plateRead, rfidRead);

                if (owner != null)
                {
                    // กรณีผ่าน: เปิดไม้กั้น
                    MessageBox.Show($"✅ อนุญาตให้เข้า!\nยินดีต้อนรับ: {owner}", "Access Granted");
                    db.SaveLog(plateRead, rfidRead, "path/to/snapshot.jpg", "ALLOWED");
                }
                else
                {
                    // กรณีไม่ผ่าน
                    MessageBox.Show($"❌ ปฏิเสธการเข้า!\nไม่พบข้อมูลในระบบ", "Access Denied");
                    db.SaveLog(plateRead, rfidRead, "path/to/snapshot.jpg", "DENIED");
                }
            }


        }

        // --- 4. แก้ Event ปิดโปรแกรม ให้หยุดทั้ง 2 ตัว ---
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            isCam1Running = false;
            isCam2Running = false;

            // รอให้ Thread จบ
            if (threadCam1 != null && threadCam1.IsAlive) threadCam1.Join(200);
            if (threadCam2 != null && threadCam2.IsAlive) threadCam2.Join(200);

            // เพิ่มโค้ดปิด RFID
            isRfidRunning = false;
            if (rfidTelnet != null) rfidTelnet.Disconnect();
            if (rfidThread != null && rfidThread.IsAlive) rfidThread.Join(200);
        }



        private void groupBox1_Enter(object sender, EventArgs e)
        {

        }

        private void lblRfidStatus1_Click(object sender, EventArgs e)
        {

        }

        private void btnConnectRFID_Click(object sender, EventArgs e)
        {
            // --- กรณีที่ 1: ถ้าเชื่อมต่ออยู่แล้ว (ต้องการตัดสาย) ---
            if (isRfidRunning)
            {
                // 1. สั่งหยุด Loop
                isRfidRunning = false;

                // 2. สั่งตัดสาย Telnet
                if (rfidTelnet != null) rfidTelnet.Disconnect();

                // 3. เปลี่ยนหน้าจอกลับเป็นสถานะเดิม
                btnConnectRFID.Text = "เชื่อมต่อ RFID"; // เปลี่ยนชื่อปุ่มกลับ
                btnConnectRFID.BackColor = Color.LightGray; // (ออปชั่นเสริม) คืนสีปุ่มเดิม
                lblRfidStatus1.Text = "สถานะ: ตัดการเชื่อมต่อแล้ว";
                lblRfidStatus1.ForeColor = Color.Red;
            }
            // --- กรณีที่ 2: ถ้ายังไม่เชื่อมต่อ (ต้องการเริ่มเชื่อมต่อ) ---
            else
            {
                string ip = txtRfidIP.Text;
                int port = 23;

                lblRfidStatus1.Text = "กำลังเชื่อมต่อ...";
                lblRfidStatus1.ForeColor = Color.Orange;
                btnConnectRFID.Enabled = false; // ล็อกปุ่มชั่วคราวกันกดรัวๆ

                // เริ่ม Thread เชื่อมต่อ
                Thread loginThread = new Thread(() =>
                {
                    rfidTelnet = new SimpleTelnet();

                    if (rfidTelnet.Connect(ip, port))
                    {
                        bool loginSuccess = rfidTelnet.Login("alien", "password");

                        if (loginSuccess)
                        {
                            // สั่งห้ามหลับ (Timeout = 0)
                            rfidTelnet.Send("set TimeOut = 0");
                            Thread.Sleep(500);

                            this.Invoke(new Action(() =>
                            {
                                // อัปเดตเมื่อต่อติด
                                lblRfidStatus1.Text = "สถานะ: เชื่อมต่อสำเร็จ";
                                lblRfidStatus1.ForeColor = Color.Green;

                                // *** เปลี่ยนหน้าตาปุ่มให้เป็นปุ่มตัดสาย ***
                                btnConnectRFID.Text = "ตัดการเชื่อมต่อ";
                                btnConnectRFID.Enabled = true; // ปลดล็อกปุ่ม
                            }));

                            // เริ่ม Loop อ่านข้อมูล
                            isRfidRunning = true;
                            rfidThread = new Thread(ReadRfidLoop);
                            rfidThread.IsBackground = true;
                            rfidThread.Start();
                        }
                        else
                        {
                            this.Invoke(new Action(() =>
                            {
                                MessageBox.Show("Login ไม่ผ่าน!");
                                btnConnectRFID.Enabled = true; // ปลดล็อกปุ่มให้ลองใหม่
                                lblRfidStatus1.Text = "Login ผิดพลาด";
                            }));
                            rfidTelnet.Disconnect();
                        }
                    }
                    else
                    {
                        this.Invoke(new Action(() =>
                        {
                            MessageBox.Show("เชื่อมต่อ IP ไม่ได้");
                            btnConnectRFID.Enabled = true; // ปลดล็อกปุ่มให้ลองใหม่
                            lblRfidStatus1.Text = "ไม่พบเครื่อง RFID";
                        }));
                    }
                });

                loginThread.IsBackground = true;
                loginThread.Start();
            }
        }
        private void ReadRfidLoop()
        {
            while (isRfidRunning && rfidTelnet.IsConnected)
            {
                try
                {
                    // 1. ส่งคำสั่งถาม Tag
                    rfidTelnet.Send("Get TagList");

                    // 2. รออ่านคำตอบ (รอคำว่า "Tag:" หรือเครื่องหมาย ">" ที่จบประโยค)
                    // เราใช้ WaitFor เพื่อดึงข้อมูลทั้งหมดที่เครื่องตอบกลับมา
                    string response = rfidTelnet.WaitFor(">");

                    if (!string.IsNullOrEmpty(response))
                    {
                        // ... (โค้ดตัด string เหมือนเดิม) ...
                        string[] lines = response.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                        foreach (string line in lines)
                        {
                            if (line.Contains("Tag:")) // เช็คว่ามีคำว่า Tag: ไหม
                            {
                                // ตัวอย่าง response: Tag:3000E2..., Disc:2023...
                                int startIndex = line.IndexOf("Tag:") + 4;
                                int endIndex = line.IndexOf(",");

                                if (endIndex > startIndex)
                                {
                                    string tagId = line.Substring(startIndex, endIndex - startIndex).Trim();

                                    this.Invoke(new Action(() =>
                                    {
                                        txtRFIDInput2.Text = tagId;
                                        // เรียกฟังก์ชันถ่ายรูป/OCR ต่อตรงนี้
                                    }));
                                }
                            }
                        }
                    }

                    Thread.Sleep(200); // พัก 0.2 วิ ก่อนถามรอบถัดไป
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Loop Error: " + ex.Message);
                }
            }

            // หลุด Loop
            isRfidRunning = false;
            rfidTelnet.Disconnect();
            this.Invoke(new Action(() =>
            {
                lblRfidStatus1.Text = "หลุดการเชื่อมต่อ";
                lblRfidStatus1.ForeColor = Color.Red;
                btnConnectRFID.Enabled = true;
            }));
        }

        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void groupBox2_Enter(object sender, EventArgs e)
        {

        }

        private void txtRfidPort_TextChanged(object sender, EventArgs e)
        {

        }

        private void btnOpenManage_Click(object sender, EventArgs e)
        {
            // สร้างหน้าต่าง ManageForm ขึ้นมา
            ManageForm frm = new ManageForm();

            // สั่งให้โชว์แบบ Dialog (คือต้องปิดหน้านั้นก่อน ถึงจะกลับมาหน้าหลักได้)
            frm.ShowDialog();
        }

        private void txtSimulateRFID_KeyDown(object sender, KeyEventArgs e)
        {
            // เช็คว่าปุ่มที่กด คือปุ่ม Enter หรือไม่?
            if (e.KeyCode == Keys.Enter)
            {
                string id = txtSimulateRFID.Text.Trim(); // รับค่าเลขบัตร

                // สั่งค้นหาใน Database
                DataTable dt = db.GetUserByTag(id);

                if (dt.Rows.Count > 0) // ถ้าเจอข้อมูล (มากกว่า 0 แถว)
                {
                    // ดึงข้อมูลแถวแรกมาโชว์
                    string plate = dt.Rows[0]["plate_number"].ToString();
                    string name = dt.Rows[0]["owner_name"].ToString();

                    // 1. โชว์ข้อมูล
                    lblShowPlate.Text = plate;
                    lblShowName.Text = name;
                    lblStatus.Text = "อนุญาตให้ผ่าน (Access Granted)";
                    lblStatus.ForeColor = Color.Green;

                    timerGate.Interval = 3000; // ตั้งเวลา 3000 ms = 3 วินาที
                    timerGate.Start();         // เริ่มนับถอยหลัง!

                    // 2. เปลี่ยนสีไม้กั้นเป็นเขียว (เปิด)
                    picGate.BackColor = Color.LimeGreen;
                }
                else // ถ้าไม่เจอข้อมูล
                {
                    // แจ้งเตือน
                    lblShowPlate.Text = "-";
                    lblShowName.Text = "-";
                    lblStatus.Text = "ไม่พบข้อมูล (Access Denied)";
                    lblStatus.ForeColor = Color.Red;

                    // เปลี่ยนสีไม้กั้นเป็นแดง (ปิด)
                    picGate.BackColor = Color.Red;
                }

                // เคลียร์ช่องให้พร้อมสแกนคนต่อไป
                txtSimulateRFID.Clear();

                // กันเสียง ติ๊ง! เวลาด Enter
                e.SuppressKeyPress = true;
            }
        }

        private void timerGate_Tick(object sender, EventArgs e)
        {
            // 1. หยุดจับเวลา (เดี๋ยวทำงานซ้ำ)
            timerGate.Stop();

            // 2. เปลี่ยนสีกลับเป็นสีแดง (ปิดไม้กั้น)
            picGate.BackColor = Color.Red;

            // 3. รีเซ็ตสถานะเป็น "พร้อมใช้งาน"
            lblStatus.Text = "พร้อมใช้งาน (Ready)";
            lblStatus.ForeColor = Color.Blue; // หรือสีดำตามชอบ

            // 4. (ออพชั่นเสริม) เคลียร์ชื่อกับทะเบียนออกด้วยก็ได้
            lblShowPlate.Text = "-";
            lblShowName.Text = "-";
        }

        private void label3_Click_1(object sender, EventArgs e)
        {

        }

        // --- ฟังก์ชันเรียก Python (Engine) ---
        private string RunPythonScript(string imagePath)
        {
            // เช็ค Path ให้ชัวร์นะครับ
            string pythonExe = @"C:\Users\Gigabyte_2\AppData\Local\Programs\Python\Python310\python.exe";
            string scriptPath = @"C:\Users\Gigabyte_2\Desktop\lpr_service.py";

            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = pythonExe;
            start.Arguments = $"\"{scriptPath}\" \"{imagePath}\"";
            start.UseShellExecute = false;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.CreateNoWindow = true; // ซ่อนจอดำ
            start.StandardOutputEncoding = System.Text.Encoding.UTF8; // อ่านภาษาไทยออก

            using (Process process = Process.Start(start))
            {
                string result = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return result; // ส่งค่า JSON กลับไป
            }
        }

        private void btnScan_Click(object sender, EventArgs e)
        {
            // 1. เปลี่ยนหน้าตาปุ่มให้รู้ว่าทำงานอยู่
            btnScan.Text = "กำลังประมวลผล...";
            btnScan.Enabled = false;
            lblResult.Text = "รอสักครู่...";
            lblResult.ForeColor = Color.Gray;

            try
            {
                // รูปที่จะทดสอบ (เดี๋ยวอนาคตเราค่อยเปลี่ยนเป็นรูปจากกล้อง)
                string imagePath = @"C:\Users\Gigabyte_2\Desktop\test.jpg";

                // 2. เรียก Python
                string jsonResult = RunPythonScript(imagePath);

                // --- แทรกบรรทัดนี้ เพื่อดูว่า Python ส่งอะไรกลับมา ---
                MessageBox.Show("ค่าที่ได้จาก Python:\n" + jsonResult);

                // 3. แปลงค่า JSON
                try
                {
                    var plates = JsonConvert.DeserializeObject<List<LprData>>(jsonResult);

                    if (plates != null && plates.Count > 0)
                    {
                        // *** เจอทะเบียน! ***
                        string plateNumber = plates[0].text;
                        lblResult.Text = plateNumber;
                        lblResult.ForeColor = Color.Green; // สีเขียว = ผ่าน

                        MessageBox.Show($"อ่านได้: {plateNumber}\nความมั่นใจ: {plates[0].confidence * 100:0.00}%", "สำเร็จ!");
                    }
                    else
                    {
                        // ไม่เจอ
                        lblResult.Text = "ไม่พบป้ายทะเบียน";
                        lblResult.ForeColor = Color.Red;
                    }
                }
                catch
                {
                    // กรณี Python ส่ง Error มา หรือไม่ใช่ JSON
                    lblResult.Text = "Error: อ่านค่าไม่ได้";
                    MessageBox.Show(jsonResult, "ผลลัพธ์จาก Python (Raw)");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("C# Error: " + ex.Message);
            }
            finally
            {
                // คืนค่าปุ่มกลับสู่สภาพเดิม
                btnScan.Text = "อ่านป้ายทะเบียน";
                btnScan.Enabled = true;
            }
        }

        
    }
}