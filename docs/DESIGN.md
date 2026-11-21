# Snack Counter Automation – Design

**Background:** the Oslo and Bengaluru offices each have a snack box, refilled by hand today. When it runs out, nobody knows who should buy it. This causes the problems listed in the table below.

**The idea:** when the box is empty, a chat bot picks one fair person to buy snacks. That person spends within the budget and sends "done". The bot claims the money from HR. One round of this (box empty, someone buys, they get paid back) is called a refill.

Diagrams (draw.io): [system setup and states](https://app.diagrams.net/#Uhttps%3A%2F%2Fraw.githubusercontent.com%2Fyashwanthgh%2Fsnack-counter-automation%2Fmain%2Fdocs%2Fdiagrams%2Fsnack-counter.drawio), [full flowchart](https://app.diagrams.net/#Uhttps%3A%2F%2Fraw.githubusercontent.com%2Fyashwanthgh%2Fsnack-counter-automation%2Fmain%2Fdocs%2Fdiagrams%2Frefill-flowchart.drawio). Code: [github.com/yashwanthgh/snack-counter-automation](https://github.com/yashwanthgh/snack-counter-automation)

## Your problems and how we fix them

| Problem in the brief | How it is prevented |
| :---- | :---- |
| Two people buy at the same time | Each office has only one open refill. A second "empty" report joins the open refill. Only the first "yes" wins (checked in a lock); later ones get "already taken". |
| The same few people always do it | Fair order: the person with the fewest refills in the last 90 days is asked first. If two people are tied, the one who refilled longest ago goes first. |
| People are away, travelling or visiting | Only people who are in that office today and willing to help are asked. A visitor's purchase counts against the office they bought for, and HR pays them back in their payroll currency. |
| The box sits empty because everyone assumes someone else will refill it | Anyone sends "empty" in chat, and the bot asks one named person, so it is never "someone else". They have 30 minutes to answer, then the next person is asked. If nobody is left, the office contact is told. |
| Someone says yes and then forgets | They have 3 office hours to buy and get one nudge. After that, or if they reply "no", the next person is asked. |
| Overspending | At "yes" the buyer is told the money left this month. The claim cannot go above it. If no budget is left, nobody is asked and the contact is told. |
| People wait weeks for their money, or forget to claim | The claim (amount, currency, description, receipt image, employee id) is sent to HR the moment they say "done", so the wait is only HR's 1–10 days. Each claim carries the refill's id, so HR ignores a copy sent twice. |
| Nobody knows the spend or which snacks get eaten | One monthly report: each office in its own currency plus one combined total for management, budget left, every purchase (when, who, how much, what) and a count per snack. |

## The flow, from empty to reimbursed

1. Someone sends "empty" or "low": a refill opens (Offered: waiting for a yes), the fairest person is asked.  
2. That person replies **yes** (Committed: they will buy) and hears the spending limit. No, or no answer: next person.  
3. They buy, then send **"done 245 chips, nuts"** (Filled: box is full). The spend is recorded at once.  
4. The bot sends the claim to HR (Claimed: waiting for HR).  
5. HR approves it (**Reimbursed**) or rejects it (**ClaimRejected**, and the money goes back into the budget).

Side states: **Escalated** means nobody could be asked or there is no budget, and a human is told. **Waiting** means the box was reported outside office hours (10–17 local, Mon–Fri), so someone is asked when it opens.

## How it works

* **The bot** runs each refill from empty to paid. It remembers state, buyer, who was asked, deadline, history.  
* **Fair picker** chooses the next person in that office today and willing to help, using their refill history.  
* **Budget** keeps each office's monthly budget and every purchase, in whole øre/paise (no rounding errors).  
* **A monthly report** is built from those purchases.  
* **Office hours** are set per office, in its own time zone.  
* **Outside systems are kept separate:** chat, HR and the people directory. Here they are simulated, so the real ones can be plugged in later without changing the rules.  
* **Settings** (budgets, timers, hours) live in a file, not in the code. Admins can change them from chat.  
* **A timer** checks every minute for missed deadlines, and retries HR if it was down.

In production this would run on AWS, using pay-per-use parts: API Gateway (receives messages), one Lambda function (runs the bot, one copy at a time), DynamoDB (the database), and an EventBridge timer every minute. That costs about \$0–1 a month, compared with about \$10 for one always-on container.

## Trade-offs

* **People report "empty" and "done"; there is no sensor.** Cheap, no hardware. A false "empty" is stopped when the person asked replies "full"; HR's receipt check stops fake claims. A weight sensor is a later upgrade.  
* **The amount the buyer types is trusted.** HR checks it against the receipt, and it is capped by the limit.  
* **Spend is counted at "done", not when HR approves.** Budget is right at once; a rejection adds it back.  
* **Snacks are free text.** Easy, but "crisps" and "chips" count as different snacks; a fixed list would be better.  
* **Data is kept in memory.** Simple and fast to test, but lost on restart. Production uses DynamoDB.  
* **Months are counted in UTC.** A purchase just after midnight on the 1st can count in the wrong month.

## What we won't build (now)

Real chat or HR connections, a database, login, reading receipts automatically (OCR), weight sensors, public holidays, more than one box per office, budget carry-over, converting claim currencies (HR does it), and automatic resubmission of rejected claims.

## Our questions and assumptions

| Assumption | Why |
| :---- | :---- |
| One snack box per office | The brief talks about one box. More boxes is a small change. |
| A monthly budget in the office's own currency, not carried over | The brief describes a monthly budget. Carry-over is a finance decision, not ours. |
| HR approves claims and checks receipts | HR already does this. We should not copy that work. |
| People set "away" or "visiting" in chat, with an end date | No leave calendar was named. A real one can replace it later. |
| 30 min to answer, 3 office hours to buy | Fair to the buyer, but the box isn't empty all day. |
| Office hours: 10–17 local, Mon–Fri | We should not message people at night or at the weekend. |
| The reporter can also be asked to buy | They are already at the box. The fair order still spreads the work. |
| Only admins change budgets and timers | Money settings need control. |
| Chat gives us the sender's id | So we trust the sender id and need no extra login. |

**Questions for you:** Should a rejected claim be resubmitted automatically? Are there public holidays per office? Should unspent budget carry over? Is there one box per office, or could there be more?

**Changed since my email**: the budget is a limit told at "yes", not a soft limit with approval (waiting for approval could leave the box empty for days), and the time to buy is 3 office hours, not the next working day.