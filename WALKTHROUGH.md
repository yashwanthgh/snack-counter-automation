# Walkthrough: how the code works, in plain words

- **Part 1** follows one refill from "the box is empty" to "the volunteer got their money back".
- **Part 2** lists every folder, class and method, and what each one does.

## The big picture

- Someone types "empty" in chat.
- The system asks **one** volunteer. It never asks everyone at once, because that is how two people end up buying.
- **Yes:** the job is theirs, and nobody else can take it.
- **No, or no answer in 30 minutes:** the **next** person is asked.
- **Done:** the volunteer sends the amount and items. The system records the cost and sends a **claim to HR**.
- **HR approves** (done), or **rejects** (the volunteer and the office contact are told, and people sort it out).
- **Every month:** a report shows what each office spent.

Nothing is hard-coded. Budgets, timers and exchange rates are in `config/settings.json`. People, and whether they are away or visiting, are in `config/people.json`.

---

# Part 1: one refill, step by step

The people (from `config/people.json`):

| Person | Office | Volunteer? | Note |
|---|---|---|---|
| Lars | Oslo | No | Can report, never asked to buy |
| Anna, Bjørn | Oslo | Yes | |
| Ingrid | Oslo | Yes | On holiday until 9 Nov |
| Kavya | Bengaluru | Yes | Visiting Oslo until 16 Nov |

The time is Monday 2 Nov, 09:00 UTC.

### Step 0: The program starts (`ChatSim/Program.cs`, `Main`)
1. `SettingsLoader` reads `settings.json`. If anything is missing or wrong, the program stops with a clear message. It never guesses.
2. `PeopleLoader` reads `people.json`.
3. It creates the fake clock, fake HR, `BudgetService`, `ReportService` and `RefillService` (the rules), then waits for typed lines.

### Step 1: Lars types `lars: empty` (`CommandHandler.Handle`)
- It splits the line at `:` and finds Lars.
- It sees the word `empty`, works out where Lars is today (Oslo), and calls **`RefillService.ReportEmpty(Oslo, "lars")`**.

### Step 2: `ReportEmpty`
- **Is there already an open refill for Oslo?**
  - **No:** it creates a `Refill` and calls `OfferToNextPerson`.
  - **Yes, someone is buying:** it creates no new refill. It just asks that person "still on it?". **This is how we stop two refills for one empty box.**
  - **Yes, but it was escalated (nobody was free):** it clears the "already asked" list and tries again, because people may be back by now.

### Step 3: `OfferToNextPerson` asks `VolunteerSelector.PickNext`
`PickNext` **skips**:
- people who are not volunteers (Lars)
- people who are away (Ingrid)
- people not in Oslo today
- people already asked for this refill

From those left (Anna, Bjørn and Kavya), it picks:
1. fewest refills in the last 90 days, then
2. longest since their last refill, then
3. alphabetical, so the result is always the same.

**Anna** is picked:
- the state becomes **Offered**
- the deadline is 09:30
- Anna gets "Can you refill it? yes/no".

If nobody is left, the state becomes **Escalated** and the **office contact** is told.

### Step 4: Anna doesn't answer. `time +31m` runs `Tick()`
`Tick()` is the "sweeper". In real life it runs every minute.
- **Offered or Committed, and past the deadline:** hand it on with `OfferToNextPerson`. (If it was Committed, the person is also told "don't buy".)
- **Filled:** HR didn't take the claim last time, so try again.

Anna is already on the "asked" list, so **Bjørn** is asked. If Anna says "yes" now, she gets `NotOfferedToYou`.

### Step 5: `bjorn: yes` calls `Accept`
Three checks, in order:
1. Is it still Offered? If not: `AlreadyTaken`.
2. Was it offered to Bjørn? If not: `NotOfferedToYou`.
3. Is it in time? If not: `OfferExpired`.

Then:
- The state becomes **Committed** and the deadline is 3 hours of open office time from now (it pauses overnight and at weekends).
- Bjørn gets "please refill by…, typical spend 300–1,200 NOK".
- **Budget check, before he spends:** is at least one typical maximum purchase (1,200 NOK) left this month? If not, the **office contact is warned**. The refill still goes ahead, because an empty box is the bigger problem.

**Why two people can't both say yes:** every public method runs inside `lock`, so only one runs at a time. The first "yes" changes the state, and the second one sees `AlreadyTaken`.

### Step 6: `bjorn: done 850 chips, cola, fruit` calls `MarkFilled`
1. Is it Bjørn's, and still Committed?
   - If not, return `NotYours`.
   - If he *was* asked earlier (his time ran out but he bought anyway), the **office contact** is told about the late receipt, so his money isn't silently lost.
2. Is the amount more than 0? If not: `InvalidAmount`.
3. Is it in the box's currency (NOK for Oslo)? If not: `WrongCurrency`.
4. The state becomes **Filled**, and the amount, items, receipt and time are saved.
5. The purchase goes in the **ledger** (`BudgetService.Record`), and Bjørn goes to the back of the line for next time.
6. If his receipt is more than the limit he was told when he said yes, **only the limit is claimed** and he is told the rest is his own cost. (He was warned before buying.)
7. `SendClaimToHr`.

### Step 7: `SendClaimToHr`
- It builds a `ClaimRequest` with the fields from the brief: employee id, amount, currency, description and receipt image.
- It adds an **idempotency key**, which is the refill id. **Same key means same claim**, so a retry never creates a second one.
- **Works:** the state becomes **Claimed**, and Bjørn is told "claim C-1 sent".
- **HR is down:** the state stays **Filled**, and `Tick()` retries with the same key.

### Step 8: HR decides. `hr approve C-1` / `hr reject C-1 reason` call `RecordHrDecision`
- **Approved:** the state becomes **Reimbursed**, and Bjørn is told.
- **Rejected:** the state becomes **ClaimRejected**, and **both Bjørn and the office contact** are told. Fixing it is handled by people. That is a deliberate shortcut.
- If HR sends the same decision twice, the second one is ignored.

### Step 9: `report` calls `ReportService.Build`
- It adds up each office **in its own currency**, in whole øre/paise, so the totals are exact.
- It converts each office total **once** with the rate from `settings.json` to get one combined number, and keeps the rate in the report.
- It counts which items were bought.

```
Offered ──yes──▶ Committed ──done──▶ Filled ──HR ok──▶ Claimed ──approved──▶ Reimbursed
   │               │                   ▲                  └──rejected──▶ ClaimRejected
   │ no / 30 min   │ 3 open h passed   │ HR down: Tick retries
   ▼               ▼
 next person (or Escalated if nobody is left; the next "empty" report tries again)
```

---

# Part 2: every folder, class and method

```
config/                     settings.json (budgets, timers, rates), people.json (who, where, away, admin)
src/SnackCounter/           the rules ONLY (no files, no console, no fakes)
  Models.cs                 what things look like (all small data types in one file)
  MonthlyReport.cs          the monthly report data
  Interfaces.cs             the doors to the outside world (chat, HR, clock)
  Services/                 the rules: what happens when (one file per service)
src/SnackCounter.HrSim/     separate project: the pretend HR system
src/SnackCounter.ChatSim/   separate project: the console chat, admin commands, and the config loaders
tests/SnackCounter.Tests/   automated tests, plus the test fakes (FakeChat, FakeClock)
```

## Models.cs (and MonthlyReport.cs)
| Class | What it is |
|---|---|
| `Office` | `Oslo` or `Bengaluru` |
| `Money` | Amount in **whole øre/paise** (`Minor`) plus `Currency`. `Money.Of(850.50m, "NOK")` makes one; `ToString()` shows "850.50 NOK" |
| `Employee` | Id, name, home office, `IsVolunteer`, `UnavailableUntil`, `VisitingOffice/Until`. `IsAvailable(now)`, `CurrentOffice(now)` |
| `RefillState` | `Offered, Committed, Filled, Claimed, Reimbursed, ClaimRejected, Escalated, Waiting` (Waiting = reported while the office is closed) |
| `OfficeHours` | One office's opening hours: time zone, open/close hour, work days |
| `Refill` | One empty-box event: state, `AssignedTo`, `Deadline`, amount, items, receipt, `ClaimId`, `AlreadyAsked`, `History`. `IsOpen()` (box still empty), `ShortId()` |
| `AcceptResult` | Answers to "yes": `Accepted, NotOfferedToYou, AlreadyTaken, OfferExpired, NoBudgetLeft` |
| `FillResult` | Answers to "done": `Filled, NotYours, InvalidAmount, WrongCurrency` |
| `CompletedRefill` | "Person X did a refill at time T", used for fairness |
| `OfficeBudget` | One office's currency, monthly budget and typical min/max purchase |
| `SpendRecord` | One ledger line: office, who, amount (original currency), items, when |
| `SnackConfig` | The three timers. No values in the code; they come from `settings.json` |
| `ClaimRequest` | What goes to HR: employee, amount, description, receipt image, idempotency key |
| `MonthlyReport`, `OfficeReportLine` | The report data; `ToText()` prints it |

## Interfaces.cs
An interface is a promise: "anything with these methods can be plugged in here". That's how a fake works in tests and a real one in production, without changing the rules.
- `IClock.GetNow()`
- `IChat.SendTo(person, text)`, `IChat.SendToOfficeContact(office, text)`
- `IHr.SubmitClaim(request)` returns the claim id
- `HrUnavailableException`: HR can't be reached, so retry later

## Services/
### `RefillService`: the rules
Each method: **check the state, change the state, send a message.** Every public method runs inside `lock`.

| Method | When | What |
|---|---|---|
| `ReportEmpty` | "empty" | Reuse the open refill, or create one and offer it; retry if escalated |
| `Accept` | "yes" | 3 checks, then Committed, plus the budget warning |
| `Decline` | "no" | Offer to the next person |
| `MarkFilled` | "done" | Checks, then Filled, ledger, over-budget flag, HR claim; tells the contact about a late receipt |
| `RecordHrDecision` | HR answers | Reimbursed, or ClaimRejected (volunteer + contact told) |
| `Tick` | Every minute | Hand on expired offers and commitments; retry claims |
| `GetRefills`, `FindActiveRefillFor` | Chat adapter | Lookups |
| `OfferToNextPerson`, `SendClaimToHr` (private) | | Pick and ask the next person; build and send the claim |

### `VolunteerSelector.PickNext`
Skip non-volunteers, people who are away, people in another office, and people already asked. Then pick: fewest refills in 90 days, then longest since last refill, then alphabetical. Returns `null` if nobody is left.

### `WorkingHours`
Time maths for opening hours, in each office's own time zone:
- `IsOpen`
- `NextOpen` (now, or the next 10:00 on a work day)
- `AddOpenTime` (start + 3 h, counting only open time)

If an office has no hours in config, it counts as always open, which keeps the tests simple.

### `BudgetService`
The ledger, plus money questions:
- `Record`, and `Remove` (HR rejected the claim, so the money goes back)
- `RecordsInMonth`
- `SpentInMonth`
- `RemainingMinor` (can go negative)
- `HasRoomForTypicalPurchase`
- `RemainingText` and `TypicalRange` (for messages)

### `ReportService.Build`
Per-office totals, the combined total (converted once, rounded once), and item counts. A missing exchange rate **throws an error**, rather than silently counting that office as zero.

## HR simulator (`src/SnackCounter.HrSim/`, separate project)
- `FakeHr`: stores claims as C-1, C-2…; the same idempotency key returns the same claim. `FailNextSubmissions` pretends HR is down. `SetStatus` approves or rejects.
- `HrClaim`: one stored claim and its status.

**Currency:** the claim goes to HR in the **receipt currency**. Example: Anna (Oslo, paid in NOK) visits Bengaluru and buys for 1,500 INR. The claim says 1,500 INR, HR converts it and pays her in NOK, and the 1,500 INR counts against the **Bengaluru** budget. Our code never converts claim money; it only converts for the report total.

## The chat simulator (separate project)
- `Program.Main`: loads config, builds everything, reads lines until `quit`.
- `SettingsLoader`, `Settings`: read and **check** `settings.json` (refuses missing values, numbers of 0 or less, unknown offices, an office with no budget, min > max). `SaveToFile` writes it back after an admin change.
- `PeopleLoader`: reads `people.json`, including `isAdmin`, `awayUntil` and `visitingOffice/Until`. Refuses duplicate ids. In real life this comes from the company directory and leave calendar.
- `SimClock`: pretend time that moves with `time +30m`.
- `CommandHandler`: the **chat adapter**. It turns text into method calls. A real Slack/Teams bot would replace only this part.
- `ConsoleChat`: prints the bot's messages (cyan for people, yellow for contacts).

| You type | It calls |
|---|---|
| `lars: empty` | `ReportEmpty` |
| `anna: yes` / `no` | `Accept` / `Decline` |
| `anna: done 850 chips, cola` | `MarkFilled` |
| `anna: away 5`, `anna: visiting oslo 10` | changes the person's status |
| `hr approve C-1`, `hr reject C-1 reason` | `RecordHrDecision` |
| `hr down`, `hr claims` | pretend HR is down / list claims |
| `time +30m` / `+2h` / `+3d` | move the clock, then `Tick` |
| `lars: set budget oslo 7000 300 1300` | admin only: `BudgetService.UpdateBudget`, then save |
| `lars: set timer accept 45` / `complete 24` | admin only: change a timer, then save |
| `status`, `report`, `help`, `quit` | |

## Tests
Each test: **set up, do something, check.** `TestBase` gives every test a fresh world with the budgets from the brief. Test-only fakes: `FakeClock` (jump forward in time), `FakeChat` (remembers messages; `WasTold(who, text)`, contacts are `"contact:Oslo"`).

| File | Proves |
|---|---|
| `RefillLifecycleTests` | Only one commit (also 50 at once); handed on after timeouts; late receipt goes to the contact; no second refill; escalation and retry; fair selection; visitors |
| `BudgetTests` | Exact totals per office and currency; visitor charged to the box's office; wrong currency or zero refused; low-budget warning; exact boundary; receipt above the limit is claimed only up to the limit; no budget left means nobody buys; monthly reset; admin budget change applies at once, bad values refused |
| `ClaimTests` | Claim only after the receipt, with the right fields; HR down means retry, no duplicate; idempotency key; approved and duplicate decision; rejected tells both |
| `ReportTests` | Exact totals plus combined; only that month; items ignoring case; missing rate is an error |
| `ConfigTests` | Real config files load; broken ones are refused; saved settings load back the same |

## Known shortcuts (said on purpose, not forgotten)
- Timers run around the clock in UTC, without pausing for office hours.
- Months are counted in UTC.
- A rejected claim is handed to people; there is no resubmit flow.
- Data is kept in memory. A database would hold it in production, where "first yes wins" becomes `UPDATE … WHERE state = 'Offered'`.
