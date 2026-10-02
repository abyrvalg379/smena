using System;
using System.Windows;
using System.Windows.Media;

namespace UCHET.Services
{
    /// <summary>
    /// Applies a theme to Application.Current.Resources. Resource keys use the shell's own
    /// names (Bg/Panel/Fg/…) so XAML binds them with DynamicResource. PanelHi/Track/Hover and
    /// AccentSoft are derived per theme; Green/Red/Orange are semantic and theme-independent.
    /// </summary>
    public static class ThemeApplier
    {
        /// <summary>Fires after resources are replaced — canvases re-render via VM.RefreshAll.</summary>
        public static event Action? ThemeChanged;

        public static void Apply(string themeKey)
        {
            var t = ThemeManager.GetTheme(themeKey);
            var res = Application.Current.Resources;

            var panel = (Color)ColorConverter.ConvertFromString(t.Panel);
            var accent = (Color)ColorConverter.ConvertFromString(t.Accent);

            res["Bg"] = ThemeManager.Brush(t.Bg);
            res["Panel"] = ThemeManager.Brush(t.Panel);
            res["PanelHi"] = Frozen(Shift(panel, 1.35));
            res["Border"] = ThemeManager.Brush(t.Border);
            res["Fg"] = ThemeManager.Brush(t.Text);
            res["FgDim"] = ThemeManager.Brush(t.Dim);
            res["Accent"] = ThemeManager.Brush(t.Accent);
            res["Track"] = Frozen(Shift(panel, 1.45));   // bar backgrounds, hover fills
            res["Hover"] = Frozen(Shift(panel, 1.45));
            res["AccentSoft"] = Frozen(Mix(accent, panel, 0.30));   // tinted fills (start button, toggles)

            // semantic, theme-independent
            res["Green"] = ThemeManager.Brush("#4CD964");
            res["Red"] = ThemeManager.Brush("#FF453A");
            res["Orange"] = ThemeManager.Brush("#FF9F0A");
            res["RedSoft"] = ThemeManager.Brush("#3A1620");
            res["RedHi"] = ThemeManager.Brush("#FF6B62");
            res["UnsortedBrush"] = ThemeManager.Brush("#5A5A64");
            res["IdleBrush"] = ThemeManager.Brush("#4A4A52");

            // system color overrides: ComboBox dropdowns and default templates follow the theme
            res[SystemColors.WindowBrushKey] = ThemeManager.Brush(t.Panel);
            res[SystemColors.WindowTextBrushKey] = ThemeManager.Brush(t.Text);
            res[SystemColors.ControlBrushKey] = ThemeManager.Brush(t.Panel);
            res[SystemColors.ControlTextBrushKey] = ThemeManager.Brush(t.Text);
            res[SystemColors.HighlightBrushKey] = ThemeManager.Brush(t.Accent);
            res[SystemColors.HighlightTextBrushKey] = ThemeManager.Brush(t.Bg);

            ThemeChanged?.Invoke();
        }

        /// <summary>Theme-aware brush for code-drawn UI (canvases, donut); null → caller fallback.</summary>
        public static Brush? OptBrush(string key) =>
            Application.Current?.TryFindResource(key) as Brush;

        private static Color Shift(Color c, double k)
        {
            return Color.FromRgb(
                (byte)Math.Min(255, c.R * k),
                (byte)Math.Min(255, c.G * k),
                (byte)Math.Min(255, c.B * k));
        }

        private static Color Mix(Color a, Color b, double t)
        {
            return Color.FromRgb(
                (byte)(a.R * t + b.R * (1 - t)),
                (byte)(a.G * t + b.G * (1 - t)),
                (byte)(a.B * t + b.B * (1 - t)));
        }

        private static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
