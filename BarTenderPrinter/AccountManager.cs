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
        private readonly string _path;
        private readonly List<UserAccount> _accounts = new List<UserAccount>();

        public AccountManager(string path = null)
        {
            AppPaths.Initialize();
            _path = string.IsNullOrWhiteSpace(path) ? AppPaths.AccountsFile : path;
            Load();
        }

        public UserAccount DefaultAccount => _accounts.FirstOrDefault(account => account.UserName == "superadmin");
        public Exception LoadError { get; private set; }
        public string AccountFilePath => _path;

        public bool TryLogin(string userName, string password, out UserAccount account)
        {
            account = _accounts.FirstOrDefault(item => string.Equals(item.UserName, userName ?? "", System.StringComparison.OrdinalIgnoreCase));
            if (account == null || !string.Equals(account.PasswordHash, HashPassword(password ?? ""), System.StringComparison.OrdinalIgnoreCase))
            {
                account = null;
                return false;
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
            if (_accounts.All(account => !string.Equals(account.UserName, "superadmin", System.StringComparison.OrdinalIgnoreCase)))
                _accounts.Add(new UserAccount { UserName = "superadmin", PasswordHash = HashPassword("admin"), Role = "Admin" });
            if (!fileExists || _accounts.Count > 0) Save();
        }

        private void Save()
        {
            AtomicFileWriter.WriteAllText(_path, JsonSerializer.Serialize(_accounts, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static string HashPassword(string password)
        {
            using (var sha = SHA256.Create())
                return System.Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(password ?? "")));
        }
    }
}
