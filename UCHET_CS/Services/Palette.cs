using System;
using System.Windows.Media;

namespace UCHET.Services
{
    /// <summary>Task accent colors: custom ColorHex wins, else stable palette hash, gray for unsorted.</summary>
    public static class Palette
    {
        public static readonly string[] Colors =
        {
            "#5AC8FA", "#4CD964", "#FF9F0A", "#FF6482", "#BF5AF2", "#FFD60A", "#64D2FF", "#30D158"
        };

        public static string HexFor(Guid? taskId, string customHex)
        {
            if (!string.IsNullOrWhiteSpace(customHex)) return customHex;
            if (taskId == null) return "#5A5A64";
            return Colors[Math.Abs(taskId.Value.GetHashCode()) % Colors.Length];
        }

        public static SolidColorBrush BrushFor(Guid? taskId, string customHex) => Frozen(HexFor(taskId, customHex));

        public static SolidColorBrush Frozen(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }
}
