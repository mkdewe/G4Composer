#!/usr/bin/env bash
# Build the quadro image.
#
#   tools/build.sh                          # uses third_party/, tags quadro:latest
#                                           # (and quadro16:latest)
#   tools/build.sh -t myquadro:dev          # custom tag
#   tools/build.sh --no-cache               # rebuild every layer from scratch
#
# CYANA and Xplor-NIH are passed as BuildKit *named build contexts*, so they can
# live anywhere — they do not have to be inside this repository. Override the
# paths when they are shared with other quadro versions in a parent repository:
#
#   CYANA_DIR=../cyana-2.1 XPLOR_DIR=../xplor-nih-2.39 tools/build.sh
#
# ⚠ The resulting image embeds CYANA and Xplor-NIH. Never push it to a registry
#   that non-licensees can pull from.

set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="$(tr -d '[:space:]' < "$REPO/VERSION")"

CYANA_DIR="${CYANA_DIR:-$REPO/third_party/cyana-2.1}"
XPLOR_DIR="${XPLOR_DIR:-$REPO/third_party/xplor-nih-2.39}"
TAG="${QUADRO_TAG:-quadro:latest}"
VERSION_TAG="quadro$(echo "$VERSION" | tr '[:upper:]' '[:lower:]'):latest"

NO_CACHE=()

while [ $# -gt 0 ]; do
    case "$1" in
        -t|--tag) TAG="$2"; VERSION_TAG=""; shift 2 ;;
        # Rebuild every layer from scratch. Use it when verifying that the image
        # can be reproduced from the repository alone, rather than from whatever
        # happens to be in the local build cache.
        --no-cache) NO_CACHE=(--no-cache); shift ;;
        -h|--help) sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \?//'; exit 0 ;;
        *) echo "build.sh: unknown argument '$1'" >&2; exit 64 ;;
    esac
done

CYANA_DIR="$CYANA_DIR" XPLOR_DIR="$XPLOR_DIR" "$REPO/tools/check-third-party.sh"

# Named build contexts need BuildKit and Dockerfile frontend 1.4+.
if ! docker buildx version >/dev/null 2>&1; then
    echo "build.sh: docker buildx not available — it is required for --build-context." >&2
    echo "          Install Docker 23+ or the buildx plugin." >&2
    exit 70
fi

TAGS=(-t "$TAG")
if [ -n "$VERSION_TAG" ] && [ "$VERSION_TAG" != "$TAG" ]; then
    TAGS+=(-t "$VERSION_TAG")
fi

echo
echo "Building $TAG (engine $VERSION)"
echo "  CYANA: $CYANA_DIR"
echo "  Xplor: $XPLOR_DIR"
echo

DOCKER_BUILDKIT=1 docker buildx build \
    --load \
    "${NO_CACHE[@]}" \
    -f "$REPO/docker/Dockerfile" \
    "${TAGS[@]}" \
    --build-arg "QUADRO_VERSION=$VERSION" \
    --build-context "cyana=$CYANA_DIR" \
    --build-context "xplor=$XPLOR_DIR" \
    "$REPO"

echo
echo "Built $TAG. Try it:"
echo "  docker run --rm -v \"\$PWD:/work\" $TAG examples/7ys7.inp"
