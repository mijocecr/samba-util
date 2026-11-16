#!/usr/bin/env bash
#
# Build the release artifacts for SAMBA-Util.
#
# Usage:
#   scripts/build-release.sh [version]
#
# The version defaults to the <Version> value in SAMBA-Util.csproj.
# Artifacts are written to dist/<version>/:
#   - SAMBA-Util              self-contained single-file executable
#   - SAMBA-Util.pdb          portable debug symbols
#   - SAMBA-Util.tar.gz       folder-based self-contained build + desktop files
#   - SAMBA_Util-x86_64.AppImage   (only when APPIMAGETOOL is set)
#   - SHA256SUMS
#
# To build the AppImage:
#   APPIMAGETOOL=/path/to/appimagetool-x86_64.AppImage \
#   APPIMAGETOOL_FLAGS=--appimage-extract-and-run \
#   scripts/build-release.sh
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/SAMBA-Util.csproj"
RID="${RID:-linux-x64}"

VERSION="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJECT" | head -n 1)}"
if [[ -z "$VERSION" ]]; then
    echo "error: no version found; pass one as an argument" >&2
    exit 1
fi

DIST="$ROOT/dist/$VERSION"
SINGLE="$DIST/.single"
FOLDER="$DIST/.folder"

echo "==> Building SAMBA-Util $VERSION ($RID)"
rm -rf "$DIST"
mkdir -p "$SINGLE" "$FOLDER"

# Restore once with the target RID so both publishes share the same assets
# file. A plain build in between can otherwise overwrite it without the RID.
dotnet restore "$PROJECT" -r "$RID" --nologo

echo "==> Single-file publish"
dotnet publish "$PROJECT" \
    -c Release \
    -r "$RID" \
    --self-contained true \
    --no-restore \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=false \
    -o "$SINGLE" \
    --nologo

cp "$SINGLE/SAMBA-Util" "$DIST/SAMBA-Util"
if [[ -f "$SINGLE/SAMBA-Util.pdb" ]]; then
    cp "$SINGLE/SAMBA-Util.pdb" "$DIST/SAMBA-Util.pdb"
fi

echo "==> Folder publish"
dotnet publish "$PROJECT" \
    -c Release \
    -r "$RID" \
    --self-contained true \
    --no-restore \
    -p:PublishSingleFile=false \
    -o "$FOLDER" \
    --nologo

cp "$ROOT/packaging/samba-util.desktop" "$ROOT/packaging/samba-util.png" "$FOLDER/"

echo "==> Creating SAMBA-Util.tar.gz"
# Tarball reproducible: orden fijo, propietario y fecha fijos, y gzip sin el
# nombre/estampado original en su cabecera. Dos builds del mismo código
# producen un archivo idéntico byte a byte, de modo que el checksum de AUR
# es estable.
tar --sort=name \
    --owner=0 --group=0 --numeric-owner \
    --mtime='@1577836800' \
    --pax-option=exthdr.name=%d/PaxHeaders/%f,delete=atime,delete=ctime \
    -cf - -C "$FOLDER" . | gzip -n -9 > "$DIST/SAMBA-Util.tar.gz"

if [[ -n "${APPIMAGETOOL:-}" ]]; then
    echo "==> Creating AppImage"
    APPDIR="$DIST/.AppDir"
    mkdir -p \
        "$APPDIR/usr/bin" \
        "$APPDIR/usr/share/applications" \
        "$APPDIR/usr/share/icons/hicolor/1024x1024/apps"

    cp "$DIST/SAMBA-Util" "$APPDIR/usr/bin/SAMBA-Util"
    cp "$ROOT/packaging/AppRun" "$APPDIR/AppRun"
    cp "$ROOT/packaging/samba-util.desktop" "$APPDIR/samba-util.desktop"
    cp "$ROOT/packaging/samba-util.desktop" "$APPDIR/usr/share/applications/samba-util.desktop"
    cp "$ROOT/packaging/samba-util.png" "$APPDIR/samba-util.png"
    cp "$ROOT/packaging/samba-util.png" "$APPDIR/usr/share/icons/hicolor/1024x1024/apps/samba-util.png"
    ln -sf samba-util.png "$APPDIR/.DirIcon"
    chmod +x "$APPDIR/AppRun" "$APPDIR/usr/bin/SAMBA-Util"

    read -r -a APPIMAGETOOL_FLAGS <<< "${APPIMAGETOOL_FLAGS:-}"
    ARCH=x86_64 "$APPIMAGETOOL" "${APPIMAGETOOL_FLAGS[@]}" \
        "$APPDIR" "$DIST/SAMBA_Util-x86_64.AppImage"
    rm -rf "$APPDIR"
fi

rm -rf "$SINGLE" "$FOLDER"

echo "==> Checksums"
(
    cd "$DIST"
    shopt -s nullglob
    sha256sum SAMBA-Util SAMBA-Util.pdb SAMBA-Util.tar.gz ./*.AppImage > SHA256SUMS
)

echo "==> Done: $DIST"
ls -lh "$DIST"
