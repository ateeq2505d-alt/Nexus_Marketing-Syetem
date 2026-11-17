using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusApp.Data;
using NexusApp.Models;
using NexusApp.Services;

namespace NexusApp.Controllers;

[Authorize]
public class OrdersController : AppController
{
    private readonly IdService _ids; private readonly BillingService _bs; private readonly IWebHostEnvironment _env;
    public OrdersController(AppDb db, IdService ids, BillingService bs, IWebHostEnvironment env) : base(db) { _ids = ids; _bs = bs; _env = env; }

    private IQueryable<Order> Visible()
    {
        var q = Db.Orders.Include(o => o.Customer).ThenInclude(c => c.City).Include(o => o.Plan).Include(o => o.LandlinePlan).Include(o => o.Equipment).Include(o => o.Shop).AsQueryable();
        if (IsCustomer) q = q.Where(o => o.CustomerId == Uid);
        else if (User.IsInRole("Retail") && ShopId != null) q = q.Where(o => o.ShopId == ShopId);
        return q;
    }

    /// <summary>Lists records (with optional search / status filter).</summary>
    public IActionResult Index(string status)
    {
        var q = Visible();
        if (Enum.TryParse<OrderStatus>(status, out var s)) q = q.Where(o => o.Status == s);
        ViewBag.Status = status;
        return View(q.ToList().OrderByDescending(o => o.ApplicationDate).ToList());
    }

    /// <summary>Shows the full details of one record.</summary>
    public IActionResult Details(int id)
    {
        var o = Visible().FirstOrDefault(x => x.Id == id); if (o == null) return NotFound();
        ViewBag.Connections = Db.Connections.Include(c => c.Plan).Where(c => c.OrderId == id).ToList();
        ViewBag.Docs = Db.Documents.Where(d => d.OrderNo == o.OrderNo).ToList();
        ViewBag.Discount = _bs.EffectiveDiscount(o);
        ViewBag.MaxKm = _bs.Settings.MaxDistanceKm;
        return View(o);
    }

    // ---------- place an order: retail-outlet employees or customers only ----------
    private void FillCreate()
    {
        ViewBag.Plans = Db.Plans.Where(p => p.Active).ToList().OrderBy(p => p.Rental).ToList();
        ViewBag.Products = Db.Products.Where(p => p.Type == ProductType.Modem || p.Type == ProductType.Router || p.Type == ProductType.Telephone).ToList();
        ViewBag.Shops = Db.Shops.ToList();
        ViewBag.Cities = Db.Cities.ToList();
        ViewBag.Customers = IsCustomer ? new List<Customer>() : Db.Customers.OrderBy(c => c.Name).ToList();
    }

    /// <summary>Shows the form / saves a new record.</summary>
    [Authorize(Roles = "Retail,Customer")]
    public IActionResult Create() { FillCreate(); return View(new Order { Quantity = 1 }); }

    /// <summary>Shows the form / saves a new record.</summary>
    [Authorize(Roles = "Retail,Customer")]
    [HttpPost]
    public async Task<IActionResult> Create(Order o, int? existingCustomerId, string newName, string newPhone, string newEmail, string newAddress, int? newCityId,
        bool newCorporate, string newCompany, IFormFile addressProof, IFormFile applicationForm)
    {
        FillCreate();
        Customer cust = null; string newCreds = null;
        if (IsCustomer) { cust = Db.Customers.Find(Uid); if (cust == null) ModelState.AddModelError("", "Your customer account could not be found. Please sign out and sign in again."); }
        else if (existingCustomerId != null) { cust = Db.Customers.Find(existingCustomerId); if (cust == null) ModelState.AddModelError("", "The selected customer no longer exists."); }
        else
        {
            if (string.IsNullOrWhiteSpace(newName) || string.IsNullOrWhiteSpace(newPhone) || newCityId == null)
                ModelState.AddModelError("", "Choose an existing customer or enter the new customer's name, phone and city.");
            else
            {
                var pwd = "Nx" + Random.Shared.Next(100000, 999999);
                cust = new Customer { Name = newName, Phone = newPhone, Email = newEmail, Address = newAddress, CityId = newCityId.Value, IsCorporate = newCorporate, CompanyName = newCompany, PasswordHash = Pwd.Hash(pwd) };
                Db.Customers.Add(cust); Db.SaveChanges();
                cust.Username = "cust" + cust.Id; Db.SaveChanges();
                newCreds = $"New customer login created - username: {cust.Username}, password: {pwd}";
            }
        }
        var plan = Db.Plans.Find(o.PlanId);
        if (plan == null || plan.ConnType != o.ConnType) ModelState.AddModelError("PlanId", "Select a plan that matches the connection type.");
        if (o.ConnType == ConnType.D && !o.HasNexusLandline)
        {
            var lp = o.LandlinePlanId == null ? null : Db.Plans.Find(o.LandlinePlanId);
            if (lp == null || lp.ConnType != ConnType.T) ModelState.AddModelError("LandlinePlanId", "A landline from Nexus is compulsory for dial-up: pick a landline plan or tick 'already holds a Nexus landline'.");
        }
        if (o.ConnType != ConnType.D || o.HasNexusLandline) o.LandlinePlanId = null;
        if (o.ConnType == ConnType.T) o.EquipmentProductId = o.EquipmentProductId; // optional telephone set
        if (o.Quantity < 1) ModelState.AddModelError("Quantity", "Quantity must be at least 1.");
        int? shop = IsCustomer ? o.ShopId : ShopId;
        if (shop == null) ModelState.AddModelError("ShopId", "Select the retail shop through which the order is placed.");
        if (cust == null || !ModelState.IsValid) return View(o);

        o.Id = 0; o.CustomerId = cust.Id; o.ShopId = shop; o.OrderNo = _ids.NextOrderNo(o.ConnType);
        o.PlacedByEmployeeId = IsCustomer ? null : Uid; o.ApplicationDate = DateTime.Now; o.Status = OrderStatus.Received;
        Db.Orders.Add(o); Db.SaveChanges();
        var code = Db.Cities.Find(cust.CityId).Code;
        await SaveDoc(addressProof, DocType.AddressProof, cust, o, code);
        await SaveDoc(applicationForm, DocType.ApplicationForm, cust, o, code);
        Ok($"Order placed. Order ID: {o.OrderNo}. " + newCreds);
        return RedirectToAction("Details", new { id = o.Id });
    }

    /// <summary>Documents are filed by year and area (city code) as per the existing manual process.</summary>
    private async Task SaveDoc(IFormFile f, DocType t, Customer c, Order o, string code)
    {
        if (f == null || f.Length == 0) return;
        var year = DateTime.Now.Year;
        var rel = Path.Combine("uploads", year.ToString(), code);
        var dir = Path.Combine(_env.WebRootPath, rel); Directory.CreateDirectory(dir);
        var name = $"{o.OrderNo}_{t}_{Path.GetFileName(f.FileName)}";
        using (var fs = System.IO.File.Create(Path.Combine(dir, name))) await f.CopyToAsync(fs);
        Db.Documents.Add(new Document { CustomerId = c.Id, OrderNo = o.OrderNo, Type = t, FileName = f.FileName, StoredPath = "/" + rel.Replace('\\', '/') + "/" + name, Year = year, CityCode = code });
        Db.SaveChanges();
    }

    // ---------- technical staff: feasibility and connection creation ----------
    [Authorize(Roles = "Technical")]
    [HttpPost]
    public IActionResult Feasibility(int id, decimal distance, bool serverAvailable, string remarks)
    {
        var o = Db.Orders.Include(x => x.Customer).FirstOrDefault(x => x.Id == id); if (o == null) return NotFound();
        if (o.Status != OrderStatus.Received && o.Status != OrderStatus.NotFeasible && o.Status != OrderStatus.Feasible) { Err("Order is already " + o.Status); return RedirectToAction("Details", new { id }); }
        Ok(_bs.RecordFeasibility(o, distance, serverAvailable, remarks, Uid));
        return RedirectToAction("Details", new { id });
    }

    /// <summary>Technical staff create connection(s) for a feasible order and assign account ids.</summary>
    [Authorize(Roles = "Technical")]
    [HttpPost]
    public IActionResult CreateConnections(int id)
    {
        var o = Db.Orders.Include(x => x.Customer).FirstOrDefault(x => x.Id == id); if (o == null) return NotFound();
        var err = _bs.CreateConnections(o, Uid);
        if (err != null) Err(err); else Ok("Connection(s) created and account ID(s) assigned.");
        return RedirectToAction("Details", new { id });
    }

    /// <summary>Cancels an order that is not yet connected.</summary>
    [HttpPost]
    public IActionResult Cancel(int id)
    {
        var o = Visible().FirstOrDefault(x => x.Id == id); if (o == null) return NotFound();
        if (o.Status == OrderStatus.Connected || o.Status == OrderStatus.Cancelled) { Err("This order cannot be cancelled."); return RedirectToAction("Details", new { id }); }
        o.Status = OrderStatus.Cancelled; o.StatusDate = DateTime.Now; Db.SaveChanges();
        Ok("Order cancelled."); return RedirectToAction("Details", new { id });
    }
}
