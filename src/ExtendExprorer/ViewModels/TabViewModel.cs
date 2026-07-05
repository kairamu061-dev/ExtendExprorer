using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ExtendExprorer.Models;
using ExtendExprorer.Services;

namespace ExtendExprorer.ViewModels;

public enum SortColumn { Name, Modified, Type, Size }

public partial class TabViewModel : ObservableObject
{
    private readonly IFileSystemService _fs;
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private List<EntryViewModel> _allEntries = new();

    [ObservableProperty]
    private string path = "";

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private SortColumn sortColumn = SortColumn.Name;

    [ObservableProperty]
    private bool sortAscending = true;

    public ObservableCollection<EntryViewModel> Entries { get; } = new();

    public bool CanGoBack => _historyIndex > 0;
    public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;
    public bool CanGoUp => System.IO.Path.GetDirectoryName(Path) is not null;

    public TabViewModel(IFileSystemService fs)
    {
        _fs = fs;
    }

    /// <summary>履歴に追加して移動する（ダブルクリック・アドレスバー・上へ）。</summary>
    public async Task NavigateAsync(string newPath)
    {
        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        }
        _history.Add(newPath);
        _historyIndex = _history.Count - 1;
        await LoadAsync(newPath);
    }

    public async Task GoBackAsync()
    {
        if (!CanGoBack)
        {
            return;
        }
        _historyIndex--;
        await LoadAsync(_history[_historyIndex]);
    }

    public async Task GoForwardAsync()
    {
        if (!CanGoForward)
        {
            return;
        }
        _historyIndex++;
        await LoadAsync(_history[_historyIndex]);
    }

    public async Task GoUpAsync()
    {
        if (System.IO.Path.GetDirectoryName(Path) is { } parent)
        {
            await NavigateAsync(parent);
        }
    }

    public void SetSort(SortColumn column)
    {
        if (SortColumn == column)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = column;
            SortAscending = true;
        }
        ApplySort();
    }

    private async Task LoadAsync(string targetPath)
    {
        Path = targetPath;
        IsLoading = true;
        ErrorMessage = null;
        // ソート状態はフォルダ単位（移動したら既定の名前昇順に戻す）
        SortColumn = SortColumn.Name;
        SortAscending = true;
        var result = await _fs.ListAsync(targetPath);
        IsLoading = false;

        switch (result)
        {
            case ListOk ok:
                _allEntries = ok.Entries.Select(e => new EntryViewModel(e)).ToList();
                ApplySort();
                break;
            case ListError err:
                _allEntries = new();
                Entries.Clear();
                ErrorMessage = err.Kind switch
                {
                    ListErrorKind.AccessDenied => "アクセスが拒否されました",
                    ListErrorKind.NotFound => "パスが見つかりません",
                    _ => $"読み込みに失敗しました: {err.Message}",
                };
                break;
        }

        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
    }

    private void ApplySort()
    {
        IEnumerable<EntryViewModel> sorted = SortColumn switch
        {
            SortColumn.Name => _allEntries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase),
            SortColumn.Modified => _allEntries.OrderBy(e => e.Model.Modified),
            SortColumn.Type => _allEntries.OrderBy(e => e.TypeLabel, StringComparer.CurrentCultureIgnoreCase),
            SortColumn.Size => _allEntries.OrderBy(e => e.Model.Size),
            _ => _allEntries,
        };
        if (!SortAscending)
        {
            sorted = sorted.Reverse();
        }
        // OrderBy は安定ソートなので、最後に適用するフォルダ優先が列ソート順を保つ
        sorted = sorted.OrderByDescending(e => e.IsDirectory);

        Entries.Clear();
        foreach (var entry in sorted)
        {
            Entries.Add(entry);
        }
    }
}
