using System;
using System.Globalization;

namespace Jellyfin.Plugin.ServerPairing.KeyStore;

/// <summary>
/// Thrown when the key store's file is there and is not a key store.
/// </summary>
/// <remarks>
/// The failure this refuses is not a crash, it is the quiet answer that used to stand in its
/// place. A file that parsed as anything other than an object was read as an empty store, and
/// an empty store is what a fresh installation has, so an operator whose file had been
/// truncated, half-overwritten or replaced saw a plugin with no pairings and did the one thing
/// that makes the loss permanent: paired again, over the top of bytes that were still on the
/// disk.
/// <para>
/// So a damaged file fails the plugin closed. Every operation on the store refuses with this,
/// because every operation reads the file, and nothing repairs, truncates or replaces the file
/// on the way to refusing. What an operator gets is a plugin that will not pair and a sentence
/// naming the file, which leaves the bytes there for whoever looks at them.
/// </para>
/// <para>
/// It is deliberately NOT the answer for a file in a format this build does not understand.
/// That one parsed, carried a format number and carried a number higher than this build reads,
/// which is a plugin that was rolled back rather than a file that is broken, and it is
/// <see cref="StoreFormatRefusedException"/>. The two are separate because what an operator
/// does about them is separate: one is fixed by installing the newer plugin again, and this
/// one is not fixed by installing anything.
/// </para>
/// <para>
/// WHAT IT DOES NOT SEE is a file that is an intact key store and is nevertheless the wrong
/// one: a store restored from a backup, or a copy of one machine's store on another. Those
/// files parse, carry the envelope and hold well-formed keys, so no reading of one file tells
/// them from the store they were copied from. <c>docs/keystore.md</c> says which cases this
/// plugin can see and which it cannot, and issue #33 is where the rest of them live.
/// </para>
/// </remarks>
public sealed class StoreDamagedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StoreDamagedException"/> class.
    /// </summary>
    /// <remarks>
    /// This constructor and the two below take a message rather than a file and leave
    /// <see cref="File"/> empty. They exist because the framework's own guidance asks every
    /// exception type for them; nothing in this plugin calls any of the three. What this
    /// plugin throws is built by <see cref="For(string)"/>, which is the only route that
    /// composes the sentence and sets the file.
    /// </remarks>
    public StoreDamagedException()
    {
        File = string.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreDamagedException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public StoreDamagedException(string message)
        : base(message)
    {
        File = string.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreDamagedException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">What caused it.</param>
    public StoreDamagedException(string message, Exception innerException)
        : base(message, innerException)
    {
        File = string.Empty;
    }

    /// <summary>
    /// Gets the file that was read.
    /// </summary>
    public string File { get; init; }

    /// <summary>
    /// The refusal for a damaged store, naming the file.
    /// </summary>
    /// <param name="file">The file that was read.</param>
    /// <returns>The refusal.</returns>
    public static StoreDamagedException For(string file) =>
        new StoreDamagedException(Sentence(file)) { File = file };

    /// <summary>
    /// The refusal for a damaged store that failed while it was being read, keeping what it
    /// failed with.
    /// </summary>
    /// <param name="file">The file that was read.</param>
    /// <param name="cause">What the read failed with.</param>
    /// <returns>The refusal.</returns>
    /// <remarks>
    /// The cause is kept rather than discarded because the message an operator reads is
    /// deliberately the same for every shape of damage, and somebody diagnosing one still
    /// wants to know whether the file failed to parse or failed to deserialise.
    /// </remarks>
    public static StoreDamagedException For(string file, Exception cause) =>
        new StoreDamagedException(Sentence(file), cause) { File = file };

    private static string Sentence(string file) => string.Format(
        CultureInfo.InvariantCulture,
        "The key store at '{0}' is damaged: it is there and it does not hold what a key store holds. It is refused rather than read as an empty store, because an empty store is what a fresh installation has, and pairing afresh over this one would overwrite whatever is still in it. Nothing here has changed the file. Move it aside and keep it before pairing again, and no pairing works until then.",
        file);
}
