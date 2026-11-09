using GeoBench.Core.Data.Filters;

namespace GeoBench.Tests.Filters
{
    public class WhereParserTests
    {
        [Fact]
        public void EmptyText_IsNoFilter()
        {
            Assert.Null(WhereParser.Parse(null));
            Assert.Null(WhereParser.Parse("   "));
        }

        [Fact]
        public void AndBindsStrongerThanOr()
        {
            var node = WhereParser.Parse("a = 1 OR b = 2 AND c = 3");

            var or = Assert.IsType<WhereOr>(node);
            Assert.IsType<WhereCompare>(or.Left);
            Assert.IsType<WhereAnd>(or.Right);
        }

        [Fact]
        public void Parentheses_ChangeGrouping()
        {
            var node = WhereParser.Parse("(a = 1 OR b = 2) and c = 3");

            var and = Assert.IsType<WhereAnd>(node);
            Assert.IsType<WhereOr>(and.Left);
        }

        [Fact]
        public void NotBindsStrongerThanAnd()
        {
            var and = Assert.IsType<WhereAnd>(WhereParser.Parse("NOT a = 1 AND b = 2"));
            Assert.IsType<WhereNot>(and.Left);
        }

        [Fact]
        public void Predicates_AllForms()
        {
            Assert.Equal(WhereComparison.NotEqual, Assert.IsType<WhereCompare>(WhereParser.Parse("a <> 1")).Operator);
            Assert.Equal(WhereComparison.NotEqual, Assert.IsType<WhereCompare>(WhereParser.Parse("a != 1")).Operator);
            Assert.Equal(WhereComparison.GreaterOrEqual, Assert.IsType<WhereCompare>(WhereParser.Parse("a >= 1")).Operator);

            var notIn = Assert.IsType<WhereIn>(WhereParser.Parse("a NOT IN ('x', 'y''z')"));
            Assert.True(notIn.Negated);
            Assert.Equal(new object[] { "x", "y'z" }, notIn.Values.Select(v => v.Value));

            Assert.True(Assert.IsType<WhereLike>(WhereParser.Parse("a not like 'R%'")).Negated);
            Assert.True(Assert.IsType<WhereIsNull>(WhereParser.Parse("a IS NOT NULL")).Negated);
            Assert.False(Assert.IsType<WhereIsNull>(WhereParser.Parse("a is null")).Negated);

            var between = Assert.IsType<WhereBetween>(WhereParser.Parse("a BETWEEN -1.5 AND 2"));
            Assert.Equal(-1.5m, between.Low.Value);
            Assert.Equal(2m, between.High.Value);
        }

        [Fact]
        public void BetweenAnd_IsNotLogicalAnd()
        {
            var and = Assert.IsType<WhereAnd>(WhereParser.Parse("a BETWEEN 1 AND 2 AND b = 3"));
            Assert.IsType<WhereBetween>(and.Left);
            Assert.IsType<WhereCompare>(and.Right);
        }

        [Theory]
        [InlineData("rodzaj = 'm", 9)]          // niezamknięty tekst
        [InlineData("rodzaj = ", 9)]            // brak wartości na końcu
        [InlineData("rodzaj 'm'", 7)]           // brak operatora
        [InlineData("(rodzaj = 'm'", 13)]       // brak nawiasu
        [InlineData("rodzaj = 'm')", 12)]       // nadmiarowy nawias
        [InlineData("rodzaj = NULL", 9)]        // porównanie z NULL
        [InlineData("rodzaj IN 'm'", 10)]       // IN bez nawiasu
        [InlineData("rodzaj = 'm' ; x", 13)]    // nieznany znak
        [InlineData("a = 1,5", 5)]              // przecinek dziesiętny
        public void SyntaxErrors_HavePosition(string text, int position)
        {
            var error = Assert.Throws<WhereSyntaxException>(() => WhereParser.Parse(text));
            Assert.Equal(position, error.Position);
            Assert.StartsWith($"Znak {position + 1}: ", error.DisplayMessage);
        }
    }
}
