using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using static CommyPanel.Tests.TestKit;

namespace CommyPanel.Tests;

public class TlsTests
{
    private const string Ok = "{\"data\":{\"name\":\"ci\",\"abilities\":[],\"apps\":null,\"expires_at\":null,\"last_used_at\":null,\"owner\":\"Sam\"}}";

    private static CommyPanelClient TrustingClient(string url, bool trustTestCa)
    {
        var options = new CommyPanelClientOptions
        {
            CaCertificatePem = trustTestCa ? File.ReadAllText(Fixture("ca.pem")) : null,
            MaxRetries = 0,
        };

        return new CommyPanelClient(url, TestKit.Token, options);
    }

    [Fact]
    public async Task ATrustedPrivateAuthorityWorks()
    {
        await using var panel = await FakePanel.StartAsync(Always(200, Ok), LoadCertificate("server"));
        Assert.StartsWith("https://127.0.0.1:", panel.Url);
        using var client = TrustingClient(panel.Url, trustTestCa: true);

        var token = await client.TokenAsync();

        Assert.Equal("ci", token.Name);
        Assert.Equal("Bearer " + TestKit.Token, panel.Last.Auth);
    }

    [Fact]
    public async Task WithoutTheAuthorityTheCertificateIsRefused_AndTheTokenIsNeverSent()
    {
        await using var panel = await FakePanel.StartAsync(Always(200, Ok), LoadCertificate("server"));
        using var client = TrustingClient(panel.Url, trustTestCa: false);

        await Assert.ThrowsAsync<CommyPanelException>(() => client.TokenAsync());

        Assert.Empty(panel.Requests);
    }

    [Fact]
    public async Task ACertificateFromAnAuthorityThatWasNotTrustedIsRefused()
    {
        await using var panel = await FakePanel.StartAsync(Always(200, Ok), LoadCertificate("other-server"));
        using var client = TrustingClient(panel.Url, trustTestCa: true);

        await Assert.ThrowsAsync<CommyPanelException>(() => client.TokenAsync());

        Assert.Empty(panel.Requests);
    }

    [Fact]
    public async Task ACertificateForAnotherNameIsRefusedEvenFromATrustedAuthority()
    {
        await using var panel = await FakePanel.StartAsync(Always(200, Ok), LoadCertificate("wrong-host"));
        using var client = TrustingClient(panel.Url, trustTestCa: true);

        await Assert.ThrowsAsync<CommyPanelException>(() => client.TokenAsync());

        Assert.Empty(panel.Requests);
    }

    [Fact]
    public void ThereIsNoWayToSkipVerification()
    {
        var names = typeof(CommyPanelClientOptions).GetProperties().Select(p => p.Name.ToLowerInvariant());

        Assert.DoesNotContain(names, name => name.Contains("insecure") || name.Contains("skip") || name.Contains("ignore") || name.Contains("disable"));
    }

    [Fact]
    public void TheChainCheckForgivesOnlyAnUnknownAuthorityThatWeTrust()
    {
        var roots = TlsTrust.ParseRoots(File.ReadAllText(Fixture("ca.pem")));
        using var good = LoadCertificate("server");
        using var other = LoadCertificate("other-server");

        // What the system says is accepted as it is.
        Assert.True(TlsTrust.Validate(good, null, SslPolicyErrors.None, roots));

        // An unknown authority is forgiven only when it is one we were given.
        Assert.True(TlsTrust.Validate(good, null, SslPolicyErrors.RemoteCertificateChainErrors, roots));
        Assert.False(TlsTrust.Validate(other, null, SslPolicyErrors.RemoteCertificateChainErrors, roots));

        // A name that does not match is never forgiven, nor is a missing certificate.
        Assert.False(TlsTrust.Validate(good, null, SslPolicyErrors.RemoteCertificateNameMismatch, roots));
        Assert.False(TlsTrust.Validate(good, null, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch, roots));
        Assert.False(TlsTrust.Validate(null, null, SslPolicyErrors.RemoteCertificateChainErrors, roots));
        Assert.False(TlsTrust.Validate(good, null, SslPolicyErrors.RemoteCertificateNotAvailable, roots));
    }

    [Fact]
    public void ACertificateThatIsNotPemIsRefusedWhenTheClientIsMade()
    {
        Assert.ThrowsAny<ArgumentException>(() => TlsTrust.ParseRoots("-----BEGIN NOTHING-----"));
        Assert.ThrowsAny<ArgumentException>(() => TlsTrust.ParseRoots(""));
    }
}
