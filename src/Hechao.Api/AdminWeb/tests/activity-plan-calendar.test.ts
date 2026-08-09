import { describe, expect, it } from "vitest";
import {
  moveActivityPlanDates,
  resizeActivityPlanDates,
  toActivityPlanCalendarRange
} from "@/activityPlanCalendarDates";

const oneDay = { years: 0, months: 0, days: 1, milliseconds: 0 };
const zero = { years: 0, months: 0, days: 0, milliseconds: 0 };

function localDate(
  year: number,
  month: number,
  day: number,
  hour: number,
  minute: number,
  second = 0,
  millisecond = 0
): Date {
  return new Date(year, month - 1, day, hour, minute, second, millisecond);
}

describe("activity plan calendar dates", () => {
  it("maps real timestamps to an exclusive all-day calendar range", () => {
    const opensAt = localDate(2026, 8, 12, 19, 15);
    const closesAt = localDate(2026, 8, 12, 22, 45);
    const midnightClose = localDate(2026, 8, 13, 0, 0);

    expect(toActivityPlanCalendarRange(opensAt.toISOString(), closesAt.toISOString()))
      .toEqual({ start: "2026-08-12", end: "2026-08-13" });
    expect(toActivityPlanCalendarRange(opensAt.toISOString(), midnightClose.toISOString()))
      .toEqual({ start: "2026-08-12", end: "2026-08-13" });
  });

  it("moves both boundaries by calendar days without clearing the time", () => {
    const opensAt = localDate(2026, 8, 12, 19, 15, 30, 250);
    const closesAt = localDate(2026, 8, 12, 22, 45, 40, 500);
    const moved = moveActivityPlanDates(
      opensAt.toISOString(),
      closesAt.toISOString(),
      oneDay
    );

    expect(moved).not.toBeNull();
    expect([
      moved!.opensAt.getDate(),
      moved!.opensAt.getHours(),
      moved!.opensAt.getMinutes(),
      moved!.opensAt.getSeconds(),
      moved!.opensAt.getMilliseconds()
    ]).toEqual([13, 19, 15, 30, 250]);
    expect([
      moved!.closesAt.getDate(),
      moved!.closesAt.getHours(),
      moved!.closesAt.getMinutes(),
      moved!.closesAt.getSeconds(),
      moved!.closesAt.getMilliseconds()
    ]).toEqual([13, 22, 45, 40, 500]);
  });

  it("resizes the start and end boundaries independently", () => {
    const opensAt = localDate(2026, 8, 12, 19, 15);
    const closesAt = localDate(2026, 8, 12, 22, 45);
    const startResized = resizeActivityPlanDates(
      opensAt.toISOString(),
      closesAt.toISOString(),
      { ...oneDay, days: -1 },
      zero
    );
    const endResized = resizeActivityPlanDates(
      opensAt.toISOString(),
      closesAt.toISOString(),
      zero,
      oneDay
    );

    expect(startResized).not.toBeNull();
    expect(startResized!.opensAt.getDate()).toBe(11);
    expect(startResized!.closesAt.getDate()).toBe(12);
    expect(startResized!.opensAt.getHours()).toBe(19);
    expect(startResized!.closesAt.getHours()).toBe(22);

    expect(endResized).not.toBeNull();
    expect(endResized!.opensAt.getDate()).toBe(12);
    expect(endResized!.closesAt.getDate()).toBe(13);
    expect(endResized!.opensAt.getMinutes()).toBe(15);
    expect(endResized!.closesAt.getMinutes()).toBe(45);
  });
});
