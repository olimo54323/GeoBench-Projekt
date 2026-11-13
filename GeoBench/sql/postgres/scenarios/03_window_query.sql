-- Scenariusz 3: zapytanie oknem (PostgreSQL + PostGIS). Wykonywane osobno dla każdego okna z parametrami
-- @xmin, @ymin, @xmax, @ymax (jak przy przesuwaniu mapy).
-- ST_Intersects (z indeksem GiST przez &&), więc działka stykająca się z oknem też się liczy.
-- Kolumna wyniku: id_b (identyfikator okna dodaje aplikacja). Bez średnika na końcu.
SELECT d.id_dzialki AS id_b
FROM dzialka AS d
WHERE ST_Intersects(d.geom, ST_MakeEnvelope(@xmin, @ymin, @xmax, @ymax, 2180))
