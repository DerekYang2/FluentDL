using System.Runtime.InteropServices;
using System.Text;

namespace FluentDL.Helpers;

internal static class AuthenticationIcons
{
    private static readonly Lazy<bool> hasPasskey = new(() => HasGlyph("Segoe Fluent Icons", '\uEE7E'));

    public static string ProviderGlyph => SelectProviderGlyph(hasPasskey.Value);

    internal static string SelectProviderGlyph(bool supportsPasskey) => supportsPasskey ? "\uEE7E" : "\uEC19";

    // Windows 10 can lack the Fluent font, and older Windows 11 fonts can lack this newer glyph.
    internal static bool HasGlyph(string family, char character)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return false;
        var font = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            font = CreateFontW(-20, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, family);
            if (font == IntPtr.Zero) return false;
            previous = SelectObject(dc, font);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1)) return false;
            var actualFamily = new StringBuilder(128);
            if (GetTextFaceW(dc, actualFamily.Capacity, actualFamily) == 0
                || !string.Equals(actualFamily.ToString(), family, StringComparison.OrdinalIgnoreCase)) return false;
            var indices = new ushort[1];
            return GetGlyphIndicesW(dc, character.ToString(), 1, indices, 1) != uint.MaxValue
                && indices[0] is not (0 or ushort.MaxValue);
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(dc, previous);
            if (font != IntPtr.Zero) DeleteObject(font);
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeout, uint charset, uint outputPrecision, uint clipPrecision, uint quality,
        uint pitchAndFamily, string face);
    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetTextFaceW(IntPtr dc, int count, StringBuilder face);
    [DllImport("gdi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint GetGlyphIndicesW(IntPtr dc, string text, int count, [Out] ushort[] indices, uint flags);
}
