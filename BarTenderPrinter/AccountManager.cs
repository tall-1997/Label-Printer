using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System;

namespace BarTenderPrinter
{
    public class UserAccount
    {
        public string UserName { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string Role { get; set; } = "Operator";
    }

    public class AccountManager
    {
        private const string SuperAdminUserName = "superadmin";
        private const string SuperAdminPassword = "admin123";
        private readonly string _path;
        private readonly List<UserAccount> _accounts = new List<UserAccount>();

        public AccountManager(string path = null)
        {
            AppPaths.Initialize();
            _path = string.IsNullOrWhiteSpace(path) ? AppPaths.AccountsFile : path;
            Load();
        }

        public UserAccount DefaultAccount => _accounts.FirstOrDefault(account => account.UserName == SuperAdminUserName);
        public Exception LoadError { get; private set; }
        public string AccountFilePath => _path;
        public string BootstrapPassword { get; private set; }

        public bool TryLogin(string userName, string password, out UserAccount account)
        {
            account = _accounts.FirstOrDefault(item => string.Equals(item.UserName, userName ?? "", System.StringComparison.OrdinalIgnoreCase));
            if (account == null || !VerifyPassword(password ?? "", account.PasswordHash))
            {
                account = null;
                return false;
            }
            if (!IsPbkdf2Hash(account.PasswordHash))
            {
                account.PasswordHash = HashPassword(password ?? "");
                Save();
            }
            return true;
        }

        private void Load()
        {
            var fileExists = File.Exists(_path);
            try
            {
                if (fileExists)
                    _accounts.AddRange(JsonSerializer.Deserialize<List<UserAccount>>(File.ReadAllText(_path)) ?? new List<UserAccount>());
            }
            catch (Exception ex)
            {
                LoadError = ex;
                LoggerService.Error("加载账户文件失败，原文件已保留", ex);
                return;
            }
            var superAdmin = _accounts.FirstOrDefault(account => string.Equals(account.UserName, SuperAdminUserName, StringComparison.OrdinalIgnoreCase));
            if (superAdmin == null)
            {
                BootstrapPassword = SuperAdminPassword;
                _accounts.Add(new UserAccount { UserName = SuperAdminUserName, PasswordHash = HashPassword(SuperAdminPassword), Role = "Admin" });
            }
            else
            {
                superAdmin.UserName = SuperAdminUserName;
                superAdmin.Role = "Admin";
                if (!VerifyPassword(SuperAdminPassword, superAdmin.PasswordHash))
                    superAdmin.PasswordHash = HashPassword(SuperAdminPassword);
            }
            if (!fileExists || _accounts.Count > 0) Save();
        }

        private void Save()
        {
            AtomicFileWriter.WriteAllText(_path, JsonSerializer.Serialize(_accounts, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static string HashPassword(string password)
        {
            const int iterations = 120000;
            var salt = RandomNumberGenerator.GetBytes(16);
            using (var pbkdf2 = new Rfc2898DeriveBytes(password ?? "", salt, iterations, HashAlgorithmName.SHA256))
                return $"PBKDF2-SHA256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(pbkdf2.GetBytes(32))}";
        }

        private static bool VerifyPassword(string password, string storedHash)
        {
            if (IsPbkdf2Hash(storedHash)) return VerifyPbkdf2(password, storedHash);
            using (var sha = SHA256.Create())
            {
                var legacy = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(password ?? "")));
                return string.Equals(legacy, storedHash, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool IsPbkdf2Hash(string value) => (value ?? "").StartsWith("PBKDF2-SHA256$", StringComparison.Ordinal);

        private static bool VerifyPbkdf2(string password, string storedHash)
        {
            try
            {
                var parts = storedHash.Split('$');
                if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations)) return false;
                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                using (var pbkdf2 = new Rfc2898DeriveBytes(password ?? "", salt, iterations, HashAlgorithmName.SHA256))
                    return CryptographicOperations.FixedTimeEquals(expected, pbkdf2.GetBytes(expected.Length));
            }
            catch { return false; }
        }
    }
}
