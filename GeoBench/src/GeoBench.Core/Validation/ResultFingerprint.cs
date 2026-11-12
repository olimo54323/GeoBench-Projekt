using System.Security.Cryptography;
using System.Text;
using GeoBench.Core.Benchmark;

namespace GeoBench.Core.Validation
{
    /// <summary>
    /// Odcisk wyniku niezależny od kolejności par: liczba par, suma miar i SHA-256 posortowanych par.
    /// Miara nie wchodzi do skrótu (różnice zaokrągleń między silnikami); jej sumę porównuje się z tolerancją.
    /// </summary>
    public sealed record ResultFingerprint(int Count, double? MeasureSum, string Sha256)
    {
        public static ResultFingerprint Compute(IReadOnlyList<ResultPair> pairs)
        {
            var lines = pairs.Select(p => $"{p.IdA}\t{p.IdB}\n").ToArray();
            Array.Sort(lines, StringComparer.Ordinal);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var line in lines)
                hash.AppendData(Encoding.UTF8.GetBytes(line));

            double? measureSum = pairs.Any(p => p.Measure.HasValue) ? pairs.Sum(p => p.Measure ?? 0) : null;
            return new ResultFingerprint(pairs.Count, measureSum, Convert.ToHexString(hash.GetHashAndReset()));
        }

        /// <summary>Skrót do wyświetlania (pierwsze 12 znaków).</summary>
        public string ShortHash => Sha256[..Math.Min(12, Sha256.Length)];
    }
}
