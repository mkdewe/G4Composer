#!/usr/bin/env bash
# Convenience wrapper around `docker run` for the g4composer image.
#
#   tools/run.sh examples/7ys7.inp
#   tools/run.sh --no-mirror --outdir out examples/7ys7.inp
#
# Paths are given relative to the repository root, which is mounted at /work.
# Everything after the options is passed straight through to the container
# entrypoint — see `tools/run.sh --help` for the full option list.

set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="$(tr -d '[:space:]' < "$REPO/VERSION")"
IMAGE="${G4COMPOSER_IMAGE:-g4composer:latest}"

if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
    echo "run.sh: image '$IMAGE' not found. Build it first:" >&2
    echo "        tools/build.sh" >&2
    exit 70
fi

exec docker run --rm \
    -u "$(id -u):$(id -g)" \
    -v "$REPO:/work" \
    "$IMAGE" "$@"
