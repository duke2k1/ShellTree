# ShellTree v0.2

Developed by David Charnock.

A Windows desktop browser for Linux directories over SSH, inspired by [FetchTree](https://github.com/duke2k1/FetchTree).

## Run

Extract the complete Windows package and launch **ShellTree.exe**. Keep the included DLLs, configuration file and notices beside it. Requires Windows 10/11 and .NET Framework 4.8. SSH is integrated using SSH.NET; no OpenSSH installation or terminal is required.

Enter **Host**, **Port**, **Username**, and **Password**, choose an absolute Linux root path and scan depth, then click **Connect / Scan**. The password field is masked. The server must permit SSH password authentication. Private-key authentication, keyboard-interactive authentication and MFA are not included in v0.2.

On first connection, ShellTree displays the server's SHA-256 fingerprint. Verify it with the server administrator before trusting it. Trusted keys are saved under `%LOCALAPPDATA%\\ShellTree\\trusted-hosts.json`. Changed keys are blocked. This trust store is independent of OpenSSH's known_hosts file.

Passwords remain in memory while the app is open; they are never written to snapshots, settings, command-line arguments, or logs. Changing the host or username, opening a saved listing, or closing the app clears the password field. No SSH credentials are required to browse a saved listing.

## Browse

Use the folder tree or double-click folders in the details view. **Back** and **Up** provide familiar navigation.

JSON controls are under **File → Open JSON** and **File → Save JSON As** in the top menu. They do not appear on the main connection/navigation panel. **Help → SSH connection help** explains connection requirements.

Depth 0 lists the root and its immediate children. Higher depths preload more levels. Folders beyond the scan depth display **Not scanned** instead of appearing empty. To browse deeper, enter that folder's absolute path as Root and scan again.

## Scanning and storage

ShellTree uses SSH command execution with `sh`, `cd` and GNU `find -printf`. It does not use SCP, FTP, SFTP, or a remote agent. Names and metadata are NUL-separated to preserve spaces, quotes, tabs and newlines. Filenames must be valid UTF-8. Symlinks are listed but never followed, and file contents are neither transferred nor executed.

Successful scans are saved atomically to `%LOCALAPPDATA%\\ShellTree\\last.snapshot.json`; the GUI then reads this file. It reopens the last listing on startup. Failed, cancelled, permission-denied or truncated scans preserve the previous listing. Snapshots include host, username, file paths, sizes and timestamps, but no passwords.

The server requires GNU find with `-printf`; BusyBox/BSD variants without that option are unsupported. Limits: depth 0–20, 100,000 entries, 32 MB listing/JSON, 64 KB diagnostics, 15-second connection timeout and two-minute command timeout. A scan is a point-in-time approximation; remote files can change while it runs.

## Build and test

```powershell
./build.ps1 -Test
./bin/ShellTree.exe
```

The build restores pinned NuGet packages and verifies their SHA-256 hashes from `dependencies.lock.json`. Internet access is required on the first build. Optional `-NodePath <node.exe>` selects Node's HTTPS downloader when needed.

The automated suite checks parsing, path integrity, shell quoting, JSON preservation, output limits, password exclusion and host-key trust. Optional SSH integration tests use a loopback-only fixture with synthetic credentials:

```powershell
python -m pip install -r tests/requirements.txt
./tests/integration.ps1
```

The fixture checks password success/failure, unknown-host rejection, Unicode output, remote errors, truncated output and cancellation. It uses SSH command execution and does not serve SFTP. A separate optional test runs the scanner script with real GNU find from Git for Windows. Testing against your own Linux server is still recommended.

## JSON format

Version 1 remains compatible with v0.1 snapshots. Connection metadata, `Root`, `Depth` and `ScannedUtc` accompany `Entries`, each with relative `Path`, Linux `Kind`, byte `Size` and `ModifiedUnix`. See [examples/demo.json](examples/demo.json).

## Dependencies and scope

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and the included notice archive for bundled library licenses.

File downloads, uploads, drag-to-Explorer transfers, on-demand live expansion and an installer are not implemented. v0.2 focuses on built-in password-based SSH, bounded directory scans and cached navigation.
