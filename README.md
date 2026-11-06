# DockerPortMirror v1

A .NET 10 Windows console app for this topology:

```text
Windows 11 VM -> Docker CLI over SSH -> macOS Docker Desktop
```

DockerPortMirror dynamically discovers TCP ports published by containers on the remote Mac Docker host and creates Windows-local OpenSSH tunnels:

```text
Windows localhost:<port>
  -> SSH
  -> Mac localhost:<port>
  -> Docker container
```

This works when Docker publishes a container port as `127.0.0.1:<port>` on the Mac, where direct Windows `netsh portproxy` forwarding to the Mac's LAN address would not work.

## Prerequisites

- Windows 11
- .NET 10 SDK for building
- Docker CLI on `PATH`
- Windows OpenSSH Client (`ssh.exe`) on `PATH`
- A working SSH-based Docker context, such as `ssh://macdocker`
- Passwordless public-key authentication or an unlocked key in `ssh-agent`

The program uses `BatchMode=yes`, so it will not stop to request a password or key passphrase.

## Run from source

```powershell
dotnet run
```

The selected Docker context and SSH target are normally discovered automatically.

Optional overrides:

```powershell
dotnet run -- --context mac --ssh-target macdocker --poll 1 --verbose
```

## Publish as one executable

### Windows ARM64, typical for Parallels on Apple Silicon

```powershell
dotnet publish -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true
```

### Windows x64

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The executable is written under:

```text
bin\Release\net10.0\<runtime>\publish\DockerPortMirror.exe
```

## Usage

```text
DockerPortMirror.exe [options]

--context <name>       Docker context; defaults to `docker context show`
--ssh-target <target>  SSH host alias or user@host; defaults to context endpoint
--bind <address>       127.0.0.1 or localhost; default 127.0.0.1
--poll <seconds>       Polling interval; default 2
--verbose              Show SSH diagnostics
--help                 Show help
```

## Behaviour

- Polls running containers with `docker --context <context> ps`.
- Mirrors every published TCP port.
- Adds tunnels when containers start.
- Removes tunnels when containers stop.
- Restarts a tunnel if its SSH process exits.
- Removes all child SSH processes when stopped with Ctrl+C.
- Does not mirror UDP ports.
- Does not require Administrator rights because it binds ordinary dynamic ports above 1024.

## Troubleshooting

Confirm SSH is non-interactive before starting:

```powershell
ssh -o BatchMode=yes macdocker true
```

Confirm the Docker context works:

```powershell
docker context show
docker ps
```

If a local port is already occupied, the app reports an SSH bind error. Find the owner with:

```powershell
Get-NetTCPConnection -LocalPort <port> -State Listen
```
