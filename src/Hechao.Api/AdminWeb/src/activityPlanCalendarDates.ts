export interface CalendarDuration {
  years: number;
  months: number;
  days: number;
  milliseconds: number;
}

export interface ActivityPlanDateRange {
  opensAt: Date;
  closesAt: Date;
}

function pad(value: number): string {
  return String(value).padStart(2, "0");
}

function localDateKey(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function isLocalMidnight(date: Date): boolean {
  return date.getHours() === 0 &&
    date.getMinutes() === 0 &&
    date.getSeconds() === 0 &&
    date.getMilliseconds() === 0;
}

function addCalendarDuration(date: Date, duration: CalendarDuration): Date {
  const next = new Date(date);
  if (duration.years) next.setFullYear(next.getFullYear() + duration.years);
  if (duration.months) next.setMonth(next.getMonth() + duration.months);
  if (duration.days) next.setDate(next.getDate() + duration.days);
  if (duration.milliseconds) next.setTime(next.getTime() + duration.milliseconds);
  return next;
}

function validRange(opensAt: Date, closesAt: Date): ActivityPlanDateRange | null {
  if (
    !Number.isFinite(opensAt.getTime()) ||
    !Number.isFinite(closesAt.getTime()) ||
    opensAt >= closesAt
  ) {
    return null;
  }
  return { opensAt, closesAt };
}

export function toActivityPlanCalendarRange(opensAtValue: string, closesAtValue: string) {
  const opensAt = new Date(opensAtValue);
  const closesAt = new Date(closesAtValue);
  if (!validRange(opensAt, closesAt)) return null;

  const calendarEnd = new Date(closesAt);
  if (!isLocalMidnight(calendarEnd)) calendarEnd.setDate(calendarEnd.getDate() + 1);

  return {
    start: localDateKey(opensAt),
    end: localDateKey(calendarEnd)
  };
}

export function toUnmanagedScheduleCalendarRange(
  opensAtValue: string | null,
  closesAtValue: string | null
) {
  if (opensAtValue && closesAtValue) {
    const range = toActivityPlanCalendarRange(opensAtValue, closesAtValue);
    if (range) return range;
  }

  const anchor = new Date(opensAtValue ?? closesAtValue ?? "");
  if (!Number.isFinite(anchor.getTime())) return null;
  const nextDay = new Date(anchor);
  nextDay.setDate(nextDay.getDate() + 1);
  return {
    start: localDateKey(anchor),
    end: localDateKey(nextDay)
  };
}

export function activityPlanStartTimeLabel(value: string): string {
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return "";
  return `${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

export function moveActivityPlanDates(
  opensAtValue: string,
  closesAtValue: string,
  delta: CalendarDuration
): ActivityPlanDateRange | null {
  return validRange(
    addCalendarDuration(new Date(opensAtValue), delta),
    addCalendarDuration(new Date(closesAtValue), delta)
  );
}

export function resizeActivityPlanDates(
  opensAtValue: string,
  closesAtValue: string,
  startDelta: CalendarDuration,
  endDelta: CalendarDuration
): ActivityPlanDateRange | null {
  return validRange(
    addCalendarDuration(new Date(opensAtValue), startDelta),
    addCalendarDuration(new Date(closesAtValue), endDelta)
  );
}
