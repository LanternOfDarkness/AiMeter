# Releasing AiMeter

Manual release checklist. No CI workflow exists yet; every step is done by hand.

## Checklist

1. Bump the version in `src/AiMeter/AiMeter.csproj` before publishing:

   ```xml
   <Version>X.Y.Z</Version>
   <AssemblyVersion>X.Y.Z.0</AssemblyVersion>
   <FileVersion>X.Y.Z.0</FileVersion>
   ```

2. Publish the single-file build:

   ```powershell
   dotnet publish src/AiMeter/AiMeter.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish/win-x64
   ```

3. Hash the binary:

   ```powershell
   Get-FileHash publish/win-x64/AiMeter.exe -Algorithm SHA256
   ```

   Write `AiMeter.exe.sha256` with one line, two spaces between hash and name:

   ```text
   <64 hex>  AiMeter.exe
   ```

   This asset is an optional fallback. GitHub adds a `digest` to every uploaded
   asset automatically, and the updater prefers it.

4. Confirm the publish output is still a single `AiMeter.exe`. The updater
   replaces only that file; any companion file in the publish output needs a
   release-contract change and an installer update first.

5. Tag the release `vX.Y.Z` (matching the csproj version), upload `AiMeter.exe`
   and optionally `AiMeter.exe.sha256`, and confirm the release page shows the
   asset digest. `/releases/latest` ignores prereleases.

6. Update the versioned download link and hash in `README.md` and
   `CHANGELOG.md`, and rebuild the installer if one is published.

## Why the tag, csproj version and binary must agree

The updater reads `ProductVersion` from the downloaded `AiMeter.exe` and refuses
to install when it is lower than the release tag. A release whose tag, csproj
version and uploaded binary disagree makes every client reject the update, and
the hash cannot catch this because it is computed from whatever was uploaded.
Rebuild and re-upload rather than retagging a mismatched binary.

## Add/Remove Programs caveat

After a self-update, the Inno Setup entry in Add/Remove Programs still shows the
version from the last installer run. That is expected: the app replaces only
its executable and does not rewrite uninstall metadata.

## Manual recovery

If a new build does not start, delete the failed `AiMeter.exe` and rename
`AiMeter.exe.old` (left next to it by the updater) back to `AiMeter.exe`.
