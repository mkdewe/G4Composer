# Reference outputs — provenance

Reference results for the inputs in `examples/`, with a record of the
environment that produced them. Use them to check that a local installation
behaves sanely before trusting it on real work.

Because CYANA and Xplor-NIH are supplied by each user rather than pinned by this
repository, results can differ slightly between installations. **Compare
`Etotal` and heavy-atom RMSD, not the files byte for byte.** The archived PDBs
have the run-specific `REMARK FILENAME`/`REMARK DATE` lines stripped, and the
energy files are reduced to the final `Etotal` summary, so a fresh run will not
be textually identical even when the coordinates agree.

Regenerate with:

```bash
tools/build.sh
tools/run.sh --outdir examples/reference \
    examples/6a-1hap_js12B.inp examples/7ys7.inp examples/example.inp
```

## Environment

| Component | Version / build |
|---|---|
| engine | G4Composer 1.0 (`g4composer.awk`, `tetrad.lib`) |
| CYANA | 2.1 |
| Xplor-NIH | 2.39, Linux x86-64 |
| Base image | `ubuntu:22.04@sha256:2edbbc5dc405e9612ba3584ce95480277e3eb374407b5505fe26f17df77c7dbc` |
| CPU | Intel Core i7-12700K (x86-64) |
| Date | 2026-11-16 |

## Results

G4Composer 1.0 is single-pass: each input yields one structure. Run the mirrored
input as a second job to sample the opposite-handed stack.

| Input | Output | Atoms | Residues | `Etotal` |
|---|---|---|---|---|
| `6a-1hap_js12B.inp` | `1hap_js12B_100.pdb` | 488 | 15 | −614.388 |
| `7ys7.inp` | `7ys7.pdb` | 649 | 20 | −857.538 |
| `example.inp` | `example.pdb` | 555 | 17 | −670.026 |

All three runs completed with no `ERROR` lines in the engine output.

G4Composer 1.0 now reads the documented `sugar` keyword (`shugar` is kept as an
alias). `7ys7.inp` and `example.inp` carry non-default pucker annotations, so their
`Etotal` values changed when the fix was applied; `6a-1hap_js12B.inp` has no
`sugar` line and is unchanged.

These G4Composer 1.0 runs use the same engine as the archived per-model RMSD,
lDDT and energy values in the G4Composer manuscript.
