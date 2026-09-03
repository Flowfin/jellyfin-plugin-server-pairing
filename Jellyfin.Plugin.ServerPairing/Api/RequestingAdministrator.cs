using System.Security.Claims;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// Who is on the other end of a request on the administrative plane, as the host authenticated
/// them, for an audit entry to name.
/// </summary>
/// <remarks>
/// An audit entry naming nobody is the failure this type exists against. Every request that
/// reaches the administrative plane has passed the host's elevation policy, so the host has
/// already decided who the caller is; what this does is read that decision back out of the
/// claims the host set rather than asking the caller to say who they are.
/// <para>
/// The claim is the host's own and is read from the server's source at both supported tags
/// rather than assumed. <c>CustomAuthenticationHandler</c> builds every authenticated principal
/// with the user's identifier under this name, formatted as thirty-two hex characters, and the
/// file is byte for byte the same at both tags, which <c>docs/endpoints.md</c> shows:
/// </para>
/// <code>
/// git grep -n 'UserId = ' v10.11.9 v12.0-rc3 -- Jellyfin.Api/Constants/InternalClaimTypes.cs
/// v10.11.9:Jellyfin.Api/Constants/InternalClaimTypes.cs:11:    public const string UserId = "Jellyfin-UserId";
/// v12.0-rc3:Jellyfin.Api/Constants/InternalClaimTypes.cs:11:    public const string UserId = "Jellyfin-UserId";
///
/// git grep -n 'new Claim(InternalClaimTypes.UserId\|new Claim(InternalClaimTypes.IsApiKey' v10.11.9 -- Jellyfin.Api/Auth/CustomAuthenticationHandler.cs
/// v10.11.9:Jellyfin.Api/Auth/CustomAuthenticationHandler.cs:64:                    new Claim(InternalClaimTypes.UserId, authorizationInfo.UserId.ToString("N", CultureInfo.InvariantCulture)),
/// v10.11.9:Jellyfin.Api/Auth/CustomAuthenticationHandler.cs:70:                    new Claim(InternalClaimTypes.IsApiKey, authorizationInfo.IsApiKey.ToString(CultureInfo.InvariantCulture))
/// </code>
/// <para>
/// The constant that names the claim lives in an assembly a plugin does not reference, so the
/// string is carried here with the reading above beside it, which is the same arrangement the
/// elevation policy's name would have needed had the host not put it in a package this plugin
/// does reference.
/// </para>
/// <para>
/// AN API KEY IS NAMED AS ONE RATHER THAN AS THE EMPTY USER. The handler marks a request made
/// with an API key as an administrator and gives it the empty identifier, so a trail that wrote
/// the claim as it stands would record thirty-two zeros as the person who asked. That is a
/// sentence nobody can act on, so the key is named as a key, which is what an operator reading
/// the trail needs to know to go looking in the right place.
/// </para>
/// </remarks>
public static class RequestingAdministrator
{
    /// <summary>
    /// The claim the host puts the authenticated user's identifier in.
    /// </summary>
    public const string UserIdClaim = "Jellyfin-UserId";

    /// <summary>
    /// The claim the host sets to say the request was made with an API key rather than by a
    /// person.
    /// </summary>
    public const string IsApiKeyClaim = "Jellyfin-IsApiKey";

    /// <summary>
    /// What an audit entry names when the request was made with an API key.
    /// </summary>
    public const string ApiKey = "api-key";

    /// <summary>
    /// Who made the request, for an audit entry to name.
    /// </summary>
    /// <param name="user">The principal the host authenticated.</param>
    /// <returns>
    /// The user's identifier as the host formatted it, <see cref="ApiKey"/> where the request
    /// was made with an API key, or null where the principal carries no identifier at all.
    /// </returns>
    /// <remarks>
    /// Null is the fail-closed answer and the caller's job is to refuse rather than to write an
    /// entry under nobody. On a host that behaves as the reading above says it does, every
    /// principal that reaches this plane carries the claim, so the null arm is what happens when
    /// the host has changed under this plugin rather than a case an operator will meet.
    /// </remarks>
    public static string? Of(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return null;
        }

        // Parsed rather than compared as text. The host writes the claim with bool.ToString, so
        // the same parser reads it back, and a parse is not a comparison the secret-comparison
        // guard has to read the words of.
        if (bool.TryParse(user.FindFirst(IsApiKeyClaim)?.Value, out var madeWithAnApiKey) && madeWithAnApiKey)
        {
            return ApiKey;
        }

        var userId = user.FindFirst(UserIdClaim)?.Value;

        return string.IsNullOrWhiteSpace(userId) ? null : userId;
    }
}
