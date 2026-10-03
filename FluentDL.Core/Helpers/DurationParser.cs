namespace FluentDL.Core.Helpers;

public static class DurationParser
{
    // Sources store durations as whole or fractional seconds, or as clock text such as "3:45" or "75:30".
    public static bool TryParse(object? value, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        switch (value)
        {
            case int seconds:
                duration = TimeSpan.FromSeconds(seconds);
                return true;
            case string text when !string.IsNullOrWhiteSpace(text):
                if (double.TryParse(text, out var totalSeconds))
                {
                    duration = TimeSpan.FromSeconds(Math.Round(totalSeconds));
                    return true;
                }
                return TryParseClock(text, out duration);
            default:
                return false;
        }
    }

    private static bool TryParseClock(string text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var parts = text.Split(':');
        if (parts.Length is not (2 or 3)) return false;

        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0) return false;
        }

        duration = parts.Length == 2
            ? new TimeSpan(0, numbers[0], numbers[1])
            : new TimeSpan(numbers[0], numbers[1], numbers[2]);
        return true;
    }
}
