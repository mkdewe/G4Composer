#!/bin/bash
# Wybiera reprezentantów spośród przykładów w bazie i oznacza ich flagą IsCurated.
#
# Zasada wyboru:
#   * kubełek = (podtyp Silva × liczba tetrad); z każdego wygrywa struktura o NAJNIŻSZEJ
#     energii, reszta jest odrzucana. Podtyp mający i 2T, i 3T, i 4T daje trzech
#     reprezentantów — stąd "jedna lub dwie (lub trzy) na topologię".
#   * struktury non-canonical (SilvaSubtypeId IS NULL) są oznaczane WSZYSTKIE. Nie mają
#     topologii Silva, więc nie ma czego reprezentować; każda jest osobnym przypadkiem.
#
# Energia = najniższe Etotal spośród wszystkich klatek danego przykładu, obu wariantów
# (standard i alternatywa) — czyli dokładnie ten model, który aplikacja pokazuje jako
# najlepszy. Bierze się z cache'a, więc krok 1 musi policzyć wszystko, zanim krok 2 wybierze.
#
# Uruchamianie na serwerze:
#     cd /home/G4Composer && ./tools/curate-examples.sh
#
# Skrypt jest idempotentny: struktury policzone wcześniej (mające w cache'u komplet klatek
# obu wariantów) są pomijane, więc przerwany przebieg można wznowić.
#
#   --dry-run   policz brakujące, pokaż wybór, ale NIE zapisuj flagi
#   --select    pomiń liczenie, tylko przelicz wybór z tego, co już jest w cache'u

set -euo pipefail

API="${API:-http://localhost:5238}"
PGCONN="${PGCONN:-host=127.0.0.1 port=5433 dbname=g4composer user=g4composer password=g4pass}"
# Musi być >= Quadro:TimeoutSeconds, inaczej curl rozłączy się w trakcie liczenia i wynik
# przepadnie mimo że kontener dobiegł końca.
HTTP_TIMEOUT="${HTTP_TIMEOUT:-1200}"

DRY_RUN=0
SKIP_COMPUTE=0
for arg in "$@"; do
  case "$arg" in
    --dry-run) DRY_RUN=1 ;;
    --select)  SKIP_COMPUTE=1 ;;
    *) echo "Nieznany argument: $arg" >&2; exit 2 ;;
  esac
done

psqlq() { psql "$PGCONN" -v ON_ERROR_STOP=1 -tAq -c "$1"; }

# ── 0. Sanity: czy silnik w ogóle policzy alternatywę ────────────────────────
health=$(curl -sf --max-time 20 "$API/api/quadro11/health") || {
  echo "BŁĄD: $API/api/quadro11/health nie odpowiada. Czy serwis działa?" >&2; exit 1; }
echo "health: $health"
case "$health" in
  *'"status":"ready"'*) ;;
  *) echo "BŁĄD: silnik nie jest 'ready' — napraw konfigurację, zanim policzysz 70 struktur." >&2
     exit 1 ;;
esac

# ── 1. Policz brakujące ──────────────────────────────────────────────────────
# "Brakujące" = przykład bez wpisu w cache'u albo z wpisem bez klatek alternatywy.
# Ten drugi przypadek to ślad po awarii alternatywy — taki wpis trzeba przeliczyć.
MISSING_SQL='
SELECT e."PdbId"
FROM "StructureExamples" e
WHERE NOT EXISTS (
  SELECT 1 FROM "PdbCacheEntries" c
  JOIN "PdbCacheFrames" f ON f."PdbCacheEntryId" = c."Id" AND f."Variant" = '"'"'alt'"'"'
  WHERE c."StructureExampleId" = e."Id")
ORDER BY e."PdbId";'

if [ "$SKIP_COMPUTE" -eq 0 ]; then
  mapfile -t MISSING < <(psqlq "$MISSING_SQL")
  total=${#MISSING[@]}
  echo "=== [1/2] Liczenie: $total struktur do policzenia ==="

  i=0
  failed=()
  for pdbid in "${MISSING[@]}"; do
    [ -z "$pdbid" ] && continue
    i=$((i+1))

    # Wejście budujemy z bazy, żeby było identyczne z tym, co wysyła UI — inaczej hash
    # cache'a się nie zgodzi i policzone modele nie zostaną nigdy trafione.
    # iterationSteps celowo pominięte: backend użyje domyślnych [30,50,70,100,150,300],
    # czyli tych samych, które wysyła klient.
    payload=$(psqlq "
      SELECT json_build_object(
        'name',      e.\"InpName\",
        'sequence',  e.\"Sequence\",
        'structure', e.\"Structure\",
        'chi',       e.\"Chi\",
        'orient',    e.\"Orient\",
        'rise',      e.\"Rise\",
        'twist',     e.\"Twist\",
        'path',      string_to_array(e.\"Path\", ';'),
        'rmLevel',   e.\"RmLevel\")::text
      FROM \"StructureExamples\" e WHERE e.\"PdbId\" = '${pdbid//\'/\'\'}';")

    if [ -z "$payload" ]; then
      echo "  [$i/$total] $pdbid  POMINIĘTY (brak wiersza w bazie)"
      failed+=("$pdbid")
      continue
    fi

    hdr=$(mktemp)
    if curl -sf --max-time "$HTTP_TIMEOUT" -o /dev/null -D "$hdr" \
         -X POST "$API/api/quadro11/run" \
         -H 'Content-Type: application/json' \
         -d "[$payload]"
    then
      has_alt=$(grep -i '^x-has-alt:' "$hdr" | tr -d '\r' | awk '{print $2}')
      std=$(grep -i '^x-std-energy:' "$hdr" | tr -d '\r' | cut -d' ' -f2-)
      alt=$(grep -i '^x-alt-energy:' "$hdr" | tr -d '\r' | cut -d' ' -f2-)
      win=$(grep -i '^x-winner:'     "$hdr" | tr -d '\r' | cut -d' ' -f2-)
      printf '  [%d/%d] %-8s std=%-12s alt=%-12s wygrywa=%-12s hasAlt=%s\n' \
             "$i" "$total" "$pdbid" "${std:-—}" "${alt:-—}" "${win:-—}" "${has_alt:-0}"
      [ "${has_alt:-0}" = "1" ] || failed+=("$pdbid (brak alternatywy)")
    else
      echo "  [$i/$total] $pdbid  BŁĄD (run nie powiódł się)"
      failed+=("$pdbid")
    fi
    rm -f "$hdr"
  done

  if [ ${#failed[@]} -gt 0 ]; then
    echo
    echo "UWAGA: ${#failed[@]} struktur nie policzyło się w pełni:"
    printf '  - %s\n' "${failed[@]}"
    echo "Wybór poniżej pominie je (nie mają energii), więc ich kubełki mogą dostać"
    echo "gorszego reprezentanta. Napraw je i uruchom ponownie."
  fi

  # Sukces HTTP nie znaczy, że wynik wylądował w bazie: QuadroController zapisuje do
  # cache'a przez TrySaveToCacheAsync, który loguje i POŁYKA błędy DB, żeby nie zabijać
  # udanego obliczenia. 2026-08-25 kosztowało to 66 policzonych na darmo struktur —
  # zapis wywalał się na kolumnie IsCurated (integer zamiast boolean), a skrypt widział
  # same nagłówki i raportował same sukcesy. Dlatego sprawdzamy stan po fakcie.
  mapfile -t STILL_MISSING < <(psqlq "$MISSING_SQL")
  if [ "${#STILL_MISSING[@]}" -gt 0 ] && [ -n "${STILL_MISSING[0]}" ]; then
    echo
    echo "BŁĄD: ${#STILL_MISSING[@]} struktur policzyło się, ale NIE MA ich w bazie."
    echo "To znaczy, że zapis do cache'a padł po cichu. Sprawdź:"
    echo "    journalctl -u g4composer --no-pager | grep -i 'failed to save result to PDB cache'"
    echo "Wybór poniżej byłby liczony na niepełnych danych — przerywam."
    exit 1
  fi
fi

# ── 2. Wybór ─────────────────────────────────────────────────────────────────
echo
echo "=== [2/2] Wybór reprezentantów ==="

# Przykłady bez policzonej energii — nie da się ich uszeregować.
NOENERGY=$(psqlq '
SELECT COUNT(*) FROM "StructureExamples" e
WHERE e."SilvaSubtypeId" IS NOT NULL AND NOT EXISTS (
  SELECT 1 FROM "PdbCacheEntries" c
  JOIN "PdbCacheFrames" f ON f."PdbCacheEntryId" = c."Id"
  WHERE c."StructureExampleId" = e."Id" AND f."Etotal" IS NOT NULL);')
if [ "$NOENERGY" -gt 0 ]; then
  echo "UWAGA: $NOENERGY kanonicznych przykładów nie ma policzonej energii i wypadnie z rankingu."
fi

SELECT_SQL='
WITH best AS (
    SELECT c."StructureExampleId" AS example_id, MIN(f."Etotal") AS etotal
    FROM "PdbCacheEntries" c
    JOIN "PdbCacheFrames" f ON f."PdbCacheEntryId" = c."Id"
    WHERE c."StructureExampleId" IS NOT NULL AND f."Etotal" IS NOT NULL
    GROUP BY c."StructureExampleId"
),
ranked AS (
    SELECT e."Id", e."PdbId", s."Code" AS subtype, e."Tetrads", b.etotal,
           ROW_NUMBER() OVER (
               PARTITION BY e."SilvaSubtypeId", e."Tetrads"
               -- Remis rozstrzygany po PdbId, żeby kolejne przebiegi dawały ten sam wynik.
               ORDER BY b.etotal ASC, e."PdbId" ASC) AS rn
    FROM "StructureExamples" e
    JOIN "SilvaSubtypes" s ON s."Id" = e."SilvaSubtypeId"
    JOIN best b ON b.example_id = e."Id"
)
SELECT "PdbId", subtype, "Tetrads", ROUND(etotal::numeric, 1), rn
FROM ranked ORDER BY subtype, "Tetrads", rn;'

echo
printf '%-10s %-8s %-3s %12s  %s\n' "PdbId" "podtyp" "T" "Etotal" "wynik"
psqlq "$SELECT_SQL" | while IFS='|' read -r pdbid subtype tetrads etotal rn; do
  [ -z "$pdbid" ] && continue
  if [ "$rn" = "1" ]; then verdict="WYBRANY"; else verdict="odrzucony"; fi
  printf '%-10s %-8s %-3s %12s  %s\n' "$pdbid" "$subtype" "${tetrads}T" "$etotal" "$verdict"
done

if [ "$DRY_RUN" -eq 1 ]; then
  echo
  echo "--dry-run: baza nietknięta."
  exit 0
fi

# Jedna transakcja: kasujemy poprzedni wybór i nadajemy nowy, żeby nie zostać z hybrydą
# starego i nowego rankingu, gdyby coś padło w połowie.
psql "$PGCONN" -v ON_ERROR_STOP=1 -q <<'SQL'
BEGIN;

UPDATE "StructureExamples" SET "IsCurated" = false;

WITH best AS (
    SELECT c."StructureExampleId" AS example_id, MIN(f."Etotal") AS etotal
    FROM "PdbCacheEntries" c
    JOIN "PdbCacheFrames" f ON f."PdbCacheEntryId" = c."Id"
    WHERE c."StructureExampleId" IS NOT NULL AND f."Etotal" IS NOT NULL
    GROUP BY c."StructureExampleId"
),
ranked AS (
    SELECT e."Id",
           ROW_NUMBER() OVER (
               PARTITION BY e."SilvaSubtypeId", e."Tetrads"
               ORDER BY b.etotal ASC, e."PdbId" ASC) AS rn
    FROM "StructureExamples" e
    JOIN best b ON b.example_id = e."Id"
    WHERE e."SilvaSubtypeId" IS NOT NULL
)
UPDATE "StructureExamples" e
SET "IsCurated" = true
FROM ranked r
WHERE r."Id" = e."Id" AND r.rn = 1;

-- Non-canonical: wszystkie, bez rankingu.
UPDATE "StructureExamples" SET "IsCurated" = true WHERE "SilvaSubtypeId" IS NULL;

COMMIT;
SQL

echo
psqlq '
SELECT
  (SELECT COUNT(*) FROM "StructureExamples" WHERE "IsCurated") || '"'"' wybranych z '"'"' ||
  (SELECT COUNT(*) FROM "StructureExamples") || '"'"' (w tym '"'"' ||
  (SELECT COUNT(*) FROM "StructureExamples" WHERE "IsCurated" AND "SilvaSubtypeId" IS NULL) ||
  '"'"' non-canonical)'"'"';'
echo "=== Gotowe ==="
