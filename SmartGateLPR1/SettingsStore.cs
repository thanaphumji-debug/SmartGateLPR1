using System;
using System.IO;
using Newtonsoft.Json;

namespace SmartGateLPR1
{
    public class AppSettings
    {
        public string RtspCamera1 { get; set; } = "";
        public string RtspCamera2 { get; set; } = "";
        public string RfidIp { get; set; } = "";
        public int RfidPort { get; set; } = 23;
        public string BarrierIp { get; set; } = "";
        public int BarrierPort { get; set; } = 0;
        public string RfidUser { get; set; } = "alien";        // ⬅️ เพิ่ม
        public string RfidPassword { get; set; } = "password"; // ⬅️ เพิ่ม
        // --- ที่เก็บฐานข้อมูล ---
        public string StorageMode { get; set; } = "local";   // "local" หรือ "cloud"
        public string DbLocalPath { get; set; } = "";        // ว่าง = ข้าง .exe ตามเดิม
        public string DbHost { get; set; } = "";
        public int DbPort { get; set; } = 3306;
        public string DbName { get; set; } = "";
        public string DbUser { get; set; } = "";
        public string DbPassword { get; set; } = "";
        public string CloudType { get; set; } = "MySQL";      // MySQL / PostgreSQL / Nextcloud / S3
        public bool CloudUseSsl { get; set; } = true;
        public string LogImageDir { get; set; } = "";        // โฟลเดอร์เก็บภาพประวัติ (ว่าง = ข้างไฟล์ฐานข้อมูล)
        // --- เงื่อนไขการอนุญาตเข้า-ออก ---
        public bool RequireRfid { get; set; } = true;
        public bool AllowNoPlate { get; set; } = true;
        public bool RequirePlatesAgree { get; set; } = false;
        public bool AllowPlateTagMismatch { get; set; } = false;
    }

    public static class SettingsStore
    {
        private static readonly string FilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch { }
            return new AppSettings();
        }

        public static void Save(AppSettings s)
        {
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(s, Formatting.Indented));
        }
    }
}