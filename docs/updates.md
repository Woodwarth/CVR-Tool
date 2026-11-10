# GitHub application updates

The header's Updates button checks the latest stable GitHub release only when
requested. It chooses Windows x64/ARM64 from OS architecture or Apple Silicon,
requires the exact version/platform asset and GitHub SHA-256 digest, checks
size and digest, then stages the full self-contained archive. No account,
SDK, administrator permissions, shell scripts or extra runtime are needed.

Archives are bounded and reject escaping paths, duplicate entries and links.
The staged desktop assembly version must match the release. Existing installs
are retained under local application data (`CvrTool/updates/versions`). The
active-version pointer is written atomically. A new app process waits for the
previous session owner to exit before starting the GUI. Packaged older versions
follow the active pointer on later launches; source builds do not redirect.
Version 0.3.0 predates the updater: download 0.4.0 manually once.

Updates close the application session, not the amplifier. Local in-memory
settings reset on restart; export a preset before restarting. Download or
verification failure leaves the current app available. Cancel closes the
dialog and cancels transfer/staging. Existing directories are never overwritten.
The checksum verifies integrity against GitHub metadata; it is not a developer
signature. Windows signing and Mac notarization remain unconfigured.

Metadata follows the [GitHub Releases API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)
and its [asset digest](https://docs.github.com/en/rest/releases/assets#get-a-release-asset).
Graphical peak EQ preview uses the [RBJ/W3C equations](https://www.w3.org/TR/audio-eq-cookbook/)
at an explicit 48 kHz. Unknown device filter types do not receive an invented curve.
