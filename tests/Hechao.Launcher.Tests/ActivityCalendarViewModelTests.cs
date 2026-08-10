using Hechao.Contracts;
using Hechao.Launcher.ViewModels;

namespace Hechao.Launcher.Tests;

public sealed class ActivityCalendarViewModelTests
{
    [Fact]
    public void CalendarBuildsSixMondayFirstWeeksAndSelectsToday()
    {
        var today = new DateTime(2026, 8, 4);
        var calendar = new ActivityCalendarViewModel(() => today);

        Assert.Equal(42, calendar.Days.Count);
        Assert.Equal(new DateTime(2026, 7, 27), calendar.Days[0].Date);
        Assert.Equal(new DateTime(2026, 9, 6), calendar.Days[^1].Date);
        Assert.Equal(today, Assert.Single(calendar.Days, day => day.IsToday).Date);
        Assert.Equal(today, Assert.Single(calendar.Days, day => day.IsSelected).Date);
        Assert.Equal("2026年8月", calendar.DisplayedMonthText);
        Assert.Equal("本月暂无活动", calendar.MonthSummaryText);
    }

    [Fact]
    public void ActivitiesMapAcrossDateRangesAndKeepUnscheduledEntriesAccessible()
    {
        var today = new DateTime(2026, 8, 4);
        var calendar = new ActivityCalendarViewModel(() => today);
        var spanning = CreateActivity(
            "spanning",
            LocalDateTime(2026, 8, 3, 18),
            LocalDateTime(2026, 8, 5, 21));
        var opensOnly = CreateActivity(
            "opens-only",
            LocalDateTime(2026, 8, 4, 9),
            null);
        var closesOnly = CreateActivity(
            "closes-only",
            null,
            LocalDateTime(2026, 8, 4, 23));
        var unscheduled = CreateActivity("unscheduled", null, null);

        calendar.ReplaceActivities([spanning, opensOnly, closesOnly, unscheduled]);

        Assert.Equal(
            ["spanning"],
            FindDay(calendar, new DateTime(2026, 8, 3)).Activities.Select(item => item.Id));
        var selectedDay = FindDay(calendar, today);
        Assert.Equal(3, selectedDay.ActivityCount);
        Assert.Equal(2, selectedDay.VisibleActivities.Count);
        Assert.True(selectedDay.HasHiddenActivities);
        Assert.Equal("另有 1 场", selectedDay.HiddenActivityText);
        Assert.Equal(
            ["spanning"],
            FindDay(calendar, new DateTime(2026, 8, 5)).Activities.Select(item => item.Id));
        Assert.Equal(3, calendar.SelectedActivities.Count);
        Assert.Equal("unscheduled", Assert.Single(calendar.UnscheduledActivities).Id);
        Assert.Equal(3, calendar.MonthActivityCount);
        Assert.Equal("本月 3 场活动 · 1 个待排期", calendar.MonthSummaryText);
    }

    [Fact]
    public void MonthNavigationClampsTheSelectedDayAndAdjacentDatesChangeMonth()
    {
        var today = new DateTime(2026, 1, 31);
        var calendar = new ActivityCalendarViewModel(() => today);

        calendar.NextMonthCommand.Execute(null);

        Assert.Equal(new DateTime(2026, 2, 1), calendar.DisplayedMonthStart);
        Assert.Equal(new DateTime(2026, 2, 28), calendar.SelectedDate);

        var marchFirst = FindDay(calendar, new DateTime(2026, 3, 1));
        Assert.False(marchFirst.IsCurrentMonth);
        calendar.SelectDayCommand.Execute(marchFirst);

        Assert.Equal(new DateTime(2026, 3, 1), calendar.DisplayedMonthStart);
        Assert.Equal(new DateTime(2026, 3, 1), calendar.SelectedDate);

        calendar.GoToTodayCommand.Execute(null);

        Assert.Equal(new DateTime(2026, 1, 1), calendar.DisplayedMonthStart);
        Assert.Equal(today, calendar.SelectedDate);
    }

    [Fact]
    public void InvalidReverseScheduleIsConfinedToItsOpeningDate()
    {
        var openingDate = new DateTime(2026, 8, 8);
        var calendar = new ActivityCalendarViewModel(() => openingDate);
        calendar.ReplaceActivities(
        [
            CreateActivity(
                "reverse",
                LocalDateTime(2026, 8, 8, 20),
                LocalDateTime(2026, 8, 7, 20)),
        ]);

        Assert.Single(FindDay(calendar, openingDate).Activities);
        Assert.Empty(FindDay(calendar, openingDate.AddDays(-1)).Activities);
    }

    [Fact]
    public void MidnightCloseKeepsTheCalendarRangeEndExclusive()
    {
        var openingDate = new DateTime(2026, 8, 8);
        var calendar = new ActivityCalendarViewModel(() => openingDate);
        calendar.ReplaceActivities(
        [
            CreateActivity(
                "overnight",
                LocalDateTime(2026, 8, 8, 20),
                LocalDateTime(2026, 8, 10, 0)),
        ]);

        Assert.Single(FindDay(calendar, openingDate).Activities);
        Assert.Single(FindDay(calendar, openingDate.AddDays(1)).Activities);
        Assert.Empty(FindDay(calendar, openingDate.AddDays(2)).Activities);
    }

    [Fact]
    public void UpcomingActivities_ContainsOnlyOngoingAndFutureScheduledEntries()
    {
        var today = new DateTime(2026, 8, 4);
        var calendar = new ActivityCalendarViewModel(() => today);

        calendar.ReplaceActivities(
        [
            CreateActivity(
                "past",
                LocalDateTime(2026, 8, 1, 9),
                LocalDateTime(2026, 8, 2, 21)),
            CreateActivity(
                "ongoing",
                LocalDateTime(2026, 8, 3, 9),
                LocalDateTime(2026, 8, 4, 21)),
            CreateActivity(
                "future",
                LocalDateTime(2026, 8, 8, 19),
                LocalDateTime(2026, 8, 8, 22)),
            CreateActivity("unscheduled", null, null),
        ]);

        Assert.Equal(
            ["ongoing", "future"],
            calendar.UpcomingActivities.Select(item => item.Id));
        Assert.True(calendar.HasUpcomingActivities);
        Assert.False(calendar.HasNoUpcomingActivities);
    }

    private static ActivityCalendarDayViewModel FindDay(
        ActivityCalendarViewModel calendar,
        DateTime date) =>
        Assert.Single(calendar.Days, day => day.Date == date.Date);

    private static ActivityServerItemViewModel CreateActivity(
        string id,
        DateTimeOffset? opensAt,
        DateTimeOffset? closesAt) =>
        new(new ServerSummary(
            id,
            $"活动 {id}",
            id,
            "活",
            ServerStatus.Online,
            0,
            30,
            "1.21.11",
            ModLoaderKind.NeoForge,
            AccessTier.Participant,
            $"profile-{id}",
            "活动公告",
            opensAt,
            closesAt,
            ServerCatalogSection.Activity));

    private static DateTimeOffset LocalDateTime(
        int year,
        int month,
        int day,
        int hour) =>
        new(new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Local));
}
