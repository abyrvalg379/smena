using System.IO;

namespace SMENA.Services
{
    /// <summary>Crash-safe JSON writes: temp file + atomic rename, so a half-written
    /// store can never be read back (the read-race the UCHET-era smokes hit).</summary>
    public static class Atomic
    {
        public static void Write(string path, string content)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, true);
        }
    }
}
