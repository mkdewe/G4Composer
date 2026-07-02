# G4Composer — raport ze zmian i analiza openmm-utils (2026-07-02)

Raport obejmuje cztery zlecone zmiany. Punkty 1–3 są zaimplementowane i przetestowane
lokalnie (backend .NET + frontend Vitest). Punkt 4 (openmm-utils): obraz **zbudowany i pogłębienie
przetestowany lokalnie** — energie policzone na 10 topologiach jednej sekwencji (jabłka z jabłkami)
oraz kontrola integralności tetrad ElTetrado przed/po minimalizacji. Wnioski z testu **empirycznie
potwierdzają obawę przełożonego** i doprecyzowują, jak realnie użyć tego narzędzia (sekcja „Test
pogłębiony”). Komendy serwerowe niżej.

---

## 1. Nazwa zakładki Source (uwaga testerki)

Testerka słusznie zauważyła, że obie zakładki bazują na sekwencji, więc „Sequence prediction” nie
oddaje istoty. Ta zakładka pokazuje **kanoniczne topologie Webba da Silvy** (kuratowany katalog 26
podtypów o znanych strukturach eksperymentalnych), oceniane dla wprowadzonej sekwencji.

**Zmieniono:** zakładka `Sequence prediction` → **`Canonical topologies (Silva)`**
(`g4composer.client/src/components/HomeSection.jsx`). Dodatkowo etykieta grupy `Prediction (n)` →
`Silva (n)`, a opis tabeli analizy odwołuje się teraz do „canonical-Silva folds”. Zachowany kontrast
z zakładką **`ONQuadro aligner`** (dopasowanie do konkretnej deponowanej struktury PDB).

## 2. Ujednolicenie notacji pętli (uwaga testerki)

Problem: zakładka ONQuadro aligner pokazywała notację ONQuadro/ElTetrado (np. `-L+L-L`), a zakładka
predykcji notację Silvy (np. `-Lw-Ln-Lw`). To ta sama biologia zapisana dwoma alfabetami.

Kluczowa obserwacja: **notacja ONQuadro nie koduje rozróżnienia szeroka/wąska bruzda (Lw/Ln)** — w
łańcuchu topologii ma tylko typ pętli (`p`/`l`/`d`) i znak progresji. Rozróżnienie Lw/Ln wynika z
geometrii. Dlatego zamiast zgadywać, **odtwarzamy podtyp Silvy z geometrii dopasowanego szablonu**.

**Zmieniono:**
- Nowa klasa `G4Composer.Server/Domain/SilvaTopologyMatcher.cs` — odwrotność `SilvaTopology`:
  z pola `path` z pliku `.inp` szablonu (rzeczywiste nawinięcie nici) rekonstruuje kanoniczną
  notację Silvy, porównując z 26 nawinięciami katalogu (`SilvaCatalog`). Zwraca notację **tylko gdy
  dokładnie jeden fold katalogu pasuje** (z dokładnością do przenumerowania nici — to ten sam fizyczny
  fold); w każdym innym przypadku zwraca `null`, a wywołujący zostawia oryginalną notację ONQuadro.
  Nigdy nie wymyśla etykiety Silvy, której nie da się uzasadnić geometrią.
- `PipelineController.ToGeneratedTopology` używa matchera; surowa notacja ElTetrado trafia do
  „rationale”, więc pierwotna adnotacja szablonu nie ginie.
- Testy: `SilvaTopologyMatcherTests` — round-trip każdego jednoznacznego foldu, niezmienniczość
  względem przenumerowania nici, odzyskanie Lw/Ln (6a vs 6b), `null` dla N=1 i wejść zniekształconych.

**Uwaga do walidacji:** matcher jest samo-spójny (testowany na własnych ścieżkach katalogu). Zakłada,
że `path` z alignera używa tej samej konwencji nawinięcia quadro14L, z dokładnością do rotacji nici.
Po zbudowaniu nowego obrazu alignera warto sprawdzić na 2–3 realnych szablonach (np. `5oph`), czy
etykieta Silvy pojawia się zgodnie z oczekiwaniem; jeśli konwencja różni się np. odbiciem, matcher
bezpiecznie zwróci `null` i zostanie notacja ONQuadro (bez błędnej adnotacji).

## 3. Nowa wersja onquadro-aligner

Upstream (`tzok/onquadro-aligner`, commity z 1–2 lipca 2026: „viability as rules table”,
„edit-distance linker scoring”, „fix topology classification”) **zmienił format wyjścia**, co psuło
nasz parser:

| Element | Stara wersja | Nowa wersja |
|---|---|---|
| Kolumna CSV | `Linker score` | `Linker distance` |
| Klucz w `.inp` | `linker_score` | `linker_distance` |
| Semantyka | wynik, **wyżej = lepiej** | dystans edycyjny, **niżej = lepiej** |

**Zmieniono (z kompatybilnością wsteczną — stare nazwy nadal działają):**
- `OnquadroService.cs` — parser CSV i `.inp` akceptuje obie nazwy; pole modelu `LinkerScore` →
  `LinkerDistance` (`PipelineModels.cs`).
- `PipelineController.cs` — wybór najlepszego dopasowania sortuje teraz rosnąco po `LinkerDistance`
  (niżej = lepiej), rationale zapisuje `linker_distance=…`.
- `HomeSection.jsx` — nagłówek tabeli „Linker” → „Linker dist.” z opisem „lower is a better fit”;
  parser rationale czyta `linker_distance` (fallback do `linker_score`).

**Do wykonania na serwerze** (obraz git-clone’uje HEAD — konieczny `--no-cache`, zgodnie z notatką):
```bash
cd /home/G4Composer/docker-biotools && git checkout main && git pull origin main
git submodule update --init
docker build --no-cache -t onquadro-aligner:latest onquadroAligner/
```

## 4. openmm-utils — obraz, narzędzia i analiza

### Co to jest
`tzok/openmm-utils` (`compute_energy.py`) liczy energię potencjalną modelu G4 polem Amber
(**OL15 dla DNA, OL3 dla RNA**, woda tip3p) z **wyłączonymi oddziaływaniami elektrostatycznymi**
(wszystkie ładunki cząstkowe zerowane, `NoCutoff`). Czyta PDB ze stdin, wypisuje na stdout dwie
liczby — **energię przed i po minimalizacji** (kcal/mol) — a z `--output` zapisuje zminimalizowaną
strukturę.

### Co dodano do repo
- `docker-biotools/openmmUtils/Dockerfile` — obraz `openmm-utils:latest` (miniforge + conda-forge:
  ambertools/openmm/pdbfixer; git-clone upstream HEAD).
- `docker-biotools/openmmUtils/run.sh` — wrapper: przy zamontowanym `/work` zapisuje
  `energy.txt` (dwie energie) i `minimized.pdb`; bez mountu zachowuje wariant upstream
  (stdout=energie, stderr=zminimalizowany PDB).
- `docker-biotools/openmmUtils/batch-energy.sh` — harness: liczy energie dla folderu PDB → CSV
  (`file, e_before, e_after, delta`).
- Rejestracja obrazu w `CLAUDE.md` (lista obrazów + deploy flow).

### Budowa i test (na maszynie z Dockerem — u mnie demon był offline)
```bash
cd /home/G4Composer/docker-biotools/openmmUtils
docker build --no-cache -t openmm-utils:latest .

# pojedynczy model
cat model.pdb | docker run -i --rm openmm-utils:latest        # -> "E_przed E_po"

# wsad: energie dla wielu modeli do CSV
./batch-energy.sh /sciezka/do/modeli openmm-energies.csv
```

### Test pogłębiony „jabłka z jabłkami” (przeprowadzony)

Aby uczciwie ocenić sygnał, wygenerowano **jedną sekwencję (`gggttagggttagggttaggg`, ludzki telomer,
DNA) w 10 kandydackich topologiach** przez prawdziwy `G4TopologyGenerator`, każdą zbudowano przez
`quadro14L` (683 atomy, ten sam zestaw), po czym policzono energię `openmm-utils` przed i po
minimalizacji. Zestaw jest w pełni porównywalny (te same atomy, ta sama procedura budowy).

| topologia | fold | quadro Etotal | openmm PRZED | openmm PO |
|---|---|--:|--:|--:|
| -Lw-Ln-Lw | chair 6a | −754 | 2,96×10⁷ | −1694 |
| -Lw-Ln-P | hybrid3 7a | −773 | 7,1×10⁵ | −1725 |
| -P-Lw-Ln | hybrid1 9a | −363 | 1,48×10¹⁰ | −1726 |
| -P-P-Lw | hybrid2 2a | +10093 | 2,8×10⁴ | −138 |
| -P-P-P | parallel 1a | +153031 | 3,5×10⁶ | −52 |
| +LnD-Lw | basket2 11b | −716 | 4,99×10⁹ | −1687 |
| +Ln+Lw+Ln | chair 6b | +103878 | 4,7×10⁶ | −1738 |
| +Ln+Lw+P | hybrid2 7b | +11462 | 4,4×10⁶ | +4578 |
| +P+Ln+Lw | hybrid1 9b | −635 | 6,1×10⁷ | −1698 |
| +P+P+Ln | hybrid3 2b | +8874 | 4,6×10⁵ | +1556 |

**Wniosek 1 — surowa energia PRZED minimalizacją NIE jest użytecznym rankingiem (dla tych modeli).**
Waha się o **6 rzędów wielkości** (2,8×10⁴ … 1,48×10¹⁰) i jest **nieskorelowana** z jakością modelu:
np. 9a ma najgorszą energię przed (1,48×10¹⁰), a jedną z najlepszych po (−1726); 2a ma najlepszą
przed (2,8×10⁴), a słabą po (−138). Powód: modele quadro/xplor są **bezwodorowe**, a `pdbfixer`
dokłada wodory w idealnych pozycjach → katastrofalne kolizje dominują energię przed minimalizacją.
To bezpośrednio testuje pomysł „patrzmy na energię przed” i pokazuje, że **na obecnych suchych
modelach on nie działa** bez wcześniejszego oczyszczenia.

**Wniosek 2 — energia PO minimalizacji jest MYLĄCA, bo minimalizacja niszczy tetrady.** Sprawdzono to
wprost: ElTetrado na modelu przed i po minimalizacji (3 reprezentatywne topologie):

| topologia | tetrady PRZED | tetrady PO | openmm PO |
|---|:--:|:--:|--:|
| +P+Ln+Lw (hybrid1 9b) | 3 | **0** | −1692 |
| -P-P-P (parallel 1a) | 3 | **0** | −50 |
| +Ln+Lw+Ln (chair 6b) | 3 | **0** | −1743 |

Minimalizacja **rozwaliła kwadrupleks we wszystkich przypadkach (3→0 tetrad)** — także tam, gdzie
energia po minimalizacji wyglądała znakomicie (−1700). ElTetrado na zminimalizowanym 9b raportuje już
tylko „single tetrad without stacking” zamiast „3 tetrads”. **Przyczyna: wyłączona elektrostatyka** —
tetrada G trzyma się głównie wiązaniami Hoogsteena i centralnym kationem (K⁺), a przy wyzerowanych
ładunkach nic jej nie utrzymuje, więc minimalizacja swobodnie ją rozkłada, obniżając energię. To jest
**dokładnie mechanizm, przed którym ostrzegał przełożony — potwierdzony eksperymentalnie**: niska
energia po minimalizacji = rozpad struktury, nie lepszy model.

**Wniosek 3 — najstabilniejszym dostępnym sygnałem jest własna energia `quadro14L` (Etotal).** Zgadza
się z „po minimalizacji” tam, gdzie oba wskazują zły model (2a, 1a, 7b, 2b mają Etotal > 0), a nie
jest podatna na artefakt rozpadu. Rozbieżność (6b: Etotal wysoki, ale geometrycznie poprawny) to
sygnał, że sama liczba to za mało — trzeba kontroli tetrad.

### Co z tego wynika (rekomendacja, wzmacnia kierunek przełożonego)

- **Nie używać energii PO minimalizacji `openmm-utils` do oceny G4** w obecnej konfiguracji
  (elektrostatyka off) — nagradza rozpad tetrad.
- Intuicja przełożonego („patrz na model przed destrukcyjną minimalizacją”) jest **słuszna**, ale
  surowej energii przed nie da się użyć wprost — najpierw potrzebna **relaksacja samych wodorów**
  (zamrożone atomy ciężkie), żeby energia odzwierciedlała naprężenie topologii ciężkoatomowej, a nie
  kolizje dołożonych wodorów.
- Najmocniejsze praktyczne zastosowanie tego narzędzia to **kontrola integralności**: ElTetrado
  przed/po dowolnej minimalizacji jako **flaga „model traci tetrady”**, plus `quadro14L` Etotal jako
  główny ranking energetyczny.
- Do prawdziwego rankingu stabilności biologicznej trzeba by **innej konfiguracji** (elektrostatyka +
  rozpuszczalnik niejawny + centralny K⁺) — to poza zakresem tego obrazu.

Skrypty testu w repo: `docker-biotools/openmmUtils/batch-energy.sh` (energie wsadowo) oraz — jako
załącznik do tego raportu — protokół generowania modeli (test `ModelInpDumpUtility`, bramkowany
`INP_DUMP_DIR`) + `quadro14L` + ElTetrado przed/po.

### Metodyka (wykonana lokalnie — do powtórzenia na serwerze)
1. Zbuduj obraz (`--no-cache`).
2. Weź sekwencję o znanej topologii; wygeneruj pipeline’em wszystkie modele-kandydatów (poprawne i
   błędne topologie) — użyto testu `ModelInpDumpUtility` (bramkowany `INP_DUMP_DIR`/`INP_DUMP_SEQ`).
3. Zbuduj każdy model przez `quadro14L`; policz energię `openmm-utils` przed/po.
4. Sprawdź hipotezy: (a) czy energia **przed** minimalizacją rankuje topologie; (b) czy energia
   **po** minimalizacji wprowadza w błąd.
5. Uruchom ElTetrado na modelu przed i po minimalizacji — policz tetrady (weryfikacja „minimalizacja
   rozwala tetrady”).

Wyniki i wnioski z wykonania tej metodyki są w sekcji „Test pogłębiony” powyżej.

### Test wstępny: struktury deponowane (kontekst)

Obraz zbudowany (`openmm-utils:latest`, 3,94 GB) i przetestowany też na deponowanych strukturach G4
(oba tryby wrappera — stdin oraz montowany `/work` — działają, zwracają energie + zminimalizowany
PDB). Energie (kcal/mol; pierwszy model dla zespołów NMR):

| struktura | E przed min. | E po min. | Δ |
|---|---:|---:|---:|
| 1kf1 (X-ray, parallel DNA G4) | 996 270 | −1 714 | 997 984 |
| 2gku (NMR, hybrydowy DNA G4) | 7 431 403 | −1 682 | 7 433 086 |
| 5oph (NMR) | 20 055 | −3 515 | 23 571 |

**Kluczowy, uczciwy wniosek doprecyzowujący hipotezę przełożonego:** surowa energia PRZED
minimalizacją waha się o **trzy rzędy wielkości** (20k–7,4M) między strukturami i jest zdominowana
przez kolizje steryczne z **dodawania wodorów przez pdbfixer do struktur, których nigdy nie
minimalizowano tym polem siłowym**. To NIE jest miara jakości foldu porównywalna między dowolnymi
plikami PDB — 5oph „20k” vs 2gku „7,4M” nie znaczy, że 5oph to lepszy fold; to artefakt umieszczenia
wodorów i pochodzenia struktury.

Wniosek praktyczny: **energia przed minimalizacją jest użytecznym sygnałem tylko przy porównaniu
jabłek z jabłkami** — kandydatów o **tym samym zestawie atomów** i **tej samej procedurze budowy
modelu** (dokładnie to, co robi pipeline template-geometry G4Composera: modele dla jednej sekwencji,
budowane identycznie). Porównywanie surowej energii przed minimalizacją między różnymi sekwencjami/
źródłami jest zdominowane przez szum budowy. Dodatkowo energia PO minimalizacji też ma szum przebiegu
(1kf1: −1697 vs −1714 w dwóch uruchomieniach — zbieżność do nieco innych minimów lokalnych, ~16
kcal/mol), więc różnice poniżej kilkudziesięciu kcal/mol nie są znaczące.

To wzmacnia rekomendację: sygnał ma sens **wewnątrz grupy kandydatów jednej sekwencji**, po
ujednoliceniu budowy modeli, i najlepiej sparowany z kontrolą integralności tetrad (ElTetrado na
`minimized.pdb`).

### Proponowana integracja w G4Composer (po walidacji)
- Dodatkowa kolumna per-kandydat: **`E_pre` (Amber, bez elektrostatyki)** obok energii quadro14L,
  jako niezależny sygnał jakości budowy modelu.
- **Flaga sanity** dla kandydatów, których zminimalizowana struktura traci tetrady (ElTetrado
  tetrad-count < oczekiwany) — ostrzeżenie zamiast fałszywie niskiej energii.
- Ranking oparty na `E_pre` jako sugerował przełożony, z energią quadro14L i integralnością tetrad
  jako uzupełnieniem.

---

## Status i weryfikacja

| Punkt | Status | Weryfikacja |
|---|---|---|
| 1. Nazwa zakładki | zrobione | `npm run build` OK, 60 testów Vitest OK |
| 2. Notacja Silvy z geometrii | zrobione | `dotnet test` — 161 testów OK (w tym nowe matchera) |
| 3. Nowa wersja alignera | zrobione (kod) | `dotnet build` OK; przebudowa obrazu → serwer |
| 4. openmm-utils | zbudowane + przetestowane lokalnie | obraz 3,94 GB; energie policzone na 1kf1/2gku/5oph (patrz „Wyniki testu") |

**Pozostaje:** przebudowa `onquadro-aligner` (nowa wersja) na serwerze z `--no-cache` oraz push
submodułu `docker-biotools` (remote `mkdewe/docker-quadro`, commit `openmmUtils` na gałęzi
`feature/openmm-utils`) i bump wskaźnika submodułu w głównym repo przed budową na serwerze. Obraz
`openmm-utils` zbudowano i przetestowano lokalnie po tym, jak demon Dockera wystartował.
