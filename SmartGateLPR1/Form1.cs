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
        private IBarrier barrier;
        private System.Windows.Forms.Label? lblBarrierStatus;             // ป้ายบอกสถานะไม้กั้นบนหน้าหลัก
        private readonly ToolTip barrierTip = new ToolTip();
        private int gateOpenSec = 3;                                      // เปิดไม้กั้นค้างกี่วินาที (ตั้งได้ในหน้าตั้งค่า)

        // ตัวแปร Global สำหรับรองรับกล้อง 2 ตัว (แยกตาม ID กล้อง)
        private Rectangle triggerZone = new Rectangle(150, 200, 400, 200);
        private double triggerThreshold = 20.0;
        // เว้นระยะระหว่างการยิงอ่านเลขของกล้องเดียวกัน (วินาที)
        //
        // เดิม 1 วินาทีเต็ม ซึ่งเป็นตัวถ่วงหลักของเวลา "กว่าจะส่งค่าให้ตัดสิน":
        // ต้องอ่านให้ได้เลขเดิมซ้ำ readsToConfirm (2) ครั้ง = เสียเวลารออย่างน้อย
        // 1 วินาทีเปล่า ๆ คั่นกลาง ทั้งที่ /predict เองใช้เวลาแค่ ~0.3-0.5 วิ
        // และมี isAIProcessing กันไม่ให้ยิงซ้อนอยู่แล้ว จึงไม่ต้องเว้นนานขนาดนั้น
        private double cooldownSeconds = 0.25;

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
        // ===== สลับกันอ่านทีละกล้อง + ค้างผลที่อ่านได้แล้ว =====
        private readonly object turnLock = new object();
        private int lprOwner = 0;                       // กล้องที่กำลังถือสิทธิ์อ่าน (0 = ว่าง)
        private DateTime lprOwnerSince = DateTime.MinValue;
        private int lprOwnerMaxSec = 4;                // ถือนานเกินนี้ให้อีกตัวแย่งได้ กันค้าง
        private bool[] plateLocked = new bool[3];       // อ่านเลขได้แล้ว ค้างไว้ ไม่อ่านซ้ำ
        // เวลาที่ล็อกเลขของแต่ละกล้อง — ใช้คู่กับ relockRecheckSec ด้านล่าง
        private DateTime[] plateLockedAt = new DateTime[] { DateTime.MinValue, DateTime.MinValue, DateTime.MinValue };
        // ส่งผลเข้าศูนย์ตัดสินใจไปแล้วหรือยัง (กล้องละครั้งเดียวต่อรถหนึ่งคัน)
        //
        // เดิมกล้องจะอ่านแล้วส่งซ้ำเรื่อย ๆ ทุก 2 วินาที ทำให้
        //   - เปลืองรอบ OCR ทั้งที่ได้คำตอบที่ชัวร์แล้ว
        //   - กล้องที่ยืนยันเสร็จก่อนสั่งตัดสินทันที อีกกล้องยังอ่านไม่ทัน
        //     ผลที่โชว์จึงเป็น "ผ่านโดยกล้องหน้า" เสมอ ไม่เคยเป็น
        //     "ทะเบียนหน้า-หลังตรงกัน" ทั้งที่กล้องทั้งสองอ่านได้เลขเดียวกัน
        // ตอนนี้: มั่นใจแล้วส่งครั้งเดียว ขึ้นสถานะ "ตรวจสอบสำเร็จ" แล้วหยุด
        // จนกว่าผลตัดสินจะออก (ResetLprTurn) หรือรถออกจากเฟรม (ResetLprTurnCam)
        private bool[] plateSubmitted = new bool[3];
        private DateTime[] plateSubmittedAt = new DateTime[] { DateTime.MinValue, DateTime.MinValue, DateTime.MinValue };
        // ความมั่นใจสูงสุดของเลขที่กล้องนั้นยืนยัน (ใช้เลือกฝั่งที่น่าเชื่อกว่าตอนหน้า-หลังไม่ตรงกัน)
        private double[] bestConf = new double[3];
        // กันค้าง: ส่งไปแล้วแต่ผลตัดสินไม่ออกสักทีภายในกี่วินาที ให้กลับไปอ่านใหม่
        // ต้องมากกว่า otherCamMaxWaitSec เสมอ ไม่งั้นกล้องที่ส่งไปแล้วจะปลดล็อก
        // กลับไปอ่านใหม่ทั้งที่ศูนย์ตัดสินใจยังรออีกกล้องอยู่
        private double submitHoldMaxSec = 12.0;
        // เพดานการรออีกกล้อง (วินาที)
        //
        // ไม่ได้รอตายตัวตามเวลา แต่รอตาม "อีกกล้องกำลังทำอะไรอยู่":
        //   เห็นป้ายอยู่ / กำลังอ่านเลขอยู่  → รอต่อไปเรื่อย ๆ จนกว่าจะเสร็จ
        //   ตรวจไม่เจอป้ายเลย                → ตัดสินทันที ไม่ต้องรอ
        // เพดานนี้เป็นแค่ตัวกันค้าง เผื่อกล้องเห็นป้ายแต่อ่านไม่ออกสักที
        private double otherCamMaxWaitSec = 10.0;
        // ล็อกแล้วอ่านซ้ำเพื่อ "ตรวจทาน" ทุกกี่วินาที
        //
        // เดิมล็อกแล้วคือหยุดอ่านถาวร จนกว่าจะครบรอบเปิด-ปิดไม้กั้น (ResetLprTurn)
        // ซึ่งพังตอนป้ายในเฟรมเปลี่ยนเป็นคันใหม่โดยไม่มีรอบตัดสินคั่น เช่น เอาป้าย
        // ใบที่ 2 มาเปลี่ยนแทนใบที่ 1 ตรงหน้ากล้อง — กล้องไม่เคย "มองไม่เห็นป้าย"
        // เลยสักครั้ง ตัวนับพลาดจึงไม่ครบ ล็อกไม่ถูกปลด แล้วหน้าจอก็ค้างเลขใบแรก
        // ไปตลอด  ตอนนี้ล็อกแค่ "พักการอ่าน" ชั่วคราว ครบเวลาแล้วอ่านทวนอีกครั้ง
        // ถ้าเลขยังเดิมก็พักต่อ ถ้าเลขเปลี่ยน = คนละคัน เริ่มนับยืนยันใหม่ทันที
        private double relockRecheckSec = 2.0;
        // ป้ายหายจากเฟรมนานเกินกี่วินาที ถือว่ารถคันนั้นไปแล้ว ล้างผลที่ค้างไว้
        private double plateGoneResetSec = 1.0;
        private string[] lastReadPlate = new string[] { "", "", "" };
        private int[] confirmCount = new int[3];
        // ต้องอ่านได้เลขเดิมซ้ำกี่ครั้งถึงจะค้าง
        // ลดจาก 3 เหลือ 2: การยืนยันซ้ำแต่ละรอบต้องรอ /predict อีกหนึ่งรอบ (~0.3-0.5 วิ)
        // รอบที่ 3 แทบไม่เคยเปลี่ยนคำตอบจากรอบที่ 2 แต่ถ่วงเวลาตัดสินใจทุกคัน
        // ตั้ง 1 ได้ถ้าอยากตัดการยืนยันซ้ำทิ้งเลย (เร็วสุด แต่พลาดง่ายขึ้น)
        private int readsToConfirm = 2;

        // กันแท็กเดิมวนกระตุ้นซ้ำหลังตัดสินไปแล้ว
        private string lastDecidedTag = "";
        private DateTime lastDecidedAt = DateTime.MinValue;
        private int sameTagCooldownSec = 6;
        private DateTime[] lastDetectTimes = new DateTime[] { DateTime.MinValue, DateTime.MinValue, DateTime.MinValue };
        private bool[] isDetecting = new bool[3];
        private readonly object boxLock = new object();
        // ยิง /detect ถี่แค่ไหน (ต่อกล้อง)
        //
        // เดิม 66ms (15 ครั้ง/วินาที/กล้อง = 30 ครั้ง/วินาทีรวม 2 กล้อง) ซึ่งเกินกำลัง
        // เครื่องมาก เพราะตอนนั้น /detect ใช้ความละเอียดเต็ม 1280 = ~0.19 วินาที/ครั้ง
        // → ต้องการเวลาประมวลผล ~6 วินาที ต่อเวลาจริง 1 วินาที งานตรวจจับจึงยึดโมเดล
        // ไว้ตลอดจนงานอ่านตัวอักษรไม่ได้รันเลย
        //
        // ค่านี้ใช้ตอน "ยังค้นหาป้าย" (ไม่มีรถ) ซึ่งฝั่ง AI ต้องค้นทั้งเฟรมด้วย
        // ความละเอียดเต็ม ~185ms/ครั้ง จึงไม่ควรยิงถี่เกินไป ไม่งั้นเผาซีพียูทิ้ง
        // ตอนไม่มีอะไรเข้ามา  ตอนเกาะติดป้ายแล้วจะใช้ trackIntervalMs แทน
        private int detectIntervalMs = 150;
        // ตอนเกาะติดป้ายอยู่แล้วให้ยิงถี่กว่าปกติ เพราะฝั่ง AI ค้นเฉพาะรอบกรอบเดิม
        // (ROI) ซึ่งใช้เวลาแค่ ~20ms เทียบกับค้นทั้งเฟรม ~185ms จึงยิงถี่ได้สบาย ๆ
        // กรอบจะเกาะตามป้ายลื่นขึ้นมาก  ส่วนตอนยังค้นหา (ไม่มีรถ) ใช้ค่าปกติ
        // จะได้ไม่เผาซีพียูทิ้งตอนไม่มีอะไรเข้ามา
        private int trackIntervalMs = 40;
        // ตัวนับว่าตรวจไม่เจอป้ายติดกันกี่ครั้งแล้ว (แยกตามกล้อง)
        private int[] missCount = new int[3];
        // ต้องพลาดติดกันกี่ครั้งถึงจะยอมดับกรอบ — กันกรอบกระพริบเวลา YOLO พลาด
        // เฟรมสองเฟรมเพราะภาพเบลอ/มุมเอียง ที่ 40ms ต่อครั้ง 8 ครั้ง = ~0.3 วินาที
        private int missToLose = 8;
        // กรอบค้างบนจอได้นานแค่ไหนหลังผลตรวจจับล่าสุด — ต้องยาวกว่าช่วงที่ /detect
        // หยุดหลบให้ /predict (อ่านป้ายใช้เวลา ~2 วินาที) ไม่งั้นกรอบจะหายวับ
        // ระหว่างกำลังอ่านป้าย แล้วโผล่กลับมาใหม่ ดูเหมือนกระพริบ
        private int boxHoldMs = 4000;
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
        private double[] pendingPlateConf = new double[3];                // ความมั่นใจของป้ายนั้น
        private DateTime[] pendingPlateCamTime = new DateTime[] { DateTime.MinValue, DateTime.MinValue, DateTime.MinValue };
        private readonly object hybridLock = new object();
        private bool gateBusy = false;                 // กันตัดสินซ้ำระหว่างไม้เปิดค้าง
        private System.Windows.Forms.Timer timerHybridTimeout;

        // ===== เวลาทุกเงื่อนไข (ปรับให้กระชับ รถจะได้ไม่ต้องจอดรอนาน) =====
        private int hybridWindowSec = 10;              // สองฝั่งต้องมาห่างกันไม่เกินกี่วินาที
        private int noPlateGraceSec = 7;     // มีบัตรแต่ไม่เจอป้าย รอกี่วิ แล้วปล่อยผ่าน
        private int noPlateDenySec = 9;      // มีบัตรแต่ไม่เจอป้าย รอกี่วิ แล้วปฏิเสธ (สวิตช์ 2 ปิด)
        private bool requireRfid = true;
        private bool allowNoPlate = true;
        private bool requirePlatesAgree = false;
        private bool allowPlateTagMismatch = false;
        private DateTime plateSeenNoTagAt = DateTime.MinValue;  // เวลาที่เริ่มเห็นป้ายทั้งที่ยังไม่มีแท็ก (โหมด RFID)
        private int plateOnlyDenySec = 2;                       // เจอป้ายแต่ไม่มีแท็กกี่วิ → ปฏิเสธ
        private bool[] plateSeen = new bool[3];   // index 1,2 = กล้องหน้า/หลังเจอป้ายไหม
        private DateTime lastPlateSeenAt = DateTime.MinValue;  // เวลาที่กล้องใดกล้องหนึ่งเห็นป้ายล่าสุด

        // ตัดช่องว่างก่อนเทียบ (ฐานข้อมูลเก็บ "กท 2058" แต่ LPR อ่านได้ "กท2058")
        private static string NormPlate(string s) =>
            (s ?? "").Replace(" ", "").Replace("-", "").Trim();

        private Label PlateLabel(int camId) => camId == 1 ? lblLicensePlate1 : lblLicensePlate2;
        private Label StatusLabel(int camId) => camId == 1 ? lblLprStatus1 : lblLprStatus2;
        // มีบัตรแล้วแต่ป้ายยังไม่ตรง จะวนอ่านซ้ำได้กี่รอบ / นานสุดกี่วิ ก่อนยอมแพ้
        //
        // เดิมให้เวลา 20 วินาที ซึ่งนานเกินไป — ถ้าอ่าน 2 รอบแล้วยังไม่ตรง
        // รอบที่ 3-4 ก็มักได้ผลเดิม คนขับได้แต่จอดรอเปล่า ๆ
        // ตอนนี้จำกัดทั้ง "จำนวนรอบ" และ "เวลา" อันไหนถึงก่อนถือว่าหมดสิทธิ์
        private int retryMaxRounds = 2;  // อ่านซ้ำได้ 2 รอบ (ตั้ง 0 = ไม่อ่านซ้ำเลย)
        private int retryMaxSec = 6;     // และต้องไม่เกินกี่วิ นับจากตอนแตะบัตร
        private int retryCount = 0;      // นับรอบที่อ่านซ้ำไปแล้วของบัตรใบปัจจุบัน
        private bool sawMismatch = false;


        private Process aiProcess = null;

        private void StartAiService()
        {
            try
            {
                // ถ้ามีบริการ AI เปิดอยู่แล้ว (ตอน dev เปิดเองด้วย python) ไม่ต้องเปิดซ้ำ
                using (var probe = new System.Net.Sockets.TcpClient())
                {
                    try { probe.Connect("127.0.0.1", 5000); return; } catch { }
                }

                string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai", "lpr_api.exe");
                if (!File.Exists(exe)) return;   // โหมด dev: ไม่มีไฟล์นี้ ข้ามไป

                aiProcess = new Process();
                aiProcess.StartInfo.FileName = exe;
                aiProcess.StartInfo.WorkingDirectory = Path.GetDirectoryName(exe);
                aiProcess.StartInfo.CreateNoWindow = true;
                aiProcess.StartInfo.UseShellExecute = false;
                aiProcess.Start();
            }
            catch (Exception ex) { Console.WriteLine("เปิดบริการ AI ไม่ได้: " + ex.Message); }
        }

        private void StopAiService()
        {
            try
            {
                if (aiProcess != null && !aiProcess.HasExited) aiProcess.Kill(true);
            }
            catch { }
        }

        public btnDisconnectRFID()
        {
            InitializeComponent();
            StartAiService();
            try { db = new DatabaseHelper(); }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
            InitSideMenu();
            LoadSavedSettings();
            LoadAccessPolicy();
            InitBarrierStatus();
            ReloadBarrier();            // สร้างตัวควบคุมไม้กั้น + อัปเดตป้ายสถานะ
            InitHistoryButton();
            if (!string.IsNullOrEmpty(DatabaseHelper.LastSchemaError))
            {
                this.BeginInvoke(new Action(() =>
                    MessageBox.Show("ใช้ฐานข้อมูลที่ตั้งไว้ไม่ได้:\n\n" + DatabaseHelper.LastSchemaError +
                                    "\n\nเปิดเมนู ☰ → ตั้งค่าฐานข้อมูล เพื่อแก้ไข",
                                    "ฐานข้อมูล", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
            }
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
            panelMenu.Controls.Add(MakeMenuButton("🚦  ตั้งค่าไม้กั้น", 350, (s, e) =>
            {
                ToggleMenu();
                using (var f = new BarrierSettingsForm(this)) f.ShowDialog(this);
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

                        // --- 1.5 อัปเดตกรอบแดงให้ตามป้าย: เรียก /detect เป็นระยะ ---
                        // กำลังเกาะติดป้ายอยู่ → ยิงถี่กว่าปกติได้ เพราะฝั่ง AI ค้นเฉพาะ
                        // รอบกรอบเดิม (ROI) ซึ่งเร็วกว่าค้นทั้งเฟรมหลายเท่า กรอบจะตามลื่น
                        bool nowTracking;
                        lock (boxLock) nowTracking = hasPlateBox[camId];
                        int myInterval = nowTracking ? trackIntervalMs : detectIntervalMs;
                        lock (turnLock)
                        {
                            // อีกกล้องกำลังอ่านเลขอยู่ → กล้องนี้ลดความถี่ลง คืน GPU ให้ตัวที่กำลังทำงาน
                            if (lprOwner != 0 && lprOwner != camId) myInterval *= 3;
                        }
                        if (!isDetecting[camId] &&
                            (DateTime.Now - lastDetectTimes[camId]).TotalMilliseconds >= myInterval)
                        {
                            lastDetectTimes[camId] = DateTime.Now;
                            Bitmap detectFrame = new Bitmap(image);
                            Task.Run(() => DetectBox(detectFrame, camId));
                        }


                        // --- 2. ระบบ Auto-Trigger เช็คป้ายทะเบียน ---
                        // เงื่อนไขใหม่: ยิงอ่านตราบใดที่ "เห็นกรอบป้ายอยู่" ไม่ใช่แค่ตอนมีความเคลื่อนไหว
                        // (แก้ปัญหา: รถหยุดนิ่งสนิทแล้วภาพไม่เปลี่ยน ระบบเดิมจะไม่ยิงอ่านซ้ำอีกเลย)
                        bool seeingPlate;
                        lock (boxLock) seeingPlate = hasPlateBox[camId];

                        if (seeingPlate &&
                            (DateTime.Now - lastCaptureTimes[camId]).TotalSeconds >= cooldownSeconds)
                        {
                            lastCaptureTimes[camId] = DateTime.Now;

                            // ส่งรูปเต็มไปให้ AI อ่าน (ภาพใหม่จากเฟรมปัจจุบันเสมอ)
                            Bitmap frameToSend = new Bitmap(image);
                            Task.Run(() => SendToAI(frameToSend, camId));
                        }

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

            try { barrier?.Dispose(); } catch { }

            StopAiService();
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
            try { barrier?.Close(); } catch { }
            gateBusy = false;                              // พร้อมรับคันถัดไป
            lock (hybridLock) { sawMismatch = false; retryCount = 0; plateSeenNoTagAt = DateTime.MinValue; }
            // สำคัญ: ปลดล็อกป้ายที่ค้างไว้ของคันก่อนหน้า ไม่งั้นกล้องจะไม่อ่านป้ายให้คันถัดไปอีกเลย
            lastDecidedTag = logTag;
            lastDecidedAt = DateTime.Now;
            ResetLprTurn();

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
            string tag = tagId.Trim();

            lock (hybridLock)
            {
                // แท็กเดิมที่ค้างอยู่ → ไม่รีเซ็ตเวลา ไม่งั้นตัวจับเวลาไม่มีวันครบ
                if (tag == pendingRfidTag) return;

                // แท็กเดิมที่เพิ่งตัดสินไป (รถอาจยังไม่ทันขยับออกจากสนามอ่าน) → ยังไม่รับซ้ำ
                if (tag == lastDecidedTag &&
                    (DateTime.Now - lastDecidedAt).TotalSeconds < sameTagCooldownSec) return;

                pendingRfidTag = tag;
                pendingRfidTime = DateTime.Now;
                plateSeenNoTagAt = DateTime.MinValue;
                retryCount = 0;              // บัตรใบใหม่ เริ่มนับรอบอ่านซ้ำใหม่
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
        public void OnPlateRead(string plate, int camId, double conf = 0)
        {
            if (string.IsNullOrWhiteSpace(plate)) return;
            lock (hybridLock)
            {
                pendingPlateCam[camId] = plate.Trim();
                pendingPlateConf[camId] = conf;
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

        /// <summary>สรุปเป็นข้อความว่ากล้องหน้า-หลังอ่านได้ตรงกันไหม (ใช้ทั้งตอนอนุญาตและปฏิเสธ)</summary>
        private string DescribePlates(string p1, string p2)
        {
            if (p1 != "" && p2 != "")
                return NormPlate(p1) == NormPlate(p2)
                    ? $"ทะเบียนหน้า-หลังตรงกัน ({p1})"
                    : $"ทะเบียนหน้า-หลังไม่ตรงกัน (หน้า {p1} / หลัง {p2})";
            if (p1 != "") return $"อ่านได้เฉพาะกล้องหน้า ({p1})";
            if (p2 != "") return $"อ่านได้เฉพาะกล้องหลัง ({p2})";
            return "ยังไม่ได้เลขทะเบียน";
        }

        /// <summary>ได้ผลจากกล้องเดียว ควรรออีกกล้องไหม — ตัดสินจาก "อีกกล้องกำลังทำอะไรอยู่"
        /// ไม่ใช่การรอตามเวลาตายตัว
        ///
        ///   เห็นป้ายอยู่ หรือกำลังอ่านเลขอยู่ → รอต่อ (มันกำลังจะได้คำตอบ)
        ///   ตรวจไม่เจอป้ายเลย                 → ไม่ต้องรอ ตัดสินได้เลย
        ///
        /// มีเพดาน otherCamMaxWaitSec กันค้างกรณีเห็นป้ายแต่อ่านไม่ออกสักที
        /// คืนเหตุผลออกมาทาง waitReason ไว้โชว์บนหน้าจอด้วย</summary>
        private bool ShouldWaitForOtherCam(string p1, string p2, out string waitReason)
        {
            waitReason = "";
            if (p1 != "" && p2 != "") return false;          // ครบสองฝั่งแล้ว
            if (p1 == "" && p2 == "") return false;          // ยังไม่มีสักฝั่ง
            int other = (p1 != "") ? 2 : 1;
            int mine = (p1 != "") ? 1 : 2;
            string otherName = other == 1 ? "หน้า" : "หลัง";

            bool otherSeeing;
            lock (hybridLock) otherSeeing = plateSeen[other];

            lock (turnLock)
            {
                if (plateSubmitted[other]) return false;     // อีกกล้องส่งมาแล้ว (ค่าหมดอายุไปเอง) ไม่ต้องรอ
                if (plateSubmittedAt[mine] == DateTime.MinValue) return false;

                double waited = (DateTime.Now - plateSubmittedAt[mine]).TotalSeconds;
                if (waited >= otherCamMaxWaitSec) return false;   // เพดานกันค้าง

                // อีกกล้องกำลังทำงานอยู่จริงไหม
                bool busy = isReading[other] || confirmCount[other] > 0 || otherSeeing;
                if (!busy) return false;                     // ไม่เจอป้าย ไม่ได้อ่านอะไรอยู่ → ตัดสินเลย

                waitReason = isReading[other] || confirmCount[other] > 0
                    ? $"กล้อง{otherName}กำลังอ่านเลขอยู่ ({confirmCount[other]}/{readsToConfirm}) — รอให้เสร็จก่อน ({waited:F0}/{otherCamMaxWaitSec:F0} วิ)"
                    : $"กล้อง{otherName}เห็นป้ายแล้ว กำลังจะอ่าน — รอก่อน ({waited:F0}/{otherCamMaxWaitSec:F0} วิ)";
                return true;
            }
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

            // อ่านได้กล้องเดียวแต่อีกกล้องกำลังจะได้ → รอให้ครบสองฝั่งก่อนตัดสิน
            // (ทำทุกกรณี ไม่ใช่เฉพาะตอนเปิดสวิตช์ "ต้องตรงทั้ง 2 กล้อง" เหมือนเดิม
            //  เพราะต่อให้นโยบายไม่บังคับ ผลที่แสดงก็ควรบอกได้ว่าหน้า-หลังตรงกันไหม)
            if (ShouldWaitForOtherCam(p1, p2, out string waitWhy))
            {
                SetAccessUi("⏳ กำลังรอกล้องอีกตัว...", Color.DarkOrange, Color.Red,
                            dbPlate, "-", waitWhy);
                return;
            }

            // สวิตช์ "ต้องตรงทั้ง 2 กล้อง" = บล็อกเฉพาะตอนอ่านได้ทั้งคู่แต่ขัดกัน
            // (รถติดป้ายด้านเดียว อีกกล้องอ่านไม่เจอ → ไม่ถือว่าขัด ยังผ่านได้)
            bool blockedByDisagree = requirePlatesAgree && platesDisagree;
            bool plateOk = blockedByDisagree ? false : (m1 || m2);

            if (plateOk)
            {
                lock (hybridLock)
                {
                    gateBusy = true; sawMismatch = false; retryCount = 0;
                    pendingRfidTag = ""; pendingPlateCam[1] = ""; pendingPlateCam[2] = ""; pendingPlateConf[1] = pendingPlateConf[2] = 0;
                }
                string note = DescribePlates(p1, p2);
                // อ่านได้ทั้งคู่แต่คนละเลข แล้วนโยบายไม่ได้บังคับให้ตรงกัน → ผ่านด้วยฝั่งที่ตรงบัตร
                // ต้องบอกให้ชัดว่าอีกฝั่งไม่ตรง ไม่ใช่กลบไว้เฉย ๆ
                if (platesDisagree)
                    note += (m1 && m2) ? "" : $" — ใช้ฝั่งที่ตรงกับบัตร (กล้อง{(m1 ? "หน้า" : "หลัง")})";
                GrantAccess(owner, dbPlateShow, dbPerm, $"✔ {note} และตรงกับบัตร");
                return;
            }

            // สวิตช์: อ่านป้ายได้แต่ไม่ตรง + อนุญาตให้ผ่านด้วยบัตร (แต่ถ้าหน้า-หลังขัดกันและเปิด requirePlatesAgree ห้ามใช้ทางลัดนี้)
            if (havePlate && allowPlateTagMismatch && !blockedByDisagree)
            {
                lock (hybridLock)
                {
                    gateBusy = true; sawMismatch = false; retryCount = 0;
                    pendingRfidTag = ""; pendingPlateCam[1] = ""; pendingPlateCam[2] = ""; pendingPlateConf[1] = pendingPlateConf[2] = 0;
                }
                GrantAccess(owner, dbPlateShow, dbPerm,
                            $"⚠️ {DescribePlates(p1, p2)} แต่ไม่ตรงกับบัตร — อนุญาตด้วย RFID ตามนโยบาย");
                return;
            }

            if (!havePlate) return;   // ยังไม่มีป้าย → รอ (timer จัดการเคสไม่มีป้าย) อย่าตั้ง sawMismatch

            // มีป้ายแต่ไม่ผ่านเงื่อนไข → วนอ่านซ้ำจนครบเวลา แล้วปฏิเสธ
            bool keepTrying;
            lock (hybridLock)
            {
                // หมดสิทธิ์อ่านซ้ำเมื่อ "ครบจำนวนรอบ" หรือ "หมดเวลา" อย่างใดอย่างหนึ่ง
                keepTrying = retryCount < retryMaxRounds &&
                             (DateTime.Now - pendingRfidTime).TotalSeconds < retryMaxSec;
                if (keepTrying)
                {
                    retryCount++;
                    pendingPlateCam[1] = ""; pendingPlateCam[2] = ""; pendingPlateConf[1] = pendingPlateConf[2] = 0; sawMismatch = true;
                }
                else { gateBusy = true; pendingRfidTag = ""; sawMismatch = false; retryCount = 0; }
            }
            if (keepTrying) ResetLprTurn();   // เคลียร์ล็อกป้ายเก่า ให้กล้องอ่านใหม่ได้จริงในรอบ retry

            string detail = blockedByDisagree
                ? $"{DescribePlates(p1, p2)} — กำลังอ่านซ้ำ..."
                : $"{DescribePlates(p1, p2)} แต่ไม่ตรงกับบัตร ({dbPlate}) — กำลังอ่านซ้ำ...";
            if (keepTrying)
                SetAccessUi($"🔄 กำลังตรวจสอบใหม่ (รอบ {retryCount}/{retryMaxRounds})...",
                            Color.DarkOrange, Color.Red, dbPlate, "-", detail);
            else
                DenyAccess(blockedByDisagree
                    ? $"⛔ {DescribePlates(p1, p2)} (ตรวจสอบซ้ำแล้ว)"
                    : $"⛔ {DescribePlates(p1, p2)} ไม่ตรงกับบัตร {dbPlate} (ตรวจสอบซ้ำแล้ว)");
        }

        // ---- โหมด LPR อย่างเดียว (ไม่มีบัตร): ป้ายตรงฐานข้อมูล = ผ่าน ----
        private void DecideLprOnly(string p1, string p2)
        {
            // อ่านได้กล้องเดียวแต่อีกกล้องกำลังจะได้ → รอให้ครบสองฝั่ง (เหมือนโหมดไฮบริด)
            if (ShouldWaitForOtherCam(p1, p2, out string waitWhy))
            {
                SetAccessUi("⏳ กำลังรอกล้องอีกตัว...", Color.DarkOrange, Color.Red,
                            p1 != "" ? p1 : p2, "-", waitWhy);
                return;
            }

            logMode = "LPR"; logTag = ""; logPlate1 = p1; logPlate2 = p2;
            logPlateDb = ""; logProvince = ""; logOwner = ""; logPermission = "";
            // สวิตช์ 4: อ่านได้ทั้ง 2 กล้องแต่เลขคนละอัน → ปฏิเสธ (กันปลอมป้าย) ในโหมด LPR ล้วนด้วย
            bool bothRead = p1 != "" && p2 != "";
            if (requirePlatesAgree && bothRead && NormPlate(p1) != NormPlate(p2))
            {
                lock (hybridLock) { gateBusy = true; pendingPlateCam[1] = ""; pendingPlateCam[2] = ""; pendingPlateConf[1] = pendingPlateConf[2] = 0; }
                DenyAccess($"⛔ {DescribePlates(p1, p2)}");
                return;
            }

            // หน้า-หลังไม่ตรงกันแต่นโยบายไม่ได้บังคับ → ลองฝั่งที่ OCR มั่นใจกว่าก่อน
            double c1, c2;
            lock (hybridLock) { c1 = pendingPlateConf[1]; c2 = pendingPlateConf[2]; }
            var order = (bothRead && NormPlate(p1) != NormPlate(p2) && c2 > c1)
                ? new[] { (plate: p2, cam: "หลัง"), (plate: p1, cam: "หน้า") }
                : new[] { (plate: p1, cam: "หน้า"), (plate: p2, cam: "หลัง") };

            foreach (var item in order)
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
                    lock (hybridLock) { gateBusy = true; pendingPlateCam[1] = ""; pendingPlateCam[2] = ""; pendingPlateConf[1] = pendingPlateConf[2] = 0; }
                    GrantAccess(owner, dbPlate, perm,
                                $"✔ {DescribePlates(p1, p2)} — ผ่านด้วยเลขจากกล้อง{item.cam} (โหมดไม่ใช้ RFID)");
                    return;
                }
            }
            // ไม่มีป้ายที่ลงทะเบียนในระบบ → ปฏิเสธ (มีป้ายให้เทียบแล้ว แต่ไม่พบข้อมูล)
            lock (hybridLock) { gateBusy = true; pendingPlateCam[1] = ""; pendingPlateCam[2] = ""; pendingPlateConf[1] = pendingPlateConf[2] = 0; }
            DenyAccess($"⛔ {DescribePlates(p1, p2)} — ไม่พบข้อมูลในระบบ");
        }

        // ปุ่ม "ประวัติการเข้า-ออก" ในโซนอนุญาต (สร้างด้วยโค้ด ไม่ต้องเพิ่มใน Designer)
        // ---- ไม้กั้น: ป้ายบอกสถานะบนหน้าหลัก + โหลดค่าที่ตั้งไว้ใหม่ ----

        private void InitBarrierStatus()
        {
            lblBarrierStatus = new System.Windows.Forms.Label
            {
                AutoSize = true,
                Location = new System.Drawing.Point(39, 247),
                Font = new Font("Tahoma", 8.5f, FontStyle.Bold),
                ForeColor = Color.Gray,
                Text = "🚦 ไม้กั้น: -",
            };
            groupBox4.Controls.Add(lblBarrierStatus);
            lblBarrierStatus.BringToFront();
        }

        /// <summary>สร้างตัวควบคุมไม้กั้นใหม่ตามค่าที่ตั้งไว้ (เรียกหลังบันทึกหน้าตั้งค่า)</summary>
        public void ReloadBarrier()
        {
            try { barrier?.Dispose(); } catch { }

            var st = SettingsStore.Load();
            barrier = BarrierFactory.Create(st);
            gateOpenSec = st.GateOpenSec > 0 ? st.GateOpenSec : 3;

            if (lblBarrierStatus == null) return;

            string mode = (st.BarrierMode ?? "simulate").ToLower();
            string shortText;
            Color color;
            if (mode == "serial")
            {
                shortText = $"🚦 ไม้กั้น: {st.BarrierComPort} · {st.BarrierBaudRate} bps";
                color = Color.FromArgb(0, 100, 160);
            }
            else if (mode == "http")
            {
                shortText = "🚦 ไม้กั้น: บอร์ดในเครือข่าย";
                color = Color.FromArgb(0, 100, 160);
            }
            else
            {
                shortText = "🚦 ไม้กั้น: โหมดจำลอง";
                color = Color.Gray;
            }

            lblBarrierStatus.Text = shortText;
            lblBarrierStatus.ForeColor = color;
            // รายละเอียดเต็มไว้ใน tooltip กันข้อความยาวเกินกรอบ
            barrierTip.SetToolTip(lblBarrierStatus, barrier.Describe +
                                  $"\nเปิดค้าง {gateOpenSec} วินาที ก่อนสั่งปิดอัตโนมัติ");
        }

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

        private void WriteAccessLog(string result, string reason, bool saveImages)
        {
            // ก๊อปข้อมูลออกมาก่อน กันถูกทับตอนทำงานเบื้องหลัง
            DateTime now = DateTime.Now;
            string mode = logMode, tag = logTag, c1 = logPlate1, c2 = logPlate2,
                   pdb = logPlateDb, prov = logProvince, own = logOwner, perm = logPermission;

            System.Threading.Tasks.Task.Run(() =>
            {
                string w1 = "", w2 = "", i1 = "", i2 = "";

                // เซฟภาพแยก try ของตัวเอง — ถ้าโฟลเดอร์ภาพมีปัญหา ต้องยังบันทึกประวัติลงฐานข้อมูลได้
                if (saveImages)
                {
                    try
                    {
                        string dir = db.GetLogImageDir(now);
                        string stamp = now.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture);
                        var a = SaveCamImages(1, dir, stamp);
                        var b = SaveCamImages(2, dir, stamp);
                        w1 = a.wide; i1 = a.plate;
                        w2 = b.wide; i2 = b.plate;
                    }
                    catch (Exception ex) { Console.WriteLine("เซฟภาพประวัติไม่ได้: " + ex.Message); }
                }

                // บันทึกฐานข้อมูลเสมอ ไม่ว่าภาพจะเซฟสำเร็จหรือไม่
                try
                {
                    db.SaveAccessLog(now, result, reason, mode, tag, c1, c2,
                                     pdb, prov, own, perm, w1, w2, i1, i2);
                }
                catch (Exception ex) { Console.WriteLine("บันทึกประวัติไม่ได้: " + ex.Message); }
            });
        }

        private void GrantAccess(string owner, string plate, string permission,
                                 string detail = "ยืนยัน 2 ชั้นผ่าน (RFID + ป้ายทะเบียน)")
        {
            lock (hybridLock) { sawMismatch = false; retryCount = 0; }
            string who = owner + (permission != "" ? $" ({permission})" : "");
            SetAccessUi("✅ อนุญาตให้เข้า", Color.Green, Color.LimeGreen, plate, who, detail);
            try { barrier?.Open(); } catch { }
            WriteAccessLog("ALLOWED", detail, true);       // ผ่าน → เก็บภาพด้วย
            this.BeginInvoke(new Action(() => { timerGate.Interval = gateOpenSec * 1000; timerGate.Start(); }));
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
            string who = owner + (perm != "" ? $" ({perm})" : "");

            logMode = "RFID"; logTag = tag; logPlate1 = ""; logPlate2 = "";
            logPlateDb = dbPlate; logProvince = dbProv; logOwner = owner; logPermission = perm;
            if (dbProv != "") dbPlate = dbPlate + " " + dbProv;

            SetAccessUi("✅ อนุญาตให้เข้า", Color.Green, Color.LimeGreen,
                        dbPlate, who, "⚠️ ตรวจพบแท็ก RFID แต่ตรวจจับไม่พบป้ายทะเบียน");
            try { barrier?.Open(); } catch { }
            WriteAccessLog("ALLOWED", "⚠️ ตรวจพบแท็ก RFID แต่ตรวจจับไม่พบป้ายทะเบียน", true);
            this.BeginInvoke(new Action(() => { timerGate.Interval = gateOpenSec * 1000; timerGate.Start(); }));
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

                // กล้องยังเห็นป้ายอยู่จริง ๆ หรือเพิ่งเห็นไปเมื่อครู่ = ไม่ใช่ "รถไม่ติดป้าย"
                bool plateVisible = plateSeen[1] || plateSeen[2] ||
                                    (DateTime.Now - lastPlateSeenAt).TotalSeconds < 3;

                // เคสA: มีบัตร + กล้องไม่เห็นป้ายเลยจริง ๆ + ครบ noPlateGraceSec → อนุญาต (รถไม่ติดป้าย)
                if (haveRfid && !havePlate && !plateVisible && !sawMismatch && allowNoPlate &&
                    (DateTime.Now - pendingRfidTime).TotalSeconds >= noPlateGraceSec)
                {
                    tagOnlyGrant = pendingRfidTag;
                    gateBusy = true;
                    pendingRfidTag = "";
                }
                // เคสA2: มีบัตร ไม่มีป้าย + สวิตช์ 2 ปิด + ครบ noPlateDenySec → ปฏิเสธ
                else if (haveRfid && !havePlate && !sawMismatch && !allowNoPlate &&
                         (DateTime.Now - pendingRfidTime).TotalSeconds >= noPlateDenySec)
                {
                    noPlateDeny = true;
                    gateBusy = true;
                    pendingRfidTag = "";
                }
                // เคสA3: มีบัตร + เห็นป้ายอยู่แต่อ่านไม่สำเร็จสักที จนครบเวลา → ปฏิเสธ
                // (กันช่องโหว่: ป้ายผิดคันที่อ่านไม่นิ่ง จะหลุดไปเข้าเคส A แล้วได้ผ่านฟรี)
                else if (haveRfid && !havePlate && plateVisible && !sawMismatch &&
                         (DateTime.Now - pendingRfidTime).TotalSeconds >= retryMaxSec)
                {
                    noPlateDeny = true;
                    gateBusy = true;
                    pendingRfidTag = "";
                }
                // เคสB: มีบัตร + เคยอ่านป้ายได้แต่ไม่ตรงบัตร + ครบเวลาอ่านซ้ำ → ปฏิเสธจริง
                // (ต้องมีเคสนี้ไว้ใน timer ด้วย เพราะถ้ารถพ้นโซนกล้องไปก่อน จะไม่มีป้ายใหม่
                //  เข้ามาเรียก DecideWithRfid ให้ตัดสินอีก แล้ว sawMismatch จะค้าง
                //  ทำให้ pendingRfidTag ไม่ถูกล้าง และเคส A/A2/A3 ก็ทำงานไม่ได้ทั้งหมด)
                else if (haveRfid && sawMismatch &&
                         (DateTime.Now - pendingRfidTime).TotalSeconds >= retryMaxSec)
                {
                    tagMismatchDeny = pendingRfidTag;
                    gateBusy = true;
                    pendingRfidTag = "";
                    pendingPlateCam[1] = ""; pendingPlateCam[2] = ""; pendingPlateConf[1] = pendingPlateConf[2] = 0;
                    sawMismatch = false; retryCount = 0;
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
                        pendingPlateCam[1] = ""; pendingPlateCam[2] = ""; pendingPlateConf[1] = pendingPlateConf[2] = 0;
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
            else
            {
                // กระตุ้นให้ตัดสินอีกครั้ง — จำเป็นเพราะตอนนี้กล้องส่งผลเข้ามาแค่ครั้งเดียว
                // (plateSubmitted) ถ้ารอบแรกเจอ ShouldWaitForOtherCam แล้วถอยออกไป
                // จะไม่มีใครเรียก TryDecide ให้อีกเลยจนกว่าจะหมดเวลาเป็นวินาที
                // ตัวจับเวลานี้เต้นทุก 1 วินาที เรียกซ้ำได้ปลอดภัย (TryDecide
                // เช็คเงื่อนไขครบเองและถอยออกเมื่อยังไม่ถึงเวลา)
                bool anyPlate;
                lock (hybridLock) anyPlate = pendingPlateCam[1] != "" || pendingPlateCam[2] != "";
                if (anyPlate) TryDecide();
            }
        }

        private void label3_Click_1(object sender, EventArgs e)
        {

        }

        private void label3_Click_2(object sender, EventArgs e)
        {

        }

        // 💡 1. เพิ่มตัวแปรเช็คสถานะ AI ไว้ (สำคัญมาก ป้องกัน RAM ล้น)
        private bool isAIProcessing = false;
        // กล้องนี้กำลังรอผลอ่านเลขจากฝั่ง AI อยู่หรือเปล่า (แยกรายกล้อง)
        // ใช้ตอบคำถาม "อีกกล้องกำลังทำอะไรอยู่" ตอนตัดสินใจว่าจะรอมันไหม
        private bool[] isReading = new bool[3];

        // ขอสิทธิ์อ่านเลขทะเบียน — ให้ทีละกล้องเท่านั้น ใครเจอป้ายก่อนได้ก่อน
        private bool TryTakeLprTurn(int camId)
        {
            lock (turnLock)
            {
                // ส่งผลเข้าศูนย์ตัดสินใจไปแล้ว → หยุดอ่าน รอผลตัดสิน
                // (มีเพดานเวลากันค้าง เผื่อมีเส้นทางที่ผลไม่ออกสักที)
                if (plateSubmitted[camId])
                {
                    if ((DateTime.Now - plateSubmittedAt[camId]).TotalSeconds < submitHoldMaxSec)
                        return false;
                    plateSubmitted[camId] = false;   // รอนานผิดปกติ กลับไปอ่านใหม่
                }
                if (plateLocked[camId])
                {
                    // ยังไม่ถึงเวลาตรวจทาน → พักไว้ก่อน ไม่เปลืองรอบ OCR
                    if ((DateTime.Now - plateLockedAt[camId]).TotalSeconds < relockRecheckSec)
                        return false;
                    // ถึงเวลาแล้ว → ปล่อยให้อ่านหนึ่งรอบ เช็คว่ายังเป็นป้ายเดิมอยู่ไหม
                    // (เลื่อนเวลาไว้ก่อนเลย กันยิงซ้ำรัว ๆ ระหว่างรอผลรอบนี้)
                    plateLockedAt[camId] = DateTime.Now;
                }
                if (lprOwner == camId) return true;              // ถืออยู่แล้ว
                if (lprOwner == 0)
                {
                    lprOwner = camId; lprOwnerSince = DateTime.Now; return true;
                }
                // อีกกล้องถืออยู่ ถ้าถือนานผิดปกติให้แย่งได้ กันระบบค้าง
                if ((DateTime.Now - lprOwnerSince).TotalSeconds > lprOwnerMaxSec)
                {
                    lprOwner = camId; lprOwnerSince = DateTime.Now; return true;
                }
                return false;
            }
        }

        /// <summary>รูปแบบป้ายไทยที่เป็นไปได้ — กรองผลอ่านเพี้ยนทิ้งก่อนนับยืนยัน</summary>
        private static bool IsPlausiblePlate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            string t = s.Replace(" ", "").Replace("-", "").Trim();
            if (t.Length < 4 || t.Length > 10) return false;

            int thai = 0, digit = 0;
            foreach (char c in t)
            {
                if (c >= '\u0E00' && c <= '\u0E7F') thai++;
                else if (char.IsDigit(c)) digit++;
                else return false;                  // มีอักขระแปลกปน = อ่านเพี้ยนแน่นอน
            }
            return thai >= 1 && digit >= 2;         // ต้องมีทั้งตัวอักษรไทยและตัวเลข
        }

        /// <summary>นับผลอ่าน คืน true เมื่อ "ยืนยันแล้ว" (อ่านได้เลขเดิมซ้ำครบตามกำหนด)
        /// ถ้ายังไม่ยืนยัน กล้องนี้จะถือคิวต่อ อ่านซ้ำจนกว่าจะชัวร์</summary>
        private bool ReleaseLprTurn(int camId, string plateRead, double conf, out bool plateChanged)
        {
            plateChanged = false;
            lock (turnLock)
            {
                bool confirmed = false;

                if (IsPlausiblePlate(plateRead))
                {
                    if (plateRead == lastReadPlate[camId])
                    {
                        confirmCount[camId]++;
                        // เก็บค่าที่มั่นใจที่สุดของเลขนี้ไว้ส่งให้ศูนย์ตัดสินใจ
                        if (conf > bestConf[camId]) bestConf[camId] = conf;
                    }
                    else
                    {
                        // อ่านได้คนละเลขกับที่ค้างไว้ = คนละคัน/เปลี่ยนป้ายแล้ว
                        // ทิ้งผลเก่าทั้งหมดแล้วเริ่มนับยืนยันของเลขใหม่
                        plateChanged = lastReadPlate[camId] != "";
                        lastReadPlate[camId] = plateRead;
                        confirmCount[camId] = 1;
                        plateLocked[camId] = false;
                        bestConf[camId] = conf;
                    }

                    if (confirmCount[camId] >= readsToConfirm)
                    {
                        plateLocked[camId] = true;
                        plateLockedAt[camId] = DateTime.Now;
                        confirmed = true;
                    }
                }

                // ปล่อยคิวทุกครั้งที่อ่านจบ ไม่ใช่เฉพาะตอนยืนยันแล้ว
                //
                // เดิมกล้องแรกจะยึดคิวไว้จนกว่าจะยืนยันครบ (2 รอบ) อีกกล้องจึงเริ่ม
                // อ่านไม่ได้เลยจนกล้องแรกเสร็จ กว่าจะได้ผลครบสองฝั่งจึงนานมาก
                // ตอนนี้สลับกันอ่านคนละรอบ ทั้งสองกล้องจึงยืนยันเสร็จไล่เลี่ยกัน
                // (ยังอ่านทีละกล้องอยู่ดี เพราะ isAIProcessing กันการยิงซ้อนไว้แล้ว)
                if (lprOwner == camId) lprOwner = 0;
                return confirmed;
            }
        }

        // เริ่มนับใหม่ทั้งหมด (รถคันใหม่เข้ามา หรือตัดสินเสร็จแล้ว)
        private void ResetLprTurn()
        {
            lock (turnLock)
            {
                lprOwner = 0;
                plateLocked[1] = plateLocked[2] = false;
                lastReadPlate[1] = lastReadPlate[2] = "";
                confirmCount[1] = confirmCount[2] = 0;
                plateLockedAt[1] = plateLockedAt[2] = DateTime.MinValue;
                plateSubmitted[1] = plateSubmitted[2] = false;
                plateSubmittedAt[1] = plateSubmittedAt[2] = DateTime.MinValue;
                bestConf[1] = bestConf[2] = 0;
            }
        }

        /// <summary>ล้างผลอ่านของกล้องเดียว — ใช้ตอนป้ายหายจากเฟรมนานพอจะถือว่ารถไปแล้ว
        /// (อีกกล้องอาจยังจับรถของตัวเองอยู่ จึงห้ามไปล้างของมันด้วย)</summary>
        private void ResetLprTurnCam(int camId)
        {
            lock (turnLock)
            {
                if (lprOwner == camId) lprOwner = 0;
                plateLocked[camId] = false;
                lastReadPlate[camId] = "";
                confirmCount[camId] = 0;
                plateLockedAt[camId] = DateTime.MinValue;
                plateSubmitted[camId] = false;
                plateSubmittedAt[camId] = DateTime.MinValue;
                bestConf[camId] = 0;
            }
        }

        // ✅ 3. ฟังก์ชันส่งรูปไปให้ Python API (ฉบับแก้ RAM ระเบิด 30GB)
        // หมายเหตุ: await ทุกจุดในนี้ใส่ .ConfigureAwait(false) เพราะเมธอดนี้ถูกเรียก
        // รัวมากตอน tracking (สูงสุด ~25 ครั้ง/วินาที/กล้อง) — ถ้าไม่ใส่ Continuation
        // หลัง await จะถูกดีดกลับไปรันบน UI thread ทุกครั้ง (WinForms SynchronizationContext)
        // พอมี 2 กล้องพร้อมกันจะยิงรวมกันหลายสิบครั้ง/วินาที ทำให้ UI thread รับงาน
        // ท่วมจน "ค้าง" ทั้งโปรแกรม (คือปัญหาที่เจอตอนต่อกล้อง 2 ตัว) ส่วนจุดที่ต้อง
        // แตะ UI จริง ๆ (SetPlateText/SetLprStatus/this.Invoke) ยังมี Invoke/BeginInvoke
        // กำกับไว้ครบอยู่แล้ว จึงตัดการ capture context ทิ้งได้อย่างปลอดภัย
        private async Task SendToAI(Bitmap bitmap, int camId)
        {
            // ถ้า AI ยังประมวลผลรูปเก่าไม่เสร็จ ให้โยนรูปใหม่ทิ้งทันที! ไม่ต้องรอคิวให้หนัก RAM
            if (isAIProcessing)
            {
                bitmap.Dispose();
                return;
            }

            // ยังไม่ถึงคิวของกล้องนี้ หรือกล้องนี้อ่านเลขได้แล้ว → ทิ้งรูปทันทีเช่นกัน
            if (!TryTakeLprTurn(camId))
            {
                bitmap.Dispose();
                return;
            }

            isAIProcessing = true; // ล็อกคิวบอกว่า AI กำลังทำงาน
            lock (turnLock) isReading[camId] = true;

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
                        var response = await client.PostAsync("http://localhost:5000/predict", content).ConfigureAwait(false);
                        var jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        // แกะคำตอบ JSON มาโชว์บนหน้าจอ
                        dynamic result = JsonConvert.DeserializeObject(jsonResponse);
                        if (result != null && result.status == "success")
                        {
                            // อ่านเฉพาะเลขทะเบียน — ฝั่ง AI ไม่ส่งชื่อจังหวัดมาแล้ว
                            // (ตัดการอ่านจังหวัดออกเมื่อ 2569-09-15 เพราะอ่านไม่นิ่ง
                            //  และไม่เคยถูกใช้ตัดสินเปิด-ปิดไม้กั้นอยู่แล้ว — จังหวัดที่
                            //  โชว์ตอนอนุญาตใช้ค่าจากฐานข้อมูลที่ลงทะเบียนไว้แทน)
                            string plateText = (string)result.text;
                            string camName = camId == 1 ? "หน้า" : "หลัง";
                            double conf = 0;
                            try { if (result.confidence != null) conf = (double)result.confidence; } catch { }

                            // นับยืนยันก่อน — ส่งเข้าระบบตัดสินเฉพาะเลขที่ชัวร์แล้วเท่านั้น
                            bool confirmed = ReleaseLprTurn(camId, plateText, conf, out bool plateChanged);
                            // อ่านได้คนละเลขกับของเดิม = คันก่อนหน้าไปแล้ว ผลเก่าที่ค้าง
                            // อยู่ในศูนย์ตัดสินใจใช้ไม่ได้อีกแล้ว ต้องล้างทันที ไม่งั้นถ้ามี
                            // บัตรแตะเข้ามาตอนนี้ จะเอาเลขของคันก่อนไปเทียบกับบัตร
                            if (plateChanged)
                            {
                                lock (hybridLock) { pendingPlateCam[camId] = ""; pendingPlateConf[camId] = 0; }
                            }
                            int seenTimes;
                            double sendConf;
                            // ยืนยันแล้วให้ "ส่งครั้งเดียว" — ตั้งธงตรงนี้ใต้ล็อกเดียวกับที่อ่านค่า
                            // กันกรณีสองรอบอ่านจบพร้อมกันแล้วส่งซ้ำ
                            bool submitNow = false;
                            lock (turnLock)
                            {
                                seenTimes = confirmCount[camId];
                                sendConf = bestConf[camId];
                                if (confirmed && !plateSubmitted[camId])
                                {
                                    plateSubmitted[camId] = true;
                                    plateSubmittedAt[camId] = DateTime.Now;
                                    submitNow = true;
                                }
                            }

                            this.Invoke((MethodInvoker)delegate
                            {
                                SetPlateText(camId, plateText);
                                if (submitNow)
                                {
                                    SetLprStatus(camId, $"✅ ตรวจสอบสำเร็จ — ส่งให้ระบบตัดสินแล้ว (กล้อง{camName})",
                                                 Color.Green);
                                    OnPlateRead(plateText, camId, sendConf);
                                }
                                else if (confirmed)
                                {
                                    SetLprStatus(camId, $"✅ ตรวจสอบสำเร็จ — รอผลตัดสิน (กล้อง{camName})", Color.Green);
                                }
                                else
                                {
                                    SetLprStatus(camId, $"🔎 กำลังยืนยัน {seenTimes}/{readsToConfirm} (กล้อง{camName})",
                                                 Color.DarkOrange);
                                }
                            });

                            lock (boxLock)
                            {
                                latestPlateText[camId] = plateText;
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
                        else
                        {
                            // เดิมตรงนี้ "ไม่มี else" เลย — พอฝั่ง AI ตอบ error (เช่น
                            // อ่านตัวอักษรไม่ออก / ไม่พบป้ายในภาพ) โค้ดจะเงียบสนิท
                            // ไม่แจ้งอะไรบนหน้าจอ ผู้ใช้จึงเห็นแค่ "ตรวจจับเจอกรอบ
                            // แล้วจบแค่นั้น" โดยไม่รู้ว่าเกิดอะไรขึ้น
                            // ต้องโชว์สาเหตุออกมา ไม่งั้นไล่ปัญหาไม่ได้เลย
                            string why = "อ่านไม่สำเร็จ";
                            try
                            {
                                if (result != null && result.message != null)
                                    why = (string)result.message;
                            }
                            catch { }

                            string camName2 = camId == 1 ? "หน้า" : "หลัง";
                            SetLprStatus(camId, $"⚠️ {why} (กล้อง{camName2})", Color.OrangeRed);
                            Console.WriteLine($"[predict] กล้อง{camId} อ่านไม่สำเร็จ: {why}");
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
                lock (turnLock) isReading[camId] = false;
                bitmap.Dispose();       // 💡 เคลียร์ขยะรูปนี้ออกจาก RAM ทันที
                ReleaseLprTurn(camId, "", 0, out _);
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

        // หมายเหตุ ConfigureAwait(false): ดูคำอธิบายเดียวกันที่ SendToAI() ด้านบน
        // เมธอดนี้ยิ่งเรียกถี่กว่า SendToAI มาก (ทุก trackIntervalMs) จึงสำคัญกว่าด้วยซ้ำ
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

                        // บอกฝั่ง AI ว่ากล้องนี้กำลัง "เกาะติด" ป้ายที่เจอแล้วอยู่หรือเปล่า
                        //   เกาะติดอยู่ -> ฝั่งนั้นใช้ความละเอียดต่ำลง เร็วขึ้น ~3 เท่า
                        //                  กรอบจึงตามป้ายได้ลื่นขึ้นมาก
                        //   ยังไม่เจอ   -> ใช้ความละเอียดเต็ม จับรถที่เพิ่งเข้ามาไกล ๆ ให้ไวที่สุด
                        bool tracking; Rectangle lastBox = Rectangle.Empty;
                        lock (boxLock)
                        {
                            tracking = hasPlateBox[camId] &&
                                       (DateTime.Now - latestBoxTime[camId]).TotalMilliseconds < 1000;
                            if (tracking) lastBox = latestPlateBox[camId];
                        }
                        content.Add(new StringContent(tracking ? "1" : "0"), "tracking");

                        // ส่งกรอบจากเฟรมก่อนหน้าไปด้วย ฝั่ง AI จะได้ค้นเฉพาะบริเวณรอบ ๆ
                        // กรอบนั้นแทนที่จะค้นทั้งเฟรม (เร็วกว่า ~4 เท่า) ถ้าหาไม่เจอ
                        // ในบริเวณนั้น ฝั่งนั้นจะถอยไปค้นทั้งเฟรมให้เอง
                        if (tracking && lastBox.Width > 0 && lastBox.Height > 0)
                        {
                            content.Add(new StringContent(lastBox.Left.ToString()), "bx1");
                            content.Add(new StringContent(lastBox.Top.ToString()), "by1");
                            content.Add(new StringContent(lastBox.Right.ToString()), "bx2");
                            content.Add(new StringContent(lastBox.Bottom.ToString()), "by2");
                        }

                        var response = await client.PostAsync("http://localhost:5000/detect", content).ConfigureAwait(false);
                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        dynamic result = JsonConvert.DeserializeObject(json);

                        // ฝั่ง AI กำลังอ่านตัวอักษรอยู่ จึงยังไม่ได้ตรวจกรอบให้เฟรมนี้
                        // → ข้ามไปเฉย ๆ ห้ามตกไปที่ else ด้านล่าง ไม่งั้นจะไปล้าง
                        // hasPlateBox/plateSeen ทิ้ง ทำให้กรอบกระพริบและคิวอ่าน
                        // (lprOwner) หลุดกลางคันทั้งที่ป้ายยังอยู่ในเฟรม
                        if (result != null && result.status == "busy") return;

                        lock (boxLock)
                        {
                            if (result != null && result.status == "success" && result.box != null)
                            {
                                int x1 = (int)result.box[0], y1 = (int)result.box[1];
                                int x2 = (int)result.box[2], y2 = (int)result.box[3];
                                latestPlateBox[camId] = new Rectangle(x1, y1, x2 - x1, y2 - y1);
                                hasPlateBox[camId] = true;
                                latestBoxTime[camId] = DateTime.Now;
                                missCount[camId] = 0;      // เจอแล้ว เริ่มนับพลาดใหม่
                                lock (hybridLock) { plateSeen[camId] = true; lastPlateSeenAt = DateTime.Now; }
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
                                // อย่าเพิ่งดับกรอบเพราะพลาดครั้งเดียว! ป้ายจริงยังอยู่ตรงนั้น
                                // แค่เฟรมนั้นเบลอ/มุมเอียง/แสงแวบ ทำให้ YOLO พลาดชั่วคราว
                                // ถ้าดับทันทีกรอบจะกระพริบติด ๆ ดับ ๆ ตลอดเวลา
                                // ต้องพลาดติดกันครบ missToLose ครั้งก่อน ถึงจะถือว่าป้ายหายจริง
                                missCount[camId]++;
                                if (missCount[camId] >= missToLose)
                                {
                                    bool wasVisible = hasPlateBox[camId];
                                    hasPlateBox[camId] = false;
                                    lock (hybridLock) plateSeen[camId] = false;
                                    // มองไม่เห็นป้ายแล้ว → ถ้ายังถือคิวอยู่และยังไม่ยืนยัน ให้ปล่อยคิวทันที กันอีกกล้องรอเก้อ
                                    lock (turnLock)
                                    {
                                        if (lprOwner == camId && !plateLocked[camId]) lprOwner = 0;
                                    }
                                    if (wasVisible) UpdateLprZone(camId);

                                    // ป้ายหายไปนานพอแล้ว = รถคันนั้นไปแล้ว ล้างเลขที่ค้างไว้ทิ้ง
                                    // ไม่งั้นคันถัดไปเข้ามา หน้าจอจะยังโชว์เลขของคันก่อนอยู่
                                    // (เดิมล้างตรงนี้ไม่ได้เลย ต้องรอครบรอบเปิด-ปิดไม้กั้นเท่านั้น)
                                    if ((DateTime.Now - latestBoxTime[camId]).TotalSeconds >= plateGoneResetSec)
                                    {
                                        bool hadResult;
                                        lock (turnLock) hadResult = lastReadPlate[camId] != "";
                                        if (hadResult)
                                        {
                                            ResetLprTurnCam(camId);
                                            latestPlateText[camId] = "";
                                            SetPlateText(camId, "-");
                                        }
                                    }
                                }
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
            // "-" คือสถานะยังไม่มีผล (ป้ายหายจากเฟรม) ใช้สีเทาให้ต่างจากเลขที่อ่านได้จริง
            Color color = (text == "-" || string.IsNullOrWhiteSpace(text)) ? Color.Gray : Color.Green;
            Action apply = () => { lbl.Text = text; lbl.ForeColor = color; };
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

        private void groupBox2_Enter_1(object sender, EventArgs e)
        {

        }
    }
}