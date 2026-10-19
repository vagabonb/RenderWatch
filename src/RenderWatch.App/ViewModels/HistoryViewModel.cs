using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;
using RenderWatch.Core.History;
using RenderWatch.Core.Models;

namespace RenderWatch.App.ViewModels;

public class HistoryViewModel : ViewModelBase
{
    private readonly IHistoryRepository _repository;
    private ObservableCollection<RenderSession> _sessions = new();
    private RenderSession? _selectedSession;
    private string _searchText = string.Empty;
    private string _statusFilter = "Все";
    private string _notesEditorText = string.Empty;

    // Comparison slots
    private RenderSession? _compareSessionA;
    private RenderSession? _compareSessionB;
    private string _comparisonSummary = "Выберите две сессии для сравнения параметров рендера.";

    public ObservableCollection<RenderSession> FilteredSessions { get; } = new();

    public RenderSession? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetField(ref _selectedSession, value))
            {
                NotesEditorText = value?.UserNotes ?? string.Empty;
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (SetField(ref _statusFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    public string NotesEditorText
    {
        get => _notesEditorText;
        set => SetField(ref _notesEditorText, value);
    }

    public RenderSession? CompareSessionA
    {
        get => _compareSessionA;
        set
        {
            if (SetField(ref _compareSessionA, value)) UpdateComparison();
        }
    }

    public RenderSession? CompareSessionB
    {
        get => _compareSessionB;
        set
        {
            if (SetField(ref _compareSessionB, value)) UpdateComparison();
        }
    }

    public string ComparisonSummary
    {
        get => _comparisonSummary;
        set => SetField(ref _comparisonSummary, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand SaveNotesCommand { get; }
    public ICommand DeleteSessionCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand SetCompareACommand { get; }
    public ICommand SetCompareBCommand { get; }

    public HistoryViewModel(IHistoryRepository repository)
    {
        _repository = repository;

        RefreshCommand = new RelayCommand(async () => await LoadHistoryAsync());
        SaveNotesCommand = new RelayCommand(async () => await SaveSelectedNotesAsync());
        DeleteSessionCommand = new RelayCommand(async () => await DeleteSelectedSessionAsync());
        ClearAllCommand = new RelayCommand(async () => await ClearAllSessionsAsync());
        ExportCsvCommand = new RelayCommand(async () => await ExportCsvAsync());

        SetCompareACommand = new RelayCommand(() =>
        {
            if (SelectedSession != null) CompareSessionA = SelectedSession;
        });

        SetCompareBCommand = new RelayCommand(() =>
        {
            if (SelectedSession != null) CompareSessionB = SelectedSession;
        });
    }

    public async Task LoadHistoryAsync()
    {
        var list = await _repository.GetAllSessionsAsync();
        _sessions = new ObservableCollection<RenderSession>(list);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredSessions.Clear();
        foreach (var s in _sessions)
        {
            bool matchesSearch = string.IsNullOrWhiteSpace(_searchText) ||
                                 s.SceneName.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                                 s.AppName.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                                 s.ProfileName.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                                 s.UserNotes.Contains(_searchText, StringComparison.OrdinalIgnoreCase);

            bool matchesStatus = _statusFilter == "Все" ||
                                 s.FinalStatus.ToDisplayName().Equals(_statusFilter, StringComparison.OrdinalIgnoreCase);

            if (matchesSearch && matchesStatus)
            {
                FilteredSessions.Add(s);
            }
        }
    }

    private async Task SaveSelectedNotesAsync()
    {
        if (SelectedSession != null)
        {
            SelectedSession.UserNotes = NotesEditorText;
            await _repository.UpdateSessionAsync(SelectedSession);
            await LoadHistoryAsync();
        }
    }

    private async Task DeleteSelectedSessionAsync()
    {
        if (SelectedSession != null)
        {
            await _repository.DeleteSessionAsync(SelectedSession.Id);
            SelectedSession = null;
            await LoadHistoryAsync();
        }
    }

    private async Task ClearAllSessionsAsync()
    {
        await _repository.ClearAllAsync();
        SelectedSession = null;
        CompareSessionA = null;
        CompareSessionB = null;
        await LoadHistoryAsync();
    }

    private async Task ExportCsvAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV файлы (*.csv)|*.csv",
            FileName = $"render_watch_history_{DateTime.Now:yyyyMMdd_HHmm}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            await _repository.ExportToCsvAsync(dialog.FileName, FilteredSessions);
        }
    }

    private void UpdateComparison()
    {
        if (CompareSessionA == null || CompareSessionB == null)
        {
            ComparisonSummary = "Выберите сессию А и сессию Б в таблице для сравнения.";
            return;
        }

        var durationDiff = CompareSessionB.Duration - CompareSessionA.Duration;
        string durSign = durationDiff.TotalSeconds >= 0 ? "+" : "-";
        var absDur = durationDiff.Duration();

        double peakGpuDiff = (CompareSessionB.PeakGpuUsage ?? 0) - (CompareSessionA.PeakGpuUsage ?? 0);
        double peakVramDiff = (CompareSessionB.PeakVramGb ?? 0) - (CompareSessionA.PeakVramGb ?? 0);

        ComparisonSummary = $"""
        Сравнение сессий:
        • Сессия А: {CompareSessionA.SceneName} ({CompareSessionA.FormattedDuration}) | GPU: {CompareSessionA.PeakGpuUsage:F0}% | VRAM: {CompareSessionA.PeakVramGb:F1} ГБ
        • Сессия Б: {CompareSessionB.SceneName} ({CompareSessionB.FormattedDuration}) | GPU: {CompareSessionB.PeakGpuUsage:F0}% | VRAM: {CompareSessionB.PeakVramGb:F1} ГБ
        -------------------------------------------------------------
        Разница во времени: {durSign}{absDur:hh\\:mm\\:ss}
        Разница пиковой GPU нагрузки: {(peakGpuDiff >= 0 ? "+" : "")}{peakGpuDiff:F1}%
        Разница пикового VRAM: {(peakVramDiff >= 0 ? "+" : "")}{peakVramDiff:F1} ГБ
        """;
    }
}