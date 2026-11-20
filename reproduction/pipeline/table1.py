#!/usr/bin/env python3
"""The fifteen Table 1 targets and their 3D-NuS-Qplex models.

One record per target. ``key`` is the input stem (the 12-column tables use the
same identifier with dashes replaced by underscores); ``assembly`` and
``chain`` name the experimental reference, and ``qplex`` is the bundled model
in ``qplex/``.
"""

from __future__ import annotations

TARGETS = [
    dict(pdb="1KF1", key="1kf1_assembly1_A", assembly=1, chain="A", qplex="1kf1_qplex_mp1.pdb"),
    dict(pdb="5LQH", key="5lqh_assembly1_A", assembly=1, chain="A", qplex="5lqh_qplex_ma5.pdb"),
    dict(pdb="2MBJ", key="2mbj_assembly1_A", assembly=1, chain="A", qplex="2mbj_qplex_ma6.pdb"),
    dict(pdb="2KF8", key="2kf8_assembly1_A", assembly=1, chain="A", qplex="2kf8_qplex_ma8.pdb"),
    dict(pdb="2MFT", key="2mft_assembly1_A", assembly=1, chain="A", qplex="2mft_qplex_ma9.pdb"),
    dict(pdb="1HUT", key="1hut_assembly1_D", assembly=1, chain="D", qplex="1hut_qplex_ma13.pdb"),
    dict(pdb="5OPH", key="5oph_assembly1_A", assembly=1, chain="A", qplex="5oph_qplex_ma13.pdb"),
    dict(pdb="1HAO", key="1hao_assembly1_D", assembly=1, chain="D", qplex="1hao_qplex_ma18.pdb"),
    # 9UK8 has 23-nt (assemblies 1-A/2-B) and 24-nt (3-C/4-D) observed variants.
    # The Qplex model is 23 nt, so Table 1 uses the matching assembly1-A chain.
    dict(pdb="9UK8", key="9uk8_assembly1_A", assembly=1, chain="A", qplex="9uk8_qplex_ma18.pdb"),
    dict(pdb="2HY9", key="2hy9_assembly1_1", assembly=1, chain="1", qplex="2hy9_qplex_mh7.pdb"),
    dict(pdb="2LOD", key="2lod_assembly1_A", assembly=1, chain="A", qplex="2lod_qplex_mh14.pdb"),
    dict(pdb="6L92", key="6l92_assembly1_A", assembly=1, chain="A", qplex="6l92_qplex_mh17.pdb"),
    dict(pdb="2MFU", key="2mfu_assembly1_A", assembly=1, chain="A", qplex="2mfu_qplex_mh1.pdb"),
    dict(pdb="6R9K", key="6r9k_assembly1_A", assembly=1, chain="A", qplex="6r9k_qplex_mh24.pdb"),
    dict(pdb="6R9L", key="6r9l_assembly1_A", assembly=1, chain="A", qplex="6r9l_qplex_mh24.pdb"),
]

BY_PDB = {target["pdb"]: target for target in TARGETS}


def keys() -> list[str]:
    return [target["key"] for target in TARGETS]


if __name__ == "__main__":
    print(",".join(keys()))
