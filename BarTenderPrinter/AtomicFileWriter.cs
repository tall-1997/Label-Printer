using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BarTenderPrinter
{
    public static class AtomicFileWriter
    {
        public static void WriteAllText(string path, string content, Encoding encoding = null)
        {
            var tempPath = path + "." + System.Guid.NewGuid().ToString("N") + ".tmp";
            var backupPath = path + ".bak";
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
            var mutexName = "Global\\BarTenderPrinter-" + System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path))));
            using (var mutex = new System.Threading.Mutex(false, mutexName))
            {
                var lockTaken = false;
                try
                {
                    try
                    {
                        lockTaken = mutex.WaitOne();
                    }
                    catch (System.Threading.AbandonedMutexException)
                    {
                        lockTaken = true;
                    }
                    File.WriteAllText(tempPath, content ?? "", encoding ?? Encoding.UTF8);
                    if (File.Exists(path)) File.Copy(path, backupPath, true);
                    File.Move(tempPath, path, true);
                }
                finally
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    if (lockTaken) mutex.ReleaseMutex();
                }
            }
        }

        public static void WriteAllLines(string path, IEnumerable<string> lines, Encoding encoding = null)
        {
            WriteAllText(path, string.Join(System.Environment.NewLine, lines ?? new string[0]), encoding);
        }
    }
}
