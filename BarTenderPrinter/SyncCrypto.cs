using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BarTenderPrinter
{
    public static class SyncCrypto
    {
        private static readonly byte[] ObjectMagic = Encoding.ASCII.GetBytes("BTPSOBJ1");
        private const byte ObjectFormatVersion = 2;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int HeaderSize = 8 + 1 + 1 + 1 + 1 + 4;
        private const int MaximumObjectSize = 500 * 1024 * 1024;

        public static byte[] GenerateDataKey()
        {
            return RandomNumberGenerator.GetBytes(32);
        }

        public static string ComputeSha256Hex(ReadOnlySpan<byte> content)
        {
            return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        }

        public static byte[] EncryptObject(byte[] plaintext, byte[] dataKey, string spaceId, string objectType, string objectId)
        {
            ValidateObjectArguments(plaintext, dataKey, spaceId, objectType, objectId);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];
            var associatedData = BuildAssociatedData(spaceId, objectType, objectId);
            using (var aes = new AesGcm(dataKey, TagSize))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            }

            var result = new byte[HeaderSize + NonceSize + TagSize + ciphertext.Length];
            ObjectMagic.CopyTo(result, 0);
            result[8] = ObjectFormatVersion;
            result[9] = NonceSize;
            result[10] = TagSize;
            result[11] = 0;
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(12, 4), ciphertext.Length);
            var offset = HeaderSize;
            nonce.CopyTo(result, offset); offset += NonceSize;
            tag.CopyTo(result, offset); offset += TagSize;
            ciphertext.CopyTo(result, offset);
            return result;
        }

        public static byte[] DecryptObject(byte[] encryptedObject, byte[] dataKey, string spaceId, string objectType, string objectId)
        {
            if (encryptedObject == null) throw new ArgumentNullException(nameof(encryptedObject));
            ValidateDataKey(dataKey);
            ValidateIdentity(spaceId, nameof(spaceId));
            ValidateIdentity(objectType, nameof(objectType));
            ValidateIdentity(objectId, nameof(objectId));

            try
            {
                if (encryptedObject.Length < HeaderSize + NonceSize + TagSize ||
                    !CryptographicOperations.FixedTimeEquals(encryptedObject.AsSpan(0, 8), ObjectMagic))
                    throw Corrupted();
                if (encryptedObject[8] > ObjectFormatVersion)
                    throw new SyncSecurityException(SyncErrorCodes.SchemaTooNew, "同步对象格式高于当前客户端支持版本，请升级软件。");
                if (encryptedObject[8] != ObjectFormatVersion || encryptedObject[9] != NonceSize || encryptedObject[10] != TagSize || encryptedObject[11] != 0)
                    throw Corrupted();

                var ciphertextLength = BinaryPrimitives.ReadInt32BigEndian(encryptedObject.AsSpan(12, 4));
                if (ciphertextLength < 0 || ciphertextLength > MaximumObjectSize || encryptedObject.Length != HeaderSize + NonceSize + TagSize + ciphertextLength)
                    throw Corrupted();
                var offset = HeaderSize;
                var nonce = encryptedObject.AsSpan(offset, NonceSize); offset += NonceSize;
                var tag = encryptedObject.AsSpan(offset, TagSize); offset += TagSize;
                var plaintext = new byte[ciphertextLength];
                using (var aes = new AesGcm(dataKey, TagSize))
                {
                    aes.Decrypt(nonce, encryptedObject.AsSpan(offset, ciphertextLength), tag, plaintext, BuildAssociatedData(spaceId, objectType, objectId));
                }
                return plaintext;
            }
            catch (SyncSecurityException) { throw; }
            catch (Exception ex) when (ex is CryptographicException || ex is ArgumentException || ex is OverflowException)
            {
                throw Corrupted(ex);
            }
        }

        private static byte[] BuildAssociatedData(string spaceId, string objectType, string objectId)
        {
            return Encoding.UTF8.GetBytes($"BTPSOBJ\n{ObjectFormatVersion}\n{spaceId}\n{objectType}\n{objectId}");
        }

        private static void ValidateObjectArguments(byte[] plaintext, byte[] dataKey, string spaceId, string objectType, string objectId)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            if (plaintext.Length > MaximumObjectSize) throw new ArgumentOutOfRangeException(nameof(plaintext), "同步对象不能超过 500MB。");
            ValidateDataKey(dataKey);
            ValidateIdentity(spaceId, nameof(spaceId));
            ValidateIdentity(objectType, nameof(objectType));
            ValidateIdentity(objectId, nameof(objectId));
        }

        private static void ValidateDataKey(byte[] dataKey)
        {
            if (dataKey == null || dataKey.Length != 32) throw new ArgumentException("数据密钥必须为 32 字节。", nameof(dataKey));
        }

        private static void ValidateIdentity(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("同步对象身份字段无效。", name);
        }

        private static SyncSecurityException Corrupted(Exception innerException = null)
        {
            return new SyncSecurityException(SyncErrorCodes.ObjectCorrupted, "同步对象认证或摘要校验失败，已拒绝使用该对象。", innerException);
        }
    }
}
