-- Scenariusz 08: walidacja wszystkich geometrii trzech warstw i naprawa niepoprawnych (PostgreSQL + PostGIS).
-- Wynik tylko dla niepoprawnych według ST_IsValid: warstwa (nazwa tabeli), identyfikator obiektu i pole po ST_MakeValid.
-- ST_MakeValid może zwrócić kolekcję (np. wielokąt z odcinkiem); ST_Area liczy pole części powierzchniowych.
-- Kolumny wyniku: id_a (warstwa), id_b (obiekt), pole. Bez średnika na końcu (plik jest opakowywany w EXPLAIN).
SELECT 'budynek' AS id_a, id_budynku AS id_b, ST_Area(ST_MakeValid(geom)) AS pole
FROM budynek WHERE geom IS NOT NULL AND NOT ST_IsValid(geom)
UNION ALL
SELECT 'dzialka', id_dzialki, ST_Area(ST_MakeValid(geom))
FROM dzialka WHERE geom IS NOT NULL AND NOT ST_IsValid(geom)
UNION ALL
SELECT 'strefa_planistyczna', oznaczenie, ST_Area(ST_MakeValid(geom))
FROM strefa_planistyczna WHERE geom IS NOT NULL AND NOT ST_IsValid(geom)
