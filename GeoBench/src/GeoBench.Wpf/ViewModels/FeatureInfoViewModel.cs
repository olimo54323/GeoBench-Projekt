using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoBench.Core.Data;

namespace GeoBench.Wpf.ViewModels
{
    /// <summary>Węzeł drzewa okna informacji: tabela, obiekt albo pole.</summary>
    public abstract partial class FeatureNodeViewModel : ObservableObject
    {
        protected FeatureNodeViewModel(FeatureInfoViewModel owner)
        {
            Owner = owner;
        }

        protected FeatureInfoViewModel Owner { get; }

        [ObservableProperty]
        private bool _isExpanded = true;

        [ObservableProperty]
        private bool _isSelected;

        /// <summary>Obiekty do podświetlenia na mapie po wybraniu węzła.</summary>
        public abstract IReadOnlyList<FeatureDetails> Records { get; }
    }

    /// <summary>Tabela (warstwa) ze znalezionymi obiektami.</summary>
    public sealed partial class FeatureTableNodeViewModel : FeatureNodeViewModel
    {
        public FeatureTableNodeViewModel(FeatureInfoViewModel owner, IdentifyResult result) : base(owner)
        {
            Table = result.Table;
            Header = result.Truncated ? $"{result.Table} ({result.Records.Count}+)" : $"{result.Table} ({result.Records.Count})";
            // Wiele obiektów rozwiniętych naraz to długa lista pól; rozwinięty jest tylko pojedynczy obiekt
            var expandObjects = result.Records.Count == 1;
            Objects = result.Records.Select(r => new FeatureObjectNodeViewModel(owner, r, expandObjects)).ToList();
            Records = result.Records;
        }

        public string Table { get; }

        public string Header { get; }

        public IReadOnlyList<FeatureObjectNodeViewModel> Objects { get; }

        public override IReadOnlyList<FeatureDetails> Records { get; }

        [RelayCommand]
        private void CopyTable() => Owner.Copy(FeatureAttributes.CopyTable(Records), $"tabelę {Table} ({Records.Count} obiektów)");
    }

    /// <summary>Obiekt z polami.</summary>
    public sealed partial class FeatureObjectNodeViewModel : FeatureNodeViewModel
    {
        public FeatureObjectNodeViewModel(FeatureInfoViewModel owner, FeatureDetails record, bool expanded) : base(owner)
        {
            Record = record;
            IsExpanded = expanded;
            Fields = record.Fields.Select(f => new FeatureFieldNodeViewModel(owner, this, f)).ToList();
        }

        public FeatureDetails Record { get; }

        public string Header => Record.Id;

        public IReadOnlyList<FeatureFieldNodeViewModel> Fields { get; }

        public override IReadOnlyList<FeatureDetails> Records => [Record];

        public bool HasGeometry => Record.Geometry != null;

        [RelayCommand]
        private void CopyObject() => Owner.Copy(FeatureAttributes.CopyRecord(Record), $"obiekt {Record.Id}");

        [RelayCommand]
        private void CopyWkt()
        {
            if (Record.Geometry != null)
                Owner.Copy(Record.Geometry.AsText(), $"geometrię {Record.Id} (WKT, EPSG:{Record.Geometry.SRID})");
        }
    }

    /// <summary>Pole obiektu: nazwa kolumny i wartość.</summary>
    public sealed partial class FeatureFieldNodeViewModel : FeatureNodeViewModel
    {
        private readonly FeatureObjectNodeViewModel _parent;

        public FeatureFieldNodeViewModel(FeatureInfoViewModel owner, FeatureObjectNodeViewModel parent, FeatureField field) : base(owner)
        {
            _parent = parent;
            Field = field;
        }

        public FeatureField Field { get; }

        public string Name => Field.Name;

        public string Value => Field.Value;

        public override IReadOnlyList<FeatureDetails> Records => _parent.Records;

        [RelayCommand]
        private void CopyField() => Owner.Copy(FeatureAttributes.CopyField(Field), $"pole {Field.Name}");

        [RelayCommand]
        private void CopyValue() => Owner.Copy(Field.Value, $"wartość pola {Field.Name}");

        [RelayCommand]
        private void CopyObject() => _parent.CopyObjectCommand.Execute(null);
    }

    /// <summary>
    /// Niemodalne okno informacji o obiektach wskazanych prawym przyciskiem na mapie: drzewo tabela → obiekt → pole.
    /// Kolejne wskazanie odświeża to samo okno. Wybrany węzeł jest podświetlany na mapie.
    /// </summary>
    public sealed partial class FeatureInfoViewModel : ObservableObject
    {
        private readonly Action<IReadOnlyList<FeatureDetails>> _highlight;
        private IReadOnlyList<FeatureDetails> _all = [];

        public FeatureInfoViewModel(Action<IReadOnlyList<FeatureDetails>> highlight)
        {
            _highlight = highlight;
        }

        public ObservableCollection<FeatureTableNodeViewModel> Tables { get; } = [];

        /// <summary>Skąd pochodzą dane: baza i wskazany punkt.</summary>
        [ObservableProperty]
        private string _location = "";

        [ObservableProperty]
        private string _status = "";

        [ObservableProperty]
        private bool _isSearching;

        /// <summary>Początek wyszukiwania w nowym punkcie.</summary>
        public void BeginSearch(string location)
        {
            Location = location;
            IsSearching = true;
            Status = "Wyszukiwanie obiektów…";
        }

        public void SetResults(IReadOnlyList<IdentifyResult> results)
        {
            IsSearching = false;
            Tables.Clear();
            foreach (var result in results.Where(r => r.Records.Count > 0))
                Tables.Add(new FeatureTableNodeViewModel(this, result));
            _all = results.SelectMany(r => r.Records).ToList();

            var count = _all.Count;
            Status = count == 0
                ? "Brak obiektów w tym miejscu (szukane w widocznych warstwach)."
                : $"Znaleziono obiektów: {count}. PPM na węźle: kopiowanie.";
            _highlight(_all);
        }

        public void SetError(string message)
        {
            IsSearching = false;
            Tables.Clear();
            _all = [];
            Status = $"Błąd: {message}";
            _highlight(_all);
        }

        /// <summary>Wywoływane przez okno po zmianie zaznaczenia w drzewie; brak zaznaczenia podświetla wszystkie.</summary>
        public void OnSelectedNodeChanged(FeatureNodeViewModel? node) => _highlight(node?.Records ?? _all);

        internal void Copy(string text, string what)
        {
            try
            {
                Clipboard.SetText(text);
                Status = $"Skopiowano {what}.";
            }
            catch (Exception ex) when (ex is ExternalException or ArgumentException)
            {
                // Schowek bywa chwilowo zajęty przez inny program
                Status = $"Nie skopiowano ({ex.Message}). Spróbuj ponownie.";
            }
        }
    }
}
