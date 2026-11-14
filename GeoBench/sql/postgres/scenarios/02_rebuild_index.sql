-- Scenariusz 2: przebudowa indeksu przestrzennego (PostgreSQL + PostGIS). Szablon: aplikacja wstawia w miejsca
-- w klamrach schemat i nazwę indeksu oraz jego definicję z pg_get_indexdef (CREATE INDEX … USING gist …),
-- więc indeks powstaje z tymi samymi ustawieniami.
-- Całość w jednej transakcji: przy błędzie albo przerwaniu baza przywraca poprzedni indeks.
DROP INDEX "{schema}"."{index}";
{definition};
