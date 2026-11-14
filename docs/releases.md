# Releases

Version lives in Directory.Build.props. Push a matching v* tag to build native
Windows x64, Windows ARM64 and macOS Apple Silicon self-contained packages.
The workflow creates a draft only after all three jobs pass. Each package
includes Desktop and an optional standalone MCP executable. Integrated MCP is
smoke-tested from the packaged desktop executable on each native runner.
No SDK, Python or installed .NET runtime is required by users.

Before publication, inspect all three assets, archive paths, CPU architecture,
runtime files and SHA-256 digests. Publish the verified draft explicitly.
Windows packages are unsigned. Mac is ad-hoc signed, not notarized.

CI runs the locked restore, release build, core and network checks, and an MCP smoke test on all three platforms. Run the desktop interaction checks locally in a graphical session.
