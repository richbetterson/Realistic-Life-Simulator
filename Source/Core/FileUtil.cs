// Realistic Life Simulator - file helpers (no game dependencies).
// NOTE: this project is compiled with the C# 5 compiler that ships with Windows
// (.NET Framework 4.x csc.exe). Do not use C# 6+ syntax ($"", ?., =>, nameof).

using System;
using System.IO;
using System.Text;

namespace RealisticLifeSimulator.Core
{
    public static class FileUtil
    {
        public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void WriteAllTextAtomic(string path, string text)
        {
            WriteAllBytesAtomic(path, Utf8NoBom.GetBytes(text));
        }

        // Writes to a temporary file first, then swaps it in, so a crash or power loss
        // mid-write never leaves a half-written file behind.
        public static void WriteAllBytesAtomic(string path, byte[] bytes)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, null);
                    return;
                }
                catch (Exception)
                {
                    // Some file systems do not support Replace; fall back to copy.
                }
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        public static string Timestamp()
        {
            return DateTime.Now.ToString("yyyyMMdd-HHmmss");
        }

        // Moves a file out of the way (never deletes it). Returns the new path.
        public static string MoveAside(string path, string tag, string destDir)
        {
            Directory.CreateDirectory(destDir);
            string name = Path.GetFileName(path) + "." + tag + "-" + Timestamp();
            string dest = Path.Combine(destDir, name);
            int n = 1;
            while (File.Exists(dest))
            {
                dest = Path.Combine(destDir, name + "-" + n);
                n++;
            }
            File.Move(path, dest);
            return dest;
        }

        // Copies a file aside (keeps the original). Returns the new path.
        public static string CopyAside(string path, string tag, string destDir)
        {
            Directory.CreateDirectory(destDir);
            string name = Path.GetFileName(path) + "." + tag + "-" + Timestamp();
            string dest = Path.Combine(destDir, name);
            int n = 1;
            while (File.Exists(dest))
            {
                dest = Path.Combine(destDir, name + "-" + n);
                n++;
            }
            File.Copy(path, dest);
            return dest;
        }
    }
}
