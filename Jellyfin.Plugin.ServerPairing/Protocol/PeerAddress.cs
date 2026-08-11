using System;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The one place this server will send a pairing request, as the operator approved it.
/// </summary>
/// <remarks>
/// The address is operator-entered configuration, and everything sent to it is a request this
/// server makes on its own behalf, which is the server-side request forgery surface. So the
/// accepted forms are a list, in <see cref="Parse"/> and in <c>docs/protocol.md</c>, rather
/// than a pattern that happens to pass the examples that were tried.
/// <para>
/// A peer names an address of its own in <c>hello</c>. That value is compared against this one
/// by <see cref="Approves"/> and is never adopted, so the only way the recorded address moves
/// is an operator moving it. There is no message that carries a new address and no code here
/// that writes one.
/// </para>
/// <para>
/// Plaintext is refused. <c>docs/protocol.md</c> gives the field as an absolute <c>https</c>
/// URI, and the operator acknowledgement that would allow anything else is a setting, which is
/// issue #50 and does not exist. Until it does this refuses in the closed direction.
/// </para>
/// </remarks>
public sealed class PeerAddress
{
    /// <summary>
    /// The most characters an address may carry, from the field table in
    /// <c>docs/protocol.md</c>.
    /// </summary>
    public const int LengthLimit = 255;

    /// <summary>
    /// The only scheme the pairing plane runs over.
    /// </summary>
    public const string AllowedScheme = "https";

    private PeerAddress(string value, Uri uri)
    {
        Value = value;
        Uri = uri;
    }

    /// <summary>
    /// Gets the address in the one spelling this type produces, which is what two addresses
    /// are compared as.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the address as a URI, for a caller building a request against it.
    /// </summary>
    public Uri Uri { get; }

    /// <summary>
    /// Judges a candidate against the list of accepted forms.
    /// </summary>
    /// <param name="candidate">What was entered.</param>
    /// <param name="address">The accepted address, or null where the outcome is not
    /// <see cref="PeerAddressOutcome.Accepted"/>.</param>
    /// <returns>Which rule accepted or refused it.</returns>
    public static PeerAddressOutcome Parse(string? candidate, out PeerAddress? address)
    {
        address = null;

        if (string.IsNullOrEmpty(candidate))
        {
            return PeerAddressOutcome.Empty;
        }

        if (candidate.Length > LengthLimit)
        {
            return PeerAddressOutcome.TooLong;
        }

        foreach (var c in candidate)
        {
            if (c <= ' ' || c > '~')
            {
                return PeerAddressOutcome.NotAnAbsoluteUri;
            }

            if (c == '?')
            {
                return PeerAddressOutcome.QueryPresent;
            }

            if (c == '#')
            {
                return PeerAddressOutcome.FragmentPresent;
            }

            if (c == '@')
            {
                return PeerAddressOutcome.UserInfoPresent;
            }
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return PeerAddressOutcome.NotAnAbsoluteUri;
        }

        if (!string.Equals(uri.Scheme, AllowedScheme, StringComparison.Ordinal))
        {
            return PeerAddressOutcome.SchemeNotAllowed;
        }

        if (uri.UserInfo.Length != 0)
        {
            return PeerAddressOutcome.UserInfoPresent;
        }

        if (!IsAllowedHost(uri))
        {
            return PeerAddressOutcome.HostFormNotAllowed;
        }

        if (uri.Port < 1 || uri.Port > 65535)
        {
            return PeerAddressOutcome.PortNotAllowed;
        }

        if (!string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal))
        {
            return PeerAddressOutcome.PathPresent;
        }

        if (uri.Query.Length != 0)
        {
            return PeerAddressOutcome.QueryPresent;
        }

        if (uri.Fragment.Length != 0)
        {
            return PeerAddressOutcome.FragmentPresent;
        }

        var canonical = uri.IsDefaultPort
            ? string.Concat(uri.Scheme, "://", uri.Host)
            : string.Concat(uri.Scheme, "://", uri.Host, ":", uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));

        address = new PeerAddress(canonical, new Uri(canonical + "/", UriKind.Absolute));
        return PeerAddressOutcome.Accepted;
    }

    /// <summary>
    /// Whether an address a peer names for itself is the one the operator approved.
    /// </summary>
    /// <param name="claimed">The address carried in the message.</param>
    /// <returns>True only where the claim parses to this same address.</returns>
    /// <remarks>
    /// The claim is put through <see cref="Parse"/> before it is compared, so a peer cannot
    /// match by spelling the approved address in a form this plugin would never send to. A
    /// claim that does not parse is not the approved address, which is the same answer as a
    /// claim that parses to a different one: nothing here tells the peer which it was.
    /// </remarks>
    public bool Approves(string? claimed)
    {
        return Parse(claimed, out var parsed) == PeerAddressOutcome.Accepted
            && string.Equals(Value, parsed!.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host is a plain ASCII domain name, an IPv4 literal, or a bracketed IPv6 literal.
    /// </summary>
    /// <param name="uri">The parsed candidate.</param>
    /// <returns>True where the host is one of the three forms.</returns>
    /// <remarks>
    /// Domain names are held to ASCII letters, digits and hyphens. A name outside that is
    /// refused rather than converted, because two spellings that render alike and resolve
    /// differently are the mistake an operator cannot see on the page they approve it on.
    /// </remarks>
    private static bool IsAllowedHost(Uri uri)
    {
        if (uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6)
        {
            return true;
        }

        if (uri.HostNameType != UriHostNameType.Dns)
        {
            return false;
        }

        var host = uri.Host;

        if (host.Length == 0 || host[0] == '.' || host[^1] == '.')
        {
            return false;
        }

        var labelLength = 0;

        foreach (var c in host)
        {
            if (c == '.')
            {
                if (labelLength == 0)
                {
                    return false;
                }

                labelLength = 0;
                continue;
            }

            var isLetter = c >= 'a' && c <= 'z';
            var isDigit = c >= '0' && c <= '9';

            if (!isLetter && !isDigit && c != '-')
            {
                return false;
            }

            labelLength++;
        }

        return labelLength != 0;
    }
}
