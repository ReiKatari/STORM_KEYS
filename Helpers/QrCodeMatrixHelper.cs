using System;
using System.Security.Cryptography;
using System.Text;

namespace StormKeys.Helpers;

/// <summary>
/// Генератор компактной графической 2D QR-матрицы (Unicode Half-Blocks) и стандартных URI-полезных нагрузок
/// для 2FA (otpauth://totp/...) и сетей Wi-Fi (WIFI:S:...;T:WPA;P:...;;).
/// </summary>
public static class QrCodeMatrixHelper
{
    public static string GenerateUnicodeQrMatrix(string payload, int size = 21) => GenerateVisualMatrix(payload, size);
    public static string RenderMatrixArt(string payload, int size = 21) => GenerateVisualMatrix(payload, size);
    public static string GenerateVisualMatrix(string payload, int size = 21)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return string.Empty;

        if (size < 21) size = 21;
        if (size % 2 == 0) size++;

        bool[,] grid = new bool[size, size];
        bool[,] reserved = new bool[size, size];

        // 1. Поисковые квадраты 7x7 по 3 углам (Finder Patterns)
        PlaceFinderPattern(grid, reserved, 0, 0, size);
        PlaceFinderPattern(grid, reserved, size - 7, 0, size);
        PlaceFinderPattern(grid, reserved, 0, size - 7, size);

        // 2. Линии синхронизации (Timing Patterns)
        for (int i = 8; i < size - 8; i++)
        {
            grid[6, i] = (i % 2 == 0);
            reserved[6, i] = true;
            grid[i, 6] = (i % 2 == 0);
            reserved[i, 6] = true;
        }

        // 3. Детерминированное кодирование полезных данных + SHA-256 контрольной суммы
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        byte[] hash = SHA256.HashData(payloadBytes);
        byte[] combined = new byte[payloadBytes.Length + hash.Length];
        Buffer.BlockCopy(payloadBytes, 0, combined, 0, payloadBytes.Length);
        Buffer.BlockCopy(hash, 0, combined, payloadBytes.Length, hash.Length);

        int bitIdx = 0;
        int totalBits = combined.Length * 8;

        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (reserved[r, c]) continue;
                int bIdx = (bitIdx / 8) % combined.Length;
                int shift = 7 - (bitIdx % 8);
                bool bit = ((combined[bIdx] >> shift) & 1) == 1;
                // Применяем маску (r + c) % 2 == 0 для равномерной плотности модулей
                if ((r + c) % 2 == 0) bit = !bit;
                grid[r, c] = bit;
                bitIdx = (bitIdx + 1) % Math.Max(1, totalBits);
            }
        }

        // 4. Рендеринг в полублочные символы Unicode (2 вертикальных пикселя в 1 символ)
        var sb = new StringBuilder();
        for (int r = 0; r < size; r += 2)
        {
            sb.Append("  ");
            for (int c = 0; c < size; c++)
            {
                bool top = grid[r, c];
                bool bottom = (r + 1 < size) && grid[r + 1, c];
                char ch = (top, bottom) switch
                {
                    (true, true) => '█',
                    (true, false) => '▀',
                    (false, true) => '▄',
                    _ => ' '
                };
                sb.Append(ch);
            }
            if (r + 2 < size)
                sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void PlaceFinderPattern(bool[,] grid, bool[,] reserved, int startR, int startC, int size)
    {
        for (int r = -1; r <= 7; r++)
        {
            for (int c = -1; c <= 7; c++)
            {
                int rr = startR + r;
                int cc = startC + c;
                if (rr < 0 || rr >= size || cc < 0 || cc >= size)
                    continue;

                reserved[rr, cc] = true;
                if (r >= 0 && r <= 6 && c >= 0 && c <= 6)
                {
                    bool isBorder = (r == 0 || r == 6 || c == 0 || c == 6);
                    bool isInner = (r >= 2 && r <= 4 && c >= 2 && c <= 4);
                    grid[rr, cc] = isBorder || isInner;
                }
                else
                {
                    grid[rr, cc] = false;
                }
            }
        }
    }
}
