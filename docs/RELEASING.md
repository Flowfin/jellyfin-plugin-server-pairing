# Releasing

A release is published by pushing a tag. Nothing is created by hand.

## The tag

The tag has the form `X.Y.Z-stable` or `X.Y.Z.W-stable`, for example `1.4.0-stable`
or `0.1.0.0-stable`. The numeric part is the plugin version that Jellyfin installs,
and it must be exactly the `version` in `build.yaml`, written the same way, with the
same number of parts. The `-stable` suffix lives only in the tag and in the release
name.

## Cutting a release

1. Write the entry first, then move the number, in one change on the release
   branch, and merge it. The entry goes under a new `## X.Y.Z.W` heading in
   [`../CHANGELOG.md`](../CHANGELOG.md), the same words go into the `changelog`
   field of `build.yaml` and `build.net10.0.yaml`, and `version` in both
   manifests and the three version fields in `Directory.Build.props` move to
   match. A version raised in one commit and described in another leaves a
   published version with no record of what it changed, and the record cannot be
   reconstructed once the release is out. What each part of the number promises
   is in [`versioning.md`](versioning.md).
2. Check that the commit you want to release is on that branch.
3. Push the tag for that commit:

    ```
    git tag 1.4.0-stable <commit>
    git push origin 1.4.0-stable
    ```

The `Publish Release` workflow takes it from there.

Push one tag at a time and wait for its run to finish. GitHub keeps at most one
queued run per concurrency group, and although the group here is keyed on the tag,
serialising them by hand is what keeps the release order readable.

## What the run produces

The workflow builds the plugin from the tagged commit, creates the GitHub release
for the tag, and attaches five files:

- the plugin archive
- the packaging metadata written beside it, `<archive>.zip.meta.json`
- the component list read out of the archive, `<archive>.zip.cdx.json`
- one `.md5` file, the checksum of the archive
- one `.sha256` file for the same archive

The component list is where to look for what is inside the archive without
downloading and opening it. It is CycloneDX 1.6, one component per file in the
archive with the SHA-256 of that file's bytes, and it is generated from the
archive this run built rather than from the project file, so a file that arrived
without anyone deciding to ship it is in the list. What it does not carry is a
version or a package identity per component: reading those means reading
assembly metadata out of a `.dll`, which needs a runtime the script that writes
the list does not have.

The same reading refuses the release, below, when the archive holds an assembly
`build.yaml` does not name. It is the script `.github/package-audit.sh`, which
the package check also runs on a pull request into `master`, so the list attached
to a release and the list attached to a pull-request run come out of the same
bytes. Which pull requests that check sees is the workflow's own filter, read in
item 5 of [`release.md`](release.md) rather than carried a second time here.

The `.md5` is the value a Jellyfin catalog serves as the plugin checksum. There is
exactly one per release so that no generator can pair a checksum with the wrong
file.

Three of those five are checked for existence by name before the release job
runs, so a release missing the archive, the packaging metadata or the component
list is not a state this route can reach:

    git grep -n 'for file in ' origin/master -- .github/workflows/publish.yaml
    origin/master:.github/workflows/publish.yaml:372:          for file in "${ARTIFACT}" "${ARTIFACT}.meta.json" "${ARTIFACT}.cdx.json"; do

The other two are outside that check because neither can go missing on its own.
Both are written from the archive in the release job itself, which refuses unless
it finds exactly one archive and exactly one metadata file to write them from.

The run also signs a build provenance statement for the archive, in a separate job
that downloads the archive and runs no build tooling. A downloaded archive can be
checked against it:

```
gh attestation verify <archive>.zip --repo <owner>/<repository>
```

Nothing here writes a plugin catalog. A GitHub release is the whole output. If this
repository previously published through the Jellyfin meta plugins workflow, that path
is gone and no catalog is fed until a manifest generator is added.

## What fails the run

- The tag does not end in `-stable`, or the workflow was started from something
  other than a tag.
- The numeric part of the tag differs from `version` in `build.yaml`.
- `build.yaml` is missing a required field, or `version`, `targetAbi`, `framework`
  or `guid` has the wrong shape.
- `framework` in `build.yaml` names a target the plugin project is not built for.
- A packaging manifest that shadows `build.yaml` is present, such as `jprm.yaml` or
  `meta.yaml`.
- `build.yaml` declares an `image` file that is not in the repository.
- The tagged commit is not contained in a release branch, or the tag was moved after
  the run started.
- There is no `packages.lock.json` next to the plugin project, so the release build
  cannot restore against a reviewed dependency graph. Create one with
  `dotnet restore <project> -p:RestorePackagesWithLockFile=true` and commit it.
- The version stamped into the assembly is not the version in `build.yaml`.
- The archive holds an assembly that `build.yaml` does not name in its artefact
  list, or it could not be read at all: an archive that will not extract, one
  with no entries, one with no assembly in it, or a manifest with no artefact
  list. Each of those is a reading that failed rather than a clean package.
- The build produced no archive, or no packaging metadata, or no component list.
- The build produced more than one archive.
- A release already exists for the tag.

All of these fail before anything is published.

## What the run notes without failing

The packaging tool warns when a manifest declares neither `image` nor `imageUrl`, and
the plugin then shows without a logo in a catalog. That is not a reason to hold a
release. Neither manifest is in that state, so it is not a warning this repository
currently produces:

    git grep -nE '^(image|imageUrl):' -- build.yaml build.net10.0.yaml
    build.net10.0.yaml:8:imageUrl: "https://raw.githubusercontent.com/Flowfin/jellyfin-plugin-server-pairing/master/img/logo.png"
    build.yaml:8:imageUrl: "https://raw.githubusercontent.com/Flowfin/jellyfin-plugin-server-pairing/master/img/logo.png"

Both point at a tracked file through the repository's own raw URL, so the logo is
served by GitHub rather than carried in the package. No route here reads that field.
The link check walks the tracked markdown for inline links and autolinks, and a
manifest is neither:

    git grep -n "files=\$(git ls-files" -- .github/link-check.sh
    .github/link-check.sh:42:files=$(git ls-files '*.md')

So a release whose logo URL has stopped resolving fails nothing and warns nobody, and
what a catalog shows in that case was not measured here.

## Re-running

A release that exists is not touched again. The release job asks whether a release
exists for the tag before it writes anything and stops if one does, and the upload
step is configured not to replace an asset of the same name. Replacing the bytes of a
version people have already installed is the failure this prevents, and it is worth
more than the convenience of a re-run.

So: if a release went out with the wrong contents, fix the problem, raise the version
in `build.yaml`, and push a new tag.

If a run failed **before** the release was created, the tag is still clean. Fix the
cause and re-run the workflow from the Actions page, or delete and re-push the tag.

If a run failed **after** the release was created but before every asset was attached,
the release is incomplete and a re-run will refuse it. What is possible then depends
on the repository settings below. Without immutable releases you can delete the
incomplete release, delete the tag, and push it again. With immutable releases you
cannot, and the version has to be raised.

## Repository settings this expects

- Default workflow permissions set to read only.
- A rule that restricts who may push `*-stable` tags.
- The `ABI floor build` check required on the release branches.
- Immutable releases, if the repository wants the guarantee that a published release
  can never be edited or deleted at all. The workflow does not depend on it: the
  refusal to touch an existing release is enforced in the release job. Turning it on
  removes the only recovery path for an incomplete release, so try it on one
  repository and cut a release there before turning it on everywhere.
