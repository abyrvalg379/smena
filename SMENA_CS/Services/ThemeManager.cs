using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace SMENA.Services
{
    public class ThemeColors
    {
        public string Name { get; set; } = "";
        public string Bg { get; set; } = "";
        public string Panel { get; set; } = "";
        public string Border { get; set; } = "";
        public string Text { get; set; } = "";
        public string Dim { get; set; } = "";
        public string Accent { get; set; } = "";
    }

    /// <summary>Built-in themes: "smena" (default palette) + the DCC looks from FLOMASTER.</summary>
    public static class ThemeManager
    {
        public static readonly Dictionary<string, ThemeColors> Themes = new()
        {
            ["smena"] = new()
            {
                Name = "SMENA",
                Bg = "#0B0F14",
                Panel = "#111720",
                Border = "#263241",
                Text = "#E8EDF3",
                Dim = "#8995A5",
                Accent = "#5AC8FA"
            },
            ["blender"] = new()
            {
                Name = "Blender",
                Bg = "#161616",
                Panel = "#202020",
                Border = "#303030",
                Text = "#E0E0E0",
                Dim = "#858585",
                Accent = "#E87D0D"
            },
            ["maya"] = new()
            {
                Name = "Maya",
                Bg = "#1B232C",
                Panel = "#2A3642",
                Border = "#3A4958",
                Text = "#EAF1F6",
                Dim = "#92A6B8",
                Accent = "#3FD9E8"
            },
            ["houdini"] = new()
            {
                Name = "Houdini",
                Bg = "#1A181D",
                Panel = "#2C2A2E",
                Border = "#3D3B40",
                Text = "#E6E6E6",
                Dim = "#787779",
                Accent = "#FF4713"
            },
            ["nuke"] = new()
            {
                Name = "Nuke",
                Bg = "#262626",
                Panel = "#333333",
                Border = "#454545",
                Text = "#E8E8E8",
                Dim = "#969696",
                Accent = "#C8C8C8"
            },
            ["davinci"] = new()
            {
                Name = "DaVinci",
                Bg = "#121222",
                Panel = "#1E2340",
                Border = "#2C2C4A",
                Text = "#EDEAF2",
                Dim = "#8E8CAB",
                Accent = "#FF4D6D"
            },
            ["unreal"] = new()
            {
                Name = "Unreal",
                Bg = "#1C1F24",
                Panel = "#262A31",
                Border = "#33383F",
                Text = "#E0E0E0",
                Dim = "#8A919C",
                Accent = "#3D9BFF"
            },
            ["substance"] = new()
            {
                Name = "Substance",
                Bg = "#0D0F0D",
                Panel = "#161A16",
                Border = "#262B26",
                Text = "#E0E0E0",
                Dim = "#8FA08F",
                Accent = "#76B900"
            }
        };

        public static readonly List<string> Order = new()
        {
            "smena", "blender", "maya", "houdini", "nuke", "davinci", "unreal", "substance"
        };

        public static ThemeColors GetTheme(string key)
        {
            return Themes.TryGetValue(key, out var theme) ? theme : Themes["smena"];
        }

        public static string KeyOfName(string? displayName)
        {
            return Order.FirstOrDefault(k => Themes[k].Name == displayName) ?? "smena";
        }

        public static SolidColorBrush Brush(string color)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            b.Freeze();
            return b;
        }
    }
}
