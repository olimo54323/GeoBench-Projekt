using System.Globalization;
using System.Text;

namespace GeoBench.Core.Data.Filters
{
    public enum WhereTokenKind
    {
        Identifier,
        Keyword,
        String,
        Number,
        Operator,
        LeftParen,
        RightParen,
        Comma
    }

    /// <summary>
    /// Leksem warunku. Dla słów kluczowych Text jest wielkimi literami, dla tekstów to zawartość bez apostrofów.
    /// Complete = false tylko dla niezamkniętego tekstu w trybie tolerancyjnym (podpowiedzi).
    /// </summary>
    public sealed record WhereToken(WhereTokenKind Kind, string Text, int Position, int Length, bool Complete = true);

    /// <summary>Błąd składni lub znaczenia warunku; Position liczona od 0 (w komunikatach dla użytkownika od 1).</summary>
    public sealed class WhereSyntaxException : Exception
    {
        public WhereSyntaxException(string message, int position, int length = 1) : base(message)
        {
            Position = Math.Max(0, position);
            Length = Math.Max(1, length);
        }

        public int Position { get; }

        public int Length { get; }

        /// <summary>Komunikat z numerem znaku, np. „Znak 12: nieznana kolumna…”.</summary>
        public string DisplayMessage => $"Znak {Position + 1}: {Message}";
    }

    /// <summary>
    /// Podział tekstu warunku WHERE na leksemy. Składnia jak w SQL: teksty w apostrofach ('' to apostrof),
    /// liczby z kropką, słowa kluczowe bez względu na wielkość liter.
    /// </summary>
    public static class WhereLexer
    {
        public static readonly IReadOnlySet<string> Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AND", "OR", "NOT", "IN", "LIKE", "IS", "NULL", "BETWEEN"
        };

        /// <summary>
        /// Leksemy tekstu. W trybie tolerancyjnym (podpowiedzi przy pisaniu) niezamknięty tekst nie jest błędem,
        /// a na nieznanym znaku podział się kończy.
        /// </summary>
        public static List<WhereToken> Tokenize(string text, bool tolerant = false)
        {
            var tokens = new List<WhereToken>();
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                var start = i;
                if (char.IsLetter(c) || c == '_')
                {
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                        i++;
                    var word = text[start..i];
                    tokens.Add(Keywords.Contains(word)
                        ? new WhereToken(WhereTokenKind.Keyword, word.ToUpperInvariant(), start, i - start)
                        : new WhereToken(WhereTokenKind.Identifier, word, start, i - start));
                }
                else if (char.IsDigit(c) || (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
                {
                    while (i < text.Length && char.IsDigit(text[i]))
                        i++;
                    if (i < text.Length && text[i] == '.')
                    {
                        i++;
                        while (i < text.Length && char.IsDigit(text[i]))
                            i++;
                    }
                    if (i < text.Length && (char.IsLetter(text[i]) || text[i] == '_' || text[i] == '.'))
                    {
                        if (tolerant)
                            return tokens;
                        throw new WhereSyntaxException("Niepoprawna liczba (część dziesiętną oddziela kropka, np. 0.5).", start, i - start + 1);
                    }
                    tokens.Add(new WhereToken(WhereTokenKind.Number, text[start..i], start, i - start));
                }
                else if (c == '\'')
                {
                    var value = new StringBuilder();
                    var closed = false;
                    i++;
                    while (i < text.Length)
                    {
                        if (text[i] == '\'')
                        {
                            if (i + 1 < text.Length && text[i + 1] == '\'')
                            {
                                value.Append('\'');
                                i += 2;
                                continue;
                            }
                            i++;
                            closed = true;
                            break;
                        }
                        value.Append(text[i]);
                        i++;
                    }
                    if (!closed && !tolerant)
                        throw new WhereSyntaxException("Niezamknięty tekst: brakuje apostrofu ' na końcu.", start, i - start);
                    tokens.Add(new WhereToken(WhereTokenKind.String, value.ToString(), start, i - start, closed));
                }
                else if (c == '(' || c == ')' || c == ',')
                {
                    var kind = c == '(' ? WhereTokenKind.LeftParen : c == ')' ? WhereTokenKind.RightParen : WhereTokenKind.Comma;
                    tokens.Add(new WhereToken(kind, c.ToString(), start, 1));
                    i++;
                }
                else if (c is '=' or '<' or '>' or '!' or '-')
                {
                    var two = i + 1 < text.Length ? text.Substring(i, 2) : "";
                    var op = two is "<=" or ">=" or "<>" or "!=" ? two : c.ToString();
                    if (op == "!")
                    {
                        if (tolerant)
                            return tokens;
                        throw new WhereSyntaxException("Nieoczekiwany znak „!”: nierówność to != albo <>.", start);
                    }
                    tokens.Add(new WhereToken(WhereTokenKind.Operator, op, start, op.Length));
                    i += op.Length;
                }
                else
                {
                    if (tolerant)
                        return tokens;
                    throw new WhereSyntaxException($"Nieoczekiwany znak „{c}”.", start);
                }
            }
            return tokens;
        }
    }

    /// <summary>Węzeł drzewa składni warunku.</summary>
    public abstract record WhereNode;

    public sealed record WhereAnd(WhereNode Left, WhereNode Right) : WhereNode;

    public sealed record WhereOr(WhereNode Left, WhereNode Right) : WhereNode;

    public sealed record WhereNot(WhereNode Operand) : WhereNode;

    /// <summary>Kolumna z pozycją w tekście (do komunikatów o nieznanej kolumnie).</summary>
    public sealed record WhereColumnRef(string Name, int Position, int Length);

    /// <summary>Wartość: string albo decimal.</summary>
    public sealed record WhereLiteral(object Value, int Position, int Length);

    public enum WhereComparison
    {
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual
    }

    public sealed record WhereCompare(WhereColumnRef Column, WhereComparison Operator, WhereLiteral Value) : WhereNode;

    public sealed record WhereIn(WhereColumnRef Column, bool Negated, IReadOnlyList<WhereLiteral> Values) : WhereNode;

    public sealed record WhereLike(WhereColumnRef Column, bool Negated, WhereLiteral Pattern) : WhereNode;

    public sealed record WhereBetween(WhereColumnRef Column, bool Negated, WhereLiteral Low, WhereLiteral High) : WhereNode;

    public sealed record WhereIsNull(WhereColumnRef Column, bool Negated) : WhereNode;

    /// <summary>
    /// Parser zstępujący bazowej klauzuli WHERE:
    /// <code>
    /// warunek   := and (OR and)*
    /// and       := not (AND not)*
    /// not       := NOT not | '(' warunek ')' | predykat
    /// predykat  := kolumna ( (= | != | &lt;&gt; | &lt; | &lt;= | &gt; | &gt;=) wartość
    ///                      | [NOT] IN '(' wartość (',' wartość)* ')'
    ///                      | [NOT] LIKE wartość
    ///                      | [NOT] BETWEEN wartość AND wartość
    ///                      | IS [NOT] NULL )
    /// wartość   := 'tekst' | [-]liczba
    /// </code>
    /// Po lewej stronie zawsze kolumna, po prawej stałe (bez porównań kolumna–kolumna i funkcji).
    /// </summary>
    public static class WhereParser
    {
        /// <summary>Drzewo składni; null dla pustego warunku (filtr bez ograniczeń).</summary>
        public static WhereNode? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            var state = new State(text, WhereLexer.Tokenize(text));
            var node = state.ParseOr();
            if (state.Current is { } extra)
            {
                throw extra.Kind == WhereTokenKind.RightParen
                    ? new WhereSyntaxException("Nadmiarowy nawias zamykający ).", extra.Position)
                    : new WhereSyntaxException($"Nieoczekiwane „{Show(extra)}”: oczekiwano AND, OR albo końca warunku.", extra.Position, extra.Length);
            }
            return node;
        }

        private static string Show(WhereToken token) => token.Kind == WhereTokenKind.String ? $"'{token.Text}'" : token.Text;

        private sealed class State
        {
            private readonly string _text;
            private readonly List<WhereToken> _tokens;
            private int _index;

            public State(string text, List<WhereToken> tokens)
            {
                _text = text;
                _tokens = tokens;
            }

            public WhereToken? Current => _index < _tokens.Count ? _tokens[_index] : null;

            private bool IsKeyword(string keyword) => Current is { Kind: WhereTokenKind.Keyword } token && token.Text == keyword;

            private bool Is(WhereTokenKind kind) => Current?.Kind == kind;

            private WhereSyntaxException Error(string message) => Current is { } token
                ? new WhereSyntaxException(message, token.Position, token.Length)
                : new WhereSyntaxException(message, _text.Length);

            private string Found => Current is { } token ? $", a jest „{Show(token)}”" : "";

            public WhereNode ParseOr()
            {
                var left = ParseAnd();
                while (IsKeyword("OR"))
                {
                    _index++;
                    left = new WhereOr(left, ParseAnd());
                }
                return left;
            }

            private WhereNode ParseAnd()
            {
                var left = ParseNot();
                while (IsKeyword("AND"))
                {
                    _index++;
                    left = new WhereAnd(left, ParseNot());
                }
                return left;
            }

            private WhereNode ParseNot()
            {
                if (IsKeyword("NOT"))
                {
                    _index++;
                    return new WhereNot(ParseNot());
                }
                if (Is(WhereTokenKind.LeftParen))
                {
                    _index++;
                    var inner = ParseOr();
                    if (!Is(WhereTokenKind.RightParen))
                        throw Error($"Brakuje nawiasu zamykającego ){Found}.");
                    _index++;
                    return inner;
                }
                if (Current is not { Kind: WhereTokenKind.Identifier } name)
                    throw Error($"Oczekiwano nazwy kolumny{Found}.");
                _index++;
                return ParsePredicate(new WhereColumnRef(name.Text, name.Position, name.Length));
            }

            private WhereNode ParsePredicate(WhereColumnRef column)
            {
                if (Current is { Kind: WhereTokenKind.Operator } op && op.Text != "-")
                {
                    _index++;
                    var comparison = op.Text switch
                    {
                        "=" => WhereComparison.Equal,
                        "!=" or "<>" => WhereComparison.NotEqual,
                        "<" => WhereComparison.Less,
                        "<=" => WhereComparison.LessOrEqual,
                        ">" => WhereComparison.Greater,
                        _ => WhereComparison.GreaterOrEqual
                    };
                    return new WhereCompare(column, comparison, ParseLiteral());
                }

                if (IsKeyword("IS"))
                {
                    _index++;
                    var negatedNull = IsKeyword("NOT");
                    if (negatedNull)
                        _index++;
                    if (!IsKeyword("NULL"))
                        throw Error($"Po IS oczekiwano NULL albo NOT NULL{Found}.");
                    _index++;
                    return new WhereIsNull(column, negatedNull);
                }

                var negated = IsKeyword("NOT");
                if (negated)
                {
                    _index++;
                    if (!IsKeyword("IN") && !IsKeyword("LIKE") && !IsKeyword("BETWEEN"))
                        throw Error($"Po NOT oczekiwano IN, LIKE albo BETWEEN{Found}.");
                }

                if (IsKeyword("IN"))
                {
                    _index++;
                    if (!Is(WhereTokenKind.LeftParen))
                        throw Error($"Po IN oczekiwano nawiasu ( z listą wartości{Found}.");
                    _index++;
                    var values = new List<WhereLiteral> { ParseLiteral() };
                    while (Is(WhereTokenKind.Comma))
                    {
                        _index++;
                        values.Add(ParseLiteral());
                    }
                    if (!Is(WhereTokenKind.RightParen))
                        throw Error($"W liście IN oczekiwano przecinka albo nawiasu ){Found}.");
                    _index++;
                    return new WhereIn(column, negated, values);
                }

                if (IsKeyword("LIKE"))
                {
                    _index++;
                    return new WhereLike(column, negated, ParseLiteral());
                }

                if (IsKeyword("BETWEEN"))
                {
                    _index++;
                    var low = ParseLiteral();
                    if (!IsKeyword("AND"))
                        throw Error($"W BETWEEN oczekiwano AND między wartościami{Found}.");
                    _index++;
                    return new WhereBetween(column, negated, low, ParseLiteral());
                }

                throw Error($"Po kolumnie {column.Name} oczekiwano operatora (=, !=, <, <=, >, >=, IN, LIKE, BETWEEN, IS NULL){Found}.");
            }

            private WhereLiteral ParseLiteral()
            {
                var start = Current;
                var negative = Current is { Kind: WhereTokenKind.Operator, Text: "-" };
                if (negative)
                    _index++;

                switch (Current)
                {
                    case { Kind: WhereTokenKind.String } text when !negative:
                        _index++;
                        return new WhereLiteral(text.Text, text.Position, text.Length);
                    case { Kind: WhereTokenKind.Number } number:
                        _index++;
                        if (!decimal.TryParse(number.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                            throw new WhereSyntaxException("Liczba jest za duża.", number.Position, number.Length);
                        var position = start!.Position;
                        return new WhereLiteral(negative ? -value : value, position, number.Position + number.Length - position);
                    case { Kind: WhereTokenKind.Keyword, Text: "NULL" }:
                        throw Error("NULL sprawdza się przez IS NULL albo IS NOT NULL (porównanie z NULL nigdy nie jest prawdą).");
                    default:
                        throw Error(negative
                            ? $"Po minusie oczekiwano liczby{Found}."
                            : $"Oczekiwano wartości: tekstu w apostrofach albo liczby{Found}.");
                }
            }
        }
    }
}
