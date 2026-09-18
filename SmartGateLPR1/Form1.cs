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
        // ต้องมากกว่า otherCamHardCapSec เสมอ ไม่งั้นกล้องที่ส่งไปแล้วจะปลดล็อก
        // กลับไปอ่านใหม่ทั้งที่ศูนย์ตัดสินใจยังรออีกกล้องอยู่
        private double submitHoldMaxSec = 20.0;
        // เพดานการรออีกกล้อง (วินาที)
        //
        // ไม่ได้รอตายตัวตามเวลา แต่รอตาม "อีกกล้องกำลังทำอะไรอยู่":
        //   เห็นป้ายอยู่ / กำลังอ่านเลขอยู่  → รอต่อไปเรื่อย ๆ จนกว่าจะเสร็จ
        //   ตรวจไม่เจอป้ายเลย                → ตัดสินทันที ไม่ต้องรอ
        // เพดานนี้เป็นแค่ตัวกันค้าง เผื่อกล้องเห็นป้ายแต่อ่านไม่ออกสักที
        private double otherCamMaxWaitSec = 15.0;
        // เพดานแข็ง: ต่อให้อีกกล้องยังยิงอ่านค้างอยู่ ก็ไม่รอเกินค่านี้
        private double otherCamHardCapSec = 18.0;
        // อีกกล้อง "ตรวจไม่เจอป้ายเลย" ให้รอกี่วินาทีก่อนตัดสินด้วยผลกล้องเดียว
        //
        // เดิมตัดสินทันที ซึ่งเร็วเกินไป — จังหวะที่กล้องแรกส่งผล อีกกล้องอาจกำลัง
        // อยู่ระหว่างเฟรมที่ยังจับกรอบไม่ติด (รถเพิ่งเข้าเฟรม/ป้ายเอียง) พอรอสัก
        // ครู่มันก็เจอ แต่ผลตัดสินออกไปก่อนแล้ว กลายเป็น "อ่านได้จากกล้องเดียว"
        // ทั้งที่จริง ๆ อ่านได้ทั้งคู่
        private double noPlateWaitSec = 3.0;
        // อีกกล้องต้อง "ไม่เห็นกรอบป้ายเลย" ต่อเนื่องกี่วินาที ถึงจะนับว่าว่างจริง
        //
        // เดิมดูแค่ธง ณ วินาทีนั้น (isReading / plateSeen / confirmCount) ซึ่งกระพริบ
        // ตลอดเวลา — ว่างระหว่างยิง OCR แต่ละรอบบ้าง ตัวนับพลาดเฟรมบ้าง พอ TryDecide
        // มาเช็คตรงจังหวะที่บังเอิญว่างครบทั้งสามธง ก็เลิกรอตั้งแต่ 3 วิ (noPlateWaitSec)
        // ทั้งที่กล้องนั้นกำลังอ่านป้ายอยู่แท้ ๆ → ผลออกที่ 8-9 วิแทนที่จะรอครบ 15 วิ
        private double otherCamIdleSec = 2.5;
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
        // ต้องอ่านได้เลขเดิมซ้ำกี่ครั้งถึงจะส่งให้ศูนย์ตัดสินใจ
        //
        // เดิมบังคับ 2 รอบเสมอ (ก่อนหน้านั้น 3) ซึ่งเสียเวลารอ /predict อีกรอบเต็ม
        // กับทุกคัน ทั้งที่ส่วนใหญ่รอบแรก OCR ก็มั่นใจเต็มที่อยู่แล้ว
        //
        // ตอนนี้ใช้ "ความมั่นใจ" แทน "จำนวนรอบ":
        //   conf >= submitConfMin  -> ส่งเลยรอบเดียว (เคสปกติ เร็วสุด)
        //   conf <  submitConfMin  -> ขออ่านซ้ำให้ได้เลขเดิมอีกครั้งก่อน (เหมือนเดิม)
        // คุณภาพไม่ได้ลดลง เพราะรอบที่สองมีไว้กันผลที่ไม่น่าเชื่อถืออยู่แล้ว
        // ผลที่ OCR ให้คะแนนสูงมากก็ไม่มีอะไรต้องกันเพิ่ม
        private int readsToConfirm = 1;          // จำนวนรอบขั้นต่ำเมื่อ conf ถึงเกณฑ์
        private int readsToConfirmLowConf = 2;   // จำนวนรอบเมื่อ conf ต่ำกว่าเกณฑ์
        private double submitConfMin = 0.90;     // คะแนน OCR ที่ถือว่า "มั่นใจพอจะส่งเลย"
        private int[] neededReads = new int[] { 1, 1, 1 };   // ใช้โชว์บนหน้าจอเท่านั้น

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
        // สองฝั่ง (บัตร / ป้าย) ต้องมาห่างกันไม่เกินกี่วินาที ถึงจะถือว่าเป็นคันเดียวกัน
        //
        // ⚠️ ค่านี้ต้องมากกว่า "เวลารอ" ทุกตัวเสมอ (otherCamHardCapSec, noPlateGraceSec,
        // plateUnconfirmedWaitSec) ไม่งั้นจะค้างตายสนิท:
        // ตอนตั้ง 10 วิ แล้วขยับเวลารอเป็น 15-18 วิ เคยเกิดอาการ "แตะบัตรแล้ว กล้อง
        // ส่งเลขแล้ว แต่ไม่ตัดสินสักที" เพราะพอเลย 10 วิ TryDecide จะมองว่าบัตร
        // หมดอายุ (rfidFresh = false) แล้ว return ทิ้งทุกครั้ง ส่วนตัวจับเวลาก็เข้า
        // เคสไหนไม่ได้เลย (เคส A/A2/A3 ต้องไม่มีป้าย แต่ป้ายดิบยังค้างอยู่) สุดท้าย
        // ไม่มีใครตัดสินให้เลยสักทาง
        private int hybridWindowSec = 25;
        // กันค้างขั้นสุดท้าย: แตะบัตรมาแล้วเกินกี่วินาที ถ้ายังไม่มีใครตัดสินให้
        // ให้บังคับตัดสินด้วยข้อมูลเท่าที่มีทันที (ดู forceDecideNow)
        // ต้องมากกว่าเวลารอทุกตัว แต่ต้องน้อยกว่า hybridWindowSec
        private int decideDeadlineSec = 20;
        // ธงบังคับตัดสิน — ตั้งโดยตัวจับเวลาเมื่อครบ decideDeadlineSec
        // ทำให้ ShouldWaitForOtherCam เลิกรอทันที และ TryDecide ไม่กรองอายุข้อมูล
        private bool forceDecideNow = false;
        private int noPlateGraceSec = 15;    // มีบัตรแต่ไม่เจอป้าย รอกี่วิ แล้วปล่อยผ่าน (ขยายจาก 7 → 15 วิ ตามที่ผู้ใช้สั่ง)
        private int noPlateDenySec = 9;      // มีบัตรแต่ไม่เจอป้าย รอกี่วิ แล้วปฏิเสธ (สวิตช์ 2 ปิด)
        private bool requireRfid = true;
        private bool allowNoPlate = true;
        private DateTime plateSeenNoTagAt = DateTime.MinValue;  // เวลาที่เริ่มเห็นป้ายทั้งที่ยังไม่มีแท็ก (โหมด RFID)
        private int plateOnlyDenySec = 2;                       // เจอป้ายแต่ไม่มีแท็กกี่วิ → ปฏิเสธ
        private bool[] plateSeen = new bool[3];   // index 1,2 = กล้องหน้า/หลังเจอป้ายไหม
        private DateTime lastPlateSeenAt = DateTime.MinValue;  // เวลาที่กล้องใดกล้องหนึ่งเห็นป้ายล่าสุด

        // ตัดช่องว่างก่อนเทียบ (ฐานข้อมูลเก็บ "กท 2058" แต่ LPR อ่านได้ "กท2058")
        private static string NormPlate(string s) =>
            (s ?? "").Replace(" ", "").Replace("-", "").Trim();

        private Label PlateLabel(int camId) => camId == 1 ? lblLicensePlate1 : lblLicensePlate2;
        // ===== แถบสถานะ 3 บรรทัดต่อกล้อง 1 ตัว =====
        //
        // เดิมเป็น Label เดียว (lblLprStatus1/2) ที่เขียนทับข้อความไปเรื่อย ๆ
        // ผู้ใช้จึงเห็นแค่ "สถานะล่าสุด" ไม่รู้ว่าเดินมาถึงขั้นไหนแล้ว และข้อความ
        // กระพริบเปลี่ยนเร็วมากจนอ่านไม่ทัน
        //
        // แบบใหม่: แสดงเป็นขั้นบันได 3 บรรทัด ค้างไว้ให้เห็นทั้งกระบวนการ
        //   บรรทัด 1  ตรวจจับป้าย            เทา → เจอป้ายแล้วเปลี่ยนเป็นเหลืองค้าง
        //   บรรทัด 2  OCR กำลังประมวลผล     โผล่มาเป็นเหลือง → อ่านได้แล้วเป็นเขียวค้าง
        //   บรรทัด 3  ผลยืนยันเรียบร้อยแล้ว  เขียวค้าง (กล้องตัวนี้หยุดอ่านจนกว่าผลตัดสินจะออก)
        // พอผลตัดสินออก ทุกบรรทัดกลับไปจุดเริ่มต้น (เหลือบรรทัด 1 สีเทา)
        private enum LprStage
        {
            Idle = 0,        // ยังไม่เจอป้าย
            PlateFound = 1,  // เจอกรอบป้ายแล้ว
            Ocr = 2,         // กำลังอ่านตัวอักษร
            OcrDone = 3,     // อ่านตัวอักษรได้แล้ว
            Confirmed = 4,   // ส่งเข้าระบบตัดสินแล้ว รอผล
        }

        private readonly Label[,] lprLines = new Label[3, 3];   // [camId, บรรทัด 0-2]
        private readonly LprStage[] lprStage = new LprStage[3];

        private static readonly Color StageGray = Color.Gray;
        // เหลืองล้วนบนพื้นขาวอ่านแทบไม่ออก ใช้เหลืองเข้ม (amber) แทนให้ยังอ่านได้
        private static readonly Color StageYellow = Color.FromArgb(200, 150, 0);
        private static readonly Color StageGreen = Color.FromArgb(0, 150, 60);

        /// <summary>สร้าง Label 3 บรรทัดของทั้งสองกล้อง (แทน lblLprStatus1/2 ที่ถอดออกไปแล้ว)</summary>
        private void InitLprStatusLines()
        {
            string[] texts = { "🔍 ตรวจจับป้าย", "🔤 OCR กำลังประมวลผล", "✅ ผลยืนยันเรียบร้อยแล้ว" };
            for (int camId = 1; camId <= 2; camId++)
            {
                var host = camId == 1 ? groupBox5 : groupBox6;
                for (int i = 0; i < 3; i++)
                {
                    var lbl = new Label
                    {
                        AutoSize = true,
                        Location = new System.Drawing.Point(20, 160 + i * 22),
                        Font = new Font("Tahoma", 9f, FontStyle.Bold),
                        ForeColor = StageGray,
                        Text = texts[i],
                        Visible = i == 0,          // เริ่มต้นโชว์แค่บรรทัดแรก
                    };
                    host.Controls.Add(lbl);
                    lbl.BringToFront();
                    lprLines[camId, i] = lbl;
                }
                lprStage[camId] = LprStage.Idle;
            }
        }

        /// <summary>เลื่อนแถบสถานะของกล้องไปยังขั้นที่กำหนด</summary>
        /// <param name="rewind">true = ยอมให้ถอยกลับไปขั้นก่อนหน้าได้ (ใช้ตอนรีเซ็ต/อ่านไม่สำเร็จ)
        /// ปกติเป็น false เพื่อไม่ให้สถานะกระพริบถอยหน้าถอยหลังตามจังหวะเฟรม</param>
        private void SetLprStage(int camId, LprStage stage, bool rewind = false)
        {
            if (camId < 1 || camId > 2) return;
            if (lprLines[camId, 0] == null) return;

            Action apply = () =>
            {
                if (!rewind && stage <= lprStage[camId]) return;
                lprStage[camId] = stage;

                // บรรทัด 1: เทาตอนยังไม่เจอ → เหลืองค้างเมื่อเจอป้ายแล้ว
                lprLines[camId, 0].ForeColor = stage >= LprStage.PlateFound ? StageYellow : StageGray;

                // บรรทัด 2: โผล่เมื่อเริ่มอ่าน เป็นเหลือง → เขียวค้างเมื่ออ่านได้
                lprLines[camId, 1].Visible = stage >= LprStage.Ocr;
                lprLines[camId, 1].ForeColor = stage >= LprStage.OcrDone ? StageGreen : StageYellow;

                // บรรทัด 3: โผล่เป็นเขียวค้างเมื่อส่งเข้าระบบตัดสินแล้ว
                lprLines[camId, 2].Visible = stage >= LprStage.Confirmed;
                lprLines[camId, 2].ForeColor = StageGreen;
            };

            if (this.InvokeRequired) this.BeginInvoke(apply);
            else apply();
        }

        /// <summary>กลับไปจุดเริ่มต้น: เหลือบรรทัดเดียว "ตรวจจับป้าย" สีเทา</summary>
        private void ResetLprStage(int camId) => SetLprStage(camId, LprStage.Idle, rewind: true);

        /// <summary>ข้อความอธิบายเพิ่มเติม (conf / สาเหตุที่อ่านไม่ออก) ไปอยู่ใน tooltip
        /// ของบรรทัดแรก เพื่อไม่ให้ไปรกแถบสถานะ 3 บรรทัด</summary>
        private void SetLprDetail(int camId, string detail)
        {
            if (camId < 1 || camId > 2 || lprLines[camId, 0] == null) return;
            Action apply = () => barrierTip.SetToolTip(lprLines[camId, 0], detail ?? "");
            if (this.InvokeRequired) this.BeginInvoke(apply);
            else apply();
        }
        // มีบัตรแล้วแต่ป้ายยังไม่ตรง จะวนอ่านซ้ำได้กี่รอบ / นานสุดกี่วิ ก่อนยอมแพ้
        //
        // เดิมให้เวลา 20 วินาที ซึ่งนานเกินไป — ถ้าอ่าน 2 รอบแล้วยังไม่ตรง
        // รอบที่ 3-4 ก็มักได้ผลเดิม คนขับได้แต่จอดรอเปล่า ๆ
        // ตอนนี้จำกัดทั้ง "จำนวนรอบ" และ "เวลา" อันไหนถึงก่อนถือว่าหมดสิทธิ์
        private int retryMaxRounds = 2;  // อ่านซ้ำได้ 2 รอบ (ตั้ง 0 = ไม่อ่านซ้ำเลย)
        private int retryMaxSec = 6;     // และต้องไม่เกินกี่วิ นับจากตอนแตะบัตร
        private int retryCount = 0;      // นับรอบที่อ่านซ้ำไปแล้วของบัตรใบปัจจุบัน
        private bool sawMismatch = false;
        // เห็นกรอบป้ายอยู่ (หรือกระพริบ) แต่ OCR ไม่เคยยืนยันเลขได้เลยสักครั้ง —
        // รอกี่วิก่อนตัดสิน (เคส A3 ใน TimerHybridTimeout_Tick) แยกจาก retryMaxSec
        // เพราะ retryMaxSec ใช้กับจังหวะ "อ่านซ้ำตอนป้ายไม่ตรงบัตร" (เคส B) ด้วย
        // ถ้าใช้ค่าเดียวกันจะไปกระทบจังหวะนั้นโดยไม่ได้ตั้งใจ — ตั้งเท่ากับ
        // noPlateGraceSec (15 วิ) ตามที่ผู้ใช้สั่งให้รอนานเท่ากัน
        private int plateUnconfirmedWaitSec = 15;


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
            InitLprStatusLines();       // แถบสถานะ 3 บรรทัดของกล้องทั้งสองตัว
            CheckTimingInvariants();    // กันตั้งค่าเวลาขัดกันจนระบบค้าง
            ReloadBarrier();           // สร้างตัวควบคุมไม้กั้น + อัปเดตป้ายสถานะ
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
            ResetLprStage(camId);
            var pLbl = PlateLabel(camId);
            Action reset = () =>
            {
                pLbl.Text = "แสดงเลขทะเบียน"; pLbl.ForeColor = Color.Black;
            };
            if (this.InvokeRequired) this.BeginInvoke(reset); else reset();

        }

        // ===== ระบบรับภาพจากกล้อง: แยก "ดูดเฟรม" ออกจาก "ประมวลผล" =====
        //
        // อาการเดิม: พอเริ่มตรวจจับ/อ่านป้าย ภาพจะดีเลย์ 7-10 วินาที และถ้าถอดสาย
        // กล้องออก ภาพยังขยับต่ออีกหลายวินาทีก่อนจะตัด — นั่นคือหลักฐานชัดเจนว่า
        // "เฟรมค้างอยู่ในคิว" ไม่ใช่ภาพสด
        //
        // สาเหตุ: ลูปเดิมทำทุกอย่างอยู่ในเธรดเดียว —
        //     capture.Read() -> แปลงเป็น Bitmap -> วาดกรอบ -> ส่งขึ้นจอ (Invoke)
        //     -> ยิง /detect -> ยิง /predict -> Thread.Sleep(30)
        // กล้องส่งเฟรมมาทุก ~40ms (25fps) แต่ลูปหนึ่งรอบใช้เวลามากกว่านั้นมาก
        // (โดยเฉพาะตอน displayBox.Invoke ไปติดรอ UI thread ที่กำลังยุ่ง)
        // เฟรมที่ตามมาจึงไปกองอยู่ในบัฟเฟอร์ของ FFMPEG/RTSP และ capture.Read()
        // จะดึง "เฟรมที่เก่าที่สุดในคิว" ออกมาเสมอ ไม่ใช่เฟรมล่าสุด
        // → ยิ่งรันนาน คิวยิ่งยาว ดีเลย์ยิ่งสะสมขึ้นเรื่อย ๆ ไม่มีวันไล่ทัน
        //
        // วิธีแก้: แยกเป็นสองเธรด
        //   1) เธรดดูดเฟรม (GrabLoop) — วนอ่านให้เร็วที่สุดโดยไม่ทำอะไรเลย
        //      เก็บไว้แค่ "เฟรมล่าสุดใบเดียว" ใบเก่าทิ้งทันที
        //      หน้าที่เดียวคือระบายคิวของ FFMPEG ให้ว่างตลอดเวลา
        //   2) เธรดประมวลผล (CaptureCamera) — หยิบเฟรมล่าสุดไปใช้ตามจังหวะตัวเอง
        //      ถ้าทำงานช้าก็แค่ "ข้ามเฟรม" ไม่ได้ทำให้คิวยาวขึ้น
        // ผลคือภาพที่เห็นเป็นเฟรมล่าสุดเสมอ ต่อให้ AI ช้าแค่ไหนก็ไม่หน่วงสะสม
        private readonly object[] grabLock = { new object(), new object(), new object() };
        private Mat[] latestGrab = new Mat[3];      // เฟรมล่าสุดที่ยังไม่ถูกหยิบไปใช้
        private bool[] hasNewGrab = new bool[3];
        // กันไม่ให้ส่งภาพขึ้นจอซ้อนกันจนคิวของ UI ยาว (ดูเหตุผลที่จุดเรียกใช้)
        private bool[] displayPending = new bool[3];
        private readonly object displayLock = new object();
        // เก็บเฟรมไว้ทำภาพประวัติทุกกี่ ms (ไม่ต้องทุกเฟรม เปลืองเปล่า ๆ)
        private const int LastFrameSnapshotMs = 200;
        private DateTime[] lastSnapshotTimes = new DateTime[] { DateTime.MinValue, DateTime.MinValue, DateTime.MinValue };

        private bool CamRunning(int camId) => camId == 1 ? isCam1Running : isCam2Running;

        /// <summary>เปิดกล้องแบบตั้งค่าให้หน่วงน้อยที่สุด</summary>
        private VideoCapture OpenCameraLowLatency(string url)
        {
            // บอก FFMPEG ไม่ให้สะสมบัฟเฟอร์ — ต้องตั้งก่อนสร้าง VideoCapture
            // (OpenCV อ่านค่านี้ตอนเปิดสตรีม)
            //   rtsp_transport;tcp = ไม่ให้แพ็กเก็ตหาย (ถ้ากล้องรองรับ udp และ
            //                        อยากได้หน่วงต่ำกว่านี้อีก เปลี่ยนเป็น udp ได้)
            //   fflags;nobuffer    = ไม่ต้องสะสมเฟรมไว้ให้ภาพลื่น เอาสดไว้ก่อน
            //   flags;low_delay    = โหมดหน่วงต่ำของ decoder
            //   max_delay;0        = ไม่ต้องรอจัดเรียงแพ็กเก็ต
            try
            {
                Environment.SetEnvironmentVariable(
                    "OPENCV_FFMPEG_CAPTURE_OPTIONS",
                    "rtsp_transport;tcp|fflags;nobuffer|flags;low_delay|max_delay;0");
            }
            catch { }

            var cap = new VideoCapture(url);
            try
            {
                // ขอให้ไดรเวอร์เก็บบัฟเฟอร์ไว้ใบเดียว (บางแบ็กเอนด์ไม่รองรับ ไม่เป็นไร)
                cap.Set(VideoCaptureProperties.BufferSize, 1);
            }
            catch { }
            return cap;
        }

        /// <summary>เธรดดูดเฟรม: อ่านให้เร็วที่สุด เก็บแต่เฟรมล่าสุด ใบเก่าทิ้งทันที</summary>
        private void GrabLoop(VideoCapture capture, int camId)
        {
            Mat m = new Mat();
            while (CamRunning(camId))
            {
                try
                {
                    // ห้ามมีงานหนักหรือ Sleep ในลูปนี้เด็ดขาด — มันคือตัวที่คอย
                    // ระบายคิวของ FFMPEG ถ้ามันช้า คิวจะยาวและดีเลย์จะกลับมาทันที
                    if (!capture.Read(m) || m.Empty())
                    {
                        Thread.Sleep(5);      // อ่านไม่ได้ (สายหลุด/สตรีมสะดุด) พักสั้น ๆ
                        continue;
                    }

                    lock (grabLock[camId])
                    {
                        latestGrab[camId]?.Dispose();   // ใบเก่ายังไม่ถูกใช้ก็ทิ้งเลย
                        latestGrab[camId] = m.Clone();
                        hasNewGrab[camId] = true;
                    }
                }
                catch
                {
                    Thread.Sleep(5);
                }
            }
            m.Dispose();
        }

        /// <summary>หยิบเฟรมล่าสุดไปใช้ (คืน null ถ้ายังไม่มีเฟรมใหม่ตั้งแต่ครั้งก่อน)
        /// ผู้เรียกเป็นเจ้าของ Mat ที่ได้ ต้อง Dispose เอง</summary>
        private Mat TakeLatestGrab(int camId)
        {
            lock (grabLock[camId])
            {
                if (!hasNewGrab[camId] || latestGrab[camId] == null) return null;
                hasNewGrab[camId] = false;
                Mat m = latestGrab[camId];
                latestGrab[camId] = null;
                return m;
            }
        }

        // --- 3. ฟังก์ชันดึงภาพ (ใช้ร่วมกันได้ โดยดูจาก ID) ---
        private void CaptureCamera(string url, PictureBox displayBox, int camId)
        {
            VideoCapture capture = OpenCameraLowLatency(url);

            if (!capture.IsOpened())
            {

                ShowNoSignal(displayBox, "❌ ไม่มีการเชื่อมต่อกล้อง");

                // ปิดสถานะตาม ID
                if (camId == 1) isCam1Running = false;
                else isCam2Running = false;

                return;
            }

            // เธรดดูดเฟรมทำงานคู่ขนานไปตลอด ไม่ว่าเธรดนี้จะช้าแค่ไหน
            Thread grabber = new Thread(() => GrabLoop(capture, camId)) { IsBackground = true };
            grabber.Start();

            // วนลูปโดยเช็คสถานะของใครของมัน
            while (CamRunning(camId))
            {
                Mat frame = TakeLatestGrab(camId);
                if (frame == null)
                {
                    Thread.Sleep(5);          // ยังไม่มีเฟรมใหม่ รอสั้น ๆ
                    continue;
                }

                try
                {
                    using (frame)
                    {
                        // ---- ตัดสินใจก่อนว่ารอบนี้ต้องใช้ภาพทำอะไรบ้าง ----
                        // ถ้าไม่ต้องทำอะไรเลย จะได้ไม่เสียเวลาแปลงเป็น Bitmap
                        // (การแปลงภาพ 1080p + วาดกรอบ ใช้เวลาหลายมิลลิวินาที
                        //  ถ้าทำทิ้งทุกเฟรมโดยไม่ได้ใช้ ก็เปลืองซีพียูเปล่า ๆ)
                        bool needDisplay;
                        lock (displayLock) needDisplay = !displayPending[camId];

                        bool nowTracking;
                        lock (boxLock) nowTracking = hasPlateBox[camId];
                        int myInterval = nowTracking ? trackIntervalMs : detectIntervalMs;
                        lock (turnLock)
                        {
                            // อีกกล้องกำลังอ่านเลขอยู่ → กล้องนี้ลดความถี่ลง คืนเครื่องให้ตัวที่กำลังทำงาน
                            if (lprOwner != 0 && lprOwner != camId) myInterval *= 3;
                        }
                        // ส่งผลเข้าระบบตัดสินไปแล้ว = งานของกล้องตัวนี้จบรอบแล้ว
                        // ต้อง "หยุดจริง ๆ" ทั้งตรวจจับตำแหน่งและอ่านตัวอักษร จนกว่า
                        // ผลตัดสินจะออก (ResetLprTurn จะล้างธงนี้ให้) ไม่งั้นกรอบแดง
                        // กับเลขบนจอจะยังขยับ ทั้งที่ค่าที่ส่งไปแล้วถูกล็อกไว้แล้ว
                        // — ผู้ใช้เห็นแล้วสับสนว่าตกลงมันจบรอบหรือยัง
                        //
                        // ยังคงเพดาน submitHoldMaxSec ไว้กันค้าง เผื่อผลตัดสินไม่ออก
                        // สักที (เช่น อีกกล้องหลุดไป) จะได้กลับมาทำงานเองได้
                        bool holdAfterSubmit;
                        lock (turnLock)
                        {
                            holdAfterSubmit = plateSubmitted[camId] &&
                                (DateTime.Now - plateSubmittedAt[camId]).TotalSeconds < submitHoldMaxSec;
                        }

                        bool needDetect = !holdAfterSubmit && !isDetecting[camId] &&
                            (DateTime.Now - lastDetectTimes[camId]).TotalMilliseconds >= myInterval;

                        // ยิงอ่านตราบใดที่ "เห็นกรอบป้ายอยู่" ไม่ใช่แค่ตอนมีความเคลื่อนไหว
                        // (แก้ปัญหา: รถหยุดนิ่งสนิทแล้วภาพไม่เปลี่ยน ระบบเดิมจะไม่ยิงอ่านซ้ำอีกเลย)
                        bool needRead = !holdAfterSubmit && nowTracking &&
                            (DateTime.Now - lastCaptureTimes[camId]).TotalSeconds >= cooldownSeconds;

                        bool needSnapshot =
                            (DateTime.Now - lastSnapshotTimes[camId]).TotalMilliseconds >= LastFrameSnapshotMs;

                        if (!needDisplay && !needDetect && !needRead && !needSnapshot)
                            continue;

                        Bitmap image = BitmapConverter.ToBitmap(frame);
                        try
                        {
                            // เก็บเฟรมล่าสุดไว้ใช้บันทึกภาพประวัติ (ไม่ต้องทำทุกเฟรม —
                            // ภาพประวัติเก่ากว่าปัจจุบันเสี้ยววินาทีก็ไม่ต่างอะไร
                            // แต่การ clone ภาพ 1080p ทุกเฟรมเปลืองซีพียูจริง)
                            if (needSnapshot)
                            {
                                lastSnapshotTimes[camId] = DateTime.Now;
                                Bitmap snap = (Bitmap)image.Clone();
                                lock (frameLock)
                                {
                                    if (lastFrame[camId] != null) lastFrame[camId].Dispose();
                                    lastFrame[camId] = snap;
                                }
                            }

                            // --- 1. โชว์ภาพสดขึ้นหน้าจอ UI ---
                            //
                            // ⚠️ ต้องเป็น BeginInvoke ห้ามใช้ Invoke — Invoke จะ "บล็อก"
                            // เธรดนี้ไว้จนกว่า UI thread จะว่างมารับงาน ซึ่งตอน AI ทำงาน
                            // UI thread ยุ่งมาก เธรดนี้เลยค้างยาว → คิวเฟรมยิ่งพอกขึ้น
                            // (นี่คือสาเหตุหลักข้อหนึ่งของอาการหน่วง 7-10 วินาที)
                            //
                            // และต้องกันไม่ให้ส่งซ้อนด้วย (displayPending) ไม่งั้นถ้า UI
                            // ตามไม่ทัน งานจะไปกองในคิวของ UI แทน พร้อมกับ Bitmap ที่
                            // ยังไม่ถูกปล่อย = ทั้งหน่วงทั้งกินแรม
                            if (needDisplay)
                            {
                                lock (displayLock) displayPending[camId] = true;
                                Bitmap displayImage = (Bitmap)image.Clone();
                                DrawPlateOverlay(displayImage, camId);
                                try
                                {
                                    displayBox.BeginInvoke(new Action(() =>
                                    {
                                        try
                                        {
                                            var old = displayBox.Image;
                                            displayBox.Image = displayImage;
                                            old?.Dispose();
                                        }
                                        finally
                                        {
                                            lock (displayLock) displayPending[camId] = false;
                                        }
                                    }));
                                }
                                catch
                                {
                                    displayImage.Dispose();
                                    lock (displayLock) displayPending[camId] = false;
                                }
                            }

                            // --- 1.5 อัปเดตกรอบแดงให้ตามป้าย: เรียก /detect เป็นระยะ ---
                            if (needDetect)
                            {
                                lastDetectTimes[camId] = DateTime.Now;
                                Bitmap detectFrame = new Bitmap(image);
                                Task.Run(() => DetectBox(detectFrame, camId));
                            }

                            // --- 2. ระบบ Auto-Trigger เช็คป้ายทะเบียน ---
                            if (needRead)
                            {
                                lastCaptureTimes[camId] = DateTime.Now;
                                // ส่งรูปเต็มไปให้ AI อ่าน (ภาพใหม่จากเฟรมปัจจุบันเสมอ)
                                Bitmap frameToSend = new Bitmap(image);
                                Task.Run(() => SendToAI(frameToSend, camId));
                            }
                        }
                        finally
                        {
                            image.Dispose();
                        }
                    }
                }
                catch
                {
                    // กรณี Error ข้ามไปก่อน
                }
            }

            // รอเธรดดูดเฟรมจบก่อนค่อยปิดกล้อง ไม่งั้นมันจะไปอ่าน capture ที่ถูกปิดไปแล้ว
            try { grabber.Join(500); } catch { }
            lock (grabLock[camId])
            {
                latestGrab[camId]?.Dispose();
                latestGrab[camId] = null;
                hasNewGrab[camId] = false;
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
            lock (hybridLock)
            {
                sawMismatch = false; retryCount = 0; plateSeenNoTagAt = DateTime.MinValue;
                forceDecideNow = false;      // จบรอบแล้ว เลิกโหมดกันค้าง
            }
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
                forceDecideNow = false;      // บัตรใบใหม่ เริ่มนับเวลากันค้างใหม่
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
        }
        public void ReloadAccessPolicy() => LoadAccessPolicy();

        // ตัดสินเมื่อข้อมูลครบสองฝั่งภายในหน้าต่างเวลา
        /// <param name="force">true = โหมดกันค้าง ใช้ข้อมูลเท่าที่มีโดยไม่กรองอายุ
        /// และไม่รออีกกล้องอีกแล้ว (เรียกจากตัวจับเวลาเมื่อครบ decideDeadlineSec)</param>
        private void TryDecide(bool force = false)
        {
            string tag, p1, p2;
            lock (hybridLock)
            {
                if (gateBusy) return;
                bool rfidFresh = pendingRfidTag != "" &&
                                 (force || (DateTime.Now - pendingRfidTime).TotalSeconds <= hybridWindowSec);

                p1 = (pendingPlateCam[1] != "" && (force || (DateTime.Now - pendingPlateCamTime[1]).TotalSeconds <= hybridWindowSec)) ? pendingPlateCam[1] : "";
                p2 = (pendingPlateCam[2] != "" && (force || (DateTime.Now - pendingPlateCamTime[2]).TotalSeconds <= hybridWindowSec)) ? pendingPlateCam[2] : "";
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
            // ครบสองฝั่งแล้ว = ได้เลขจากกล้องทั้งสองตัว → ตัดสินทันที ไม่ต้องรออะไรอีก
            if (p1 != "" && p2 != "") return false;
            if (p1 == "" && p2 == "") return false;          // ยังไม่มีสักฝั่ง

            // โหมดกันค้าง: ครบกำหนดแล้ว เลิกรอทุกกรณี
            lock (hybridLock) { if (forceDecideNow) return false; }
            int other = (p1 != "") ? 2 : 1;
            int mine = (p1 != "") ? 1 : 2;
            string otherName = other == 1 ? "หน้า" : "หลัง";

            bool otherSeeing;
            lock (hybridLock) otherSeeing = plateSeen[other];

            // อีกกล้องเพิ่งเห็นกรอบป้ายไปเมื่อไม่นานมานี้ไหม — ดูจาก "เวลา" แทนธง
            // ชั่วขณะ ธงกระพริบได้ แต่เวลาที่เจอกรอบล่าสุดไม่กระพริบ
            DateTime otherBoxAt;
            lock (boxLock) otherBoxAt = latestBoxTime[other];
            bool otherRecentlySawPlate = otherBoxAt != DateTime.MinValue &&
                                         (DateTime.Now - otherBoxAt).TotalSeconds < otherCamIdleSec;

            lock (turnLock)
            {
                if (plateSubmitted[other]) return false;     // อีกกล้องส่งมาแล้ว (ค่าหมดอายุไปเอง) ไม่ต้องรอ
                if (plateSubmittedAt[mine] == DateTime.MinValue) return false;

                double waited = (DateTime.Now - plateSubmittedAt[mine]).TotalSeconds;

                // เพดานกันค้าง — แต่ห้ามตัดบทตอนอีกกล้อง "กำลังยิงอ่านอยู่จริง ๆ"
                // ถ้าครบ 10 วิพอดีตอนที่มันยิง /predict ค้างอยู่ การตัดสินทิ้งตรงนั้น
                // แปลว่าทิ้งคำตอบที่เหลืออีกไม่กี่ร้อยมิลลิวินาทีก็จะได้แล้ว
                // จึงยืดให้อีกหน่อยถึง otherCamHardCapSec เฉพาะกรณีที่กำลังอ่านค้าง
                if (waited >= otherCamHardCapSec) return false;
                if (waited >= otherCamMaxWaitSec && !isReading[other]) return false;

                // อีกกล้องกำลังทำงานอยู่จริงไหม
                bool busy = isReading[other] || confirmCount[other] > 0 ||
                            otherSeeing || otherRecentlySawPlate;
                if (!busy)
                {
                    // ไม่เจอป้าย ไม่ได้อ่านอะไรอยู่ → ให้โอกาสอีก noPlateWaitSec วินาที
                    // เผื่อมันกำลังจะจับกรอบติด พ้นเวลานี้แล้วค่อยตัดสินด้วยกล้องเดียว
                    if (waited >= noPlateWaitSec) return false;
                    waitReason = $"กล้อง{otherName}ยังตรวจไม่เจอป้าย — รออีกหน่อย ({waited:F0}/{noPlateWaitSec:F0} วิ)";
                    return true;
                }

                waitReason = isReading[other] || confirmCount[other] > 0
                    ? $"กล้อง{otherName}กำลังอ่านเลขอยู่ ({confirmCount[other]}/{neededReads[other]}) — รอให้เสร็จก่อน ({waited:F0}/{otherCamMaxWaitSec:F0} วิ)"
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

            // ป้ายฝั่งใดฝั่งหนึ่งตรงกับบัตร = ผ่าน
            // (เคยมีสวิตช์ "บังคับให้ป้ายหน้า-หลังต้องเลขตรงกัน" คร่อมเงื่อนไขนี้อยู่
            //  ถอดออกแล้วเมื่อ 2569-09-18 ตามที่ผู้ใช้สั่ง — ในทางปฏิบัติมันปิดอยู่เสมอ
            //  เพราะ OCR อ่านพลาดฝั่งเดียวก็ทำให้รถที่ถูกต้องผ่านไม่ได้)
            bool plateOk = m1 || m2;

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

            // (เคยมีสวิตช์ "อนุญาตรถที่ป้ายทะเบียนไม่ตรงกับแท็ก RFID" เป็นทางลัดให้ผ่าน
            //  ด้วยบัตรอย่างเดียวตรงนี้ ถอดออกแล้วเมื่อ 2569-09-18 ตามที่ผู้ใช้สั่ง
            //  ป้ายต้องตรงกับบัตรเสมอ ไม่มีทางลัด)

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

            string detail = $"{DescribePlates(p1, p2)} แต่ไม่ตรงกับบัตร ({dbPlate}) — กำลังอ่านซ้ำ...";
            if (keepTrying)
                SetAccessUi($"🔄 กำลังตรวจสอบใหม่ (รอบ {retryCount}/{retryMaxRounds})...",
                            Color.DarkOrange, Color.Red, dbPlate, "-", detail);
            else
                DenyAccess($"⛔ {DescribePlates(p1, p2)} ไม่ตรงกับบัตร {dbPlate} (ตรวจสอบซ้ำแล้ว)");
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
            bool bothRead = p1 != "" && p2 != "";

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

        /// <summary>ตรวจว่าค่าเวลาทุกตัวยังเรียงลำดับถูกต้อง ถ้าไม่ถูกให้ขยับตามอัตโนมัติ
        ///
        /// เคยพังมาแล้วจริง ๆ: ตอนขยับ "เวลารออีกกล้อง" จาก 10 เป็น 15-18 วิ แต่ลืม
        /// ขยับ hybridWindowSec (10 วิ) ตาม ผลคือพอเลย 10 วิ ศูนย์ตัดสินใจมองว่า
        /// บัตรหมดอายุแล้ว return ทิ้งทุกครั้ง ส่วนตัวจับเวลาก็เข้าเคสไหนไม่ได้เลย
        /// ระบบค้างสนิท ไม้กั้นไม่ขยับ ต้องปิดโปรแกรมทิ้งอย่างเดียว
        ///
        /// ลำดับที่ต้องเป็นจริงเสมอ (จากมากไปน้อย):
        ///   hybridWindowSec  &gt; decideDeadlineSec  &gt; เวลารอทุกตัว
        ///   submitHoldMaxSec &gt; otherCamHardCapSec &gt; otherCamMaxWaitSec</summary>
        private void CheckTimingInvariants()
        {
            double longestWait = Math.Max(otherCamHardCapSec,
                                 Math.Max(noPlateGraceSec,
                                 Math.Max(plateUnconfirmedWaitSec, noPlateDenySec)));

            if (otherCamHardCapSec <= otherCamMaxWaitSec)
            {
                otherCamHardCapSec = otherCamMaxWaitSec + 3;
                WarnTiming($"otherCamHardCapSec ต้องมากกว่า otherCamMaxWaitSec \u2192 ปรับเป็น {otherCamHardCapSec}");
            }
            if (submitHoldMaxSec <= otherCamHardCapSec)
            {
                submitHoldMaxSec = otherCamHardCapSec + 2;
                WarnTiming($"submitHoldMaxSec ต้องมากกว่า otherCamHardCapSec \u2192 ปรับเป็น {submitHoldMaxSec}");
            }
            if (decideDeadlineSec <= longestWait)
            {
                decideDeadlineSec = (int)Math.Ceiling(longestWait) + 2;
                WarnTiming($"decideDeadlineSec ต้องมากกว่าเวลารอทุกตัว \u2192 ปรับเป็น {decideDeadlineSec}");
            }
            if (hybridWindowSec <= decideDeadlineSec)
            {
                hybridWindowSec = decideDeadlineSec + 5;
                WarnTiming($"hybridWindowSec ต้องมากกว่า decideDeadlineSec \u2192 ปรับเป็น {hybridWindowSec}");
            }
        }

        private static void WarnTiming(string msg) =>
            Console.WriteLine("\u26A0\uFE0F ค่าเวลาตั้งขัดกัน (แก้ให้อัตโนมัติแล้ว): " + msg);

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
            bool forceDecide = false;
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
                // เคสA3: มีบัตร + เห็นป้ายอยู่ (หรือกระพริบ) แต่อ่านไม่สำเร็จสักที จนครบเวลา
                //
                // เดิมเคสนี้ "ปฏิเสธเสมอ" ไม่สนสวิตช์ allowNoPlate เลย ทำให้รถที่มี
                // แท็กถูกต้องแต่ป้ายอ่านไม่ออก (สกปรก/มุมเอียง/แสงไม่พอ) โดนบล็อกทั้งที่
                // ผู้ดูแลเปิดสวิตช์ "อนุญาตรถไม่ติดป้าย" ไว้แล้ว — บั๊กนี้ทำให้ดูเหมือน
                // ระบบไม่อนุญาตทั้งที่ควรอนุญาต ตอนนี้ให้ยึดตามสวิตช์เดียวกับเคส A/A2:
                //   allowNoPlate เปิด  → ปฏิเสธไม่ได้ผลอะไร ก็ปล่อยผ่านด้วยแท็กอย่างเดียว
                //   allowNoPlate ปิด   → ยังคงปฏิเสธเหมือนเดิม (เข้มงวด ต้องเห็นป้ายชัด)
                else if (haveRfid && !havePlate && plateVisible && !sawMismatch &&
                         (DateTime.Now - pendingRfidTime).TotalSeconds >= plateUnconfirmedWaitSec)
                {
                    if (allowNoPlate)
                    {
                        tagOnlyGrant = pendingRfidTag;
                    }
                    else
                    {
                        noPlateDeny = true;
                    }
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
                // เคสD (กันค้าง): มีบัตร + มีป้ายมาแล้วอย่างน้อยหนึ่งฝั่ง แต่ยังไม่มี
                // ใครตัดสินให้สักที จนเลย decideDeadlineSec → บังคับตัดสินเดี๋ยวนี้
                //
                // เคสนี้เคยเป็นรูโหว่ที่ทำให้ระบบค้างสนิท: เคส A/A2/A3 ทุกตัวต้องการ
                // "ไม่มีป้าย" (!havePlate) ส่วนเคส B ต้องการ sawMismatch และเคส C
                // ต้องการ "ไม่มีบัตร" — พอสถานะเป็น "มีบัตร + มีป้ายฝั่งเดียว + ยัง
                // ไม่ mismatch" จึงไม่เข้าเคสไหนเลย ได้แต่ตกไปเรียก TryDecide ซึ่ง
                // ถ้ามันติดเงื่อนไขอะไรอยู่ก็วนแบบนั้นไปตลอดกาล ไม้กั้นไม่ขยับ
                else if (haveRfid && havePlate &&
                         (DateTime.Now - pendingRfidTime).TotalSeconds >= decideDeadlineSec)
                {
                    forceDecideNow = true;
                    forceDecide = true;
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
            else if (forceDecide)
                TryDecide(force: true);      // กันค้าง: ตัดสินด้วยข้อมูลเท่าที่มี
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
                // ล็อกไว้รอตรวจทาน (plateLocked) หรือส่งไปตัดสินแล้วรอผล (plateSubmitted)
                // ทั้งสองสถานะต้อง "แอบอ่านทวนเป็นระยะ" เหมือนกัน ไม่งั้นถ้าป้ายที่
                // เห็นอยู่จริง ๆ ถูกสลับเป็นคันใหม่ระหว่างรอ (กล้องไม่เคย "มองไม่เห็น
                // ป้าย" เลยสักครั้ง) กรอบ+เลขบนจอจะค้างของป้ายเก่าไปจนกว่าจะครบ
                // submitHoldMaxSec (12 วิ) ซึ่งนานเกินไปสำหรับผู้ใช้ที่กำลังทดสอบอยู่
                //
                // แก้โดยให้ทั้งสองสถานะใช้จังหวะรีเช็กเดียวกันคือ relockRecheckSec (2 วิ)
                // — เดิม plateSubmitted เช็คก่อนแล้วบล็อกอ่านยาวไปจนครบ submitHoldMaxSec
                // เลย ไม่เคยไปถึงจังหวะรีเช็กของ plateLocked ด้านล่างนี้เลย
                if (plateLocked[camId] || plateSubmitted[camId])
                {
                    bool dueForRecheck = (DateTime.Now - plateLockedAt[camId]).TotalSeconds >= relockRecheckSec;
                    if (!dueForRecheck)
                    {
                        // ยังไม่ถึงคิวรีเช็ก — แต่ถ้าค้างสถานะ "ส่งไปตัดสินแล้ว" นานผิดปกติ
                        // (ผลตัดสินไม่ออกสักที) ให้ปลดกลับไปอ่านใหม่กันค้างตายตัว
                        if (plateSubmitted[camId] &&
                            (DateTime.Now - plateSubmittedAt[camId]).TotalSeconds >= submitHoldMaxSec)
                        {
                            plateSubmitted[camId] = false;
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        // ถึงเวลาแล้ว → ปล่อยให้อ่านหนึ่งรอบ เช็คว่ายังเป็นป้ายเดิมอยู่ไหม
                        // (เลื่อนเวลาไว้ก่อนเลย กันยิงซ้ำรัว ๆ ระหว่างรอผลรอบนี้)
                        plateLockedAt[camId] = DateTime.Now;
                    }
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
                    // เทียบแบบตัดช่องว่าง/ขีดออกก่อน (NormPlate) ไม่ใช่เทียบตรงตัวอักษร
                    //
                    // บักเดิม: OCR อ่านรอบแรกได้ "กย 3779" รอบสองได้ "กย3779" ซึ่งเป็น
                    // เลขเดียวกันแท้ ๆ แต่สตริงไม่ตรงกัน โค้ดจึงคิดว่า "เปลี่ยนเป็นคันใหม่"
                    // แล้วรีเซ็ตตัวนับยืนยันกลับเป็น 1 ทุกรอบ ผลคือกล้องที่ conf ต่ำกว่า
                    // เกณฑ์ (ต้องอ่านซ้ำให้ได้เลขเดิม 2 ครั้ง) ไม่มีวันยืนยันสำเร็จเลย
                    // — ตรงกับอาการ "กล้องหลังไม่ยอมส่งค่า ทั้งที่เลขถูกแล้ว"
                    if (NormPlate(plateRead) == NormPlate(lastReadPlate[camId]))
                    {
                        confirmCount[camId]++;
                        // เก็บค่าที่มั่นใจที่สุดของเลขนี้ไว้ส่งให้ศูนย์ตัดสินใจ
                        if (conf > bestConf[camId]) bestConf[camId] = conf;
                    }
                    else
                    {
                        // อ่านได้คนละเลขกับที่ค้างไว้ = คนละคัน/เปลี่ยนป้ายแล้ว
                        // ทิ้งผลเก่าทั้งหมดแล้วเริ่มนับยืนยันของเลขใหม่
                        //
                        // ต้องล้าง plateSubmitted ด้วย ไม่ใช่แค่ plateLocked — ไม่งั้น
                        // พอป้ายใหม่ยืนยันครบแล้ว (อาจครบตั้งแต่รอบเดียวถ้า conf สูง)
                        // SendToAI จะเห็น plateSubmitted[camId] เป็น true ค้างจากป้าย
                        // เก่าอยู่ (เช็คเงื่อนไข "confirmed && !plateSubmitted[camId]")
                        // จึงไม่ส่งป้ายใหม่เข้าศูนย์ตัดสินใจเลย ทั้งที่ยืนยันได้แล้วจริง ๆ
                        plateChanged = lastReadPlate[camId] != "";
                        lastReadPlate[camId] = plateRead;
                        confirmCount[camId] = 1;
                        plateLocked[camId] = false;
                        plateSubmitted[camId] = false;
                        bestConf[camId] = conf;
                    }

                    // มั่นใจพอไหม — ใช้คะแนนที่ดีที่สุดของเลขนี้ตัดสินว่าต้องอ่านกี่รอบ
                    int need = bestConf[camId] >= submitConfMin ? readsToConfirm : readsToConfirmLowConf;
                    neededReads[camId] = need;
                    if (confirmCount[camId] >= need)
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
                neededReads[1] = neededReads[2] = readsToConfirm;
                plateSubmitted[1] = plateSubmitted[2] = false;
                plateSubmittedAt[1] = plateSubmittedAt[2] = DateTime.MinValue;
                bestConf[1] = bestConf[2] = 0;
            }
            // ผลตัดสินออกแล้ว → แถบสถานะทั้งสองกล้องกลับไปจุดเริ่มต้น
            ResetLprStage(1);
            ResetLprStage(2);
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
                neededReads[camId] = readsToConfirm;
                plateSubmitted[camId] = false;
                plateSubmittedAt[camId] = DateTime.MinValue;
                bestConf[camId] = 0;
            }
            ResetLprStage(camId);
        }

        // ✅ 3. ฟังก์ชันส่งรูปไปให้ Python API (ฉบับแก้ RAM ระเบิด 30GB)
        // หมายเหตุ: await ทุกจุดในนี้ใส่ .ConfigureAwait(false) เพราะเมธอดนี้ถูกเรียก
        // รัวมากตอน tracking (สูงสุด ~25 ครั้ง/วินาที/กล้อง) — ถ้าไม่ใส่ Continuation
        // หลัง await จะถูกดีดกลับไปรันบน UI thread ทุกครั้ง (WinForms SynchronizationContext)
        // พอมี 2 กล้องพร้อมกันจะยิงรวมกันหลายสิบครั้ง/วินาที ทำให้ UI thread รับงาน
        // ท่วมจน "ค้าง" ทั้งโปรแกรม (คือปัญหาที่เจอตอนต่อกล้อง 2 ตัว) ส่วนจุดที่ต้อง
        // แตะ UI จริง ๆ (SetPlateText/SetLprStage/this.Invoke) ยังมี Invoke/BeginInvoke
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
                        SetLprStage(camId, LprStage.Ocr);
                        content.Add(new ByteArrayContent(ms.ToArray()), "image", "frame.jpg");

                        // ส่งกรอบที่ /detect เพิ่งหาเจอไปด้วย ฝั่ง AI จะได้ไม่ต้องค้น
                        // ทั้งเฟรมใหม่ (imgsz=1280 + TTA ~330ms) แค่ค้นซ้ำรอบ ๆ กรอบนั้น
                        // (~23ms) วัดแล้วเร็วขึ้น 14 เท่าโดย conf แทบไม่ต่าง (0.826 vs 0.845)
                        Rectangle hintBox;
                        bool haveHint;
                        lock (boxLock)
                        {
                            haveHint = hasPlateBox[camId];
                            hintBox = latestPlateBox[camId];
                        }
                        if (haveHint && hintBox.Width > 0 && hintBox.Height > 0)
                        {
                            content.Add(new StringContent(hintBox.Left.ToString()), "bx1");
                            content.Add(new StringContent(hintBox.Top.ToString()), "by1");
                            content.Add(new StringContent(hintBox.Right.ToString()), "bx2");
                            content.Add(new StringContent(hintBox.Bottom.ToString()), "by2");
                        }

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
                            Console.WriteLine($"[predict] กล้อง{camId} อ่านได้ '{plateText}' conf {conf:F3}");

                            // นับยืนยันก่อน — ส่งเข้าระบบตัดสินเฉพาะเลขที่ชัวร์แล้วเท่านั้น
                            bool confirmed = ReleaseLprTurn(camId, plateText, conf, out bool plateChanged);
                            // อ่านได้คนละเลขกับของเดิม = คันก่อนหน้าไปแล้ว ผลเก่าที่ค้าง
                            // อยู่ในศูนย์ตัดสินใจใช้ไม่ได้อีกแล้ว ต้องล้างทันที ไม่งั้นถ้ามี
                            // บัตรแตะเข้ามาตอนนี้ จะเอาเลขของคันก่อนไปเทียบกับบัตร
                            if (plateChanged)
                            {
                                lock (hybridLock) { pendingPlateCam[camId] = ""; pendingPlateConf[camId] = 0; }
                            }
                            int seenTimes, needReads;
                            double sendConf;

                            // ⚠️ ลำดับตรงนี้สำคัญมาก — เคยเป็นบั๊ก "อ่านได้จากกล้องเดียว"
                            //
                            // อาการ: รอกล้องอีกตัวอยู่ พอกล้องนั้นอ่านได้ปุ๊บ ผลกลับขึ้นว่า
                            // อ่านได้จากกล้องเดียว ทั้งที่อ่านได้ทั้งคู่ (เกิดได้ทั้งหน้าและหลัง)
                            //
                            // สาเหตุ: เดิมตั้งธง plateSubmitted บนเธรดกล้อง แต่เลขทะเบียน
                            // ถูกส่งเข้าศูนย์ตัดสินใจ (OnPlateRead) ข้างใน BeginInvoke คือ
                            // ไปรอคิวของ UI thread ซึ่งตอนกล้องสองตัวทำงานพร้อมกันอาจหน่วง
                            // ได้เป็นร้อยมิลลิวินาที ระหว่างนั้นสถานะจะขัดกันเอง:
                            //     plateSubmitted[นี่] = true   ← ตั้งแล้ว
                            //     pendingPlateCam[นี่] = ""     ← ยังไม่ได้ตั้ง
                            // แล้ว ShouldWaitForOtherCam มีบรรทัด "ถ้าอีกกล้องส่งมาแล้ว
                            // ไม่ต้องรอ" มันจึงเลิกรอทันที ส่วน TryDecide อ่าน
                            // pendingPlateCam ได้ค่าว่าง → ตัดสินด้วยกล้องเดียว
                            // ตัวจับเวลาไฮบริดเต้นทุก 1 วินาที จึงมีโอกาสตกร่องนี้ได้เรื่อย ๆ
                            //
                            // แก้โดยบันทึกเลขเข้าศูนย์ตัดสินใจ "ก่อน" ตั้งธง plateSubmitted
                            // และทำบนเธรดนี้เลย ไม่ผ่าน BeginInvoke — ความจริงสองอย่างนี้
                            // จะได้ปรากฏพร้อมกันเสมอ ไม่มีช่วงที่ขัดกัน
                            // (OnPlateRead ที่เรียกทีหลังใน BeginInvoke เขียนค่าเดิมซ้ำ
                            //  ไม่มีผลข้างเคียง มีไว้ให้ส่วน UI กับ TryDecide ทำงานต่อ)
                            bool willSubmit;
                            lock (turnLock)
                            {
                                willSubmit = confirmed && !plateSubmitted[camId];
                                sendConf = bestConf[camId];
                            }
                            if (willSubmit && !string.IsNullOrWhiteSpace(plateText))
                            {
                                lock (hybridLock)
                                {
                                    pendingPlateCam[camId] = plateText.Trim();
                                    pendingPlateConf[camId] = sendConf;
                                    pendingPlateCamTime[camId] = DateTime.Now;
                                }
                            }

                            // ยืนยันแล้วให้ "ส่งครั้งเดียว" — ตั้งธงตรงนี้ใต้ล็อกเดียวกับที่อ่านค่า
                            // กันกรณีสองรอบอ่านจบพร้อมกันแล้วส่งซ้ำ
                            bool submitNow = false;
                            lock (turnLock)
                            {
                                seenTimes = confirmCount[camId];
                                needReads = neededReads[camId];
                                sendConf = bestConf[camId];
                                if (confirmed && !plateSubmitted[camId])
                                {
                                    plateSubmitted[camId] = true;
                                    plateSubmittedAt[camId] = DateTime.Now;
                                    submitNow = true;
                                }
                            }

                            // BeginInvoke ไม่ใช่ Invoke — Invoke จะบล็อกเธรดนี้รอ UI
                            // thread ว่าง ซึ่งตอนมี 2 กล้องทำงานพร้อมกัน UI thread ยุ่งมาก
                            // ทำให้งานอ่านป้ายค้างยาวและพลอยทำให้เฟรมกล้องกองคิวตามไปด้วย
                            // งานในนี้ไม่มีอะไรที่ต้องรอผลกลับ จึงยิงแล้วปล่อยได้เลย
                            this.BeginInvoke((MethodInvoker)delegate
                            {
                                SetPlateText(camId, plateText);
                                if (submitNow)
                                {
                                    // โชว์ conf ด้วย จะได้เห็นว่าค่าจริงจากกล้องนี้อยู่ราวไหน
                                    // (ใช้เทียบกับ submitConfMin ตอนจูนค่า)
                                    SetLprStage(camId, LprStage.OcrDone);
                                    SetLprStage(camId, LprStage.Confirmed);
                                    SetLprDetail(camId, $"ส่งให้ระบบตัดสินแล้ว (กล้อง{camName}, conf {sendConf:F2})");
                                    OnPlateRead(plateText, camId, sendConf);
                                }
                                else if (confirmed)
                                {
                                    SetLprStage(camId, LprStage.OcrDone);
                                    SetLprDetail(camId, $"อ่านได้แล้ว รอผลตัดสิน (กล้อง{camName})");
                                }
                                else
                                {
                                    // ยังไม่มั่นใจพอ — ค้างที่ขั้น "กำลังประมวลผล" แล้ววนอ่านต่อ
                                    SetLprDetail(camId, $"ยังไม่มั่นใจพอ อ่านซ้ำ {seenTimes}/{needReads} (กล้อง{camName})");
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
                            // อ่านไม่ออก — ถอยกลับไปขั้น "เจอป้าย" เพื่อให้วนอ่านใหม่
                            SetLprStage(camId, LprStage.PlateFound, rewind: true);
                            SetLprDetail(camId, $"{why} (กล้อง{camName2})");
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
            // เจอป้าย = ขึ้นขั้นที่ 1 (บรรทัดแรกเปลี่ยนเป็นเหลืองค้าง)
            // ป้ายหายไป = กลับไปจุดเริ่มต้น แต่ห้ามไปล้างของกล้องที่ส่งผลเข้าระบบ
            // ตัดสินไปแล้ว (ขั้น Confirmed) — ขั้นนั้นต้องค้างจนกว่าผลตัดสินจะออก
            if (seen) SetLprStage(camId, LprStage.PlateFound);
            else if (lprStage[camId] < LprStage.Confirmed) ResetLprStage(camId);
        }

        private void txtRTSP_TextChanged(object sender, EventArgs e)
        {

        }

        private void ShowNoSignal(PictureBox box, string msg)
        {
            // เรียกจากเธรดกล้อง — ใช้ BeginInvoke ไม่ให้เธรดค้างรอ UI (เหมือน ShowCameraPlaceholder)
            if (box.InvokeRequired) { box.BeginInvoke(new Action(() => ShowNoSignal(box, msg))); return; }
            Bitmap bmp = new Bitmap(Math.Max(box.Width, 320), Math.Max(box.Height, 240));
            using (Graphics g = Graphics.FromImage(bmp))
            using (var font = new System.Drawing.Font("Tahoma", 14, FontStyle.Bold))
            {
                g.Clear(Color.Black);
                SizeF sz = g.MeasureString(msg, font);
                g.DrawString(msg, font, Brushes.Red, (bmp.Width - sz.Width) / 2, (bmp.Height - sz.Height) / 2);
            }
            if (box.Image != null) box.Image.Dispose();
            box.Image = bmp;
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