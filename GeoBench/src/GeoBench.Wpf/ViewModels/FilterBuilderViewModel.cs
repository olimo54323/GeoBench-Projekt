using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoBench.Core.Data;
using GeoBench.Core.Data.Filters;
using GeoBench.Core.Styles;
using GeoBench.Wpf.Services;

namespace GeoBench.Wpf.ViewModels
{
    /// <summary>Warstwa do wyboru w builderze: tabela w bazie i nazwa z panelu.</summary>
    public sealed record FilterTableOption(string Table, string Title);

    /// <summary>Kolumna na liście buildera: nazwa, typ i opis do podpowiedzi (typ, NULL, dozwolone wartości).</summary>
    public sealed record FilterColumnItem(string Name, string KindName, string Description)
    {
        public static FilterColumnItem From(WhereColumn column)
        {
            var description = $"{column.Name} – {column.KindName}, {(column.IsNullable ? "może być NULL" : "wymagana (bez NULL)")}";
            if (column.EnumValues.Count > 0)
                description += Environment.NewLine + "Wartości: " + string.Join(", ", column.EnumValues);
            return new FilterColumnItem(column.Name, column.KindName, description);
        }
    }

    /// <summary>Pozycja listy filtrów; Saved = null dla nowego, jeszcze niezapisanego filtra.</summary>
    public sealed partial class FilterListItem : ObservableObject
    {
        public FilterListItem(MapFilter? saved, string name)
        {
            Saved = saved;
            _name = name;
        }

        public MapFilter? Saved { get; set; }

        [ObservableProperty]
        private string _name;
    }

    /// <summary>
    /// Builder filtrów: lista filtrów warstwy, edycja nazwy, warunku WHERE (walidacja na bieżąco, podpowiedzi,
    /// sprawdzenie liczby wierszy w wybranej bazie) i stylu; zapis i usuwanie plików w katalogu filters.
    /// </summary>
    public sealed partial class FilterBuilderViewModel : ObservableObject
    {
        /// <summary>Kolory nowych filtrów: kolejny nieużyty w warstwie.</summary>
        private static readonly string[] Palette =
            ["#1F77B4", "#FF7F0E", "#2CA02C", "#9467BD", "#8C564B", "#E377C2", "#17BECF", "#BCBD22", "#D62728", "#7F7F7F"];

        private const int MaxSuggestedValues = 50;

        private readonly FilterStore _store;
        private readonly GeoBenchContextFactory _factory;
        private readonly IDialogService _dialogs;
        private readonly Dictionary<string, WhereSchema> _schemas = [];
        private readonly Dictionary<(string Table, string Column), IReadOnlyList<string>> _values = [];
        private readonly HashSet<(string Table, string Column)> _loadingValues = [];
        private FilterTableOption _selectedTable;
        private FilterListItem? _selectedFilter;
        private MapFilter? _working;
        private WhereSchema? _schema;
        private WhereCompletionContext? _completion;
        private int _completionCaret;
        private bool _loading;
        private bool _confirming;

        public FilterBuilderViewModel(FilterStore store, GeoBenchContextFactory factory, EngineOption engine,
            IReadOnlyList<FilterTableOption> tables, IDialogService dialogs, string table, string? filterName)
        {
            _store = store;
            _factory = factory;
            _dialogs = dialogs;
            Engine = engine;
            Tables = tables;
            _selectedTable = tables.FirstOrDefault(t => t.Table == table) ?? tables[0];
            LoadTable();
            SelectedFilter = Filters.FirstOrDefault(f => string.Equals(f.Name, filterName, StringComparison.OrdinalIgnoreCase))
                             ?? Filters.FirstOrDefault();
        }

        public EngineOption Engine { get; }

        public IReadOnlyList<FilterTableOption> Tables { get; }

        public ObservableCollection<FilterListItem> Filters { get; } = [];

        public FilterTableOption SelectedTable
        {
            get => _selectedTable;
            set
            {
                if (value == null || value == _selectedTable)
                    return;
                if (!ConfirmLeave())
                {
                    RevertSelection(nameof(SelectedTable));
                    return;
                }
                _selectedTable = value;
                OnPropertyChanged();
                LoadTable();
                SelectFilter(Filters.FirstOrDefault());
            }
        }

        public FilterListItem? SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                if (value == _selectedFilter)
                    return;
                if (!ConfirmLeave())
                {
                    RevertSelection(nameof(SelectedFilter));
                    return;
                }
                SelectFilter(value);
            }
        }

        public bool HasSelection => _working != null;

        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private string _where = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsStyleEditable))]
        private bool _useLayerStyle;

        public bool IsStyleEditable => !UseLayerStyle;

        [ObservableProperty]
        private StyleItemViewModel? _styleEditor;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        private bool _isDirty;

        [ObservableProperty]
        private string _validationText = "";

        [ObservableProperty]
        private bool _hasError;

        [ObservableProperty]
        private string _checkText = "";

        /// <summary>Kolumny wybranej warstwy do wstawiania w warunek (typ i NULL w podpowiedzi po najechaniu).</summary>
        [ObservableProperty]
        private IReadOnlyList<FilterColumnItem> _columns = [];

        public ObservableCollection<WhereCompletionItem> Suggestions { get; } = [];

        [ObservableProperty]
        private bool _isSuggestionsOpen;

        [ObservableProperty]
        private WhereCompletionItem? _selectedSuggestion;

        /// <summary>Wywoływane, gdy wartości do podpowiedzi doszły z bazy (widok odświeża listę dla bieżącego kursora).</summary>
        public event EventHandler? SuggestionsChanged;

        partial void OnNameChanged(string value) => MarkDirty();

        partial void OnWhereChanged(string value)
        {
            MarkDirty();
            Validate();
            CheckText = "";
        }

        partial void OnUseLayerStyleChanged(bool value) => MarkDirty();

        private void MarkDirty()
        {
            if (!_loading && _working != null)
                IsDirty = true;
        }

        // Lista i wybór filtra

        private void LoadTable()
        {
            Filters.Clear();
            var errors = new List<string>();
            foreach (var filter in _store.Load(_selectedTable.Table, errors))
                Filters.Add(new FilterListItem(filter, filter.Name));
            CheckText = errors.Count == 0 ? "" : $"Pominięto uszkodzone pliki: {string.Join("; ", errors)}";

            _schema = Schema(_selectedTable.Table);
            Columns = _schema == null ? [] : _schema.Columns.Select(FilterColumnItem.From).ToList();
        }

        private WhereSchema? Schema(string table)
        {
            if (_schemas.TryGetValue(table, out var schema))
                return schema;
            try
            {
                // Sam model EF: kontekst nie łączy się z bazą
                using var context = _factory.Create(Engine.Engine);
                schema = WhereSchema.ForTable(context, table);
                _schemas[table] = schema;
                return schema;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                ValidationText = $"Nie można odczytać kolumn tabeli: {ex.Message}";
                HasError = true;
                return null;
            }
        }

        private void SelectFilter(FilterListItem? item)
        {
            _selectedFilter = item;
            OnPropertyChanged(nameof(SelectedFilter));

            _loading = true;
            _working = item == null ? null : (item.Saved?.Clone() ?? _working);
            Name = _working?.Name ?? "";
            Where = _working?.Where ?? "";
            UseLayerStyle = _working?.UseLayerStyle ?? false;
            var working = _working;
            var originalStyle = working?.Style.Clone();
            StyleEditor = working == null
                ? null
                : new StyleItemViewModel("", working.Name, working.Style, () => originalStyle!.Clone(), MarkDirty);
            _loading = false;

            IsDirty = item is { Saved: null };
            CheckText = "";
            IsSuggestionsOpen = false;
            Validate();
            OnPropertyChanged(nameof(HasSelection));
            DeleteCommand.NotifyCanExecuteChanged();
            CheckCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Przy niezapisanych zmianach pyta o zapis; false = zostajemy przy bieżącym filtrze.</summary>
        public bool ConfirmLeave()
        {
            // Usunięcie zaznaczonej pozycji zmienia zaznaczenie w ListBox, co wraca tu przez setter SelectedFilter
            if (_confirming || !IsDirty || _working == null)
                return true;
            _confirming = true;
            try
            {
                if (_dialogs.Confirm($"Zapisać zmiany w filtrze „{Name}”?", "Filtry"))
                    return TrySave();
                IsDirty = false;
                if (_selectedFilter is { Saved: null } unsaved)
                    Filters.Remove(unsaved);
                return true;
            }
            finally
            {
                _confirming = false;
            }
        }

        /// <summary>
        /// Przywraca zaznaczenie w kontrolce po odmowie zmiany. Powiadomienie musi przyjść po zakończeniu
        /// obsługi zaznaczenia przez ListBox/ComboBox, inaczej kontrolka go nie uwzględni.
        /// </summary>
        private void RevertSelection(string propertyName) =>
            Application.Current?.Dispatcher.InvokeAsync(() => OnPropertyChanged(propertyName));

        // Polecenia

        [RelayCommand]
        private void New()
        {
            if (!ConfirmLeave())
                return;
            var name = UniqueName("Nowy filtr");
            var used = Filters.Select(f => f.Saved?.Style.FillColor).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var color = Palette.FirstOrDefault(c => !used.Contains(c)) ?? Palette[Filters.Count % Palette.Length];
            _working = new MapFilter
            {
                Name = name,
                Table = _selectedTable.Table,
                // Strefy domyślnie w kolorach rodzajów; działki i budynki we własnym kolorze
                UseLayerStyle = _selectedTable.Table == "strefa_planistyczna",
                Style = new LayerStyle { FillColor = color, FillOpacity = 0.4, OutlineColor = color, OutlineWidth = 1.5 }
            };
            var item = new FilterListItem(null, name);
            Filters.Add(item);
            SelectFilter(item);
        }

        private string UniqueName(string baseName)
        {
            var name = baseName;
            for (var i = 2; Filters.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                            || _store.IsNameTaken(_selectedTable.Table, name); i++)
                name = $"{baseName} {i}";
            return name;
        }

        private bool CanSave() => IsDirty;

        [RelayCommand(CanExecute = nameof(CanSave))]
        private void Save() => TrySave();

        private bool TrySave()
        {
            if (_working == null || _selectedFilter == null)
                return true;
            if (HasError)
            {
                _dialogs.ShowError($"Warunek ma błąd i nie zostanie zapisany.\n\n{ValidationText}", "Filtry");
                return false;
            }

            var previousName = _selectedFilter.Saved?.Name;
            // Zapisywana jest kopia: edytor stylu dalej pracuje na _working.Style
            var filter = _working.Clone();
            filter.Name = Name.Trim();
            filter.Where = Where.Trim();
            filter.UseLayerStyle = UseLayerStyle;
            try
            {
                _store.Save(filter, previousName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                _dialogs.ShowError(ex.Message, "Zapis filtra");
                return false;
            }

            _working.Name = filter.Name;
            _working.Where = filter.Where;
            _working.UseLayerStyle = filter.UseLayerStyle;
            _selectedFilter.Saved = filter;
            _selectedFilter.Name = filter.Name;
            IsDirty = false;
            CheckText = $"Zapisano: {_store.FilePath(filter.Table, filter.Name)}";
            return true;
        }

        private bool CanDelete() => _selectedFilter != null;

        [RelayCommand(CanExecute = nameof(CanDelete))]
        private void Delete()
        {
            if (_selectedFilter is not { } item)
                return;
            if (item.Saved != null)
            {
                if (!_dialogs.Confirm($"Usunąć filtr „{item.Name}”?\nPlik {_store.FilePath(item.Saved.Table, item.Saved.Name)} zostanie usunięty z dysku.", "Usuwanie filtra"))
                    return;
                try
                {
                    _store.Delete(item.Saved.Table, item.Saved.Name);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _dialogs.ShowError(ex.Message, "Usuwanie filtra");
                    return;
                }
            }
            IsDirty = false;
            var index = Filters.IndexOf(item);
            Filters.Remove(item);
            SelectFilter(Filters.Count == 0 ? null : Filters[Math.Min(index, Filters.Count - 1)]);
        }

        private bool CanCheck() => _working != null && _schema != null;

        [RelayCommand(CanExecute = nameof(CanCheck))]
        private async Task CheckAsync()
        {
            if (_schema is not { } schema)
                return;
            var where = Where;
            CheckText = $"Sprawdzanie w {Engine.Name}…";
            try
            {
                var (matching, total) = await Task.Run(async () =>
                {
                    await using var context = _factory.Create(Engine.Engine);
                    return await FilterQuery.CountAsync(context, schema, where);
                });
                CheckText = $"{Engine.Name}: pasuje {matching:N0} z {total:N0} wierszy tabeli {schema.Table}.";
            }
            catch (WhereSyntaxException ex)
            {
                CheckText = ex.DisplayMessage;
            }
            catch (Exception ex)
            {
                CheckText = $"Błąd zapytania: {ex.GetBaseException().Message}";
            }
        }

        private void Validate()
        {
            if (_schema == null || _working == null)
                return;
            var error = WhereFilter.Validate(Where, _schema);
            HasError = error != null;
            ValidationText = error?.DisplayMessage
                ?? (string.IsNullOrWhiteSpace(Where) ? "Pusty warunek: filtr pokaże wszystkie obiekty warstwy." : "Warunek poprawny.");
        }

        // Podpowiedzi

        /// <summary>
        /// Podpowiedzi dla kursora. Bez jawnego żądania (Ctrl+Spacja) lista nie jest pokazywana, gdy nic nie pasuje
        /// albo jedyna pozycja to dokładnie wpisane słowo.
        /// </summary>
        public void UpdateSuggestions(int caret, bool explicitRequest)
        {
            if (_schema == null || _working == null)
            {
                IsSuggestionsOpen = false;
                return;
            }
            var context = WhereCompletion.Analyze(Where, caret, _schema);
            _completion = context;
            _completionCaret = caret;

            IEnumerable<WhereCompletionItem> items = context.Items;
            if (context is { Kind: WhereCompletionKind.Value, Column: { Kind: not WhereColumnKind.Enum } column })
                items = ValuesFor(column);
            var filtered = context.Filter(items);

            var pointless = filtered.Count == 0
                            || (filtered.Count == 1 && string.Equals(filtered[0].Display, context.Prefix, StringComparison.OrdinalIgnoreCase));
            if (!explicitRequest && pointless)
            {
                IsSuggestionsOpen = false;
                return;
            }
            Suggestions.Clear();
            foreach (var item in filtered)
                Suggestions.Add(item);
            SelectedSuggestion = Suggestions.FirstOrDefault();
            IsSuggestionsOpen = Suggestions.Count > 0;
        }

        /// <summary>Wstawia podpowiedź w miejsce zaczętego słowa; zwraca nową pozycję kursora.</summary>
        public int ApplySuggestion(WhereCompletionItem item)
        {
            IsSuggestionsOpen = false;
            if (_completion == null)
                return _completionCaret;
            var start = Math.Min(_completion.ReplaceStart, Where.Length);
            var end = Math.Clamp(_completionCaret, start, Where.Length);
            Where = Where[..start] + item.Insert + Where[end..];
            return start + item.Insert.Length;
        }

        /// <summary>
        /// Wstawia fragment z przycisku składni albo nazwę kolumny w miejsce zaznaczenia i zwraca pozycję kursora.
        /// Znak | w szablonie to miejsce kursora; zaznaczony tekst trafia właśnie tam (np. „(|)” bierze zaznaczenie w nawias).
        /// Spacje przed i po są dokładane, gdy fragment skleiłby się z sąsiednim tekstem.
        /// </summary>
        public int InsertSnippet(string template, int selectionStart, int selectionLength)
        {
            IsSuggestionsOpen = false;
            var text = Where;
            var start = Math.Clamp(selectionStart, 0, text.Length);
            var length = Math.Clamp(selectionLength, 0, text.Length - start);
            var selected = text.Substring(start, length);
            var before = text[..start];
            var after = text[(start + length)..];

            var marker = template.IndexOf('|');
            var body = marker < 0 ? template : template[..marker] + selected + template[(marker + 1)..];
            var lead = before.Length > 0 && !char.IsWhiteSpace(before[^1]) && before[^1] != '(' ? " " : "";
            var trail = after.Length == 0 || (!char.IsWhiteSpace(after[0]) && after[0] is not (')' or ',')) ? " " : "";

            Where = before + lead + body + trail + after;
            var bodyStart = start + lead.Length;
            return marker < 0 ? bodyStart + body.Length + trail.Length : bodyStart + marker + selected.Length;
        }

        public void MoveSuggestion(int delta)
        {
            if (Suggestions.Count == 0)
                return;
            var index = SelectedSuggestion == null ? -1 : Suggestions.IndexOf(SelectedSuggestion);
            SelectedSuggestion = Suggestions[Math.Clamp(index + delta, 0, Suggestions.Count - 1)];
        }

        /// <summary>Najczęstsze wartości kolumny z bazy (wczytywane w tle przy pierwszym użyciu).</summary>
        private IReadOnlyList<WhereCompletionItem> ValuesFor(WhereColumn column)
        {
            var key = (_schema!.Table, column.Name);
            if (_values.TryGetValue(key, out var values))
                return values.Select(v => WhereCompletion.ValueItem(column, v)).ToList();
            if (_loadingValues.Add(key))
                _ = LoadValuesAsync(_schema, column, key);
            return [];
        }

        private async Task LoadValuesAsync(WhereSchema schema, WhereColumn column, (string, string) key)
        {
            try
            {
                var values = await Task.Run(async () =>
                {
                    await using var context = _factory.Create(Engine.Engine);
                    return await FilterQuery.TopValuesAsync(context, schema, column, MaxSuggestedValues);
                });
                _values[key] = values;
                SuggestionsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // Bez wartości z bazy podpowiedzi kolumn i operatorów nadal działają
                _values[key] = [];
                CheckText = $"Nie wczytano wartości kolumny {column.Name}: {ex.GetBaseException().Message}";
            }
            finally
            {
                _loadingValues.Remove(key);
            }
        }
    }
}
