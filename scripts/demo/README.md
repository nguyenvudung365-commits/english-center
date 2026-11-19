# Demo dataset (isolated local stack)

Repeatable, rich demo data for presenting every shipped feature. It talks only to a **loopback** API of its **own**
Docker Compose project (`english-center-demo`), never to the VPS, production or an existing database.

```text
python scripts/demo/seed_demo.py up [--web]   # create .env.demo (random secrets, only if absent) and start the stack
python scripts/demo/seed_demo.py seed         # idempotent upsert through the API (~10-15 min, see "Timing")
python scripts/demo/seed_demo.py verify       # read-only PASS/FAIL checks of the seeded data
python scripts/demo/seed_demo.py accounts     # list demo emails (passwords are never printed)
python scripts/demo/seed_demo.py down         # stop, KEEP data volumes
python scripts/demo/seed_demo.py self-check   # offline checks of the generator (no Docker, no network)
python scripts/demo/seed_demo.py seed --dry-run
python scripts/demo/seed_demo.py seed --only classes,sessions   # run chosen phases (order is fixed)
```

Requirements: Docker Desktop running (the script never starts or reconfigures it), Python 3.12, stdlib only.
Ports (loopback only): API `18080`, web `18088`, MinIO `19000/19001`. Override in `.env.demo` before the first `up`.

## Safety

- Uses `compose.minio.yaml` (+ `compose.web.yaml` with `--web`) under project name `english-center-demo`; own volumes.
- `.env.demo` and `.env.demo-seed.json` (id cache) are git-ignored (`.env.*`); secrets are random and never printed.
- Refuses non-loopback API/storage URLs, refuses an env file without the `DEMO_STACK` marker, and refuses to run if
  `ACADEMIC_RESOLVE_MISSED=true`. `ResolveMissed` is never enabled.
- The script has no reset or volume deletion command.
- The bootstrap Admin provisions demo Staff through the API. Both `seed` and `verify` require the API port
  to be published by the demo project on a local Docker context.

## Idempotency (preserve-on-repeat)

Every item is looked up by a natural key before creation (class code, session start/unit, feed text, FAQ question,
assignment title, placement test name, feedback title, ...). Existing items are **left untouched**, never overwritten,
so a second run creates nothing (`self-check` and a fake-API double run confirm the control flow). Notification read
states are applied only on a run that created new activity, so a repeat never wipes the unread demo state.

## What gets seeded

| Area | Content |
|---|---|
| Actors | 1 Admin (bootstrap), 4 Staff, 10 Teachers, 48 Students (42 enrolled, 6 applicants) |
| Classes | 10 over all 3 courses: 7 Ongoing, 2 Upcoming, 1 Finished, Main + Supplementary, one exactly full |
| Schedule | Relative to the run date: sessions every other day from -14 to +21 days, mock tests, plus one "Live" session ending within the last 24h per ongoing class so attendance is writable |
| Enrollment | 67 enrollments with violation limits, 2 revoked memberships |
| Finance | invoices from enrollment, ~65% confirmed paid, the rest Unpaid |
| Placement | Pending / Done / Cancelled tests for applicants and enrolled students |
| Attendance | all six statuses, some corrected (revision history); a sample absence exceeds a limit of zero and creates a warning |
| Coursework | class materials, PDF exams, homework with file submissions and grades, in-class work, and draft/upcoming/closed examples |
| Practice | text exercises per class (teacher and staff authors) |
| Feed | posts, comments, likes, one hidden post and one hidden comment (moderation) |
| Feedback | 12 items in New / InProgress / Resolved |
| FAQ | 8 published + 2 draft |
| Notifications / stats | produced by the events above; `verify` reads them back |

## Timing

Auth endpoints allow 20 requests/minute/IP, so the client paces logins (3.2 s). A full first run performs roughly
60 logins plus ~1500 calls: expect 10-15 minutes. Re-runs only log in where work is needed.

## Honest gaps (cannot be reached through supported APIs)

1. **No Missed/Exempted outcomes or Missed notifications**: they require `ResolveMissed`, kept off on purpose.
2. **Attendance only within 24h after a session ends.** Older history cannot be backfilled; run `seed` again on
   later days to accumulate it.
3. **Closed assignments have no submissions/grades** (submitting is only allowed while open).
4. **Finished class has no students** (enrollment is refused after a class ends) so "completed classes" stays 0
   until real time passes.
5. **No overdue invoices**: invoices are only `Unpaid`/`Paid`.
6. Avatars are not seeded; there is no student-list API, so student ids are cached in `.env.demo-seed.json` (rebuilt
   via logins if missing; discarded automatically if the stack's admin id changes).

## Not verified here

The real API could not be started in the authoring environment (Docker daemon down, API requires PostgreSQL), so the
end-to-end run against the real stack is unverified. Only `self-check`, `--dry-run` and a throwaway in-memory fake-API
run (control flow and idempotency, not the real contract) were executed.
