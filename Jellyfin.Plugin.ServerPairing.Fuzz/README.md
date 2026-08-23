# The fuzz harness

The coverage-guided harness over the parse and validate path, issue #69. It is
not in `Jellyfin.Plugin.ServerPairing.sln`, so no ordinary build, test, mutation
or packaging run restores SharpFuzz or compiles this project, and the packaged
plugin cannot carry it. The fuzz workflow builds it by path.

## The three surfaces

Each is one leg of the run and one directory of seeds.

`envelope` drives the shape check, the signature decode and the comparison, which
is what a caller reaches without holding a key. `canonical` drives the
reconstruction of the bytes a signature covers, without the shape check in front
of it, so it reaches field values the envelope leg never sees. `address` drives
the decoder that turns a peer-supplied address into somewhere this server will
send a request, which is the one field where being wrong sends traffic rather
than refusing it.

## What a finding is

Two kinds of property, and the second is why this is worth running.

An entry point must terminate with a refusal rather than an exception, whatever
arrives. On the real path an unmapped exception is a five hundred handed to an
unauthenticated caller.

An accepted input must satisfy the invariant the acceptance is supposed to
guarantee. The bytes a signature covers must split back into exactly the fields
they were built from, or two different requests can share one signature. An
accepted address must parse back to itself, or the address held is not the
address the operator approved. Those catch a wrong answer, which not crashing
never does.

A finding is a defect in the code under test. It is triaged, minimised and
repaired there. Widening a filter in this harness to make a run pass turns it
into a harness that reports nothing, which is worse than not having one.

## Running it

The `sharpfuzz` instrumentation command and the libFuzzer runtime are Linux
only, so the coverage-guided run happens in the workflow. Everywhere else there
is smoke mode, which replays the seeds through one target and exits, and which
is what proves the wiring:

    dotnet build Jellyfin.Plugin.ServerPairing.Fuzz/Jellyfin.Plugin.ServerPairing.Fuzz.csproj -c Release
    SERVERPAIRING_FUZZ_SMOKE=1 SERVERPAIRING_FUZZ_TARGET=envelope \
      dotnet Jellyfin.Plugin.ServerPairing.Fuzz/bin/Release/net10.0/Jellyfin.Plugin.ServerPairing.Fuzz.dll \
      Jellyfin.Plugin.ServerPairing.Fuzz/corpus/envelope

Smoke mode exits non-zero on a finding and on an empty corpus. It proves the
seeds are read and the path is driven. It proves nothing about coverage.

## The seeds

`corpus/envelope` and `corpus/canonical` carry the field values the protocol
suite already uses, as the seven fields and the body separated by a zero byte,
the body last, which is eight parts and seven separators. The two directories
hold the same bytes on purpose: the seeds are field values and the two legs
drive different code from them. The separator is a zero byte rather than a line
feed on purpose: every field this protocol accepts is printable ASCII, so a zero
byte cannot be part of one, and a line feed separator would make it impossible
for a mutation to put a line feed inside a field. A field carrying a line feed
past the shape check is exactly the defect the canonical leg exists to find.

`corpus/address` carries the accepted and refused forms from the address suite,
one address per file, as raw bytes.

The corpus directory has its own `.gitattributes` holding every seed exactly as
written. A seed whose line endings moved on the way through git is a different
input from the one somebody committed.

## What this does not do

It does not reach a socket, a serialiser or the host. There is no wire format
below the field level in this tree yet, so what is fuzzed is the field decoders
and the reconstruction rather than a byte parser underneath them. When a body
gains a parser, it becomes the fourth leg.

It does not prove the absence of a defect. A run that writes no reproducer says
the inputs libFuzzer reached in the time it had did not break a property, and
the corpus it archived is what it reached.
