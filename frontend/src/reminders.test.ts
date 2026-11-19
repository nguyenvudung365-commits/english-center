import { expect, it } from "vitest";
import { upcoming } from "./reminders";

it("keeps only future items within the supplied window, soonest first", () => {
    const now = Date.parse("2026-11-19T08:00:00Z");
    const entries = ["2026-11-19T10:00:00Z", "2026-11-19T08:00:01Z", "2026-11-19T08:00:00Z", "2026-11-21T09:00:00Z", "invalid"];
    expect(upcoming(entries, item => item, now, 48)).toEqual([entries[1], entries[0]]);
});
