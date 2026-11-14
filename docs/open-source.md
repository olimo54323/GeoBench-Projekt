# Biblioteki open source

GeoBench korzysta wyłącznie z bibliotek open source dostępnych w NuGet. Wersje są w plikach `.csproj`.

## Biblioteka GeoBench.Core

| Pakiet | Zastosowanie | Licencja |
|---|---|---|
| [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) | geometria, predykaty i operacje przestrzenne, STRtree, naprawa geometrii (strategia NTS) | BSD-3-Clause |
| [ProjNET](https://github.com/NetTopologySuite/ProjNet4GeoAPI) | transformacje układów współrzędnych (PL-2000 → PL-1992, Web Mercator) | LGPL-2.1 |
| [Microsoft.EntityFrameworkCore](https://github.com/dotnet/efcore) (+ SqlServer, SqlServer.NetTopologySuite, Design, Tools) | ORM, migracje, dostawca SQL Server z typem `geometry` | MIT |
| [Npgsql.EntityFrameworkCore.PostgreSQL](https://github.com/npgsql/efcore.pg) (+ NetTopologySuite) | dostawca PostgreSQL z PostGIS | PostgreSQL License |
| [EFCore.NamingConventions](https://github.com/efcore/EFCore.NamingConventions) | nazwy tabel i kolumn w `snake_case` | Apache-2.0 |
| [Microsoft.Extensions.Configuration](https://github.com/dotnet/runtime) (Json, EnvironmentVariables) | konfiguracja z `appsettings.json` i zmiennych środowiskowych | MIT |
| [ClosedXML](https://github.com/ClosedXML/ClosedXML) | eksport do XLSX | MIT |
| [CommunityToolkit.Diagnostics](https://github.com/CommunityToolkit/dotnet) | sprawdzanie argumentów | MIT |
| [System.Security.Cryptography.Pkcs](https://github.com/dotnet/runtime) | zależność pomocnicza (kryptografia), dodana jawnie w nowszej wersji | MIT |

## Aplikacja GeoBench.Wpf

| Pakiet | Zastosowanie | Licencja |
|---|---|---|
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | wzorzec MVVM (ObservableProperty, RelayCommand) | MIT |
| [Mapsui.Wpf](https://github.com/Mapsui/Mapsui) | mapa 2D, podkład OpenStreetMap | MIT |
| [ScottPlot.WPF](https://github.com/ScottPlot/ScottPlot) | wykresy wyników, eksport PNG/SVG | MIT |
| [Extended.Wpf.Toolkit](https://github.com/xceedsoftware/wpftoolkit) | kontrolki (wybór koloru, pola liczbowe) | Ms-PL |

## Testy

| Pakiet | Zastosowanie | Licencja |
|---|---|---|
| [xunit](https://github.com/xunit/xunit), xunit.runner.visualstudio | testy jednostkowe i integracyjne | Apache-2.0 |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) | uruchamianie testów | MIT |
| [coverlet.collector](https://github.com/coverlet-coverage/coverlet) | pokrycie kodu | MIT |

## Narzędzia i dane

| Element | Zastosowanie | Licencja / warunki |
|---|---|---|
| [DocFX](https://github.com/dotnet/docfx) | strona dokumentacji z referencją API | MIT |
| GitHub Actions: `actions/*`, [softprops/action-gh-release](https://github.com/softprops/action-gh-release), [dawidd6/action-send-mail](https://github.com/dawidd6/action-send-mail) | CI, wydania, powiadomienia e-mail | MIT |
| Kafelki [OpenStreetMap](https://www.openstreetmap.org/copyright) | podkład mapy | ODbL, atrybucja „© OpenStreetMap contributors” |
| Usługa WFS EGiB (powiat 2464) | działki i budynki | dane publiczne udostępniane przez starostwo / urząd miasta |
| Plan ogólny gminy (APP 2.0) | strefy planistyczne, OUZ, OZS | dane publiczne; w projekcie wyłącznie dane badawcze |
| [QGIS](https://qgis.org) | przygotowanie wyniku wzorcowego | GPL-2.0+ (narzędzie zewnętrzne, nie jest częścią aplikacji) |
