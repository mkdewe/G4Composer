#!/bin/bash
# quadro container entrypoint.
#
# The engine resolves its data files relative to the working directory
# (DIR="./"), so it must run from $QUADRO_HOME. This script therefore stages the
# user's .inp into $QUADRO_HOME, runs it there, and copies the results back to
# the mounted /work directory — the user never has to know about that detail.

set -uo pipefail

QUADRO_HOME="${QUADRO_HOME:-/opt/bin}"
ENGINE="${QUADRO_EXE:-quadro16.awk}"
WORK="/work"
OUTDIR="$WORK"
ENGINE_OPTS=()

usage() {
    cat <<EOF
quadro — G-quadruplex 3D structure generator

USAGE
    docker run --rm -v "\$PWD:/work" quadro:latest [OPTIONS] INPUT.inp [INPUT2.inp ...]

    Paths are relative to /work, i.e. to the directory you mounted.

    Engine 16 is single-pass: one input produces one PDB.  To sample the
    opposite-handed stack, run the mirrored input (orient/rise/twist signs
    inverted, path positions 2 and 4 exchanged) as a second input.

OPTIONS
    --no-mirror         Accepted for compatibility. Engine 16 is already
                        single-pass, so this changes nothing.
    --outdir DIR        Write results to DIR (relative to /work). Default: /work.
    --shell             Drop into an interactive shell inside the container.
    --version           Print the engine version and exit.
    -h, --help          Show this message.

OUTPUT
    For an input whose 'name' field is FOO, the engine writes FOO.pdb and
    FOO_energy.txt, plus FOO_alt.pdb and FOO_alt_energy.txt from the mirror
    pass. All are copied to the output directory, together with a .runlog
    holding the full engine output.

EXAMPLE
    docker run --rm -v "\$PWD:/work" quadro:latest examples/7ys7.inp

SEE ALSO
    docs/INPUT-FORMAT.md for the .inp file format.
EOF
}

# ── Argument parsing ─────────────────────────────────────────────────────────
inputs=()
while [ $# -gt 0 ]; do
    case "$1" in
        --no-mirror) ENGINE_OPTS=(--no-mirror); shift ;;
        --outdir)   OUTDIR="$WORK/$2"; shift 2 ;;
        --shell)    exec /bin/bash ;;
        --version)  echo "quadro ${QUADRO_VERSION:-unknown} ($ENGINE)"; exit 0 ;;
        -h|--help)  usage; exit 0 ;;
        -*)         echo "quadro: unknown option '$1'" >&2; usage >&2; exit 64 ;;
        *)          inputs+=("$1"); shift ;;
    esac
done

if [ ${#inputs[@]} -eq 0 ]; then
    usage >&2
    exit 64
fi

if [ ! -f "$QUADRO_HOME/$ENGINE" ]; then
    echo "quadro: engine '$ENGINE' not found in $QUADRO_HOME" >&2
    exit 70
fi

mkdir -p "$OUTDIR" || { echo "quadro: cannot create output directory $OUTDIR" >&2; exit 73; }

# ── Run ──────────────────────────────────────────────────────────────────────
failures=0

for input in "${inputs[@]}"; do
    src="$WORK/$input"
    if [ ! -f "$src" ]; then
        echo "quadro: input not found: $input (looked in $src)" >&2
        failures=$((failures + 1))
        continue
    fi

    base="$(basename "$input")"
    stem="${base%.*}"
    log="$OUTDIR/$stem.runlog"

    echo "── $base ──────────────────────────────────────────────"

    # Each input gets its own scratch directory rather than running in
    # $QUADRO_HOME. The engine resolves its data files relative to the working
    # directory (DIR="./"), so those files are copied in alongside the input —
    # about 400 kB, negligible next to the calculation itself.
    #
    # Running in $QUADRO_HOME would need it writable, which forces the container
    # to run as root and leaves root-owned results on the host. It would also
    # make two inputs share one directory, so their intermediate files and
    # outputs would overwrite each other. CYANA and Xplor-NIH are found through
    # PATH and do not need to be in the working directory.
    run_dir="$(mktemp -d "${TMPDIR:-/tmp}/quadro.XXXXXXXX")" || {
        echo "quadro: cannot create a scratch directory" >&2
        failures=$((failures + 1))
        continue
    }
    find "$QUADRO_HOME" -maxdepth 1 -type f -exec cp {} "$run_dir/" \;

    # Strip CR on the way in. An .inp authored on Windows carries CRLF, and the
    # engine's awk parser takes the trailing \r as part of the field value — a
    # 15-residue sequence then reports "ERROR 2 : invalid residue at 16", naming
    # a position that does not exist and a character that prints as nothing.
    tr -d '\r' < "$src" > "$run_dir/$base"

    # The exit status is not the success criterion. The engine's own status is
    # whatever its last system() call returned, which says nothing about whether
    # a structure was built. Judge the run by whether it produced a PDB.
    case "$ENGINE" in
        *.awk) ( cd "$run_dir" && sh "./$ENGINE" "$base" ) > "$log" 2>&1 ;;
        *)     ( cd "$run_dir" && "./$ENGINE" "${ENGINE_OPTS[@]}" "$base" ) > "$log" 2>&1 ;;
    esac
    engine_status=$?

    # Collect by the input's `name` field, not by extension. The working
    # directory also holds build-up snapshots (temperary*.pdb, ~15 of them under
    # `test y`) and the raw Xplor output (<name>_xplor.pdb) that xplor2pdb2.exe
    # renumbers into the real result. Copying every *.pdb reported a single run
    # as 16 structures.
    #
    # The run writes <name>.pdb, and <name>_alt.pdb for the mirror pass. Those
    # are the results; everything else in the directory is not.
    name="$(awk '$1 == "name" { n = $2 } END { print (n == "" ? "quadro7_test" : n) }' "$run_dir/$base")"

    produced=0
    for f in "$run_dir/$name".pdb "$run_dir/$name"_*.pdb; do
        [ -e "$f" ] || continue
        case "${f##*/}" in *_xplor.pdb) continue ;; esac
        cp "$f" "$OUTDIR/"
        produced=$((produced + 1))
    done
    for f in "$run_dir/$name"*_energy.txt; do
        [ -e "$f" ] || continue
        cp "$f" "$OUTDIR/"
    done

    # With rm_level 0 the engine keeps its intermediates on purpose, so hand the
    # whole scratch directory over instead of deleting it.
    if grep -qE '^[[:space:]]*rm_level[[:space:]]+0[[:space:]]*$' "$src"; then
        cp -r "$run_dir" "$OUTDIR/$stem.work"
        echo "   rm_level 0 — intermediates kept in ${stem}.work/"
    fi
    rm -rf "$run_dir"

    if [ "$produced" -eq 0 ]; then
        echo "FAILED: no PDB produced (engine exit $engine_status)" >&2
        echo "        engine output follows:" >&2
        sed 's/^/        /' "$log" >&2
        failures=$((failures + 1))
    else
        echo "OK: $produced structure(s) written to ${OUTDIR#$WORK/}"
        grep -E '^ERROR|^WARNING' "$log" >&2 || true
    fi
done

[ "$failures" -eq 0 ] || exit 1
exit 0
