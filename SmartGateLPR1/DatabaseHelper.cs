using System;
using System.Collections.Generic;
using System.Data;
using System.IO;

namespace SmartGateLPR1
{
    /// <summary>
    /// จัดการข้อมูลทั้งหมดของระบบ — ทำงานได้ทั้งกับ SQLite (ในเครื่อง)
    /// และ MySQL/MariaDB (บน server) ผ่านชั้นกลาง Db
    /// </summary>
    public class DatabaseHelper
    {
        private static bool schemaReady = false;
        private static readonly object schemaLock = new object();

        /// <summary>ข้อความ error ตอนสร้างตาราง (ว่าง = ปกติ) ไว้ให้หน้าจอหลักเอาไปเตือน</summary>
        public static string LastSchemaError = "";

        public DatabaseHelper()
        {
            if (string.IsNullOrEmpty(Db.ConnStr)) Db.Configure();

            lock (schemaLock)
            {
                if (schemaReady) return;
                try
                {
                    EnsureSchema();
                    schemaReady = true;
                    LastSchemaError = "";
                }
                catch (Exception ex)
                {
                    // ต่อฐานข้อมูลไม่ได้ก็ยังต้องเปิดโปรแกรมได้ ผู้ใช้จะได้เข้าไปแก้ค่าที่ตั้งไว้
                    LastSchemaError = Db.Explain(ex);
                }
            }
        }

        /// <summary>เรียกหลังเปลี่ยนที่เก็บข้อมูล เพื่อให้สร้าง/ตรวจตารางใหม่อีกรอบ</summary>
        public static void ResetSchema()
        {
            lock (schemaLock) { schemaReady = false; LastSchemaError = ""; }
        }

        // ================= สร้างตารางทั้งหมด =================

        private void EnsureSchema()
        {
            CreateUsersTable();
            EnsureVehicleDetailsTable();
            EnsureAccessLogTable();
        }

        private void CreateUsersTable()
        {
            Db.Exec($@"CREATE TABLE IF NOT EXISTS users (
                    id            {Db.PkAuto},
                    plate_number  {Db.V(60)},
                    rfid_tag      {Db.V(100)} UNIQUE,
                    owner_name    {Db.V(200)}
                ){Db.TableTail}");

            // เพิ่มคอลัมน์ที่มาทีหลัง (รันซ้ำได้ ถ้ามีแล้วจะ error แล้วข้ามไปเอง)
            Db.TryExec($"ALTER TABLE users ADD COLUMN plate_letters {Db.V(20)}");
            Db.TryExec($"ALTER TABLE users ADD COLUMN plate_digits {Db.V(20)}");
            Db.TryExec($"ALTER TABLE users ADD COLUMN province {Db.V(60)}");
            Db.TryExec($"ALTER TABLE users ADD COLUMN permission {Db.V(60)}");
        }

        public void EnsureVehicleDetailsTable()
        {
            Db.Exec($@"CREATE TABLE IF NOT EXISTS vehicle_details (
                    user_id            BIGINT PRIMARY KEY,
                    veh_type           {Db.V(100)},
                    brand              {Db.V(100)},
                    model_year         {Db.V(20)},
                    chassis_no         {Db.V(100)},
                    engine_brand       {Db.V(100)},
                    engine_no          {Db.V(100)},
                    owner_name         {Db.V(200)},
                    owner_addr         {Db.V(400)},
                    owner_birth        {Db.V(40)},
                    owner_nationality  {Db.V(60)},
                    holder_name        {Db.V(200)},
                    holder_addr        {Db.V(400)},
                    holder_birth       {Db.V(40)},
                    holder_nationality {Db.V(60)}
                ){Db.TableTail}");
        }

        public void EnsureAccessLogTable()
        {
            Db.Exec($@"CREATE TABLE IF NOT EXISTS access_logs (
                    id          {Db.PkAuto},
                    ts          {Db.V(25)},
                    result      {Db.V(20)},
                    reason      {Db.V(500)},
                    mode        {Db.V(20)},
                    rfid_tag    {Db.V(100)},
                    plate_cam1  {Db.V(60)},
                    plate_cam2  {Db.V(60)},
                    plate_db    {Db.V(60)},
                    province    {Db.V(60)},
                    owner_name  {Db.V(200)},
                    permission  {Db.V(60)},
                    img_wide1   {Db.V(500)},
                    img_wide2   {Db.V(500)},
                    img_plate1  {Db.V(500)},
                    img_plate2  {Db.V(500)}
                ){Db.TableTail}");

            Db.TryExec("CREATE INDEX idx_access_logs_ts ON access_logs (ts)");
        }

        // ================= ข้อมูลรถ / ผู้ใช้ =================

        public void AddUser(string plate, string rfid, string name)
        {
            Db.Exec("INSERT INTO users (plate_number, rfid_tag, owner_name) VALUES (@p, @r, @n)",
                cmd => { Db.P(cmd, "@p", plate); Db.P(cmd, "@r", rfid); Db.P(cmd, "@n", name); });
        }

        public void AddUserFull(string letters, string digits, string province,
                                string permission, string rfid)
        {
            Db.Exec(@"INSERT INTO users
                        (plate_number, plate_letters, plate_digits, province, permission, rfid_tag, owner_name)
                      VALUES (@pn, @pl, @pd, @pv, @pm, @r, @own)",
                cmd =>
                {
                    Db.P(cmd, "@pn", letters + digits);
                    Db.P(cmd, "@pl", letters);
                    Db.P(cmd, "@pd", digits);
                    Db.P(cmd, "@pv", province);
                    Db.P(cmd, "@pm", permission);
                    Db.P(cmd, "@r", rfid);
                    Db.P(cmd, "@own", "");
                });
        }

        public void UpdateUserFull(long id, string letters, string digits, string province,
                                   string permission, string rfid)
        {
            Db.Exec(@"UPDATE users SET
                        plate_number = @pn, plate_letters = @pl, plate_digits = @pd,
                        province = @pv, permission = @pm, rfid_tag = @r
                      WHERE id = @id",
                cmd =>
                {
                    Db.P(cmd, "@pn", letters + digits);
                    Db.P(cmd, "@pl", letters);
                    Db.P(cmd, "@pd", digits);
                    Db.P(cmd, "@pv", province);
                    Db.P(cmd, "@pm", permission);
                    Db.P(cmd, "@r", rfid);
                    Db.P(cmd, "@id", id);
                });
        }

        public void DeleteUserById(long id)
        {
            Db.Exec("DELETE FROM users WHERE id = @id", cmd => Db.P(cmd, "@id", id));
        }

        public void DeleteUser(string rfid)
        {
            Db.Exec("DELETE FROM users WHERE rfid_tag = @rfid", cmd => Db.P(cmd, "@rfid", rfid));
        }

        public DataTable GetAllUsers()
        {
            return Db.Query("SELECT * FROM users ORDER BY id");
        }

        /// <summary>ค้นด้วยแท็ก RFID (ตัดช่องว่าง/ขีด และไม่สนตัวพิมพ์เล็กใหญ่)</summary>
        public DataTable GetUserByTag(string tag)
        {
            string norm = (tag ?? "").Replace(" ", "").Replace("-", "").ToUpper();
            return Db.Query(
                "SELECT * FROM users WHERE REPLACE(REPLACE(UPPER(rfid_tag),' ',''),'-','') = @tag",
                cmd => Db.P(cmd, "@tag", norm));
        }

        /// <summary>ค้นด้วยป้ายทะเบียน (ตัดช่องว่าง/ขีดก่อนเทียบ)</summary>
        public DataTable GetUserByPlate(string plateNorm)
        {
            return Db.Query(
                "SELECT * FROM users WHERE REPLACE(REPLACE(plate_number,' ',''),'-','') = @p",
                cmd => Db.P(cmd, "@p", plateNorm ?? ""));
        }

        // ================= ข้อมูลเจ้าของรถ (เล่มทะเบียน) =================

        public DataRow GetVehicleDetails(long userId)
        {
            DataTable dt = Db.Query("SELECT * FROM vehicle_details WHERE user_id = @id",
                                    cmd => Db.P(cmd, "@id", userId));
            return dt.Rows.Count > 0 ? dt.Rows[0] : null;
        }

        public void SaveVehicleDetails(long userId, Dictionary<string, string> f)
        {
            string[] keys = { "veh_type","brand","model_year","chassis_no","engine_brand","engine_no",
                              "owner_name","owner_addr","owner_birth","owner_nationality",
                              "holder_name","holder_addr","holder_birth","holder_nationality" };

            string cols = string.Join(", ", keys);
            string vals = "@" + string.Join(", @", keys);
            string sets = string.Join(", ", Array.ConvertAll(keys, k => $"{k}=@{k}"));

            string sql = $"INSERT INTO vehicle_details (user_id, {cols}) VALUES (@id, {vals})" +
                         Db.UpsertHead("user_id") + sets;

            Db.Exec(sql, cmd =>
            {
                Db.P(cmd, "@id", userId);
                foreach (string k in keys)
                    Db.P(cmd, "@" + k, f.ContainsKey(k) ? (f[k] ?? "") : "");
            });
        }

        // ================= ประวัติการผ่านเข้า-ออก =================

        /// <summary>โฟลเดอร์เก็บภาพประวัติ แยกตามวัน (ตั้งที่อยู่ได้ในหน้าตั้งค่าที่เก็บข้อมูล)</summary>
        public string GetLogImageDir(DateTime when)
        {
            string baseDir = Db.ImageBaseDir;
            if (string.IsNullOrEmpty(baseDir)) baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dir = Path.Combine(baseDir, "logs", when.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public long SaveAccessLog(DateTime ts, string result, string reason, string mode,
                                  string rfidTag, string plateCam1, string plateCam2,
                                  string plateDb, string province, string ownerName, string permission,
                                  string imgWide1, string imgWide2, string imgPlate1, string imgPlate2)
        {
            return Db.InsertGetId(@"INSERT INTO access_logs
                    (ts, result, reason, mode, rfid_tag, plate_cam1, plate_cam2, plate_db,
                     province, owner_name, permission, img_wide1, img_wide2, img_plate1, img_plate2)
                    VALUES (@ts,@res,@rea,@mode,@tag,@p1,@p2,@pdb,@prov,@own,@perm,@w1,@w2,@c1,@c2)",
                cmd =>
                {
                    Db.P(cmd, "@ts", ts.ToString("yyyy-MM-dd HH:mm:ss"));
                    Db.P(cmd, "@res", result ?? "");
                    Db.P(cmd, "@rea", reason ?? "");
                    Db.P(cmd, "@mode", mode ?? "");
                    Db.P(cmd, "@tag", rfidTag ?? "");
                    Db.P(cmd, "@p1", plateCam1 ?? "");
                    Db.P(cmd, "@p2", plateCam2 ?? "");
                    Db.P(cmd, "@pdb", plateDb ?? "");
                    Db.P(cmd, "@prov", province ?? "");
                    Db.P(cmd, "@own", ownerName ?? "");
                    Db.P(cmd, "@perm", permission ?? "");
                    Db.P(cmd, "@w1", imgWide1 ?? "");
                    Db.P(cmd, "@w2", imgWide2 ?? "");
                    Db.P(cmd, "@c1", imgPlate1 ?? "");
                    Db.P(cmd, "@c2", imgPlate2 ?? "");
                });
        }

        public DataTable GetAccessLogs(DateTime from, DateTime to, string result = "", string keyword = "")
        {
            string sql = "SELECT * FROM access_logs WHERE ts >= @from AND ts <= @to";
            if (!string.IsNullOrEmpty(result)) sql += " AND result = @res";
            if (!string.IsNullOrEmpty(keyword))
                sql += " AND (plate_cam1 LIKE @kw OR plate_cam2 LIKE @kw OR plate_db LIKE @kw" +
                       " OR rfid_tag LIKE @kw OR owner_name LIKE @kw)";
            sql += " ORDER BY id DESC";

            return Db.Query(sql, cmd =>
            {
                Db.P(cmd, "@from", from.ToString("yyyy-MM-dd 00:00:00"));
                Db.P(cmd, "@to", to.ToString("yyyy-MM-dd 23:59:59"));
                if (!string.IsNullOrEmpty(result)) Db.P(cmd, "@res", result);
                if (!string.IsNullOrEmpty(keyword)) Db.P(cmd, "@kw", "%" + keyword + "%");
            });
        }

        public void DeleteAccessLog(long id)
        {
            Db.Exec("DELETE FROM access_logs WHERE id = @id", cmd => Db.P(cmd, "@id", id));
        }
    }
}