# Changelog

All notable changes to SAMBA-Util are documented in this file.

## [Unreleased]

### Added

- The configuration window now includes a "Map unknown users to guest"
  toggle (`map to guest = Bad User`). It is enabled by default and, when
  disabled, writes `map to guest = Never` to `smb.conf`.
- The AppImage now bundles its runtime libraries (OpenSSL, ICU and the
  X11/xcb stack) so it no longer depends on the versions installed on the
  target system. They are taken from a container based on an older glibc
  (`packaging/appimage/`), which keeps the AppImage working on distributions
  such as Ubuntu 22.04+, Debian 12+ and Fedora. The font stack (fontconfig,
  freetype, GLib, ...) is deliberately left to the host: every desktop
  provides it, and bundling an older GLib breaks host tools that link it.
- The AppImage ships AppStream metadata and update information (with a
  `.zsync` file for delta updates), and provides the `samba-util` name used by
  its `.desktop` file. Inside the AppImage the desktop file is named after the
  reverse-DNS component id (`io.github.mijocecr.SambaUtil.desktop`) so that
  appimagetool finds the AppStream file and appstreamcli validates it without
  warnings; the AUR tarball keeps the plain `samba-util.desktop` name.

### Fixed

- SAMBA-Util now ensures `usershares.conf` exists in the Samba configuration
  directory, creating a sensible default when it is missing. Previously, if
  `smb.conf` included a non-existent `usershares.conf` (for example
  `include = /etc/samba/usershares.conf`), loading the configuration failed and
  the app could not start.
- The UI no longer aborts on systems without any system font. The bundled Inter
  font is now the default family instead of asking fontconfig for a system
  default that may not exist (for example in minimal installations).

## [4.3.0] - 2026-11-16

This is a security and reliability release. It hardens every place where user
input is passed to a shell, stops leaking credentials to the process list and
the logs, fixes several high-impact bugs and makes releases reproducible.

### Security

- Every value interpolated into a shell command (usernames, share paths, share
  names and remote hosts) is now quoted as a literal argument. Previously a
  crafted username or share path could run arbitrary commands, including as
  root through `sudo`.
- Samba passwords are no longer passed on the command line. `smbpasswd` reads
  them from standard input, and `smbclient`/`rpcclient` use a temporary `0600`
  credentials file, so they no longer show up in `ps`.
- Shell command logging now masks credentials (`-U user%****`,
  `password=****`, `smb://user:****@host`).
- The GUI "fix permissions" action validates the configured mode (octal) and
  quotes the path before running `chmod -R`.

### Fixed

- The CLI reported a valid `smb.conf` as invalid: `testparm` writes
  "Loaded services file OK." to stderr, not stdout.
- The share editor no longer rewrites `create mask`/`directory mask` to `755`
  when opening and saving a share. `0744` is now selectable and masks are
  compared ignoring the leading zero.
- The credentials dialog can no longer hang the app: closing it with the window
  button completes the pending operation and unblocks the Network tab.
- A disabled Samba account is detected correctly again (`pdbedit` shows the `D`
  flag padded inside brackets, e.g. `[DU         ]`).
- Changing a user password from the edit-user window works again
  (`smbpasswd -s`).
- A managed printing marker without its matching end marker no longer causes
  the rest of `smb.conf` to be discarded.
- Waiting on `gio`/`xdg-open` no longer reads `ExitCode` after a timeout; the
  process is killed and the call fails cleanly.
- Active GVfs mounts are matched by exact `server=`/`share=` fields instead of
  a substring, avoiding false matches such as `10.0.0.1` vs `10.0.0.10`.
- The application configuration is written atomically (temporary file +
  rename).
- The OS detection cache is thread-safe.
- The "path does not exist" validation message is no longer contradictory.

### Added

- `scripts/build-release.sh` builds the self-contained single-file binary, the
  `SAMBA-Util.tar.gz` AUR source tarball, the optional AppImage and the
  `SHA256SUMS` file.
- `packaging/` with `AppRun`, `samba-util.desktop` and `samba-util.png`, so the
  release artifacts no longer have to be assembled by hand.
- This changelog.

### Changed

- Version bumped to 4.3.0 in `SAMBA-Util.csproj` and `app.manifest`.
- Release tags now use the full three-part version (`samba-util_v4.3.0`), to
  match the version in the project files.
- README requirements fixed and the release process documented.
