using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PyAcad.Runtime
{
    /// <summary>
    /// Reads text files people wrote by hand (bundle.yaml, script.py), in whatever their
    /// editor saved. File.ReadAllText assumes UTF-8, so a file saved as ANSI came back
    /// with every accented character replaced by U+FFFD and no error: a mangled button
    /// title at best, a silently different string literal at worst.
    /// </summary>
    public static class TextFiles
    {
        /// <summary>
        /// BOM first (UTF-8 / UTF-16), then strict UTF-8, and only when the bytes are not
        /// valid UTF-8 the machine's ANSI code page. Valid UTF-8 is never misread as ANSI:
        /// multi-byte UTF-8 sequences are vanishingly unlikely in real ANSI text.
        /// </summary>
        /// <param name="ansi">The fallback code page; the system default when null. A seam
        /// so the tests do not depend on the machine's locale.</param>
        public static string Read(string path, Encoding ansi = null)
        {
            var bytes = File.ReadAllBytes(path);

            if (HasPreamble(bytes, Encoding.UTF8)) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (HasPreamble(bytes, Encoding.Unicode)) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (HasPreamble(bytes, Encoding.BigEndianUnicode))
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            try
            {
                return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return (ansi ?? SystemAnsi).GetString(bytes);
            }
        }

        /// <summary>The machine's ANSI code page (what Notepad calls ANSI). Not
        /// Encoding.Default: on .NET 8 that is UTF-8, which would mangle exactly the
        /// files this class exists for.</summary>
        public static Encoding SystemAnsi => CodePage(GetACP());

        /// <summary>A Windows code page by number. .NET 8 ships only the Unicode code
        /// pages until the code-pages provider is registered, so this registers it first.</summary>
        public static Encoding CodePage(int codePage)
        {
#if !NETFRAMEWORK
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
            return Encoding.GetEncoding(codePage);
        }

        [DllImport("kernel32.dll")]
        private static extern int GetACP();

        private static bool HasPreamble(byte[] bytes, Encoding encoding)
        {
            var preamble = encoding.GetPreamble();
            if (bytes.Length < preamble.Length) return false;
            for (var i = 0; i < preamble.Length; i++)
                if (bytes[i] != preamble[i]) return false;
            return true;
        }
    }
}
