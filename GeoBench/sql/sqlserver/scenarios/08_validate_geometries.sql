-- Scenariusz 08: walidacja wszystkich geometrii trzech warstw i naprawa niepoprawnych (SQL Server).
-- Wynik tylko dla niepoprawnych według STIsValid: warstwa (nazwa tabeli), identyfikator obiektu i pole po MakeValid.
-- MakeValid może zwrócić kolekcję (np. wielokąt z odcinkiem); STArea liczy pole części powierzchniowych.
-- Kolumny wyniku: id_a (warstwa), id_b (obiekt), pole. Bez średnika na końcu (plik jest opakowywany).
SELECT N'budynek' AS id_a, id_budynku AS id_b, geom.MakeValid().STArea() AS pole
FROM budynek WHERE geom IS NOT NULL AND geom.STIsValid() = 0
UNION ALL
SELECT N'dzialka', id_dzialki, geom.MakeValid().STArea()
FROM dzialka WHERE geom IS NOT NULL AND geom.STIsValid() = 0
UNION ALL
SELECT N'strefa_planistyczna', oznaczenie, geom.MakeValid().STArea()
FROM strefa_planistyczna WHERE geom IS NOT NULL AND geom.STIsValid() = 0
