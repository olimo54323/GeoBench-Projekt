-- Scenariusz 4: punkt w wielokącie (PostgreSQL + PostGIS).
-- Para (budynek, strefa) powstaje, gdy centroid budynku przecina strefę planistyczną.
-- ST_Intersects (a nie ST_Contains), żeby centroid na granicy dwóch stref trafiał do obu,
-- tak samo jak STIntersects w SQL Server i Intersects w NTS.
-- Kolumny wyniku: id_a (budynek), id_b (strefa). Bez średnika na końcu (plik jest opakowywany w EXPLAIN).
SELECT b.id_budynku AS id_a, s.oznaczenie AS id_b
FROM budynek AS b
JOIN strefa_planistyczna AS s ON ST_Intersects(s.geom, ST_Centroid(b.geom))
