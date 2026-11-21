# Rules and decisions

Every rule the snack counter follows, why we chose it, and what we could have done instead.

## In short

We always picked **safe and simple** over fast and clever. The decisions that matter most:

| Decision | Why it matters | Rule |
|---|---|---|
| Only one open refill per office, and the first "yes" wins | Two people can never buy for the same empty box | [1](#rule-1), [8](#rule-8) |
| Fair order: fewest refills in the last 90 days goes first | The same few people don't end up doing it all | [5](#rule-5) |
| 30 minutes to answer, 3 office hours to buy, "no" allowed any time before "done" | The box never waits on one person for long | [7](#rule-7), [10](#rule-10), [10b](#rule-10b) |
| The bot only messages people 10:00-17:00, Mon-Fri, office time | Nobody is asked at night or at the weekend | [11](#rule-11) |
| The buyer is told how much they can spend when they say yes | No overspending, and no approval step that blocks the box | [14](#rule-14) |
| Visitors: a purchase counts against the office it was bought for, in that office's currency. HR pays the buyer back in their own home currency | Each office's budget stays correct, and nobody loses money when they buy abroad | [12](#rule-12), [18](#rule-18) |
| The money is claimed from HR automatically, and never twice | Nobody forgets to claim, and nobody is paid twice | [19](#rule-19), [20](#rule-20) |
| The budget goes down at purchase, and back up if HR rejects | The budget left is always up to date | [22b](#rule-22b) |
| People report "empty" and "done"; there is no sensor. The person asked can reply "full" if the box is not empty | Cheap and simple. A false "empty" is closed by "full"; a fake claim is caught by HR's receipt check | [16b](#rule-16b) |

**Build first before real use:** holidays per office, a real database, real away/visiting data, real chat and HR connections, and a weight sensor under each box.

## All rules

Click a rule to read it in full. Each one has the same parts: **Rule** (what happens), **Why**, **Pros**, **Cons**, **Better way?** and **Why not**.

Two reasons come up often in "Why not": this is a take-home task with a deadline, and every extra feature is more code to read, test and explain.

### Reporting an empty box

<a id="rule-1"></a>
<details>
<summary><b>1.</b> Only one open refill per office</summary>

**Rule:** If the Oslo box is already being handled, a second "empty" report does NOT create a second refill. The same refill is returned. "low" (running low) works exactly like "empty": it starts a refill early, and a later "empty" joins that same refill.

**Why:** The brief's main problem is "two people buy at the same time". Two open refills for one box = two people asked = two people may buy.

**Pros:** Makes double buying impossible by design, not by luck. Simple to explain: one box, one job.

**Cons:** If an office ever has two snack boxes (two floors), they share one refill.

**Better way?:** Track a refill per BOX instead of per office (box id in the report).

**Why not:** The brief talks about one box per office. Adding boxes adds data, config and tests for a case nobody asked for. Easy to add later: replace "office" with "box" in FindOpenRefill.

</details>

<a id="rule-2"></a>
<details>
<summary><b>2.</b> A second report nudges the buyer</summary>

**Rule:** If someone already said yes and the box is reported empty again, the buyer gets "the box was reported empty again. Still on it?"

**Why:** A second report means people are still waiting. The buyer is the one who can do something about it.

**Pros:** Turns a "duplicate" report into something useful instead of ignoring it.

**Cons:** If ten people report, the buyer gets ten nudges (could feel like spam).

**Better way?:** Nudge at most once per hour.

**Why not:** Small offices, few reports. Easy to add if it becomes annoying.

</details>

<a id="rule-3"></a>
<details>
<summary><b>3.</b> The reporter is treated like everyone else, and is thanked</summary>

**Rule:** The person who reports the box is NOT skipped. If they are next in the fair order (rule 5), they are asked to refill it. If someone else is asked, the reporter gets "Thanks, Rahul has been asked".

**Why:** The reporter is standing next to the box, so they are often the most convenient person. The fair order already spreads the work over time. (We tried "reporter asked last" and decided against it: one simple order is easier to explain and test.)

**Pros:** One rule for everyone. The person on the spot can act at once.

**Cons:** Some people may feel "if I report it, I get the job" and stop reporting.

**Better way?:** Skip the reporter, or ask them "do you want to do it yourself?".

**Why not:** Adds a special case. If reporting drops off in real use, it is a two-line change in OfferToNextPerson.

</details>


### Choosing who buys

<a id="rule-4"></a>
<details>
<summary><b>4.</b> Who can be asked</summary>

**Rule:** Only people who are (1) volunteers, (2) not away, (3) in that office right now. Visitors count in the office they are visiting.

**Why:** The brief says people work from home, travel and visit other offices. Asking someone who is not there wastes 30 minutes.

**Pros:** Never asks someone who cannot physically buy snacks. Volunteers only = nobody is forced to do it.

**Cons:** "Away" and "visiting" must be kept up to date. If someone forgets to say they are working from home, they are still asked (and simply say no).

**Better way?:** Read status from the real calendar / HR leave system / office check-in.

**Why not:** No such system is available in a take-home. We simulate it with "away N" and "visiting <office> N" commands and the people.json file. The code only needs IsAvailable() and CurrentOffice(), so a real source can be plugged in later without changing the rules.

</details>

<a id="rule-5"></a>
<details>
<summary><b>5.</b> Fair order: fewest refills first</summary>

**Rule:** Among people who can be asked, pick the one with the fewest refills in the last 90 days. Tie → the one who waited longest since their last refill. Still a tie → by id (so it is always the same, testable answer).

**Why:** The brief says the same few people end up doing it all. Counting refills spreads the work.

**Pros:** Fair over time. Predictable. Easy to explain to staff. Saying "no" does not count as a refill, so nobody is punished for being busy.

**Cons:** Someone who always says "no" is asked first every time (they never get a refill counted). Amount spent is ignored (a 1,200 NOK trip = a 300 NOK trip).

**Better way?:** Count "offers declined" too, or use a points system.

**Why not:** Penalising "no" turns a volunteer system into a duty roster, which changes the culture. A simple count is easier to defend. The 90-day window is configurable.

</details>

<a id="rule-6"></a>
<details>
<summary><b>6.</b> Never ask the same person twice for one refill</summary>

**Rule:** Once asked (yes, no or no answer), a person is not asked again for that same refill.

**Why:** Otherwise the list can loop forever between two people who keep ignoring it.

**Pros:** The list always ends: either someone says yes or the contact is told.

**Cons:** Someone who said "no" at 9:00 but is free at 11:00 is not asked again.

**Better way?:** Reset the list after a few hours.

**Why not:** A new "empty" report already restarts an escalated refill (rule 9). Good enough.

</details>


### Saying yes / no and the timers

<a id="rule-7"></a>
<details>
<summary><b>7.</b> 30 minutes to answer, then the next person</summary>

**Rule:** The person asked has 30 minutes to say yes. "No" or silence → next person.

**Why:** Asking one person at a time is what stops two people buying at once. A timeout stops one silent person blocking the box all day.

**Pros:** Only ever one person holds the job. No double buying.

**Cons:** Slow: if 5 people ignore it, the box waits 2.5 hours.

**Better way?:** Ask 2-3 people at once, first yes wins, others get "already taken".

**Why not:** That is exactly how double buying happens if someone misses the "already taken" message. One at a time is slower but safe. The time is configurable (admin: "set timer accept 15").

</details>

<a id="rule-8"></a>
<details>
<summary><b>8.</b> First "yes" wins, even at the same millisecond</summary>

**Rule:** Every change runs inside a lock. If two "yes" arrive together, the first wins and the second gets "already taken". A late "yes" after the offer moved on gets "not offered to you".

**Why:** This is the brief's main problem. It must be impossible, not unlikely.

**Pros:** Proven by a test that sends 50 "yes" at the same time: exactly 1 wins.

**Cons:** A lock only works inside ONE running program. Two servers would each have their own lock.

**Better way?:** In a real database: UPDATE refills SET state='Committed' WHERE id=@id AND state='Offered'. Only one update can succeed.

**Why not:** We keep data in memory (no database) for the take-home. The comment in RefillService shows the database version.

</details>

<a id="rule-9"></a>
<details>
<summary><b>9.</b> Nobody available → tell the office contact</summary>

**Rule:** If nobody can be asked, the refill becomes "Escalated" and the office contact is told. The next "empty" report tries the whole list again.

**Why:** The system must never fail silently. A human decides what to do.

**Pros:** No empty box is forgotten. Simple.

**Cons:** An escalated refill waits until someone reports again. It does not retry on its own when people come back.

**Better way?:** Retry automatically every few hours, or when someone comes back from away.

**Why not:** Kept simple; a new report is a natural signal that it is still needed.

</details>

<a id="rule-10"></a>
<details>
<summary><b>10.</b> 3 open hours to buy, then handed on</summary>

**Rule:** After "yes", the buyer has 3 hours of OPEN office time to send "done" (settings.json "completeWindowHours", admin: "set timer complete 3"). If not, they are told "handed to someone else, please don't buy" and the next person is asked. Example (Oslo, open 10-17): "yes" at 16:00 → 1 hour today + 2 hours tomorrow → deadline 12:00 the next day.

**Why:** People say yes and then forget. The box should not stay empty for long. It used to be 24 hours, but that let the box stay empty for a whole day. 3 hours is enough for a lunch break or an afternoon trip to the shop. This only works because the timer pauses when the office is closed (rule 11): otherwise a "yes" at 16:00 would run out at 19:00, at night.

**Pros:** The box is refilled the same day, or the next morning at the latest.

**Cons:** If the buyer bought late without reading the message, they paid and the job was given away (see rule 15 for how that is handled). 3 hours is tight if the shop is far away; an admin can raise it from chat.

**Better way?:** Remind the buyer 30 minutes before the time runs out.

**Why not:** One more timer and message. Easy to add, not essential.

</details>

<a id="rule-10b"></a>
<details>
<summary><b>10b.</b> The buyer can say "no" after "yes"</summary>

**Rule:** After saying yes, the buyer can still reply "no" (for example when the bot asks "reported empty again, still on it?"). They are told "please don't buy", and the next fair person is asked at once. The yes message and the nudge both say "can't do it? reply no".

**Why:** Plans change (sick, meeting, no time). Without this, the box stays empty until the 24-hour timer runs out, even though everyone knows nobody is going.

**Pros:** The box moves on in minutes instead of up to 24 hours. Same word ("no") as before, nothing new to learn. If they already bought anyway, rule 15 (late receipt) makes sure a person checks it.

**Cons:** Someone could say yes and no to "pass the job on", but they are never asked again for that refill, so nothing is gained.

**Better way?:** Ask why ("sick / no time / no money") to spot patterns.

**Why not:** Not needed to keep the box filled. Easy to add later.

</details>

<a id="rule-11"></a>
<details>
<summary><b>11.</b> The bot sleeps outside office hours (10:00-17:00, Mon-Fri, office time)</summary>

**Rule:** Each office has opening hours in its OWN time zone (settings.json "officeHours": Oslo = Europe/Oslo, Bengaluru = Asia/Kolkata, 10-17, Mon-Fri).

- Outside those hours nobody is asked. A report at 20:00 makes the refill "Waiting"; the reporter is told "someone will be asked at 10:00". At opening time the 1-minute Tick asks the first fair person.
- The 30-minute and 3-hour timers only count open time. Nothing runs out, and nobody is handed a job, at night or at the weekend.
- Replies to what a person types themselves (yes, done...) still come at once, any time: they are awake, they just wrote.

**Why:** Without this, a report at 21:00 went through the whole list overnight while everyone slept, and was escalated by morning. People were pinged at night.

**Pros:** No night or weekend messages. A late "yes" keeps its time until morning. Each office uses its own clock (10:00 in Bengaluru is 05:30 in Oslo). All in config: hours, days and time zone can change without code.

**Cons:** An empty box reported at 17:30 on Friday waits until Monday 10:00. Public holidays are not known: the bot works on 17 May or Diwali. Times inside the system are still shown in UTC (chat messages too).

**Better way?:** A holiday calendar per office. Show times in the reader's own time zone.

**Why not:** Holidays need a list per country per year (or a calendar service), which is more than a take-home needs. Showing local times is a small change to the message text only, listed as a next step.

</details>


### Budget and money

<a id="rule-12"></a>
<details>
<summary><b>12.</b> Each office has a monthly budget in its own currency</summary>

**Rule:** Oslo 6,000 NOK, Bengaluru 15,000 INR per month. Resets on the 1st. Unused money does not carry over.

**Why:** From the brief. Each office pays in its own money; converting would add exchange-rate noise to a simple limit.

**Pros:** Office managers see numbers in the currency they use.

**Cons:** No carry-over means a quiet month's savings are lost.

**Better way?:** Carry over, or a yearly budget.

**Why not:** The brief describes a monthly budget. Carry-over is a finance policy decision, not ours to invent.

</details>

<a id="rule-13"></a>
<details>
<summary><b>13.</b> Months are counted in UTC (known shortcut)</summary>

**Rule:** "This month" is the UTC month.

**Why:** One clock for everything.

**Cons:** A purchase in Bengaluru at 02:00 on 1 Dec local time is still 30 Nov in UTC, so it counts in November.

**Better way?:** Use each office's local time zone for the month.

**Why not:** Only matters for a few hours each month. Marked "shortcut:" in the code.

</details>

<a id="rule-14"></a>
<details>
<summary><b>14.</b> The buyer is told a spending limit when they say yes</summary>

**Rule:** On "yes" the buyer is told "you can spend up to X" where X = budget left. If the receipt is above X, only X is claimed; the rest is their own cost. If NO budget is left, the buyer is told "don't buy" and the contact decides. The contact also gets a "budget low" warning if less than one typical purchase is left.

**Why:** Your decision: the system cannot stop someone spending in a shop, but it can warn them before. If they were warned and spent more, that is their choice.

**Pros:** The budget is never broken by purchases. The buyer knows the rule BEFORE paying, so it is fair. No approval step that could leave the box empty.

**Cons:** A buyer may lose a little money. The limit is fixed at "yes"; if an admin lowers the budget afterwards, the office can still go over (contact is told).

**Better way?:** Ask a manager to approve purchases above the limit.

**Why not:** We built an approval flow first and removed it: it could block an empty box for days waiting for a manager (a "deadlock"). A clear limit up front is simpler and nobody waits.

</details>

<a id="rule-15"></a>
<details>
<summary><b>15.</b> Amount must be above zero and in the office's currency</summary>

**Rule:** "done 0" or "done -50" is refused. "done 500" in Oslo is NOK; a receipt in another currency for that office is refused.

**Why:** Stops typos and nonsense from entering the budget and HR.

**Pros:** Cheap check, catches real mistakes.

**Cons:** Does not catch "850" typed as "8500".

**Better way?:** Warn when the amount is far above the typical maximum.

**Why not:** The spending limit (rule 14) already caps what can be claimed.

</details>

<a id="rule-16"></a>
<details>
<summary><b>16.</b> The typed amount is trusted; HR checks the receipt</summary>

**Rule:** The system takes the amount the buyer types plus the receipt photo. It does not read the photo. HR compares them when approving.

**Why:** Checking receipts is already HR's job, and the reason "reject" exists.

**Pros:** Simple. No image-reading service needed.

**Cons:** Until HR checks, a wrong amount is counted in the budget.

**Better way?:** Read the receipt automatically (OCR / AI) and compare.

**Why not:** Out of scope for the brief, costs money, and still needs a human for blurry receipts. Can be added in front of MarkFilled later.

</details>

<a id="rule-16b"></a>
<details>
<summary><b>16b.</b> We trust people to report "empty" and "filled" (no sensor)</summary>

**Rule:** The box is "empty" only when a person says so, and "filled" only when the buyer says "done". Nothing checks the box itself.

**Why:** The brief is about the chat and HR process. There is no hardware.

**Pros:** Works with no extra devices. Easy to test.

**Cons:** Three weak spots:

- If nobody reports, the box can stay empty for a long time.
- A fake claim is possible: someone sends a real receipt but the snacks never reach the box (taken home). HR only sees the receipt, not the box.
- A false "empty": the box is full, but someone reports it empty anyway (by mistake or as a joke). A refill starts and money may be spent on snacks that were not needed.

What limits the damage today: only one refill per office can be open, the person asked can look at the box and reply "full" (the refill is closed and nothing is bought), only the person who said yes can claim, the claim is capped at the spending limit, and HR can reject.

**Better way?:** A weight sensor under the snack box.

- It reports "empty" by itself when the weight is low. Nobody has to say anything, so the box is never forgotten.
- It ignores an "empty" report when the box is still heavy (full), so a false report never starts a refill.
- It sees the weight go up after "done". If the buyer says done but the weight did not change, the claim is flagged for HR. This catches fake claims.

Without a sensor, a cheaper option: after "done", ask one other person in the office "Is the box full now?", and flag a claim if the box is reported empty again within a few hours.

**Why not:** A sensor is hardware: it must be bought, installed and connected in both offices, which is outside a take-home task. The design is ready for it: the sensor would call the same ReportEmpty that the chat calls today, so nothing else changes.

</details>

<a id="rule-17"></a>
<details>
<summary><b>17.</b> Money is stored as whole øre / paise</summary>

**Rule:** 850.50 NOK is stored as 85050 (a whole number) plus "NOK".

**Why:** Decimal fractions in normal number types cause rounding errors (0.1 + 0.2 = 0.30000000000000004).

**Pros:** Totals are always exact. Standard practice for money.

**Cons:** Must divide by 100 when showing it.

**Better way?:** None really; this is the industry standard.

</details>

<a id="rule-18"></a>
<details>
<summary><b>18.</b> A visitor's purchase belongs to the office they bought for</summary>

**Rule:** Anna (Oslo, paid in NOK) buys 1,500 INR of snacks in Bengaluru. The 1,500 INR counts against BENGALURU's budget. The claim goes to HR in INR. HR converts and pays Anna in NOK.

**Why:** The snacks are for the Bengaluru box. HR already handles currency for travel expenses; we should not duplicate that.

**Pros:** Budgets stay correct per office. No exchange rates in our code for claims.

**Cons:** We rely on HR handling foreign-currency claims.

**Better way?:** None needed; this is how expense systems usually work.

</details>


### HR claim

<a id="rule-19"></a>
<details>
<summary><b>19.</b> The claim is sent automatically after "done"</summary>

**Rule:** When the buyer sends "done <amount> <items>" with the receipt, the claim is created in HR straight away: who, amount, currency, items, receipt photo.

**Why:** The brief says people forget or give up on claiming. Automatic = nobody has to remember.

**Pros:** Nobody pays for the office out of their own pocket and forgets to claim.

**Cons:** None big.

</details>

<a id="rule-20"></a>
<details>
<summary><b>20.</b> If HR is down, try again, never twice</summary>

**Rule:** If HR does not answer, the refill stays "Filled" and the claim is retried later. Every try uses the same key (the refill id), so HR creates at most one claim.

**Why:** Networks fail. A retry without a key can create two claims = paid twice.

**Pros:** Never lost, never duplicated. Proven by a test.

**Cons:** Retries forever with no limit or alert.

**Better way?:** After N failed tries, tell the office contact.

**Why not:** Small addition, not needed to show the idea.

</details>

<a id="rule-21"></a>
<details>
<summary><b>21.</b> Approved → done. Rejected → tell people, no automatic resubmit (known shortcut)</summary>

**Rule:** HR approves → "Reimbursed", buyer is thanked. HR rejects → buyer and office contact are told the reason. A duplicate decision is ignored.

**Why:** Rejections are rare and need a human conversation anyway.

**Cons:** The buyer cannot fix and resend the claim inside our system.

**Better way?:** "resubmit <new receipt>" command.

**Why not:** We built it and removed it: lots of code for a rare case. Marked "shortcut:" in the code.

</details>

<a id="rule-22"></a>
<details>
<summary><b>22.</b> A late receipt is not claimed, but a human is told</summary>

**Rule:** If someone sends "done" for a refill that was already handed to someone else, it is NOT sent to HR. The office contact is told: "X paid Y after it was handed on. Please check."

**Why:** They spent their own money in good faith. The code should not silently ignore it, but also should not pay twice automatically.

**Pros:** No double payment, and nobody is quietly out of pocket.

**Cons:** Needs a human to sort out.

**Better way?:** None simple; this is a judgement call.

</details>


### Reports and settings

<a id="rule-22b"></a>
<details>
<summary><b>22b.</b> The budget goes down at purchase, not at HR approval</summary>

**Rule:** When the buyer sends "done", the amount is counted as spent straight away. The page shows it as "waiting for HR" until HR approves. If HR REJECTS the claim, the amount is given back to the budget.

**Why:** The money is gone the moment the snacks are bought. HR takes 1-10 days. If we waited for HR, the next buyer would be told they can spend money that is already spent, and the office would go over budget.

**Pros:** The spending limit (rule 14) is always correct. A rejected claim does not eat the budget.

**Cons:** For a few days the budget shows money as spent that HR may still refuse.

**Better way?:** Two numbers everywhere: "committed" and "approved" (the web page shows both).

**Why not:** That is what we do on the page; the limit itself must use "committed", so there is nothing more to build. BUG WE FOUND AND FIXED: At first, a REJECTED claim still counted as spent. The company never paid, but the budget was smaller anyway. Now a rejection gives the amount back (RefillService.RecordHrDecision → BudgetService.Remove), and a test checks it.

</details>

<a id="rule-23"></a>
<details>
<summary><b>23.</b> Monthly report</summary>

**Rule:** Per office: budget, spent, number of refills, in that office's currency. Plus one combined total in a chosen currency (NOK now) using fixed rates from settings.json. Plus what items were bought and how often.

**Why:** Managers want per-office numbers in their currency, and finance wants one total.

**Pros:** Rates are saved with the report, so an old report never changes.

**Cons:** Fixed rates are not live rates.

**Better way?:** Let the user choose the combined currency ("report INR"), live rates.

**Why not:** Choosing the currency is a small change (only the setting is fixed now). Live rates need an outside service; for a snack report, fixed monthly rates are accurate enough.

</details>

<a id="rule-23b"></a>
<details>
<summary><b>23b.</b> Snacks are free text, counted by name ("which snacks get eaten")</summary>

**Rule:** The buyer types the snacks after the amount: "done 850 chips, cola, kitkat". They are split at the commas, made lower-case and saved with the purchase. The report counts each name for the month ("chips x 2, cola x 1"), and the list also shows who bought what, when and for how much. "Most eaten" = take this month's purchases, count each name, sort. The top one is the answer. (In a database: read the month's rows, count in code.)

**Why:** The brief says "nobody knows which snacks actually get eaten". Typing the names on "done" costs the buyer a few seconds and needs no setup.

**Pros:** Simple. Works for any snack, no list to keep up to date. Case does not matter ("Chips" = "chips").

**Cons:** (a) Different spellings do not match: "chips", "Lays" and "potato chips" count as three snacks.

- No quantity: 1 cola counts the same as 12 colas.
- Bought is not eaten. We only see what was BOUGHT. A snack bought often is probably popular, but it is a guess.

**Better way?:** (1) A fixed snack list in config (chips, cola, chocolate, fruit...). The bot matches what people type to the list, so spellings join up. (2) Optional amounts: "done 850 chips x3, cola x12", so units are counted. (3) Ask what ran out first: "empty chips". This is the closest thing to "eaten": if chips run out first in most reports, chips are the favourite. Then the report can say: "Most bought: cola (36 units). Runs out first most often: chips (5 of 7 reports)." Best of all: a weight sensor per shelf (see rule 16b), but that is hardware.

**Why not:** The brief asks for the refill flow, choosing who buys and the budget check; snack tracking is a side benefit. Free text answers the question roughly with no extra setup. (1)-(3) are small changes on top (config list, a parser change, one optional word on "empty") and are the first thing to add.

</details>

<a id="rule-24"></a>
<details>
<summary><b>24.</b> Only admins can change budgets and timers from chat</summary>

**Rule:** "lars: set budget oslo 7000 300 1200", "set timer accept 45". Only people with isAdmin in people.json. Bad values refused (0 < min <= max <= monthly). Saved to settings.json.

**Why:** Budgets change; editing code to change a number is wrong.

**Pros:** No restart, no developer needed. Takes effect at once.

**Cons:** No history of who changed what.

**Better way?:** Keep an audit log of changes.

**Why not:** Small addition, not asked for.

</details>

<a id="rule-25"></a>
<details>
<summary><b>25.</b> Nothing is hard-coded</summary>

**Rule:** Budgets, timers, rates and people come from config/settings.json and config/people.json.

**Why:** Numbers change more often than code.

**Pros:** Change a number without rebuilding.

**Cons:** Bad files must be checked (they are: the loader refuses bad values).

</details>


### How it is built

<a id="rule-26"></a>
<details>
<summary><b>26.</b> Data is kept in memory (no database)</summary>

**Why:** Take-home scope; the brief asks for simulated systems.

**Cons:** Everything is lost on restart. Only one server can run.

**Better way?:** A database (e.g. SQL Server / Postgres).

**Why not:** Adds setup for the reviewer. All data lives in a few lists, so swapping in a database is a contained change.

</details>

<a id="rule-27"></a>
<details>
<summary><b>27.</b> Chat and HR are behind interfaces (IChat, IHr, IClock)</summary>

**Why:** The core rules do not care whether messages go to a console, a web page, Slack or Teams. Tests use fakes; the simulator uses console/web.

**Pros:** The real Slack/Teams/HR integration replaces one small class each. The clock can be moved forward in tests, so "30 minutes later" takes 0 ms.

</details>

<a id="rule-28"></a>
<details>
<summary><b>28.</b> A "tick" every minute checks the timers</summary>

**Rule:** Tick() looks at all open refills: expired offers move on, HR claims are retried, and "Waiting" refills start when the office opens.

**Why:** Simplest way to make timers work.

**Cons:** Up to one minute late. Looks at every refill (fine for a handful of offices). On AWS it runs about 43,200 times a month, even when nothing is open. COST: practically zero. EventBridge Scheduler and Lambda each have about a million or more free calls a month; 43,200 is far below that, and most runs find no open refill and finish in milliseconds. Well under $1 even without the free tier.

**Better way?:** A scheduled job per deadline (only a few hundred calls a month).

**Why not:** More moving parts (set, replace and cancel each timer) to save almost nothing. At this size the every-minute tick is simpler and costs the same.

</details>

<a id="rule-29"></a>
<details>
<summary><b>29.</b> Separate projects</summary>

- **SnackCounter**: only the rules (no files, no screen)
- **SnackCounter.HrSim**: fake HR
- **SnackCounter.ChatSim**: terminal chat, loading config, admin commands
- **SnackCounter.Web**: the two web pages (test console, one person's chat)
- **SnackCounter.Tests**: 43 automated tests

**Why:** The rules can be read and tested on their own. Simulators can be thrown away when real systems are connected.

</details>
