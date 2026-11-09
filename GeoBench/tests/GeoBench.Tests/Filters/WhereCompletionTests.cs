using GeoBench.Core.Data.Filters;
using GeoBench.Core.Model;

namespace GeoBench.Tests.Filters
{
    public class WhereCompletionTests
    {
        private static WhereCompletionContext Analyze(string text, int? caret = null)
        {
            using var context = TestModel.Create();
            var schema = WhereSchema.For<Budynek>(context);
            return WhereCompletion.Analyze(text, caret ?? text.Length, schema);
        }

        [Fact]
        public void Start_SuggestsColumns()
        {
            var result = Analyze("");

            Assert.Equal(WhereCompletionKind.Column, result.Kind);
            Assert.Contains(result.Items, i => i.Display == "rodzaj" && i.Detail == "tekst");
            Assert.Contains(result.Items, i => i.Display == "kondygnacje_nadziemne" && i.Detail == "liczba całkowita");
        }

        [Fact]
        public void StartedWord_IsPrefixToReplace()
        {
            var result = Analyze("rodzaj = 'm' AND kond");

            Assert.Equal(WhereCompletionKind.Column, result.Kind);
            Assert.Equal("kond", result.Prefix);
            Assert.Equal(17, result.ReplaceStart);
            Assert.Equal(new[] { "kondygnacje_nadziemne", "kondygnacje_podziemne" }, result.Filter(result.Items).Select(i => i.Display));
        }

        [Fact]
        public void AfterColumn_SuggestsOperatorsForType()
        {
            var text = Analyze("rodzaj ");
            Assert.Equal(WhereCompletionKind.Operator, text.Kind);
            Assert.Contains(text.Items, i => i.Display == "LIKE");

            var number = Analyze("kondygnacje_nadziemne ");
            Assert.DoesNotContain(number.Items, i => i.Display == "LIKE");
            Assert.Contains(number.Items, i => i.Display == "BETWEEN");

            var started = Analyze("rodzaj I");
            Assert.Equal(new[] { "IN", "IS NULL", "IS NOT NULL" }, started.Filter(started.Items).Take(3).Select(i => i.Display));
        }

        [Theory]
        [InlineData("rodzaj = ")]
        [InlineData("rodzaj IN (")]
        [InlineData("rodzaj IN ('m', ")]
        [InlineData("rodzaj LIKE ")]
        [InlineData("rodzaj BETWEEN 'a' AND ")]
        public void ValuePositions_AskForColumnValues(string text)
        {
            var result = Analyze(text);

            Assert.Equal(WhereCompletionKind.Value, result.Kind);
            Assert.Equal("rodzaj", result.Column!.Name);
        }

        [Fact]
        public void StartedString_IsReplacedWithQuotedValue()
        {
            var result = Analyze("rodzaj = 'g");

            Assert.Equal(WhereCompletionKind.Value, result.Kind);
            Assert.Equal("g", result.Prefix);
            Assert.Equal(9, result.ReplaceStart);
            Assert.Equal("'O''Brien' ", WhereCompletion.ValueItem(result.Column!, "O'Brien").Insert);
        }

        [Fact]
        public void AfterValue_SuggestsConnectors()
        {
            Assert.Equal(new[] { "AND", "OR" }, Analyze("rodzaj = 'm' ").Items.Select(i => i.Display));
            Assert.Equal(new[] { "AND", "OR", ")" }, Analyze("(rodzaj = 'm' ").Items.Select(i => i.Display));
            Assert.Equal(new[] { ",", ")" }, Analyze("rodzaj IN ('m' ").Items.Select(i => i.Display));
            Assert.Equal(new[] { "AND" }, Analyze("kondygnacje_nadziemne BETWEEN 1 ").Items.Select(i => i.Display));
            Assert.Equal(new[] { "NULL", "NOT NULL" }, Analyze("rodzaj IS ").Items.Select(i => i.Display));
        }

        [Fact]
        public void UnknownColumn_GivesNoSuggestions()
        {
            Assert.Equal(WhereCompletionKind.None, Analyze("rodzja ").Kind);
        }

        [Fact]
        public void CaretInMiddle_UsesTextBeforeCaret()
        {
            var result = Analyze("rodzaj = 'm' AND kondygnacje_nadziemne > 2", caret: 7);
            Assert.Equal(WhereCompletionKind.Operator, result.Kind);
        }
    }
}
