-- Scenariusz 05a: działki × strefy planistyczne, pole przecięcia (SQL Server).
-- Para powstaje, gdy geometrie się przecinają, a pole przecięcia wynosi co najmniej 0,01 m² (sama styczność odpada);
-- niepoprawne geometrie są naprawiane (MakeValid / ST_MakeValid) w ramach mierzonego zapytania.
-- Złączenie przez Filter() na kolumnie z indeksem przestrzennym (strefa_planistyczna), dokładny test i pole na geometriach po kontroli.
-- Kolumny wyniku: id_a, id_b, pole. Bez średnika na końcu (plik jest opakowywany).
SELECT a.id_a, s.oznaczenie AS id_b, p.pole
FROM (SELECT id_dzialki AS id_a, CASE WHEN geom.STIsValid() = 1 THEN geom ELSE geom.MakeValid() END AS geom
      FROM dzialka WHERE geom IS NOT NULL) AS a
JOIN strefa_planistyczna AS s ON s.geom.Filter(a.geom) = 1
CROSS APPLY (SELECT CASE WHEN s.geom.STIsValid() = 1 THEN s.geom ELSE s.geom.MakeValid() END AS geom) AS b
CROSS APPLY (SELECT b.geom.STIntersection(a.geom).STArea() AS pole) AS p
WHERE b.geom.STIntersects(a.geom) = 1 AND p.pole >= 0.01
