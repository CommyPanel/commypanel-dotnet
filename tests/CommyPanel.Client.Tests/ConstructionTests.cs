namespace CommyPanel.Tests;

public class ConstructionTests
{
    [Theory]
    [InlineData("https://panel.example.com", "https://panel.example.com")]
    [InlineData("https://panel.example.com/", "https://panel.example.com")]
    [InlineData("https://panel.example.com:8443", "https://panel.example.com:8443")]
    [InlineData("https://panel.example.com/api/v1", "https://panel.example.com")]
    public void AcceptsAnHttpsAddress(string url, string expected)
    {
        using var client = new CommyPanelClient(url, TestKit.Token);

        Assert.Equal(expected, client.BaseUrl);
    }

    [Theory]
    [InlineData("http://localhost:8000")]
    [InlineData("http://127.0.0.1:8000")]
    [InlineData("http://[::1]:8000")]
    public void AllowsPlainHttpForThisMachineWhenAskedFor(string url)
    {
        using var client = new CommyPanelClient(url, TestKit.Token, new CommyPanelClientOptions { AllowHttpLoopback = true });

        Assert.StartsWith("http://", client.BaseUrl);
    }

    [Theory]
    [InlineData("http://panel.example.com", false)]
    [InlineData("http://localhost:8000", false)] // loopback http needs the option
    [InlineData("http://panel.example.com", true)] // and only loopback
    [InlineData("http://192.168.1.10", true)]
    [InlineData("ftp://panel.example.com", false)]
    public void RefusesAnAddressThatIsNotHttps(string url, bool allowLoopback)
    {
        Assert.Throws<InsecureUrlException>(() => new CommyPanelClient(url, TestKit.Token, new CommyPanelClientOptions { AllowHttpLoopback = allowLoopback }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("panel.example.com")]
    [InlineData("https://")]
    [InlineData("https://user:pw@panel.example.com")]
    [InlineData("https://panel.example.com/admin")]
    [InlineData("https://panel.example.com?x=1")]
    [InlineData("https://panel.example.com#top")]
    [InlineData("https://panel.example.com/api/v2")]
    public void RefusesAnAddressThatIsNotAPlainSchemeAndHost(string url)
    {
        Assert.Throws<InvalidUrlException>(() => new CommyPanelClient(url, TestKit.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("has space")]
    [InlineData("new\nline")]
    [InlineData("tab\t")]
    [InlineData("ctl\u0001")]
    [InlineData("café")]
    public void RefusesATokenThatCannotBeOne(string token)
    {
        Assert.Throws<InvalidTokenException>(() => new CommyPanelClient("https://panel.example.com", token));
    }

    [Fact]
    public void RefusesAnEnormousTokenAndNeverQuotesATokenInTheMessage()
    {
        Assert.Throws<InvalidTokenException>(() => new CommyPanelClient("https://panel.example.com", new string('a', 513)));

        var exception = Assert.Throws<InvalidTokenException>(() => new CommyPanelClient("https://panel.example.com", "secret token with spaces"));

        Assert.DoesNotContain("secret", exception.Message);
    }

    [Fact]
    public void RefusesBadOptions()
    {
        const string url = "https://panel.example.com";

        Assert.ThrowsAny<ArgumentException>(() => new CommyPanelClient(url, TestKit.Token, new CommyPanelClientOptions { RequestTimeout = TimeSpan.Zero }));
        Assert.ThrowsAny<ArgumentException>(() => new CommyPanelClient(url, TestKit.Token, new CommyPanelClientOptions { MaxRetries = -1 }));
        Assert.ThrowsAny<ArgumentException>(() => new CommyPanelClient(url, TestKit.Token, new CommyPanelClientOptions { MaxRetries = 6 }));
        Assert.ThrowsAny<ArgumentException>(() => new CommyPanelClient(url, TestKit.Token, new CommyPanelClientOptions { MaxResponseBytes = 0 }));
        Assert.ThrowsAny<ArgumentException>(() => new CommyPanelClient(url, TestKit.Token, new CommyPanelClientOptions { UserAgentPrefix = "bad\nagent" }));
        Assert.ThrowsAny<ArgumentException>(() => new CommyPanelClient(url, TestKit.Token, new CommyPanelClientOptions { CaCertificatePem = "not pem" }));
    }
}
