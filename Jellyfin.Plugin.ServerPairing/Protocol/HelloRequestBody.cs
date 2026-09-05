using System;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The body of a <c>hello</c> request, read from the bytes that arrived.
/// </summary>
/// <remarks>
/// The four members and their limits are <c>docs/protocol.md</c>, which is the authority for
/// both; this type is that row of the member table expressed in code and a difference between
/// the two is a defect in this file.
/// <para>
/// EVERY MEMBER IS REQUIRED AND THERE IS NO DEFAULT. A body missing one is refused rather than
/// completed, because a default is a value neither side agreed on standing in for one they would
/// have had to send. That is the failure issue #25 is written against: a newer server accepting
/// an older message shape and treating a missing field as a permissive default.
/// </para>
/// <para>
/// WHAT IT DOES NOT JUDGE IS THE ADDRESS AND THE KEY BEYOND THEIR LIMITS. The address is checked
/// against the length limit the field table gives it and not against the four forms
/// <see cref="PeerAddress"/> accepts, because whether a cleartext address is acceptable is a
/// setting on this server that no body carries and this type cannot read; comparing the address
/// a peer believes it is talking to against the one an administrator approved is issue #22. The
/// key is checked to be base64 inside its limit and is not imported, because importing one is
/// what the ceremony does with it and there is no key pair here to import it against, which is
/// issue #19.
/// </para>
/// </remarks>
public sealed class HelloRequestBody
{
    /// <summary>
    /// The member carrying the public key this sender offers.
    /// </summary>
    public const string KeyMember = "key";

    /// <summary>
    /// The member carrying the lowest protocol version the sender speaks.
    /// </summary>
    public const string VersionLowMember = "versionLow";

    /// <summary>
    /// The member carrying the highest protocol version the sender speaks.
    /// </summary>
    public const string VersionHighMember = "versionHigh";

    /// <summary>
    /// The member carrying the address the sender believes it is talking to.
    /// </summary>
    public const string AddressMember = "address";

    /// <summary>
    /// The most characters the public key member may carry.
    /// </summary>
    public const int KeyLengthLimit = 512;

    /// <summary>
    /// How many members this body has. Every one of them is required, so a body of this many
    /// members that carries all four names carries nothing else.
    /// </summary>
    private const int MemberCount = 4;

    private HelloRequestBody(string key, VersionRange versions, string address)
    {
        Key = key;
        Versions = versions;
        Address = address;
    }

    /// <summary>
    /// Gets the public key the sender offered, as the base64 it was written as.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the range of protocol versions the sender speaks.
    /// </summary>
    public VersionRange Versions { get; }

    /// <summary>
    /// Gets the address the sender believes it is talking to, as it was written.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Reads a <c>hello</c> request body.
    /// </summary>
    /// <param name="body">The body bytes, exactly as they arrived.</param>
    /// <param name="hello">The body read, where this returns true.</param>
    /// <returns>True where the bytes are a <c>hello</c> request body.</returns>
    public static bool TryRead(ReadOnlySpan<byte> body, out HelloRequestBody? hello)
    {
        hello = null;

        if (!BodyObject.TryRead(body, out var members) || members.Count != MemberCount)
        {
            return false;
        }

        if (!members.TryText(KeyMember, out var key)
            || !IsKey(key)
            || !members.TryText(AddressMember, out var address)
            || address.Length == 0
            || address.Length > PeerAddress.LengthLimit)
        {
            return false;
        }

        if (!members.TryDigits(VersionLowMember, FieldShape.VersionDigitLimit, out var low)
            || !members.TryDigits(VersionHighMember, FieldShape.VersionDigitLimit, out var high)
            || !VersionRange.TryParse(low, high, out var versions))
        {
            return false;
        }

        hello = new HelloRequestBody(key, versions, address);
        return true;
    }

    /// <summary>
    /// Whether a value is a public key as the field table describes one.
    /// </summary>
    /// <param name="value">The value, as it was written.</param>
    /// <returns>True where it is base64 inside the limit.</returns>
    /// <remarks>
    /// The limit is a character count rather than a decoded length, which is what the field
    /// table gives it, so a key of a length this build does not expect is refused where a key is
    /// imported rather than here. Decoding is asked for because a value that is not base64
    /// carries no key at all, and answering that at the shape rather than at the import is what
    /// keeps a caller from reaching an importer with arbitrary text.
    /// </remarks>
    private static bool IsKey(string value)
    {
        if (value.Length == 0 || value.Length > KeyLengthLimit)
        {
            return false;
        }

        var decoded = new byte[KeyLengthLimit];

        return Convert.TryFromBase64String(value, decoded, out var written) && written != 0;
    }
}
