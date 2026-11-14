namespace GeoBench.Core.Model
{
    /// <summary>
    /// Podklasy regulacji planu ogólnego (APP 2.0) obecne w danych; nazwy wartości to nazwy typów obiektów w GML.
    /// Obszar standardów dostępności pominięty: ma dodatkowe atrybuty i nie występuje w pliku.
    /// </summary>
    public enum RodzajRegulacji
    {
        ObszarUzupelnieniaZabudowy,  // OUZ
        ObszarZabudowySrodmiejskiej  // OZS
    }
}
