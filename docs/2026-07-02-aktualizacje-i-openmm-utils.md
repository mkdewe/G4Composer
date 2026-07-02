# G4Composer — raport ze zmian i analiza openmm-utils (2026-07-02)

Raport obejmuje cztery zlecone zmiany. Punkty 1–3 są zaimplementowane i przetestowane
lokalnie (backend .NET + frontend Vitest). Punkt 4 (openmm-utils) jest przygotowany „pod klucz” —
obraz + skrypty + integracja w dokumentacji — ale **nie mógł zostać zbudowany ani przetestowany na
tej maszynie, bo demon Dockera był wyłączony**. Wszystkie komendy do uruchomienia na serwerze są
niżej.

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

### Analiza mocnych stron i sygnału z energii przed minimalizacją

Komentarz przełożonego („może warto skupić się na energii przed minimalizacją, nawet dodatniej”) jest
**trafny i zgodny z tym, jak to narzędzie liczy energię**:

1. **Wyłączona elektrostatyka → energia = miara naprężeń i kolizji sterycznych.** Bez członu
   elektrostatycznego energia jest zdominowana przez wiązania, kąty, dihedry i van der Waalsa. To nie
   jest energia swobodna zwijania — to **miara jakości geometrii modelu** (naprężenia i zderzenia
   atomów powstałe przy przeszczepie geometrii szablonu na sekwencję). Idealnie pasuje do tego, co
   produkuje pipeline template-geometry G4Composera.

2. **Dlaczego energia PRZED minimalizacją jest lepszym sygnałem topologii.** Minimalizacja potrafi
   ściągnąć energię kiepskiego modelu bardzo nisko, ale — jak zauważył przełożony — kosztem
   rozerwania tetrad. Zatem **energia PO minimalizacji bywa myląca**: rozprute struktury mogą mieć
   bardzo niską energię. Energia PRZED minimalizacją odzwierciedla, jak dobrze wyjściowa topologia
   „siedzi” na sekwencji bez relaksacji — model o poprawnej topologii startuje z mniejszym
   naprężeniem. To czyni ją **dyskryminatorem poprawności topologii tam, gdzie energia po
   minimalizacji zawodzi**.

3. **Dodatnia energia jest oczekiwana i użyteczna.** Modele „as-built” mają kolizje → dodatnia
   energia bezwzględna nie jest wadą. Liczy się **porównanie względne** między kandydatami dla tej
   samej sekwencji (te same atomy → uczciwe porównanie bezwzględne): topologia o najniższej energii
   przed minimalizacją nawija sekwencję z najmniejszym naprężeniem.

**Ograniczenia (żeby nie nadinterpretować):**
- Energia skaluje się z liczbą atomów — **porównywać tylko w obrębie kandydatów jednej sekwencji**
  (albo normalizować na resztę/atom przy porównaniach między sekwencjami).
- Bez elektrostatyki metryka nie nagradza wiązań wodorowych ani stackingu — **nie jest rankingiem
  stabilności biologicznej**, tylko jakości budowy modelu.
- Energię PO minimalizacji trzeba parować z **kontrolą integralności tetrad** (np. ElTetrado/DNATCO
  na `minimized.pdb`): jeśli liczba tetrad spadła, niska energia to artefakt rozerwania — dokładnie
  przypadek opisany przez przełożonego.

### Proponowana metodyka testu (do wykonania na serwerze)
1. Zbuduj obraz (`--no-cache`).
2. Weź kilka sekwencji o **znanej** topologii eksperymentalnej; wygeneruj pipeline’em wszystkie
   modele-kandydatów (poprawne i błędne topologie).
3. `batch-energy.sh` → CSV energii przed/po dla każdego kandydata.
4. Sprawdź hipotezy: (a) czy poprawna topologia ma najniższą energię **przed** minimalizacją;
   (b) czy energia **po** minimalizacji sama wprowadza w błąd (błędna topologia schodzi najniżej).
5. Uruchom ElTetrado na `minimized.pdb` i policz tetrady — potwierdź obserwację „minimalizacja
   rozwala tetrady”.

### Wyniki testu (zbudowane i uruchomione lokalnie)

Obraz zbudowany (`openmm-utils:latest`, 3,94 GB) i przetestowany na deponowanych strukturach G4
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
