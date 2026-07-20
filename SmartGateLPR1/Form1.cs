using Newtonsoft.Json;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using SmartGateLPR;
using SmartGateLPR1;
using System;
using System;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing;
using System.Net.Http;
using System.Net.Sockets; // สำหรับ TCP
using System.Reflection.Emit;
using System.Text;        // สำหรับแปลง bytes เป็น string
using System.Threading;
using System.Threading.Tasks;
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
        private System.Windows.Forms.Timer timerAutoLPR; // ตัวนับเวลาสำหรับส่งภาพไปตรวจ
        private bool isProcessingLPR = false; // ตัวเช็คว่า Python กำลังทำงานอยู่ไหม (กันงานชนกัน)

        private bool isRfidRunning = false;
        private bool isCam1Running = false;
        private bool isCam2Running = false;

        private DatabaseHelper db;

        // ตัวแปร Global สำหรับรองรับกล้อง 2 ตัว (แยกตาม ID กล้อง)
        private Rectangle triggerZone = new Rectangle(150, 200, 400, 200);
        private double triggerThreshold = 20.0;
        private int cooldownSeconds = 1;

        // เปลี่ยน 2 บรรทัดนี้ให้เป็น Array ขนาด 3 ช่อง (เพื่อใช้ช่อง index 1 และ 2 ให้ตรงกับ ID กล้อง)
        private Bitmap[] previousZoneImages = new Bitmap[3];
        private DateTime[] lastCaptureTimes = new DateTime[] { DateTime.MinValue, DateTime.MinValue, DateTime.MinValue };

        private Rectangle[] latestPlateBox = new Rectangle[3];
        private bool[] hasPlateBox = new bool[3];
        private string[] latestPlateText = new string[] { "", "", "" };
        private DateTime[] latestBoxTime = new DateTime[] { DateTime.MinValue, DateTime.MinValue, DateTime.MinValue };
        private DateTime[] lastDetectTimes = new DateTime[] { DateTime.MinValue, DateTime.MinValue, DateTime.MinValue };
        private bool[] isDetecting = new bool[3];
        private readonly object boxLock = new object();
        private int detectIntervalMs = 200;
        private int boxHoldMs = 1500;

        private Panel panelMenu;
        private Button btnMenu;
        private System.Windows.Forms.Timer menuTimer;
        private bool menuOpening = false;
        private const int MenuWidth = 220;
        public btnDisconnectRFID()
        {
            InitializeComponent();
            try { db = new DatabaseHelper(); }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
            InitSideMenu();              
            LoadSavedSettings();
        }


        private void InitSideMenu()
        {
            // ปุ่ม ☰ มุมบนซ้าย
            btnMenu = new Button
            {
                Text = "☰",
                Font = new System.Drawing.Font("Segoe UI", 14, FontStyle.Bold),
                Size = new System.Drawing.Size(44, 36),
                Location = new System.Drawing.Point(8, 8),
                FlatStyle = FlatStyle.Flat,
            };
            btnMenu.FlatAppearance.BorderSize = 0;
            btnMenu.Click += (s, e) => ToggleMenu();

            // แผงเมนูซ้าย เริ่มกว้าง 0 (ซ่อนอยู่)
            panelMenu = new Panel
            {
                Width = 0,
                Dock = DockStyle.Left,
                BackColor = Color.FromArgb(45, 45, 48),
            };

            var lblTitle = new System.Windows.Forms.Label
            {
                Text = "การตั้งค่าอุปกรณ์",
                ForeColor = Color.White,
                Font = new System.Drawing.Font("Tahoma", 11, FontStyle.Bold),
                Location = new System.Drawing.Point(16, 55),
                AutoSize = true,
            };
            panelMenu.Controls.Add(lblTitle);

            panelMenu.Controls.Add(MakeMenuButton("📷  ตั้งค่ากล้อง", 100, (s, e) =>
            {
                ToggleMenu();
                using (var f = new CameraSettingsForm())
                    if (f.ShowDialog(this) == DialogResult.OK) LoadSavedSettings();
            }));
            panelMenu.Controls.Add(MakeMenuButton("📡  ตั้งค่า RFID", 150, (s, e) =>
            {
                ToggleMenu();
                using (var f = new RfidSettingsForm())
                    if (f.ShowDialog(this) == DialogResult.OK) LoadSavedSettings();
            }));
            panelMenu.Controls.Add(MakeMenuButton("🚧  ตั้งค่าไม้กั้น", 200, (s, e) =>
            {
                ToggleMenu();
                MessageBox.Show("หน้าตั้งค่าไม้กั้น — เดี๋ยวทำตามแบบ CameraSettingsForm");
            }));
            panelMenu.Controls.Add(MakeMenuButton("🗂  บันทึกป้ายทะเบียน", 250, (s, e) =>
            {
                ToggleMenu();
                using (var f = new PlateTableForm())
                    f.ShowDialog(this);
            }));
            panelMenu.Controls.Add(MakeMenuButton("💾  ตั้งค่าฐานข้อมูล", 300, (s, e) =>
            {
                ToggleMenu();
                using (var f = new StorageSettingsForm()) f.ShowDialog(this);
            }));

            Controls.Add(panelMenu);
            Controls.Add(btnMenu);
            btnMenu.BringToFront();

            // ตัวทำอนิเมชันสไลด์
            menuTimer = new System.Windows.Forms.Timer { Interval = 10 };
            menuTimer.Tick += (s, e) =>
            {
                if (menuOpening)
                {
                    panelMenu.Width += 25;
                    if (panelMenu.Width >= MenuWidth) { panelMenu.Width = MenuWidth; menuTimer.Stop(); }
                }
                else
                {
                    panelMenu.Width -= 25;
                    if (panelMenu.Width <= 0) { panelMenu.Width = 0; menuTimer.Stop(); }
                }
            };
        }

        private Button MakeMenuButton(string text, int top, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new System.Drawing.Size(MenuWidth - 20, 40),
                Location = new System.Drawing.Point(10, top),
                Font = new System.Drawing.Font("Tahoma", 10),
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += onClick;
            return b;
        }

        private void ToggleMenu()
        {
            menuOpening = panelMenu.Width < MenuWidth / 2;
            panelMenu.BringToFront();
            btnMenu.BringToFront();
            menuTimer.Start();
        }

        private void LoadSavedSettings()
        {
            var st = SettingsStore.Load();
            // ⚠️ เปลี่ยน txtRtsp1 / txtRtsp2 เป็น "ชื่อจริง" ของช่องกรอก RTSP สองช่องในฟอร์มคุณ
            if (!string.IsNullOrEmpty(st.RtspCamera1)) txtRTSP.Text = st.RtspCamera1;
            if (!string.IsNullOrEmpty(st.RtspCamera2)) txtRTSP2.Text = st.RtspCamera2;
            if (!string.IsNullOrEmpty(st.RfidIp)) txtRfidIP.Text = st.RfidIp;
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
                
                ShowNoSignal(displayBox, "❌ ไม่มีการเชื่อมต่อกล้อง");

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

                        // --- 1. โชว์ภาพสดขึ้นหน้าจอ UI ทันที (ทำทุกเฟรม ภาพจะได้ไม่กระตุก) ---
                        Bitmap displayImage = (Bitmap)image.Clone();
                        DrawPlateOverlay(displayImage, camId);
                        displayBox.Invoke(new Action(() =>
                        {
                            if (displayBox.Image != null) displayBox.Image.Dispose();
                            displayBox.Image = displayImage;
                        }));

                        // --- 1.5 อัปเดตกรอบแดงให้ตามป้าย: เรียก /detect ทุก ~detectIntervalMs ---
                        if (!isDetecting[camId] &&
                            (DateTime.Now - lastDetectTimes[camId]).TotalMilliseconds >= detectIntervalMs)
                        {
                            lastDetectTimes[camId] = DateTime.Now;
                            Bitmap detectFrame = new Bitmap(image);
                            Task.Run(() => DetectBox(detectFrame, camId));
                        }


                        // --- 2. ระบบ Auto-Trigger เช็คป้ายทะเบียน ---
                        // 1. เช็ค Cooldown แยกตามกล้องตัวนั้นๆ
                        if ((DateTime.Now - lastCaptureTimes[camId]).TotalSeconds < cooldownSeconds)
                        {
                            image.Dispose();
                            continue;
                        }

                        // 2. ตัดภาพ (Crop) เอาเฉพาะในกรอบ Trigger Zone ที่เราตีเส้นไว้
                        Bitmap currentZoneImage = image.Clone(triggerZone, image.PixelFormat);

                        // เช็คภาพพื้นหลังแยกตามกล้อง
                        if (previousZoneImages[camId] == null)
                        {
                            previousZoneImages[camId] = currentZoneImage;
                            image.Dispose();
                            continue;
                        }

                        // คำนวณความต่าง โดยเทียบกับภาพก่อนหน้าของกล้องตัวเองเท่านั้น!
                        double diffPercentage = CalculateDifference(previousZoneImages[camId], currentZoneImage);

                        // ถ้ามีการเปลี่ยนแปลงเกินเกณฑ์ (มีรถวิ่งเข้ากล้องตัวนั้น)
                        if (diffPercentage >= triggerThreshold)
                        {
                            lastCaptureTimes[camId] = DateTime.Now; // เริ่มนับ Cooldown ของกล้องตัวนี้

                            // ส่งรูปเต็มไปให้ AI อ่าน
                            Bitmap frameToSend = new Bitmap(image);
                            Task.Run(() => SendToAI(frameToSend, camId));
                        }

                        // อัปเดตภาพเก่าเก็บไว้เทียบในเฟรมถัดไป (ของใครของมัน)
                        previousZoneImages[camId].Dispose();
                        previousZoneImages[camId] = currentZoneImage;

                        image.Dispose();
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
                var cfg = SettingsStore.Load();
                string ip = !string.IsNullOrWhiteSpace(cfg.RfidIp) ? cfg.RfidIp : txtRfidIP.Text.Trim();
                int port = cfg.RfidPort > 0 ? cfg.RfidPort : 23;
                string rfidUser = string.IsNullOrEmpty(cfg.RfidUser) ? "alien" : cfg.RfidUser;
                string rfidPass = string.IsNullOrEmpty(cfg.RfidPassword) ? "password" : cfg.RfidPassword;

                lblRfidStatus1.Text = "กำลังเชื่อมต่อ...";
                lblRfidStatus1.ForeColor = Color.Orange;
                btnConnectRFID.Enabled = false; // ล็อกปุ่มชั่วคราวกันกดรัวๆ

                // เริ่ม Thread เชื่อมต่อ
                Thread loginThread = new Thread(() =>
                {
                    rfidTelnet = new SimpleTelnet();

                    if (rfidTelnet.Connect(ip, port))
                    {
                        bool loginSuccess = rfidTelnet.Login(rfidUser, rfidPass);

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

        private void label3_Click_2(object sender, EventArgs e)
        {

        }

        // 💡 1. เพิ่มตัวแปรเช็คสถานะ AI ไว้ (สำคัญมาก ป้องกัน RAM ล้น)
        private bool isAIProcessing = false;

        // ✅ 3. ฟังก์ชันส่งรูปไปให้ Python API (ฉบับแก้ RAM ระเบิด 30GB)
        private async Task SendToAI(Bitmap bitmap, int camId)
        {
            // ถ้า AI ยังประมวลผลรูปเก่าไม่เสร็จ ให้โยนรูปใหม่ทิ้งทันที! ไม่ต้องรอคิวให้หนัก RAM
            if (isAIProcessing)
            {
                bitmap.Dispose();
                return;
            }

            isAIProcessing = true; // ล็อกคิวบอกว่า AI กำลังทำงาน

            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15); // ตั้งเวลาเผื่อ AI ค้าง จะได้ตัดจบ

                    using (var ms = new MemoryStream())
                    {
                        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                        var content = new MultipartFormDataContent();
                        content.Add(new ByteArrayContent(ms.ToArray()), "image", "frame.jpg");

                        // ยิงไปที่ Python API
                        var response = await client.PostAsync("http://localhost:5000/predict", content);
                        var jsonResponse = await response.Content.ReadAsStringAsync();

                        // แกะคำตอบ JSON มาโชว์บนหน้าจอ
                        dynamic result = JsonConvert.DeserializeObject(jsonResponse);
                        if (result != null && result.status == "success")
                        {
                            string plateText = (string)result.text;
                            string fullText = result.full_text != null ? (string)result.full_text : plateText;

                            this.Invoke((MethodInvoker)delegate
                            {
                                lblLicensePlate.Text = fullText;
                                lblLicensePlate.ForeColor = Color.Green;
                            });

                            lock (boxLock)
                            {
                                latestPlateText[camId] = fullText;
                                if (result.box != null)
                                {
                                    int bx1 = (int)result.box[0], by1 = (int)result.box[1];
                                    int bx2 = (int)result.box[2], by2 = (int)result.box[3];
                                    latestPlateBox[camId] = new Rectangle(bx1, by1, bx2 - bx1, by2 - by1);
                                    hasPlateBox[camId] = true;
                                    latestBoxTime[camId] = DateTime.Now;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("API Error: " + ex.Message);
            }
            finally
            {
                isAIProcessing = false; // ปลดล็อกคิวรับรูปใหม่
                bitmap.Dispose();       // 💡 เคลียร์ขยะรูปนี้ออกจาก RAM ทันที
            }
        }

        // ✅ 4. ฟังก์ชันคำนวณความต่างของพิกเซล (ฉบับประหยัด CPU ไม่ค้าง)
        private double CalculateDifference(Bitmap img1, Bitmap img2)
        {
            // 💡 ย่อภาพเป็น 100x100 ก่อนคำนวณ เพื่อไม่ให้ CPU โหลดหนักตอนเจอกล้อง 2K
            using (Bitmap bmp1 = new Bitmap(img1, new System.Drawing.Size(100, 100)))
            using (Bitmap bmp2 = new Bitmap(img2, new System.Drawing.Size(100, 100)))
            {
                int diffCount = 0;
                int totalPixels = bmp1.Width * bmp1.Height;

                for (int y = 0; y < bmp1.Height; y++)
                {
                    for (int x = 0; x < bmp1.Width; x++)
                    {
                        Color c1 = bmp1.GetPixel(x, y);
                        Color c2 = bmp2.GetPixel(x, y);

                        // ดูความต่างของสี
                        if (Math.Abs(c1.R - c2.R) + Math.Abs(c1.G - c2.G) + Math.Abs(c1.B - c2.B) > 60)
                        {
                            diffCount++;
                        }
                    }
                }
                return ((double)diffCount / totalPixels) * 100.0;
            }
        }

        private void DrawPlateOverlay(Bitmap bmp, int camId)
        {
            Rectangle box; bool has; string text; DateTime t;
            lock (boxLock)
            {
                has = hasPlateBox[camId]; box = latestPlateBox[camId];
                text = latestPlateText[camId]; t = latestBoxTime[camId];
            }
            if (!has || (DateTime.Now - t).TotalMilliseconds > boxHoldMs) return;

            Rectangle r = Rectangle.Intersect(box, new Rectangle(0, 0, bmp.Width, bmp.Height));
            if (r.Width <= 0 || r.Height <= 0) return;

            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
            using (System.Drawing.Pen pen = new System.Drawing.Pen(Color.Red, 3))
            using (System.Drawing.Font font = new System.Drawing.Font("Tahoma", 14, FontStyle.Bold))
            {
                g.DrawRectangle(pen, r);
                if (!string.IsNullOrEmpty(text))
                {
                    SizeF sz = g.MeasureString(text, font);
                    float ty = r.Y - sz.Height - 2; if (ty < 0) ty = r.Y + 2;
                    g.FillRectangle(Brushes.Red, r.X, ty, sz.Width, sz.Height);
                    g.DrawString(text, font, Brushes.White, r.X, ty);
                }
            }
        }

        private async Task DetectBox(Bitmap bitmap, int camId)
        {
            isDetecting[camId] = true;
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    using (var ms = new MemoryStream())
                    {
                        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                        var content = new MultipartFormDataContent();
                        content.Add(new ByteArrayContent(ms.ToArray()), "image", "frame.jpg");

                        var response = await client.PostAsync("http://localhost:5000/detect", content);
                        var json = await response.Content.ReadAsStringAsync();
                        dynamic result = JsonConvert.DeserializeObject(json);

                        lock (boxLock)
                        {
                            if (result != null && result.status == "success" && result.box != null)
                            {
                                int x1 = (int)result.box[0], y1 = (int)result.box[1];
                                int x2 = (int)result.box[2], y2 = (int)result.box[3];
                                latestPlateBox[camId] = new Rectangle(x1, y1, x2 - x1, y2 - y1);
                                hasPlateBox[camId] = true;
                                latestBoxTime[camId] = DateTime.Now;
                                // 🎯 เจอป้ายในเฟรม = จังหวะดีที่สุดที่จะอ่าน → สั่งอ่านเลย (แทน motion trigger)
                                if (!isAIProcessing &&
                                    (DateTime.Now - lastCaptureTimes[camId]).TotalSeconds >= cooldownSeconds)
                                {
                                    lastCaptureTimes[camId] = DateTime.Now;
                                    Bitmap readFrame = new Bitmap(bitmap);
                                    Task.Run(() => SendToAI(readFrame, camId));
                                }
                            }
                            else { hasPlateBox[camId] = false; }
                        }
                    }
                }
            }
            catch { }
            finally { isDetecting[camId] = false; bitmap.Dispose(); }
        }

        private void txtRTSP_TextChanged(object sender, EventArgs e)
        {

        }

        private void ShowNoSignal(PictureBox box, string msg)
        {
            Bitmap bmp = new Bitmap(Math.Max(box.Width, 320), Math.Max(box.Height, 240));
            using (Graphics g = Graphics.FromImage(bmp))
            using (var font = new System.Drawing.Font("Tahoma", 14, FontStyle.Bold))
            {
                g.Clear(Color.Black);
                SizeF sz = g.MeasureString(msg, font);
                g.DrawString(msg, font, Brushes.Red, (bmp.Width - sz.Width) / 2, (bmp.Height - sz.Height) / 2);
            }
            box.Invoke(new Action(() =>
            {
                if (box.Image != null) box.Image.Dispose();
                box.Image = bmp;
            }));
        }
    }
}