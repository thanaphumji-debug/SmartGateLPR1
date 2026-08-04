namespace SmartGateLPR1
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            try { Db.Configure(); }
            catch (Exception ex)
            {
                MessageBox.Show("อ่านการตั้งค่าที่เก็บข้อมูลไม่ได้ จะใช้ฐานข้อมูลในเครื่องไปก่อน\n\n" + ex.Message,
                                "ตั้งค่าที่เก็บข้อมูล", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            // บังคับ RTSP ผ่าน TCP + โหมดหน่วงต่ำ (ห้ามใส่ buffer_size ใหญ่ จะยิ่งดีเลย์)
            Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS",
                "rtsp_transport;tcp|fflags;nobuffer|flags;low_delay|max_delay;0|reorder_queue_size;0|stimeout;5000000");
            Application.Run(new btnDisconnectRFID());
        }
    }
}