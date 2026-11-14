-- Scenariusz 06: najbliższy inny budynek (PostgreSQL + PostGIS).
-- Odległość między powierzchniami (budynki stykające się mają 0); przy równej odległości mniejsze id_budynku
-- w porządku bajtowym (COLLATE "C", jak porządek porządkowy w NTS i Latin1_General_BIN2 w SQL Server).
-- <-> z indeksem GiST: kolejność według prawdziwej odległości geometrii (sprawdzanej po indeksie), LIMIT 1.
-- Kolumny wyniku: id_a, id_b, odleglosc. Bez średnika na końcu (plik jest opakowywany w EXPLAIN).
SELECT a.id_budynku AS id_a, n.id_b, n.odleglosc
FROM budynek AS a
CROSS JOIN LATERAL (
    SELECT b.id_budynku AS id_b, ST_Distance(a.geom, b.geom) AS odleglosc
    FROM budynek AS b
    WHERE b.oid <> a.oid AND b.geom IS NOT NULL
    ORDER BY a.geom <-> b.geom, b.id_budynku COLLATE "C"
    LIMIT 1
) AS n
WHERE a.geom IS NOT NULL
