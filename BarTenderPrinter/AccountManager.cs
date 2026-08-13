using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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

        public UserAccount DefaultAccount => _accounts.First(account => account.UserName == "superadmin");

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
            try
            {
                if (File.Exists(_path))
                    _accounts.AddRange(JsonSerializer.Deserialize<List<UserAccount>>(File.ReadAllText(_path)) ?? new List<UserAccount>());
            }
            catch { }
            if (_accounts.All(account => !string.Equals(account.UserName, "superadmin", System.StringComparison.OrdinalIgnoreCase)))
                _accounts.Add(new UserAccount { UserName = "superadmin", PasswordHash = HashPassword("admin"), Role = "Admin" });
            Save();
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
