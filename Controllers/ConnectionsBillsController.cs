using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusApp.Data;
using NexusApp.Models;
using NexusApp.Services;

namespace NexusApp.Controllers;

[Authorize]
public class ConnectionsController : AppController
{
    private readonly BillingService _bs;
    public ConnectionsController(AppDb db, BillingService bs) : base(db) { _bs = bs; }

    private IQueryable<Connection> Visible()
    {
        var q = Db.Connections.Include(c => c.Customer).Include(c => c.Plan).Include(c => c.City).Include(c => c.Equipment).AsQueryable();
        if (IsCustomer) q = q.Where(c => c.CustomerId == Uid);
        else if (User.IsInRole("Retail") && ShopId != null) q = q.Where(c => c.Order.ShopId == ShopId);
        return q;
    }

    /// <summary>Lists records (with optional search / status filter).</summary>
    public IActionResult Index(string status)
    {
        var q = Visible();
        if (Enum.TryParse<ConnStatus>(status, out var s)) q = q.Where(c => c.Status == s);
        var list = q.ToList().OrderByDescending(c => c.StartDate).ToList();
        ViewBag.Dues = list.ToDictionary(c => c.Id, c => _bs.OutstandingFor(c.Id));
        ViewBag.Status = status;
        return View(list);
    }

    /// <summary>Shows the full details of one record.</summary>
    public IActionResult Details(int id)
    {
        var c = Visible().Include(x => x.Order).FirstOrDefault(x => x.Id == id); if (c == null) return NotFound();
        ViewBag.Bills = Db.Bills.Where(b => b.ConnectionId == id).ToList().OrderByDescending(b => b.GeneratedDate).ToList();
        ViewBag.Materials = Db.Materials.Include(m => m.Product).Where(m => m.ConnectionId == id).ToList();
        ViewBag.Due = _bs.OutstandingFor(id);
        ViewBag.Products = Db.Products.ToList();
        ViewBag.Usage = _bs.UsageFor(c);
        ViewBag.UsageRows = Db.Usage.Where(u => u.ConnectionId == id).ToList().OrderByDescending(u => u.Date).ToList();
        ViewBag.NetDeposit = BillingService.NetDeposit(c);
        ViewBag.Changes = Db.PlanChanges.Include(p => p.NewPlan).Where(p => p.ConnectionId == id).ToList().OrderByDescending(p => p.RequestedOn).ToList();
        return View(c);
    }

    /// <summary>Technical staff or accounts record hours used on an hourly-pack connection.</summary>
    [Authorize(Roles = "Technical,Accounts")]
    [HttpPost]
    public IActionResult RecordUsage(int id, decimal hours)
    {
        if (hours <= 0) { Err("Hours must be positive."); return RedirectToAction("Details", new { id }); }
        _bs.RecordUsage(id, hours, Uid); Ok("Usage recorded.");
        return RedirectToAction("Details", new { id });
    }

    /// <summary>Accounts refund the security deposit of a withdrawn (permanently inactive) connection.</summary>
    [Authorize(Roles = "Accounts")]
    [HttpPost]
    public IActionResult RefundDeposit(int id)
    {
        var c = Db.Connections.Find(id); if (c == null) return NotFound();
        var err = _bs.RefundDeposit(c);
        if (err != null) Err(err); else Ok("Security deposit refunded: " + c.DepositRefunded.ToString("C"));
        return RedirectToAction("Details", new { id });
    }

    /// <summary>Technical staff make a connection inactive (temporarily / permanently) or active again.</summary>
    [Authorize(Roles = "Technical")]
    [HttpPost]
    public IActionResult SetStatus(int id, ConnStatus status, string reason)
    {
        var c = Db.Connections.Find(id); if (c == null) return NotFound();
        if (c.Status == ConnStatus.PermanentlyInactive) { Err("A permanently inactive connection cannot be changed."); return RedirectToAction("Details", new { id }); }
        _bs.SetConnectionStatus(c, status, reason);
        Ok("Connection status updated to " + status + ".");
        return RedirectToAction("Details", new { id });
    }

    /// <summary>Technical staff record an equipment replacement; the charge is added to the next bill.</summary>
    [Authorize(Roles = "Technical")]
    [HttpPost]
    public IActionResult Replace(int id, int productId, int quantity, bool chargeCustomer)
    {
        var p = Db.Products.Find(productId); if (p == null || quantity < 1) return BadRequest();
        if (p.Stock < quantity) { Err("Insufficient stock."); return RedirectToAction("Details", new { id }); }
        p.Stock -= quantity;
        Db.Materials.Add(new ConnectionMaterial { ConnectionId = id, ProductId = productId, Quantity = quantity, IsReplacement = true, Charge = chargeCustomer ? p.ReplacementCharge * quantity : 0 });
        Db.SaveChanges(); Ok("Equipment replacement recorded" + (chargeCustomer ? " and will be charged on the next bill." : "."));
        return RedirectToAction("Details", new { id });
    }
}

[Authorize]
public class BillsController : AppController
{
    private readonly BillingService _bs;
    public BillsController(AppDb db, BillingService bs) : base(db) { _bs = bs; }

    private IQueryable<Bill> Visible()
    {
        var q = Db.Bills.Include(b => b.Connection).ThenInclude(c => c.Customer).Include(b => b.Connection).ThenInclude(c => c.Plan).Include(b => b.Payments).AsQueryable();
        if (IsCustomer) q = q.Where(b => b.Connection.CustomerId == Uid);
        else if (User.IsInRole("Retail") && ShopId != null) q = q.Where(b => b.Connection.Order.ShopId == ShopId);
        return q;
    }

    /// <summary>Lists records (with optional search / status filter).</summary>
    [Authorize(Roles = "Admin,Retail,Accounts,Customer")]
    public IActionResult Index(bool dueOnly)
    {
        var list = Visible().ToList().OrderByDescending(b => b.GeneratedDate).ToList();
        if (dueOnly) list = list.Where(b => b.Due > 0).ToList();
        ViewBag.DueOnly = dueOnly;
        return View(list);
    }

    /// <summary>Shows the full details of one record.</summary>
    [Authorize(Roles = "Admin,Retail,Accounts,Customer")]
    public IActionResult Details(int id)
    {
        var b = Visible().FirstOrDefault(x => x.Id == id); if (b == null) return NotFound();
        return View(b);
    }

    /// <summary>Printable payment receipt (also filed under Documents by year and area).</summary>
    [Authorize]
    public IActionResult Receipt(int id)
    {
        var p = Db.Payments.Include(x => x.Bill).ThenInclude(b => b.Connection).ThenInclude(c => c.Customer).Include(x => x.Bill).ThenInclude(b => b.Connection).ThenInclude(c => c.Plan).FirstOrDefault(x => x.Id == id);
        if (p == null) return NotFound();
        if (IsCustomer && p.Bill.Connection.CustomerId != Uid) return Forbid();
        return View(p);
    }

    /// <summary>Lists payments received.</summary>
    [Authorize(Roles = "Accounts,Retail,Admin")]
    public IActionResult Payments()
    {
        var q = Db.Payments.Include(p => p.Bill).ThenInclude(b => b.Connection).ThenInclude(c => c.Customer).AsQueryable();
        if (User.IsInRole("Retail") && ShopId != null) q = q.Where(p => p.ShopId == ShopId);
        return View(q.ToList().OrderByDescending(p => p.PaidOn).ToList());
    }

    // ---------- only the accounts department generates / updates bills ----------
    [Authorize(Roles = "Accounts")]
    public IActionResult Generate(int? connectionId)
    {
        ViewBag.Connections = Db.Connections.Include(c => c.Customer).Include(c => c.Plan).Where(c => c.Status != ConnStatus.PermanentlyInactive).ToList();
        ViewBag.Selected = connectionId;
        return View();
    }

    /// <summary>Accounts generate a postpaid bill for a connection.</summary>
    [Authorize(Roles = "Accounts")]
    [HttpPost]
    public IActionResult Generate(int connectionId, DateTime periodStart, int local, int std, int sms, decimal extra)
    {
        var c = Db.Connections.Find(connectionId); if (c == null) return NotFound();
        var b = _bs.GenerateBill(c, periodStart, local, std, sms, extra, Uid);
        Ok("Bill " + b.BillNo + " generated.");
        return RedirectToAction("Details", new { id = b.Id });
    }

    /// <summary>Payments are recorded by retail outlet employees and by accounts (office payments).</summary>
    [Authorize(Roles = "Retail,Accounts")]
    [HttpPost]
    public IActionResult Pay(int id, decimal amount, PayMode mode)
    {
        var b = Visible().FirstOrDefault(x => x.Id == id); if (b == null) return NotFound();
        var err = _bs.RecordPayment(b, amount, mode, Uid, ShopId);
        if (err != null) Err(err); else Ok("Payment recorded.");
        return RedirectToAction("Details", new { id });
    }

    /// <summary>Suspends connections whose bills are overdue beyond the grace period.</summary>
    [Authorize(Roles = "Accounts")]
    [HttpPost]
    public IActionResult SuspendOverdue()
    {
        var n = _bs.SuspendOverdue();
        Ok(n + " connection(s) suspended for non-payment.");
        return RedirectToAction("Index", new { dueOnly = true });
    }
}

[Authorize]
public class CustomersController : AppController
{
    private readonly BillingService _bs;
    public CustomersController(AppDb db, BillingService bs) : base(db) { _bs = bs; }

    /// <summary>Lists records (with optional search / status filter).</summary>
    [Authorize(Roles = "Admin,Retail,Accounts,Technical")]
    public IActionResult Index(string q)
    {
        var list = Db.Customers.Include(c => c.City).ToList();
        if (!string.IsNullOrWhiteSpace(q)) list = list.Where(c => (c.Name + c.Phone + c.Email + c.CompanyName).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        ViewBag.Q = q;
        return View(list);
    }

    /// <summary>Shows the full details of one record.</summary>
    [Authorize(Roles = "Admin,Retail,Accounts,Technical")]
    public IActionResult Details(int id)
    {
        var c = Db.Customers.Include(x => x.City).FirstOrDefault(x => x.Id == id); if (c == null) return NotFound();
        ViewBag.Orders = Db.Orders.Include(o => o.Plan).Where(o => o.CustomerId == id).ToList();
        ViewBag.Connections = Db.Connections.Include(o => o.Plan).Where(o => o.CustomerId == id).ToList();
        return View(c);
    }

    /// <summary>Retail outlet staff and admin maintain customer contact details.</summary>
    [Authorize(Roles = "Admin,Retail")]
    public IActionResult Edit(int id)
    {
        var c = Db.Customers.Find(id); if (c == null) return NotFound();
        ViewBag.Cities = Db.Cities.ToList(); return View(c);
    }

    /// <summary>Shows the form / saves changes to an existing record.</summary>
    [Authorize(Roles = "Admin,Retail")]
    [HttpPost]
    public IActionResult Edit(int id, string name, string phone, string email, string address, int cityId, bool isCorporate, string companyName)
    {
        var c = Db.Customers.Find(id); if (c == null) return NotFound();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(phone)) { Err("Name and phone are required."); return RedirectToAction("Edit", new { id }); }
        c.Name = name; c.Phone = phone; c.Email = email; c.Address = address; c.CityId = cityId; c.IsCorporate = isCorporate; c.CompanyName = companyName;
        Db.SaveChanges(); Ok("Customer details updated."); return RedirectToAction("Details", new { id });
    }

    /// <summary>Customer maintains own contact details.</summary>
    [Authorize(Roles = "Customer")]
    public IActionResult Profile() { ViewBag.Cities = Db.Cities.ToList(); return View(Db.Customers.Find(Uid)); }

    /// <summary>Customer views / updates own contact details.</summary>
    [Authorize(Roles = "Customer")]
    [HttpPost]
    public IActionResult Profile(string phone, string email, string address, int cityId)
    {
        var c = Db.Customers.Find(Uid);
        if (string.IsNullOrWhiteSpace(phone)) { Err("Phone is required."); return RedirectToAction("Profile"); }
        c.Phone = phone; c.Email = email; c.Address = address; c.CityId = cityId;
        Db.SaveChanges(); Ok("Contact details updated."); return RedirectToAction("Profile");
    }
}

[Authorize]
public class FeedbackController : AppController
{
    public FeedbackController(AppDb db) : base(db) { }

    /// <summary>Lists records (with optional search / status filter).</summary>
    public IActionResult Index()
    {
        var q = Db.Feedbacks.Include(f => f.Customer).AsQueryable();
        if (IsCustomer) q = q.Where(f => f.CustomerId == Uid);
        return View(q.ToList().OrderByDescending(f => f.Date).ToList());
    }

    /// <summary>Shows the form / saves a new record.</summary>
    [Authorize(Roles = "Customer")]
    public IActionResult Create() { ViewBag.Orders = Db.Orders.Where(o => o.CustomerId == Uid).ToList(); return View(new Feedback()); }

    /// <summary>Shows the form / saves a new record.</summary>
    [Authorize(Roles = "Customer")]
    [HttpPost]
    public IActionResult Create(Feedback f)
    {
        ViewBag.Orders = Db.Orders.Where(o => o.CustomerId == Uid).ToList();
        if (string.IsNullOrWhiteSpace(f.Comments)) ModelState.AddModelError("Comments", "Please enter your comments.");
        if (!ModelState.IsValid) return View(f);
        f.Id = 0; f.CustomerId = Uid; f.Date = DateTime.Now;
        Db.Feedbacks.Add(f); Db.SaveChanges(); Ok("Thank you for your feedback."); return RedirectToAction("Index");
    }
}

[Authorize(Roles = "Admin,Retail,Accounts,Technical")]
public class DocumentsController : AppController
{
    private readonly IWebHostEnvironment _env;
    public DocumentsController(AppDb db, IWebHostEnvironment env) : base(db) { _env = env; }

    /// <summary>Retail staff / admin file a document (address proof, application form or receipt scan) against a customer.</summary>
    [Authorize(Roles = "Admin,Retail")]
    [HttpPost]
    public async Task<IActionResult> Upload(int customerId, DocType type, string orderNo, IFormFile file)
    {
        var c = Db.Customers.Include(x => x.City).FirstOrDefault(x => x.Id == customerId);
        if (c == null || file == null || file.Length == 0) { Err("Choose a customer and a file."); return RedirectToAction("Index"); }
        var year = DateTime.Now.Year; var rel = Path.Combine("uploads", year.ToString(), c.City.Code);
        var dir = Path.Combine(_env.WebRootPath, rel); Directory.CreateDirectory(dir);
        var name = $"{DateTime.Now:HHmmss}_{type}_{Path.GetFileName(file.FileName)}";
        using (var fs = System.IO.File.Create(Path.Combine(dir, name))) await file.CopyToAsync(fs);
        Db.Documents.Add(new Document { CustomerId = c.Id, OrderNo = orderNo, Type = type, FileName = file.FileName, StoredPath = "/" + rel.Replace(Path.DirectorySeparatorChar, '/') + "/" + name, Year = year, CityCode = c.City.Code });
        Db.SaveChanges(); Ok("Document filed under " + year + " / area " + c.City.Code + ".");
        return RedirectToAction("Index");
    }

    /// <summary>Customer documents filed year-wise and area-wise.</summary>
    public IActionResult Index(int? year, string city)
    {
        var q = Db.Documents.Include(d => d.Customer).AsQueryable();
        if (year != null) q = q.Where(d => d.Year == year);
        if (!string.IsNullOrEmpty(city)) q = q.Where(d => d.CityCode == city);
        ViewBag.Cities = Db.Cities.ToList(); ViewBag.Year = year; ViewBag.City = city; ViewBag.Customers = Db.Customers.OrderBy(c => c.Name).ToList();
        return View(q.ToList().OrderByDescending(d => d.UploadedOn).ToList());
    }
}
