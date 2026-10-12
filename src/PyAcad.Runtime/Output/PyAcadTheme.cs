using System;
using System.IO;
using System.Windows.Media;
using PyAcad.Runtime.Config;

namespace PyAcad.Runtime.Output
{
    /// <summary>
    /// Answers "is the host UI dark?". Config "theme" key ("dark"/"light") wins;
    /// otherwise AutoCAD's COLORTHEME system variable decides (0 dark, 1 light), and
    /// where that variable is missing the AdWindows ribbon background is sampled, so
    /// this tracks whatever theme the running AutoCAD actually renders - releases
    /// without a dark theme simply always read light. Never throws: unknown = light.
    /// </summary>
    public static class PyAcadTheme
    {
        private static bool? _cached;

        public static bool IsDark
        {
            get
            {
                if (_cached == null)
                {
                    var configPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "pyACAD", "config.json");
                    _cached = ResolveIsDark(PyAcadConfig.Load(configPath).Theme, SampleRibbonIsDark);
                }
                return _cached.Value;
            }
        }

        /// <summary>Drop the cached answer (Reload calls this so a theme change is picked up).</summary>
        public static void Invalidate() => _cached = null;

        /// <summary>Pure precedence: explicit override beats detection; anything else = detect.</summary>
        public static bool ResolveIsDark(string configTheme, Func<bool> detector)
        {
            if (string.Equals(configTheme, "dark", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(configTheme, "light", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                return detector();
            }
            catch (Exception ex)
            {
                Log.Error("Theme detection failed - assuming light.", ex);
                return false;
            }
        }

        /// <summary>Pure: COLORTHEME's value as a dark/light answer, null when it is not a number.
        /// AutoCAD hands it back as a short; a string is accepted for the LISP-ish callers.</summary>
        public static bool? IsDarkFromColorTheme(object colorTheme)
        {
            switch (colorTheme)
            {
                case null: return null;
                case short s: return s == 0;
                case int i: return i == 0;
                case long l: return l == 0;
                case string text when int.TryParse(text.Trim(), out var parsed): return parsed == 0;
                default: return null;
            }
        }

        private static bool SampleRibbonIsDark()
        {
            var fromSysvar = IsDarkFromColorTheme(ReadColorTheme());
            if (fromSysvar != null)
            {
                Log.Info("Theme: COLORTHEME says " + (fromSysvar.Value ? "dark" : "light") + ".");
                return fromSysvar.Value;
            }
            var ribbon = Autodesk.Windows.ComponentManager.Ribbon;
            var brush = ribbon?.Background as SolidColorBrush;
            if (brush == null)
            {
                Log.Info("Theme: no COLORTHEME and the ribbon background is not a solid brush - assuming light.");
                return false;
            }
            var c = brush.Color;
            var dark = ThemeMath.IsDarkColor(c.R, c.G, c.B);
            Log.Info($"Theme: ribbon background {c} reads {(dark ? "dark" : "light")}.");
            return dark;
        }

        private static object ReadColorTheme()
        {
            try { return Host.AcadHostProbe.ColorTheme(); }
            catch (Exception) { return null; } // AcCoreMgd absent (test host)
        }
    }
}
