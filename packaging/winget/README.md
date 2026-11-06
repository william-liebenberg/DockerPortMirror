# winget manifest

DockerPortMirror is published to [winget-pkgs](https://github.com/microsoft/winget-pkgs) as
`WilliamLiebenberg.DockerPortMirror`, a portable command (`dockerportmirror`) inside the `win-x64` and `win-arm64` release zips.

## First submission (by hand, once)

1. Tag and push the first stable release (for example `v1.0.0`) and wait for the **Release** workflow to
   publish the GitHub Release.
2. Either run `wingetcreate new` with the two zip URLs of the release and answer the prompts, or edit the
   template in [manifest/](manifest/): set `PackageVersion`, the URLs and the two `InstallerSha256` values (from
   `SHA256SUMS.txt` of the release).
3. Check it:

   ```powershell
   winget validate --manifest packaging/winget/manifest
   winget install --manifest packaging/winget/manifest   # needs "winget settings --enable LocalManifestFiles"
   ```

4. Submit it: `wingetcreate submit packaging/winget/manifest` (or open a pull request on winget-pkgs).

## Later versions (automatic)

Once the package exists in winget-pkgs, the **Release** workflow runs `wingetcreate update --submit` for
every stable `v*` tag. It needs a `WINGET_TOKEN` repository secret: a classic GitHub personal access
token with the `public_repo` scope, owned by the maintainer. Without the secret the step is skipped.
