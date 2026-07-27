using ClosedXML.Excel;
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
using Label = System.Windows.Forms.Label;

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
        // ===== ประวัติการเข้า-ออก: เฟรมล่าสุด + ข้อมูลประกอบ =====
        private readonly object frameLock = new object();
        private Bitmap[] lastFrame = new Bitmap[3];     // เฟรมล่าสุดของแต่ละกล้อง (ไว้เซฟภาพประวัติ)
        private string logMode = "RFID", logTag = "", logPlate1 = "", logPlate2 = "",
                       logPlateDb = "", logProvince = "", logOwner = "", logPermission = "";
        private DateTime[] lastDetectTimes = new DateTime[] { DateTime.MinValue, DateTime.MinValue, DateTime.MinValue };
        private bool[] isDetecting = new bool[3];
        private readonly object boxLock = new object();
        private int detectIntervalMs = 66;
        private int boxHoldMs = 350;
        // ใช้ HttpClient ตัวเดียวร่วมกัน (สร้างใหม่ทุกครั้งทำให้ช้าและซ็อกเก็ตเต็ม)
        private static readonly HttpClient httpDetect = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly HttpClient httpPredict = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // ตัวเข้ารหัส JPEG คุณภาพสูง (ใช้เฉพาะภาพที่ส่งไป "อ่านเลข" เท่านั้น)
        private static System.Drawing.Imaging.ImageCodecInfo GetJpegCodec()
        {
            foreach (var c in System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders())
                if (c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid) return c;
            return null;
        }
        private static System.Drawing.Imaging.EncoderParameters MakeJpegQuality(long q)
        {
            var ps = new System.Drawing.Imaging.EncoderParameters(1);
            ps.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, q);
            return ps;
        }
        private static readonly System.Drawing.Imaging.ImageCodecInfo jpegCodec = GetJpegCodec();
        private static readonly System.Drawing.Imaging.EncoderParameters jpegHiQ = MakeJpegQuality(95L);

        private Panel panelMenu;
        private Button btnMenu;
        private System.Windows.Forms.Timer menuTimer;
        private bool menuOpening = false;
        private const int MenuWidth = 220;

        // ===== ศูนย์ตัดสินใจไฮบริด RFID + LPR =====
        private string pendingRfidTag = "";
        private DateTime pendingRfidTime = DateTime.MinValue;
        private string[] pendingPlateCam = new string[] { "", "", "" };   // ป้ายล่าสุดต่อกล้อง [1]=หน้า [2]=หลัง
        private string[] pendingProvCam = new string[] { "", "", "" };
        private DateTime[] pendingPlateCamTime = new DateTime[] { DateTime.MinValue, DateTime.MinValue, DateTime.MinValue };
        private readonly object hybridLock = new object();
        private bool gateBusy = false;                 // กันตัดสินซ้ำระหว่างไม้เปิดค้าง
        private System.Windows.Forms.Timer timerHybridTimeout;

        private int hybridWindowSec = 15;              // สองฝั่งต้องมาห่างกันไม่เกินกี่วินาที
        private int noPlateGraceSec = 10;    // มีบัตรแต่ไม่เจอป้าย รอกี่วิ แล้วปล่อยผ่าน
        private int noPlateDenySec = 15;     // มีบัตรแต่ไม่เจอป้าย รอกี่วิ แล้วปฏิเสธ (สวิตช์ 2 ปิด)
        private bool strictProvince = false;           // true = จังหวัดต้องตรงด้วยถึงเปิด
        private bool requireRfid = true;
        private bool allowNoPlate = true;
        private bool requirePlatesAgree = false;
        private bool allowPlateTagMismatch = false;
        private DateTime plateSeenNoTagAt = DateTime.MinValue;  // เวลาที่เริ่มเห็นป้ายทั้งที่ยังไม่มีแท็ก (โหมด RFID)
        private int plateOnlyDenySec = 3;                       // เจอป้ายแต่ไม่มีแท็กกี่วิ → ปฏิเสธ
        private bool[] plateSeen = new bool[3];   // index 1,2 = กล้องหน้า/หลังเจอป้ายไหม

        // ตัดช่องว่างก่อนเทียบ (ฐานข้อมูลเก็บ "กท 2058" แต่ LPR อ่านได้ "กท2058")
        private static string NormPlate(string s) =>
            (s ?? "").Replace(" ", "").Replace("-", "").Trim();

        private Label PlateLabel(int camId) => camId == 1 ? lblLicensePlate1 : lblLicensePlate2;
        private Label StatusLabel(int camId) => camId == 1 ? lblLprStatus1 : lblLprStatus2;
        private int retryMaxSec = 20;    // มีบัตรแล้ว วนอ่านป้ายซ้ำได้นานสุดกี่วิ ก่อนยอมแพ้
        private bool sawMismatch = false;



        public btnDisconnectRFID()
        {
            InitializeComponent();
            try { db = new DatabaseHelper(); }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
            InitSideMenu();
            LoadSavedSettings();
            LoadAccessPolicy();
            InitHistoryButton();
            ShowCameraPlaceholder(pbCamera1);
            ShowCameraPlaceholder(pbCamera2);
            timerHybridTimeout = new System.Windows.Forms.Timer { Interval = 1000 };
            timerHybridTimeout.Tick += TimerHybridTimeout_Tick;
            timerHybridTimeout.Start();
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
                using (var f = new CameraSettingsForm(this))
                    if (f.ShowDialog(this) == DialogResult.OK) LoadSavedSettings();
            }));
            panelMenu.Controls.Add(MakeMenuButton("📡  ตั้งค่า RFID", 150, (s, e) =>
            {
                ToggleMenu();
                using (var f = new RfidSettingsForm(this))
                    if (f.ShowDialog(this) == DialogResult.OK) LoadSavedSettings();
            }));
            panelMenu.Controls.Add(MakeMenuButton("🚧  ตั้งค่าเงื่อนไขการอนุญาต", 200, (s, e) =>
            {
                ToggleMenu();
                using (var f = new AccessPolicyForm(this)) f.ShowDialog(this);
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

        private void btnStartCamera_Click(object sender, EventArgs e)
        {
            StartCamera(1);
            StartCamera(2);
        }

        public void StartCamera(int camId)
        {
            var cfg = SettingsStore.Load();

            if (camId == 1 && !isCam1Running)
            {
                string url = !string.IsNullOrWhiteSpace(cfg.RtspCamera1) ? cfg.RtspCamera1 : txtRTSP.Text.Trim();
                if (string.IsNullOrWhiteSpace(url))
                {
                    ShowCameraPlaceholder(pbCamera1, "ยังไม่ได้ตั้งค่ากล้อง 1 (☰ → ตั้งค่ากล้อง)");
                    return;
                }
                isCam1Running = true;
                threadCam1 = new Thread(() => CaptureCamera(url, pbCamera1, 1)) { IsBackground = true };
                threadCam1.Start();
            }
            else if (camId == 2 && !isCam2Running)
            {
                string url = !string.IsNullOrWhiteSpace(cfg.RtspCamera2) ? cfg.RtspCamera2 : txtRTSP2.Text.Trim();
                if (string.IsNullOrWhiteSpace(url))
                {
                    ShowCameraPlaceholder(pbCamera2, "ยังไม่ได้ตั้งค่ากล้อง 2 (☰ → ตั้งค่ากล้อง)");
                    return;
                }
                isCam2Running = true;
                threadCam2 = new Thread(() => CaptureCamera(url, pbCamera2, 2)) { IsBackground = true };
                threadCam2.Start();
            }
        }

        public void StopCamera(int camId)
        {
            if (camId == 1) isCam1Running = false;
            else isCam2Running = false;
            // ทิ้งเฟรมค้างของกล้องที่ตัดการเชื่อมต่อ (ประวัติจะได้ไม่เก็บภาพเก่า)
            lock (frameLock)
            {
                if (lastFrame[camId] != null) { lastFrame[camId].Dispose(); lastFrame[camId] = null; }
            }

            lock (hybridLock) plateSeen[camId] = false;
            var pLbl = PlateLabel(camId);
            var sLbl = StatusLabel(camId);
            Action reset = () =>
            {
                sLbl.Text = "สถานะการตรวจจับป้ายทะเบียน"; sLbl.ForeColor = Color.Black;
                pLbl.Text = "แสดงเลขทะเบียน"; pLbl.ForeColor = Color.Black;
            };
            if (this.InvokeRequired) this.BeginInvoke(reset); else reset();

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
                        // เก็บเฟรมล่าสุดไว้ใช้บันทึกภาพประวัติ (เก็บทีละ 1 ใบ ทิ้งใบเก่าทันที กัน RAM บวม)
                        lock (frameLock)
                        {
                            if (lastFrame[camId] != null) lastFrame[camId].Dispose();
                            lastFrame[camId] = (Bitmap)image.Clone();
                        }

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
            ShowCameraPlaceholder(displayBox, "CAMERA NOT FOUND");   // ⬅️ เพิ่ม
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
            if (isRfidRunning) DisconnectRfid();
            else ConnectRfid();
        }

        // เชื่อมต่อ RFID (เรียกได้จากทั้งหน้าหลักและเมนู) — ใช้ค่าจาก settings.json
        public void ConnectRfid()
        {
            if (isRfidRunning) return;   // ต่ออยู่แล้ว ไม่ต้องต่อซ้ำ

            var cfg = SettingsStore.Load();
            string ip = !string.IsNullOrWhiteSpace(cfg.RfidIp) ? cfg.RfidIp : txtRfidIP.Text.Trim();
            int port = cfg.RfidPort > 0 ? cfg.RfidPort : 23;
            string user = string.IsNullOrEmpty(cfg.RfidUser) ? "alien" : cfg.RfidUser;
            string pass = string.IsNullOrEmpty(cfg.RfidPassword) ? "password" : cfg.RfidPassword;

            if (string.IsNullOrWhiteSpace(ip))
            {
                MessageBox.Show("ยังไม่ได้ตั้งค่า IP ของเครื่อง RFID (☰ → ตั้งค่า RFID)");
                return;
            }

            SetRfidUi("กำลังเชื่อมต่อ...", Color.Orange, false);

            Thread loginThread = new Thread(() =>
            {
                rfidTelnet = new SimpleTelnet();
                if (rfidTelnet.Connect(ip, port) && rfidTelnet.Login(user, pass))
                {
                    rfidTelnet.Send("set TimeOut = 0");
                    Thread.Sleep(500);
                    isRfidRunning = true;
                    rfidThread = new Thread(ReadRfidLoop) { IsBackground = true };
                    rfidThread.Start();
                    this.Invoke(new Action(() => SetRfidUi("สถานะ: เชื่อมต่อสำเร็จ", Color.Green, true)));
                }
                else
                {
                    try { rfidTelnet.Disconnect(); } catch { }
                    this.Invoke(new Action(() => SetRfidUi("เชื่อมต่อไม่สำเร็จ — เช็ค IP/user/pass", Color.Red, false)));
                }
            })
            { IsBackground = true };
            loginThread.Start();
        }

        public void DisconnectRfid()
        {
            isRfidRunning = false;
            if (rfidTelnet != null) { try { rfidTelnet.Disconnect(); } catch { } }
            SetRfidUi("สถานะ: ตัดการเชื่อมต่อแล้ว", Color.Red, false);
        }

        // อัปเดตหน้าตาปุ่ม/สถานะ RFID ให้ตรงกัน (connected = true เมื่อต่อติด)
        private void SetRfidUi(string status, Color color, bool connected)
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(() => SetRfidUi(status, color, connected))); return; }
            lblRfidStatus1.Text = status;
            lblRfidStatus1.ForeColor = color;
            btnConnectRFID.Text = connected ? "ตัดการเชื่อมต่อ" : "เชื่อมต่อ RFID";
            btnConnectRFID.Enabled = true;
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
                    try
                    {
                        System.IO.File.AppendAllText("rfid_debug.txt",
                        DateTime.Now.ToString("HH:mm:ss") + " >>> " + response + "\r\n-----\r\n");
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(response))
                    {
                        // ... (โค้ดตัด string เหมือนเดิม) ...
                        string[] lines = response.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                        foreach (string raw in lines)
                        {
                            string body = raw;

                            // ตัดตั้งแต่คอมมาแรกทิ้ง (ทิ้ง Disc/Last/Count/Ant/Proto)
                            int c = body.IndexOf(',');
                            if (c >= 0) body = body.Substring(0, c);

                            // ตัดคำว่า "Tag:" ออก ถ้ามี
                            int t = body.IndexOf("Tag:");
                            if (t >= 0) body = body.Substring(t + 4);

                            // เก็บเฉพาะตัวอักษรฐาน 16 (ทิ้งช่องว่าง/แท็บ/อักขระแปลกทั้งหมด)
                            var sb = new StringBuilder();
                            foreach (char ch in body) if (Uri.IsHexDigit(ch)) sb.Append(ch);
                            string tagId = sb.ToString().ToUpper();

                            // เขียน log ไว้ดูว่าตัดได้อะไร
                            try
                            {
                                System.IO.File.AppendAllText("rfid_debug.txt",
                                "   ตัดได้ = [" + tagId + "] len=" + tagId.Length + "\r\n");
                            }
                            catch { }

                            if (tagId.Length >= 8)
                                this.Invoke(new Action(() => OnRfidScanned(tagId)));
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
                if (e.KeyCode == Keys.Enter)
                {
                    OnRfidScanned(txtSimulateRFID.Text.Trim());
                    txtSimulateRFID.Clear();
                    e.SuppressKeyPress = true;
                }
            }
        }

        private void timerGate_Tick(object sender, EventArgs e)
        {
            timerGate.Stop();
            gateBusy = false;                              // พร้อมรับคันถัดไป
            lock (hybridLock) { sawMismatch = false; plateSeenNoTagAt = DateTime.MinValue; }

            // รีเซ็ตโซนอนุญาต: ไฟแดง + ข้อความรอ (SetAccessUi จัดการ picGate/lblShowPlate/lblShowName/lblStatus ให้หมดแล้ว)
            SetAccessUi("⚪ รอตรวจสอบ...", Color.Gray, Color.Red, "-", "-", "พร้อมใช้งาน");

            // รีเซ็ตสถานะ RFID กลับเป็น "รอตรวจจับ"
            lblRfidStatus.Text = "⏳ กำลังรอตรวจจับแท็ก RFID...";
            lblRfidStatus.ForeColor = Color.Gray;

            // รีเซ็ตฝั่ง LPR
            lock (hybridLock) { plateSeen[1] = false; plateSeen[2] = false; }
            UpdateLprZone(1);
            UpdateLprZone(2);
        }

        // จุดรับข้อมูลจาก RFID (ทั้งตัวจริงและจำลอง)
        public void OnRfidScanned(string tagId)
        {
            if (string.IsNullOrWhiteSpace(tagId)) return;
            lock (hybridLock)
            {
                pendingRfidTag = tagId.Trim();
                pendingRfidTime = DateTime.Now;
                plateSeenNoTagAt = DateTime.MinValue;   // ⬅️ เพิ่ม: แท็กมาแล้ว ยกเลิกนับถอยหลังปฏิเสธ
            }
            this.BeginInvoke(new Action(() =>
            {
                txtRFIDInput2.Text = tagId;
                lblRfidStatus.Text = "✅ ตรวจพบแท็ก RFID แล้ว";
                lblRfidStatus.ForeColor = Color.Green;
                lblResult.Text = "⏳ กำลังรอข้อมูลจาก LPR...";
                lblResult.ForeColor = Color.DarkOrange;
            }));
            TryDecide();
        }

        // จุดรับข้อมูลจาก LPR (เรียกตอนอ่านป้ายสำเร็จ)
        public void OnPlateRead(string plate, string province, int camId)
        {
            if (string.IsNullOrWhiteSpace(plate)) return;
            lock (hybridLock)
            {
                pendingPlateCam[camId] = plate.Trim();
                pendingProvCam[camId] = (province ?? "").Trim();
                pendingPlateCamTime[camId] = DateTime.Now;
                if (requireRfid && pendingRfidTag == "" && plateSeenNoTagAt == DateTime.MinValue)   // ⬅️ เพิ่ม
                    plateSeenNoTagAt = DateTime.Now;                                                 // ⬅️ เพิ่ม
            }
            this.BeginInvoke(new Action(() =>
            {
                if (string.IsNullOrEmpty(pendingRfidTag))
                {
                    lblRfidStatus.Text = "⏳ กำลังรอตรวจจับแท็ก RFID...";
                    lblRfidStatus.ForeColor = Color.Gray;
                    lblResult.Text = "⏳ รอข้อมูลจาก RFID...";
                    lblResult.ForeColor = Color.DarkOrange;
                }
            }));
            TryDecide();
        }

        private void LoadAccessPolicy()
        {
            var st = SettingsStore.Load();
            requireRfid = st.RequireRfid;
            allowNoPlate = st.AllowNoPlate;
            requirePlatesAgree = st.RequirePlatesAgree;
            allowPlateTagMismatch = st.AllowPlateTagMismatch;
        }
        public void ReloadAccessPolicy() => LoadAccessPolicy();

        // ตัดสินเมื่อข้อมูลครบสองฝั่งภายในหน้าต่างเวลา
        private void TryDecide()
        {
            string tag, p1, p2;
            lock (hybridLock)
            {
                if (gateBusy) return;
                bool rfidFresh = pendingRfidTag != "" &&
                                 (DateTime.Now - pendingRfidTime).TotalSeconds <= hybridWindowSec;

                p1 = (pendingPlateCam[1] != "" && (DateTime.Now - pendingPlateCamTime[1]).TotalSeconds <= hybridWindowSec) ? pendingPlateCam[1] : "";
                p2 = (pendingPlateCam[2] != "" && (DateTime.Now - pendingPlateCamTime[2]).TotalSeconds <= hybridWindowSec) ? pendingPlateCam[2] : "";
                bool havePlate = p1 != "" || p2 != "";

                if (requireRfid && !rfidFresh) return;   // โหมดบังคับบัตร: ไม่มีบัตรไม่ตัดสิน
                if (!rfidFresh && !havePlate) return;     // ไม่มีทั้งบัตรและป้าย รอต่อ

                tag = rfidFresh ? pendingRfidTag : "";
            }

            if (tag != "") { DecideWithRfid(tag, p1, p2); return; }   // มีบัตร → ไฮบริด
            DecideLprOnly(p1, p2);                                    // ไม่มีบัตร (requireRfid=false) → LPR อย่างเดียว
        }

        // ---- โหมดมีบัตร (ไฮบริด) ----
        private void DecideWithRfid(string tag, string p1, string p2)
        {
            DataTable dt = db.GetUserByTag(tag);
            if (dt.Rows.Count == 0)
            {
                logMode = "RFID"; logTag = tag; logPlate1 = p1; logPlate2 = p2;
                logPlateDb = ""; logProvince = ""; logOwner = ""; logPermission = "";
                lock (hybridLock) { gateBusy = true; pendingRfidTag = ""; }
                DenyAccess($"ไม่พบบัตร {tag} ในระบบ");
                return;
            }

            var row = dt.Rows[0];
            string dbPlate = row["plate_number"]?.ToString() ?? "";
            string dbPerm = row.Table.Columns.Contains("permission") ? row["permission"]?.ToString() ?? "" : "";
            string owner = row["owner_name"]?.ToString() ?? "";
            string dbProv = row.Table.Columns.Contains("province") ? row["province"]?.ToString() ?? "" : "";
            string dbPlateShow = dbProv != "" ? dbPlate + " " + dbProv : dbPlate;   // ทะเบียน + จังหวัด สำหรับแสดงผล
            logMode = "RFID"; logTag = tag; logPlate1 = p1; logPlate2 = p2;
            logPlateDb = dbPlate; logProvince = dbProv; logOwner = owner; logPermission = dbPerm;

            bool havePlate = p1 != "" || p2 != "";
            bool bothRead = p1 != "" && p2 != "";
            bool platesDisagree = bothRead && NormPlate(p1) != NormPlate(p2);   // อ่านได้ทั้งคู่แต่เลขคนละอัน
            bool m1 = p1 != "" && NormPlate(p1) == NormPlate(dbPlate);
            bool m2 = p2 != "" && NormPlate(p2) == NormPlate(dbPlate);

            // สวิตช์ "ต้องตรงทั้ง 2 กล้อง" = บล็อกเฉพาะตอนอ่านได้ทั้งคู่แต่ขัดกัน
            // (รถติดป้ายด้านเดียว อีกกล้องอ่านไม่เจอ → ไม่ถือว่าขัด ยังผ่านได้)
            bool blockedByDisagree = requirePlatesAgree && platesDisagree;
            bool plateOk = blockedByDisagree ? false : (m1 || m2);

            if (plateOk)
            {
                lock (hybridLock)
                {
                    gateBusy = true; sawMismatch = false;
                    pendingRfidTag = ""; pendingPlateCam[1] = ""; pendingPlateCam[2] = "";
                }
                string which = (m1 && m2) ? "กล้องหน้า+หลัง" : (m1 ? "กล้องหน้า" : "กล้องหลัง");
                GrantAccess(owner, dbPlateShow, dbPerm, $"ยืนยันผ่าน {which} ตรงกับบัตร");
                return;
            }

            // สวิตช์: อ่านป้ายได้แต่ไม่ตรง + อนุญาตให้ผ่านด้วยบัตร (แต่ถ้าหน้า-หลังขัดกันและเปิด requirePlatesAgree ห้ามใช้ทางลัดนี้)
            if (havePlate && allowPlateTagMismatch && !blockedByDisagree)
            {
                lock (hybridLock)
                {
                    gateBusy = true; sawMismatch = false;
                    pendingRfidTag = ""; pendingPlateCam[1] = ""; pendingPlateCam[2] = "";
                }
                GrantAccess(owner, dbPlateShow, dbPerm, "อนุญาตด้วย RFID (ป้ายไม่ตรง อนุญาตตามนโยบาย)");
                return;
            }

            if (!havePlate) return;   // ยังไม่มีป้าย → รอ (timer จัดการเคสไม่มีป้าย) อย่าตั้ง sawMismatch

            // มีป้ายแต่ไม่ผ่านเงื่อนไข → วนอ่านซ้ำจนครบเวลา แล้วปฏิเสธ
            bool keepTrying;
            lock (hybridLock)
            {
                keepTrying = (DateTime.Now - pendingRfidTime).TotalSeconds < retryMaxSec;
                if (keepTrying) { pendingPlateCam[1] = ""; pendingPlateCam[2] = ""; sawMismatch = true; }
                else { gateBusy = true; pendingRfidTag = ""; sawMismatch = false; }
            }
            string detail = blockedByDisagree
                ? $"ป้ายหน้า-หลังไม่ตรงกัน ({p1} / {p2}) — กำลังอ่านซ้ำ..."
                : $"ป้ายที่อ่านได้ยังไม่ตรงบัตร ({dbPlate}) — กำลังอ่านซ้ำ...";
            if (keepTrying)
                SetAccessUi("🔄 กำลังตรวจสอบใหม่...", Color.DarkOrange, Color.Red, dbPlate, "-", detail);
            else
                DenyAccess(blockedByDisagree
                    ? "⛔ ป้ายหน้า-หลังไม่ตรงกัน (ตรวจสอบซ้ำแล้ว)"
                    : "⛔ ป้ายทะเบียนไม่ตรงกับบัตร (ตรวจสอบซ้ำแล้ว)");
        }

        // ---- โหมด LPR อย่างเดียว (ไม่มีบัตร): ป้ายตรงฐานข้อมูล = ผ่าน ----
        private void DecideLprOnly(string p1, string p2)
        {
            logMode = "LPR"; logTag = ""; logPlate1 = p1; logPlate2 = p2;
            logPlateDb = ""; logProvince = ""; logOwner = ""; logPermission = "";
            // สวิตช์ 4: อ่านได้ทั้ง 2 กล้องแต่เลขคนละอัน → ปฏิเสธ (กันปลอมป้าย) ในโหมด LPR ล้วนด้วย
            bool bothRead = p1 != "" && p2 != "";
            if (requirePlatesAgree && bothRead && NormPlate(p1) != NormPlate(p2))
            {
                lock (hybridLock) { gateBusy = true; pendingPlateCam[1] = ""; pendingPlateCam[2] = ""; }
                DenyAccess($"⛔ ป้ายหน้า-หลังไม่ตรงกัน ({p1} / {p2})");
                return;
            }

            foreach (var item in new[] { (plate: p1, cam: "หน้า"), (plate: p2, cam: "หลัง") })
            {
                if (string.IsNullOrEmpty(item.plate)) continue;
                DataTable dt = db.GetUserByPlate(NormPlate(item.plate));
                if (dt.Rows.Count > 0)
                {
                    var row = dt.Rows[0];
                    string owner = row["owner_name"]?.ToString() ?? "";
                    string perm = row.Table.Columns.Contains("permission") ? row["permission"]?.ToString() ?? "" : "";
                    string dbPlate = row["plate_number"]?.ToString() ?? "";
                    string dbProv = row.Table.Columns.Contains("province") ? row["province"]?.ToString() ?? "" : "";
                    logPlateDb = row["plate_number"]?.ToString() ?? ""; logProvince = dbProv;
                    logOwner = owner; logPermission = perm;
                    if (dbProv != "") dbPlate = dbPlate + " " + dbProv;
                    lock (hybridLock) { gateBusy = true; pendingPlateCam[1] = ""; pendingPlateCam[2] = ""; }
                    GrantAccess(owner, dbPlate, perm, $"✔ ผ่านด้วยป้ายทะเบียน (กล้อง{item.cam}) — โหมดไม่ใช้ RFID");
                    return;
                }
            }
            // ไม่มีป้ายที่ลงทะเบียนในระบบ → ปฏิเสธ (มีป้ายให้เทียบแล้ว แต่ไม่พบข้อมูล)
            lock (hybridLock) { gateBusy = true; pendingPlateCam[1] = ""; pendingPlateCam[2] = ""; }
            DenyAccess("⛔ ปฏิเสธ — ไม่พบข้อมูลในระบบ");
        }

        // ปุ่ม "ประวัติการเข้า-ออก" ในโซนอนุญาต (สร้างด้วยโค้ด ไม่ต้องเพิ่มใน Designer)
        private void InitHistoryButton()
        {
            var btn = new Button
            {
                Text = "📋  ประวัติการเข้า-ออก",
                Size = new System.Drawing.Size(220, 34),
                Location = new System.Drawing.Point(groupBox4.Width - 240, groupBox4.Height - 45),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(235, 243, 255),
                Font = new Font("Tahoma", 9.5f, FontStyle.Bold)
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(120, 160, 210);
            btn.Click += (s, e) =>
            {
                using (var f = new LogViewerForm()) f.ShowDialog(this);
            };
            groupBox4.Controls.Add(btn);
            btn.BringToFront();
        }

        // เซฟภาพมุมกว้าง + ภาพซูมป้าย ของกล้องที่ระบุ คืน path ทั้งสอง
        private (string wide, string plate) SaveCamImages(int camId, string dir, string stamp)
        {
            string wide = "", plateImg = "";
            Bitmap snap = null;
            lock (frameLock)
            {
                if (lastFrame[camId] != null) snap = (Bitmap)lastFrame[camId].Clone();
            }
            if (snap == null) return (wide, plateImg);

            try
            {
                wide = System.IO.Path.Combine(dir, $"{stamp}_cam{camId}_wide.jpg");
                if (jpegCodec != null) snap.Save(wide, jpegCodec, jpegHiQ);
                else snap.Save(wide, System.Drawing.Imaging.ImageFormat.Jpeg);

                Rectangle box; bool has;
                lock (boxLock) { has = hasPlateBox[camId]; box = latestPlateBox[camId]; }

                if (has && box.Width > 4 && box.Height > 4)
                {
                    Rectangle r = Rectangle.Inflate(box, 10, 10);          // เผื่อขอบป้ายนิดหน่อย
                    r.Intersect(new Rectangle(0, 0, snap.Width, snap.Height));
                    if (r.Width > 4 && r.Height > 4)
                    {
                        using (Bitmap crop = snap.Clone(r, snap.PixelFormat))
                        {
                            plateImg = System.IO.Path.Combine(dir, $"{stamp}_cam{camId}_plate.jpg");
                            if (jpegCodec != null) crop.Save(plateImg, jpegCodec, jpegHiQ);
                            else crop.Save(plateImg, System.Drawing.Imaging.ImageFormat.Jpeg);
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("เซฟภาพประวัติไม่ได้: " + ex.Message); }
            finally { snap.Dispose(); }

            return (wide, plateImg);
        }

        // เขียนประวัติ 1 รายการ (saveImages = true เฉพาะตอนอนุญาตให้ผ่าน)
        private void WriteAccessLog(string result, string reason, bool saveImages)
        {
            // ก๊อปข้อมูลออกมาก่อน กันถูกทับตอนทำงานเบื้องหลัง
            DateTime now = DateTime.Now;
            string mode = logMode, tag = logTag, c1 = logPlate1, c2 = logPlate2,
                   pdb = logPlateDb, prov = logProvince, own = logOwner, perm = logPermission;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string w1 = "", w2 = "", i1 = "", i2 = "";
                    if (saveImages)
                    {
                        string dir = db.GetLogImageDir(now);
                        string stamp = now.ToString("HHmmss");
                        var a = SaveCamImages(1, dir, stamp);
                        var b = SaveCamImages(2, dir, stamp);
                        w1 = a.wide; i1 = a.plate;
                        w2 = b.wide; i2 = b.plate;
                    }
                    db.SaveAccessLog(now, result, reason, mode, tag, c1, c2,
                                     pdb, prov, own, perm, w1, w2, i1, i2);
                }
                catch (Exception ex) { Console.WriteLine("บันทึกประวัติไม่ได้: " + ex.Message); }
            });
        }

        private void GrantAccess(string owner, string plate, string permission,
                                 string detail = "ยืนยัน 2 ชั้นผ่าน (RFID + ป้ายทะเบียน)")
        {
            lock (hybridLock) sawMismatch = false;      // ⬅️ เพิ่ม
            string who = owner + (permission != "" ? $" ({permission})" : "");
            SetAccessUi("✅ อนุญาตให้เข้า", Color.Green, Color.LimeGreen, plate, who, detail);
            WriteAccessLog("ALLOWED", detail, true);       // ผ่าน → เก็บภาพด้วย
            this.BeginInvoke(new Action(() => { timerGate.Interval = 3000; timerGate.Start(); }));
        }

        private void DenyAccess(string reason)
        {
            SetAccessUi("⛔ ไม่อนุญาตให้เข้า", Color.Red, Color.Red, "-", "-", reason);
            WriteAccessLog("DENIED", reason, false);       // ปฏิเสธ → บันทึกอย่างเดียว ไม่เก็บภาพ
            this.BeginInvoke(new Action(() => { timerGate.Interval = 4000; timerGate.Start(); }));
        }

        private void GrantAccessNoPlate(string tag)
        {
            DataTable dt = db.GetUserByTag(tag);
            if (dt.Rows.Count == 0)
            {
                DenyAccess($"ไม่พบบัตร {tag} ในระบบ");
                return;
            }

            var row = dt.Rows[0];
            string owner = row["owner_name"]?.ToString() ?? "";
            string perm = row.Table.Columns.Contains("permission") ? row["permission"]?.ToString() ?? "" : "";
            string dbPlate = row["plate_number"]?.ToString() ?? "";
            string dbProv = row.Table.Columns.Contains("province") ? row["province"]?.ToString() ?? "" : "";
            if (dbProv != "") dbPlate = dbPlate + " " + dbProv;
            string who = owner + (perm != "" ? $" ({perm})" : "");

            logMode = "RFID"; logTag = tag; logPlate1 = ""; logPlate2 = "";
            logPlateDb = dbPlate; logProvince = dbProv; logOwner = owner; logPermission = perm;
            if (dbProv != "") dbPlate = dbPlate + " " + dbProv;

            SetAccessUi("✅ อนุญาตให้เข้า", Color.Green, Color.LimeGreen,
                        dbPlate, who, "⚠️ ตรวจพบแท็ก RFID แต่ตรวจจับไม่พบป้ายทะเบียน");
            WriteAccessLog("ALLOWED", "⚠️ ตรวจพบแท็ก RFID แต่ตรวจจับไม่พบป้ายทะเบียน", true);
            this.BeginInvoke(new Action(() => { timerGate.Interval = 3000; timerGate.Start(); }));
        }

        private void TimerHybridTimeout_Tick(object sender, EventArgs e)
        {
            string tagOnlyGrant = null;
            string tagMismatchDeny = null;
            bool plateNoTagDeny = false;
            bool noPlateDeny = false;
            string noTagP1 = "", noTagP2 = "";   // ทะเบียนที่อ่านได้ตอนไม่มีแท็ก (ไว้บันทึกประวัติ)

            lock (hybridLock)
            {
                if (gateBusy) return;

                bool haveRfid = pendingRfidTag != "";
                bool havePlate = pendingPlateCam[1] != "" || pendingPlateCam[2] != "";

                // เคสA: มีบัตร ไม่มีป้าย ไม่เคยเจอป้ายผิด + ครบ 10 วิ → อนุญาต (รถไม่ติดป้าย)
                if (haveRfid && !havePlate && !sawMismatch && allowNoPlate &&
                    (DateTime.Now - pendingRfidTime).TotalSeconds >= noPlateGraceSec)
                {
                    tagOnlyGrant = pendingRfidTag;
                    gateBusy = true;
                    pendingRfidTag = "";
                }
                // เคสA2: มีบัตร ไม่มีป้าย + สวิตช์ 2 ปิด + ครบ 15 วิ → ปฏิเสธ
                else if (haveRfid && !havePlate && !sawMismatch && !allowNoPlate &&
                         (DateTime.Now - pendingRfidTime).TotalSeconds >= noPlateDenySec)
                {
                    noPlateDeny = true;
                    gateBusy = true;
                    pendingRfidTag = "";
                }
                // เคสB: มีบัตร เคยเจอป้ายผิด วนอ่านซ้ำครบ 20 วิ → ปฏิเสธจริง
                else if (haveRfid && sawMismatch &&
                         (DateTime.Now - pendingRfidTime).TotalSeconds >= retryMaxSec)
                {
                    tagMismatchDeny = pendingRfidTag;
                    gateBusy = true;
                    pendingRfidTag = "";
                    sawMismatch = false;
                }
                // เคสC: มีป้าย ไม่มีบัตร
                else if (havePlate && !haveRfid)
                {
                    // โหมด RFID: เจอป้ายแต่ไม่มีแท็กครบ plateOnlyDenySec วิ → ปฏิเสธ (ให้ระบบตอบสนอง)
                    if (requireRfid && plateSeenNoTagAt != DateTime.MinValue &&
                        (DateTime.Now - plateSeenNoTagAt).TotalSeconds >= plateOnlyDenySec)
                    {
                        plateNoTagDeny = true;
                        gateBusy = true;
                        noTagP1 = pendingPlateCam[1]; noTagP2 = pendingPlateCam[2];
                        pendingPlateCam[1] = ""; pendingPlateCam[2] = "";
                        plateSeenNoTagAt = DateTime.MinValue;
                    }
                    else
                    {
                        // ล้างป้ายที่หมดอายุ + ถ้าป้ายหายหมดให้รีเซ็ตตัวนับ (กันค้างไปคันถัดไป)
                        if ((DateTime.Now - pendingPlateCamTime[1]).TotalSeconds > hybridWindowSec) pendingPlateCam[1] = "";
                        if ((DateTime.Now - pendingPlateCamTime[2]).TotalSeconds > hybridWindowSec) pendingPlateCam[2] = "";
                        if (pendingPlateCam[1] == "" && pendingPlateCam[2] == "") plateSeenNoTagAt = DateTime.MinValue;
                    }
                }
            }

            if (tagOnlyGrant != null) GrantAccessNoPlate(tagOnlyGrant);
            else if (tagMismatchDeny != null)
                DenyAccess("⛔ ป้ายทะเบียนไม่ตรงกับบัตร (ตรวจสอบซ้ำแล้ว)");
            else if (plateNoTagDeny)
            {
                // เคสนี้ไม่มีแท็ก → ล้าง context เก่า กันบันทึกแท็กของคันก่อนหน้าผิด ๆ
                logMode = "RFID"; logTag = ""; logPlate1 = noTagP1; logPlate2 = noTagP2;
                logPlateDb = ""; logProvince = ""; logOwner = ""; logPermission = "";
                DenyAccess("⛔ ตรวจพบป้ายทะเบียน แต่ไม่พบแท็ก RFID");
            }
            else if (noPlateDeny)                                              // ⬅️ เพิ่ม
                DenyAccess("⛔ ไม่พบป้ายทะเบียน ");
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

                {
                    var client = httpPredict;

                    using (var ms = new MemoryStream())
                    {
                        if (jpegCodec != null) bitmap.Save(ms, jpegCodec, jpegHiQ);
                        else bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                        var content = new MultipartFormDataContent();
                        SetLprStatus(camId, "⏳ กำลังประมวลผล...", Color.Blue);
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
                                SetPlateText(camId, fullText);
                                SetLprStatus(camId, $"✅ อ่านสำเร็จ (กล้อง{(camId == 1 ? "หน้า" : "หลัง")})", Color.Green);
                                string provinceRead = result.province != null ? (string)result.province : "";
                                OnPlateRead((string)result.text, provinceRead, camId);
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

                {
                    var client = httpDetect;
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
                                lock (hybridLock) plateSeen[camId] = true;
                                UpdateLprZone(camId);
                                // 🎯 เจอป้ายในเฟรม = จังหวะดีที่สุดที่จะอ่าน → สั่งอ่านเลย (แทน motion trigger)
                                if (!isAIProcessing &&
                                    (DateTime.Now - lastCaptureTimes[camId]).TotalSeconds >= cooldownSeconds)
                                {
                                    lastCaptureTimes[camId] = DateTime.Now;
                                    Bitmap readFrame = new Bitmap(bitmap);
                                    Task.Run(() => SendToAI(readFrame, camId));
                                }
                            }
                            else
                            {
                                hasPlateBox[camId] = false;
                                lock (hybridLock) plateSeen[camId] = false;    // ⬅️ เพิ่ม
                                UpdateLprZone(camId);
                            }

                        }
                    }
                }
            }
            catch { }
            finally { isDetecting[camId] = false; bitmap.Dispose(); }
        }

        private void UpdateLprZone(int camId)
        {
            bool seen;
            lock (hybridLock) seen = plateSeen[camId];
            if (seen) SetLprStatus(camId, "🟥 พบป้ายทะเบียน", Color.DarkOrange);
            else SetLprStatus(camId, "รอตรวจจับ...", Color.Gray);
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

        private void ShowCameraPlaceholder(PictureBox box, string msg = "CAMERA NOT FOUND")
        {
            if (box.InvokeRequired) { box.BeginInvoke(new Action(() => ShowCameraPlaceholder(box, msg))); return; }   // ⬅️ เพิ่ม
            Bitmap bmp = new Bitmap(Math.Max(box.Width, 320), Math.Max(box.Height, 240));
            using (Graphics g = Graphics.FromImage(bmp))
            using (var font = new System.Drawing.Font("Segoe UI", 16, FontStyle.Bold))
            {
                g.Clear(Color.FromArgb(74, 74, 74));   // เทาเข้ม
                SizeF sz = g.MeasureString(msg, font);
                g.DrawString(msg, font, Brushes.LightGray, (bmp.Width - sz.Width) / 2, (bmp.Height - sz.Height) / 2);
            }
            if (box.Image != null) box.Image.Dispose();
            box.Image = bmp;
        }

        private void SetLprStatus(int camId, string msg, Color c)
        {
            var lbl = StatusLabel(camId);
            if (lbl.InvokeRequired) lbl.BeginInvoke(new Action(() => { lbl.Text = msg; lbl.ForeColor = c; }));
            else { lbl.Text = msg; lbl.ForeColor = c; }
        }

        private void SetPlateText(int camId, string text)
        {
            var lbl = PlateLabel(camId);
            Action apply = () => { lbl.Text = text; lbl.ForeColor = Color.Green; };
            if (lbl.InvokeRequired) lbl.BeginInvoke(apply);
            else apply();
        }

        private void SetAccessUi(string result, Color resultColor, Color gateColor,
                                 string plate, string name, string detail)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => SetAccessUi(result, resultColor, gateColor, plate, name, detail)));
                return;
            }
            lblResult.Text = result;
            lblResult.ForeColor = resultColor;
            picGate.BackColor = gateColor;
            lblShowPlate.Text = "ทะเบียน: " + plate;
            lblShowName.Text = "ประเภทสิทธิ์: " + name;
            lblStatus.Text = detail;
            lblStatus.ForeColor = resultColor;
        }
        private void txtSimulateRFID_TextChanged(object sender, EventArgs e)
        {

        }

        private void txtRFIDInput2_TextChanged(object sender, EventArgs e)
        {

        }

        private void pbCamera1_Click(object sender, EventArgs e)
        {

        }

        private void groupBox3_Enter(object sender, EventArgs e)
        {

        }

        private void btnDisconnectRFID_Load(object sender, EventArgs e)
        {

        }

        private void groupBox4_Enter(object sender, EventArgs e)
        {

        }
    }
}