# 3D-NuS-Qplex models — provenance

One model per target of Table 1, generated with the **3D-NuS-Qplex** server
using its default settings. The files are included unchanged, exactly as
downloaded; `manifest.csv` pins each one by SHA256.

| property | value |
|---|---|
| server | 3D-NuS-Qplex web server, default settings |
| generated | 2026-10-30 to 2026-11-19 |
| models | 15 (one per Table 1 target) |
| chain ID | none (all atoms in a single blank chain) |
| residue names | `ADE`, `GUA`, `THY`, `CYT` |
| hydrogens | retained in the distributed files |

The 2KF8 and 5LQH models were regenerated on 2026-11-19 with their complete
22-nt sequences.

The models carry hydrogens because that is how the server writes them. The
scoring step (`pipeline/score_qplex.py`) removes hydrogens from both the model
and the experimental reference, maps residues with PyMOL `align`, and then
superposes and scores every heavy atom present in both mapped residues,
including phosphate oxygens. The files themselves are not modified.

`6r9k_qplex_mh24.pdb` and `6r9l_qplex_mh24.pdb` are byte-identical. PDB entries
6R9K and 6R9L have the same sequence and topology, so the server produced the
same model for both; the two targets are nevertheless scored against their own
experimental structures.

The 3D-NuS-Qplex files use `O1P`/`O2P` for the phosphate oxygens; the scorer
normalises both structures to those names, so phosphate atoms are not dropped
from the common heavy-atom set.

## 9UK8 sequence variants

PDB entry 9UK8 contains two observed G-quadruplex sequences. Assemblies 1-A and
2-B carry the 23-nt sequence `GGGGCCGGGGCCGGGGCCGGGGC`; assemblies 3-C and 4-D
carry the 24-nt sequence `GGGGCCGGGGCCGGGGCCGGGGCC`. The two 23-nt copies are
essentially identical (0.39~\AA{} all-heavy-atom RMSD between them).

The 3D-NuS-Qplex model for 9UK8 is 23 nt, so Table 1 compares it with the
23-nt assembly1-A chain. Assembly2-B is an equivalent second copy and remains
available in the full G4Composer dataset; assemblies 3-C and 4-D remain in the
full dataset as the 24-nt variants. This pinning keeps the Qplex and G4Composer
comparisons on the same observed sequence and reference structure.
