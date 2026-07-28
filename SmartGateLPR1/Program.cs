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
            Application.Run(new btnDisconnectRFID());
        }
    }
}