# Archived G4Composer models

6,516 PDB files and 6,516 energy summaries: 12 models for each of the 543
modellable inputs.

| property | value |
|---|---|
| engine | G4Composer 1.0 (`g4composer.awk`) with the `sugar` keyword fix |
| CYANA | 2.1 |
| Xplor-NIH | 2.39 |
| depths | 30, 50, 70, 100, 150, 300 |
| modes | `n` (input as written), `a` (mirrored input) |
| naming | `<key>_<depth>_<mode>.pdb` and `_energy.txt` |

The three mixed DNA/RNA inputs (`4ni7-assembly1-B`, `4ni9-assembly1-B`,
`4ni9-assembly1-D`) cannot be built and have no models. The two synthetic
7ZJ4/7ZJ5 constructs are used only for clustering and are not modelled.

The files are sanitized for distribution: PDB files have no run-specific
`REMARK FILENAME`/`REMARK DATE` lines, and each energy file contains only the
final `Etotal` summary. Coordinates and energies are unchanged.

CYANA and Xplor-NIH are **not** included. The coordinates and energies are
research outputs generated under the authors' licences; rebuilding them
requires each user to obtain those programs.

Rebuild this directory from scratch with:

```bash
FORCE=1 bash ../pipeline/run_all.sh
```
