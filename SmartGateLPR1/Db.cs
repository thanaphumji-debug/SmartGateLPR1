using System;
using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using System.IO;
using MySqlConnector;

namespace SmartGateLPR1
{
    public enum DbKind { SQLite, MySQL }

    /// <summary>
    /// ชั้นกลางระหว่างโปรแกรมกับฐานข้อมูล — สลับระหว่าง SQLite (ในเครื่อง)
    /// กับ MySQL/MariaDB (บน server) โดยโค้ดส่วนอื่นไม่ต้องรู้ว่ากำลังใช้ตัวไหน
    /// </summary>
    public static class Db
    {
        public static DbKind Kind { get; private set; } = DbKind.SQLite;
        public static string ConnStr { get; private set; } = "";
        public static string LocalDbPath { get; private set; } = "";
        public static string ImageBaseDir { get; private set; } = "";

        /// <summary>คำอธิบายสั้น ๆ ว่าตอนนี้ต่ออยู่กับอะไร (ไว้โชว์บนหน้าจอ)</summary>
        public static string Describe { get; private set; } = "";

        // ================= ตั้งค่า =================

        public static void Configure(AppSettings st = null)
        {
            st = st ?? SettingsStore.Load();
            bool cloud = st.StorageMode == "cloud" && !string.IsNullOrWhiteSpace(st.DbHost);

            if (cloud)
            {
                Kind = DbKind.MySQL;
                ConnStr = BuildMySqlConnStr(st);
                Describe = $"MySQL — {st.DbUser}@{st.DbHost}:{(st.DbPort > 0 ? st.DbPort : 3306)}/{st.DbName}";
            }
            else
            {
                Kind = DbKind.SQLite;
                string path = string.IsNullOrWhiteSpace(st.DbLocalPath)
                    ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smartgate.db")
                    : st.DbLocalPath;

                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                LocalDbPath = Path.GetFullPath(path);
                ConnStr = $"Data Source={LocalDbPath};Version=3;";
                Describe = "SQLite — " + LocalDbPath;
            }

            // โฟลเดอร์เก็บภาพ: ตั้งเองได้ (ใช้ได้ทั้งโฟลเดอร์ในเครื่องและ network share)
            string img = st.LogImageDir;
            if (string.IsNullOrWhiteSpace(img))
            {
                img = (Kind == DbKind.SQLite && !string.IsNullOrEmpty(LocalDbPath))
                    ? Path.GetDirectoryName(LocalDbPath)
                    : AppDomain.CurrentDomain.BaseDirectory;
            }
            ImageBaseDir = img;
        }

        public static string BuildMySqlConnStr(AppSettings st)
        {
            int port = st.DbPort > 0 ? st.DbPort : 3306;
            // สร้างเป็นข้อความตรง ๆ เพื่อไม่ผูกกับชื่อ enum ของไลบรารีเวอร์ชันใด ๆ
            return $"Server={st.DbHost};Port={port};Database={st.DbName};" +
                   $"User ID={st.DbUser};Password={st.DbPassword};" +
                   $"SslMode={(st.CloudUseSsl ? "Preferred" : "None")};" +
                   "CharSet=utf8mb4;Connection Timeout=8;Default Command Timeout=30;";
        }

        // ================= เปิดการเชื่อมต่อ / สั่งงาน =================

        public static DbConnection Open()
        {
            if (string.IsNullOrEmpty(ConnStr)) Configure();
            DbConnection c = Kind == DbKind.SQLite
                ? new SQLiteConnection(ConnStr)
                : (DbConnection)new MySqlConnection(ConnStr);
            c.Open();
            return c;
        }

        public static DbCommand Cmd(DbConnection conn, string sql)
        {
            DbCommand cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return cmd;
        }

        /// <summary>ผูกค่าพารามิเตอร์ (ใช้ชื่อขึ้นต้นด้วย @ ได้ทั้ง SQLite และ MySQL)</summary>
        public static void P(DbCommand cmd, string name, object value)
        {
            DbParameter p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public static int Exec(string sql, Action<DbCommand> bind = null)
        {
            using (var c = Open())
            using (var cmd = Cmd(c, sql))
            {
                bind?.Invoke(cmd);
                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>สั่งงานแบบไม่สนใจ error (ใช้กับ ALTER TABLE ที่คอลัมน์อาจมีอยู่แล้ว)</summary>
        public static void TryExec(string sql)
        {
            try { Exec(sql); } catch { }
        }

        public static object Scalar(string sql, Action<DbCommand> bind = null)
        {
            using (var c = Open())
            using (var cmd = Cmd(c, sql))
            {
                bind?.Invoke(cmd);
                return cmd.ExecuteScalar();
            }
        }

        public static DataTable Query(string sql, Action<DbCommand> bind = null)
        {
            var dt = new DataTable();
            using (var c = Open())
            using (var cmd = Cmd(c, sql))
            {
                bind?.Invoke(cmd);
                using (var rd = cmd.ExecuteReader()) dt.Load(rd);
            }
            return dt;
        }

        /// <summary>INSERT แล้วคืนค่า id ที่เพิ่งสร้าง (ต่างกันคนละคำสั่งในแต่ละฐานข้อมูล)</summary>
        public static long InsertGetId(string sql, Action<DbCommand> bind = null)
        {
            using (var c = Open())
            using (var cmd = Cmd(c, sql))
            {
                bind?.Invoke(cmd);
                cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();
                cmd.CommandText = Kind == DbKind.SQLite
                    ? "SELECT last_insert_rowid()"
                    : "SELECT LAST_INSERT_ID()";
                object id = cmd.ExecuteScalar();
                return (id == null || id == DBNull.Value) ? 0 : Convert.ToInt64(id);
            }
        }

        // ================= ความต่างของภาษา SQL แต่ละค่าย =================

        /// <summary>คอลัมน์ id ที่เพิ่มเลขอัตโนมัติ</summary>
        public static string PkAuto => Kind == DbKind.SQLite
            ? "INTEGER PRIMARY KEY AUTOINCREMENT"
            : "BIGINT AUTO_INCREMENT PRIMARY KEY";

        /// <summary>ชนิดข้อความ — MySQL ต้องระบุความยาว ส่วน SQLite ใช้ TEXT ได้หมด</summary>
        public static string V(int n) => Kind == DbKind.SQLite ? "TEXT" : $"VARCHAR({n})";

        /// <summary>ส่วนท้าย CREATE TABLE (MySQL ต้องระบุ charset ไม่งั้นภาษาไทยเพี้ยน)</summary>
        public static string TableTail => Kind == DbKind.SQLite
            ? ""
            : " ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

        /// <summary>ท่อน UPSERT (เขียนทับถ้ามีอยู่แล้ว) — คนละไวยากรณ์กัน</summary>
        public static string UpsertHead(string keyColumn) => Kind == DbKind.SQLite
            ? $" ON CONFLICT({keyColumn}) DO UPDATE SET "
            : " ON DUPLICATE KEY UPDATE ";

        // ================= ทดสอบการเชื่อมต่อ =================

        /// <summary>ลองต่อตามค่าที่กรอก คืนผลว่าต่อได้ไหมพร้อมข้อความอธิบาย</summary>
        public static string TestConnection(AppSettings st)
        {
            try
            {
                if (st.StorageMode != "cloud")
                {
                    string path = string.IsNullOrWhiteSpace(st.DbLocalPath)
                        ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smartgate.db")
                        : st.DbLocalPath;
                    string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    using (var c = new SQLiteConnection($"Data Source={path};Version=3;"))
                    {
                        c.Open();
                        using (var cmd = c.CreateCommand())
                        {
                            cmd.CommandText = "SELECT sqlite_version()";
                            return "OK|เชื่อมต่อสำเร็จ\nSQLite เวอร์ชัน " + cmd.ExecuteScalar() +
                                   "\nไฟล์: " + Path.GetFullPath(path);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(st.DbHost)) return "ERR|ยังไม่ได้กรอก Host / IP ของ server";
                if (string.IsNullOrWhiteSpace(st.DbName)) return "ERR|ยังไม่ได้กรอกชื่อฐานข้อมูล";

                using (var c = new MySqlConnection(BuildMySqlConnStr(st)))
                {
                    c.Open();
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = "SELECT VERSION()";
                        string ver = Convert.ToString(cmd.ExecuteScalar());

                        // เช็คสิทธิ์เขียนด้วย ไม่ใช่แค่ต่อได้
                        cmd.CommandText = "CREATE TABLE IF NOT EXISTS _sg_check (id INT PRIMARY KEY)";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "DROP TABLE _sg_check";
                        cmd.ExecuteNonQuery();

                        return "OK|เชื่อมต่อสำเร็จ\nMySQL/MariaDB เวอร์ชัน " + ver +
                               "\nฐานข้อมูล: " + st.DbName + "\nมีสิทธิ์สร้าง/ลบตาราง ✓";
                    }
                }
            }
            catch (Exception ex)
            {
                return "ERR|" + Explain(ex);
            }
        }

        /// <summary>แปลง error ของไลบรารีเป็นภาษาที่อ่านรู้เรื่องว่าต้องไปแก้ตรงไหน</summary>
        public static string Explain(Exception ex)
        {
            string m = ex.Message ?? "";
            string low = m.ToLower();

            if (low.Contains("unable to connect") || low.Contains("no such host") ||
                low.Contains("timeout") || low.Contains("actively refused"))
                return "ต่อไปยัง server ไม่ได้\n\nตรวจสอบ:\n" +
                       "• Host/IP และ Port ถูกต้องไหม\n" +
                       "• server เปิดอยู่ และอนุญาตให้ต่อจากเครื่องนี้\n" +
                       "• firewall เปิดพอร์ตให้แล้ว\n\n(" + m + ")";

            if (low.Contains("access denied"))
                return "Username หรือ Password ไม่ถูกต้อง\nหรือผู้ใช้นี้ไม่มีสิทธิ์เข้าฐานข้อมูลนี้\n\n(" + m + ")";

            if (low.Contains("unknown database"))
                return "ไม่พบฐานข้อมูลชื่อนี้บน server\nต้องสร้างฐานข้อมูลเปล่าไว้ก่อน\n\n(" + m + ")";

            if (low.Contains("ssl") || low.Contains("tls"))
                return "ปัญหาการเข้ารหัส SSL/TLS\nลองปิดตัวเลือก SSL แล้วต่อใหม่\n\n(" + m + ")";

            return m;
        }
    }
}