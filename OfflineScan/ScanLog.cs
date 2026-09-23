using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OfflineScan
{
    /// <summary>
    /// Diagnostinis žurnalas. Failai rašomi į aplanką "Logs" šalia programos (.exe).
    /// Jei ten rašyti negalima (pvz., USB apsaugota nuo rašymo), naudojamas
    /// Dokumentai\OfflineScan\Logs. Kiekvienas programos paleidimas yra atskiras failas.
    /// </summary>
    public static class ScanLog
    {
        private static readonly object Sync = new();
        private static string? _file;

        public static bool Enabled { get; set; } = true;
        public static string Folder { get; } = ResolveFolder();
        public static string? CurrentFile => _file;

        private static string ResolveFolder()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Logs");
            try
            {
                Directory.CreateDirectory(dir);
                string test = Path.Combine(dir, ".write_test");
                File.WriteAllText(test, "");
                File.Delete(test);
                return dir;
            }
            catch
            {
                string alt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OfflineScan", "Logs");
                try { Directory.CreateDirectory(alt); } catch { }
                return alt;
            }
        }

        public static void StartSession()
        {
            Section("PROGRAMA PALEISTA");
            var asm = System.Reflection.Assembly.GetEntryAssembly();
            Write($"Versija: {asm?.GetName().Version}\n" +
                  $"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})\n" +
                  $".NET: {RuntimeInformation.FrameworkDescription}, procesas {(Environment.Is64BitProcess ? 64 : 32)} bit\n" +
                  $"Programos aplankas: {AppContext.BaseDirectory}\n" +
                  $"Žurnalo aplankas: {Folder}\n" +
                  $"Kompiuteris: {Environment.MachineName}");
        }

        public static void Write(string text)
        {
            if (!Enabled) return;
            lock (Sync)
            {
                try
                {
                    _file ??= Path.Combine(Folder, $"OfflineScan_{DateTime.Now:yyyy-MM-dd_HHmmss}.log");
                    string ts = DateTime.Now.ToString("HH:mm:ss.fff");
                    var sb = new StringBuilder();
                    foreach (var line in text.Replace("\r", "").Split('\n'))
                        sb.Append(ts).Append("  ").Append(line).Append("\r\n");
                    File.AppendAllText(_file, sb.ToString(), Encoding.UTF8); // iškart į diską, kad neprarastume įrašų
                }
                catch { } // žurnalas niekada neturi sutrukdyti skenavimui
            }
        }

        public static void Section(string title) => Write($"==================== {title} ====================");

        public static void Error(string where, Exception ex) =>
            Write($"KLAIDA [{where}] 0x{HR(ex):X8}\n{ex}");

        /// <summary>Tikrasis COM klaidos kodas (ieško ir vidinėse išimtyse).</summary>
        public static int HR(Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
                if (e is COMException c) return c.ErrorCode;
            return ex.HResult;
        }
    }
}
