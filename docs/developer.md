# Instrukcja dla programisty

## 1. Wymagania

| Składnik | Wersja | Uwagi |
|---|---|---|
| Windows | 10/11 | aplikacja WPF działa tylko w Windows; biblioteka i testy jednostkowe także w Linux |
| .NET SDK | 10.0 | `dotnet --version` |
| Visual Studio | 2022 17.14+ / 2026 | opcjonalnie; obciążenie „Programowanie aplikacji klasycznych .NET” |
| Microsoft SQL Server | 2019+ (Developer/Express) | typ `geometry`; Express ogranicza pamięć i rdzenie, co wpływa na pomiary |
| PostgreSQL + PostGIS | 16+ / 3.4+ | rozszerzenie `postgis` tworzy migracja |
| Narzędzie EF Core | `dotnet tool install --global dotnet-ef` | tylko do tworzenia nowych migracji |
| QGIS | 3.x | opcjonalnie, do przygotowania wyniku wzorcowego |

## 2. Pobranie i konfiguracja

```bash
git clone https://github.com/olimo54323/GeoBench-Projekt.git
cd GeoBench-Projekt/GeoBench
copy appsettings.example.json appsettings.json
```

Uzupełnij w `GeoBench/appsettings.json` connection stringi do **pustych** baz (nazwa bazy dowolna, np. `geobench`):

```json
{
  "ConnectionStrings": {
    "SqlServer": "Server=<SERWER>\\<INSTANCJA>;Database=geobench;User Id=<LOGIN>;Password=<HASLO>;TrustServerCertificate=True",
    "Postgres": "Host=localhost;Port=5432;Database=geobench;Username=postgres;Password=<HASLO>"
  }
}
```

- `appsettings.json` jest w `.gitignore` – nie commituj haseł.
- Plik jest wyszukiwany w górę drzewa katalogów od katalogu programu, więc działa dla aplikacji, testów i `dotnet ef`.
- Każdą wartość można nadpisać zmienną środowiskową, np. `ConnectionStrings__Postgres` (tak robi CI).
- SQL Server: przy logowaniu SQL włącz tryb mieszany uwierzytelniania i **zrestartuj usługę**.

Źródła WFS są w `GeoBench/sources.json` (adres usługi, warstwy, rozmiar strony, liczba równoległych żądań).

## 3. Bazy danych i migracje

Schemat tworzą migracje EF Core (osobne dla obu silników, katalogi `Data/Migrations/SqlServer` i `Data/Migrations/Postgres`).
Najprościej nałożyć je z aplikacji: zakładka **Połączenia → Testuj połączenia → Utwórz bazę i nałóż migracje**.

Z wiersza poleceń (katalog `GeoBench`):

```bash
dotnet ef database update --project src/GeoBench.Core --context SqlServerContext
dotnet ef database update --project src/GeoBench.Core --context PostgresContext
```

Nowa migracja po zmianie modelu (zawsze dla obu silników, ta sama nazwa):

```bash
dotnet ef migrations add <Nazwa> --project src/GeoBench.Core --context SqlServerContext --output-dir Data/Migrations/SqlServer
dotnet ef migrations add <Nazwa> --project src/GeoBench.Core --context PostgresContext --output-dir Data/Migrations/Postgres
```

`DesignTimeFactories` pozwala uruchamiać `dotnet ef` bez startu WPF. Indeksy przestrzenne nie są częścią migracji:
powstają po imporcie danych (SQL Server potrzebuje zasięgu danych jako `BOUNDING_BOX`).

## 4. Budowanie i uruchomienie

```bash
dotnet build GeoBench.slnx -c Release
dotnet run --project src/GeoBench.Wpf -c Release
```

Pomiary wykonuj w konfiguracji **Release** i bez debuggera. Wyniki trafiają do `GeoBench/results/<data_godzina>/`,
a cache stron WFS do `GeoBench/cache/` (oba katalogi są poza gitem).

## 5. Testy

Testy xUnit są w `tests/GeoBench.Tests`:

- **jednostkowe** – bez baz (mapowanie GML i WFS, geometria, scenariusze NTS na danych w pamięci, walidacja, raporty, eksport),
- **integracyjne** – oznaczone `[Trait("Category", "Integration")]`, wymagają obu baz z nałożonymi migracjami.
  Działają w transakcjach wycofywanych na końcu, więc nie zmieniają danych. Część z nich zakłada zaimportowane dane
  (np. istniejący indeks przestrzenny budynków).

```bash
dotnet test GeoBench.slnx --filter "Category!=Integration"
dotnet test GeoBench.slnx --filter "Category=Integration"
dotnet test GeoBench.slnx
```

## 6. Struktura kodu

```
src/GeoBench.Core/
  Benchmark/        BenchmarkRunner, warianty (VariantFactory), IBenchmarkScenario, Scenarios/ (01–08)
  Processing/       strategie: InDatabaseStrategy (SQL), NtsStrategy (NTS, fazy odczyt/indeks/obliczenie)
  Storage/          ISpatialStore + SqlServerSpatialStore, PostgresSpatialStore (ADO.NET na połączeniu EF)
  Data/             GeoBenchContext (+ SqlServerContext, PostgresContext), migracje, zapis masowy, zapytania mapy
  Import/           Gml/ (czytnik GML), Wfs/ (klient, cache, pobieranie), Egib/ i PlanOgolny/ (mapowanie, import)
  Spatial/          transformacje układów (ProjNET), linearyzacja łuków, normalizacja geometrii
  Validation/       odcisk wyniku, porównanie ze wzorcem
  Reporting/        raport JSON/Markdown, dane wykresów
  Export/           eksport tabel wyników (XLSX, CSV, XML)
src/GeoBench.Wpf/   widoki i ViewModele (CommunityToolkit.Mvvm), mapa (Mapsui), wykresy (ScottPlot)
sql/<silnik>/scenarios/   zapytania scenariuszy, wkompilowane jako zasoby osadzone
```

## 7. Dodanie scenariusza

1. Zaimplementuj `IBenchmarkScenario` w `src/GeoBench.Core/Benchmark/Scenarios/` (wzór: `PointInPolygonScenario`
   dla par, `IndexRebuildScenario` dla scenariusza bez par). Część NTS to `INtsScenarioRun` z fazami
   `LoadAsync` → `Prepare` → `BuildIndex` → `Compute`.
2. Dodaj zapytania `sql/sqlserver/scenarios/<id>_<nazwa>.sql` i `sql/postgres/scenarios/<id>_<nazwa>.sql`
   zwracające kolumny `id_a`, `id_b` i opcjonalnie miarę. Pliki są dołączane automatycznie (wzorzec `sql/**/*.sql`).
3. Jeśli scenariusz zapisuje wyniki: encja w `Model/`, konfiguracja w `GeoBenchContext`, migracje dla obu silników
   i wpis w `ResultTableCatalog` (eksport, zakładka Dane).
4. Zarejestruj scenariusz na liście `BenchmarkViewModel.Scenarios`.
5. Testy: jednostkowe na `FakeSpatialStore` i integracyjne w klasie bazowej z podklasami dla obu silników.

## 8. Konwencje

- Nazwy w bazie: polski `snake_case` (konwencja `UseSnakeCaseNamingConvention`), identyczne w obu silnikach.
- Wszystkie geometrie w EPSG:2180, `MultiPolygon`, SRID pilnowany ograniczeniem `CHECK`.
- Komentarze i nazwy w kodzie po polsku.
- Import przygotowuje dane i nie jest pomiarem; pomiar to wyłącznie scenariusze.
