-- Scenariusz 07: liczba budynków w odległości do 50 m od działki (PostgreSQL + PostGIS).
-- Warunek odległości między powierzchniami (bez budowania bufora), granica włącznie; budynek na działce ma 0.
-- ST_DWithin korzysta z indeksu GiST (porównanie obwiedni powiększonych o odległość, potem dokładny test).
-- LATERAL z COUNT zwraca wiersz także dla działki bez budynków (liczba 0).
-- Kolumny wyniku: id_a (działka), id_b (liczba budynków jako tekst, żeby trafiła do odcisku wyniku).
-- Bez średnika na końcu (plik jest opakowywany w EXPLAIN).
SELECT d.id_dzialki AS id_a, n.liczba::text AS id_b
FROM dzialka AS d
CROSS JOIN LATERAL (
    SELECT COUNT(*) AS liczba
    FROM budynek AS b
    WHERE ST_DWithin(d.geom, b.geom, 50)
) AS n
WHERE d.geom IS NOT NULL
