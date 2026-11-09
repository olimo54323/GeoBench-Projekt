namespace GeoBench.Core.Data.Filters
{
    public enum WhereCompletionKind
    {
        None,
        Column,
        Operator,
        Value,
        Keyword
    }

    /// <summary>Podpowiedź: tekst na liście, tekst wstawiany w miejsce bieżącego słowa i opis (np. typ kolumny).</summary>
    public sealed record WhereCompletionItem(string Display, string Insert, string? Detail = null);

    /// <summary>
    /// Co pasuje w miejscu kursora. Wstawienie zastępuje tekst od ReplaceStart do kursora (zaczęte słowo
    /// albo zaczęty tekst w apostrofach). Dla Kind = Value wartości z bazy dokłada wywołujący (Column mówi, której kolumny).
    /// </summary>
    public sealed record WhereCompletionContext(
        WhereCompletionKind Kind, int ReplaceStart, string Prefix, WhereColumn? Column, IReadOnlyList<WhereCompletionItem> Items)
    {
        /// <summary>Pozycje pasujące do zaczętego słowa: najpierw zaczynające się od niego, potem zawierające.</summary>
        public IReadOnlyList<WhereCompletionItem> Filter(IEnumerable<WhereCompletionItem> items)
        {
            if (Prefix.Length == 0)
                return items.ToList();
            var list = items.ToList();
            return list.Where(i => i.Display.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                .Concat(list.Where(i => !i.Display.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                                        && i.Display.Contains(Prefix, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }

    /// <summary>
    /// Lekkie podpowiedzi do warunku WHERE: rozpoznaje, czy w miejscu kursora pasuje kolumna, operator, wartość
    /// czy łącznik (AND, OR, nawias). Działa na leksemach tekstu przed kursorem, bez pełnej poprawności składni.
    /// </summary>
    public static class WhereCompletion
    {
        private enum State
        {
            ExpectColumn,
            AfterColumn,
            AfterColumnNot,
            AfterIs,
            AfterIsNot,
            ExpectValue,
            ExpectInOpen,
            ExpectInValue,
            AfterInValue,
            ExpectBetweenLow,
            AfterBetweenLow,
            ExpectBetweenHigh,
            AfterPredicate,
            Invalid
        }

        public static WhereCompletionContext Analyze(string text, int caret, WhereSchema schema)
        {
            caret = Math.Clamp(caret, 0, text.Length);
            var tokens = WhereLexer.Tokenize(text[..caret], tolerant: true);
            var replaceStart = caret;
            var prefix = "";
            if (tokens.Count > 0)
            {
                var last = tokens[^1];
                var touchesCaret = last.Position + last.Length == caret;
                var isWord = last.Kind is WhereTokenKind.Identifier or WhereTokenKind.Keyword or WhereTokenKind.Number
                             || (last.Kind == WhereTokenKind.String && !last.Complete);
                if (touchesCaret && isWord)
                {
                    tokens.RemoveAt(tokens.Count - 1);
                    replaceStart = last.Position;
                    prefix = last.Kind == WhereTokenKind.String ? last.Text : text.Substring(last.Position, last.Length);
                }
            }

            var (state, column, depth) = Walk(tokens, schema);
            return state switch
            {
                State.ExpectColumn => Context(WhereCompletionKind.Column, null,
                    schema.Columns.Select(c => new WhereCompletionItem(c.Name, c.Name + " ", c.KindName))
                        .Append(new WhereCompletionItem("NOT", "NOT ", "zaprzeczenie"))
                        .Append(new WhereCompletionItem("(", "(", "nawias"))),
                State.AfterColumn when column != null => Context(WhereCompletionKind.Operator, column, Operators(column)),
                State.AfterColumnNot when column != null => Context(WhereCompletionKind.Operator, column, NegatedOperators(column)),
                State.AfterIs => Context(WhereCompletionKind.Keyword, column, [Item("NULL"), Item("NOT NULL")]),
                State.AfterIsNot => Context(WhereCompletionKind.Keyword, column, [Item("NULL")]),
                State.ExpectInOpen => Context(WhereCompletionKind.Keyword, column, [new WhereCompletionItem("(", "(")]),
                State.ExpectValue or State.ExpectInValue or State.ExpectBetweenLow or State.ExpectBetweenHigh when column != null =>
                    Context(WhereCompletionKind.Value, column, column.EnumValues.Select(v => ValueItem(column, v))),
                State.AfterInValue => Context(WhereCompletionKind.Keyword, column,
                    [new WhereCompletionItem(",", ", "), new WhereCompletionItem(")", ") ")]),
                State.AfterBetweenLow => Context(WhereCompletionKind.Keyword, column, [Item("AND")]),
                State.AfterPredicate => Context(WhereCompletionKind.Keyword, null,
                    depth > 0
                        ? new[] { Item("AND"), Item("OR"), new WhereCompletionItem(")", ") ") }
                        : new[] { Item("AND"), Item("OR") }),
                _ => Context(WhereCompletionKind.None, null, [])
            };

            WhereCompletionContext Context(WhereCompletionKind kind, WhereColumn? forColumn, IEnumerable<WhereCompletionItem> items) =>
                new(kind, replaceStart, prefix, forColumn, items.ToList());
        }

        /// <summary>Podpowiedź wartości kolumny: teksty, daty i wyliczenia w apostrofach, liczby bez.</summary>
        public static WhereCompletionItem ValueItem(WhereColumn column, string value)
        {
            var literal = column.Kind is WhereColumnKind.Integer or WhereColumnKind.Decimal ? value : Quote(value);
            return new WhereCompletionItem(value, literal + " ");
        }

        /// <summary>Tekst jako literał SQL: w apostrofach, apostrof podwojony.</summary>
        public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

        private static WhereCompletionItem Item(string keyword) => new(keyword, keyword + " ");

        private static IEnumerable<WhereCompletionItem> Operators(WhereColumn column)
        {
            yield return new WhereCompletionItem("=", "= ");
            yield return new WhereCompletionItem("!=", "!= ");
            if (column.Kind != WhereColumnKind.Enum)
            {
                yield return new WhereCompletionItem("<", "< ");
                yield return new WhereCompletionItem("<=", "<= ");
                yield return new WhereCompletionItem(">", "> ");
                yield return new WhereCompletionItem(">=", ">= ");
            }
            yield return new WhereCompletionItem("IN", "IN (", "lista wartości");
            yield return new WhereCompletionItem("NOT IN", "NOT IN (", "poza listą");
            if (column.Kind == WhereColumnKind.Text)
            {
                yield return new WhereCompletionItem("LIKE", "LIKE '", "% dowolny ciąg, _ jeden znak");
                yield return new WhereCompletionItem("NOT LIKE", "NOT LIKE '");
            }
            if (column.Kind != WhereColumnKind.Enum)
            {
                yield return new WhereCompletionItem("BETWEEN", "BETWEEN ", "od … AND do …");
                yield return new WhereCompletionItem("NOT BETWEEN", "NOT BETWEEN ");
            }
            yield return new WhereCompletionItem("IS NULL", "IS NULL ", "brak wartości");
            yield return new WhereCompletionItem("IS NOT NULL", "IS NOT NULL ");
        }

        private static IEnumerable<WhereCompletionItem> NegatedOperators(WhereColumn column)
        {
            yield return new WhereCompletionItem("IN", "IN (");
            if (column.Kind == WhereColumnKind.Text)
                yield return new WhereCompletionItem("LIKE", "LIKE '");
            if (column.Kind != WhereColumnKind.Enum)
                yield return new WhereCompletionItem("BETWEEN", "BETWEEN ");
        }

        private static (State State, WhereColumn? Column, int Depth) Walk(List<WhereToken> tokens, WhereSchema schema)
        {
            var state = State.ExpectColumn;
            WhereColumn? column = null;
            var depth = 0;
            foreach (var token in tokens)
            {
                var keyword = token.Kind == WhereTokenKind.Keyword ? token.Text : null;
                var isLiteral = token.Kind is WhereTokenKind.String or WhereTokenKind.Number;
                var isMinus = token is { Kind: WhereTokenKind.Operator, Text: "-" };
                state = state switch
                {
                    State.ExpectColumn when token.Kind == WhereTokenKind.LeftParen => Open(ref depth),
                    State.ExpectColumn when keyword == "NOT" => State.ExpectColumn,
                    State.ExpectColumn when token.Kind == WhereTokenKind.Identifier => SetColumn(schema.Find(token.Text), ref column),

                    State.AfterColumn when token.Kind == WhereTokenKind.Operator && !isMinus => State.ExpectValue,
                    State.AfterColumn when keyword == "IS" => State.AfterIs,
                    State.AfterColumn when keyword == "NOT" => State.AfterColumnNot,
                    State.AfterColumn or State.AfterColumnNot when keyword == "IN" => State.ExpectInOpen,
                    State.AfterColumn or State.AfterColumnNot when keyword == "LIKE" => State.ExpectValue,
                    State.AfterColumn or State.AfterColumnNot when keyword == "BETWEEN" => State.ExpectBetweenLow,

                    State.AfterIs when keyword == "NULL" => State.AfterPredicate,
                    State.AfterIs when keyword == "NOT" => State.AfterIsNot,
                    State.AfterIsNot when keyword == "NULL" => State.AfterPredicate,

                    State.ExpectValue or State.ExpectInValue or State.ExpectBetweenLow or State.ExpectBetweenHigh when isMinus => state,
                    State.ExpectValue or State.ExpectBetweenHigh when isLiteral => State.AfterPredicate,
                    State.ExpectInOpen when token.Kind == WhereTokenKind.LeftParen => State.ExpectInValue,
                    State.ExpectInValue when isLiteral => State.AfterInValue,
                    State.AfterInValue when token.Kind == WhereTokenKind.Comma => State.ExpectInValue,
                    State.AfterInValue when token.Kind == WhereTokenKind.RightParen => State.AfterPredicate,
                    State.ExpectBetweenLow when isLiteral => State.AfterBetweenLow,
                    State.AfterBetweenLow when keyword == "AND" => State.ExpectBetweenHigh,

                    State.AfterPredicate when keyword is "AND" or "OR" => State.ExpectColumn,
                    State.AfterPredicate when token.Kind == WhereTokenKind.RightParen && depth > 0 => Close(ref depth),

                    _ => State.Invalid
                };
                if (state == State.Invalid)
                    break;
            }
            return (state, column, depth);

            static State Open(ref int depth)
            {
                depth++;
                return State.ExpectColumn;
            }

            static State Close(ref int depth)
            {
                depth--;
                return State.AfterPredicate;
            }

            static State SetColumn(WhereColumn? found, ref WhereColumn? current)
            {
                current = found;
                return found == null ? State.Invalid : State.AfterColumn;
            }
        }
    }
}
