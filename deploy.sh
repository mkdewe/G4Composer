#!/bin/bash
set -e

echo "=== G4Composer Deploy ==="
cd /home/G4Composer

echo "[1/6] git pull"
git reset --hard origin/main
git checkout -- G4Composer.Server/obj/ G4Composer.Server/bin/ G4Composer.Server/Data/Migrations/AppDbContextModelSnapshot.cs 2>/dev/null || true
git pull

echo "[2/6] docker-biotools: update submodules + rebuild changed images"
# image tag -> build context dir inside docker-biotools/
declare -A IMAGES=(
  [quadro14l:latest]=quadro14L
  [quadro14n:latest]=quadro14N
  [gqrs:latest]=gqrsMapper
  [onquadro-aligner:latest]=onquadroAligner
  [viennarna:latest]=ViennaRNA
  [eltetrado:latest]=eltetrado
  [dnatco:latest]=dnatco
  [openmm-utils:latest]=openmmUtils
)
# Images whose Dockerfile `git clone`s an upstream repo at build time. Their build
# context dir never changes when upstream does, so we detect a new version by
# comparing the remote HEAD against the sha stamped on the last built image and
# rebuild with --no-cache (a cached build would silently keep the old code).
declare -A CLONE_REPO=(
  [onquadro-aligner:latest]=https://github.com/tzok/onquadro-aligner
  [openmm-utils:latest]=https://github.com/tzok/openmm-utils
)
# Images that must be built with the REPO ROOT as context, not their own subdir:
# quadro14L/Dockerfile and quadro14N/Dockerfile do `COPY cyana-2.1/` and
# `COPY xplor-nih-2.39/`, which live at the root and are shared by every quadro
# version instead of being duplicated (~1.4 GB) per subfolder. Building these with
# context = their own dir fails with "COPY failed: file not found in build context".
declare -A ROOT_CONTEXT=(
  [quadro14l:latest]=quadro14L/Dockerfile
  [quadro14n:latest]=quadro14N/Dockerfile
)
# Extra paths whose change must also rebuild a root-context image — the shared
# minimizers are outside the image's own directory, so `$DIR/ changed` misses them.
SHARED_PATHS='^(cyana-2\.1|xplor-nih-2\.39)/'

cd docker-biotools
git checkout main                       # server may be in detached HEAD
OLD_SHA=$(git rev-parse HEAD)
git pull origin main
git submodule update --init --recursive # nested qgrs-cpp + any other submodules
NEW_SHA=$(git rev-parse HEAD)

if [ "$OLD_SHA" = "$NEW_SHA" ]; then
  echo "  docker-biotools unchanged ($NEW_SHA)"
  CHANGED=""
else
  echo "  docker-biotools $OLD_SHA -> $NEW_SHA"
  CHANGED=$(git diff --name-only "$OLD_SHA" "$NEW_SHA")
fi

for IMG in "${!IMAGES[@]}"; do
  DIR="${IMAGES[$IMG]}"
  if [ ! -d "$DIR" ]; then
    echo "  [$IMG] $DIR/ not on this branch - skip"
    continue
  fi

  # For clone-at-build images, look up the current upstream HEAD once.
  REPO="${CLONE_REPO[$IMG]:-}"
  UPSTREAM_SHA=""
  if [ -n "$REPO" ]; then
    UPSTREAM_SHA=$(git ls-remote "$REPO" HEAD | awk '{print $1}')
  fi

  DOCKERFILE="${ROOT_CONTEXT[$IMG]:-}"

  REASON=""
  if ! docker image inspect "$IMG" >/dev/null 2>&1; then
    REASON="image missing"
  elif echo "$CHANGED" | grep -q "^$DIR/"; then
    REASON="$DIR/ changed"
  elif [ -n "$DOCKERFILE" ] && echo "$CHANGED" | grep -qE "$SHARED_PATHS"; then
    REASON="shared cyana/xplor changed"
  elif [ -n "$REPO" ] && [ -n "$UPSTREAM_SHA" ]; then
    BUILT_SHA=$(docker image inspect --format '{{ index .Config.Labels "upstream_sha" }}' "$IMG" 2>/dev/null || true)
    if [ "$UPSTREAM_SHA" != "$BUILT_SHA" ]; then
      REASON="upstream moved (${BUILT_SHA:0:12} -> ${UPSTREAM_SHA:0:12})"
    fi
  fi

  if [ -z "$REASON" ]; then
    echo "  [$IMG] up to date"
    continue
  fi

  echo "  [$IMG] $REASON -> build"
  if [ -n "$DOCKERFILE" ]; then
    docker build -f "$DOCKERFILE" -t "$IMG" .
  elif [ -n "$REPO" ]; then
    # bust the cache and stamp the built upstream sha for next-deploy comparison
    docker build --no-cache --label "upstream_sha=$UPSTREAM_SHA" -t "$IMG" "$DIR/"
  else
    docker build -t "$IMG" "$DIR/"
  fi
done
cd /home/G4Composer

echo "[3/6] Frontend build"
cd g4composer.client
npm ci --silent
npm run build

echo "[4/6] Database migrations"
cd ../G4Composer.Server
rm -rf obj/ bin/
dotnet restore
export PATH="$PATH:$HOME/.dotnet/tools"
export ASPNETCORE_ENVIRONMENT=Production
dotnet ef database update --connection "Host=127.0.0.1;Port=5433;Database=g4composer;Username=g4composer;Password=g4pass" || true

echo "[5/6] Backend publish"
# NOTE: appsettings.Production.json lives only on the server and `dotnet publish` never
# overwrites it, because it is not part of the project. It therefore silently outranks
# everything in appsettings.json — on 2026-08-25 it pinned Quadro:Version to 14L for a week
# after the repo had moved to 14N. Keep it down to server-only settings (connection string,
# log levels); anything about the engine belongs in appsettings.json.
dotnet publish -c Release -o /var/www/g4composer

echo "[6/6] Restart service"
sudo systemctl restart g4composer
sleep 3
curl -s http://localhost:5238/api/quadro11/health
echo ""
# `"status":"degraded"` or a non-null `configProblem` means the engine config does not match
# the image that is actually installed — see QuadroReadinessCheck for the exact reason.
echo "=== Deploy complete ==="
