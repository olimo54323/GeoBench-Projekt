-- Scenariusz 1: ładowanie masowe (PostgreSQL + PostGIS). Bez zapytania: aplikacja usuwa indeks przestrzenny,
-- obcina tabelę (TRUNCATE … RESTART IDENTITY), zapisuje wiersze binarnym COPY paczkami jak import,
-- tworzy indeks GiST i wykonuje ANALYZE – w jednej transakcji (BulkLoadScenario).
SELECT 1
