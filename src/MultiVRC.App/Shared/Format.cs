namespace MultiVRC.App.Shared;

public static class Format
{
    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };

    public static string Ago(DateTimeOffset? when)
    {
        if (when is not { } value) return "never";
        var span = DateTimeOffset.UtcNow - value;
        return span.TotalMinutes < 1 ? "just now"
            : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago"
            : span.TotalDays < 2 ? $"{(int)span.TotalHours} h ago"
            : span.TotalDays < 60 ? $"{(int)span.TotalDays} days ago"
            : value.LocalDateTime.ToString("d MMM yyyy");
    }

    public static string Played(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min" : $"{(int)span.TotalMinutes} min";
}
