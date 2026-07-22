Using System;
Using System.Drawing;
Using System.Threading;
Using System.Windows.Forms;
Using OpenCvSharp;
Using OpenCvSharp.Extensions; // สำคัญสำหรับการแปลงภาพ

Namespace SmartGateLPR1
{
    Partial Public Class Form1 :  Form
    {
        // สร้างตัวแปรสำหรับเก็บ Thread และสถานะการทำงาน
        Private Thread cameraThread;
        Private bool isCameraRunning = False;

        Public Form1()
        {
            InitializeComponent();
            InitSideMenu();
            LoadSavedSettings();
        }

        Private void btnStartCamera_Click(Object sender, EventArgs e)
        {
            // ถ้ากล้องทำงานอยู่แล้ว ให้หยุดก่อน (ป้องกันกดซ้ำ)
            If (isCameraRunning) Return;

            // เอา Link RTSP มาจาก TextBox (ถ้าว่าง ให้ใส่ค่า Default)
            String rtspUrl = txtRTSP.Text; 
            If (String.IsNullOrEmpty(rtspUrl))
            {
                // ตัวอย่าง Link RTSP (ต้องแก้เป็นของจริงของคุณ)
                // รูปแบบมักจะเป็น rtsp://admin:password@192.168.1.xxx:554/...
                rtspUrl = "rtsp://Thanaphum:Thanaphum48.@192.168.1.64:194/stream1"; 
            }

            isCameraRunning = true;
            
            // สั่งให้เริ่มทำงานใน Thread ใหม่ (เพื่อไม่ให้หน้าจอค้าง)
            cameraThread = New Thread(() => CaptureCamera(rtspUrl));
            cameraThread.IsBackground = true;
            cameraThread.Start();
        }

        Private void CaptureCamera(String url)
        {
            // 1. เชื่อมต่อกล้อง
            VideoCapture capture = New VideoCapture(url);

            If (!capture.IsOpened())
            {
                MessageBox.Show("เชื่อมต่อกล้องไม่ได้! เช็ค IP/User/Pass");
                isCameraRunning = false;
                Return;
            }

            // 2. วนลูปดึงภาพ
            Mat frame = New Mat(); // ตัวเก็บภาพดิบ
            While (isCameraRunning)
            {
                capture.Read(frame); // อ่านภาพ 1 เฟรม
                
                If (!frame.Empty())
                {
                    // แปลงภาพจาก OpenCV เป็น Bitmap ของ Windows
                    Bitmap image = BitmapConverter.ToBitmap(frame);

                    // ส่งรูปไปแสดงที่ PictureBox (ต้องใช้ Invoke เพราะอยู่คนละ Thread)
                    If (pbCamera1.Image! = null) pbCamera1.Image.Dispose(); // เคลียร์รูปเก่าเพื่อคืน Ram
                    
                    pbCamera1.Invoke(New Action(() => 
                    {
                        pbCamera1.Image = image; 
                    }));
                }
                
                // พักนิดนึงเพื่อไม่ให้กิน CPU 100%
                Thread.Sleep(30); 
            }

            // 3. ปิดการเชื่อมต่อเมื่อหยุดลูป
            capture.Release();
        }

        // เพิ่ม Event นี้เพื่อให้ปิดโปรแกรมแล้ว Thread จบสมบูรณ์
        Private void Form1_FormClosing(Object sender, FormClosingEventArgs e)
        {
            isCameraRunning = false; // สั่งหยุดลูป
            If (cameraThread! = null && cameraThread.IsAlive)
            {
                cameraThread.Join(500); // รอให้ Thread จบ
            }
        }

        Private void InitSideMenu()
        {
            // ปุ่ม ☰ มุมบนซ้าย
            btnMenu = New Button
            {
                Text = "☰",
                Font = New System.Drawing.Font("Segoe UI", 14, FontStyle.Bold),
                Size = New System.Drawing.Size(44, 36),
                Location = New System.Drawing.Point(8, 8),
                FlatStyle = FlatStyle.Flat,
            };
            btnMenu.FlatAppearance.BorderSize = 0;
            btnMenu.Click += (s, e) => ToggleMenu();

            // แผงเมนูซ้าย เริ่มกว้าง 0 (ซ่อนอยู่)
            panelMenu = New Panel
            {
                Width = 0,
                Dock = DockStyle.Left,
                BackColor = Color.FromArgb(45, 45, 48),
            };

            var lblTitle = New Label
            {
                Text = "การตั้งค่าอุปกรณ์",
                ForeColor = Color.White,
                Font = New System.Drawing.Font("Tahoma", 11, FontStyle.Bold),
                Location = New System.Drawing.Point(16, 55),
                AutoSize = true,
            };
            panelMenu.Controls.Add(lblTitle);

            panelMenu.Controls.Add(MakeMenuButton("📷  ตั้งค่ากล้อง", 100, (s, e) =>
            {
                ToggleMenu();
                Using (var f = New CameraSettingsForm())
                    If (f.ShowDialog(this) == DialogResult.OK) LoadSavedSettings();
            }));
            panelMenu.Controls.Add(MakeMenuButton("📡  ตั้งค่า RFID", 150, (s, e) =>
            {
                ToggleMenu();
                MessageBox.Show("หน้าตั้งค่า RFID — เดี๋ยวทำตามแบบ CameraSettingsForm");
            }));
            panelMenu.Controls.Add(MakeMenuButton("🚧  ตั้งค่าไม้กั้น", 200, (s, e) =>
            {
                ToggleMenu();
                MessageBox.Show("หน้าตั้งค่าไม้กั้น — เดี๋ยวทำตามแบบ CameraSettingsForm");
            }));

            Controls.Add(panelMenu);
            Controls.Add(btnMenu);
            btnMenu.BringToFront();

            // ตัวทำอนิเมชันสไลด์
            menuTimer = New System.Windows.Forms.Timer { Interval = 10 };
            menuTimer.Tick += (s, e) =>
            {
                If (menuOpening)
                {
                    panelMenu.Width += 25;
                    If (panelMenu.Width >= MenuWidth) { panelMenu.Width = MenuWidth; menuTimer.Stop(); }
                }
                Else
                {
                    panelMenu.Width -= 25;
                    If (panelMenu.Width <= 0) { panelMenu.Width = 0; menuTimer.Stop(); }
                }
            };
        }

        Private Button MakeMenuButton(String text, int top, EventHandler onClick)
        {
            var b = New Button
            {
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = New System.Drawing.Size(MenuWidth - 20, 40),
                Location = New System.Drawing.Point(10, top),
                Font = New System.Drawing.Font("Tahoma", 10),
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += onClick;
            Return b;
        }

        Private void ToggleMenu()
        {
            menuOpening = panelMenu.Width < MenuWidth / 2;
            panelMenu.BringToFront();
            btnMenu.BringToFront();
            menuTimer.Start();
        }

        Private void LoadSavedSettings()
        {
            var st = SettingsStore.Load();
            // ⚠️ เปลี่ยน txtRtsp1 / txtRtsp2 เป็น "ชื่อจริง" ของช่องกรอก RTSP สองช่องในฟอร์มคุณ
            If (!string.IsNullOrEmpty(st.RtspCamera1)) txtRTSP.Text = st.RtspCamera1;
            If (!string.IsNullOrEmpty(st.RtspCamera2)) txtRTSP2.Text = st.RtspCamera2;
        }
    }
}