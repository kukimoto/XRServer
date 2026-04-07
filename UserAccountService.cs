using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace XRServer
{
    public sealed class UserAccountRow
    {
        public long Id { get; set; }
        public string Username { get; set; } = "";
        public string Role { get; set; } = "";
    }

    public sealed class UserOpResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = "";

        public static UserOpResult Success(string message) => new UserOpResult { Ok = true, Message = message };
        public static UserOpResult Fail(string message) => new UserOpResult { Ok = false, Message = message };
    }

    internal static class PasswordUtil
    {
        private const int Iterations = 100_000;
        private const int SaltSize = 16;
        private const int HashSize = 32;

        public static void HashPassword(string password, out string saltBase64, out string hashBase64)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);

            using var pbkdf2 = new Rfc2898DeriveBytes(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256);

            byte[] hash = pbkdf2.GetBytes(HashSize);

            saltBase64 = Convert.ToBase64String(salt);
            hashBase64 = Convert.ToBase64String(hash);
        }

        public static bool VerifyPassword(string password, string saltBase64, string hashBase64)
        {
            byte[] salt = Convert.FromBase64String(saltBase64);
            byte[] expectedHash = Convert.FromBase64String(hashBase64);

            using var pbkdf2 = new Rfc2898DeriveBytes(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256);

            byte[] actualHash = pbkdf2.GetBytes(expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
    }

    public sealed class UserAccountService
    {
        private readonly string _connectionString;

        public UserAccountService(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        public void Initialize()
        {
            using var con = new SqliteConnection(_connectionString);
            con.Open();

            string sql = @"
CREATE TABLE IF NOT EXISTS users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    username TEXT NOT NULL UNIQUE COLLATE NOCASE,
    password_hash TEXT NOT NULL,
    password_salt TEXT NOT NULL,
    role TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);";
            using var cmd = new SqliteCommand(sql, con);
            cmd.ExecuteNonQuery();
        }

        public List<UserAccountRow> GetUsers()
        {
            var rows = new List<UserAccountRow>();

            using var con = new SqliteConnection(_connectionString);
            con.Open();

            string sql = @"
SELECT id, username, role
FROM users
WHERE is_active = 1
ORDER BY id ASC;";

            using var cmd = new SqliteCommand(sql, con);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                rows.Add(new UserAccountRow
                {
                    Id = reader.GetInt64(0),
                    Username = reader.GetString(1),
                    Role = reader.GetString(2)
                });
            }

            return rows;
        }

        public int GetActiveUserCount()
        {
            using var con = new SqliteConnection(_connectionString);
            con.Open();

            using var cmd = new SqliteCommand("SELECT COUNT(*) FROM users WHERE is_active = 1;", con);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public int GetActiveAdminCount()
        {
            using var con = new SqliteConnection(_connectionString);
            con.Open();

            using var cmd = new SqliteCommand("SELECT COUNT(*) FROM users WHERE is_active = 1 AND role = 'admin';", con);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public bool UsernameExists(string username)
        {
            using var con = new SqliteConnection(_connectionString);
            con.Open();

            using var cmd = new SqliteCommand("SELECT COUNT(*) FROM users WHERE username = @username;", con);
            cmd.Parameters.AddWithValue("@username", username.Trim());
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public UserOpResult CreateUser(string username, string password, string role, int maxUsers)
        {
            username = (username ?? "").Trim();
            password = password ?? "";
            role = (role ?? "").Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(username))
                return UserOpResult.Fail("Username を入力してください。");

            if (string.IsNullOrWhiteSpace(password))
                return UserOpResult.Fail("Password を入力してください。");

            if (password.Length < 8)
                return UserOpResult.Fail("Password は8文字以上にしてください。");

            if (role != "admin" && role != "editor" && role != "viewer")
                return UserOpResult.Fail("Role が不正です。");

            int currentUsers = GetActiveUserCount();
            if (currentUsers >= maxUsers)
                return UserOpResult.Fail($"ライセンス上限に達しています。上限={maxUsers}");

            if (UsernameExists(username))
                return UserOpResult.Fail("その Username は既に存在します。");

            if (GetActiveUserCount() == 0)
                role = "admin";

            PasswordUtil.HashPassword(password, out string saltBase64, out string hashBase64);

            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            using var con = new SqliteConnection(_connectionString);
            con.Open();

            string sql = @"
INSERT INTO users(username, password_hash, password_salt, role, is_active, created_at, updated_at)
VALUES(@username, @password_hash, @password_salt, @role, 1, @created_at, @updated_at);";

            using var cmd = new SqliteCommand(sql, con);
            cmd.Parameters.AddWithValue("@username", username);
            cmd.Parameters.AddWithValue("@password_hash", hashBase64);
            cmd.Parameters.AddWithValue("@password_salt", saltBase64);
            cmd.Parameters.AddWithValue("@role", role);
            cmd.Parameters.AddWithValue("@created_at", now);
            cmd.Parameters.AddWithValue("@updated_at", now);

            cmd.ExecuteNonQuery();
            return UserOpResult.Success("ユーザを作成しました。");
        }

        public UserOpResult DeleteUser(long userId)
        {
            using var con = new SqliteConnection(_connectionString);
            con.Open();

            string role = "";
            int isActive = 0;

            using (var getCmd = new SqliteCommand("SELECT role, is_active FROM users WHERE id = @id LIMIT 1;", con))
            {
                getCmd.Parameters.AddWithValue("@id", userId);
                using var reader = getCmd.ExecuteReader();
                if (!reader.Read())
                    return UserOpResult.Fail("対象ユーザが見つかりません。");

                role = reader.GetString(0);
                isActive = reader.GetInt32(1);
            }

            if (role == "admin" && isActive == 1 && GetActiveAdminCount() <= 1)
                return UserOpResult.Fail("最後の admin は削除できません。");

            using var cmd = new SqliteCommand("DELETE FROM users WHERE id = @id;", con);
            cmd.Parameters.AddWithValue("@id", userId);

            int rows = cmd.ExecuteNonQuery();
            if (rows == 0)
                return UserOpResult.Fail("削除対象が見つかりません。");

            return UserOpResult.Success("ユーザを削除しました。");
        }
    }
}