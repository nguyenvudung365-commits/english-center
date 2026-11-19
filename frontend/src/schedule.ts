import { api, ApiError } from "./api";
import { isAcademicId } from "./academic";

export type CalendarMode = "week" | "month";
export type ScheduledSession = {
    id: string;
    classId: string;
    classCode: string;
    className: string;
    teacherId: string;
    teacherName: string;
    unit: string;
    lesson: string;
    skills: string[];
    sessionType: "Regular" | "MockTest" | "FinalTest";
    examSlot: string | null;
    startsAtUtc: string;
    endsAtUtc: string;
    createdAtUtc: string;
    revision: number;
    isCancelled: boolean;
};
export type SessionDraft = {
    classId: string;
    unit: string;
    lesson: string;
    skills: string[];
    sessionType: string;
    examSlot: string;
    startsAtLocal: string;
    endsAtLocal: string;
};
const dayMs = 86400000;
const offsetMs = 7 * 3600000;
export const sessionSkills = ["Listening", "Reading", "Writing", "Speaking"] as const;

function dateMilliseconds(day: string): number {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(day) || day < "0001-01-01" || day > "9999-12-31")
        throw new Error("Ngày phải hợp lệ, theo YYYY-MM-DD.");
    const value = Date.parse(`${day}T00:00:00.000Z`);
    if (!Number.isFinite(value) || new Date(value).toISOString().slice(0, 10) !== day)
        throw new Error("Ngày không tồn tại.");
    return value;
}
function dateString(value: number): string {
    const text = new Date(value).toISOString().slice(0, 10);
    dateMilliseconds(text);
    return text;
}
function instantTicks(instant: string): bigint {
    const match = /^(\d{4}-\d{2}-\d{2})T(\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{1,7}))?)?(Z|[+-](\d{2}):(\d{2}))$/.exec(instant);
    if (!match) throw new Error("Thời điểm phải có múi giờ ISO rõ ràng.");
    dateMilliseconds(match[1]);
    if (+match[2] > 23 || +match[3] > 59 || +(match[4] ?? 0) > 59 ||
        (match[6] !== "Z" && (+match[7] > 14 || +match[8] > 59 || (+match[7] === 14 && +match[8] !== 0))))
        throw new Error("Giờ hoặc múi giờ không hợp lệ.");
    // Parse whole seconds first so fractional ticks also remain correct before the Unix epoch.
    const seconds = Date.parse(`${match[1]}T${match[2]}:${match[3]}:${match[4] ?? "00"}${match[6]}`);
    if (!Number.isFinite(seconds)) throw new Error("Thời điểm không hợp lệ.");
    const value = BigInt(seconds) * 10000n + BigInt((match[5] ?? "").padEnd(7, "0"));
    if (value < BigInt(Date.parse("0001-01-01T00:00:00Z")) * 10000n ||
        value > BigInt(Date.parse("9999-12-31T23:59:59Z")) * 10000n + 9999999n)
        throw new Error("Thời điểm nằm ngoài phạm vi hỗ trợ.");
    return value;
}
function instantMilliseconds(instant: string): number {
    const ticks = instantTicks(instant);
    return Number(ticks / 10000n - (ticks < 0n && ticks % 10000n !== 0n ? 1n : 0n));
}
export function centerDate(instant: string): string {
    return dateString(instantMilliseconds(instant) + offsetMs);
}
export function calendarRange(anchor: string, mode: CalendarMode): { fromUtc: string; toUtc: string; days: string[] } {
    const value = dateMilliseconds(anchor);
    if (mode !== "week" && mode !== "month") throw new Error("Chọn tuần hoặc tháng.");
    const first = mode === "week" ? value : dateMilliseconds(`${anchor.slice(0, 7)}-01`);
    const start = first - ((new Date(first).getUTCDay() + 6) % 7) * dayMs;
    let end = start + 7 * dayMs;
    if (mode === "month") {
        const nextMonth = new Date(first);
        nextMonth.setUTCMonth(nextMonth.getUTCMonth() + 1);
        end = nextMonth.getTime();
        end += ((7 - (new Date(end).getUTCDay() + 6) % 7) % 7) * dayMs;
    }
    const days: string[] = [];
    for (let day = start; day < end; day += dayMs) days.push(dateString(day));
    return { fromUtc: new Date(start - offsetMs).toISOString(), toUtc: new Date(end - offsetMs).toISOString(), days };
}
export function navigateCalendar(anchor: string, mode: CalendarMode, direction: -1 | 1): string {
    const value = dateMilliseconds(anchor);
    if ((mode !== "week" && mode !== "month") || (direction !== -1 && direction !== 1))
        throw new Error("Điều hướng lịch không hợp lệ.");
    if (mode === "week") return dateString(value + direction * 7 * dayMs);
    const target = new Date(value);
    const day = target.getUTCDate();
    target.setUTCDate(1);
    target.setUTCMonth(target.getUTCMonth() + direction);
    const next = new Date(target);
    next.setUTCMonth(next.getUTCMonth() + 1);
    const lastDay = new Date(next.getTime() - dayMs).getUTCDate();
    target.setUTCDate(Math.min(day, lastDay));
    return dateString(target.getTime());
}
export function vietnamLocalToUtc(local: string): string {
    const match = /^(\d{4}-\d{2}-\d{2})T(\d{2}):(\d{2})$/.exec(local);
    if (!match || +match[2] > 23 || +match[3] > 59) throw new Error("Nhập ngày giờ Việt Nam hợp lệ, chính xác đến phút.");
    dateMilliseconds(match[1]);
    return new Date(instantMilliseconds(`${local}:00+07:00`)).toISOString();
}
export function sessionsForDay(sessions: ScheduledSession[], day: string): ScheduledSession[] {
    const start = BigInt(dateMilliseconds(day) - offsetMs) * 10000n;
    const end = start + BigInt(dayMs) * 10000n;
    return sessions.map((item) => ({ item, start: instantTicks(item.startsAtUtc), end: instantTicks(item.endsAtUtc) }))
        .filter((entry) => entry.start < end && entry.end > start)
        .sort((a, b) => a.start < b.start ? -1 : a.start > b.start ? 1 : a.item.id.localeCompare(b.item.id))
        .map((entry) => entry.item);
}
export function validateSessionDraft(draft: SessionDraft): Record<string, string[]> {
    const errors: Record<string, string[]> = {};
    if (!isAcademicId(draft.classId)) errors.classId = ["Chọn lớp có UUID hợp lệ, khác rỗng."];
    if (!draft.unit.trim() || draft.unit.trim().length > 100) errors.unit = ["Unit bắt buộc, tối đa 100 ký tự."];
    if (!draft.lesson.trim() || draft.lesson.trim().length > 200) errors.lesson = ["Bài học bắt buộc, tối đa 200 ký tự."];
    if (!Array.isArray(draft.skills) || !draft.skills.length || new Set(draft.skills).size !== draft.skills.length ||
        draft.skills.some((skill) => !sessionSkills.some((allowed) => allowed === skill))) errors.skills = ["Chọn ít nhất một kỹ năng hợp lệ, không lặp."];
    if (!["Regular", "MockTest", "FinalTest"].includes(draft.sessionType)) errors.sessionType = ["Chọn loại buổi học hợp lệ."];
    if (draft.examSlot.trim().length > 100 || (draft.sessionType === "Regular" && !!draft.examSlot.trim())) errors.examSlot = ["Ca thi tối đa 100 ký tự và chỉ áp dụng cho buổi kiểm tra."];
    let start: string | undefined;
    let end: string | undefined;
    try { start = vietnamLocalToUtc(draft.startsAtLocal); } catch { errors.startsAtLocal = ["Nhập thời gian bắt đầu hợp lệ (UTC+7)."]; }
    try { end = vietnamLocalToUtc(draft.endsAtLocal); } catch { errors.endsAtLocal = ["Nhập thời gian kết thúc hợp lệ (UTC+7)."]; }
    if (start && end && Date.parse(end) <= Date.parse(start)) errors.endsAtLocal = ["Kết thúc phải sau bắt đầu."];
    return errors;
}
export async function getScheduledSession(id: string): Promise<ScheduledSession> {
    if (!isAcademicId(id)) throw new ApiError(400, "Mã buổi học phải là UUID hợp lệ, khác rỗng.");
    return api<ScheduledSession>(`/academic/sessions/${encodeURIComponent(id)}`);
}
export async function listSessions(query: { fromUtc: string; toUtc: string; classId?: string }): Promise<ScheduledSession[]> {
    try {
        const from = instantTicks(query.fromUtc);
        const to = instantTicks(query.toUtc);
        const duration = to - from;
        if (from % 10n !== 0n || to % 10n !== 0n || duration <= 0n || duration > BigInt(93 * dayMs) * 10000n || (query.classId !== undefined && !isAcademicId(query.classId))) throw new Error();
    } catch { throw new ApiError(400, "Khoảng lịch phải có múi giờ rõ ràng, chính xác tối đa đến micro giây, lớn hơn 0 và không quá 93 ngày; lớp phải hợp lệ."); }
    const parameters = new URLSearchParams({ fromUtc: query.fromUtc, toUtc: query.toUtc });
    if (query.classId !== undefined) parameters.set("classId", query.classId);
    return api<ScheduledSession[]>(`/academic/sessions?${parameters}`);
}
export function sessionLocalTime(utc: string) {
    return new Date(instantMilliseconds(utc) + offsetMs).toISOString().slice(0, 16);
}

export async function saveSession(draft: SessionDraft, existing?: ScheduledSession): Promise<ScheduledSession> {
    const errors = validateSessionDraft(draft);
    if (Object.keys(errors).length) throw new ApiError(400, "Vui lòng kiểm tra thông tin buổi học.", errors);
    if (existing && (!isAcademicId(existing.id) || existing.classId !== draft.classId))
        throw new ApiError(400, "Không thể đổi lớp của buổi học.");
    const instant = (local: string, previous?: string) => previous && sessionLocalTime(previous) === local ? previous : vietnamLocalToUtc(local);
    return api<ScheduledSession>(existing ? `/academic/sessions/${existing.id}` : "/academic/sessions", {
        method: existing ? "PUT" : "POST",
        body: JSON.stringify({
            classId: draft.classId,
            unit: draft.unit.trim(),
            lesson: draft.lesson.trim(),
            skills: sessionSkills.filter((skill) => draft.skills.includes(skill)),
            sessionType: draft.sessionType,
            examSlot: draft.examSlot.trim() || null,
            startsAtUtc: instant(draft.startsAtLocal, existing?.startsAtUtc),
            endsAtUtc: instant(draft.endsAtLocal, existing?.endsAtUtc),
            expectedRevision: existing?.revision,
        }),
    });
}

export const createSession = (draft: SessionDraft) => saveSession(draft);

export async function cancelSession(item: ScheduledSession) {
    if (!isAcademicId(item.id)) throw new ApiError(400, "Mã buổi học không hợp lệ.");
    return api<ScheduledSession>(`/academic/sessions/${item.id}/cancel`, {
        method: "POST", body: JSON.stringify({ expectedRevision: item.revision }),
    });
}
