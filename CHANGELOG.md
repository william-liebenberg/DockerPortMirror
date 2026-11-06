# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Changes since the original single-file version of the tool.

### Added

- Windows, Linux and macOS clients are supported.
- Install as a .NET tool (`dotnet tool install -g DockerPortMirror`), from GitHub Releases
  (single-file binaries for six platforms) or with `winget`.
- Configuration file (`--config`, or `config.json` in the user configuration folder) and
  `DOCKERPORTMIRROR_*` environment variables.
- `--version`, generated `--help`, and `-v` as a short form of `--verbose`.
- Ports published on a range (`8000-8001->8000-8001/tcp`) are mirrored.
- A port that cannot be mirrored is retried with exponential backoff (1s to 60s) and reported once
  instead of every poll.
- A live dashboard in the terminal (Spectre.Console): connection and Docker status, mirrored ports grouped by
  container with their status and retry countdown, and a log panel. `--no-dashboard` (`NoDashboard`,
  `DOCKERPORTMIRROR_NODASHBOARD`) keeps the line-by-line output, which is also used when the output is redirected.
- The `+`, `-` and `!` lines show the container that publishes the port.

### Changed

- Output is produced through logging: same `+`, `-` and `!` lines, warnings and errors on standard error.
  The console output is not a stable interface for scripts.
- With `--verbose`, SSH diagnostics are printed as they happen.

### Fixed

- The SSH error of a tunnel that failed to start (for example "Address already in use") was hidden by an
  `InvalidOperationException`.
- `ssh://user@host:port` contexts lost their port.
- A container published on a specific host address was forwarded to `127.0.0.1` instead.
- One port that failed to start blocked the other new ports of the same poll.
- A hung `docker` command froze the tool; `docker` commands now time out and are killed on Ctrl+C.
- The default poll interval was 1 second while documented as 2; it is now 2 seconds.
- With `--verbose`, SSH output was buffered in memory for the life of each tunnel.
- On Windows, `ssh` processes outlived the tool when its console was closed or it was killed.

[Unreleased]: https://github.com/william-liebenberg/DockerPortMirror/commits/main
