using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Jellyfin.Plugin.ServerPairing.Protocol;
using SharpFuzz;

namespace Jellyfin.Plugin.ServerPairing.Fuzz;

/// <summary>
/// The fuzz driver for the parse and validate path, issue #69.
/// </summary>
/// <remarks>
/// This plugin reads bytes another server chose. Three surfaces turn those bytes into
/// something with meaning, and each is a target here: the message envelope, the canonical
/// form reconstruction, and the field decoder that turns a peer-supplied address into a
/// destination this server will send to.
/// <para>
/// Every target asserts two kinds of property. The first is that the entry point terminates
/// with a refusal rather than an exception, whatever arrives, because on the real path an
/// unmapped exception is a five hundred to an unauthenticated caller. The second is stronger
/// and is what makes a run worth more than a crash hunt: an accepted input has to satisfy the
/// invariant the acceptance is supposed to guarantee. A canonical form that a signature covers
/// must be recoverable into the fields it was built from, and an address the operator approved
/// must survive its own canonicalisation unchanged. Those catch a wrong answer, which no
/// amount of not crashing does.
/// </para>
/// <para>
/// A finding is a defect in the code under test and is triaged as one. It is never repaired by
/// widening a filter here, because a harness that catches its own findings is a harness that
/// reports none.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// The environment variable that selects the target, so one executable covers three
    /// surfaces while honouring libFuzzer's one-input-one-entry-point contract.
    /// </summary>
    private const string TargetVariable = "SERVERPAIRING_FUZZ_TARGET";

    /// <summary>
    /// Set to 1 to replay the seed corpus once and exit, without libFuzzer. The
    /// instrumentation command and the libFuzzer runtime are Linux only, so this is how the
    /// harness is exercised anywhere else, and it is what proves the wiring rather than the
    /// coverage.
    /// </summary>
    private const string SmokeVariable = "SERVERPAIRING_FUZZ_SMOKE";

    /// <summary>
    /// The three characters the address parse refuses by name, so an accepted value carrying
    /// one of them is a hole rather than a preference.
    /// </summary>
    private static readonly char[] RefusedByName = { '@', '?', '#' };

    /// <summary>
    /// A fixed key. The question here is what the parse and validate path does with bytes,
    /// and a key that moved between iterations would make a reproducer stop reproducing.
    /// Nothing is protected by it, and no secret of any kind belongs in this file.
    /// </summary>
    private static readonly byte[] FixedKey = new byte[]
    {
        0x6a, 0x65, 0x6c, 0x6c, 0x79, 0x66, 0x69, 0x6e, 0x2d, 0x66, 0x75, 0x7a, 0x7a, 0x2d, 0x6b, 0x65,
        0x79, 0x2d, 0x6e, 0x6f, 0x74, 0x2d, 0x61, 0x2d, 0x73, 0x65, 0x63, 0x72, 0x65, 0x74, 0x21, 0x21,
    };

    private static int Main(string[] args)
    {
        var target = Environment.GetEnvironmentVariable(TargetVariable) ?? "envelope";

        ReadOnlySpanAction run = target switch
        {
            "envelope" => FuzzEnvelope,
            "canonical" => FuzzCanonicalForm,
            "address" => FuzzPeerAddress,
            _ => throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unknown {TargetVariable} '{target}'. Expected one of: envelope, canonical, address."),
                nameof(args)),
        };

        if (Environment.GetEnvironmentVariable(SmokeVariable) == "1")
        {
            return RunSmoke(run, target, args);
        }

        Fuzzer.LibFuzzer.Run(run);
        return 0;
    }

    /// <summary>
    /// Replays every seed for the selected target through it once. A finding propagates and
    /// ends the process non-zero, which is the same signal libFuzzer records as a crash.
    /// </summary>
    private static int RunSmoke(ReadOnlySpanAction run, string target, string[] args)
    {
        var corpus = args.Length > 0
            ? args[0]
            : Path.Join("corpus", target);

        if (!Directory.Exists(corpus))
        {
            Console.Error.WriteLine(
                string.Create(CultureInfo.InvariantCulture, $"No corpus directory at '{corpus}'."));
            return 2;
        }

        var count = 0;

        foreach (var file in Directory.EnumerateFiles(corpus))
        {
            run(File.ReadAllBytes(file));
            count++;
        }

        if (count == 0)
        {
            Console.Error.WriteLine(
                string.Create(CultureInfo.InvariantCulture, $"No seeds in '{corpus}'."));
            return 2;
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Smoke: {count} seed(s) for '{target}' drove the path with no finding."));

        return 0;
    }

    /// <summary>
    /// The envelope. Seven fields and a body arrive as bytes, and what a caller reaches
    /// without holding a key is the shape check, the signature decode and the comparison.
    /// </summary>
    /// <remarks>
    /// The properties. A request the shape check refuses is refused by the verifier, and no
    /// input of any shape produces an exception. A request it accepts round-trips: the
    /// signature this key produces over it verifies, and a signature one byte away from that
    /// one does not. The last is what separates a verifier from a function returning
    /// <c>Verified</c>.
    /// </remarks>
    private static void FuzzEnvelope(ReadOnlySpan<byte> data)
    {
        var wire = WireFields.Split(data);
        var request = wire.ToRequest();
        var verifier = new RequestAuthenticator(new OneKey(request.PairingId, FixedKey));

        var wellFormed = FieldShape.IsWellFormed(request);

        // Whatever arrived in the signature header, presented as it arrived.
        if (verifier.Verify(request, wire.Signature) == VerificationOutcome.Verified && !wellFormed)
        {
            throw new FuzzFinding("A request the shape check refuses was verified.");
        }

        if (!wellFormed)
        {
            if (verifier.Verify(request, null) == VerificationOutcome.Verified)
            {
                throw new FuzzFinding("A request with no signature at all was verified.");
            }

            return;
        }

        var signature = RequestAuthenticator.Sign(request, FixedKey);

        if (verifier.Verify(request, signature) != VerificationOutcome.Verified)
        {
            throw new FuzzFinding("A request did not verify under the signature this key just produced over it.");
        }

        AssertCanonicalFormIsRecoverable(request);

        var tampered = Convert.FromBase64String(signature);
        tampered[0] ^= 0x01;

        if (verifier.Verify(request, Convert.ToBase64String(tampered)) == VerificationOutcome.Verified)
        {
            throw new FuzzFinding("A signature one bit away from the correct one verified.");
        }
    }

    /// <summary>
    /// The canonical form reconstruction, driven without the shape check in front of it, so
    /// the reconstruction is asked about field values the envelope target never reaches.
    /// </summary>
    /// <remarks>
    /// Building the bytes may not throw for any field values. Where the shape check does
    /// accept the fields, the bytes have to be eight lines that split back into exactly the
    /// values they were built from, and the response form six. A field that carries a line
    /// feed past the shape check would produce bytes two requests can share, and a signature
    /// over shared bytes authenticates both.
    /// </remarks>
    private static void FuzzCanonicalForm(ReadOnlySpan<byte> data)
    {
        var wire = WireFields.Split(data);
        var request = wire.ToRequest();

        var bytes = CanonicalForm.ForRequest(request);

        CanonicalForm.ForResponse(
            request.Version,
            request.PairingId,
            request.Nonce,
            request.Timestamp,
            request.Body);

        if (bytes.Length == 0)
        {
            throw new FuzzFinding("The canonical form of a request was empty.");
        }

        if (!FieldShape.IsWellFormed(request))
        {
            return;
        }

        AssertCanonicalFormIsRecoverable(request);
    }

    /// <summary>
    /// The field decoder for a peer address, which is the one field where being wrong sends a
    /// request somewhere rather than refusing one.
    /// </summary>
    /// <remarks>
    /// The properties. Parsing never throws and never hands back an address it did not
    /// accept. An accepted address is inside its length limit, carries the one allowed scheme,
    /// carries none of the three characters the parse refuses by name, carries no path, is a
    /// fixed point in that it parses to itself, and approves the candidate it was parsed from.
    /// The last two are what an approval means. The operator approved a spelling; if the
    /// spelling this type produces parsed to something else, or if the address it produced
    /// then refused the text it came from, the address held would not be the address approved.
    /// </remarks>
    private static void FuzzPeerAddress(ReadOnlySpan<byte> data)
    {
        var candidate = Encoding.UTF8.GetString(data);
        var outcome = PeerAddress.Parse(candidate, out var address);

        if (outcome != PeerAddressOutcome.Accepted)
        {
            if (address is not null)
            {
                throw new FuzzFinding("A refused candidate produced an address.");
            }

            return;
        }

        if (address is null)
        {
            throw new FuzzFinding("An accepted candidate produced no address.");
        }

        if (address.Value.Length > PeerAddress.LengthLimit)
        {
            throw new FuzzFinding("An accepted address is past the length limit.");
        }

        if (!address.Value.StartsWith(PeerAddress.AllowedScheme + "://", StringComparison.Ordinal))
        {
            throw new FuzzFinding("An accepted address does not carry the one allowed scheme.");
        }

        if (address.Value.IndexOfAny(RefusedByName) >= 0)
        {
            throw new FuzzFinding("An accepted address carries user information, a query or a fragment.");
        }

        if (!string.Equals(address.Uri.AbsolutePath, "/", StringComparison.Ordinal))
        {
            throw new FuzzFinding("An accepted address carries a path.");
        }

        if (PeerAddress.Parse(address.Value, out var again) != PeerAddressOutcome.Accepted
            || again is null
            || !string.Equals(again.Value, address.Value, StringComparison.Ordinal))
        {
            throw new FuzzFinding("An accepted address does not parse back to itself.");
        }

        if (!address.Approves(candidate))
        {
            throw new FuzzFinding("An address does not approve the candidate it was parsed from.");
        }
    }

    /// <summary>
    /// The bytes a signature covers have to name their own field boundaries. Eight lines, each
    /// ended by one line feed, no carriage return and nothing outside ASCII.
    /// </summary>
    /// <remarks>
    /// Seven of the eight lines are compared against the field they were built from. The
    /// eighth is the body digest and is compared against nothing here, so a wrong digest is
    /// outside what this asserts: checking it means computing it, and a harness computing it
    /// the same way the code under test does proves the two agree with each other rather than
    /// with the specification.
    /// </remarks>
    private static void AssertCanonicalFormIsRecoverable(PairingRequest request)
    {
        var bytes = CanonicalForm.ForRequest(request);

        foreach (var b in bytes)
        {
            if (b > 0x7f)
            {
                throw new FuzzFinding("The canonical form carries a byte outside ASCII.");
            }

            if (b == (byte)'\r')
            {
                throw new FuzzFinding("The canonical form carries a carriage return.");
            }
        }

        var text = Encoding.ASCII.GetString(bytes);

        if (text[^1] != '\n')
        {
            throw new FuzzFinding("The canonical form does not end in a line feed.");
        }

        var lines = text[..^1].Split('\n');

        if (lines.Length != 8)
        {
            throw new FuzzFinding("The canonical form of a well-formed request is not eight lines.");
        }

        var expected = new[]
        {
            CanonicalForm.RequestLabel,
            request.Version,
            request.Method,
            request.Path,
            request.PairingId,
            request.Timestamp,
            request.Nonce,
        };

        for (var i = 0; i < expected.Length; i++)
        {
            if (!string.Equals(lines[i], expected[i], StringComparison.Ordinal))
            {
                throw new FuzzFinding("A line of the canonical form is not the field it was built from.");
            }
        }
    }

    /// <summary>
    /// The seven fields and the body, cut out of the fuzzer's bytes.
    /// </summary>
    /// <remarks>
    /// The separator is a zero byte rather than a line feed. Every field this protocol accepts
    /// is printable ASCII, so a zero byte cannot be part of one and cutting on it costs
    /// nothing. A line feed would cost the whole point: it can never then appear inside a
    /// field, and a field carrying a line feed past the shape check is precisely the defect
    /// the canonical form target exists to find.
    /// </remarks>
    private readonly struct WireFields
    {
        /// <summary>
        /// Six covered fields and the signature header, in the order a request takes them in
        /// its constructor rather than the order the canonical form names them, with the body
        /// last so a mutation that lengthens it does not shift the rest. The two orders differ
        /// in one place: the version is first on the canonical form and fourth here, so a seed
        /// laid out by reading that form is not the seed this splitter reads.
        /// </summary>
        private const int FieldCount = 7;

        private WireFields(
            string method,
            string path,
            string pairingId,
            string version,
            string timestamp,
            string nonce,
            string signature,
            byte[] body)
        {
            Method = method;
            Path = path;
            PairingId = pairingId;
            Version = version;
            Timestamp = timestamp;
            Nonce = nonce;
            Signature = signature;
            Body = body;
        }

        public string Method { get; }

        public string Path { get; }

        public string PairingId { get; }

        public string Version { get; }

        public string Timestamp { get; }

        public string Nonce { get; }

        public string Signature { get; }

        public byte[] Body { get; }

        public static WireFields Split(ReadOnlySpan<byte> data)
        {
            var parts = new List<string>(FieldCount);
            var rest = data;

            while (parts.Count < FieldCount)
            {
                var cut = rest.IndexOf((byte)0);

                if (cut < 0)
                {
                    parts.Add(Encoding.UTF8.GetString(rest));
                    rest = ReadOnlySpan<byte>.Empty;
                    break;
                }

                parts.Add(Encoding.UTF8.GetString(rest[..cut]));
                rest = rest[(cut + 1)..];
            }

            // Everything past the seventh separator is the body, zero bytes and all. Where the
            // input ran out before seven fields, the rest are empty and there is no body.
            var body = parts.Count == FieldCount ? rest.ToArray() : Array.Empty<byte>();

            while (parts.Count < FieldCount)
            {
                parts.Add(string.Empty);
            }

            return new WireFields(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], parts[6], body);
        }

        public PairingRequest ToRequest()
            => new PairingRequest(Method, Path, PairingId, Version, Timestamp, Nonce, Body);
    }

    /// <summary>
    /// The key source the verifier reads, answering for one pairing identifier and no other.
    /// </summary>
    private sealed class OneKey : IPairingKeySource
    {
        private readonly string _pairingId;
        private readonly byte[] _key;

        public OneKey(string pairingId, byte[] key)
        {
            _pairingId = pairingId;
            _key = key;
        }

        public ReadOnlyMemory<byte> ArrivingKey(string pairingId)
            => string.Equals(pairingId, _pairingId, StringComparison.Ordinal)
                ? _key
                : ReadOnlyMemory<byte>.Empty;
    }

    /// <summary>
    /// What the harness throws when a property does not hold. libFuzzer records the input that
    /// produced it, and that input is the finding.
    /// </summary>
    private sealed class FuzzFinding : Exception
    {
        public FuzzFinding(string message)
            : base(message)
        {
        }
    }
}
