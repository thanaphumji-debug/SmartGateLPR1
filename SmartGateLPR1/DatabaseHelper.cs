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
            // 1.ดึงค่าที่อยู่ที่บันทึกไว้
            string savedPath = SettingsStore.Load().DbLocalPath;

            // 2. ถ้ายังไม่เคยเลือก (ค่าว่าง) ให้ใช้ค่าเริ่มต้นคือโฟลเดอร์ปัจจุบัน
            if (string.IsNullOrEmpty(savedPath))
            {
                savedPath = "smartgate.db";
            }

            // 3. กำหนด Connection String ไปที่นั่น
            connectionString = $"Data Source={savedPath};Version=3;";

            // 4. สร้างตาราง (ถ้ายังไม่มี)
            InitializeDatabase();
        }

        // --- ส่วนฟังก์ชันสร้างตาราง (เพิ่มเข้าไป) ---
        private void InitializeDatabase()
        {
            using (SQLiteConnection conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                // สร้างตาราง users โดยมี 4 คอลัมน์: id, plate_number, rfid_tag, owner_name
                string sql = @"
                CREATE TABLE IF NOT EXISTS users (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    plate_number TEXT,
                    rfid_tag TEXT UNIQUE,
                    owner_name TEXT
                )";
                using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.ExecuteNonQuery();
                }

                EnsureNewColumns(conn);   // ⬅️ เพิ่ม
            }
        }

        // เพิ่มคอลัมน์ใหม่ให้ตารางเก่า (รันซ้ำได้ ถ้ามีอยู่แล้วจะข้ามเอง)
        private void EnsureNewColumns(SQLiteConnection conn)
        {
            string[] adds = {
                "ALTER TABLE users ADD COLUMN plate_letters TEXT",
                "ALTER TABLE users ADD COLUMN plate_digits TEXT",
                "ALTER TABLE users ADD COLUMN province TEXT",
                "ALTER TABLE users ADD COLUMN permission TEXT",
            };
            foreach (string a in adds)
            {
                try { using (var c = new SQLiteCommand(a, conn)) c.ExecuteNonQuery(); }
                catch { /* คอลัมน์มีอยู่แล้ว ข้ามไป */ }
            }
        }

        // 2. ฟังก์ชันเพิ่มรถเข้าในระบบ (เอาไว้ทดสอบ)
        public void AddUser(string plate, string rfid, string name)
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = "INSERT INTO users (plate_number, rfid_tag, owner_name) VALUES (@p, @r, @n)";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@p", plate);
                    cmd.Parameters.AddWithValue("@r", rfid);
                    cmd.Parameters.AddWithValue("@n", name);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // เพิ่มรถแบบข้อมูลครบ (ตารางบันทึกทะเบียนใหม่)
        public void AddUserFull(string letters, string digits, string province,
                                string permission, string rfid, string ownerName = "")
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = @"INSERT INTO users
                    (plate_number, plate_letters, plate_digits, province, permission, rfid_tag, owner_name)
                    VALUES (@pn, @pl, @pd, @pv, @pm, @r, @n)";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@pn", letters + digits);  // รวมไว้ให้ LPR เทียบ
                    cmd.Parameters.AddWithValue("@pl", letters);
                    cmd.Parameters.AddWithValue("@pd", digits);
                    cmd.Parameters.AddWithValue("@pv", province);
                    cmd.Parameters.AddWithValue("@pm", permission);
                    cmd.Parameters.AddWithValue("@r", rfid);
                    cmd.Parameters.AddWithValue("@n", ownerName);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void DeleteUserById(long id)
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("DELETE FROM users WHERE id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
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

        // ฟังก์ชันสำหรับค้นหาข้อมูลจากเลข RFID (ใช้ในหน้า Form1)
        public DataTable GetUserByTag(string tag)
        {
            using (SQLiteConnection conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = "SELECT * FROM users WHERE rfid_tag = @tag";
                using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@tag", tag);

                    using (SQLiteDataAdapter da = new SQLiteDataAdapter(cmd))
                    {
                        DataTable dt = new DataTable();
                        da.Fill(dt);
                        return dt; // ส่งคืนผลลัพธ์ (ถ้าเจอจะมี 1 แถว, ไม่เจอคือว่างเปล่า)
                    }
                }
            }
        }

    }
}
