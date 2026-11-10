# GitHub Actions

CI runs locked restore, Release build, core/network checks and the shared-session
MCP smoke test on windows-latest (x64), windows-11-arm (ARM64) and macos-latest
(Apple Silicon). Release jobs build on the corresponding native host; Windows
ARM64 is not merely cross-published on x64.

A matching version tag builds three self-contained packages and runs integrated
MCP from each packaged desktop executable. The aggregation job requires all
three archives before creating a draft release. Publication is explicit.

Desktop interaction tests run locally in a graphical Windows session; CI builds
and MCP tests do not prove native GUI behavior or physical amplifier access.
