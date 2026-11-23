# Installing CYANA and Xplor-NIH

G4Composer drives two external programs that are **not included in this repository
and cannot be** — both licences forbid passing the software on to third parties.
Obtain each under your own licence:

| | Licence | Where to get it |
|---|---|---|
| **CYANA 2.1** | **paid** — commercial licence, academic and commercial terms differ | <https://www.las.jp/english/cyana.html> |
| **Xplor-NIH 2.39** | free for non-profit institutions, registration required | <https://nmr.cit.nih.gov/xplor-nih/> |

Free does not mean redistributable: Xplor-NIH is excluded from the repository
for the same reason as CYANA.

Version and build matter. G4Composer is tested against **CYANA 2.1** and the
**Linux x86-64** build of **Xplor-NIH 2.39**. Other versions are untested.

Two ways to get from there to a working setup:

- **[Option A — Docker](#option-a--docker-recommended)**: unpack both archives, run one script. The image
  handles every configuration step below for you. **Recommended.**
- **[Option B — native Linux](#option-b--native-linux)**: you do that configuration yourself.

---

## Option A — Docker (recommended)

Unpack each distribution **exactly as delivered**, directly into its directory —
no installer, no configuration:

```
third_party/
├── cyana-2.1/            ← must contain: cyana, lib/, macro/  (cyana.sh if present)
└── xplor-nih-2.39/       ← must contain: bin.Linux_x86_64/, toppar/, python/
```

Then:

```bash
tools/check-third-party.sh     # fails with a pointer back here if anything is missing
tools/build.sh                 # builds the image
tools/run.sh --outdir out examples/7ys7.inp
```

That is the whole procedure. The image does the rest itself: installs the
32-bit runtime libraries CYANA 2.1's binaries need, generates Xplor's
`arch/equivList`, copies `bin.Linux_x86_64/` into `bin/`, symlinks `cyana.sh`
to `/usr/local/bin/cyana`, sets `CYANA` / `XPLOR` / `TOPPAR` /
`LD_LIBRARY_PATH`, converts the engine's CRLF line endings, and runs the engine
from the directory holding its data libraries.

Requires Docker 23+ or `buildx` — the build passes CYANA and Xplor-NIH as named
build contexts, so they are never copied into the repository.

---

## Option B — native Linux

Supported in the sense that it works, not in the sense that it is tested on
every distribution. Everything Option A does for you, you now do yourself. Steps
below are for Ubuntu 22.04; adapt package names elsewhere.

### 1. System packages

CYANA 2.1 ships **32-bit** binaries, so the i386 architecture must be enabled
even on an x86-64 machine. `libtinfo5` and `libncurses5` are no longer installed
by default on modern releases and are required by that same old build.

```bash
sudo dpkg --add-architecture i386
sudo apt-get update
sudo apt-get install -y \
    gawk dos2unix gfortran libgfortran5 libgomp1 zlib1g libncurses6 \
    libncurses5 libtinfo5 \
    libc6:i386 libstdc++6:i386 libncurses5:i386 zlib1g:i386 libgcc-s1:i386
```

`gawk` specifically — the engine uses GNU awk extensions and will not run under
`mawk`, which is Ubuntu's default `awk`.

### 2. Install CYANA and Xplor-NIH

Follow each vendor's own instructions; unpack them anywhere you like, for
example under `/opt`:

```bash
/opt/cyana-2.1/
/opt/xplor-nih-2.39/
```

For Xplor-NIH, **run its own installer** — `./configure` in the unpacked
directory. It is the supported path and it writes the architecture files
correctly. (The container bypasses it only because it cannot run interactively
during a build; if you ever need to reproduce that manually:
`mkdir -p arch/Linux_x86_64 && echo Linux_x86_64 | tee arch/equivList arch/Linux_x86_64/equivList`
then `cp -r bin.Linux_x86_64/* bin/ && chmod +x bin/*`.)

For CYANA, make sure the launcher is executable:

```bash
chmod +x /opt/cyana-2.1/cyana /opt/cyana-2.1/cyana.sh
```

### 3. Environment

```bash
export CYANA=/opt/cyana-2.1
export XPLOR=/opt/xplor-nih-2.39
export TOPPAR=$XPLOR/toppar
export PATH=$CYANA:$XPLOR/bin:$PATH
export LD_LIBRARY_PATH=$XPLOR/bin:$XPLOR/bin.Linux_x86_64:/usr/lib/x86_64-linux-gnu:/usr/lib32:$LD_LIBRARY_PATH
```

Put these in your shell profile — the engine invokes both programs by name and
will not find them otherwise.

### 4. Prepare the engine directory

The engine resolves its data files **relative to the current directory**
(`DIR="./"` near the top of `engine/g4composer.awk`). So it must be run from a
directory containing all of `engine/`, and the files must have Unix line endings:

```bash
cp -r engine/ ~/g4composer-run
cd ~/g4composer-run
find . -type f -exec dos2unix {} \; 2>/dev/null
chmod +x *.exe tor3aa
```

If your copy of the engine carries an absolute `DIR="..."` path instead, point
it at the working directory once:

```bash
sed -i 's|^DIR="/.*"$|DIR="./"|' g4composer.awk
```

### 5. Run

Copy your `.inp` into that directory and run the engine from there:

```bash
cp /path/to/my.inp .
sh g4composer.awk my.inp
ls *.pdb *_energy.txt
```

That builds one structure. G4Composer 1.0 is single-pass; run the mirrored input
as a second job if the opposite-handed stack is needed.

The engine exits non-zero when it rejects an input, but its status is otherwise
whatever its last `system()` call returned, so judge a run by whether a `.pdb`
appeared. Loader errors mentioning 32-bit libraries mean step 1 was incomplete.
