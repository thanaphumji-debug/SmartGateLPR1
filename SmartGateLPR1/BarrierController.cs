using System;
using System.IO.Ports;

namespace SmartGateLPR1
{
    /// <summary>ชั้นกลางควบคุมไม้กั้น — สลับวิธีเชื่อมต่อได้โดยโค้ดส่วนอื่นไม่ต้องรู้</summary>
    public interface IBarrier : IDisposable
    {
        string Describe { get; }
        void Open();
        void Close();
        string Test();          // คืน "OK|ข้อความ" หรือ "ERR|ข้อความ"
    }

    // ---------- 1) โหมดจำลอง: ไม่มีฮาร์ดแวร์ ใช้ไฟบนหน้าจออย่างเดียว ----------
    public class SimulatedBarrier : IBarrier
    {
        public string Describe => "โหมดจำลอง (แสดงผลบนหน้าจอเท่านั้น)";
        public void Open() { }
        public void Close() { }
        public string Test() => "OK|โหมดจำลองพร้อมใช้งาน (ไม่ได้ต่อฮาร์ดแวร์จริง)";
        public void Dispose() { }
    }

    // ---------- 2) บอร์ดรีเลย์ผ่านพอร์ตอนุกรม / USB ----------
    public class SerialBarrier : IBarrier
    {
        private SerialPort port;
        private readonly string com, openHex, closeHex;
        private readonly int baud;

        public SerialBarrier(string com, int baud, string openHex, string closeHex)
        {
            this.com = com; this.baud = baud;
            this.openHex = openHex; this.closeHex = closeHex;
        }

        public string Describe => $"บอร์ดรีเลย์ผ่าน {com} ({baud} bps)";

        private void EnsureOpen()
        {
            if (port == null) port = new SerialPort(com, baud);
            if (!port.IsOpen) port.Open();
        }

        private static byte[] ParseHex(string s)
        {
            string[] parts = (s ?? "").Split(new[] { ' ', ',', '-' }, StringSplitOptions.RemoveEmptyEntries);
            byte[] b = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++) b[i] = Convert.ToByte(parts[i], 16);
            return b;
        }

        public void Open()
        {
            try { EnsureOpen(); byte[] d = ParseHex(openHex); port.Write(d, 0, d.Length); }
            catch (Exception ex) { Console.WriteLine("สั่งเปิดไม้กั้นไม่ได้: " + ex.Message); }
        }

        public void Close()
        {
            try { EnsureOpen(); byte[] d = ParseHex(closeHex); port.Write(d, 0, d.Length); }
            catch (Exception ex) { Console.WriteLine("สั่งปิดไม้กั้นไม่ได้: " + ex.Message); }
        }

        public string Test()
        {
            try
            {
                EnsureOpen();
                Open();
                System.Threading.Thread.Sleep(600);
                Close();
                return $"OK|เชื่อมต่อ {com} สำเร็จ และส่งคำสั่งทดสอบแล้ว\n(ฟังเสียงรีเลย์คลิก 2 ครั้ง)";
            }
            catch (Exception ex)
            {
                return "ERR|เปิดพอร์ต " + com + " ไม่ได้\n\nตรวจสอบ:\n" +
                       "• เสียบสาย USB ของบอร์ดรีเลย์แล้วหรือยัง\n" +
                       "• เลือกหมายเลข COM ถูกต้องไหม (ดูใน Device Manager)\n" +
                       "• มีโปรแกรมอื่นเปิดพอร์ตนี้ค้างอยู่หรือเปล่า\n\n(" + ex.Message + ")";
            }
        }

        public void Dispose()
        {
            try { if (port != null) { if (port.IsOpen) port.Close(); port.Dispose(); } } catch { }
        }
    }

    // ---------- ตัวสร้างตามค่าที่ตั้งไว้ ----------
    public static class BarrierFactory
    {
        public static IBarrier Create(AppSettings st = null)
        {
            st = st ?? SettingsStore.Load();
            switch ((st.BarrierMode ?? "simulate").ToLower())
            {
                case "serial":
                    return new SerialBarrier(st.BarrierComPort, st.BarrierBaudRate,
                                             st.BarrierOpenCmd, st.BarrierCloseCmd);
                default:
                    return new SimulatedBarrier();
            }
        }
    }
}