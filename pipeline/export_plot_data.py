"""Export one tidy row per AP representative for the RMSD/energy figures."""
import argparse
import csv
from pathlib import Path

# Constructs excluded from the manuscript dataset; they have no measurements.
MANUAL_EXCLUSIONS = {'7zj4-assembly1-E.inp', '7zj5-assembly1-E.inp'}
DEPTHS = (30, 50, 70, 100, 150, 300)
# Column order of the measurement tables: each depth in standard then alternative mode.
VARIANTS = [f'{d}{mode}' for d in DEPTHS for mode in ('n', 'a')]


def load_measurements(path):
    """Read a 12-value measurement table into {row: {variant: value}}."""
    result = {}
    for line in path.read_text().splitlines()[1:]:
        if not line.strip() or line.lstrip().startswith('#'):
            continue
        cells = line.split('|')[0].split()
        values = [float(x) for x in cells[1:13]]
        if len(values) != len(VARIANTS):
            raise ValueError(f'{path}:{cells[0]}: expected 12 values')
        result[cells[0]] = dict(zip(VARIANTS, values))
    return result


def row_key(representative):
    return representative.removesuffix('.inp').replace('-', '_')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--results', type=Path, required=True)
    parser.add_argument('--measurements', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()

    with (args.results / 'clusters_ap.csv').open(encoding='utf-8-sig') as stream:
        reps = sorted(
            row['representative']
            for row in csv.DictReader(stream)
            if row['representative'] not in MANUAL_EXCLUSIONS
        )
    rmsd = load_measurements(args.measurements / 'G16_rmsd.txt')
    energy = load_measurements(args.measurements / 'G16_energy_popr.txt')

    rows = []
    for rep in reps:
        key = row_key(rep)
        r = rmsd.get(key)
        e = energy.get(key)
        rmsd_min = energy_min = None
        if r:
            r_variant = min(r, key=r.get)
            rmsd_min = r[r_variant]
        else:
            r_variant = ''
        if e:
            e_variant = min(e, key=e.get)
            energy_min = e[e_variant]
        else:
            e_variant = ''
        rows.append(
            dict(
                representative=rep,
                min_rmsd='' if rmsd_min is None else f'{rmsd_min:.3f}',
                min_energy='' if energy_min is None else f'{energy_min:.3f}',
                min_rmsd_variant=r_variant,
                min_energy_variant=e_variant,
            )
        )

    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open('w', newline='', encoding='utf-8') as stream:
        writer = csv.DictWriter(
            stream,
            fieldnames=[
                'representative',
                'min_rmsd',
                'min_energy',
                'min_rmsd_variant',
                'min_energy_variant',
            ],
        )
        writer.writeheader()
        writer.writerows(rows)
    print(f'{len(rows)} representatives -> {args.output}')


if __name__ == '__main__':
    main()
