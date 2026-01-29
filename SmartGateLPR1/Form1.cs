using OpenCvSharp;
using OpenCvSharp.Extensions;
using SmartGateLPR;
using SmartGateLPR1;
using System;
using System.Drawing;
using System.Net.Sockets; // สำหรับ TCP
using System.Text;        // สำหรับแปลง bytes เป็น string
using System.Threading;
using System.Windows.Forms;

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

        private void btnTestAddData_Click_1(object sender, EventArgs e)
        {
            {
                // 1. รับค่าจากช่องที่พิมพ์ (ใช้ .Trim() เพื่อตัดช่องว่างหน้าหลังออก)
                string plate = txtPlateInput.Text.Trim();
                string rfid = txtRFIDInput.Text.Trim();
                string name = txtNameInput.Text.Trim();

                // 2. ตรวจสอบว่ากรอกครบไหม (Validation)
                if (string.IsNullOrEmpty(plate) || string.IsNullOrEmpty(name))
                {
                    MessageBox.Show("กรุณากรอก 'เลขทะเบียน' และ 'ชื่อเจ้าของ' ให้ครบถ้วน");
                    return; // จบการทำงาน ไม่บันทึก
                }

                try
                {
                    // 3. ส่งข้อมูลเข้า Database
                    db.AddUser(plate, rfid, name);

                    MessageBox.Show($"บันทึกข้อมูลเรียบร้อย!\nทะเบียน: {plate}\nชื่อ: {name}");

                    // 4. เคลียร์ช่องให้ว่าง เตรียมกรอกคนต่อไป
                    txtPlateInput.Text = "";
                    txtRFIDInput.Text = "";
                    txtNameInput.Text = "";
                }
                catch (Exception ex)
                {
                    MessageBox.Show("เกิดข้อผิดพลาด: " + ex.Message);
                }
            }
        }

        private void btnCheckData_Click_1(object sender, EventArgs e)
        {
            {
                // เอาค่าจากช่องที่เราพิมพ์ มาสมมติว่าเป็นค่าที่อ่านได้จากกล้อง/RFID
                string plateRead = txtPlateInput.Text.Trim();
                string rfidRead = txtRFIDInput.Text.Trim();

                if (string.IsNullOrEmpty(plateRead) && string.IsNullOrEmpty(rfidRead))
                {
                    MessageBox.Show("กรุณากรอกข้อมูลที่จะทดสอบ (ทะเบียน หรือ RFID)");
                    return;
                }

                // ถาม Database
                string owner = db.CheckPermission(plateRead, rfidRead);

                if (owner != null)
                {
                    MessageBox.Show($"✅ ผ่าน! ยินดีต้อนรับคุณ: {owner}");
                    db.SaveLog(plateRead, rfidRead, "test.jpg", "ALLOWED");
                }
                else
                {
                    MessageBox.Show($"❌ ไม่ผ่าน! ไม่พบข้อมูลในระบบ");
                    db.SaveLog(plateRead, rfidRead, "test.jpg", "DENIED");
                }
            }
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
                            this.Invoke(new Action(() => {
                                MessageBox.Show("Login ไม่ผ่าน!");
                                btnConnectRFID.Enabled = true; // ปลดล็อกปุ่มให้ลองใหม่
                                lblRfidStatus1.Text = "Login ผิดพลาด";
                            }));
                            rfidTelnet.Disconnect();
                        }
                    }
                    else
                    {
                        this.Invoke(new Action(() => {
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
                                        txtRFIDInput.Text = tagId;
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
            this.Invoke(new Action(() => {
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
    }
}