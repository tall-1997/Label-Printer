using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BarTenderPrinter
{
    public static class AtomicFileWriter
    {
        public static void WriteAllText(string path, string content, Encoding encoding = null)
        {
            var tempPath = path + ".tmp";
            var backupPath = path + ".bak";
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(tempPath, content ?? "", encoding ?? Encoding.UTF8);
            if (File.Exists(path)) File.Copy(path, backupPath, true);
            File.Move(tempPath, path, true);
        }

        public static void WriteAllLines(string path, IEnumerable<string> lines, Encoding encoding = null)
        {
            WriteAllText(path, string.Join(System.Environment.NewLine, lines ?? new string[0]), encoding);
        }
    }
}
