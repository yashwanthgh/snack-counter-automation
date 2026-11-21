# Snack Counter Automation – Design

Each office has a snack box. When it is empty, a chat bot picks one fair person to buy snacks. That person spends within the budget and sends "done". The bot then claims the money from HR for them. Every month a report shows what was spent, who bought it, and which snacks were bought.

Also as a Google Doc: https://docs.google.com/document/d/1fzKB-0a8-dthGGJdXy9Zcb41dY2eLs7tCuCtF2GH2ag/edit?usp=sharing

Diagrams (draw.io): the system setup and states, and a full flowchart of every case. Code and diagrams: Git repo.

## 1. Your problems and how we fix them

| Problem in the brief | How it is prevented |
|---|---|
| Two people buy at the same time | Each office has only one open refill. A second "empty" report joins the open refill. Only the first "yes" wins: it is checked inside a lock, and anyone who answers later is told "already taken". |
| The same few people always do it | Fair order: the person with the fewest refills in the last 90 days is asked first. If two people are tied, the one who refilled longest ago goes first. Nobody is asked twice for the same refill. |
| People are away, travelling or visiting | Only people who are in that office today and willing to help are asked. A visitor's purchase counts against the office they bought for. |
| Nobody answers | Each person has 30 minutes to answer, then the next person is asked. If nobody is left, the office contact is told. |
| Someone says yes and then forgets | They have 3 open hours to buy and get one nudge. After that, or if they reply "no", the next person is asked. |
| Overspending | When the buyer says yes, they are told the money left this month. The claim cannot go above it. If no budget is left, nobody is asked and the contact is told. |
| People forget to claim, or a claim is made twice | The claim is sent to HR automatically after "done". The refill id is the duplicate-check key. If HR is down, the bot tries again later and never sends the claim twice. |
| Nobody knows the spend or which snacks get eaten | Monthly report for each office: spent, budget left, every purchase (when, who, how much, items), and a count of each snack. |

## 2. The flow, from empty to reimbursed

1. Someone sends **"empty"**. A refill is opened (**Offered**) and the fairest person is asked.
2. That person replies **yes** (**Committed**) and is told the spending limit. If they reply **no** or don't answer, the next person is asked.
3. They buy, then send **"done 245 chips, nuts"** (**Filled**). The spend is recorded at once.
4. The bot sends the claim to HR (**Claimed**).
5. HR approves it (**Reimbursed**) or rejects it (**ClaimRejected**, and the money goes back into the budget).

Side states: **Escalated** means nobody could be asked or there is no budget, and a human is told. **Waiting** means the box was reported outside office hours (10–17 local, Mon–Fri), so someone is asked when it opens.

## 3. How it works

- **The bot** runs each refill from empty to paid. It remembers the state, the buyer, who was already asked, the deadline and a history.
- **Fair picker** chooses the next person from those in that office today and willing to help, using their refill history.
- **Budget** keeps each office's monthly budget and every purchase, in whole øre/paise so there are no rounding errors.
- **Monthly report** is built from those purchases.
- **Office hours** are set per office, in its own time zone.
- **Outside systems are kept separate:** chat, HR and the people directory. Here they are simulated, so the real ones can be plugged in later without changing the rules.
- **Settings** (budgets, timers, hours) live in a file, not in the code. Admins can change them from chat.
- **A timer** checks every minute for missed deadlines, and retries HR if it was down.

In production this would be API Gateway, one Lambda function (one copy at a time), DynamoDB, and an EventBridge timer every minute. That costs about $0–1 a month, compared with about $10 for one always-on container.

## 4. Trade-offs

- **People report "empty" and "done"; there is no sensor.** This is cheap and needs no hardware. But a fake claim or a false "empty" is possible. HR checking the receipt is the safety net. A weight sensor is a later upgrade.
- **The amount the buyer types is trusted.** HR checks it against the receipt, and it is capped by the limit.
- **Spend is counted at "done", not when HR approves.** The budget is right at once. If HR rejects, the money is given back.
- **Snacks are free text.** This is easy for people, but "crisps" and "chips" are counted as different snacks. A fixed snack list would be better.
- **Data is kept in memory.** Simple and fast to test, but lost on restart. Production uses DynamoDB.
- **Months are counted in UTC.** A purchase just after local midnight on the 1st can land in the previous month.

## 5. What we won't build (now)

Real chat or HR connections, a database, login, receipt photos or OCR, weight sensors, public holidays, more than one box per office, budget carry-over, currency conversion, and automatic resubmission of rejected claims.

## 6. Our questions and assumptions

| Assumption | Why |
|---|---|
| One snack box per office | The brief talks about one box. Supporting more boxes is a small change later. |
| A monthly budget in the office's own currency, not carried over | The brief describes a monthly budget. Carry-over is a finance decision, not ours. |
| HR approves claims and checks receipts | HR already does this for other expenses. We should not copy that work. |
| The people directory says who is in which office and willing to help | We should not own staff data. We only read it. |
| 30 minutes to answer, 3 open hours to buy | These are long enough to be fair and short enough that the box isn't empty all day. Both can be changed from config or chat. |
| Office hours: 10–17 local, Mon–Fri | We should not message people at night or at the weekend. |
| The reporter can also be asked to buy | They are already at the box. The fair order still spreads the work. |
| Anyone can say "no" at any point before "done" | It is better to know early than to wait for a timeout. |
| Only admins change budgets and timers | Money settings need control. |
| Chat gives us the sender's id | So we trust the sender id and need no extra login. |

**Questions for you:** Should a rejected claim be resubmitted automatically? Are there public holidays per office? Should unspent budget carry over? Is there one box per office, or could there be more?
