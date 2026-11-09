using GeoBench.Core.Data.Filters;

namespace GeoBench.Tests.Filters
{
    public sealed class FilterStoreTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "GeoBenchFilters_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private static MapFilter Filter(string name, string where = "rodzaj = 'm'") => new()
        {
            Name = name,
            Table = "budynek",
            Where = where,
            Style = { FillColor = "#1F77B4", FillOpacity = 0.4 }
        };

        [Fact]
        public void SaveAndLoad_RoundTrip()
        {
            var store = new FilterStore(_root);
            store.Save(Filter("Wysokie", "kondygnacje_nadziemne > 4"));
            store.Save(Filter("mieszkalne"));

            var loaded = store.Load("budynek");

            Assert.Equal(new[] { "mieszkalne", "Wysokie" }, loaded.Select(f => f.Name));
            Assert.Equal("kondygnacje_nadziemne > 4", loaded[1].Where);
            Assert.Equal("#1F77B4", loaded[1].Style.FillColor);
            Assert.True(File.Exists(Path.Combine(_root, "budynek", "Wysokie.json")));
            Assert.Empty(store.Load("dzialka"));
        }

        [Fact]
        public void Rename_MovesFile()
        {
            var store = new FilterStore(_root);
            var filter = Filter("Stara");
            store.Save(filter);

            filter.Name = "Nowa";
            store.Save(filter, previousName: "Stara");

            Assert.Equal(new[] { "Nowa" }, store.Load("budynek").Select(f => f.Name));
            Assert.False(File.Exists(store.FilePath("budynek", "Stara")));
        }

        [Fact]
        public void DuplicateName_IsRejected_IgnoringCase()
        {
            var store = new FilterStore(_root);
            store.Save(Filter("Wysokie"));

            Assert.Throws<InvalidOperationException>(() => store.Save(Filter("WYSOKIE")));
            Assert.True(store.IsNameTaken("budynek", "wysokie"));
            Assert.False(store.IsNameTaken("budynek", "wysokie", exceptName: "Wysokie"));

            // Zmiana samej wielkości liter tego samego filtra jest dozwolona
            var same = Filter("WYSOKIE");
            store.Save(same, previousName: "Wysokie");
            Assert.Equal("WYSOKIE", Assert.Single(store.Load("budynek")).Name);
        }

        [Fact]
        public void InvalidCharacters_AreReplacedOnlyInFileName()
        {
            var store = new FilterStore(_root);
            store.Save(Filter("Obręb 0219 / Raków: \"duże\""));

            Assert.Equal("Obręb 0219 _ Raków_ _duże_", FilterStore.FileName("Obręb 0219 / Raków: \"duże\""));
            Assert.Equal("Obręb 0219 / Raków: \"duże\"", Assert.Single(store.Load("budynek")).Name);
            Assert.Equal("_con", FilterStore.FileName("con"));
            Assert.Equal("_", FilterStore.FileName("..."));
        }

        [Fact]
        public void Delete_RemovesFile_AndBrokenFilesAreReported()
        {
            var store = new FilterStore(_root);
            store.Save(Filter("Do usunięcia"));
            File.WriteAllText(Path.Combine(_root, "budynek", "zepsuty.json"), "{ nie json");

            store.Delete("budynek", "Do usunięcia");
            var errors = new List<string>();

            Assert.Empty(store.Load("budynek", errors));
            Assert.Single(errors);
        }
    }
}
