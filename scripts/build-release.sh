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
#   - SAMBA_Util-x86_64.AppImage.zsync   delta updates for the AppImage
#   - SHA256SUMS
#
# To build the AppImage:
#   APPIMAGETOOL=/path/to/appimagetool-x86_64.AppImage \
#   APPIMAGETOOL_FLAGS=--appimage-extract-and-run \
#   scripts/build-release.sh
#
# The AppImage bundles its runtime libraries (OpenSSL, ICU, X11) taken
# from a container based on an older glibc, so it runs on a wide range of
# distributions. Docker is required for that step; set APPIMAGE_NO_DOCKER=1 to
# skip it and fall back to the host libraries.
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

# El tarball alimenta el paquete AUR, que instala samba-util.desktop y
# samba-util.png desde su raíz. Si alguno faltase, el paquete quedaría
# incompleto; abortamos el build en vez de publicar un tarball roto.
for required in ./samba-util.desktop ./samba-util.png; do
    if ! tar -tzf "$DIST/SAMBA-Util.tar.gz" | grep -qx -- "$required"; then
        echo "error: $required no está en SAMBA-Util.tar.gz" >&2
        exit 1
    fi
done

if [[ -n "${APPIMAGETOOL:-}" ]]; then
    echo "==> Creating AppImage"
    APPDIR="$DIST/.AppDir"
    rm -rf "$APPDIR"
    mkdir -p \
        "$APPDIR/usr/bin" \
        "$APPDIR/usr/share/applications" \
        "$APPDIR/usr/share/icons/hicolor/1024x1024/apps"

    # The AppImage carries the folder-based self-contained build (not the
    # single-file one) so that nothing has to be extracted into $HOME at
    # startup. This keeps it working on read-only or noexec home directories.
    cp -a "$FOLDER/." "$APPDIR/usr/bin/"
    rm -f "$APPDIR/usr/bin/samba-util.desktop" "$APPDIR/usr/bin/samba-util.png"

    # The .desktop file uses "samba-util"; provide that name inside the
    # AppImage too (the AUR package does the same with a symlink).
    ln -sf SAMBA-Util "$APPDIR/usr/bin/samba-util"

    cp "$ROOT/packaging/AppRun" "$APPDIR/AppRun"
    # The AppImage names the desktop file after its reverse-DNS component id.
    # appimagetool looks for the AppStream metadata in
    # usr/share/metainfo/<desktop-basename>.appdata.xml, and appstreamcli
    # requires that file to be named after the component id, so both names must
    # match. The AUR tarball keeps the plain "samba-util.desktop" name.
    cp "$ROOT/packaging/samba-util.desktop" "$APPDIR/io.github.mijocecr.SambaUtil.desktop"
    cp "$ROOT/packaging/samba-util.desktop" "$APPDIR/usr/share/applications/io.github.mijocecr.SambaUtil.desktop"
    cp "$ROOT/packaging/samba-util.png" "$APPDIR/samba-util.png"
    cp "$ROOT/packaging/samba-util.png" "$APPDIR/usr/share/icons/hicolor/1024x1024/apps/samba-util.png"
    install -Dm644 "$ROOT/packaging/io.github.mijocecr.SambaUtil.metainfo.xml" \
        "$APPDIR/usr/share/metainfo/io.github.mijocecr.SambaUtil.appdata.xml"
    ln -sf samba-util.png "$APPDIR/.DirIcon"
    chmod +x "$APPDIR/AppRun" "$APPDIR/usr/bin/SAMBA-Util"
    # The publish output marks some files as executable only for the owner
    # (0744); AppImage requires them to be readable/executable by everyone.
    chmod -R a+rX "$APPDIR"

    # Bundle OpenSSL, ICU and the X11 stack so the AppImage does not depend on
    # the versions installed on the target system.
    bash "$ROOT/scripts/bundle-appimage-libs.sh" "$APPDIR"

    # appimagetool emits the .zsync referenced by the update information only
    # when zsyncmake is in PATH. Its own AppImage bundles zsyncmake, so fall
    # back to that when the host does not provide it.
    TOOL_PATH_DIR=""
    if ! command -v zsyncmake >/dev/null 2>&1 && [[ "$APPIMAGETOOL" == *.AppImage ]]; then
        TOOL_DIR="$DIST/.appimagetool"
        rm -rf "$TOOL_DIR"
        mkdir -p "$TOOL_DIR"
        ( cd "$TOOL_DIR" && "$APPIMAGETOOL" --appimage-extract >/dev/null 2>&1 ) || true
        [[ -d "$TOOL_DIR/squashfs-root/usr/bin" ]] && TOOL_PATH_DIR="$TOOL_DIR/squashfs-root/usr/bin"
    fi

    read -r -a APPIMAGETOOL_FLAGS <<< "${APPIMAGETOOL_FLAGS:-}"
    # appimagetool writes the .zsync next to its working directory, so run it
    # from dist/ to keep the artifacts together.
    (
        cd "$DIST"
        PATH="$TOOL_PATH_DIR${TOOL_PATH_DIR:+:}$PATH" ARCH=x86_64 "$APPIMAGETOOL" "${APPIMAGETOOL_FLAGS[@]}" \
            -u "${APPIMAGE_UPDATE_INFO:-gh-releases-zsync|mijocecr|samba-util|latest|SAMBA_Util-*-x86_64.AppImage.zsync}" \
            "$APPDIR" "$DIST/SAMBA_Util-x86_64.AppImage"
    )
    rm -rf "$APPDIR" "$DIST/.appimagetool"
fi

rm -rf "$SINGLE" "$FOLDER"

echo "==> Checksums"
(
    cd "$DIST"
    shopt -s nullglob
    sha256sum SAMBA-Util SAMBA-Util.pdb SAMBA-Util.tar.gz ./*.AppImage ./*.zsync > SHA256SUMS
)

echo "==> Done: $DIST"
ls -lh "$DIST"
