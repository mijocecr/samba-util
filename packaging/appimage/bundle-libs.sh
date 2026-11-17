#!/usr/bin/env bash
#
# Runs INSIDE the AppImage build container (see Dockerfile in this folder).
#
# Copies every shared library the application needs at runtime into
# <appdir>/usr/lib, following transitive dependencies with ldd. It also
# bundles the libraries the application loads with dlopen, which ldd cannot
# discover (OpenSSL, ICU and the X11/xcb stack used by Avalonia).
#
# Libraries that must match the host are never bundled: the C library and its
# companions, the C++ runtime, the GPU/Mesa/Wayland/audio stacks, and the
# font/GLib stack (fontconfig, freetype, harfbuzz, GLib, ...). Those are
# provided by every desktop system and break if they are replaced: bundling an
# older GLib, for instance, makes host tools that link it fail with missing
# symbols.
#
# Usage: bundle-libs.sh <appdir>
#
set -euo pipefail

APPDIR="${1:?usage: bundle-libs.sh <appdir>}"
BINDIR="$APPDIR/usr/bin"
DEST="$APPDIR/usr/lib"
mkdir -p "$DEST"

# Sonames loaded at runtime with dlopen. Extracted from the managed Avalonia
# assemblies and the native .NET libraries. ldd does not report these.
ROOTS=(
    # OpenSSL (System.Security.Cryptography)
    libssl.so.3 libcrypto.so.3
    # ICU (System.Globalization)
    libicuuc.so libicui18n.so libicudata.so
    # X11 / xcb (Avalonia.X11)
    libX11.so.6 libXext.so.6 libXcursor.so.1 libXfixes.so.3 libXi.so.6
    libXrandr.so.2 libXrender.so.1 libXau.so.6 libXdmcp.so.6
    libxcb.so.1 libICE.so.6 libSM.so.6
    # Misc, stable and needed by the above
    libuuid.so.1
)

# Never bundled. Matched against the soname.
EXCLUDE_RE='^(ld-linux[^ ]*|libc\.so\.[0-9]+|libdl\.so\.[0-9]+|libm\.so\.[0-9]+|libmvec\.so\.[0-9]+|libpthread\.so\.[0-9]+|librt\.so\.[0-9]+|libresolv\.so\.[0-9]+|libutil\.so\.[0-9]+|libnsl\.so\.[0-9]+|libanl\.so\.[0-9]+|libBrokenLocale\.so\.[0-9]+|libthread_db\.so\.[0-9]+|libnss_[^ ]*|libcrypt\.so\.[0-9]+|libstdc\+\+\.so\.[0-9]+|libgcc_s\.so\.[0-9]+|libGL\.so\.[0-9]+|libEGL\.so\.[0-9]+|libGLX\.so\.[0-9]+|libGLdispatch\.so\.[0-9]+|libOpenGL\.so\.[0-9]+|libGLU\.so\.[0-9]+|libglapi\.so\.[0-9]+|libdrm\.so\.[0-9]+|libgbm\.so\.[0-9]+|libwayland-[^ ]*|libxcb-dri[0-9]+\.so\.[0-9]+|libxcb-present\.so\.[0-9]+|libxcb-sync\.so\.[0-9]+|libxcb-randr\.so\.[0-9]+|libasound\.so\.[0-9]+|libjack\.so\.[0-9]+|libpipewire-[^ ]*|libselinux\.so\.[0-9]+|libnvidia[^ ]*|libcuda\.so\.[0-9]+|libfontconfig\.so\.[0-9]+|libfreetype\.so\.[0-9]+|libharfbuzz\.so\.[0-9]+|libglib-2\.0\.so\.[0-9]+|libgobject-2\.0\.so\.[0-9]+|libgmodule-2\.0\.so\.[0-9]+|libgio-2\.0\.so\.[0-9]+|libpcre[^ ]*|libgraphite2\.so\.[0-9]+|libpng[0-9]*\.so\.[0-9]+|libexpat\.so\.[0-9]+|libbz2\.so\.[0-9]+|libbrotli[^ ]*|liblzma\.so\.[0-9]+|libxml2\.so\.[0-9]+|libz\.so\.[0-9]+)$'

declare -A SEEN=()

resolve() {
    # soname -> real file path (empty on failure). Falls back to a versioned
    # match so that "libicuuc.so" resolves to "libicuuc.so.67".
    local name="$1" p
    p="$(ldconfig -p | awk -v n="$name" '$1==n {print $NF; exit}')"
    if [[ -z "$p" ]]; then
        p="$(ldconfig -p | awk -v n="$name" '$1 ~ ("^" n "\\.[0-9]+$") {print $NF; exit}')"
    fi
    [[ -n "$p" ]] || return 1
    readlink -f "$p"
}

deps_of() {
    ldd "$1" 2>/dev/null | awk '$2=="=>" && $1 ~ /\.so/ {print $1}'
}

process() {
    local name="$1"
    [[ -n "${SEEN[$name]:-}" ]] && return 0
    SEEN[$name]=1

    # Libraries that already ship next to the executable are the application's
    # own (the .NET runtime); they are found through the loader's $ORIGIN and
    # must not be duplicated here.
    [[ -e "$BINDIR/$name" ]] && return 0

    if [[ "$name" =~ $EXCLUDE_RE ]]; then
        echo "    host    $name"
        return 0
    fi

    local real
    if ! real="$(resolve "$name")"; then
        echo "    MISSING $name (not found in base image)" >&2
        return 0
    fi

    local base
    base="$(basename "$real")"
    cp -fL "$real" "$DEST/$base"
    chmod 0755 "$DEST/$base" || true
    [[ "$base" == "$name" ]] || ln -sf "$base" "$DEST/$name"
    echo "    bundle  $name"

    local dep
    while IFS= read -r dep; do
        [[ -n "$dep" ]] && process "$dep"
    done < <(deps_of "$real")
}

echo "  bundling runtime libraries into usr/lib"

# Seed with the dependencies of the application's own native libraries so that
# everything they need (X11/xcb, lttng-ust, ...; libstdc++ and the font stack
# are excluded) is pulled in.
for so in "$BINDIR"/*.so*; do
    [[ -f "$so" ]] || continue
    while IFS= read -r dep; do
        [[ -n "$dep" ]] && process "$dep"
    done < <(deps_of "$so")
done

# Then the dlopen-only roots.
for root in "${ROOTS[@]}"; do
    process "$root"
done

# Hard requirement: without these the application cannot start or render.
for required in libssl.so.3 libcrypto.so.3 libicuuc.so libicui18n.so \
                libX11.so.6 libxcb.so.1; do
    if [[ ! -e "$DEST/$required" ]]; then
        echo "error: $required was not bundled" >&2
        exit 1
    fi
done

echo "  $(find "$DEST" -maxdepth 1 -type f | wc -l) libraries bundled"
