# Snack Counter Automation

Core logic for keeping the Oslo and Bengaluru snack counters stocked: who refills, when it's handed on, the budget check, the HR reimbursement claim, and the monthly report. The chat tool and the HR system are simulated.

Design document: [docs/DESIGN.md](docs/DESIGN.md), also as a [Google Doc](https://docs.google.com/document/d/1fzKB-0a8-dthGGJdXy9Zcb41dY2eLs7tCuCtF2GH2ag/edit?usp=sharing) (assumptions, trade-offs, what is deliberately not built).
Code walkthrough in plain words: `WALKTHROUGH.md`.

Budgets, timers and exchange rates are in `config/settings.json`; people and their status (away, visiting) in `config/people.json`. Nothing is hard-coded.

## Run

Requires the .NET 8 SDK.

```bash
dotnet test                                              # all tests
dotnet run --project src/SnackCounter.ChatSim            # interactive chat simulator (type 'help')
dotnet run --project src/SnackCounter.ChatSim < demo.txt # scripted demo
```

## Layout

```
config/                     settings.json (budgets, timers, rates), people.json (people, admin, away/visiting)
src/SnackCounter/           core rules only: no files, no console, no fakes
  Models.cs  MonthlyReport.cs  Interfaces.cs (IChat, IHr, IClock)  Services/
src/SnackCounter.HrSim/     simulated HR system (FakeHr): claims, idempotency, outages
src/SnackCounter.ChatSim/   simulated chat (console): commands, admin settings, config loaders
tests/SnackCounter.Tests/   NUnit tests + test fakes (FakeChat, FakeClock)
```

Admins (`"isAdmin": true` in people.json) can change budgets and timers from chat; changes apply at once and are saved to settings.json:
`lars: set budget oslo 7000 300 1300`, `lars: set timer accept 45`.

## Refill lifecycle

```
Offered -> Committed -> Filled -> Claimed -> Reimbursed (or ClaimRejected: volunteer + office contact told)
   |          |           |
   |          |           +- HR down: stays Filled, retried with the same key (never a duplicate claim)
   |          +- not done in 24h -> offered to the next person
   +- "no" / no answer in 30 min -> offered to the next person, or Escalated if nobody is left
```

## What the tests prove

- Two people cannot both commit to the same refill (including 50 parallel "yes" replies).
- A refill nobody answers, or nobody completes, is handed to someone else; a late receipt goes to the office contact.
- A repeat "empty" report never creates a second refill; an escalated refill is retried on the next report.
- Only available volunteers in that office are asked; fewest recent refills first; visitors count.
- Budgets and totals are exact in NOK and INR; a visitor's purchase is charged to the box's office.
- On "yes" the buyer is told their limit (budget left). Only up to the limit is claimed; the rest is their own cost. No budget left: nobody buys, the contact decides.
- HR claims are sent only after the receipt and never duplicated on retry.
- The monthly report is exact per office and combined with a supplied exchange rate.
- Broken config files are refused with a clear message.

## Known shortcuts

- Timers run around the clock (UTC); they don't pause outside office hours.
- Months are counted in UTC.
- A rejected HR claim is handed to people (volunteer + office contact); there is no resubmit flow.
- Data is in memory; production would use a database, with "first yes wins" as a conditional update.

## Web UI (optional)

```
dotnet run --project src/SnackCounter.Web
```

- http://localhost:5080/ : **test console**. Shows every message, the refills (with history), budgets, HR claims (approve/reject buttons) and people. Has time buttons and a box for any command.
- http://localhost:5080/person.html?id=anna : **one person's chat**. Shows only what that person would see. Also has "office contact" views.

Both pages use the same commands as the terminal simulator, and share one simulated world.
