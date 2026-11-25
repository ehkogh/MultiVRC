using MultiVRC.Core;
using Xunit;

namespace MultiVRC.Core.Tests;

public sealed class NewsTests
{
    [Fact]
    public void A_blog_feed_gives_titles_pictures_and_plain_summaries()
    {
        const string rss = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0"><channel>
              <item>
                <title>Update: Safety &amp; You</title>
                <pubDate>Wed, 24 Jun 2026 19:38:05 +0000</pubDate>
                <link>https://hello.vrchat.com/blog/june-2026-safety-update</link>
                <description><![CDATA[<figure><img data-image="https://images.example/cover.png" src="https://images.example/small.png"></figure><p>Hello <b>everyone</b>, today we are shipping things.</p>]]></description>
              </item>
            </channel></rss>
            """;
        var item = Assert.Single(NewsReader.FromRss(rss));
        Assert.Equal("Update: Safety & You", item.Title);
        Assert.Equal("https://images.example/cover.png", item.ImageUrl);
        Assert.Equal("Hello everyone, today we are shipping things.", item.Summary);
        Assert.Equal(2026, item.PublishedAt?.Year);
    }
}

public sealed class VerificationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "multivrc-trust-" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public async Task Only_a_listed_template_url_is_verified_and_official_is_always_official()
    {
        var paths = new LauncherPaths(_root);
        paths.EnsureCreated();
        var list = Path.Combine(_root, "list.json");
        File.WriteAllText(list, """[{ "name": "Good Server", "templateUrl": "https://good.example/multivrc.json", "verifiedBy": ["Alice"] }]""");
        var verification = new Verification(paths, new HttpClient(), new Uri(list).AbsoluteUri);
        await verification.RefreshAsync();

        Instance Modded() => new() { Id = "x", Name = "x", BuildId = "b", Kind = InstanceKind.Modded };
        var good = new Template { Id = "g", Name = "G", SourceUrl = "https://good.example/multivrc.json/" };
        var lookalike = new Template { Id = "l", Name = "L", SourceUrl = "https://good.example.evil.test/multivrc.json" };
        var fromFile = new Template { Id = "f", Name = "F" };

        var trust = verification.Of(Modded(), good);
        Assert.Equal(TrustLevel.Verified, trust.Level);
        Assert.Equal(["Alice"], trust.Entry!.VerifiedBy);
        Assert.Equal(TrustLevel.Unverified, verification.Of(Modded(), lookalike).Level);
        Assert.Equal(TrustLevel.Unverified, verification.Of(Modded(), fromFile).Level);
        Assert.Equal(TrustLevel.Official, verification.Of(new Instance { Id = "o", Name = "o", BuildId = "b", Kind = InstanceKind.Official }, null).Level);
    }

    public void Dispose() => FileLinks.DeleteTree(_root);
}
