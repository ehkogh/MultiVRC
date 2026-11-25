using System.Text;
using MultiVRC.Core;
using Xunit;

namespace MultiVRC.Core.Tests;

public sealed class SteamProfileTests
{
    [Fact]
    public void A_community_profile_gives_the_display_name_and_full_picture()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?><profile>
            	<steamID64>76561197960287930</steamID64>
            	<steamID><![CDATA[Rabscuttle]]></steamID>
            	<privacyState>friendsonly</privacyState>
            	<avatarIcon><![CDATA[https://avatars.fastly.steamstatic.com/c5d5.jpg]]></avatarIcon>
            	<avatarFull><![CDATA[https://avatars.fastly.steamstatic.com/c5d5_full.jpg]]></avatarFull>
            </profile>
            """;
        var profile = SteamProfiles.Parse(xml.Trim());
        Assert.Equal("Rabscuttle", profile?.Name);
        Assert.Equal("https://avatars.fastly.steamstatic.com/c5d5_full.jpg", profile?.AvatarUrl);
    }

    [Fact]
    public void A_missing_profile_gives_nothing()
    {
        Assert.Null(SteamProfiles.Parse("<response><error><![CDATA[The specified profile could not be found.]]></error></response>"));
    }

    [Fact]
    public void A_refresh_token_names_its_account()
    {
        static string Part(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = $"{Part("""{"typ":"JWT","alg":"EdDSA"}""")}.{Part("""{"iss":"steam","sub":"76561197960287930","aud":["client","renew"]}""")}.sig";
        Assert.Equal(76561197960287930UL, SteamProfiles.SteamIdOf(token));
        Assert.Null(SteamProfiles.SteamIdOf("not-a-token"));
    }
}
