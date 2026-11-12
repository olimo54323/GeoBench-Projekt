-- Scenariusz 4: punkt w wielokącie (SQL Server).
-- Para (budynek, strefa) powstaje, gdy centroid budynku przecina strefę planistyczną.
-- STIntersects (a nie STContains), żeby centroid na granicy dwóch stref trafiał do obu,
-- tak samo jak ST_Intersects w PostGIS i Intersects w NTS.
-- Kolumny wyniku: id_a (budynek), id_b (strefa). Bez średnika na końcu (plik jest też opakowywany).
SELECT b.id_budynku AS id_a, s.oznaczenie AS id_b
FROM budynek AS b
JOIN strefa_planistyczna AS s ON s.geom.STIntersects(b.geom.STCentroid()) = 1
