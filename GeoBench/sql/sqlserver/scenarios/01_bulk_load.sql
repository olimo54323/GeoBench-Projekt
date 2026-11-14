-- Scenariusz 1: ładowanie masowe (SQL Server). Bez zapytania: aplikacja usuwa indeks przestrzenny,
-- obcina tabelę (TRUNCATE), zapisuje wiersze przez SqlBulkCopy z TABLOCK paczkami jak import,
-- tworzy indeks GEOMETRY_GRID z zasięgiem danych i wykonuje UPDATE STATISTICS – w jednej transakcji (BulkLoadScenario).
SELECT 1
