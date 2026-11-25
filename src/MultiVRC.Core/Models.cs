using System.Text.Json.Serialization;

namespace MultiVRC.Core;

[JsonConverter(typeof(JsonStringEnumConverter<InstanceKind>))]
public enum InstanceKind
{
    Official,

    Modded,
}

public sealed class Build
{
    public required string Id { get; set; }
    public required string Name { get; set; }

    public string Version { get; set; } = "";

    public string? ManifestId { get; set; }

    public string? Branch { get; set; }

    public long? SteamBuildId { get; set; }

    public string Source { get; set; } = "steam";

    public long SizeBytes { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<BackgroundMode>))]
public enum BackgroundMode
{
    Server,
    Pictures,
    VRChatPhotos,
}

public sealed class Instance
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string BuildId { get; set; }
    public InstanceKind Kind { get; set; }

    public string? TemplateId { get; set; }

    public string Server { get; set; } = "VRChat";

    public string Executable { get; set; } = "VRChat.exe";

    public List<string> Arguments { get; set; } = [];

    public bool ShareCache { get; set; }

    public string Accent { get; set; } = "#3fb68b";

    public string? ArtSeed { get; set; }

    public string? BackgroundFile { get; set; }

    public BackgroundMode BackgroundMode { get; set; } = BackgroundMode.Server;

    public int BackgroundSeconds { get; set; } = 30;

    public bool BackgroundShuffle { get; set; } = true;

    public string? TrackBranch { get; set; }

    public string? TemplateVersion { get; set; }

    public bool AutoUpdate { get; set; } = true;

    public string Notes { get; set; } = "";

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }
    public TimeSpan TotalPlayed { get; set; }
}

public sealed class Template
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public string Server { get; set; } = "";
    public InstanceKind Kind { get; set; } = InstanceKind.Modded;

    public string Version { get; set; } = "";

    public TemplateBuild Build { get; set; } = new();

    public List<TemplateDownload> Downloads { get; set; } = [];

    public List<TemplateFile> Files { get; set; } = [];

    public List<TemplateCopy> Copies { get; set; } = [];

    public string Executable { get; set; } = "VRChat.exe";
    public List<string> Arguments { get; set; } = [];

    public List<TemplatePlayMode> PlayModes { get; set; } = [];

    public bool ShareCache { get; set; }
    public string Accent { get; set; } = "#5b9be0";

    public Branding Branding { get; set; } = new();

    public string? SourceUrl { get; set; }

    public bool Bundled { get; set; }
}

public sealed class TemplatePlayMode
{
    public required string Label { get; set; }

    public string? Icon { get; set; }

    public List<string> Arguments { get; set; } = [];

    public string? Executable { get; set; }
}

public sealed class TemplateBuild
{
    public string Version { get; set; } = "";
    public string? ManifestId { get; set; }

    public string? Branch { get; set; }
}

public sealed class TemplateDownload
{
    public required string Url { get; set; }

    public string Path { get; set; } = "";

    public bool Extract { get; set; }

    public string? Sha256 { get; set; }
}

public sealed class TemplateFile
{
    public required string Path { get; set; }
    public string Content { get; set; } = "";
}

public sealed class TemplateCopy
{
    public required string From { get; set; }
    public required string To { get; set; }

    public bool Junction { get; set; }
}

public sealed class Branding
{
    public string Tagline { get; set; } = "";

    public string? BackgroundUrl { get; set; }

    public string? IconUrl { get; set; }

    public string? IconBackground { get; set; }

    public NewsSource? News { get; set; }

    public List<BrandLink> Links { get; set; } = [];
}

public sealed class NewsSource
{
    public string Type { get; set; } = "rss";
    public required string Url { get; set; }
}

public sealed class BrandLink
{
    public required string Label { get; set; }
    public required string Url { get; set; }

    public string Kind { get; set; } = "website";
}

public sealed record NewsItem(string Title, string Summary, string? ImageUrl, string? Url, DateTimeOffset? PublishedAt);
