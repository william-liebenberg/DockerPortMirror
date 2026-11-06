# Security policy

## Supported versions

Only the latest release receives security fixes.

## Reporting a vulnerability

Please do **not** open a public issue. Report it privately through
[GitHub security advisories](https://github.com/william-liebenberg/DockerPortMirror/security/advisories/new),
with the version, your platform and the steps to reproduce. You should receive an answer within a week.

## Security model

- DockerPortMirror only binds `127.0.0.1` or `localhost`; any other bind address is rejected, so mirrored
  ports are never exposed to the network.
- It runs the `docker` and `ssh` programs found on `PATH`, with arguments passed as a list (no shell).
- SSH runs with `BatchMode=yes`: it never prompts for, receives or stores passwords or passphrases.
  Authentication is entirely that of your OpenSSH configuration and agent.
- It reads no secrets, and writes nothing to disk.
