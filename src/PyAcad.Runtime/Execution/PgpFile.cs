using System;
using System.Collections.Generic;

namespace PyAcad.Runtime.Execution
{
    /// <summary>
    /// The names acad.pgp defines, so an alias never takes one a user already types. Each
    /// line is NAME, then a comma; ';' starts a comment. External commands
    /// (<c>NOTEPAD, START NOTEPAD, 1,...</c>) and aliases (<c>CO, *COPY</c>) both count.
    /// Read only: pyACAD never writes acad.pgp. Pure.
    /// </summary>
    public static class PgpFile
    {
        public static ISet<string> Names(string text)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in (text ?? "").Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == ';') continue;
                var comma = trimmed.IndexOf(',');
                if (comma <= 0) continue;
                var name = trimmed.Substring(0, comma).Trim();
                if (name.Length > 0 && name.IndexOf(' ') < 0) names.Add(name.ToUpperInvariant());
            }
            return names;
        }
    }
}
