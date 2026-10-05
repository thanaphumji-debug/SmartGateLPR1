namespace SmartGateLPR1
{
    internal static class Program
    {
        // บันทึก exception ที่ไม่มีใครดักไว้ก่อนโปรแกรมถูกปิดทิ้ง (ทั้งบน UI thread
        // และ background thread อื่น เช่น thread กล้อง/RFID) ไม่งั้นโปรแกรมจะปิดตัว
        // เองเงียบ ๆ โดยไม่เหลือร่องรอยให้ไล่หาสาเหตุได้เลย
        private static void LogCrash(Exception ex, string source)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
                File.AppendAllText(path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}]\r\n{ex}\r\n----------------------------\r\n");
            }
            catch { }
        }

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            // ต้องตั้งก่อน Application.Run เสมอ ไม่งั้น exception บน UI thread ที่ไม่มี
            // ใครดักไว้จะทำให้โปรแกรมปิดตัวเองทันทีแบบไม่มี error ให้เห็นเลย
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                LogCrash(e.Exception, "UI Thread");
                MessageBox.Show("เกิดข้อผิดพลาดที่ไม่คาดคิด โปรแกรมจะพยายามทำงานต่อ\n" +
                                 "(รายละเอียดถูกบันทึกไว้ที่ crash_log.txt)\n\n" + e.Exception.Message,
                                 "ข้อผิดพลาด", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                LogCrash(e.ExceptionObject as Exception, "Background Thread (fatal)");
            };

            try { Db.Configure(); }
            catch (Exception ex)
            {
                MessageBox.Show("อ่านการตั้งค่าที่เก็บข้อมูลไม่ได้ จะใช้ฐานข้อมูลในเครื่องไปก่อน\n\n" + ex.Message,
                                "ตั้งค่าที่เก็บข้อมูล", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            // บังคับ RTSP ผ่าน TCP + โหมดหน่วงต่ำ (ห้ามใส่ buffer_size ใหญ่ จะยิ่งดีเลย์)
            Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS",
                "rtsp_transport;tcp|fflags;nobuffer|flags;low_delay|max_delay;0|reorder_queue_size;0|stimeout;5000000");
            var st0 = SettingsStore.Load();
            if (!st0.AcceptedEula)
            {
                using (var eula = new EulaForm())
                {
                    if (eula.ShowDialog() != DialogResult.OK) return;   // ไม่ยอมรับ = ปิดโปรแกรม
                }
                st0.AcceptedEula = true;
                SettingsStore.Save(st0);
            }
            Application.Run(new btnDisconnectRFID());
        }
    }
}