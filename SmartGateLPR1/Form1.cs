using OpenCvSharp;
using OpenCvSharp.Extensions;
using SmartGateLPR1;
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using System.Net.Sockets; // สำหรับ TCP
using System.Text;        // สำหรับแปลง bytes เป็น string

namespace SmartGateLPR1
{
    public partial class Form1 : Form
    {
        // --- 1. ประกาศตัวแปรแยกสำหรับกล้อง 2 ตัว ---
        private Thread threadCam1;
        private Thread threadCam2;
        // --- ส่วนประกาศตัวแปร RFID ---
        private TcpClient rfidClient;
        private NetworkStream rfidStream;
        private Thread rfidThread;

        private bool isRfidRunning = false;
        private bool isCam1Running = false;
        private bool isCam2Running = false;

        private DatabaseHelper db;

        public Form1()
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
            if (rfidClient != null) rfidClient.Close();
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
            {
                if (isRfidRunning) return; // ถ้าเชื่อมอยู่แล้ว ห้ามกดซ้ำ

                string ip = txtRfidIP.Text;
                int port;

                // แปลง Port เป็นตัวเลข
                if (!int.TryParse(txtRfidPort.Text, out port))
                {
                    MessageBox.Show("Port ต้องเป็นตัวเลขเท่านั้น");
                    return;
                }

                try
                {
                    // เริ่มเชื่อมต่อ
                    rfidClient = new TcpClient();
                    rfidClient.Connect(ip, port); // ถ้า Connect ไม่ได้จะ Error ตรงนี้

                    // ถ้าผ่านบรรทัดบนมาได้ แสดงว่าเชื่อมติด
                    isRfidRunning = true;
                    rfidStream = rfidClient.GetStream();

                    txtRfidPort.Text = "สถานะ: เชื่อมต่อแล้ว";
                    txtRfidPort.ForeColor = Color.Green;
                    btnConnectRFID.Enabled = false; // ปิดปุ่มไม่ให้กดซ้ำ

                    // แยก Thread ไปรอรับข้อมูล (เพราะ RFID ส่งมาเมื่อไหร่ไม่รู้)
                    rfidThread = new Thread(ReadRfidLoop);
                    rfidThread.IsBackground = true;
                    rfidThread.Start();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"เชื่อมต่อ RFID ไม่ได้: {ex.Message}\n(เช็ค IP, Port และสาย LAN)");
                }
            }
        }

        private void ReadRfidLoop()
        {
            try
            {
                // 1. เตรียม Stream สำหรับอ่าน/เขียน
                NetworkStream stream = rfidClient.GetStream();
                byte[] buffer = new byte[1024];

                // ฟังก์ชันช่วยส่งข้อความ (Local Function)
                void SendCommand(string cmd)
                {
                    byte[] cmdBytes = Encoding.ASCII.GetBytes(cmd + "\r\n"); // ต้องปิดท้ายด้วย \r\n
                    stream.Write(cmdBytes, 0, cmdBytes.Length);
                }

                // --- ขั้นตอน Login (สำคัญมากสำหรับ Alien) ---
                // รอรับคำว่า "Username>" แล้วส่ง "alien"
                // รอรับคำว่า "Password>" แล้วส่ง "password"
                // แต่เพื่อความง่าย เราจะส่งรวดเดียวแล้วรอเคลียร์ Buffer

                Thread.Sleep(500); // รอเครื่องพร้อมนิดนึง
                SendCommand("alien");
                Thread.Sleep(100);
                SendCommand("password");
                Thread.Sleep(500); // รอ Login สำเร็จ

                // เคลียร์ข้อความต้อนรับทิ้งไปก่อน
                if (stream.DataAvailable) stream.Read(buffer, 0, buffer.Length);

                // --- เข้าสู่ลูปถามข้อมูล (Polling) ---
                while (isRfidRunning && rfidClient.Connected)
                {
                    // 2. ส่งคำสั่ง "ขอรายชื่อ Tag เดี๋ยวนี้!"
                    SendCommand("Get TagList");

                    // 3. รออ่านคำตอบ
                    Thread.Sleep(200); // พักรอเครื่องประมวลผล (ปรับเร็ว/ช้าได้ตรงนี้)

                    if (stream.DataAvailable)
                    {
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

                        // Alien จะตอบมาประมาณว่า:
                        // "Tag:3000E200..." หรือ "(No Tags)"

                        // 4. แยกบรรทัดและกรองเอาเฉพาะเลข Tag
                        string[] lines = response.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                        foreach (string line in lines)
                        {
                            if (line.StartsWith("Tag:"))
                            {
                                // ตัดคำว่า "Tag:" ออก เหลือแต่เลข
                                // รูปแบบ: Tag:300833B2DDD9014000000000, Disc:2023...
                                // เราจะเอาแค่เลขข้างหน้า Comma
                                string rawTag = line.Substring(4).Trim();
                                string tagId = rawTag.Split(',')[0].Trim();

                                // ส่งไปโชว์หน้าจอ
                                this.Invoke(new Action(() =>
                                {
                                    txtRFIDInput.Text = tagId;

                                    // (Optional) ถ้าต้องการเช็คสิทธิ์ทันที ให้เปิดบรรทัดล่าง
                                    // btnCheckData.PerformClick();
                                }));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle Error
                this.Invoke(new Action(() => MessageBox.Show("RFID Error: " + ex.Message)));
            }
            finally
            {
                isRfidRunning = false;
                this.Invoke(new Action(() =>
                {
                    lblRfidStatus1.Text = "สถานะ: หลุดการเชื่อมต่อ";
                    lblRfidStatus1.ForeColor = Color.Red;
                    btnConnectRFID.Enabled = true;
                }));
            }
        }

        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void groupBox2_Enter(object sender, EventArgs e)
        {

        }
    }
}