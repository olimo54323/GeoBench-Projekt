using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace GeoBench.Core.Data.Filters
{
    /// <summary>
    /// Warunek WHERE zamieniony na wyrażenie LINQ, które EF tłumaczy tak samo dla obu baz.
    /// NULL działa jak w SQL: porównanie, IN, LIKE i BETWEEN z NULL nie są prawdą ani fałszem, więc
    /// <c>rodzaj != 'm'</c> i <c>NOT (rodzaj = 'm')</c> pomijają budynki bez rodzaju (w C# byłyby prawdą).
    /// Każdy węzeł daje dwa wyrażenia: kiedy jest prawdą i kiedy fałszem; NOT je zamienia.
    /// </summary>
    public static class WhereFilter
    {
        private static readonly MethodInfo LikeMethod = typeof(DbFunctionsExtensions).GetMethod(
            nameof(DbFunctionsExtensions.Like), [typeof(DbFunctions), typeof(string), typeof(string)])!;

        private static readonly MethodInfo InMemoryLikeMethod = typeof(WhereLikeMatcher).GetMethod(nameof(WhereLikeMatcher.IsMatch))!;

        private static readonly MethodInfo CompareMethod = typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!;

        private static readonly string[] DateTimeFormats = ["yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"];

        /// <summary>Wyrażenie filtra; null dla pustego warunku. Błędy składni i typów jako WhereSyntaxException.</summary>
        /// <param name="inMemory">LIKE jako dopasowanie w pamięci zamiast EF.Functions.Like (testy, dane w pamięci).</param>
        public static Expression<Func<TEntity, bool>>? Compile<TEntity>(string? text, WhereSchema schema, bool inMemory = false)
        {
            if (schema.EntityType != typeof(TEntity))
                throw new ArgumentException($"Schemat tabeli {schema.Table} nie dotyczy typu {typeof(TEntity).Name}.", nameof(schema));
            return (Expression<Func<TEntity, bool>>?)Compile(text, schema, inMemory);
        }

        public static LambdaExpression? Compile(string? text, WhereSchema schema, bool inMemory = false)
        {
            var node = WhereParser.Parse(text);
            if (node == null)
                return null;
            var parameter = Expression.Parameter(schema.EntityType, "x");
            var (isTrue, _) = new Builder(schema, parameter, inMemory).Build(node);
            return Expression.Lambda(typeof(Func<,>).MakeGenericType(schema.EntityType, typeof(bool)), isTrue, parameter);
        }

        /// <summary>Komunikat błędu warunku albo null, gdy warunek jest poprawny (także pusty).</summary>
        public static WhereSyntaxException? Validate(string? text, WhereSchema schema)
        {
            try
            {
                Compile(text, schema);
                return null;
            }
            catch (WhereSyntaxException ex)
            {
                return ex;
            }
        }

        private readonly record struct Truth(Expression True, Expression False)
        {
            public Truth Negate() => new(False, True);
        }

        private sealed class Builder
        {
            private readonly WhereSchema _schema;
            private readonly ParameterExpression _parameter;
            private readonly bool _inMemory;

            public Builder(WhereSchema schema, ParameterExpression parameter, bool inMemory)
            {
                _schema = schema;
                _parameter = parameter;
                _inMemory = inMemory;
            }

            public Truth Build(WhereNode node) => node switch
            {
                WhereAnd and => And(Build(and.Left), Build(and.Right)),
                WhereOr or => Or(Build(or.Left), Build(or.Right)),
                WhereNot not => Build(not.Operand).Negate(),
                WhereCompare compare => Compare(compare),
                WhereIn @in => In(@in),
                WhereLike like => Like(like),
                WhereBetween between => Between(between),
                WhereIsNull isNull => IsNull(isNull),
                _ => throw new NotSupportedException(node.GetType().Name)
            };

            private static Truth And(Truth left, Truth right) =>
                new(Expression.AndAlso(left.True, right.True), Expression.OrElse(left.False, right.False));

            private static Truth Or(Truth left, Truth right) =>
                new(Expression.OrElse(left.True, right.True), Expression.AndAlso(left.False, right.False));

            private (MemberExpression Member, WhereColumn Column) Column(WhereColumnRef reference)
            {
                var column = _schema.Find(reference.Name)
                    ?? throw new WhereSyntaxException($"Nieznana kolumna „{reference.Name}” w tabeli {_schema.Table}.",
                        reference.Position, reference.Length);
                return (Expression.Property(_parameter, column.Property), column);
            }

            /// <summary>Predykat znany (prawda/fałsz) tylko dla wartości różnej od NULL.</summary>
            private static Truth Known(MemberExpression member, WhereColumn column, Expression predicate)
            {
                if (!column.IsNullable)
                    return new Truth(predicate, Expression.Not(predicate));
                var notNull = Expression.NotEqual(member, Expression.Constant(null, member.Type));
                return new Truth(Expression.AndAlso(notNull, predicate), Expression.AndAlso(notNull, Expression.Not(predicate)));
            }

            private Truth Compare(WhereCompare node)
            {
                var (member, column) = Column(node.Column);
                if (column.Kind == WhereColumnKind.Enum && node.Operator is not (WhereComparison.Equal or WhereComparison.NotEqual))
                    throw OnlyEquality(node.Column, column);
                var value = Value(node.Value, column, member.Type);
                return Known(member, column, Binary(node.Operator, member, value, column));
            }

            private Truth In(WhereIn node)
            {
                var (member, column) = Column(node.Column);
                var listType = typeof(List<>).MakeGenericType(member.Type);
                var list = (IList)Activator.CreateInstance(listType)!;
                foreach (var literal in node.Values)
                    list.Add(Value(literal, column, member.Type).Value);
                var contains = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [member.Type],
                    Expression.Constant(list, listType), member);
                var truth = Known(member, column, contains);
                return node.Negated ? truth.Negate() : truth;
            }

            private Truth Like(WhereLike node)
            {
                var (member, column) = Column(node.Column);
                if (column.Kind != WhereColumnKind.Text)
                    throw new WhereSyntaxException($"LIKE działa tylko na kolumnach tekstowych; {column.Name} to {column.KindName}.",
                        node.Column.Position, node.Column.Length);
                if (node.Pattern.Value is not string pattern)
                    throw new WhereSyntaxException("Wzorzec LIKE to tekst w apostrofach, np. 'Raków%'.", node.Pattern.Position, node.Pattern.Length);

                Expression like = _inMemory
                    ? Expression.Call(InMemoryLikeMethod, member, Expression.Constant(pattern))
                    : Expression.Call(LikeMethod, Expression.Property(null, typeof(EF), nameof(EF.Functions)), member, Expression.Constant(pattern));
                var truth = Known(member, column, like);
                return node.Negated ? truth.Negate() : truth;
            }

            private Truth Between(WhereBetween node)
            {
                var (member, column) = Column(node.Column);
                if (column.Kind == WhereColumnKind.Enum)
                    throw OnlyEquality(node.Column, column);
                var low = Value(node.Low, column, member.Type);
                var high = Value(node.High, column, member.Type);
                var predicate = Expression.AndAlso(
                    Binary(WhereComparison.GreaterOrEqual, member, low, column),
                    Binary(WhereComparison.LessOrEqual, member, high, column));
                var truth = Known(member, column, predicate);
                return node.Negated ? truth.Negate() : truth;
            }

            private Truth IsNull(WhereIsNull node)
            {
                var (member, column) = Column(node.Column);
                Truth truth = column.IsNullable
                    ? new Truth(Expression.Equal(member, Expression.Constant(null, member.Type)),
                        Expression.NotEqual(member, Expression.Constant(null, member.Type)))
                    : new Truth(Expression.Constant(false), Expression.Constant(true));
                return node.Negated ? truth.Negate() : truth;
            }

            private static Expression Binary(WhereComparison comparison, Expression member, ConstantExpression value, WhereColumn column)
            {
                var type = comparison switch
                {
                    WhereComparison.Equal => ExpressionType.Equal,
                    WhereComparison.NotEqual => ExpressionType.NotEqual,
                    WhereComparison.Less => ExpressionType.LessThan,
                    WhereComparison.LessOrEqual => ExpressionType.LessThanOrEqual,
                    WhereComparison.Greater => ExpressionType.GreaterThan,
                    _ => ExpressionType.GreaterThanOrEqual
                };
                // Teksty nie mają operatora < w C#; string.Compare(a, b) < 0 EF tłumaczy na a < b
                if (column.Kind == WhereColumnKind.Text && type is not (ExpressionType.Equal or ExpressionType.NotEqual))
                    return Expression.MakeBinary(type, Expression.Call(CompareMethod, member, value), Expression.Constant(0));
                // Wyliczenia nie mają operatora ==; porównanie przez typ bazowy, jak robi to kompilator C#
                if (column.Kind == WhereColumnKind.Enum)
                {
                    var underlying = Enum.GetUnderlyingType(column.ValueType);
                    var target = member.Type == column.ValueType ? underlying : typeof(Nullable<>).MakeGenericType(underlying);
                    return Expression.MakeBinary(type, Expression.Convert(member, target),
                        Expression.Constant(Convert.ChangeType(value.Value, underlying, CultureInfo.InvariantCulture), target));
                }
                return Expression.MakeBinary(type, member, value);
            }

            private static WhereSyntaxException OnlyEquality(WhereColumnRef reference, WhereColumn column) =>
                new($"Kolumna {column.Name} ({column.KindName}) obsługuje tylko =, !=, IN, NOT IN i IS NULL.",
                    reference.Position, reference.Length);

            /// <summary>Stała warunku zamieniona na typ kolumny.</summary>
            private static ConstantExpression Value(WhereLiteral literal, WhereColumn column, Type memberType)
            {
                WhereSyntaxException Error(string message) => new(message, literal.Position, literal.Length);

                object value;
                switch (column.Kind)
                {
                    case WhereColumnKind.Text:
                        value = literal.Value as string
                            ?? throw Error($"Kolumna {column.Name} jest tekstowa: wartość w apostrofach, np. '{literal.Value}'.");
                        break;

                    case WhereColumnKind.Integer:
                        if (literal.Value is not decimal integer)
                            throw Error($"Kolumna {column.Name} to liczba całkowita: wartość bez apostrofów.");
                        if (integer != decimal.Truncate(integer))
                            throw Error($"Kolumna {column.Name} to liczba całkowita, a {integer.ToString(CultureInfo.InvariantCulture)} ma część ułamkową.");
                        try
                        {
                            value = Convert.ChangeType(integer, column.ValueType, CultureInfo.InvariantCulture);
                        }
                        catch (OverflowException)
                        {
                            throw Error($"Liczba jest za duża dla kolumny {column.Name}.");
                        }
                        break;

                    case WhereColumnKind.Decimal:
                        if (literal.Value is not decimal number)
                            throw Error($"Kolumna {column.Name} jest liczbowa: wartość bez apostrofów, z kropką dziesiętną.");
                        value = Convert.ChangeType(number, column.ValueType, CultureInfo.InvariantCulture);
                        break;

                    case WhereColumnKind.Date:
                        if (literal.Value is not string dateText
                            || !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                            throw Error($"Kolumna {column.Name} to data: wartość w postaci 'rrrr-mm-dd'.");
                        value = date;
                        break;

                    case WhereColumnKind.DateTime:
                        if (literal.Value is not string timeText
                            || !DateTime.TryParseExact(timeText, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                            throw Error($"Kolumna {column.Name} to data i czas: wartość 'rrrr-mm-dd' albo 'rrrr-mm-dd gg:mm:ss'.");
                        // Npgsql przyjmuje dla timestamptz tylko DateTime w UTC
                        value = DateTime.SpecifyKind(time, column.IsUtc ? DateTimeKind.Utc : DateTimeKind.Unspecified);
                        break;

                    default:
                        if (literal.Value is not string name
                            || column.EnumValues.FirstOrDefault(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase)) is not { } match)
                            throw Error($"Nieznana wartość {literal.Value} kolumny {column.Name}. Ctrl+Spacja pokazuje dozwolone wartości.");
                        value = Enum.Parse(column.ValueType, match);
                        break;
                }
                return Expression.Constant(value, memberType);
            }
        }
    }

    /// <summary>Dopasowanie LIKE w pamięci (% – dowolny ciąg, _ – jeden znak), z rozróżnianiem wielkości liter jak w PostgreSQL.</summary>
    public static class WhereLikeMatcher
    {
        public static bool IsMatch(string? value, string pattern)
        {
            if (value == null)
                return false;
            var regex = new StringBuilder("^");
            foreach (var c in pattern)
            {
                regex.Append(c switch
                {
                    '%' => ".*",
                    '_' => ".",
                    _ => Regex.Escape(c.ToString())
                });
            }
            regex.Append('$');
            return Regex.IsMatch(value, regex.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant);
        }
    }
}
