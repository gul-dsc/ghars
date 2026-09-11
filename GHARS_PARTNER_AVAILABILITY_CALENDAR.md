# Partner Availability Calendar — Design

**Status:** implemented
**Date:** 2026-09-11
**Scope:** an *optional* calendar on which an implementing entity may publish future times it is
willing to receive requests for, and from which a club may select one when it creates a booking.

This document is the design record required before the feature was built. It is deliberately written
as a set of decisions with their reasons, because most of the risk in this feature is not in the code
— it is in what the feature must **not** do to the booking system that already works.

---

## 1. What this is, and what it is not

An availability slot is **a future time an entity is willing to receive a request for**. It is not a
booking, not a scheduled event, and not a delivered activity.

| Record | Meaning | Counted in reporting? |
|---|---|---|
| `PartnerAvailabilitySlot` | "I could take a request at this time" | **No** — never |
| `BookingRequest` | A club asked | Yes, as a booking |
| `AgendaEntry` | The activity was scheduled / delivered | Yes, as Ghars delivery |

Publishing twenty slots changes no number anywhere in Ghars. See §12.

**The calendar is an aid, not a booking engine.** Selecting a slot does not confirm anything. The
request still lands in the entity's existing inbox and still requires the same confirmation, with the
same required lecturer details, as every other booking.

---

## 2. Model decision — a new entity, not `CalendarEvent`

The brief asked for this to be assessed rather than assumed. `CalendarEvent` was examined and
**rejected**. Six reasons, in order of weight:

1. **It has no owner.** `CalendarEvent` carries no organization of any kind — not a partner, not a
   club. Availability is meaningless without "whose". Adding `PartnerOrganizationId` to it would add
   a column that is null for every existing row and mandatory for the new use, which is the shape of
   two entities sharing a table.

2. **Title is `[Required]` in both languages.** `Available 10:00–11:00` has no title, and inventing
   one ("Availability") to satisfy a validation attribute is a sign the entity is wrong.

3. **`IsPublic` is a boolean; availability needs a lifecycle.** Available → Pending → Booked →
   released is five states and a set of legal transitions. A bool cannot express "a club has
   requested this but the partner has not yet confirmed", which is the single most important state in
   this feature.

4. **`Season.CalendarEvents` would change meaning.** That navigation property is a season's event
   list. Making it also return partner availability would silently change what every existing
   consumer of it sees.

5. **`CalendarEventType` would need a new member.** Any code comparing that enum would silently start
   including or excluding availability depending on how it was written — exactly the failure mode
   that made `OfferingApprovalStatus` a separate enum from `ActivityStatus` (see
   `Models/Core/Enums.cs`).

6. **Semantics.** A calendar event is something that *is happening*. An availability slot is
   something that *might be asked for*. Conflating the two is what makes a calendar feature slowly
   become a second booking system.

**Decision: `Models/Core/PartnerAvailabilitySlot.cs`, a new `AuditableEntity`.** `CalendarEvent` is
untouched — not one line changed.

### Flat slot model

One row per time slot, not a day row with child slots. `20 September` with three slots is three rows.

This is what makes §6 of the brief work without effort: multiple slots per date is the *default*
shape rather than a feature. Each slot is independently selectable, claimable, editable and
cancellable, and the day view is a `GROUP BY Date` over rows the database can index directly. A
parent `AvailabilityDay` row would add a join, a cascade decision and an orphan-cleanup rule, and buy
nothing — there is no per-day attribute that is not already per-slot.

---

## 3. Time and timezone

**Finding first:** this codebase had **no timezone handling at all** before this feature — zero
references to `TimeZoneInfo`, `Asia/Dubai` or any offset. The existing booking flow collects a
`DateOnly` + two `TimeOnly` values from the club, combines them with `ToDateTime(...)`, and stores
the result in `ProposedStartDateTime`; confirmation then copies that value into `ConfirmedStartUtc`.

So `ConfirmedStartUtc` **does not hold UTC**. It holds a Dubai wall-clock time in a column whose name
says otherwise. That is pre-existing, it is consistent across every booking in the database, and
changing it is not in scope for a calendar feature — reinterpreting the column would move every
historical booking by four hours.

**What this feature does about it:**

- Slots store `DateOnly Date` + `TimeOnly StartTime` + `TimeOnly EndTime`. These types cannot carry an
  offset, so there is no ambiguity to get wrong: they are Dubai local wall-clock by definition, which
  is also what the club types into the existing booking form.
- A slot selection therefore produces `ProposedStartDateTime` by exactly the same expression the
  manual path uses — `Date.ToDateTime(StartTime)`. A calendar booking and a hand-typed booking are
  stored **identically**, which is what keeps every downstream consumer (agenda, KPI, reports, the
  partner inbox) working without knowing the calendar exists.
- "Is this slot in the future?" is the one comparison that genuinely needs a zone, because `now` comes
  from the server clock in UTC. `Helpers/GharsTime.cs` provides it, and nothing else in this feature
  is allowed to call `DateTime.UtcNow` for a scheduling decision.

```
GharsTime.Zone   -> Asia/Dubai, falling back to "Arabian Standard Time", then to a fixed UTC+04:00
GharsTime.Now    -> the current Dubai wall-clock DateTime
GharsTime.Today  -> the current Dubai calendar date
```

The three-step resolution is deliberate: .NET 8 accepts IANA ids on Windows only when ICU is
available, and the Windows id is the one the IIS host will certainly have. Dubai has never observed
daylight saving, so the final fixed-offset fallback is exact rather than approximate.

---

## 4. Ownership — server-derived, never posted

`PartnerOrganizationId` is written from `OrganizationAdminLinks` for the authenticated user, using
the same `PartnerOrganizationIdsAsync()` shape `PartnerProgramsController` already uses. It is not a
property of any view model that binds from a form, and no action accepts it as a parameter.

Every query in the partner controller is scoped by those ids, and a miss returns `NotFound()` rather
than a distinguishable `Forbid()` — the existing non-disclosure convention.

A slot linked to an `Activity` re-validates that the activity is owned by the same entity, is
`Approved`, is `Published`, and belongs to an active season (§18 of the brief).

---

## 5. Lifecycle

```
                  partner publishes
                         │
                         ▼
   ┌──────────────── Available ◄──────────────┐
   │                     │                    │
   │ partner blocks      │ club requests      │ partner rejects / club withdraws
   │                     ▼                    │ / partner proposes another time
   │                  Pending ────────────────┘
   │                     │
   │                     │ partner confirms
   │                     ▼
   │                   Booked        (terminal — history)
   ▼
 Blocked ──► Available   (partner unblocks)
   │
   ▼
Cancelled                (terminal — partner withdrew an unused slot)
```

Five states, exactly the set §9 of the brief describes:

| Status | Club sees it? | Meaning |
|---|---|---|
| `Available` | yes | selectable |
| `Pending` | no | one club's request references it, awaiting the entity's decision |
| `Booked` | no | the entity confirmed a booking against it |
| `Blocked` | no | the entity temporarily withdrew it |
| `Cancelled` | no | the entity withdrew it permanently; kept for audit |

**One confirmed booking per slot.** No capacity column. Nothing in the current Ghars business says an
entity delivers two different clubs' sessions in the same hour, and a capacity field would need a
matching claim-count, a partial-availability display and a "2 of 3 left" vocabulary across two
languages. If that requirement ever arrives it is an additive column plus a change to one guard in
`PartnerAvailabilityWorkflow`; building it now would be building it wrong.

All transitions live in **one place**: `Helpers/PartnerAvailabilityWorkflow.cs`. No controller changes
`Status` directly.

---

## 6. Concurrency — the part that actually needs care

Two clubs clicking the same slot within the same second must not both get it.

**The approach: a single conditional `UPDATE`, guarded on the current status.**

```csharp
UPDATE PartnerAvailabilitySlots
   SET Status = Pending, HeldByBookingRequestId = @booking, ...
 WHERE Id = @id AND Status = Available
```

issued through `ExecuteUpdateAsync`. SQL Server takes an exclusive lock on the row for the duration of
the statement, so of two concurrent executions exactly one observes `Status = Available`. The other
gets **0 rows affected** and is told the slot is gone.

The whole claim runs inside one explicit transaction:

1. `BEGIN TRANSACTION`
2. insert the `BookingRequest` (carrying `PartnerAvailabilitySlotId`)
3. conditional `UPDATE` on the slot
4. rows affected `== 0` → **`ROLLBACK`**, no booking exists, club sees "this time slot is no longer
   available"
5. otherwise `COMMIT`

The booking is created before the claim so the claim can record which booking holds the slot; the
rollback is what makes that ordering safe. There is no window in which a booking exists without its
slot, or a slot is held by a booking that was never committed.

**Why not a `rowversion` token.** It was considered and rejected. `[Timestamp]` would put the token in
the `WHERE` clause of *every* update to the entity and raise `DbUpdateConcurrencyException` on paths
that have nothing to do with claiming — and `ExecuteUpdateAsync`, which is what makes the claim
atomic, ignores concurrency tokens entirely. Mixing the two would give two different concurrency
mechanisms on one table and a false sense that the weaker one was doing the work. The status guard is
strictly stronger: it is a compare-and-swap on the value that actually matters.

Every other transition uses the same pattern with its own guard — release is guarded on
`Status = Pending`, confirm on `Status = Pending`, block on `Status = Available`. A transition that
loses its race changes nothing and says so.

---

## 7. Booking linkage

`BookingRequest.PartnerAvailabilitySlotId` — **nullable**, `NoAction` on delete.

- Every existing booking: `NULL`. Nothing is back-filled, and in particular nothing is inferred from a
  date/time that happens to match (§29, §63 of the brief). Provenance is stored or it is not claimed.
- A manual-date booking: `NULL`, forever.
- A calendar booking: the slot id, forever — including after the slot is released and claimed by
  somebody else. It records *what the club selected at the time*, which is what an audit trail is for.

The slot also carries `HeldByBookingRequestId` — **a plain column, no foreign key**. This is the
*current* holder, which is different information from "every booking that ever referenced this slot",
and having it avoids deriving the holder by filtering historical references by status. It has no FK
precisely to avoid the circular constraint `BookingRequest → Slot → BookingRequest` (§36 of the
brief); the authoritative, constrained direction is the one on `BookingRequest`.

**Schedule Source** is derived, never stored: `PartnerAvailabilitySlotId != null` → "Partner
Calendar", else "Club Proposed". It sits in `Helpers/BookingSource.cs` beside the existing Booking
Source so the two cannot drift, and it is explicitly a *second, orthogonal* axis:

| | Existing Program | Custom Program |
|---|---|---|
| **Partner Calendar** | ✔ (general or program-specific slot) | ✔ (general slot only) |
| **Club Proposed** | ✔ | ✔ |

All four combinations are reachable and valid. No third booking type was created.

---

## 8. Optionality — the requirement that constrains everything else

A partner with no calendar must be **indistinguishable** from today.

- No screen requires availability to exist.
- Publishing a program, receiving either kind of request, and confirming a booking all run through
  exactly the code they ran through before; none of them reads the calendar.
- The club booking form renders its manual date/time fields the same way it always has. The
  availability picker is *added above them* when slots exist and is *absent entirely* when they do
  not.
- Where absence needs saying, it is said as a fact rather than a fault:
  - Partner: "You have not published any availability yet. Adding calendar availability is optional.
    Clubs can still send booking requests using their preferred date and time."
  - Club: "This partner has not published availability. You can still suggest your preferred date and
    time."

Both in Arabic too. Neither is styled as a warning.

**Slot selection is optional for the club as well.** Even where slots exist, the manual fields stay
available and "Suggest another date and time" remains one click away. The brief flagged that making
the calendar mandatory once published is a business decision requiring explicit confirmation; it has
not been confirmed, so it has not been built.

---

## 9. Validation

| Rule | Where | Notes |
|---|---|---|
| Start < End | view model + server | |
| No overnight slots | server | first version, per §22; a slot is one calendar day |
| Future only for new/edited active slots | server, via `GharsTime` | past slots stay visible, read-only |
| No overlap, same partner + same date | server | adjacent (`09:00–10:00`, `10:00–11:00`) is allowed |
| No exact duplicate | **database**, filtered unique index | see below |
| Season must be active | server | |
| Linked activity owned + approved + published + in season | server | |

The overlap rule cannot be expressed as a unique index — SQL Server has no range-exclusion
constraint. It is enforced in the workflow helper against all non-cancelled slots for that partner and
date. The database backstop is narrower and covers the case an index *can* express:

```
UX_PartnerAvailabilitySlots_ActiveUnique
  UNIQUE (PartnerOrganizationId, Date, StartTime, EndTime) WHERE Status <> 5   -- 5 = Cancelled
```

Cancelled rows are excluded so that withdrawing a slot and later re-publishing the same time works.

> This is a **filtered index**, so scripts that create it need `QUOTED_IDENTIFIER ON` —
> `sqlcmd -I`. The schema already has 225 such statements; this adds to that set, it does not
> introduce the constraint.

---

## 10. Indexes

Four, each tied to a query that exists:

| Index | Serves |
|---|---|
| `(PartnerOrganizationId, Date, Status)` | partner month view; club "upcoming available"; DSC filtered by partner |
| `(SeasonId, Date, Status)` | DSC cross-partner oversight by season and date range |
| `(ActivityId)` | program-specific slots on the Existing Program booking page |
| `UNIQUE (PartnerOrganizationId, Date, StartTime, EndTime) WHERE Status <> 5` | duplicate backstop (§9) |

EF adds one more automatically for the `BookingRequest.PartnerAvailabilitySlotId` foreign key.

No index on `Status` alone: it has five values over what will be a small table, and a scan of a
selective partner range beats a scan of a third of the table.

---

## 11. Query shape — no N+1

The club catalogue renders dozens of cards. The rule the rest of this codebase follows — one grouped
query per page, never one query per card — is followed here:

- `/Home/Booking`: **one** grouped query returns the set of partner ids that have any future available
  slot. The card reads a `HashSet` lookup. Not one query per partner.
- Existing Program booking page: **one** query for that one partner's relevant slots.
- Custom Program booking page: **no** slot query on load. Slots arrive from a JSON endpoint after the
  club picks an entity — one request for one entity.
- Partner month view: **one** query bounded to the displayed month.
- Pending-holder names on the partner calendar: **one** grouped query for the whole month, not one per
  slot.

Date ranges are always bounded. The club-facing window is the smaller of 90 days and the end of the
active season, so a season ending in three weeks does not advertise slots past it.

---

## 12. Reporting and KPI — explicitly excluded

Availability slots are **not** counted as programs, bookings, delivered lectures, agenda entries, KPI
inputs, annual-report activity totals or participant totals.

This is guaranteed structurally rather than by discipline: no reporting query was changed, and
`PartnerAvailabilitySlot` is a new table that none of them reference. The only place slot counts
appear is three operational tiles on the partner's own calendar page (upcoming available / pending /
booked this season), which feed nothing.

Booking reports continue to count `BookingRequest` rows. `Schedule Source` is available as a display
column; no total changes.

---

## 13. Privacy

The public and club-facing calendars return **only `Available` future slots**. A slot in any other
state simply is not in the result set — there is no "Unavailable" placeholder leaking that someone
else asked.

Never exposed to another club or to an anonymous visitor: the requesting club's identity, the booking
reference, the entity's internal notes, or participant information. The JSON endpoint returns a
projection of six fields (id, date, start, end, activity id, public note) — not the entity.

The entity itself sees "Pending club request" with the club name, on its own calendar only.

---

## 14. Surfaces

| Route | Who | What |
|---|---|---|
| `/partner/calendar` | Partner Admin | month + list, add/edit/block/cancel |
| `/partner/calendar/add` (POST) | Partner Admin | one date, many slots, one submit |
| `/booking/partner/{id}/calendar` | public | one entity's available times |
| `/bookings/partner-availability?partnerId=` | Club Admin | JSON, for the custom-request form |
| `/Admin/PartnerAvailability` | DSC / Super Admin | read-only oversight, filterable |

Partner navigation gains **My Calendar / تقويمي** inside the existing *My Workspace* dropdown, in the
Implementing Entity group beside Partner Dashboard and My Programs. Club Admin does not get the item —
clubs reach availability through Booking, which is where the decision is made.

DSC oversight is **read-only** in this first version. DSC maintaining a partner's availability on its
behalf is a business decision nobody has asked for, and read-only is the reversible half of it.

---

## 15. Accessibility

The month grid is a `<table>` with real headers, and every day that has slots is a `<button>` inside
its cell — reachable by keyboard, with an accessible name that states the date and the slot count.

Status is never conveyed by colour alone: every slot carries an icon **and** a text label, in the
reader's language. The list view is not a fallback for the grid, it is an equal view with its own
toggle, and it is the default under 576px where a seven-column grid stops being usable.

---

## 16. What was deliberately not built

- **Recurrence / "repeat weekly until"** (§23 of the brief, explicitly optional). The add form already
  takes many slots for one date in one submit, which covers the common case. Recurrence without a
  recurrence engine means materialising rows, and materialised rows need an "edit the series"
  answer — that is a feature, not a checkbox.
- **Capacity > 1 per slot** (§11). Default assumption confirmed: one confirmed booking per slot.
- **Mandatory slot selection once a partner publishes** (§27). Needs explicit business confirmation.
- **DSC editing partner availability** (§49). Read-only first.
- **External calendar integration** (§65). Out of scope by instruction.

---

## 17. Deployment

One migration, `AddPartnerAvailabilityCalendar`. Additive only: one new table, one new nullable column
on `BookingRequests`, four indexes, one foreign key. No existing column is altered or dropped, and no
historical migration is edited.

It deploys through the existing Azure pipeline with no change to it. The build stage already emits an
idempotent script for review, and the application applies migrations on startup. Nothing here adds a
machine-local dependency, a scheduled task, a Node build step or an external service.
