using System.Globalization;

namespace MultiVRC.App.Shared;

public static class Art
{
    public static readonly string[] Palette =
        ["#3fb68b", "#5b9be0", "#9b7bf0", "#e06c9f", "#e0a84a", "#4fc1d6", "#f07b5a", "#8bc34a"];

    public static string RandomAccent() => Palette[Random.Shared.Next(Palette.Length)];

    public static string Background(string seed, string accent)
    {
        var hash = Hash(seed);
        var (h, s, l) = ToHsl(accent);
        string Glow(int index, double hueShift, double alpha)
        {
            var x = (int)((hash >> (index * 8)) % 100);
            var y = (int)((hash >> (index * 8 + 4)) % 100);
            var hue = (h + hueShift + 360) % 360;
            return $"radial-gradient(circle at {x}% {y}%, hsla({hue:0},{Math.Min(95, s + 10):0}%,{Math.Clamp(l, 45, 65):0}%,{alpha.ToString(CultureInfo.InvariantCulture)}) 0%, transparent 55%)";
        }
        var tilt = (int)(hash % 360);
        return string.Join(", ",
            Glow(0, 0, 0.85), Glow(1, 38, 0.55), Glow(2, -42, 0.45),
            $"linear-gradient({tilt}deg, hsl({h:0},{s * 0.5:0}%,10%), hsl({(h + 30) % 360:0},{s * 0.4:0}%,6%))");
    }

    public static string Pattern(string seed) => (Hash(seed) % 4) switch
    {
        0 => "pattern-dots",
        1 => "pattern-grid",
        2 => "pattern-diagonal",
        _ => "pattern-rings",
    };

    public static string Monogram(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)],
            _ => $"{words[0][0]}{words[1][0]}",
        };
    }

    private static ulong Hash(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in text) { hash ^= c; hash *= 1099511628211UL; }
        return hash;
    }

    private static (double H, double S, double L) ToHsl(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#') hex = "#3fb68b";
        var r = Convert.ToInt32(hex[1..3], 16) / 255.0;
        var g = Convert.ToInt32(hex[3..5], 16) / 255.0;
        var b = Convert.ToInt32(hex[5..7], 16) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min) return (0, 0, l * 100);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s * 100, l * 100);
    }
}
