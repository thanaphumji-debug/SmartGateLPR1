using System;
using System.Text;
using System.Net.Sockets;
using System.Threading;

namespace SmartGateLPR
{
    // Class นี้ทำหน้าที่คุยกับ Alien RFID โดยเฉพาะ
    public class SimpleTelnet
    {
        private TcpClient client;
        private NetworkStream stream;
        private bool isConnected = false;

        public bool Connect(string ip, int port)
        {
            try
            {
                client = new TcpClient();
                client.Connect(ip, port); // เชื่อมต่อ
                stream = client.GetStream();
                stream.ReadTimeout = 3000; // รออ่านนานสุด 3 วิ (ถ้าเกินถือว่าหลุด)
                isConnected = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ฟังก์ชันสำคัญ: รออ่านจนกว่าจะเจอคำที่ต้องการ (เช่น รอคำว่า "Username>")
        public string WaitFor(string keyword)
        {
            if (!isConnected) return "";

            StringBuilder sb = new StringBuilder();
            byte[] buffer = new byte[1]; // อ่านทีละตัวอักษรเพื่อความชัวร์
            DateTime startTime = DateTime.Now;

            try
            {
                while (client.Connected && (DateTime.Now - startTime).TotalSeconds < 5) // รอไม่เกิน 5 วิ
                {
                    if (stream.DataAvailable)
                    {
                        int read = stream.Read(buffer, 0, 1);
                        if (read > 0)
                        {
                            char c = (char)buffer[0];
                            sb.Append(c);

                            // ถ้าเจอคำที่ต้องการแล้ว ให้หยุดรอและส่งค่ากลับ
                            if (sb.ToString().Contains(keyword))
                            {
                                return sb.ToString();
                            }
                        }
                    }
                    else
                    {
                        Thread.Sleep(10); // พักนิดนึงถ้ารอข้อมูล
                    }
                }
            }
            catch { }

            return sb.ToString(); // คืนค่าเท่าที่อ่านได้ (อาจจะไม่เจอ keyword)
        }

        // ฟังก์ชันส่งคำสั่ง
        public void Send(string message)
        {
            if (!isConnected) return;
            byte[] data = Encoding.ASCII.GetBytes(message + "\r\n"); // Telnet ต้องจบด้วย \r\n
            stream.Write(data, 0, data.Length);
        }

        // ฟังก์ชัน Login แบบ Alien Style
        public bool Login(string user, string pass)
        {
            try
            {
                // 1. รอเครื่องถามหา Username
                string response = WaitFor("Username>");
                if (!response.Contains("Username>")) return false; // ถ้าไม่ถาม ก็ไม่ตอบ

                // 2. ส่ง User
                Send(user);

                // 3. รอเครื่องถามหา Password
                response = WaitFor("Password>");
                if (!response.Contains("Password>")) return false;

                // 4. ส่ง Pass
                Send(pass);

                // 5. รอข้อความต้อนรับ (ปกติ Alien จะจบด้วยเครื่องหมาย >)
                response = WaitFor(">");
                return true; // Login ผ่าน
            }
            catch
            {
                return false;
            }
        }

        public void Disconnect()
        {
            isConnected = false;
            if (client != null) client.Close();
        }

        public bool IsConnected => isConnected;
    }
}