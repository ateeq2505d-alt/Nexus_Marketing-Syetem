using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NexusApp.Data;
using NexusApp.Models;

namespace NexusApp.Services;

/// <summary>Password hashing helpers (PBKDF2 with a random salt, 50,000 iterations).</summary>
public static class Pwd
{
    /// <summary>Hashes a password. Result format: base64(salt) + "." + base64(hash).</summary>
    public static string Hash(string p)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var h = Rfc2898DeriveBytes.Pbkdf2(p, salt, 50000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(salt) + "." + Convert.ToBase64String(h);
    }

    /// <summary>Verifies a password against a stored hash in constant time.</summary>
    public static bool Verify(string p, string stored)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(p)) return false;
        var parts = stored.Split('.');
        var salt = Convert.FromBase64String(parts[0]);
        var h = Rfc2898DeriveBytes.Pbkdf2(p, salt, 50000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(h, Convert.FromBase64String(parts[1]));
    }
}

/// <summary>Display helpers for connection types.</summary>
public static class TypeInfo
{
    public static string Label(ConnType t) => t switch { ConnType.D => "Dial-Up", ConnType.B => "Broadband", _ => "Landline (telephone)" };
}

/// <summary>Generates the unique order id (11 chars) and account id (16 chars) per the specification.</summary>
public class IdService
{
    private readonly AppDb _db;
    public IdService(AppDb db) { _db = db; }

    /// <summary>Order id: 1 letter (D = dial-up, T = telephone only, B = broadband) + 10-digit serial, e.g. D0000000001.</summary>
    public string NextOrderNo(ConnType t)
    {
        var p = t.ToString();
        // find the highest existing order number of this type and add one (serial order)
        var last = _db.Orders.Where(o => o.OrderNo.StartsWith(p)).OrderByDescending(o => o.OrderNo).Select(o => o.OrderNo).FirstOrDefault();
        long n = last == null ? 0 : long.Parse(last.Substring(1));
        return p + (n + 1).ToString("D10");
    }

    /// <summary>Account id: 1 letter (type) + 3-digit city code + 12-digit serial = 16 chars.</summary>
    public string NextAccountId(ConnType t, string cityCode)
    {
        var p = t.ToString() + cityCode;
        // serial runs per connection type + city
        var last = _db.Connections.Where(c => c.AccountId.StartsWith(p)).OrderByDescending(c => c.AccountId).Select(c => c.AccountId).FirstOrDefault();
        long n = last == null ? 0 : long.Parse(last.Substring(4));
        return p + (n + 1).ToString("D12");
    }
}

/// <summary>Usage position of an hourly-pack connection.</summary>
public record UsageSummary(bool IsHourly, int PackHours, decimal Used, decimal Remaining, DateTime ValidUntil, bool Expired, bool Exhausted);

/// <summary>Business rules: feasibility, connection creation, bulk discount, billing, payments,
/// auto-suspension, plan changes, hourly-pack usage, deposit refunds and customer notifications.</summary>
public class BillingService
{
    private readonly AppDb _db;
    private readonly IdService _ids;
    public BillingService(AppDb db, IdService ids) { _db = db; _ids = ids; }

    /// <summary>The single row of admin-maintained charges (deposits, tax, distance limit...).</summary>
    public ChargeSettings Settings => _db.Settings.First();

    // ------------------------------------------------------------------ notifications
    /// <summary>Stores an in-app notification for a customer.</summary>
    public void Notify(int customerId, string message, string link = null)
    {
        _db.Notifications.Add(new Notification { CustomerId = customerId, Message = message, Link = link });
        _db.SaveChanges();
    }

    // ------------------------------------------------------------------ bulk discount
    /// <summary>Bulk / corporate discount band (applies to advance rental and security deposit).
    /// 10-15 connections = 25%, 16-25 = 50%, 26-50 = 75%, above 50 = 100%.</summary>
    public static decimal BulkDiscount(int connections) => connections switch { < 10 => 0, <= 15 => 25, <= 25 => 50, <= 50 => 75, _ => 100 };

    /// <summary>Discount for an order, counting the customer's existing live connections plus the connections in this order.</summary>
    public decimal EffectiveDiscount(Order o)
    {
        // linked landlines (created with a dial-up) are not counted as separate "connections taken"
        var existing = _db.Connections.Count(c => c.CustomerId == o.CustomerId && c.LinkedConnectionId == null && c.Status != ConnStatus.PermanentlyInactive && c.OrderId != o.Id);
        return BulkDiscount(existing + o.Quantity);
    }

    /// <summary>Refundable security deposit configured by the admin for a connection type.</summary>
    public decimal DepositFor(ConnType t)
    {
        var s = Settings;
        return t switch { ConnType.D => s.DepositDialUp, ConnType.B => s.DepositBroadband, _ => s.DepositLandline };
    }

    // ------------------------------------------------------------------ feasibility and connections
    /// <summary>Feasibility rule: landline feasible if the distance is within the limit; internet feasible if within the
    /// limit AND the server is available. Which checks apply depends on the order (see Order.NeedsLandlineCheck).</summary>
    public string RecordFeasibility(Order o, decimal distance, bool serverAvailable, string remarks, int empId)
    {
        var max = Settings.MaxDistanceKm;
        o.DistanceKm = distance;
        o.ServerAvailable = serverAvailable;
        var inRange = distance <= max;
        o.LandlineFeasible = o.NeedsLandlineCheck ? inRange : null;
        o.InternetFeasible = o.NeedsInternetCheck ? inRange && serverAvailable : null;
        bool ok = (o.LandlineFeasible ?? true) && (o.InternetFeasible ?? true);
        o.Status = ok ? OrderStatus.Feasible : OrderStatus.NotFeasible;
        o.Remarks = remarks;
        o.StatusDate = DateTime.Now;
        o.UpdatedByEmployeeId = empId;
        _db.SaveChanges();
        Notify(o.CustomerId, $"Order {o.OrderNo}: area is {(ok ? "feasible - your connection will be set up soon" : "NOT feasible - " + remarks)}.", "/Orders/Details/" + o.Id);
        return ok ? "Area is feasible." : "Area is NOT feasible.";
    }

    /// <summary>Creates the connection(s) for a feasible order and assigns account ids. Returns an error text or null.</summary>
    public string CreateConnections(Order o, int empId)
    {
        if (o.Status != OrderStatus.Feasible) return "Order must be marked feasible first.";
        var city = _db.Cities.Find(o.Customer.CityId);
        var disc = EffectiveDiscount(o);
        // issue the chosen equipment from stock
        if (o.EquipmentProductId != null)
        {
            var prod = _db.Products.Find(o.EquipmentProductId);
            if (prod.Stock < o.Quantity) return $"Insufficient stock of {prod.Name} (have {prod.Stock}, need {o.Quantity}). Ask admin to raise a purchase order.";
            prod.Stock -= o.Quantity;
        }
        for (int i = 0; i < o.Quantity; i++)
        {
            var main = new Connection
            {
                AccountId = _ids.NextAccountId(o.ConnType, city.Code), OrderId = o.Id, CustomerId = o.CustomerId, CityId = city.Id,
                ConnType = o.ConnType, PlanId = o.PlanId, EquipmentProductId = o.EquipmentProductId,
                DiscountPercent = disc, SecurityDeposit = DepositFor(o.ConnType), PlanStartDate = DateTime.Now
            };
            _db.Connections.Add(main);
            _db.SaveChanges();
            // record the material provided with the connection
            if (o.EquipmentProductId != null)
                _db.Materials.Add(new ConnectionMaterial { ConnectionId = main.Id, ProductId = o.EquipmentProductId.Value });
            // dial-up needs a Nexus landline: create the linked telephone connection when the customer has none
            if (o.ConnType == ConnType.D && !o.HasNexusLandline && o.LandlinePlanId != null)
            {
                _db.Connections.Add(new Connection
                {
                    AccountId = _ids.NextAccountId(ConnType.T, city.Code), OrderId = o.Id, CustomerId = o.CustomerId, CityId = city.Id,
                    ConnType = ConnType.T, PlanId = o.LandlinePlanId.Value, DiscountPercent = disc,
                    SecurityDeposit = DepositFor(ConnType.T), LinkedConnectionId = main.Id, PlanStartDate = DateTime.Now
                });
            }
            _db.SaveChanges();
        }
        o.Status = OrderStatus.Connected;
        o.StatusDate = DateTime.Now;
        o.UpdatedByEmployeeId = empId;
        _db.SaveChanges();
        Notify(o.CustomerId, $"Order {o.OrderNo}: your connection has been provided. Check My connections for your account ID.", "/Connections");
        return null;
    }

    /// <summary>Changes a connection status (technical staff) and tells the customer.</summary>
    public void SetConnectionStatus(Connection c, ConnStatus status, string reason)
    {
        c.Status = status; c.StatusReason = reason; c.SuspendedForNonPayment = false;
        _db.SaveChanges();
        Notify(c.CustomerId, $"Connection {c.AccountId} is now {status}. {reason}", "/Connections/Details/" + c.Id);
    }

    // ------------------------------------------------------------------ billing
    /// <summary>Generates a postpaid bill for a connection. Tax applies to the whole bill (excluding carried-forward dues).
    /// The first bill adds the security deposit and applies the bulk discount to advance + deposit.</summary>
    public Bill GenerateBill(Connection c, DateTime periodStart, int local, int std, int sms, decimal extraCharge, int? empId)
    {
        var s = Settings;
        var plan = _db.Plans.Find(c.PlanId);
        var bill = new Bill
        {
            ConnectionId = c.Id, PeriodStart = periodStart, PeriodEnd = periodStart.AddMonths(plan.CycleMonths).AddDays(-1),
            RentalCharge = plan.Rental, LocalMinutes = local, StdMinutes = std, SmsMinutes = sms, GeneratedById = empId
        };
        if (!c.InitialBilled) bill.SecurityDeposit = c.SecurityDeposit;
        // call charges only apply to landline plans
        bill.CallCharges = c.ConnType == ConnType.T ? local * plan.LocalRate + std * plan.StdRate + sms * plan.SmsRate : 0;
        // unbilled equipment replacements recorded by technical staff are added to this bill
        var repl = _db.Materials.Where(m => m.ConnectionId == c.Id && m.IsReplacement && !m.Billed).ToList();
        bill.ReplacementCharges = repl.Sum(m => m.Charge) + extraCharge;
        foreach (var m in repl) m.Billed = true;
        // discounts apply to the advance (first rental) and security deposit of the initial bill
        if (!c.InitialBilled) bill.Discount = Math.Round((bill.RentalCharge + bill.SecurityDeposit) * c.DiscountPercent / 100, 2);
        var taxable = bill.RentalCharge + bill.SecurityDeposit + bill.CallCharges + bill.ReplacementCharges - bill.Discount;
        bill.Tax = Math.Round(taxable * s.TaxPercent / 100, 2);
        // earlier unpaid bills are carried forward into this one
        var older = _db.Bills.Where(b => b.ConnectionId == c.Id && !b.CarriedForward).ToList();
        bill.PreviousDue = older.Sum(b => b.Total - b.Paid);
        foreach (var b in older) b.CarriedForward = true;
        bill.Total = taxable + bill.Tax + bill.PreviousDue;
        bill.DueDate = DateTime.Today.AddDays(s.PaymentDays);
        bill.BillNo = "BL" + DateTime.Now.ToString("yyyyMM") + (_db.Bills.Count() + 1).ToString("D5");
        c.InitialBilled = true;
        _db.Bills.Add(bill);
        _db.SaveChanges();
        Notify(c.CustomerId, $"New bill {bill.BillNo} of {bill.Total:C} for account {c.AccountId}, due {bill.DueDate:yyyy-MM-dd}.", "/Bills/Details/" + bill.Id);
        return bill;
    }

    /// <summary>Records a payment against a bill. Returns an error text or null.</summary>
    public string RecordPayment(Bill b, decimal amount, PayMode mode, int? empId, int? shopId)
    {
        if (amount <= 0) return "Amount must be positive.";
        if (amount > b.Due) return $"Amount exceeds the due amount ({b.Due:C}).";
        b.Paid += amount;
        var receipt = "RC" + DateTime.Now.ToString("yyyyMMddHHmmss") + b.Id;
        _db.Payments.Add(new Payment { BillId = b.Id, Amount = amount, Mode = mode, ReceivedById = empId, ShopId = shopId, ReceiptNo = receipt });
        _db.SaveChanges();
        var conn = _db.Connections.Find(b.ConnectionId);
        // file the receipt by year and area (city code), as in the manual process
        var pay = _db.Payments.First(p => p.ReceiptNo == receipt && p.BillId == b.Id);
        var code = _db.Cities.Find(conn.CityId).Code;
        _db.Documents.Add(new Document { CustomerId = conn.CustomerId, OrderNo = _db.Orders.Find(conn.OrderId).OrderNo, Type = DocType.Receipt, FileName = receipt, StoredPath = "/Bills/Receipt/" + pay.Id, Year = DateTime.Now.Year, CityCode = code });
        _db.SaveChanges();
        ReactivateIfCleared(conn);
        Notify(conn.CustomerId, $"Payment of {amount:C} received for bill {b.BillNo} (receipt {receipt}).", "/Bills/Details/" + b.Id);
        return null;
    }

    /// <summary>Re-activates a connection that was suspended for non-payment once nothing is overdue.</summary>
    private void ReactivateIfCleared(Connection c)
    {
        if (!c.SuspendedForNonPayment) return;
        var today = DateTime.Today;
        var g = Settings.GraceDays;
        bool overdue = _db.Bills.Where(b => b.ConnectionId == c.Id && !b.CarriedForward).ToList().Any(b => b.Due > 0 && b.DueDate.AddDays(g) < today);
        if (!overdue)
        {
            c.Status = ConnStatus.Active; c.SuspendedForNonPayment = false; c.StatusReason = "Reactivated after payment";
            _db.SaveChanges();
        }
    }

    /// <summary>Connection status depends on billing: suspend active connections whose bill is overdue beyond the grace period.</summary>
    public int SuspendOverdue()
    {
        var today = DateTime.Today; var g = Settings.GraceDays; int n = 0;
        var bills = _db.Bills.Include(b => b.Connection).Where(b => !b.CarriedForward).ToList();
        foreach (var b in bills.Where(b => b.Due > 0 && b.DueDate.AddDays(g) < today && b.Connection.Status == ConnStatus.Active))
        {
            b.Connection.Status = ConnStatus.TemporarilyInactive; b.Connection.SuspendedForNonPayment = true;
            b.Connection.StatusReason = "Suspended: unpaid bill " + b.BillNo; n++;
            _db.SaveChanges();
            Notify(b.Connection.CustomerId, $"Connection {b.Connection.AccountId} suspended: bill {b.BillNo} is overdue.", "/Bills/Details/" + b.Id);
        }
        return n;
    }

    /// <summary>Total amount still owed on a connection (latest, non-carried-forward bills).</summary>
    public decimal OutstandingFor(int connectionId) => _db.Bills.Where(b => b.ConnectionId == connectionId && !b.CarriedForward).ToList().Sum(b => b.Total - b.Paid);

    // ------------------------------------------------------------------ security deposit refund
    /// <summary>Deposit actually charged to the customer after the bulk discount.</summary>
    public static decimal NetDeposit(Connection c) => Math.Round(c.SecurityDeposit * (100 - c.DiscountPercent) / 100, 2);

    /// <summary>Refunds the security deposit when a connection is withdrawn (permanently inactive).
    /// Refund = deposit charged minus any amount still owed. Returns an error text or null.</summary>
    public string RefundDeposit(Connection c)
    {
        if (c.Status != ConnStatus.PermanentlyInactive) return "Deposit can be refunded only after the connection is made permanently inactive (withdrawn / cancelled).";
        if (c.RefundedOn != null) return "Deposit already refunded.";
        if (!c.InitialBilled) return "No security deposit was ever billed for this connection.";
        var refund = Math.Max(0, NetDeposit(c) - OutstandingFor(c.Id));
        c.DepositRefunded = refund; c.RefundedOn = DateTime.Now;
        _db.SaveChanges();
        Notify(c.CustomerId, $"Security deposit refund of {refund:C} processed for account {c.AccountId}.", "/Connections/Details/" + c.Id);
        return null;
    }

    // ------------------------------------------------------------------ plan change
    /// <summary>Creates a plan-change request. Returns an error text or null.</summary>
    public string RequestPlanChange(Connection c, int newPlanId, string reason, int? empId)
    {
        var np = _db.Plans.Find(newPlanId);
        if (np == null || !np.Active || np.ConnType != c.ConnType) return "Choose an active plan of the same connection type.";
        if (np.Id == c.PlanId) return "The connection is already on that plan.";
        if (c.Status == ConnStatus.PermanentlyInactive) return "A permanently inactive connection cannot change plan.";
        if (_db.PlanChanges.Any(p => p.ConnectionId == c.Id && p.Status == RequestStatus.Pending)) return "A plan change request is already pending for this connection.";
        _db.PlanChanges.Add(new PlanChange { ConnectionId = c.Id, NewPlanId = newPlanId, Reason = reason, RequestedByEmployeeId = empId });
        _db.SaveChanges();
        return null;
    }

    /// <summary>Approves or rejects a plan-change request. Approval switches the connection to the new plan from now on.</summary>
    public void DecidePlanChange(PlanChange p, bool approve, string note, int empId)
    {
        p.Status = approve ? RequestStatus.Approved : RequestStatus.Rejected;
        p.DecidedOn = DateTime.Now; p.DecidedById = empId; p.DecisionNote = note;
        var c = _db.Connections.Find(p.ConnectionId);
        if (approve) { c.PlanId = p.NewPlanId; c.PlanStartDate = DateTime.Now; }
        _db.SaveChanges();
        Notify(c.CustomerId, $"Plan change for {c.AccountId} was {(approve ? "approved" : "rejected")}. {note}", "/PlanChanges");
    }

    // ------------------------------------------------------------------ hourly-pack usage
    /// <summary>Records hours used on an hourly-pack connection.</summary>
    public void RecordUsage(int connectionId, decimal hours, int? empId)
    {
        _db.Usage.Add(new UsageRecord { ConnectionId = connectionId, Hours = hours, RecordedById = empId });
        _db.SaveChanges();
        var c = _db.Connections.Find(connectionId);
        var u = UsageFor(c);
        if (u.IsHourly && u.Exhausted) Notify(c.CustomerId, $"All {u.PackHours} hours of your pack on {c.AccountId} have been used.", "/Connections/Details/" + c.Id);
    }

    /// <summary>Hours used / remaining and validity of the current hourly pack (for plans with Hours &gt; 0).</summary>
    public UsageSummary UsageFor(Connection c)
    {
        var plan = _db.Plans.Find(c.PlanId);
        var until = c.PlanStartDate.AddMonths(plan.CycleMonths);
        if (plan.Hours <= 0) return new UsageSummary(false, 0, 0, 0, until, DateTime.Now > until, false);
        var used = _db.Usage.Where(u => u.ConnectionId == c.Id && u.Date >= c.PlanStartDate).ToList().Sum(u => u.Hours);
        var rem = Math.Max(0, plan.Hours - used);
        return new UsageSummary(true, plan.Hours, used, rem, until, DateTime.Now > until, used >= plan.Hours);
    }
}
