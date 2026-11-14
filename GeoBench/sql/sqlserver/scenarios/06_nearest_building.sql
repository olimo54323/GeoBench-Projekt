-- Scenariusz 06: najbliższy inny budynek (SQL Server).
-- Odległość między powierzchniami (budynki stykające się mają 0); przy równej odległości mniejsze id_budynku
-- w porządku binarnym (jak porządek porządkowy w NTS i COLLATE "C" w PostgreSQL).
-- TOP (1) + STDistance IS NOT NULL + ORDER BY STDistance na kolumnie z indeksem: wzorzec, który SQL Server
-- obsługuje indeksem przestrzennym (najbliższy sąsiad); czy indeks jest użyty, pokazuje plan w raporcie.
-- Kolumny wyniku: id_a, id_b, odleglosc. Bez średnika na końcu (plik jest opakowywany).
SELECT a.id_budynku AS id_a, n.id_b, n.odleglosc
FROM budynek AS a
CROSS APPLY (
    SELECT TOP (1) b.id_budynku AS id_b, b.geom.STDistance(a.geom) AS odleglosc
    FROM budynek AS b
    WHERE b.geom.STDistance(a.geom) IS NOT NULL AND b.oid <> a.oid
    ORDER BY b.geom.STDistance(a.geom), b.id_budynku COLLATE Latin1_General_BIN2
) AS n
WHERE a.geom IS NOT NULL
