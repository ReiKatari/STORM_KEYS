using System;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace StormKeys.Helpers;

public static class BrushHelper
{
    public static SolidColorBrush FromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return new SolidColorBrush(Colors.Gray);

        try
        {
            hex = hex.Trim().TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                return new SolidColorBrush(Color.FromArgb(255, r, g, b));
            }
            else if (hex.Length == 8)
            {
                byte a = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                byte r = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);
                return new SolidColorBrush(Color.FromArgb(a, r, g, b));
            }
        }
        catch { }

        return new SolidColorBrush(Colors.Gray);
    }
}
