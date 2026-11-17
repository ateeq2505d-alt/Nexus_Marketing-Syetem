using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusApp.Data;
using NexusApp.Models;
using NexusApp.Services;

namespace NexusApp.Controllers;

// ============ Admin-only master data (generic CRUD + search) ============

[Authorize(Roles = "Admin")]
public class CitiesController : CrudController<City>
{
    public CitiesController(AppDb db) : base(db) { }
    protected override DbSet<City> Set => Db.Cities;
    protected override string Title => "City";
    protected override string Hint => "Each city in the territory has a unique 3-digit numeric code used inside customer account IDs.";
}

[Authorize(Roles = "Admin")]
public class ShopsController : CrudController<RetailShop>
{
    public ShopsController(AppDb db) : base(db) { }
    protected override DbSet<RetailShop> Set => Db.Shops;
    protected override string Title => "Retail shop";
    protected override Dictionary<string, Dictionary<int, string>> Lookups() => new() { ["CityId"] = Db.Cities.ToDictionary(c => c.Id, c => c.Name) };
}

[Authorize(Roles = "Admin")]
public class VendorsController : CrudController<Vendor>
{
    public VendorsController(AppDb db) : base(db) { }
    protected override DbSet<Vendor> Set => Db.Vendors;
    protected override string Title => "Vendor";
}

[Authorize(Roles = "Admin,Technical")]
public class ProductsController : CrudController<Product>
{
    public ProductsController(AppDb db) : base(db) { }
    protected override DbSet<Product> Set => Db.Products;
    protected override string Title => "Product / equipment (stock)";
    protected override Dictionary<string, Dictionary<int, string>> Lookups() => new() { ["VendorId"] = Db.Vendors.ToDictionary(c => c.Id, c => c.Name) };
}

[Authorize(Roles = "Admin")]
public class PlansController : CrudController<Plan>
{
    public PlansController(AppDb db) : base(db) { }
    protected override DbSet<Plan> Set => Db.Plans;
    protected override string Title => "Plan";
    protected override string Hint => "Plans and the charges levied for them. Customers and visitors can view active plans on the public Plans page.";
}

[Authorize(Roles = "Admin")]
public class EmployeesController : CrudController<Employee>
{
    public EmployeesController(AppDb db) : base(db) { }
    protected override DbSet<Employee> Set => Db.Employees;
    protected override string Title => "Employee";
    protected override string Hint => "Admin, retail outlet, technical and accounts staff each get their own login credentials.";
    protected override Dictionary<string, Dictionary<int, string>> Lookups() => new() { ["RetailShopId"] = Db.Shops.ToDictionary(c => c.Id, c => c.Name) };
    protected override void BeforeSave(Employee e, bool isNew)
    {
        if (!string.IsNullOrWhiteSpace(e.NewPassword)) e.PasswordHash = Pwd.Hash(e.NewPassword);
        else if (isNew) ModelState.AddModelError("NewPassword", "Password is required for a new employee.");
        if (e.Role == Role.Retail && e.RetailShopId == null) ModelState.AddModelError("RetailShopId", "Retail staff must be assigned to a shop.");
    }
}

/// <summary>Purchase orders to equipment vendors; receiving one increases product stock.</summary>
[Authorize(Roles = "Admin")]
public class PurchasesController : CrudController<PurchaseOrder>
{
    public PurchasesController(AppDb db) : base(db) { }
    protected override DbSet<PurchaseOrder> Set => Db.Purchases;
    protected override string Title => "Purchase order (vendor)";
    protected override Dictionary<string, Dictionary<int, string>> Lookups() => new()
    {
        ["VendorId"] = Db.Vendors.ToDictionary(c => c.Id, c => c.Name),
        ["ProductId"] = Db.Products.ToDictionary(c => c.Id, c => c.Name)
    };
    protected override List<RowAction> Actions => new() { new RowAction("Mark received", "Receive", "btn-outline-success") };
    protected override string Hint => "Order equipment from vendors based on received connection requests (see Reports > Equipment demand). 'Mark received' adds the quantity to stock.";

    /// <summary>Marks a vendor purchase as received and adds the quantity to stock.</summary>
    [HttpPost]
    public IActionResult Receive(int id)
    {
        var p = Db.Purchases.Find(id);
        if (p == null || p.Received) { Err("Already received or not found."); return RedirectToAction("Index"); }
        p.Received = true;
        Db.Products.Find(p.ProductId).Stock += p.Quantity;
        Db.SaveChanges(); Ok("Stock updated.");
        return RedirectToAction("Index");
    }
}

// ============ Charge settings, reports, backup ============

[Authorize(Roles = "Admin")]
public class AdminController : AppController
{
    private readonly BillingService _bs;
    public AdminController(AppDb db, BillingService bs) : base(db) { _bs = bs; }

    /// <summary>Admin maintains deposits, tax, feasibility distance and billing settings.</summary>
    public IActionResult Settings() => View(Db.Settings.First());

    /// <summary>Admin maintains deposits, tax, feasibility distance and billing settings.</summary>
    [HttpPost]
    public async Task<IActionResult> Settings(IFormCollection _)
    {
        var s = Db.Settings.First();
        await TryUpdateModelAsync(s);
        if (!ModelState.IsValid) return View(s);
        Db.SaveChanges(); Ok("Charges and settings updated."); return RedirectToAction("Settings");
    }

    /// <summary>Equipment demand: pending/feasible orders per equipment compared with stock, to decide what to order from vendors.</summary>
    public IActionResult Demand()
    {
        var orders = Db.Orders.Where(o => o.Status == OrderStatus.Received || o.Status == OrderStatus.Feasible).ToList();
        var rows = Db.Products.Include(p => p.Vendor).ToList().Select(p =>
        {
            var need = orders.Where(o => o.EquipmentProductId == p.Id).Sum(o => o.Quantity);
            return new DemandRow { Product = p, Demand = need, Shortfall = Math.Max(0, need - p.Stock) };
        }).ToList();
        return View(rows);
    }

    /// <summary>Downloads a consistent copy of the database.</summary>
    public IActionResult Backup()
    {
        // consistent copy of the SQLite database for download (SQL Server deployments use native BACKUP DATABASE instead)
        if (!Db.Database.IsSqlite()) { Err("Download backup is available for the SQLite demo database. Use BACKUP DATABASE on SQL Server."); return RedirectToAction("Settings"); }
        var tmp = Path.Combine(Path.GetTempPath(), "nexus-backup-" + Guid.NewGuid() + ".db");
        Db.Database.ExecuteSqlRaw("VACUUM INTO {0}", tmp);
        var bytes = System.IO.File.ReadAllBytes(tmp); System.IO.File.Delete(tmp);
        return File(bytes, "application/octet-stream", $"nexus-backup-{DateTime.Now:yyyyMMdd-HHmm}.db");
    }
}

public class DemandRow { public Product Product { get; set; } public int Demand { get; set; } public int Shortfall { get; set; } }
