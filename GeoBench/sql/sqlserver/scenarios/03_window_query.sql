-- Scenariusz 3: zapytanie oknem (SQL Server). Wykonywane osobno dla każdego okna z parametrami
-- @xmin, @ymin, @xmax, @ymax (jak przy przesuwaniu mapy). Okno jako prostokąt: obwiednia dwóch narożników.
-- Warunek STIntersects, więc działka stykająca się z oknem też się liczy (tak samo w PostGIS i NTS).
-- Kolumna wyniku: id_b (identyfikator okna dodaje aplikacja). Bez średnika na końcu.
SELECT d.id_dzialki AS id_b
FROM dzialka AS d
WHERE d.geom.STIntersects(geometry::Point(@xmin, @ymin, 2180).STUnion(geometry::Point(@xmax, @ymax, 2180)).STEnvelope()) = 1
