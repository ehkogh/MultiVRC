using MultiVRC.Core;

namespace MultiVRC.Core.Tests;

public class AppLinkTests
{
    [Theory]
    [InlineData("multivrc://launch/neon-district", "neon-district", null)]
    [InlineData("multivrc://launch/neon-district/", "neon-district", null)]
    [InlineData("MULTIVRC://Launch/vrchat-official/desktop", "vrchat-official", "desktop")]
    [InlineData("multivrc://play/neon_district/play-with-the-debug-console?from=discord", "neon_district", "play-with-the-debug-console")]
    [InlineData("multivrc:launch/neon-district/desktop", "neon-district", "desktop")]
    public void Parses_launch_links(string text, string target, string? option)
    {
        var link = Assert.IsType<LaunchRequest>(AppLinks.Parse(text));
        Assert.Equal(target, link.Target);
        Assert.Equal(option, link.Option);
    }

    [Theory]
    [InlineData("multivrc://install/https://good.example/multivrc.json")]
    [InlineData("multivrc://install/https%3A%2F%2Fgood.example%2Fmultivrc.json")]
    [InlineData("multivrc://install/https:/good.example/multivrc.json")]
    [InlineData("multivrc://install/https://good.example/multivrc.json/")]
    [InlineData("multivrc://install?url=https%3A%2F%2Fgood.example%2Fmultivrc.json")]
    [InlineData("multivrc://install/?url=https%3A%2F%2Fgood.example%2Fmultivrc.json")]
    public void Parses_install_links(string text)
    {
        var link = Assert.IsType<InstallRequest>(AppLinks.Parse(text));
        Assert.Equal("https://good.example/multivrc.json", link.TemplateUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://good.example/multivrc.json")]
    [InlineData("multivrc://")]
    [InlineData("multivrc://delete/everything")]
    [InlineData("multivrc://launch/")]
    [InlineData("multivrc://launch/a/b/c")]
    [InlineData("multivrc://launch/..%2F..%2Fwindows")]
    [InlineData("multivrc://launch/has space")]
    [InlineData("multivrc://install/http://plain.example/multivrc.json")]
    [InlineData("multivrc://install/file:///C:/Windows/win.ini")]
    [InlineData("multivrc://install/")]
    [InlineData("multivrc://install/not a url")]
    public void Rejects_anything_else(string? text) => Assert.Null(AppLinks.Parse(text));

    [Fact]
    public void Install_links_round_trip()
    {
        var url = "https://good.example/templates/multivrc.json?v=2";
        var link = Assert.IsType<InstallRequest>(AppLinks.Parse(AppLinks.InstallUrl(url)));
        Assert.Equal(url, link.TemplateUrl);
    }

    [Fact]
    public void Picks_the_instance_by_id_then_the_latest_played_of_the_template()
    {
        var old = new Instance { Id = "a", Name = "Old", BuildId = "b", TemplateId = "neon", LastPlayedAt = DateTimeOffset.UtcNow.AddDays(-3) };
        var recent = new Instance { Id = "b", Name = "Recent", BuildId = "b", TemplateId = "neon", LastPlayedAt = DateTimeOffset.UtcNow };
        var never = new Instance { Id = "c", Name = "Never", BuildId = "b", TemplateId = "neon" };
        Instance[] all = [old, never, recent];

        Assert.Same(recent, AppLinks.PickInstance(all, "neon"));
        Assert.Same(old, AppLinks.PickInstance(all, "a"));
        Assert.Null(AppLinks.PickInstance(all, "other"));
    }

    [Fact]
    public void Matches_play_modes_by_id_or_label()
    {
        var modded = new Instance { Id = "m", Name = "M", BuildId = "b", Kind = InstanceKind.Modded };
        var official = new Instance { Id = "o", Name = "O", BuildId = "b", Kind = InstanceKind.Official };
        var console = new TemplatePlayMode { Label = "Play with the debug console!" };
        var plain = new TemplatePlayMode { Label = "Play without mods", Id = "vanilla" };
        TemplatePlayMode[] modes = [console, plain];

        Assert.Equal(new LaunchChoice(LaunchMode.Normal, null), AppLinks.Choose(modded, modes, null));
        Assert.Equal(new LaunchChoice(LaunchMode.Normal, null), AppLinks.Choose(modded, modes, "play"));
        Assert.Same(console, AppLinks.Choose(modded, modes, "play-with-the-debug-console")!.Play);
        Assert.Same(plain, AppLinks.Choose(modded, modes, "VANILLA")!.Play);
        Assert.Null(AppLinks.Choose(modded, modes, "play-without-mods"));
        Assert.Null(AppLinks.Choose(modded, modes, "desktop"));

        Assert.Equal(new LaunchChoice(LaunchMode.Desktop, null), AppLinks.Choose(official, [], "desktop"));
        Assert.Null(AppLinks.Choose(official, [], "without-anticheat"));
    }
}
