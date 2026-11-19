# 3D-NuS-Qplex models — provenance

One model per target of Table 1, generated with the **3D-NuS-Qplex** server
using its default settings. The files are included unchanged, exactly as
downloaded; `manifest.csv` pins each one by SHA256.

| property | value |
|---|---|
| server | 3D-NuS-Qplex web server, default settings |
| generated | 2026-10-30 to 2026-11-04 |
| models | 15 (one per Table 1 target) |
| chain ID | none (all atoms in a single blank chain) |
| residue names | `ADE`, `GUA`, `THY`, `CYT` |
| hydrogens | retained in the distributed files |

The models carry hydrogens because that is how the server writes them. The
scoring step (`pipeline/score_qplex.py`) removes hydrogens from both the model
and the experimental reference, so the reported values are all-heavy-atom RMSD
under the same protocol used for G4Composer. The files themselves are not
modified.

`6r9k_qplex_mh24.pdb` and `6r9l_qplex_mh24.pdb` are byte-identical. PDB entries
6R9K and 6R9L have the same sequence and topology, so the server produced the
same model for both; the two targets are nevertheless scored against their own
experimental structures.

The 3D-NuS-Qplex files use `O1P`/`O2P` for the phosphate oxygens; the scorer renames
the reference's `OP1`/`OP2` to match, so no phosphate atom is silently dropped
from the common heavy-atom set.
