using System.Collections.ObjectModel;
using System.Globalization;
using Hechao.Launcher.Infrastructure;

namespace Hechao.Launcher.ViewModels;

public sealed class ActivityCalendarDayViewModel
{
    private const int MaximumVisibleActivities = 2;

    public ActivityCalendarDayViewModel(
        DateTime date,
        DateTime displayedMonthStart,
        DateTime today,
        DateTime selectedDate,
        IReadOnlyList<ActivityServerItemViewModel> activities)
    {
        Date = date.Date;
        IsCurrentMonth = Date.Year == displayedMonthStart.Year &&
                         Date.Month == displayedMonthStart.Month;
        IsToday = Date == today.Date;
        IsSelected = Date == selectedDate.Date;
        Activities = activities;
        VisibleActivities = activities
            .Take(MaximumVisibleActivities)
            .ToArray();
    }

    public DateTime Date { get; }

    public int DayNumber => Date.Day;

    public bool IsCurrentMonth { get; }

    public bool IsToday { get; }

    public bool IsSelected { get; }

    public IReadOnlyList<ActivityServerItemViewModel> Activities { get; }

    public IReadOnlyList<ActivityServerItemViewModel> VisibleActivities { get; }

    public int ActivityCount => Activities.Count;

    public bool HasActivities => ActivityCount > 0;

    public int HiddenActivityCount => Math.Max(0, ActivityCount - VisibleActivities.Count);

    public bool HasHiddenActivities => HiddenActivityCount > 0;

    public string HiddenActivityText => $"另有 {HiddenActivityCount} 场";

    public string AutomationName => ActivityCount == 0
        ? $"{Date:yyyy年M月d日}，无活动"
        : $"{Date:yyyy年M月d日}，{ActivityCount} 场活动";
}

public sealed class ActivityCalendarViewModel : ObservableObject
{
    private const int CalendarDayCount = 42;
    private static readonly CultureInfo ChineseCulture =
        CultureInfo.GetCultureInfo("zh-CN");

    private readonly Func<DateTime> _todayProvider;
    private readonly List<ActivityServerItemViewModel> _activities = [];
    private DateTime _displayedMonthStart;
    private DateTime _selectedDate;
    private int _monthActivityCount;

    public ActivityCalendarViewModel(Func<DateTime>? todayProvider = null)
    {
        _todayProvider = todayProvider ?? (() => DateTime.Today);
        var today = _todayProvider().Date;
        _displayedMonthStart = new DateTime(today.Year, today.Month, 1);
        _selectedDate = today;

        PreviousMonthCommand = new RelayCommand(() => MoveMonth(-1));
        NextMonthCommand = new RelayCommand(() => MoveMonth(1));
        GoToTodayCommand = new RelayCommand(GoToToday);
        SelectDayCommand = new RelayCommand<ActivityCalendarDayViewModel>(SelectDay);

        RebuildCalendar();
    }

    public ObservableCollection<ActivityCalendarDayViewModel> Days { get; } = [];

    public ObservableCollection<ActivityServerItemViewModel> SelectedActivities { get; } = [];

    public ObservableCollection<ActivityServerItemViewModel> UnscheduledActivities { get; } = [];

    public ObservableCollection<ActivityServerItemViewModel> UpcomingActivities { get; } = [];

    public RelayCommand PreviousMonthCommand { get; }

    public RelayCommand NextMonthCommand { get; }

    public RelayCommand GoToTodayCommand { get; }

    public RelayCommand<ActivityCalendarDayViewModel> SelectDayCommand { get; }

    public DateTime DisplayedMonthStart => _displayedMonthStart;

    public DateTime SelectedDate => _selectedDate;

    public string DisplayedMonthText =>
        _displayedMonthStart.ToString("yyyy年M月", ChineseCulture);

    public string SelectedDateText =>
        _selectedDate.ToString("M月d日 dddd", ChineseCulture);

    public int MonthActivityCount => _monthActivityCount;

    public int UnscheduledActivityCount => UnscheduledActivities.Count;

    public bool HasSelectedActivities => SelectedActivities.Count > 0;

    public bool HasNoSelectedActivities => !HasSelectedActivities;

    public bool HasUnscheduledActivities => UnscheduledActivityCount > 0;

    public bool HasUpcomingActivities => UpcomingActivities.Count > 0;

    public bool HasNoUpcomingActivities => !HasUpcomingActivities;

    public string SelectedDateSummaryText => HasSelectedActivities
        ? $"{SelectedActivities.Count} 场活动"
        : "当天暂无活动安排";

    public string MonthSummaryText
    {
        get
        {
            if (MonthActivityCount == 0 && UnscheduledActivityCount == 0)
            {
                return "本月暂无活动";
            }

            if (UnscheduledActivityCount == 0)
            {
                return $"本月 {MonthActivityCount} 场活动";
            }

            return $"本月 {MonthActivityCount} 场活动 · {UnscheduledActivityCount} 个待排期";
        }
    }

    public void ReplaceActivities(IEnumerable<ActivityServerItemViewModel> activities)
    {
        ArgumentNullException.ThrowIfNull(activities);

        _activities.Clear();
        var knownIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var activity in activities
                     .OrderBy(GetScheduleSortKey)
                     .ThenBy(item => item.Name, StringComparer.CurrentCulture))
        {
            if (knownIds.Add(activity.Id))
            {
                _activities.Add(activity);
            }
        }

        UnscheduledActivities.Clear();
        foreach (var activity in _activities.Where(item => GetDateRange(item) is null))
        {
            UnscheduledActivities.Add(activity);
        }

        UpcomingActivities.Clear();
        var today = _todayProvider().Date;
        foreach (var activity in _activities
                     .Where(item => GetDateRange(item) is { } range && range.End >= today)
                     .Take(3))
        {
            UpcomingActivities.Add(activity);
        }

        RebuildCalendar();
    }

    private void MoveMonth(int offset)
    {
        var targetMonth = _displayedMonthStart.AddMonths(offset);
        var selectedDay = Math.Min(
            _selectedDate.Day,
            DateTime.DaysInMonth(targetMonth.Year, targetMonth.Month));
        _displayedMonthStart = targetMonth;
        _selectedDate = new DateTime(targetMonth.Year, targetMonth.Month, selectedDay);
        RebuildCalendar();
    }

    private void GoToToday()
    {
        var today = _todayProvider().Date;
        _displayedMonthStart = new DateTime(today.Year, today.Month, 1);
        _selectedDate = today;
        RebuildCalendar();
    }

    private void SelectDay(ActivityCalendarDayViewModel? day)
    {
        if (day is null)
        {
            return;
        }

        _selectedDate = day.Date;
        _displayedMonthStart = new DateTime(day.Date.Year, day.Date.Month, 1);
        RebuildCalendar();
    }

    private void RebuildCalendar()
    {
        var today = _todayProvider().Date;
        var mondayOffset = ((int)_displayedMonthStart.DayOfWeek + 6) % 7;
        var firstVisibleDate = _displayedMonthStart.AddDays(-mondayOffset);

        Days.Clear();
        for (var index = 0; index < CalendarDayCount; index++)
        {
            var date = firstVisibleDate.AddDays(index);
            Days.Add(new ActivityCalendarDayViewModel(
                date,
                _displayedMonthStart,
                today,
                _selectedDate,
                GetActivitiesOnDate(date)));
        }

        SelectedActivities.Clear();
        foreach (var activity in GetActivitiesOnDate(_selectedDate))
        {
            SelectedActivities.Add(activity);
        }

        var monthEnd = _displayedMonthStart.AddMonths(1).AddDays(-1);
        _monthActivityCount = _activities.Count(activity =>
        {
            var range = GetDateRange(activity);
            return range is not null &&
                   range.Value.Start <= monthEnd &&
                   range.Value.End >= _displayedMonthStart;
        });

        NotifyCalendarStateChanged();
    }

    private IReadOnlyList<ActivityServerItemViewModel> GetActivitiesOnDate(DateTime date) =>
        _activities
            .Where(activity =>
            {
                var range = GetDateRange(activity);
                return range is not null &&
                       date.Date >= range.Value.Start &&
                       date.Date <= range.Value.End;
            })
            .OrderBy(GetScheduleSortKey)
            .ThenBy(item => item.Name, StringComparer.CurrentCulture)
            .ToArray();

    private static (DateTime Start, DateTime End)? GetDateRange(
        ActivityServerItemViewModel activity)
    {
        var opensOn = activity.Server.OpensAt?.ToLocalTime().Date;
        var closesOn = activity.Server.ClosesAt?.ToLocalTime().Date;
        if (opensOn is null && closesOn is null)
        {
            return null;
        }

        var start = opensOn ?? closesOn!.Value;
        var end = closesOn ?? start;
        return end < start
            ? (start, start)
            : (start, end);
    }

    private static long GetScheduleSortKey(ActivityServerItemViewModel activity) =>
        activity.Server.OpensAt?.UtcDateTime.Ticks ??
        activity.Server.ClosesAt?.UtcDateTime.Ticks ??
        long.MaxValue;

    private void NotifyCalendarStateChanged()
    {
        OnPropertyChanged(nameof(DisplayedMonthStart));
        OnPropertyChanged(nameof(SelectedDate));
        OnPropertyChanged(nameof(DisplayedMonthText));
        OnPropertyChanged(nameof(SelectedDateText));
        OnPropertyChanged(nameof(MonthActivityCount));
        OnPropertyChanged(nameof(UnscheduledActivityCount));
        OnPropertyChanged(nameof(HasSelectedActivities));
        OnPropertyChanged(nameof(HasNoSelectedActivities));
        OnPropertyChanged(nameof(HasUnscheduledActivities));
        OnPropertyChanged(nameof(HasUpcomingActivities));
        OnPropertyChanged(nameof(HasNoUpcomingActivities));
        OnPropertyChanged(nameof(SelectedDateSummaryText));
        OnPropertyChanged(nameof(MonthSummaryText));
    }
}
