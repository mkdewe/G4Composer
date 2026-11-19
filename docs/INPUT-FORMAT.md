# The `.inp` input file

One plain-text file describes one G-quadruplex to build. Each line is
`keyword value` — whitespace-separated, one keyword per line, order irrelevant.
Blank lines and `#`-prefixed comments are allowed.

Engine 16 accepts two spellings of the pucker field: `sugar` is canonical, and
`shugar` is kept as an alias for legacy inputs.

The value **must not contain spaces**: the parser reads `$2`, so `path A1; B1`
silently loses everything after the first space. Use `A1;B1`.

A minimal, complete example — a parallel unimolecular DNA G-quadruplex
(PDB 1HAP-like), `examples/6a-1hap_js12B.inp`:

```
name        1hap_js12B_100
sequence    ggttggtgtggttgg
structure   AB..BA...AB..BA
chi         S...S....S...S.
orient      A+;B-
rise        3.4
twist       19
path        A1;B1;B4;A4;A3;B3;B2;A2
test        y
rm_level    5
iteration   100
```

---

## Keywords

### `name` — output base name
Every result file is named after it: `<name>.pdb`, `<name>_energy.txt`.
Defaults to `quadro7_test` if omitted. Keep it filesystem-safe.

### `sequence` — the nucleotide sequence *(required)*
**Case encodes the sugar, not the base.**

| Characters | Meaning |
|---|---|
| `A C G T U` (uppercase) | ribonucleotides — RNA |
| `a c g t u` (lowercase) | deoxyribonucleotides — DNA |

Case selects the sugar and the letter selects the base, independently, so all
ten combinations are meaningful: uppercase `T` is ribothymidine and lowercase
`u` is deoxyuridine. Both were rejected by 14L and were added in 14M together
with the `RT` and `DU` entries in `other_residues.lib`.

Mixed case is allowed and gives a chimeric RNA/DNA molecule; the engine reports
the RNA fraction. Anything else is rejected with `ERROR 2`.

### `structure` — which residues form tetrads *(required)*
Same length as `sequence`, or `ERROR 4`. Two notations are accepted, chosen
automatically by whether a `^` appears anywhere in the field:

**(a) Labelled notation** — no `^` present. Uppercase letters `A`, `B`, `C`, …
mark residues that belong to a tetrad; the letter names the **column** (the
strand-like stack) the residue sits in. Everything else is a non-tetrad residue:

- `.` — unpaired (loop, overhang)
- `(` `)` `[` `]` `{` `}` `<` `>` — canonical base pairs, in the usual
  dot-bracket sense; used for duplex stems flanking or joining the quadruplex.

**(b) Caret notation** — at least one `^` present. Every `^` marks a tetrad
residue and the column assignment is taken entirely from `path`. Convenient for
large structures where letter bookkeeping is error-prone. `examples/7ys7.inp`
uses it for a three-tetrad quadruplex whose columns are not filled in sequence
order:

```
sequence    ggggcggggcggggcggggt
structure   ^^....^^^.^^^..^^^^.
path        A1;B1;B3;A3;C3;B2;A2;C2;C1;B4;A4;C4
```

The two notations mix freely with dot-bracket pairs, so a quadruplex embedded in
a duplex stem is written like this — carets for the tetrads, brackets for the
paired stem:

```
structure   (((((((((((^^.^^.((...))^^.^^.)))))))))))
```

Constraints, in the order the engine checks them:
- the number of tetrad-marked residues must be a multiple of 4 (`ERROR 104`)
- it must equal the number of entries in `path` (`ERROR 105`)
- every tetrad residue must be `G`, `g`, `U` or `u` (`ERROR 5`)
- brackets must be balanced and pair with a valid partner (`ERROR 106`)

### `path` — the build-up order *(required)*
Semicolon-separated list of `<column><position>` tokens, exactly
`4 × number_of_tetrads` of them (`ERROR 14`).

Each token names one tetrad residue: the letter is its column (`A`, `B`, …), the
digit its index within that column counted along the sequence. Consecutive
groups of four form one tetrad, and each such group must resolve to `GGGG`,
`UUUU` or `TTTT` (`ERROR 15`).

**`path` order is the build-up order**, and it is the single most consequential
field in the file: the engine adds residues to the growing molecule in exactly
this sequence, running a CYANA minimisation after each one. A different `path`
over the same topology is a different calculation with a different result. See
[ALGORITHM.md](ALGORITHM.md).

### `orient` (polarity) — hydrogen-bond directionality per tetrad *(required)*
Semicolon-separated, **one entry per tetrad**, in tetrad order (`ERROR 11`).
Each entry is a column letter followed by `+` or `-`:

- `+` → Watson–Crick/Hoogsteen (`WH`) hydrogen-bond directionality
- `-` → Hoogsteen/Watson–Crick (`HW`)

This is the field the G4Composer interface, and the animations in
[`media/`](media/), call **polarity**; `G4plus` and `G4minus` there are `+` and
`-` here. It selects which of the two `…P` / `…M` tetrad library entries is used
for that tetrad, so it fixes the direction the four Hoogsteen bonds run around
the tetrad — clockwise or anticlockwise seen from the same side.

The letter must match the tetrad's ordinal position (`A` for the first, `B` for
the second, …) or you get `ERROR 12`; the sign must be `+` or `-` (`ERROR 13`).

### `chi` — glycosidic torsion, per residue *(optional)*
Same length as `sequence` (`ERROR 17`). One character per residue, from `.AaSs`
(`ERROR 18`):

| Char | Meaning |
|---|---|
| `S` / `s` | *syn* |
| `A` / `a` | *anti* |
| `.` | let the engine decide |

Uppercase fixes the value; lowercase supplies it as a starting point.

> ⚠ `N` is **not** valid here despite being valid in `sugar`. Passing it yields
> `ERROR 18`.

### `sugar` — sugar pucker, per residue *(optional)*
Same length as `sequence` (`ERROR 19`). One character per residue, from `.NnSs`
(`ERROR 20`):

| Char | Meaning |
|---|---|
| `N` / `n` | North — C3′-endo, the RNA-like pucker |
| `S` / `s` | South — C2′-endo, the DNA-like pucker |
| `.` | default for the residue's sugar type |

Omit the line entirely to accept the defaults.

### `rise` — helical rise, Å *(optional, default 3.4)*
One value, or one per tetrad-to-tetrad step, semicolon-separated. A stack of
*n* tetrads has *n − 1* steps, so `rise 3.4;3.3` describes three tetrads: 3.4 Å
from A to B, then 3.3 Å from B to C. Give fewer values than there are steps and
the remaining steps repeat the **first** value.

**The sign is meaningful, and both signs are normal.** The first tetrad is the
frame of reference: the engine places it at *z* = 0 and positions every later
tetrad at the running sum of the steps before it —

```awk
rise_sum[1] = 0
rise_sum[i] = rise_sum[i-1] + RISE[i-1]      # then: z = z + rise_sum[i]
```

— so a value is a displacement **from the first tetrad, along the stack axis**,
not an unsigned distance between neighbours. Positive moves the next tetrad one
way along *z*, negative the other. A stack can therefore legitimately grow
downwards from its first tetrad, and one with mixed signs folds back on itself:
`examples/7ys7.inp` uses `rise -3.8;6.7`, putting tetrad B 3.8 Å *below* A and
tetrad C 2.9 Å above it.

Flipping the sign of every step reflects the stack through the plane of the
first tetrad, which is half of what the [mirror pass](ALGORITHM.md#the-mirror-pass)
does.

### `twist` — helical twist, degrees *(optional, default 29)*
Same multi-step syntax, same accumulation, same reason for the sign: the first
tetrad is unrotated and each later one is turned about *z* by the running sum of
the preceding steps. Positive and negative are opposite senses of rotation, so
`twist -24.8;54.3` turns tetrad B one way and then takes C well past A in the
other.

Typical magnitudes are ≈30° for parallel and ≈15–20° for antiparallel stacks,
but a structure being modelled against experimental data is under no obligation
to be typical.

### `iteration` — CYANA minimisation depth *(optional, default 300)*
Number of CYANA minimisation steps run **at every build-up stage**. Must be
≥ 10 (`ERROR 25`). The closing CYANA pass is fixed at 100 steps and is not
affected by it.

More is not monotonically better. `iteration` decides how good a starting
structure the Cartesian refinement receives, not how good the final answer is —
2000 hard-wired Xplor-NIH steps follow regardless. See [ALGORITHM.md](ALGORITHM.md).

> `iteration_steps`, which produced several checkpoints from one build-up, was a
> local patch on 14L and is **not** part of 14M or engine 16. To sample several
> depths, run the engine once per `iteration` value and keep the lowest
> `Etotal`.

### `my_angles` — extra torsion-angle restraints *(optional)*
Path to a CYANA angle-restraint file. The name **must** end in `.cya`
(`ERROR 16`).

### `test` — verbose mode *(optional, default `n`)*
`y` keeps the engine chatty and preserves diagnostic output. `n` for production.

### `rm_level` — intermediate-file cleanup *(optional, default 5)*
Controls only how much of the CYANA/Xplor scratch is deleted afterwards. It has
**no effect on the resulting geometry or energy**.

| Level | Effect |
|---|---|
| `0` | keep everything — use this when debugging a failed run |
| `5` | remove all intermediate files |

---

## Output

For a run with `name FOO`:

| File | Contents |
|---|---|
| `FOO.pdb` | the refined 3D structure |
| `FOO_energy.txt` | final Xplor-NIH energy terms, including `Etotal` |
| `FOO.runlog` | full engine output (written by the container entrypoint) |

Engine 16 produces **one** pair per run. To sample the opposite-handed stack,
run the mirrored input as a second job (see
[ALGORITHM.md](ALGORITHM.md#the-mirror-pass)); keep whichever has the lower
`Etotal`.

`Etotal` is the figure to compare when ranking several models of the same
sequence — lower is better.

---

## Error codes

All engine diagnostics are printed as `ERROR <n> : <message>` on standard
output, and a rejected input exits non-zero.

| Code | Meaning |
|---|---|
| 1 | `sequence` missing |
| 2 | invalid residue in `sequence` — see the alphabet above |
| 3 | `structure` missing |
| 4 | `sequence` and `structure` differ in length |
| 5 | a tetrad residue is not `G`/`g`/`U`/`u` |
| 11, 13 | malformed `orient` |
| 12 | `orient` letter does not match the tetrad's position |
| 14 | `path` length ≠ 4 × number of tetrads |
| 15 | a group of four in `path` does not spell `GGGG`/`UUUU`/`TTTT` |
| 16 | `my_angles` does not end in `.cya` |
| 17 | `chi` length ≠ `sequence` length |
| 18 | invalid character in `chi` (allowed: `.AaSs`) |
| 19 | `sugar` length ≠ `sequence` length |
| 20 | invalid character in `sugar` (allowed: `.NnSs`) |
| 25 | `iteration` value below 10 |
| 102 | `sequence` failed the residue-count cross-check |
| 104 | tetrad-residue count is not a multiple of 4, or bad `structure` character |
| 105 | `structure` and `path` disagree |
| 106 | unbalanced or invalid base pair in `structure` |

The container entrypoint judges a run by whether it produced a `.pdb`, not by
the exit status, because the engine's status is otherwise whatever its last
`system()` call returned.
