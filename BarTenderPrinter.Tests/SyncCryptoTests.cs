using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BarTenderPrinter;
using Xunit;

namespace BarTenderPrinter.Tests
{
    public sealed class SyncCryptoTests
    {
        [Fact]
        public void ObjectRoundTripAuthenticatesIdentityAndContent()
        {
            var key = SyncCrypto.GenerateDataKey();
            var plaintext = System.Text.Encoding.UTF8.GetBytes("sensitive payload");

            var encrypted = SyncCrypto.EncryptObject(plaintext, key, "space-1", "Event", "device-1:1");
            var decrypted = SyncCrypto.DecryptObject(encrypted, key, "space-1", "Event", "device-1:1");

            Assert.Equal(plaintext, decrypted);
            Assert.DoesNotContain("sensitive payload", System.Text.Encoding.UTF8.GetString(encrypted));
            Assert.Equal(0, encrypted[11]);
            Assert.Equal(44 + plaintext.Length, encrypted.Length);
        }

        [Fact]
        public void EncryptingSameObjectTwiceUsesUniqueNonce()
        {
            var key = SyncCrypto.GenerateDataKey();
            var content = new byte[] { 1, 2, 3 };

            var first = SyncCrypto.EncryptObject(content, key, "space", "Template", "hash");
            var second = SyncCrypto.EncryptObject(content, key, "space", "Template", "hash");

            Assert.False(first.SequenceEqual(second));
        }

        [Fact]
        public void TamperingOrChangingObjectIdentityIsRejected()
        {
            var key = SyncCrypto.GenerateDataKey();
            var encrypted = SyncCrypto.EncryptObject(new byte[] { 1, 2, 3 }, key, "space", "Event", "event-1");
            encrypted[^1] ^= 0x40;

            var tampered = Assert.Throws<SyncSecurityException>(() => SyncCrypto.DecryptObject(encrypted, key, "space", "Event", "event-1"));
            Assert.Equal(SyncErrorCodes.ObjectCorrupted, tampered.ErrorCode);

            var valid = SyncCrypto.EncryptObject(new byte[] { 1, 2, 3 }, key, "space", "Event", "event-1");
            Assert.Throws<SyncSecurityException>(() => SyncCrypto.DecryptObject(valid, key, "space", "Event", "event-2"));
        }

        [Fact]
        public void ConnectionFileRoundTripsAndRejectsWrongPassword()
        {
            var store = new SyncConnectionProfileStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "sync-profile.dat"));
            var profile = CreateProfile();

            var exported = store.Export(profile, "shared passphrase");
            var imported = store.Import(exported, "shared passphrase");

            Assert.Equal(profile.WebDavUrl, imported.WebDavUrl);
            Assert.Equal(profile.UserName, imported.UserName);
            Assert.Equal(profile.ApplicationPassword, imported.ApplicationPassword);
            Assert.Equal(profile.DataKey, imported.DataKey);
            var error = Assert.Throws<SyncSecurityException>(() => store.Import(exported, "incorrect passphrase"));
            Assert.Equal(SyncErrorCodes.InvalidProfile, error.ErrorCode);
        }

        [Theory]
        [InlineData("https://dav.jianguoyun.com/dav/", true)]
        [InlineData("https://DAV.JIANGUOYUN.COM/dav/", true)]
        [InlineData("http://dav.jianguoyun.com/dav/", false)]
        [InlineData("https://user@dav.jianguoyun.com/dav/", false)]
        [InlineData("https://dav.jianguoyun.com:444/dav/", false)]
        [InlineData("https://127.0.0.1/dav/", false)]
        [InlineData("https://dav.jianguoyun.com/other/", false)]
        public void WebDavUrlPolicyAllowsOnlyCanonicalJianguoyunEndpoint(string url, bool expected)
        {
            Assert.Equal(expected, SyncWebDavUrlPolicy.IsAllowed(url));
        }

        [Fact]
        public void LocalProfileUsesWindowsDpapiAndRejectsCorruption()
        {
            if (!OperatingSystem.IsWindows()) return;
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "sync-profile.dat");
            var store = new SyncConnectionProfileStore(path);
            var profile = CreateProfile();

            store.SaveLocal(profile);
            var protectedBytes = File.ReadAllBytes(path);

            Assert.DoesNotContain(profile.ApplicationPassword, System.Text.Encoding.UTF8.GetString(protectedBytes));
            Assert.Equal(profile.SpaceId, store.LoadLocal().SpaceId);
            protectedBytes[^1] ^= 0x20;
            File.WriteAllBytes(path, protectedBytes);
            Assert.Throws<SyncSecurityException>(() => store.LoadLocal());
        }

        private static SyncConnectionProfile CreateProfile()
        {
            return new SyncConnectionProfile
            {
                WebDavUrl = "https://dav.jianguoyun.com/dav/",
                UserName = "account@example.test",
                ApplicationPassword = "application-password",
                SpaceId = "space-1",
                WorkspaceName = "Workspace",
                DataKey = RandomNumberGenerator.GetBytes(32)
            };
        }
    }
}
