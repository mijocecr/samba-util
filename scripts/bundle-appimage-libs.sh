#!/usr/bin/env bash
#
# Bundle the runtime libraries the AppImage needs into <appdir>/usr/lib.
#
# The libraries are copied out of a container based on an older glibc (see
# packaging/appimage/Dockerfile) so that the resulting AppImage does not
# require a newer glibc than the one it was built against.
#
# Usage: scripts/bundle-appimage-libs.sh <appdir>
#
# Environment:
#   APPIMAGE_BASE_IMAGE  override the base image tag (default below)
#   APPIMAGE_NO_DOCKER    set to 1 to skip bundling and use the host libraries
#
set -euo pipefail

APPDIR="${1:?usage: bundle-appimage-libs.sh <appdir>}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE="${APPIMAGE_BASE_IMAGE:-samba-util-appimage-base:alma9}"

if [[ "${APPIMAGE_NO_DOCKER:-0}" == "1" ]]; then
    echo "warning: APPIMAGE_NO_DOCKER=1; the AppImage will use the host libraries" >&2
    exit 0
fi

if ! command -v docker >/dev/null 2>&1; then
    echo "warning: docker not found; the AppImage will use the host libraries" >&2
    exit 0
fi

echo "==> Building library base image ($IMAGE)"
docker build -q -t "$IMAGE" "$ROOT/packaging/appimage" >/dev/null

echo "==> Bundling runtime libraries"
docker run --rm \
    --user "$(id -u):$(id -g)" \
    -v "$APPDIR":/appdir \
    -v "$ROOT/packaging/appimage/bundle-libs.sh":/bundle-libs.sh:ro \
    "$IMAGE" bash /bundle-libs.sh /appdir
