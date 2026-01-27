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
                    If (pbCamera1.Image!= null) pbCamera1.Image.Dispose(); // เคลียร์รูปเก่าเพื่อคืน Ram
                    
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
            If (cameraThread!= null && cameraThread.IsAlive)
            {
                cameraThread.Join(500); // รอให้ Thread จบ
            }
        }
    }
}