using MultiVRC.App.Shared;
using MultiVRC.Core;

namespace MultiVRC.App.Services;

public sealed class ServerLook(LauncherState state)
{
    public Branding BrandingOf(Instance instance) =>
        instance.TemplateId is { } id && state.Library.FindTemplate(id) is { } template ? template.Branding : new Branding();

    public string? IconOf(Instance instance) => BrandingOf(instance).IconUrl is { Length: > 0 } url ? url : null;

    public string? IconBackgroundOf(Instance instance) => BrandingOf(instance).IconBackground;

    public string TaglineOf(Instance instance)
    {
        var tagline = BrandingOf(instance).Tagline;
        if (tagline.Length > 0) return tagline;
        return instance.Kind == InstanceKind.Official ? "VRChat, as it ships" : $"Private server · {instance.Server}";
    }

    public Task<List<NewsItem>> NewsOf(Instance instance, CancellationToken ct = default) =>
        BrandingOf(instance).News is { } source ? state.News.ReadAsync(source, ct) : Task.FromResult(new List<NewsItem>());

    public string BackgroundCss(Instance instance, IReadOnlyList<NewsItem> news)
    {
        if (BrandingOf(instance).BackgroundUrl is { Length: > 0 } url) return $"url(\"{Css(url)}\") center / cover no-repeat";
        if (news.FirstOrDefault(n => n.ImageUrl is not null)?.ImageUrl is { } picture) return $"url(\"{Css(picture)}\") center / cover no-repeat";
        return Art.Background(instance.ArtSeed ?? instance.Id, instance.Accent);
    }

    private static string Css(string url) => url.Replace("\"", "%22").Replace("\n", "");
}
