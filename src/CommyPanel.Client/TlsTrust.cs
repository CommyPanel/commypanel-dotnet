using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace CommyPanel;

/// <summary>
/// Trusting one more certificate authority without turning verification off: a certificate the system accepts is
/// accepted; otherwise it must chain to the given authority, be valid now, and be for the host that was asked for.
/// </summary>
internal static class TlsTrust
{
    internal static X509Certificate2Collection ParseRoots(string pem)
    {
        var roots = new X509Certificate2Collection();

        try
        {
            roots.ImportFromPem(pem);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            throw new ArgumentException("the CA certificate is not valid PEM", nameof(pem), ex);
        }

        if (roots.Count == 0)
        {
            throw new ArgumentException("the CA certificate is not valid PEM", nameof(pem));
        }

        return roots;
    }

    internal static RemoteCertificateValidationCallback Callback(X509Certificate2Collection roots) =>
        (_, certificate, chain, errors) => Validate(certificate, chain, errors, roots);

    internal static bool Validate(
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors errors,
        X509Certificate2Collection roots)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        // Only an unknown authority can be forgiven. A name that does not match, or no certificate at all, never is.
        if (certificate is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
        {
            return false;
        }

        using var leaf = new X509Certificate2(certificate);
        using var custom = new X509Chain();

        custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        custom.ChainPolicy.CustomTrustStore.AddRange(roots);

        // A private authority has no revocation service to ask.
        custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        // Intermediates the server sent are useful; the root it sent is not trusted unless it is one of ours.
        if (chain is not null)
        {
            foreach (var element in chain.ChainElements)
            {
                custom.ChainPolicy.ExtraStore.Add(element.Certificate);
            }
        }

        return custom.Build(leaf);
    }
}
