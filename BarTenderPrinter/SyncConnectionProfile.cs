using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BarTenderPrinter
{
    internal static class SyncWebDavUrlPolicy
    {
        public static bool IsAllowed(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.IdnHost, "dav.jianguoyun.com", StringComparison.OrdinalIgnoreCase) ||
                !uri.IsDefaultPort || uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo) ||
                IPAddress.TryParse(uri.Host, out _) || !string.Equals(uri.AbsolutePath, "/dav/", StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                return false;
            return true;
        }
    }

    public sealed class SyncConnectionProfile
    {
        public int SchemaVersion { get; set; } = 1;
        public string WebDavUrl { get; set; } = "";
        public string UserName { get; set; } = "";
        public string ApplicationPassword { get; set; } = "";
        public string SpaceId { get; set; } = "";
        public byte[] DataKey { get; set; } = Array.Empty<byte>();
        public string DeviceId { get; set; } = "";
        public string WorkspaceName { get; set; } = "";
        public bool DirectSyncEnabled { get; set; }
        public int DirectSyncPort { get; set; } = 45873;
        public DateTimeOffset? LastSuccessfulSyncUtc { get; set; }
        public bool? RemoteBaselineEstablished { get; set; }
        public bool IsWorkspaceCreator { get; set; }
        public bool? LocalCaptureEnabled { get; set; }
    }

    internal interface ISyncConnectionProfileStore
    {
        byte[] Export(SyncConnectionProfile profile, string sharedPassword);
        SyncConnectionProfile Import(byte[] connectionFile, string sharedPassword);
        void SaveLocal(SyncConnectionProfile profile);
        SyncConnectionProfile LoadLocal();
    }

    public sealed class SyncConnectionProfileStore : ISyncConnectionProfileStore
    {
        private static readonly byte[] ExportMagic = Encoding.ASCII.GetBytes("BTPSYNC1");
        private static readonly byte[] DpapiEntropy = SHA256.HashData(Encoding.UTF8.GetBytes("BarTenderPrinter.SyncProfile.v1"));
        private const byte ExportVersion = 1;
        private const int SaltSize = 16;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        public const int Pbkdf2Iterations = 600000;

        private readonly string _localProfilePath;

        public SyncConnectionProfileStore(string localProfilePath)
        {
            if (string.IsNullOrWhiteSpace(localProfilePath)) throw new ArgumentException("本地同步配置路径不能为空。", nameof(localProfilePath));
            _localProfilePath = localProfilePath;
        }

        public byte[] Export(SyncConnectionProfile profile, string sharedPassword)
        {
            ValidateProfile(profile);
            ValidateSharedPassword(sharedPassword);
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(profile);
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var key = Rfc2898DeriveBytes.Pbkdf2(sharedPassword, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
            try
            {
                var ciphertext = new byte[plaintext.Length];
                var tag = new byte[TagSize];
                using (var aes = new AesGcm(key, TagSize))
                {
                    aes.Encrypt(nonce, plaintext, ciphertext, tag, ExportMagic);
                }
                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
                writer.Write(ExportMagic);
                writer.Write(ExportVersion);
                writer.Write(Pbkdf2Iterations);
                writer.Write((byte)SaltSize);
                writer.Write((byte)NonceSize);
                writer.Write((byte)TagSize);
                writer.Write(ciphertext.Length);
                writer.Write(salt);
                writer.Write(nonce);
                writer.Write(tag);
                writer.Write(ciphertext);
                return stream.ToArray();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        public SyncConnectionProfile Import(byte[] connectionFile, string sharedPassword)
        {
            ValidateSharedPassword(sharedPassword);
            if (connectionFile == null) throw new ArgumentNullException(nameof(connectionFile));
            try
            {
                using var stream = new MemoryStream(connectionFile, false);
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                var magic = reader.ReadBytes(ExportMagic.Length);
                if (!CryptographicOperations.FixedTimeEquals(magic, ExportMagic)) throw InvalidProfile();
                var version = reader.ReadByte();
                if (version > ExportVersion) throw new SyncSecurityException(SyncErrorCodes.SchemaTooNew, "连接文件格式高于当前客户端支持版本，请升级软件。");
                if (version != ExportVersion) throw InvalidProfile();
                var iterations = reader.ReadInt32();
                var saltLength = reader.ReadByte();
                var nonceLength = reader.ReadByte();
                var tagLength = reader.ReadByte();
                var ciphertextLength = reader.ReadInt32();
                if (iterations < Pbkdf2Iterations || saltLength != SaltSize || nonceLength != NonceSize || tagLength != TagSize || ciphertextLength < 1 || ciphertextLength > 1024 * 1024 || stream.Length - stream.Position != saltLength + nonceLength + tagLength + ciphertextLength)
                    throw InvalidProfile();
                var salt = reader.ReadBytes(saltLength);
                var nonce = reader.ReadBytes(nonceLength);
                var tag = reader.ReadBytes(tagLength);
                var ciphertext = reader.ReadBytes(ciphertextLength);
                var plaintext = new byte[ciphertextLength];
                var key = Rfc2898DeriveBytes.Pbkdf2(sharedPassword, salt, iterations, HashAlgorithmName.SHA256, 32);
                try
                {
                    using (var aes = new AesGcm(key, TagSize))
                    {
                        aes.Decrypt(nonce, ciphertext, tag, plaintext, ExportMagic);
                    }
                    var profile = JsonSerializer.Deserialize<SyncConnectionProfile>(plaintext) ?? throw InvalidProfile();
                    ValidateProfile(profile);
                    return profile;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
            catch (SyncSecurityException) { throw; }
            catch (Exception ex) when (ex is CryptographicException || ex is EndOfStreamException || ex is IOException || ex is JsonException || ex is ArgumentException)
            {
                throw InvalidProfile(ex);
            }
        }

        public void SaveLocal(SyncConnectionProfile profile)
        {
            ValidateProfile(profile);
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("本地同步配置保护需要 Windows DPAPI。");
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(profile);
            try
            {
                var protectedBytes = ProtectedData.Protect(plaintext, DpapiEntropy, DataProtectionScope.CurrentUser);
                WriteAtomic(_localProfilePath, protectedBytes);
            }
            catch (CryptographicException ex)
            {
                throw new SyncSecurityException(SyncErrorCodes.InvalidProfile, "无法保护本地同步配置。", ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        public SyncConnectionProfile LoadLocal()
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("本地同步配置保护需要 Windows DPAPI。");
            try
            {
                var protectedBytes = File.ReadAllBytes(_localProfilePath);
                var plaintext = ProtectedData.Unprotect(protectedBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
                try
                {
                    var profile = JsonSerializer.Deserialize<SyncConnectionProfile>(plaintext) ?? throw InvalidProfile();
                    ValidateProfile(profile);
                    return profile;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
            catch (SyncSecurityException) { throw; }
            catch (Exception ex) when (ex is CryptographicException || ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is ArgumentException)
            {
                throw InvalidProfile(ex);
            }
        }

        private static void ValidateProfile(SyncConnectionProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (profile.SchemaVersion != 1) throw new ArgumentException("连接配置版本无效。", nameof(profile));
            if (!SyncWebDavUrlPolicy.IsAllowed(profile.WebDavUrl))
                throw new ArgumentException("WebDAV 地址必须为受信任的坚果云 HTTPS DAV 地址。", nameof(profile));
            if (string.IsNullOrWhiteSpace(profile.UserName) || string.IsNullOrWhiteSpace(profile.ApplicationPassword) || string.IsNullOrWhiteSpace(profile.SpaceId))
                throw new ArgumentException("连接配置缺少必要字段。", nameof(profile));
            if (profile.DataKey == null || profile.DataKey.Length != 32) throw new ArgumentException("连接配置数据密钥无效。", nameof(profile));
            if (profile.DirectSyncPort < 1024 || profile.DirectSyncPort > 65535) throw new ArgumentException("直连端口无效。", nameof(profile));
        }

        private static void ValidateSharedPassword(string sharedPassword)
        {
            if (string.IsNullOrEmpty(sharedPassword)) throw new ArgumentException("共享密码不能为空。", nameof(sharedPassword));
        }

        private static SyncSecurityException InvalidProfile(Exception innerException = null)
        {
            return new SyncSecurityException(SyncErrorCodes.InvalidProfile, "连接文件已损坏或共享密码错误，配置未更改。", innerException);
        }

        private static void WriteAtomic(string path, byte[] content)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, content);
                File.Move(temporaryPath, fullPath, true);
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch (IOException) { }
            }
        }
    }
}
