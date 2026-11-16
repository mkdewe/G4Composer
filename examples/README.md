# Examples

| File | What it demonstrates |
|---|---|
| `6a-1hap_js12B.inp` | Unimolecular antiparallel DNA G-quadruplex, two tetrads, labelled `structure` notation, `chi` fixing *syn* residues. |
| `7ys7.inp` | Three-tetrad DNA G-quadruplex, caret `^` notation, and the widest spread of fields of any example here: mixed tetrad polarity (`A-;B+;C-`), a negative `rise` alongside a positive one, a negative `twist`, both `chi` and `sugar` set per residue, and a `path` that does not fill its columns in sequence order. |
| `7ys7-rm0.inp` | Same as `7ys7.inp` with `rm_level 0`, so all CYANA/Xplor intermediates are kept. Use this one when diagnosing a failure. |
| `6pnk.inp` | Three-tetrad parallel DNA G-quadruplex with per-step `rise` and `twist` and an explicit `sugar` line. This is the input shown in the animations in [`../docs/media/`](../docs/media/). |

Run one:

```bash
tools/run.sh --outdir out examples/7ys7.inp
```

Each of these writes one structure, `<name>.pdb`. Engine 16 is single-pass; run
the mirrored input as a second job to sample the opposite-handed stack.

The `.inp` format is documented in [`../docs/INPUT-FORMAT.md`](../docs/INPUT-FORMAT.md).

## `reference/`

Reference outputs for the inputs above, produced with the CYANA and Xplor-NIH
builds recorded in `reference/PROVENANCE.md`. They let a reader check a local
installation without having to judge a structure by eye, and let a reviewer
verify the published results without owning a CYANA licence.

Because CYANA and Xplor-NIH are supplied by each user rather than pinned by this
repository, small numerical differences across builds are expected. Compare
`Etotal` and heavy-atom RMSD rather than diffing the PDB files byte for byte.
