using System;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite; // ต้องมีบรรทัดนี้ (ถ้าแดง ให้เช็คว่าลง NuGet System.Data.SQLite หรือยัง)
using System.IO;

namespace SmartGateLPR1
{
    public class DatabaseHelper
    {
        private string connectionString;
        private string dbPath;            // path จริงของไฟล์ .db (ใช้หาโฟลเดอร์เก็บภาพ)

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
            dbPath = System.IO.Path.GetFullPath(savedPath);
            connectionString = $"Data Source={savedPath};Version=3;";

            // 4. สร้างตาราง (ถ้ายังไม่มี)
            InitializeDatabase();
            EnsureAccessLogTable();
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

        // แก้ไขข้อมูลรถที่บันทึกไว้แล้ว (ตาม id)
        public void UpdateUserFull(long id, string letters, string digits, string province,
                                   string permission, string rfid)
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = @"UPDATE users SET
                        plate_number = @pn, plate_letters = @pl, plate_digits = @pd,
                        province = @pv, permission = @pm, rfid_tag = @r
                    WHERE id = @id";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@pn", letters + digits);
                    cmd.Parameters.AddWithValue("@pl", letters);
                    cmd.Parameters.AddWithValue("@pd", digits);
                    cmd.Parameters.AddWithValue("@pv", province);
                    cmd.Parameters.AddWithValue("@pm", permission);
                    cmd.Parameters.AddWithValue("@r", rfid);
                    cmd.Parameters.AddWithValue("@id", id);
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
                string sql = "SELECT * FROM users WHERE REPLACE(REPLACE(UPPER(rfid_tag),' ',''),'-','') = @tag";
                using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@tag", (tag ?? "").Replace(" ", "").Replace("-", "").ToUpper());

                    using (SQLiteDataAdapter da = new SQLiteDataAdapter(cmd))
                    {
                        DataTable dt = new DataTable();
                        da.Fill(dt);
                        return dt; // ส่งคืนผลลัพธ์ (ถ้าเจอจะมี 1 แถว, ไม่เจอคือว่างเปล่า)
                    }
                }
            }
        }

        public DataTable GetUserByPlate(string plateNorm)
        {
            using (SQLiteConnection conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = "SELECT * FROM users WHERE REPLACE(REPLACE(plate_number,' ',''),'-','') = @p";
                using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@p", plateNorm);
                    using (SQLiteDataAdapter da = new SQLiteDataAdapter(cmd))
                    {
                        DataTable dt = new DataTable();
                        da.Fill(dt);
                        return dt;
                    }
                }
            }
        }

        // ===== ข้อมูลเจ้าของรถ (เล่มทะเบียน) เก็บแยกตาราง ผูกกับ user_id =====
        public void EnsureVehicleDetailsTable()
        {
            using (SQLiteConnection conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = @"CREATE TABLE IF NOT EXISTS vehicle_details (
                    user_id INTEGER PRIMARY KEY,
                    veh_type TEXT, brand TEXT, model_year TEXT, chassis_no TEXT,
                    engine_brand TEXT, engine_no TEXT,
                    owner_name TEXT, owner_addr TEXT, owner_birth TEXT, owner_nationality TEXT,
                    holder_name TEXT, holder_addr TEXT, holder_birth TEXT, holder_nationality TEXT )";
                using (SQLiteCommand cmd = new SQLiteCommand(sql, conn)) cmd.ExecuteNonQuery();
            }
        }

        public DataRow GetVehicleDetails(long userId)
        {
            using (SQLiteConnection conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                DataTable dt = new DataTable();
                using (SQLiteCommand cmd = new SQLiteCommand("SELECT * FROM vehicle_details WHERE user_id=@id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", userId);
                    using (SQLiteDataAdapter da = new SQLiteDataAdapter(cmd)) da.Fill(dt);
                }
                return dt.Rows.Count > 0 ? dt.Rows[0] : null;
            }
        }

        public void SaveVehicleDetails(long userId, Dictionary<string, string> f)
        {
            using (SQLiteConnection conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = @"INSERT INTO vehicle_details
                    (user_id, veh_type, brand, model_year, chassis_no, engine_brand, engine_no,
                     owner_name, owner_addr, owner_birth, owner_nationality,
                     holder_name, holder_addr, holder_birth, holder_nationality)
                    VALUES (@id,@veh_type,@brand,@model_year,@chassis_no,@engine_brand,@engine_no,
                     @owner_name,@owner_addr,@owner_birth,@owner_nationality,
                     @holder_name,@holder_addr,@holder_birth,@holder_nationality)
                    ON CONFLICT(user_id) DO UPDATE SET
                     veh_type=@veh_type, brand=@brand, model_year=@model_year, chassis_no=@chassis_no,
                     engine_brand=@engine_brand, engine_no=@engine_no,
                     owner_name=@owner_name, owner_addr=@owner_addr, owner_birth=@owner_birth, owner_nationality=@owner_nationality,
                     holder_name=@holder_name, holder_addr=@holder_addr, holder_birth=@holder_birth, holder_nationality=@holder_nationality";
                using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", userId);
                    string[] keys = { "veh_type","brand","model_year","chassis_no","engine_brand","engine_no",
                                      "owner_name","owner_addr","owner_birth","owner_nationality",
                                      "holder_name","holder_addr","holder_birth","holder_nationality" };
                    foreach (string k in keys)
                        cmd.Parameters.AddWithValue("@" + k, f.ContainsKey(k) ? (f[k] ?? "") : "");
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // ===================== ประวัติการผ่านเข้า-ออก =====================

        /// <summary>โฟลเดอร์เก็บภาพประวัติ อยู่ข้างไฟล์ฐานข้อมูล แยกตามวัน</summary>
        public string GetLogImageDir(DateTime when)
        {
            string baseDir = System.IO.Path.GetDirectoryName(dbPath);
            if (string.IsNullOrEmpty(baseDir)) baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dir = System.IO.Path.Combine(baseDir, "logs", when.ToString("yyyy-MM-dd"));
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        public void EnsureAccessLogTable()
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = @"CREATE TABLE IF NOT EXISTS access_logs (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    ts           TEXT,      -- เวลา (yyyy-MM-dd HH:mm:ss)
                    result       TEXT,      -- ALLOWED / DENIED
                    reason       TEXT,      -- รายละเอียดผลการตรวจ
                    mode         TEXT,      -- RFID / LPR
                    rfid_tag     TEXT,      -- เลขแท็กที่อ่านได้
                    plate_cam1   TEXT,      -- ทะเบียนที่กล้องหน้าอ่านได้
                    plate_cam2   TEXT,      -- ทะเบียนที่กล้องหลังอ่านได้
                    plate_db     TEXT,      -- ทะเบียนที่ตรงกับฐานข้อมูล
                    province     TEXT,
                    owner_name   TEXT,
                    permission   TEXT,      -- ลูกค้าทั่วไป / บุคลากร
                    img_wide1    TEXT,      -- ภาพมุมกว้าง กล้องหน้า
                    img_wide2    TEXT,      -- ภาพมุมกว้าง กล้องหลัง
                    img_plate1   TEXT,      -- ภาพซูมป้าย กล้องหน้า
                    img_plate2   TEXT       -- ภาพซูมป้าย กล้องหลัง
                )";
                using (var cmd = new SQLiteCommand(sql, conn)) cmd.ExecuteNonQuery();
            }
        }

        public long SaveAccessLog(DateTime ts, string result, string reason, string mode,
                                  string rfidTag, string plateCam1, string plateCam2,
                                  string plateDb, string province, string ownerName, string permission,
                                  string imgWide1, string imgWide2, string imgPlate1, string imgPlate2)
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = @"INSERT INTO access_logs
                    (ts, result, reason, mode, rfid_tag, plate_cam1, plate_cam2, plate_db,
                     province, owner_name, permission, img_wide1, img_wide2, img_plate1, img_plate2)
                    VALUES (@ts,@res,@rea,@mode,@tag,@p1,@p2,@pdb,@prov,@own,@perm,@w1,@w2,@c1,@c2);
                    SELECT last_insert_rowid();";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ts", ts.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@res", result ?? "");
                    cmd.Parameters.AddWithValue("@rea", reason ?? "");
                    cmd.Parameters.AddWithValue("@mode", mode ?? "");
                    cmd.Parameters.AddWithValue("@tag", rfidTag ?? "");
                    cmd.Parameters.AddWithValue("@p1", plateCam1 ?? "");
                    cmd.Parameters.AddWithValue("@p2", plateCam2 ?? "");
                    cmd.Parameters.AddWithValue("@pdb", plateDb ?? "");
                    cmd.Parameters.AddWithValue("@prov", province ?? "");
                    cmd.Parameters.AddWithValue("@own", ownerName ?? "");
                    cmd.Parameters.AddWithValue("@perm", permission ?? "");
                    cmd.Parameters.AddWithValue("@w1", imgWide1 ?? "");
                    cmd.Parameters.AddWithValue("@w2", imgWide2 ?? "");
                    cmd.Parameters.AddWithValue("@c1", imgPlate1 ?? "");
                    cmd.Parameters.AddWithValue("@c2", imgPlate2 ?? "");
                    object id = cmd.ExecuteScalar();
                    return id == null ? 0 : Convert.ToInt64(id);
                }
            }
        }

        /// <summary>ดึงประวัติ (from/to = ช่วงวัน, result = "" คือทั้งหมด, keyword ค้นทะเบียน/แท็ก/ชื่อ)</summary>
        public DataTable GetAccessLogs(DateTime from, DateTime to, string result = "", string keyword = "")
        {
            var dt = new DataTable();
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                string sql = @"SELECT * FROM access_logs
                               WHERE ts >= @from AND ts <= @to";
                if (!string.IsNullOrEmpty(result)) sql += " AND result = @res";
                if (!string.IsNullOrEmpty(keyword))
                    sql += " AND (plate_cam1 LIKE @kw OR plate_cam2 LIKE @kw OR plate_db LIKE @kw" +
                           " OR rfid_tag LIKE @kw OR owner_name LIKE @kw)";
                sql += " ORDER BY id DESC";

                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd 00:00:00"));
                    cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd 23:59:59"));
                    if (!string.IsNullOrEmpty(result)) cmd.Parameters.AddWithValue("@res", result);
                    if (!string.IsNullOrEmpty(keyword)) cmd.Parameters.AddWithValue("@kw", "%" + keyword + "%");
                    using (var da = new SQLiteDataAdapter(cmd)) da.Fill(dt);
                }
            }
            return dt;
        }

        public void DeleteAccessLog(long id)
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("DELETE FROM access_logs WHERE id=@id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

    }
}
