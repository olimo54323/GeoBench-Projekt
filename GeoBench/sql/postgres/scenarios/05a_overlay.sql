-- Scenariusz 05a: działki × strefy planistyczne, pole przecięcia (PostgreSQL + PostGIS).
-- Para powstaje, gdy geometrie się przecinają, a pole przecięcia wynosi co najmniej 0,01 m² (sama styczność odpada);
-- niepoprawne geometrie są odrzucane: CASE zamienia je na NULL, a metody na NULL zwracają NULL, więc para nie powstaje.
-- Złączenie przez && na kolumnie z indeksem GiST (strefa_planistyczna), dokładny test i pole na geometriach po kontroli.
-- OFFSET 0 zatrzymuje spłaszczenie podzapytania, żeby ST_Intersection liczyć raz na parę (a nie w SELECT i WHERE).
-- Kolumny wyniku: id_a, id_b, pole. Bez średnika na końcu (plik jest opakowywany w EXPLAIN).
SELECT id_a, id_b, pole
FROM (
    SELECT a.id_a, b.oznaczenie AS id_b, ST_Area(ST_Intersection(a.geom, bg.geom)) AS pole
    FROM (SELECT id_dzialki AS id_a, CASE WHEN ST_IsValid(geom) THEN geom END AS geom
          FROM dzialka WHERE geom IS NOT NULL) AS a
    JOIN strefa_planistyczna AS b ON b.geom && a.geom
    CROSS JOIN LATERAL (SELECT CASE WHEN ST_IsValid(b.geom) THEN b.geom END AS geom) AS bg
    WHERE ST_Intersects(a.geom, bg.geom)
    OFFSET 0
) AS x
WHERE pole >= 0.01
