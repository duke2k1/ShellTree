# ShellTree v0.1

A Windows desktop browser for Linux directories over SSH, inspired by [FetchTree](https://github.com/duke2k1/FetchTree).

ShellTree scans a remote directory using ordinary Linux shell tools, saves the results to a local JSON file, and reads that file into an Explorer-style folder tree and details view. No SCP, FTP, SFTP subsystem, remote agent, or remote Python installation is used.

## Build and run

Requires Windows 10/11, .NET Framework 4.8, and the Windows OpenSSH Client optional feature. No third-party application libraries are required.

```powershell
./build.ps1 -Test
./bin/ShellTree.exe
```

You can also open a snapshot directly: `ShellTree.exe examples/demo.json`.

## Connect

1. Set up SSH key authentication using Windows OpenSSH or its agent.
2. Connect once in a terminal: `ssh -p 22 username@hostname`. Verify the server fingerprint through a trusted channel before accepting it. Confirm that your key works without a password prompt.
3. In ShellTree, enter the host, username, port, absolute Linux root path, and depth. Click **Connect / Scan**.
4. Browse folders in the left tree or double-click folders in the details view. Use **Back** and **Up** to navigate.
5. **Save JSON As** exports a snapshot; **Open JSON** loads one without a server connection.

The initial version supports keys/agent authentication, not interactive passwords or key-passphrase prompts. It uses the normal OpenSSH configuration and known-hosts store, with strict host verification and batch mode. It never saves a password or private key. Host aliases from your SSH configuration can be used, but the explicit username and port entered in the app take precedence.

## How scanning works

The scanner starts Windows `ssh.exe`, sends a shell script through standard input, changes to the requested directory with a safely quoted `cd`, and runs GNU `find` with `-maxdepth` and `-printf`. Names and metadata are separated by NUL bytes, so spaces, tabs, quotes, and newlines are preserved. Human-formatted `ls -l` output is intentionally not parsed.

GNU find is required on the Linux host; BusyBox/BSD find without `-printf` is not supported in v0.1. Filenames must be valid UTF-8; unsupported bytes cause an explicit scan error. Symbolic links are displayed but not followed, preventing symlink loops. Special files are listed but never opened. Scanning only reads metadata; it does not transfer file contents.

Depth 0 lists the root and its immediate children; higher values include additional levels. A folder at the depth boundary is labeled **Not scanned**, rather than presented as a confirmed empty folder. To scan beyond that boundary, enter the folder's absolute path in Root and scan again. Refreshing uses **Connect / Scan** and replaces the current snapshot after success.

Every successful scan is written atomically to `%LOCALAPPDATA%\ShellTree\last.snapshot.json`; the GUI then reloads this JSON. On startup, the last snapshot opens automatically. Failed, cancelled, truncated, permission-denied, or unsupported scans preserve the previous cache. A snapshot describes a bounded scan, not a live filesystem or transactionally consistent view. Remote files may change during or after scanning.

Limits: 0–20 depth, 100,000 entries, 32 MB listing/JSON, 64 KB SSH diagnostics, 15-second connection timeout, and two-minute scan timeout. Use smaller roots for large servers. Snapshots include host, username, paths, sizes and timestamps, so keep exported snapshots private when needed. They contain no credentials.

## JSON format

`Version` is currently `1`. Connection metadata, `Root`, `Depth`, and `ScannedUtc` accompany an `Entries` array. Each entry has `Path` relative to the scan root (`.` or `./folder/file`), `Kind` (`d`, `f`, `l`, or a Linux special-file type), `Size` in bytes, and `ModifiedUnix` in Unix seconds. The serializer also emits derived `Name` and `Parent` fields; these are recomputed on load. Linux path casing and separators are preserved even though the interface resembles Windows Explorer.

See [examples/demo.json](examples/demo.json) for an offline example.

## Current scope

This is a first working browser/scanner foundation. File downloads, uploads, drag-to-Explorer transfers, password authentication, live on-demand folder expansion, and installers are not implemented. Unlike FetchTree's HTTP browser, this release is focused on SSH metadata and JSON-backed navigation.

## Checks

`build.ps1 -Test` checks filename framing, unusual names, symlink records, malformed/truncated listings, path integrity, command quoting, option injection, depth semantics, JSON round trips, atomic replacement, and preservation after failed saves. GitHub Actions builds and runs these checks on Windows. Live SSH interoperability must also be tested against your Linux server.
