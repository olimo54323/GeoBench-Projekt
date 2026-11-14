# Instrukcja użytkownika

GeoBench porównuje czas i wynik tych samych operacji przestrzennych wykonywanych w SQL Server, w PostgreSQL/PostGIS
i w kodzie C# (NetTopologySuite). Typowa kolejność pracy:

1. **Połączenia** – sprawdzenie baz i nałożenie migracji.
2. **Import** – pobranie działek i budynków z WFS oraz import planu ogólnego (GML) do obu baz.
3. **Dane** – kontrola, czy obie bazy mają te same dane.
4. **Mapa** – podgląd danych.
5. **Benchmark** – wybór scenariusza i wariantów, pomiar.
6. **Wyniki** – wykresy z raportów.
7. **Eksport** – tabele wyników do XLSX, CSV, XML.

Na dole okna jest log operacji, pasek postępu i przycisk **Anuluj**. Naraz trwa tylko jedna operacja.

<!-- TODO: zrzut ekranu okna głównego -->

## 1. Połączenia

Connection stringi pochodzą z pliku `appsettings.json` obok `GeoBench.slnx` (patrz [instrukcja dla programisty](developer.md)).

- **Testuj połączenia** – dla każdej bazy pokazuje stan, wersję serwera i stan migracji.
- **Utwórz bazę i nałóż migracje** / **Nałóż migracje** – tworzy schemat (tabele, ograniczenia SRID, rozszerzenie PostGIS).

## 2. Import

Import przygotowuje dane i **nie jest pomiarem**. Wybierz bazy docelowe (SQL Server, PostgreSQL lub obie).

**Ewidencja gruntów i budynków (WFS)**

1. Wybierz źródło (z `sources.json`) i warstwy: działki, budynki. Obok warstwy widać stan cache.
2. **Pobierz i importuj** – strony WFS są pobierane asynchronicznie do katalogu cache, a potem ładowane do baz
   zapisem masowym. Obie bazy ładują te same strony z cache. Ponowny import zastępuje całą tabelę.
3. **Pobierz ponownie z usługi** – ignoruje cache (np. po zmianie danych w usłudze).

**Plan ogólny gminy (GML, APP 2.0)**

1. **Wybierz…** plik `.gml` planu.
2. Zaznacz **Dane badawcze**, jeśli plan nie obowiązuje i służy wyłącznie do badania.
3. **Importuj plan** – zapisuje akt planowania, strefy planistyczne i regulacje (OUZ, OZS). Ponowny import tego
   samego aktu zastępuje poprzedni.

Po imporcie powstaje indeks przestrzenny. Podsumowanie pokazuje liczby obiektów odczytanych, zapisanych,
pominiętych i odrzuconych oraz naprawione geometrie.

## 3. Dane

- **Odśwież** – dla każdej warstwy (budynki, działki, strefy, regulacje) i każdej bazy: liczba obiektów, zasięg,
  rozmiar tabeli, indeks przestrzenny (nazwa, rozmiar, liczba komórek lub wpisów).
- **Sprawdź poprawność geometrii** – dodatkowo liczy niepoprawne geometrie (wolniej).
- Komunikat na górze ostrzega, gdy liczności warstw różnią się między bazami: benchmark wymaga równych danych.
- Dolna tabela pokazuje warianty zapisane w tabelach wyników scenariuszy.
- **Wyczyść dane…** – po potwierdzeniu przywraca zaznaczone bazy do stanu po migracjach: opróżnia wszystkie tabele,
  usuwa indeksy przestrzenne i zeruje liczniki kluczy. **Operacji nie można cofnąć**; dane trzeba zaimportować ponownie.

## 4. Mapa

- Wybór bazy, podkład OpenStreetMap, warstwy: strefy (zawsze), działki i budynki (po przybliżeniu).
- **Styl mapy…** – kolory rodzajów stref i styl warstw; **Filtry…** – warunki atrybutowe (zaznaczony filtr pokazuje
  tylko pasujące obiekty).
- **Prawy przycisk myszy** na mapie – informacje o obiektach w tym miejscu, np. działka lub budynek z ich strefami.
- **Centruj mapę** – powrót do zasięgu danych.

## 5. Benchmark

1. Wybierz **scenariusz** (tabela niżej).
2. Zaznacz **warianty**:

   | Wariant | Silnik | Przetwarzanie |
   |---|---|---|
   | W1 | SQL Server | w bazie (SQL) |
   | W2 | PostgreSQL + PostGIS | w bazie (SQL) |
   | W3 / W3P | SQL Server jako magazyn | NTS jedno- / wielowątkowo |
   | W4 / W4P | PostgreSQL jako magazyn | NTS jedno- / wielowątkowo |

3. Ustaw liczbę **pomiarów** i **rozgrzewek** oraz opcje:
   - przebieg diagnostyczny z planem zapytania (W1, W2, poza pomiarem),
   - zapis par do tabeli wyników w bazie wariantu,
   - dopuszczenie różnych liczności tabel (wyniki wtedy nieporównywalne),
   - naprawa niepoprawnych geometrii (bez zaznaczenia są odrzucane i wymieniane w raporcie).
4. Opcjonalnie wskaż **wzorzec z QGIS** (CSV, patrz niżej).
5. **Uruchom pomiar**.

Tabela wyników pokazuje dla wariantu: medianę, p95, min–max, mediany faz (NTS: odczyt, przygotowanie, indeks,
obliczenie), liczbę par, odcisk SHA-256 wyniku i zgodność ze wzorcem. Raport (`report.json`, `summary.csv`, plany zapytań)
trafia do katalogu `results/<data_godzina>`. Czas zawiera odczyt wszystkich par do aplikacji.

**Scenariusze**

| Id | Scenariusz | Wynik |
|---|---|---|
| 01a–c | Ładowanie masowe budynków / działek / stref (TRUNCATE, zapis masowy, indeks) | czas; tylko W1, W2 |
| 02a–c | Przebudowa indeksu przestrzennego budynków / działek / stref | czas, rozmiar indeksu |
| 03-100/500/2000 | Zapytanie oknem: 100 okien o boku 100, 500 lub 2000 m (działki) | pary okno–działka |
| 04 | Punkt w wielokącie: centroid budynku w strefie planistycznej | pary budynek–strefa |
| 05a | Nakładanie: działki × strefy | pary z polem przecięcia |
| 05b | Nakładanie: budynki × strefy | pary z polem przecięcia |
| 05c | Nakładanie: działki × budynki | pary z polem przecięcia |
| 06 | Najbliższy inny budynek dla każdego budynku (remis: mniejsze id) | pary z odległością |
| 07 | Liczba budynków do 50 m od działki | działka i liczba |
| 08 | Walidacja i naprawa geometrii trzech warstw | niepoprawne obiekty z polem po naprawie |

Scenariusze 01 i 02 zmieniają dane w bazie (przeładowanie tabeli, przebudowa indeksu); po pomiarze zawartość
pozostaje taka sama.

**Wzorzec z QGIS**

1. Wczytaj te same dane do osobnej bazy PostGIS za pomocą QGIS.
2. Wykonaj w QGIS to samo złączenie (np. *Złącz atrybuty według położenia*) i wyeksportuj wynik do CSV z kolumnami
   `id_a`, `id_b` i opcjonalnie miarą (np. pole przecięcia).
3. W zakładce Benchmark wskaż plik CSV. Bez wzorca wyniki są porównywane z pierwszym udanym wariantem.

## 6. Wyniki

Lista raportów z katalogu `results` (**Odśwież listę**). Wykresy dla zaznaczonych raportów:

- **Mediana i rozrzut (min–p95)**, **Fazy przetwarzania**, **Wszystkie pomiary** – najnowszy zaznaczony raport,
- **Porównanie serii** – wszystkie zaznaczone raporty, od najstarszego,
- **Czas a rozmiar okna** – zaznaczone raporty scenariusza 3.

Dostępne są też skala logarytmiczna, **Eksportuj (PNG/SVG)** i **Otwórz katalog raportu**.

## 7. Eksport

1. Wybierz bazę, tabelę wyników i **Wczytaj warianty**; wybierz wariant (widać liczbę wierszy).
2. Zaznacz kolumny i ustaw ich kolejność (**Do góry**, **W dół**, **Zaznacz wszystkie**).
3. Wybierz format (XLSX, CSV, XML) i **Eksportuj…**.

CSV jest zapisywany w UTF-8 z BOM, ze średnikiem jako separatorem i kropką dziesiętną.
