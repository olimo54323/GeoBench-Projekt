# Opis API

GeoBench nie wystawia usługi sieciowej. „API” to tu:

1. **publiczne API biblioteki `GeoBench.Core`** (.NET 10, niezależnej od WPF), na którym zbudowana jest aplikacja
   i które można użyć np. z testów albo z innej aplikacji,
2. **otwarte API zewnętrzne**, z których korzysta aplikacja: usługa WFS EGiB, pliki GML planu ogólnego (schemat APP),
   kafelki OpenStreetMap.

Pełna referencja klas i metod generowana z komentarzy XML jest na
[GitHub Pages](https://olimo54323.github.io/GeoBench-Projekt/reference/) (DocFX). Ten dokument opisuje najważniejsze elementy.

## 1. Biblioteka GeoBench.Core

### 1.1. Konfiguracja i konteksty

| Typ | Opis |
|---|---|
| `GeoBenchConfiguration.Build()` | `IConfiguration` z `appsettings.json` (szukany w górę drzewa katalogów) i zmiennych środowiskowych |
| `GeoBenchContextFactory` | tworzy kontekst EF dla silnika: `Create(DatabaseEngine)`, `IsConfigured(DatabaseEngine)` |
| `DatabaseEngine` | `SqlServer`, `Postgres` |
| `GeoBenchContext` | wspólny model EF (`Dzialki`, `Budynki`, `AktyPlanowania`, `StrefyPlanistyczne`, `Regulacje`, tabele wyników); pochodne `SqlServerContext` i `PostgresContext` |

Metody pomocnicze kontekstu:

| Metoda | Działanie |
|---|---|
| `BulkInsertAsync(BulkTable<T>, rows)` | zapis masowy z pominięciem śledzenia EF (SqlBulkCopy / binarny COPY) |
| `CreateSpatialIndexAsync<T>(property, extent)` / `DropSpatialIndexAsync<T>(property)` | indeks przestrzenny (GEOMETRY_GRID / GiST) |
| `TruncateAsync<T>()` | opróżnienie tabeli z wyzerowaniem licznika |
| `ResetDataAsync()` | stan po migracjach: puste tabele, bez indeksów przestrzennych, liczniki od 1 |
| `ModelTables()` | nazwy tabel modelu |

### 1.2. Magazyn: `ISpatialStore`

Dostęp ADO.NET do tabel przestrzennych na połączeniu kontekstu EF (także w jego transakcji).
Implementacje: `SqlServerSpatialStore`, `PostgresSpatialStore`; fabryka `SpatialStore.Create(context)`.
Warstwa to `SpatialLayer` (tabela, kolumna identyfikatora, kolumna geometrii): `Budynki`, `Dzialki`, `Strefy`, `Regulacje`.

| Metoda | Opis |
|---|---|
| `CountAsync(layer)` | liczba obiektów z geometrią |
| `ReadFeaturesAsync(layer)` | strumień `SpatialFeature(Id, Geometry)` w natywnym formacie silnika |
| `ExecuteScenarioAsync(sql)` | zapytanie scenariusza → pary `ResultPair(IdA, IdB, Measure?)` |
| `ExecuteParameterizedAsync(sql, sets)` | to samo zapytanie przygotowane raz i wykonane dla wielu zestawów parametrów |
| `ExtentAsync(layer)` | zasięg warstwy |
| `InvalidIdsAsync(layer)` | identyfikatory niepoprawnych geometrii (walidator silnika) |
| `TableSizeAsync(layer)`, `SpatialIndexAsync(layer)` | rozmiar tabeli, parametry i rozmiar indeksu |
| `ExecuteDdlAsync(sql)` | DDL w jednej transakcji |
| `DiagnoseAsync(sql)` | plan zapytania (XML showplan / JSON z EXPLAIN) i statystyki |
| `GetServerInfoAsync()` | wersja serwera i ustawienia istotne dla pomiaru |

### 1.3. Scenariusze i strategie

```
IBenchmarkScenario ── zapytanie SQL (plik sql/<silnik>/scenarios/<plik>.sql)
                   └─ CreateNtsRun() → INtsScenarioRun: LoadAsync → Prepare → BuildIndex → Compute
IProcessingStrategy: InDatabaseStrategy (W1, W2) | NtsStrategy(parallel) (W3, W3P, W4, W4P)
BenchmarkVariant = (kod, silnik, strategia);  VariantFactory.All / VariantFactory.Get("W2")
```

Najważniejsze składowe `IBenchmarkScenario`: `Id`, `Name`, `SqlFileName`, `LayerA`, `LayerB`, `Layers`,
`HasMeasure`, `HasResultPairs`, `HasResultTable`, `NotApplicable(variant)`, `PrepareAsync`, `PrepareVariantAsync`,
`ExecuteInDatabaseAsync`, `CreateNtsRun`, `Describe`, `SaveResultsAsync`.

Gotowe scenariusze: `BulkLoadScenarios` (01a–c), `IndexRebuildScenarios` (02a–c), `WindowQueryScenarios` (03-*),
`PointInPolygonScenario` (04), `OverlayScenarios` (05a–c), `NearestBuildingScenario` (06),
`BuildingsWithinDistanceScenario` (07), `GeometryValidationScenario` (08).

### 1.4. Runner i raport

`BenchmarkRunner.RunAsync(scenario, variants, options, progress, cancellationToken)` wykonuje dla każdego wariantu:
kontrolę wstępną (liczności w bazach), przygotowanie, rozgrzewki, pomiary, przebieg diagnostyczny, walidację
(odcisk SHA-256, porównanie ze wzorcem) i zapis wyników. Zwraca `BenchmarkReport`.

`BenchmarkOptions`: `Warmups` (1), `Repetitions` (5), `Diagnostics`, `SaveResults`, `AllowCountMismatch`,
`ReferencePath` (CSV z QGIS), `RepairInvalidGeometries`.

`BenchmarkReportWriter.WriteAsync(report, directory)` zapisuje raport (`report.json`, `summary.csv`, plany zapytań,
listy niepoprawnych geometrii);
`BenchmarkReportReader` wczytuje raporty do wykresów.

### 1.5. Import, przegląd danych i eksport

| Typ | Opis |
|---|---|
| `WfsClient`, `WfsCache`, `WfsDownloader` | pobieranie stron WFS do cache z ponawianiem i kontrolą liczności |
| `EgibImporter.ImportAsync(context, records, layer)` | działki lub budynki do bazy (zapis masowy, indeks) |
| `PlanOgolnyImporter.ImportFileAsync(context, path, daneBadawcze)` | akt, strefy i regulacje z GML |
| `ImportReport` | odczytane, zapisane, pominięte, odrzucone, naprawione geometrie, czas |
| `DataOverview` | stan warstw i tabel wyników w bazie (zakładka Dane) |
| `ResultTableCatalog`, `ResultExportReader`, `ResultWriters` | eksport tabel wyników do XLSX, CSV, XML |

### 1.6. Przykład: pomiar z kodu

```csharp
using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Scenarios;
using GeoBench.Core.Configuration;
using GeoBench.Core.Data;
using GeoBench.Core.Reporting;
using GeoBench.Core.Storage;

var factory = new GeoBenchContextFactory(GeoBenchConfiguration.Build());
var runner = new BenchmarkRunner(factory);

var report = await runner.RunAsync(
    new PointInPolygonScenario(),
    [VariantFactory.Get("W1"), VariantFactory.Get("W2"), VariantFactory.Get("W4P")],
    new BenchmarkOptions { Warmups = 1, Repetitions = 5, SaveResults = false });

var directory = BenchmarkReportWriter.DefaultDirectory(DateTime.Now);
await BenchmarkReportWriter.WriteAsync(report, directory);
```

### 1.7. Przykład: odczyt warstwy

```csharp
await using var context = factory.Create(DatabaseEngine.Postgres);
var store = SpatialStore.Create(context);

Console.WriteLine($"Budynków: {await store.CountAsync(SpatialLayer.Budynki)}");
await foreach (var feature in store.ReadFeaturesAsync(SpatialLayer.Strefy))
    Console.WriteLine($"{feature.Id}: {feature.Geometry.Area:N0} m²");
```

## 2. Otwarte API zewnętrzne

### 2.1. Usługa WFS EGiB (działki i budynki)

Konfiguracja w `GeoBench/sources.json`. Domyślne źródło: powiat 2464 (m. Częstochowa),
`https://geoportal.czestochowa.pl/wfs2464_egib/service.svc/get`, WFS 2.0.0, warstwy `gmgml:dzialki` i `gmgml:budynki`.

Żądanie strony (`WfsClient.BuildGetFeatureUri`):

```
<Url>?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetFeature&TYPENAMES=gmgml:dzialki
     &SRSNAME=urn:ogc:def:crs:EPSG::2180&COUNT=5000&STARTINDEX=0
     &NAMESPACES=xmlns(gmgml,http://www.intergraph.com/geomedia/gml)&SORTBY=ID_DZIALKI
```

- Liczba obiektów: atrybut `numberMatched` odpowiedzi na żądanie z `COUNT=1`; z niej liczba stron.
- Stronicowanie `STARTINDEX` + `COUNT` ze stałym sortowaniem (`SORTBY`), co najwyżej `MaxParallelRequests` żądań naraz.
- Ponawianie przy błędach przejściowych (timeout, HTTP 408/429/5xx, zerwane połączenie, uszkodzony XML) po 2, 5 i 15 s.
  Strona z mniejszą liczbą obiektów niż oczekiwana (poza ostatnią) jest błędem, bo oznaczałaby zgubione obiekty.
- Odpowiedź: GML (przestrzeń nazw GeoMedia `gmgml`), geometria w EPSG:2180.

Nowe źródło dodaje się wpisem w `sources.json` (`Name`, `Url`, `Version`, `SrsName`, `PageSize`, `Layers[]` z `TypeName`,
`Target` = `Dzialka`/`Budynek`, `SortBy`).

### 2.2. Plan ogólny gminy (GML, schemat APP 2.0)

Plik GML zgodny ze schematem aplikacyjnym planowania przestrzennego (`planowaniePrzestrzenne_2_0.xsd`).
Importowane typy obiektów: `AktPlanowaniaPrzestrzennego`, `StrefaPlanistyczna`, `ObszarUzupelnieniaZabudowy`,
`ObszarZabudowySrodmiejskiej`. Pozostałe (np. dokumenty formalne) są liczone jako pominięte.
Wartości list kodowych są brane z końcówki `xlink:href`, geometria jest przeliczana z układu pliku (np. EPSG:2177)
do EPSG:2180 (ProjNET), a łuki są linearyzowane.

### 2.3. Kafelki OpenStreetMap

Podkład mapy pochodzi z serwera kafelków OpenStreetMap (przez Mapsui, `OpenStreetMap.CreateTileLayer`) z własnym
nagłówkiem `User-Agent`. Obowiązują [zasady korzystania z kafelków OSM](https://operations.osmfoundation.org/policies/tiles/)
i atrybucja „© OpenStreetMap contributors” ([licencja ODbL](https://www.openstreetmap.org/copyright)).

### 2.4. Układy współrzędnych

Dane w bazie: EPSG:2180 (PL-1992). Mapa: EPSG:3857 (Web Mercator). Plan ogólny: zwykle EPSG:2176–2179 (PL-2000).
Przeliczenia wykonuje ProjNET (`CoordinateTransformer`).
