# GeoBench

Aplikacja desktopowa (WPF, .NET 10) porównująca wydajność i wynik złączeń przestrzennych wykonywanych w SQL Server,
w PostgreSQL/PostGIS oraz w kodzie C# z biblioteką NetTopologySuite, na danych w skali miasta
(działki i budynki z usługi WFS EGiB, strefy planistyczne z planu ogólnego gminy).

Kod źródłowy: [github.com/olimo54323/GeoBench-Projekt](https://github.com/olimo54323/GeoBench-Projekt)

## Dokumentacja

- [Instrukcja programisty](developer.md) – wymagania, konfiguracja baz, migracje, budowanie, testy.
- [Instrukcja użytkownika](user-guide.md) – zakładki aplikacji, scenariusze, warianty, wzorzec z QGIS.
- [Opis API](api.md) – publiczne API biblioteki GeoBench.Core i używane otwarte API.
- [Referencja API](reference/) – klasy i metody GeoBench.Core generowane z komentarzy XML.
- [Biblioteki open source](open-source.md) – wykorzystane biblioteki i licencje.
- [CI/CD](ci.md) – GitHub Actions, wydawanie wersji, powiadomienia e-mail.
