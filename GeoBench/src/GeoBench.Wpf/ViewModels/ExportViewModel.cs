using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoBench.Core.Data;
using GeoBench.Core.Export;
using GeoBench.Core.Reporting;
using GeoBench.Wpf.Services;

namespace GeoBench.Wpf.ViewModels
{
    /// <summary>Baza na liście zakładki Eksport.</summary>
    public sealed record ExportEngineItem(DatabaseEngine Engine, string Name);

    /// <summary>Format na liście zakładki Eksport.</summary>
    public sealed record ExportFormatItem(ExportFormat Format, string Name);

    /// <summary>Kolumna tabeli wyników z zaznaczeniem do eksportu.</summary>
    public sealed partial class ExportColumnItemViewModel : ObservableObject
    {
        private readonly Action _changed;

        public ExportColumnItemViewModel(ResultColumn column, Action changed)
        {
            Column = column;
            _changed = changed;
        }

        public ResultColumn Column { get; }

        public string Name => Column.Name;

        [ObservableProperty]
        private bool _isSelected = true;

        partial void OnIsSelectedChanged(bool value) => _changed();
    }

    /// <summary>
    /// Zakładka Eksport: wybrany wariant z tabeli wyników scenariusza do XLSX, CSV albo XML,
    /// z wyborem i kolejnością kolumn. Warianty wczytywane z bazy na żądanie.
    /// </summary>
    public sealed partial class ExportViewModel : ObservableObject
    {
        private readonly GeoBenchContextFactory _factory;
        private readonly IOperationHost _host;
        private readonly IDialogService _dialogs;

        public ExportViewModel(GeoBenchContextFactory factory, IOperationHost host, IDialogService dialogs)
        {
            _factory = factory;
            _host = host;
            _dialogs = dialogs;
            _selectedEngine = Engines[0];
            _selectedFormat = Formats[0];
            SelectedTable = Tables[0];
        }

        public IReadOnlyList<ExportEngineItem> Engines { get; } =
        [
            new(DatabaseEngine.SqlServer, "SQL Server"),
            new(DatabaseEngine.Postgres, "PostgreSQL + PostGIS")
        ];

        public IReadOnlyList<ResultTable> Tables => ResultTableCatalog.All;

        public IReadOnlyList<ExportFormatItem> Formats { get; } =
        [
            new(ExportFormat.Xlsx, "Excel (.xlsx)"),
            new(ExportFormat.Csv, "CSV rozdzielany średnikami (.csv)"),
            new(ExportFormat.Xml, "XML (.xml)")
        ];

        /// <summary>Domyślny katalog plików eksportu.</summary>
        public string ExportDirectory { get; } = Path.Combine(BenchmarkReportWriter.ResultsRoot(), "eksport");

        public ObservableCollection<ExportVariant> Variants { get; } = [];

        public ObservableCollection<ExportColumnItemViewModel> Columns { get; } = [];

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
        private ExportEngineItem _selectedEngine;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ExportCommand), nameof(LoadVariantsCommand))]
        private ResultTable? _selectedTable;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
        private ExportVariant? _selectedVariant;

        [ObservableProperty]
        private ExportFormatItem _selectedFormat;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(MoveUpCommand), nameof(MoveDownCommand))]
        private ExportColumnItemViewModel? _selectedColumn;

        [ObservableProperty]
        private string? _variantsInfo;

        // Warianty zależą od bazy i tabeli: po zmianie lista jest pusta do ponownego wczytania
        partial void OnSelectedEngineChanged(ExportEngineItem value) => ClearVariants();

        partial void OnSelectedTableChanged(ResultTable? value)
        {
            ClearVariants();
            Columns.Clear();
            if (value == null)
                return;
            // Kolumny z modelu EF (bez połączenia z bazą); ten sam model w obu kontekstach
            using var context = _factory.Create(SelectedEngine.Engine);
            foreach (var column in ResultTableCatalog.Columns(context, value))
                Columns.Add(new ExportColumnItemViewModel(column, () => ExportCommand.NotifyCanExecuteChanged()));
        }

        private void ClearVariants()
        {
            Variants.Clear();
            SelectedVariant = null;
            VariantsInfo = "Wczytaj warianty z bazy.";
        }

        private bool CanLoadVariants() => !_host.IsBusy && SelectedTable != null;

        [RelayCommand(CanExecute = nameof(CanLoadVariants))]
        private async Task LoadVariantsAsync()
        {
            var table = SelectedTable!;
            var engine = SelectedEngine;
            IReadOnlyList<ExportVariant> variants = [];
            var ok = await _host.RunAsync($"Eksport: warianty w tabeli ({engine.Name})", async cancellationToken =>
            {
                await using var context = _factory.Create(engine.Engine);
                variants = await new ResultExportReader(context).VariantsAsync(table, cancellationToken);
            });
            if (!ok)
                return;

            Variants.Clear();
            foreach (var variant in variants)
                Variants.Add(variant);
            SelectedVariant = Variants.FirstOrDefault();
            VariantsInfo = Variants.Count == 0
                ? "Tabela nie ma zapisanych wyników (uruchom pomiar z zapisem tabeli wyników)."
                : $"Wariantów: {Variants.Count}.";
        }

        private bool CanMoveUp() => SelectedColumn != null && Columns.IndexOf(SelectedColumn) > 0;

        [RelayCommand(CanExecute = nameof(CanMoveUp))]
        private void MoveUp() => Move(-1);

        private bool CanMoveDown() => SelectedColumn != null && Columns.IndexOf(SelectedColumn) is var i && i >= 0 && i < Columns.Count - 1;

        [RelayCommand(CanExecute = nameof(CanMoveDown))]
        private void MoveDown() => Move(1);

        private void Move(int delta)
        {
            var column = SelectedColumn!;
            var index = Columns.IndexOf(column);
            Columns.Move(index, index + delta);
            SelectedColumn = column;
            MoveUpCommand.NotifyCanExecuteChanged();
            MoveDownCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void SelectAllColumns()
        {
            foreach (var column in Columns)
                column.IsSelected = true;
        }

        private bool CanExport() =>
            !_host.IsBusy && SelectedTable != null && SelectedVariant != null && Columns.Any(c => c.IsSelected);

        [RelayCommand(CanExecute = nameof(CanExport))]
        private async Task ExportAsync()
        {
            var table = SelectedTable!;
            var variant = SelectedVariant!;
            var engine = SelectedEngine;
            var format = SelectedFormat.Format;
            var columns = Columns.Where(c => c.IsSelected).Select(c => c.Column).ToList();

            string tableName;
            using (var model = _factory.Create(engine.Engine))
                tableName = ResultTableCatalog.TableName(model, table);
            Directory.CreateDirectory(ExportDirectory);
            var engineShort = engine.Engine == DatabaseEngine.SqlServer ? "sqlserver" : "postgres";
            var path = _dialogs.SaveFile("Eksport wyników", ResultWriters.DialogFilter(format),
                $"{tableName}_{variant.Variant}_{engineShort}{ResultWriters.Extension(format)}", ExportDirectory);
            if (path == null)
                return;

            await _host.RunAsync($"Eksport: {tableName}, wariant {variant.Variant} ({engine.Name})", async cancellationToken =>
            {
                await using var context = _factory.Create(engine.Engine);
                var data = await new ResultExportReader(context).ReadAsync(table, variant.Variant, columns, cancellationToken);
                var info = new ExportInfo(tableName, variant.Variant, engine.Name, DateTime.Now);
                // Zapis poza wątkiem interfejsu (XLSX z dziesiątkami tysięcy wierszy trwa chwilę)
                await Task.Run(() =>
                {
                    using var stream = File.Create(path);
                    ResultWriters.Write(format, data, info, stream);
                }, cancellationToken);
                _host.AddLog($"Wyeksportowano {data.Rows.Count:N0} wierszy, {columns.Count} kolumn: {path}");
            });
        }

        [RelayCommand]
        private void OpenExportDirectory() => _dialogs.OpenFolder(ExportDirectory);

        /// <summary>Odświeżenie dostępności poleceń po zmianie zajętości okna.</summary>
        public void NotifyBusyChanged()
        {
            LoadVariantsCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
        }
    }
}
