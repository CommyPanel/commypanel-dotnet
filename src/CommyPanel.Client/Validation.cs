using System.Text.RegularExpressions;

namespace CommyPanel;

/// <summary>Checks done before any request, so a name cannot change the path.</summary>
internal static class Validation
{
    // \A and \z, not ^ and $: "$" also matches before a final newline, which would let "shop\n" through.
    private static readonly Regex SlugPattern = new(@"\A[a-z0-9][a-z0-9-]{0,62}\z", RegexOptions.CultureInvariant);

    private static readonly Regex EnvKeyPattern = new(@"\A[A-Za-z_][A-Za-z0-9_]{0,127}\z", RegexOptions.CultureInvariant);

    internal static void Slug(string? slug)
    {
        if (slug is null || !SlugPattern.IsMatch(slug))
        {
            throw new InvalidInputException();
        }
    }

    internal static void EnvKey(string? key)
    {
        if (key is null || !EnvKeyPattern.IsMatch(key))
        {
            throw new InvalidInputException();
        }
    }

    internal static void Token(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 512)
        {
            throw new InvalidTokenException();
        }

        foreach (var c in token)
        {
            // Spaces and control characters could split a header; anything outside printable ASCII is not in a token.
            if (c <= ' ' || c > '~')
            {
                throw new InvalidTokenException();
            }
        }
    }
}
