using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace StormKeys.Services;

public class ThemeDefinition
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string BackgroundHex { get; set; } = string.Empty;
    public string HeaderHex { get; set; } = string.Empty;
    public string SurfaceHex { get; set; } = string.Empty;
    public string CardHex { get; set; } = string.Empty;
    public string CardStrokeHex { get; set; } = string.Empty;
    public string TextPrimaryHex { get; set; } = string.Empty;
    public string TextSecondaryHex { get; set; } = string.Empty;
    public bool IsLight { get; set; }
}

public class AccentDefinition
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Hex { get; set; } = string.Empty;
}

public class FontDefinition
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FontFamily { get; set; } = string.Empty;
}

public static class ThemeService
{
    public static readonly List<ThemeDefinition> Themes = new()
    {
        new()
        {
            Key = "MIDNIGHT",
            DisplayName = "MIDNIGHT (Фиолетовая)",
            BackgroundHex = "#0D0B18",
            HeaderHex = "#141026",
            SurfaceHex = "#17132B",
            CardHex = "#1F193B",
            CardStrokeHex = "#372E63",
            TextPrimaryHex = "#FFFFFF",
            TextSecondaryHex = "#A79EC8",
            IsLight = false
        },
        new()
        {
            Key = "NIGHT",
            DisplayName = "NIGHT (OLED)",
            BackgroundHex = "#000000",
            HeaderHex = "#050507",
            SurfaceHex = "#08080C",
            CardHex = "#0F0F14",
            CardStrokeHex = "#26262E",
            TextPrimaryHex = "#FFFFFF",
            TextSecondaryHex = "#888899",
            IsLight = false
        },
        new()
        {
            Key = "DARK",
            DisplayName = "DARK (Тёмная)",
            BackgroundHex = "#111318",
            HeaderHex = "#161920",
            SurfaceHex = "#1C2028",
            CardHex = "#242934",
            CardStrokeHex = "#3A4252",
            TextPrimaryHex = "#F4F4F5",
            TextSecondaryHex = "#9CA3AF",
            IsLight = false
        },
        new()
        {
            Key = "DAY",
            DisplayName = "DAY (Светлая)",
            BackgroundHex = "#F8FAFC",
            HeaderHex = "#FFFFFF",
            SurfaceHex = "#F1F5F9",
            CardHex = "#FFFFFF",
            CardStrokeHex = "#CBD5E1",
            TextPrimaryHex = "#0F172A",
            TextSecondaryHex = "#475569",
            IsLight = true
        },
        new()
        {
            Key = "FANTASY",
            DisplayName = "FANTASY (Индиго)",
            BackgroundHex = "#0E1324",
            HeaderHex = "#151C35",
            SurfaceHex = "#1B2444",
            CardHex = "#222D54",
            CardStrokeHex = "#3D4F88",
            TextPrimaryHex = "#F8FAFC",
            TextSecondaryHex = "#94A3B8",
            IsLight = false
        },
        new()
        {
            Key = "CYBERPUNK",
            DisplayName = "CYBERPUNK (2077)",
            BackgroundHex = "#0B0A10",
            HeaderHex = "#12101B",
            SurfaceHex = "#191626",
            CardHex = "#231F35",
            CardStrokeHex = "#FFE600",
            TextPrimaryHex = "#FFFFFF",
            TextSecondaryHex = "#FFE600",
            IsLight = false
        },
        new()
        {
            Key = "WARHAMMER 40K",
            DisplayName = "WARHAMMER 40K",
            BackgroundHex = "#0C0A08",
            HeaderHex = "#15120E",
            SurfaceHex = "#1C1813",
            CardHex = "#26211B",
            CardStrokeHex = "#78350F",
            TextPrimaryHex = "#FAF7F2",
            TextSecondaryHex = "#A8A29E",
            IsLight = false
        },
        new()
        {
            Key = "ELEMENTAL",
            DisplayName = "ELEMENTAL",
            BackgroundHex = "#091318",
            HeaderHex = "#0E1D24",
            SurfaceHex = "#132832",
            CardHex = "#1A3542",
            CardStrokeHex = "#047857",
            TextPrimaryHex = "#ECFDF5",
            TextSecondaryHex = "#6EE7B7",
            IsLight = false
        },
        new()
        {
            Key = "STORM",
            DisplayName = "STORM (Шторм)",
            BackgroundHex = "#0C0A1D",
            HeaderHex = "#141033",
            SurfaceHex = "#1A1542",
            CardHex = "#231C59",
            CardStrokeHex = "#8B5CF6",
            TextPrimaryHex = "#FFFFFF",
            TextSecondaryHex = "#C4B5FD",
            IsLight = false
        }
    };

    public static readonly List<AccentDefinition> Accents = new()
    {
        new() { Key = "Violet", DisplayName = "Violet (#8B5CF6)", Hex = "#8B5CF6" },
        new() { Key = "Cyan", DisplayName = "Cyan (#06B6D4)", Hex = "#06B6D4" },
        new() { Key = "Electric Blue", DisplayName = "Electric Blue (#3B82F6)", Hex = "#3B82F6" },
        new() { Key = "Royal Indigo", DisplayName = "Royal Indigo (#6366F1)", Hex = "#6366F1" },
        new() { Key = "Teal Mint", DisplayName = "Teal Mint (#14B8A6)", Hex = "#14B8A6" },
        new() { Key = "Emerald Green", DisplayName = "Emerald Green (#10B981)", Hex = "#10B981" },
        new() { Key = "Neon Lime", DisplayName = "Neon Lime (#84CC16)", Hex = "#84CC16" },
        new() { Key = "Amber Gold", DisplayName = "Amber Gold (#F59E0B)", Hex = "#F59E0B" },
        new() { Key = "Solar Orange", DisplayName = "Solar Orange (#F97316)", Hex = "#F97316" },
        new() { Key = "Crimson Red", DisplayName = "Crimson Red (#EF4444)", Hex = "#EF4444" },
        new() { Key = "Rose Pink", DisplayName = "Rose Pink (#F43F5E)", Hex = "#F43F5E" },
        new() { Key = "Magenta Fuchsia", DisplayName = "Magenta Fuchsia (#EC4899)", Hex = "#EC4899" },
        new() { Key = "Platinum Silver", DisplayName = "Platinum Silver (#E2E8F0)", Hex = "#E2E8F0" }
    };

    public static readonly List<FontDefinition> Fonts = new()
    {
        new() { Key = "Inter Variable", DisplayName = "Inter Variable", FontFamily = "Inter Variable, Segoe UI Variable Text, Segoe UI" },
        new() { Key = "Plus Jakarta Sans", DisplayName = "Plus Jakarta Sans", FontFamily = "Plus Jakarta Sans, Segoe UI Variable Text, Segoe UI" },
        new() { Key = "Geist Sans", DisplayName = "Geist Sans", FontFamily = "Geist Sans, Segoe UI Variable Text, Segoe UI" },
        new() { Key = "Manrope", DisplayName = "Manrope", FontFamily = "Manrope, Segoe UI Variable Text, Segoe UI" },
        new() { Key = "Outfit", DisplayName = "Outfit", FontFamily = "Outfit, Segoe UI Variable Text, Segoe UI" },
        new() { Key = "Montserrat", DisplayName = "Montserrat", FontFamily = "Montserrat, Segoe UI Variable Text, Segoe UI" },
        new() { Key = "Rubik", DisplayName = "Rubik", FontFamily = "Rubik, Segoe UI Variable Text, Segoe UI" },
        new() { Key = "Orbitron", DisplayName = "Orbitron", FontFamily = "Orbitron, Rajdhani, Segoe UI" },
        new() { Key = "JetBrains Mono", DisplayName = "JetBrains Mono", FontFamily = "JetBrains Mono, Cascadia Code, Consolas" },
        new() { Key = "Cascadia Code", DisplayName = "Cascadia Code", FontFamily = "Cascadia Code, Consolas" }
    };

    public static ThemeDefinition GetTheme(string nameOrKey)
    {
        return Themes.FirstOrDefault(t => t.DisplayName.Equals(nameOrKey, StringComparison.OrdinalIgnoreCase) ||
                                          t.Key.Equals(nameOrKey, StringComparison.OrdinalIgnoreCase) ||
                                          nameOrKey.StartsWith(t.Key, StringComparison.OrdinalIgnoreCase))
               ?? Themes[0];
    }

    public static AccentDefinition GetAccent(string nameOrKey)
    {
        return Accents.FirstOrDefault(a => a.DisplayName.Equals(nameOrKey, StringComparison.OrdinalIgnoreCase) ||
                                           a.Key.Equals(nameOrKey, StringComparison.OrdinalIgnoreCase) ||
                                           nameOrKey.StartsWith(a.Key, StringComparison.OrdinalIgnoreCase))
               ?? Accents[0];
    }

    public static FontDefinition GetFont(string nameOrKey)
    {
        return Fonts.FirstOrDefault(f => f.DisplayName.Equals(nameOrKey, StringComparison.OrdinalIgnoreCase) ||
                                         f.Key.Equals(nameOrKey, StringComparison.OrdinalIgnoreCase))
               ?? Fonts[0];
    }

    public static Color ColorFromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Colors.Transparent;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6)
        {
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return Color.FromArgb(255, r, g, b);
        }
        if (hex.Length == 8)
        {
            byte a = Convert.ToByte(hex.Substring(0, 2), 16);
            byte r = Convert.ToByte(hex.Substring(2, 2), 16);
            byte g = Convert.ToByte(hex.Substring(4, 2), 16);
            byte b = Convert.ToByte(hex.Substring(6, 2), 16);
            return Color.FromArgb(a, r, g, b);
        }
        return Colors.Transparent;
    }

    public static void ApplyToResources(string themeKey, string accentKey, string fontKey, FrameworkElement? root = null)
    {
        try
        {
            var theme = GetTheme(themeKey);
            var accent = GetAccent(accentKey);
            var font = GetFont(fontKey);

            var accentColor = ColorFromHex(accent.Hex);
            var bgColor = ColorFromHex(theme.BackgroundHex);
            var headerColor = ColorFromHex(theme.HeaderHex);
            var surfaceColor = ColorFromHex(theme.SurfaceHex);
            var cardColor = ColorFromHex(theme.CardHex);
            var strokeColor = ColorFromHex(theme.CardStrokeHex);
            var textPrimary = ColorFromHex(theme.TextPrimaryHex);
            var textSecondary = ColorFromHex(theme.TextSecondaryHex);

            var res = Application.Current.Resources;
            res["ApplicationPageBackgroundThemeBrush"] = new SolidColorBrush(bgColor);
            res["SolidBackgroundFillColorBaseBrush"] = new SolidColorBrush(headerColor);
            res["LayerOnMicaBaseAltFillColorDefaultBrush"] = new SolidColorBrush(surfaceColor);
            res["CardBackgroundFillColorDefaultBrush"] = new SolidColorBrush(cardColor);
            res["CardStrokeColorDefaultBrush"] = new SolidColorBrush(strokeColor);
            res["TextFillColorPrimaryBrush"] = new SolidColorBrush(textPrimary);
            res["TextFillColorSecondaryBrush"] = new SolidColorBrush(textSecondary);

            res["SystemAccentColor"] = accentColor;
            res["AccentFillColorDefaultBrush"] = new SolidColorBrush(accentColor);
            res["AccentFillColorSecondaryBrush"] = new SolidColorBrush(accentColor);
            res["AccentFillColorTertiaryBrush"] = new SolidColorBrush(accentColor);

            if (root != null)
            {
                root.RequestedTheme = theme.IsLight ? ElementTheme.Light : ElementTheme.Dark;
            }
        }
        catch { }
    }
}
