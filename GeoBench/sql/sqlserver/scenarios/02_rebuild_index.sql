-- Scenariusz 2: przebudowa indeksu przestrzennego (SQL Server). Szablon: aplikacja wstawia w miejsca w klamrach
-- nazwę indeksu, tabeli, kolumny i parametry odczytane z sys.spatial_index_tessellations, więc indeks powstaje
-- z tymi samymi ustawieniami (BOUNDING_BOX, poziomy siatki, CELLS_PER_OBJECT).
-- Całość w jednej transakcji: przy błędzie albo przerwaniu baza przywraca poprzedni indeks.
DROP INDEX [{index}] ON [{table}];
CREATE SPATIAL INDEX [{index}] ON [{table}] ([{column}]) USING GEOMETRY_GRID
    WITH (BOUNDING_BOX = ({xmin}, {ymin}, {xmax}, {ymax}),
          GRIDS = (LEVEL_1 = {level_1}, LEVEL_2 = {level_2}, LEVEL_3 = {level_3}, LEVEL_4 = {level_4}),
          CELLS_PER_OBJECT = {cells_per_object});
