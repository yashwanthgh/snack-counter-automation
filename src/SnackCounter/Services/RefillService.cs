namespace SnackCounter;

// The rules of a refill, from "box is empty" to "reimbursed".
//
//   Offered -> Committed -> Filled -> Claimed -> Reimbursed (or ClaimRejected)
//   no answer / no / not finished in time -> offered to the next person, or Escalated if nobody is left
//
// Every public method: check the state, change the state, send a chat message.
// Every public method runs inside lock(_lock), so only one runs at a time:
// if two "yes" replies arrive together, the first wins and the second sees "already taken".
// (With a database this is: UPDATE refills SET state='Committed' WHERE id=@id AND state='Offered')
//
// Opening hours (config): outside them nobody is asked (the refill waits), and timers only count open time.
public class RefillService
{
    private readonly object _lock = new object();

    private List<Employee> _employees;
    private IChat _chat;
    private IClock _clock;
    private IHr _hr;
    private BudgetService _budget;
    private SnackConfig _config;

    private List<Refill> _refills = new List<Refill>();
    private List<CompletedRefill> _completed = new List<CompletedRefill>();

    public RefillService(List<Employee> employees, IChat chat, IClock clock, IHr hr, BudgetService budget, SnackConfig config)
    {
        _employees = employees;
        _chat = chat;
        _clock = clock;
        _hr = hr;
        _budget = budget;
        _config = config;
    }

    public List<Refill> GetRefills()
    {
        lock (_lock)
        {
            return new List<Refill>(_refills);
        }
    }

    // 1. Anyone reports a box empty, or running low. Both start a refill the same way.
    //    At most ONE open refill per office.
    public Refill ReportEmpty(Office office, string reportedBy, bool runningLow = false)
    {
        string how = runningLow ? "running low" : "empty";
        lock (_lock)
        {
            DateTimeOffset now = _clock.GetNow();
            Refill? open = FindOpenRefill(office);

            if (open == null)
            {
                Refill refill = new Refill(office, now);
                refill.History.Add(Time(now) + ": " + reportedBy + " reported the box " + how);
                _refills.Add(refill);
                OfferToNextPerson(refill);
                if (refill.State == RefillState.Waiting)
                {
                    _chat.SendTo(reportedBy, "Thanks for reporting! The " + office + " office is closed now, so someone will be asked at " + Time(refill.Deadline!.Value) + ".");
                }
                else if (refill.AssignedTo != null && refill.AssignedTo != reportedBy)
                {
                    _chat.SendTo(reportedBy, "Thanks for reporting! " + refill.AssignedTo + " has been asked to refill the " + office + " box.");
                }
                return refill;
            }

            open.History.Add(Time(now) + ": " + reportedBy + " reported it " + how + " again");
            if (open.State == RefillState.Escalated)
            {
                // Nobody was available last time. People may be back now, so start the list again.
                open.AlreadyAsked.Clear();
                OfferToNextPerson(open);
            }
            else if (open.State == RefillState.Committed && open.AssignedTo != null)
            {
                _chat.SendTo(open.AssignedTo, "The " + office + " box was reported " + how + " again. Still on it? If you can't do it, reply no and someone else will be asked.");
            }
            return open;
        }
    }

    // 2a. A volunteer says "yes".
    public AcceptResult Accept(Guid refillId, string employeeId)
    {
        lock (_lock)
        {
            DateTimeOffset now = _clock.GetNow();
            Refill refill = GetRefill(refillId);

            if (refill.State != RefillState.Offered) return AcceptResult.AlreadyTaken;
            if (refill.AssignedTo != employeeId) return AcceptResult.NotOfferedToYou;
            if (now >= refill.Deadline) return AcceptResult.OfferExpired;

            // The limit is the budget left right now. No budget left: nobody buys, the contact decides.
            long remaining = _budget.RemainingMinor(refill.Office, now);
            if (remaining <= 0)
            {
                refill.State = RefillState.Escalated;
                refill.AssignedTo = null;
                refill.Deadline = null;
                refill.History.Add(Time(now) + ": " + employeeId + " said YES, but no budget is left, so nobody buys");
                _chat.SendTo(employeeId, "Thanks, but the " + refill.Office + " budget is used up this month. Please don't buy anything.");
                _chat.SendToOfficeContact(refill.Office, "No budget left: the " + refill.Office + " box is empty and the monthly budget is used up ("
                    + _budget.RemainingText(refill.Office, now) + "). Please decide what to do.");
                return AcceptResult.NoBudgetLeft;
            }

            refill.State = RefillState.Committed;
            refill.Deadline = WorkingHours.AddOpenTime(HoursFor(refill.Office), now, _config.CompleteWindow);
            refill.SpendLimitMinor = remaining;
            refill.History.Add(Time(now) + ": " + employeeId + " said YES (may spend up to " + _budget.RemainingText(refill.Office, now) + ")");
            _chat.SendTo(employeeId, "Thanks! Please refill the " + refill.Office + " box by " + Time(refill.Deadline.Value)
                + ", then send: done <amount> <items> and attach a photo or PDF of the receipt (HR needs it). Typical spend: " + _budget.TypicalRange(refill.Office)
                + ". You can spend up to " + _budget.RemainingText(refill.Office, now) + "; anything above that is not paid back."
                + " Can't do it after all? Reply no.");

            // Warn the contact early when the budget is nearly gone.
            if (!_budget.HasRoomForTypicalPurchase(refill.Office, now))
            {
                _chat.SendToOfficeContact(refill.Office, "Budget low: " + employeeId + " is refilling the " + refill.Office
                    + " box with only " + _budget.RemainingText(refill.Office, now) + " left this month.");
            }
            return AcceptResult.Accepted;
        }
    }

    // 2b. A volunteer says "no". Ask the next person straight away.
    // Also works after "yes" (Committed): they can't go after all, so nobody waits for the timer.
    //     boxIsFull = they checked and the box is NOT empty (a false report): the refill is closed, nobody buys.
    public void Decline(Guid refillId, string employeeId, bool boxIsFull = false)
    {
        lock (_lock)
        {
            Refill refill = GetRefill(refillId);
            bool offered = refill.State == RefillState.Offered;
            bool committed = refill.State == RefillState.Committed;
            if ((!offered && !committed) || refill.AssignedTo != employeeId) return;

            if (boxIsFull)
            {
                refill.History.Add(Time(_clock.GetNow()) + ": " + employeeId + " checked the box: it is full, nothing to buy");
                refill.State = RefillState.NotNeeded;
                refill.AssignedTo = null;
                refill.Deadline = null;
                _chat.SendTo(employeeId, "Thanks for checking! The " + refill.Office + " box is full, so nothing needs to be bought.");
                return;
            }

            if (committed)
            {
                refill.History.Add(Time(_clock.GetNow()) + ": " + employeeId + " said YES before, but now can't do it");
                _chat.SendTo(employeeId, "No problem, thanks for telling us. Please don't buy for it; someone else will be asked.");
            }
            else
            {
                refill.History.Add(Time(_clock.GetNow()) + ": " + employeeId + " said NO");
            }
            OfferToNextPerson(refill);
        }
    }

    // 3. The volunteer has refilled the box and sends the receipt.
    public FillResult MarkFilled(Guid refillId, string employeeId, Money amount, List<string> items, string receiptImage)
    {
        lock (_lock)
        {
            DateTimeOffset now = _clock.GetNow();
            Refill refill = GetRefill(refillId);

            if (refill.State != RefillState.Committed || refill.AssignedTo != employeeId)
            {
                // Someone else has it now but they paid with their own money: a person must decide, not the code.
                // (The buyer sending "done" twice is just ignored, no alarm.)
                if (refill.AssignedTo != employeeId && refill.AlreadyAsked.Contains(employeeId) && amount.Minor > 0)
                {
                    _chat.SendToOfficeContact(refill.Office, "Late receipt: " + employeeId + " paid " + amount + " for "
                        + refill.ShortId() + " after it was handed on. Please check if they should be reimbursed.");
                }
                return FillResult.NotYours;
            }
            if (amount.Minor <= 0) return FillResult.InvalidAmount;
            if (amount.Currency != _budget.GetBudget(refill.Office).Currency) return FillResult.WrongCurrency;

            // Pay back at most the limit given at "yes". The rest is the buyer's own cost.
            Money claimed = amount;
            if (amount.Minor > refill.SpendLimitMinor)
            {
                claimed = new Money(refill.SpendLimitMinor, amount.Currency);
                _chat.SendTo(employeeId, "Your receipt is " + amount + " but the limit was " + claimed
                    + ". Only " + claimed + " will be claimed; the rest is not paid back.");
            }

            refill.State = RefillState.Filled;
            refill.Deadline = null;
            refill.Amount = claimed;
            refill.Items = items;
            refill.ReceiptImage = receiptImage;
            refill.FilledAt = now;
            refill.History.Add(Time(now) + ": " + employeeId + " bought snacks: receipt " + amount + ", claiming " + claimed);

            // Counted against the budget now (the money is spent), not at HR approval days later,
            // so the next buyer's limit is right. Given back if HR rejects the claim.
            refill.Spend = new SpendRecord(refill.Office, employeeId, claimed, items, now);
            _budget.Record(refill.Spend);
            _completed.Add(new CompletedRefill(employeeId, now)); // they will be asked last next time

            // Only possible if an admin lowered the budget after the "yes".
            if (_budget.RemainingMinor(refill.Office, now) < 0)
            {
                _chat.SendToOfficeContact(refill.Office, "Over budget: the " + refill.Office + " office is now at "
                    + _budget.RemainingText(refill.Office, now) + " for this month.");
            }

            SendClaimToHr(refill);
            return FillResult.Filled;
        }
    }

    // 4. HR tells us it approved or rejected a claim (in reality a webhook or a daily poll).
    // shortcut: a rejected claim is handed to people (volunteer + office contact); no resubmit flow.
    public void RecordHrDecision(string claimId, bool approved, string reason)
    {
        lock (_lock)
        {
            Refill? refill = FindByClaimId(claimId);
            if (refill == null || refill.State != RefillState.Claimed || refill.AssignedTo == null) return; // unknown or duplicate

            refill.State = approved ? RefillState.Reimbursed : RefillState.ClaimRejected;
            refill.History.Add(Time(_clock.GetNow()) + ": HR " + (approved ? "APPROVED claim " + claimId + ", money paid back" : "REJECTED claim " + claimId + ": " + reason));

            if (approved)
            {
                _chat.SendTo(refill.AssignedTo, "Your claim " + claimId + " (" + refill.Amount + ") was approved. Thanks for refilling!");
            }
            else
            {
                if (refill.Spend != null) _budget.Remove(refill.Spend);
                _chat.SendTo(refill.AssignedTo, "Your claim " + claimId + " was rejected: " + reason + ". The office contact will help you.");
                _chat.SendToOfficeContact(refill.Office, "HR rejected claim " + claimId + " from " + refill.AssignedTo + ": " + reason);
            }
        }
    }

    // 5. Called regularly (e.g. every minute): hands on expired offers and commitments,
    // and retries claims HR did not take.
    public void Tick()
    {
        lock (_lock)
        {
            DateTimeOffset now = _clock.GetNow();
            foreach (Refill refill in _refills)
            {
                if (refill.State == RefillState.Filled)
                {
                    SendClaimToHr(refill); // HR was down last time; same key, so never a second claim
                }
                else if (refill.State == RefillState.Waiting && refill.Deadline != null && now >= refill.Deadline.Value)
                {
                    refill.History.Add(Time(now) + ": office open, asking someone now");
                    OfferToNextPerson(refill);
                }
                else if (refill.Deadline != null && now >= refill.Deadline.Value)
                {
                    if (refill.State == RefillState.Committed && refill.AssignedTo != null)
                    {
                        _chat.SendTo(refill.AssignedTo, "The " + refill.Office + " refill has been handed to someone else. Please don't buy for it now.");
                    }
                    refill.History.Add(Time(now) + ": " + refill.AssignedTo + (refill.State == RefillState.Offered ? " did not answer in time" : " did not finish in time"));
                    OfferToNextPerson(refill);
                }
            }
        }
    }

    // The refill this person is being asked about or working on (for "yes", "no", "done").
    public Refill? FindActiveRefillFor(string employeeId)
    {
        lock (_lock)
        {
            foreach (Refill r in _refills)
            {
                bool active = r.State == RefillState.Offered || r.State == RefillState.Committed;
                if (active && r.AssignedTo == employeeId) return r;
            }
            // Not theirs any more, but they were asked: lets a late "done" reach MarkFilled and the contact.
            foreach (Refill r in _refills)
            {
                if (r.AlreadyAsked.Contains(employeeId) && r.State != RefillState.Reimbursed) return r;
            }
            return null;
        }
    }

    // Ask the next fair person. If nobody is left, tell the office contact.
    private void OfferToNextPerson(Refill refill)
    {
        DateTimeOffset now = _clock.GetNow();
        OfficeHours? hours = HoursFor(refill.Office);

        // Office closed: don't message anyone now. Tick() asks someone when it opens.
        if (!WorkingHours.IsOpen(hours, now))
        {
            refill.State = RefillState.Waiting;
            refill.AssignedTo = null;
            refill.Deadline = WorkingHours.NextOpen(hours, now);
            refill.History.Add(Time(now) + ": office closed, will ask someone at " + Time(refill.Deadline.Value));
            return;
        }

        Employee? next = VolunteerSelector.PickNext(_employees, refill.Office, refill.AlreadyAsked, _completed, now, _config.FairnessWindow);
        if (next == null)
        {
            refill.State = RefillState.Escalated;
            refill.AssignedTo = null;
            refill.Deadline = null;
            refill.History.Add(Time(now) + ": nobody free to ask, office contact told");
            _chat.SendToOfficeContact(refill.Office, "Nobody available to refill the " + refill.Office + " box (reported " + Time(refill.ReportedAt) + ").");
            return;
        }

        refill.State = RefillState.Offered;
        refill.AssignedTo = next.EmployeeId;
        refill.AlreadyAsked.Add(next.EmployeeId);
        refill.Deadline = WorkingHours.AddOpenTime(hours, now, _config.AcceptWindow);
        refill.History.Add(Time(now) + ": asked " + next.EmployeeId + " to refill (answer by " + Time(refill.Deadline.Value) + ")");
        _chat.SendTo(next.EmployeeId, "The " + refill.Office + " snack box is empty. Can you refill it? Reply yes/no by " + Time(refill.Deadline.Value) + ".");
    }

    // Create the HR claim. If HR is down, stay Filled; Tick() retries with the SAME key,
    // so even if HR saved the first try, no second claim is created.
    private void SendClaimToHr(Refill refill)
    {
        if (refill.Amount == null || refill.AssignedTo == null) return;

        string description = "Snack refill, " + refill.Office + " office, " + refill.FilledAt?.ToString("yyyy-MM-dd");
        if (refill.Items.Count > 0) description += ": " + string.Join(", ", refill.Items);
        ClaimRequest request = new ClaimRequest(refill.AssignedTo, refill.Amount, description, refill.ReceiptImage ?? "", refill.Id.ToString());

        try
        {
            refill.ClaimId = _hr.SubmitClaim(request);
            refill.State = RefillState.Claimed;
            refill.History.Add(Time(_clock.GetNow()) + ": claim " + refill.ClaimId + " sent to HR, waiting for approval");
            _chat.SendTo(refill.AssignedTo, "Claim " + refill.ClaimId + " for " + refill.Amount + " sent to HR. Approval usually takes 1-10 days.");
        }
        catch (HrUnavailableException e)
        {
            refill.History.Add(Time(_clock.GetNow()) + ": HR system did not answer (" + e.Message + "), will try again");
        }
    }

    private static string Time(DateTimeOffset t)
    {
        return t.ToUniversalTime().ToString("ddd dd MMM HH:mm") + " UTC";
    }

    private Refill? FindOpenRefill(Office office)
    {
        foreach (Refill r in _refills)
        {
            if (r.Office == office && r.IsOpen()) return r;
        }
        return null;
    }

    // This office's opening hours, or null if it has none in config (always open).
    private OfficeHours? HoursFor(Office office)
    {
        foreach (OfficeHours h in _config.Hours)
        {
            if (h.Office == office) return h;
        }
        return null;
    }

    private Refill? FindByClaimId(string claimId)
    {
        foreach (Refill r in _refills)
        {
            if (r.ClaimId == claimId) return r;
        }
        return null;
    }

    private Refill GetRefill(Guid id)
    {
        foreach (Refill r in _refills)
        {
            if (r.Id == id) return r;
        }
        throw new KeyNotFoundException("No refill with id " + id);
    }
}
