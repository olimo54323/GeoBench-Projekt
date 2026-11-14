-- Scenariusz 07: liczba budynków w odległości do 50 m od działki (SQL Server).
-- Warunek odległości między powierzchniami (bez budowania bufora), granica włącznie; budynek na działce ma 0.
-- STDistance(...) <= 50 na kolumnie z indeksem: wzorzec obsługiwany indeksem przestrzennym.
-- CROSS APPLY z COUNT zwraca wiersz także dla działki bez budynków (liczba 0).
-- Kolumny wyniku: id_a (działka), id_b (liczba budynków jako tekst, żeby trafiła do odcisku wyniku).
-- Bez średnika na końcu (plik jest opakowywany).
SELECT d.id_dzialki AS id_a, CAST(n.liczba AS nvarchar(20)) AS id_b
FROM dzialka AS d
CROSS APPLY (
    SELECT COUNT(*) AS liczba
    FROM budynek AS b
    WHERE b.geom.STDistance(d.geom) <= 50
) AS n
WHERE d.geom IS NOT NULL
