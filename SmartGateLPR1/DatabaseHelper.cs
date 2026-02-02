using System;
using System.Data;
using System.Data.SQLite; // ต้องมีบรรทัดนี้ (ถ้าแดง ให้เช็คว่าลง NuGet System.Data.SQLite หรือยัง)
using System.IO;

namespace SmartGateLPR1
{
    public class DatabaseHelper
    {
        // ชื่อไฟล์ Database จะถูกสร้างในโฟลเดอร์ debug/bin ของโปรเจค
        private string dbFile = "smartgate.db";
        private string connectionString;

        public DatabaseHelper()
        {
            connectionString = $"Data Source={dbFile};Version=3;";
            InitializeDatabase();
        }

        // 1. สร้างตารางถ้ายังไม่มี (รันครั้งแรกจะสร้างไฟล์ให้เอง)
        private void InitializeDatabase()
        {
            if (!File.Exists(dbFile))
            {
                SQLiteConnection.CreateFile(dbFile);
            }

            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = @"
                    -- ตารางเก็บรายชื่อรถที่อนุญาต (Whitelist)
                    CREATE TABLE IF NOT EXISTS tb_users (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        plate_number TEXT NOT NULL,
                        rfid_code TEXT,
                        owner_name TEXT
                    );

                    -- ตารางเก็บประวัติการเข้าออก (Logs)
                    CREATE TABLE IF NOT EXISTS tb_logs (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        access_time DATETIME DEFAULT CURRENT_TIMESTAMP,
                        plate_read TEXT,
                        rfid_read TEXT,
                        image_path TEXT,
                        status TEXT -- 'ALLOWED' หรือ 'DENIED'
                    );
                ";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // 2. ฟังก์ชันเพิ่มรถเข้าในระบบ (เอาไว้ทดสอบ)
        public void AddUser(string plate, string rfid, string name)
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = "INSERT INTO tb_users (plate_number, rfid_code, owner_name) VALUES (@p, @r, @n)";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@p", plate);
                    cmd.Parameters.AddWithValue("@r", rfid);
                    cmd.Parameters.AddWithValue("@n", name);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // 3. ฟังก์ชันตรวจสอบสิทธิ์ (Check Access)
        // คืนค่าเป็น ชื่อเจ้าของรถ ถ้าเจอ, คืนค่า null ถ้าไม่เจอ
        public string CheckPermission(string plate, string rfid)
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                // ค้นหาว่ามีทะเบียนนี้ หรือ RFID นี้ ในระบบไหม
                string sql = "SELECT owner_name FROM tb_users WHERE plate_number = @p OR rfid_code = @r";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@p", plate);
                    cmd.Parameters.AddWithValue("@r", rfid);

                    var result = cmd.ExecuteScalar(); // ดึงผลลัพธ์ช่องแรก
                    if (result != null)
                    {
                        return result.ToString(); // เจอ! คืนชื่อเจ้าของ
                    }
                }
            }
            return null; // ไม่เจอ
        }

        // 4. ฟังก์ชันบันทึก Log
        public void SaveLog(string plate, string rfid, string imagePath, string status)
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = "INSERT INTO tb_logs (plate_read, rfid_read, image_path, status) VALUES (@p, @r, @img, @st)";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@p", plate);
                    cmd.Parameters.AddWithValue("@r", rfid);
                    cmd.Parameters.AddWithValue("@img", imagePath);
                    cmd.Parameters.AddWithValue("@st", status);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // 2. ฟังก์ชันลบข้อมูลตาม RFID
        public void DeleteUser(string rfid)
        {
            using (SQLiteConnection conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = "DELETE FROM users WHERE rfid_tag = @rfid";
                using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@rfid", rfid);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // 1. ฟังก์ชันดึงรายชื่อทั้งหมด (เอาไปใส่ DataGridView)
        public DataTable GetAllUsers()
        {
            DataTable dt = new DataTable();
            using (SQLiteConnection conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = "SELECT * FROM users"; // ตรวจสอบชื่อตารางให้ตรงกับที่คุณสร้างไว้นะครับ
                using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                {
                    using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(cmd))
                    {
                        adapter.Fill(dt);
                    }
                }
            }
            return dt;
        }
    }
}
