using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusApp.Data;
using NexusApp.Models;
using NexusApp.Services;

namespace NexusApp.Controllers;

public class AccountController : AppController
{
    public AccountController(AppDb db) : base(db) { }

    [HttpGet] public IActionResult Login(string returnUrl) { ViewBag.Return = returnUrl; return View(); }

    /// <summary>Signs a user in (employees first, then customers) and issues a role claim cookie.</summary>
    [HttpPost]
    public async Task<IActionResult> Login(string username, string password, string returnUrl)
    {
        username = (username ?? "").Trim();
        ClaimsIdentity id = null;
        var emp = Db.Employees.FirstOrDefault(e => e.Username == username && e.Active);
        if (emp != null && Pwd.Verify(password, emp.PasswordHash))
        {
            id = new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, emp.Id.ToString()), new Claim(ClaimTypes.Name, emp.Name),
                new Claim(ClaimTypes.Role, emp.Role.ToString()), new Claim("ShopId", emp.RetailShopId?.ToString() ?? ""), new Claim("uname", emp.Username) }, CookieAuthenticationDefaults.AuthenticationScheme);
        }
        else
        {
            var cu = Db.Customers.FirstOrDefault(c => c.Username == username);
            if (cu != null && Pwd.Verify(password, cu.PasswordHash))
                id = new ClaimsIdentity(new[] {
                    new Claim(ClaimTypes.NameIdentifier, cu.Id.ToString()), new Claim(ClaimTypes.Name, cu.Name), new Claim(ClaimTypes.Role, "Customer"), new Claim("uname", cu.Username) },
                    CookieAuthenticationDefaults.AuthenticationScheme);
        }
        if (id == null) { ViewBag.Error = "Invalid username or password."; return View(); }
        await HttpContext.SignInAsync(new ClaimsPrincipal(id));
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction("Index", "Home");
    }

    [HttpGet] public IActionResult Register() { ViewBag.Cities = Db.Cities.ToList(); return View(new Customer()); }

    /// <summary>Self-registration for new customers.</summary>
    [HttpPost]
    public async Task<IActionResult> Register(Customer c, string username, string password)
    {
        ViewBag.Cities = Db.Cities.ToList();
        username = (username ?? "").Trim();
        if (username.Length < 3) ModelState.AddModelError("", "Username must be at least 3 characters.");
        if (string.IsNullOrEmpty(password) || password.Length < 6) ModelState.AddModelError("", "Password must be at least 6 characters.");
        if (Db.Customers.Any(x => x.Username == username) || Db.Employees.Any(x => x.Username == username)) ModelState.AddModelError("", "That username is taken.");
        if (!ModelState.IsValid) return View(c);
        c.Username = username; c.PasswordHash = Pwd.Hash(password); c.RegisteredOn = DateTime.Now;
        Db.Customers.Add(c); Db.SaveChanges();
        var id = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, c.Id.ToString()), new Claim(ClaimTypes.Name, c.Name), new Claim(ClaimTypes.Role, "Customer"), new Claim("uname", c.Username) }, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(new ClaimsPrincipal(id));
        Ok("Welcome! Your customer account has been created.");
        return RedirectToAction("Index", "Home");
    }

    /// <summary>Signs the user out.</summary>
    public async Task<IActionResult> Logout() { await HttpContext.SignOutAsync(); return RedirectToAction("Index", "Home"); }
    /// <summary>Shown when the role is not allowed to open a page.</summary>
    public IActionResult Denied() => View();
}

public class HomeVm
{
    public Dictionary<string, int> Counts { get; set; } = new();
    public List<Order> RecentOrders { get; set; } = new();
    public List<Bill> DueBills { get; set; } = new();
    public Dictionary<string, int> StatusCounts { get; set; } = new();
    public decimal TotalDue { get; set; }
    public decimal Collected { get; set; }
    public int Unread { get; set; }
}

public class HomeController : AppController
{
    private readonly BillingService _bs;
    public HomeController(AppDb db, BillingService bs) : base(db) { _bs = bs; }

    /// <summary>Lists records (with optional search / status filter).</summary>
    public IActionResult Index()
    {
        var vm = new HomeVm();
        if (User.Identity.IsAuthenticated && !IsCustomer)
        {
            vm.Counts["Awaiting feasibility"] = Db.Orders.Count(o => o.Status == Models.OrderStatus.Received);
            vm.Counts["Ready to connect"] = Db.Orders.Count(o => o.Status == Models.OrderStatus.Feasible);
            vm.Counts["Active connections"] = Db.Connections.Count(c => c.Status == ConnStatus.Active);
            vm.Counts["Inactive connections"] = Db.Connections.Count(c => c.Status != ConnStatus.Active);
            vm.Counts["Customers"] = Db.Customers.Count();
            var open = Db.Bills.Include(b => b.Connection).ThenInclude(c => c.Customer).Where(b => !b.CarriedForward).ToList();
            vm.Counts["Bills with dues"] = open.Count(b => b.Due > 0);
            vm.TotalDue = open.Sum(b => b.Due);
            vm.Collected = Db.Payments.ToList().Sum(p => p.Amount);
            vm.DueBills = open.Where(b => b.Due > 0).OrderBy(b => b.DueDate).Take(5).ToList();
            foreach (var st in Enum.GetValues<Models.OrderStatus>()) vm.StatusCounts[st.ToString()] = Db.Orders.Count(o => o.Status == st);
            var oq = Db.Orders.Include(o => o.Customer).Include(o => o.Plan).AsQueryable();
            if (User.IsInRole("Retail") && ShopId != null) oq = oq.Where(o => o.ShopId == ShopId);
            vm.RecentOrders = oq.ToList().OrderByDescending(o => o.ApplicationDate).Take(6).ToList();
        }
        else if (IsCustomer)
        {
            vm.Counts["My orders"] = Db.Orders.Count(o => o.CustomerId == Uid);
            vm.Counts["My connections"] = Db.Connections.Count(c => c.CustomerId == Uid);
            var mine = Db.Bills.Include(b => b.Connection).Where(b => b.Connection.CustomerId == Uid && !b.CarriedForward).ToList();
            vm.TotalDue = mine.Sum(b => b.Due);
            vm.DueBills = mine.Where(b => b.Due > 0).OrderBy(b => b.DueDate).Take(5).ToList();
            vm.Unread = Db.Notifications.Count(n => n.CustomerId == Uid && !n.IsRead);
            vm.RecentOrders = Db.Orders.Include(o => o.Customer).Include(o => o.Plan).Where(o => o.CustomerId == Uid).ToList().OrderByDescending(o => o.ApplicationDate).Take(6).ToList();
        }
        return View(vm);
    }

    /// <summary>Public plan catalogue.</summary>
    public IActionResult Plans() => View(Db.Plans.Where(p => p.Active).ToList().OrderBy(p => p.ConnType).ThenBy(p => p.Rental).ToList());

    /// <summary>Public simple search: status of an order by order id, or of a connection by account id.</summary>
    [HttpGet]
    public IActionResult Search(string orderId, string accountId)
    {
        if (!string.IsNullOrWhiteSpace(orderId))
        {
            var o = Db.Orders.Include(x => x.Plan).Include(x => x.Customer).FirstOrDefault(x => x.OrderNo == orderId.Trim().ToUpper());
            ViewBag.Order = o; ViewBag.Searched = "order";
        }
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            var c = Db.Connections.Include(x => x.Plan).Include(x => x.Customer).FirstOrDefault(x => x.AccountId == accountId.Trim().ToUpper());
            ViewBag.Conn = c; ViewBag.Searched = "account";
            if (c != null) ViewBag.Due = _bs.OutstandingFor(c.Id);
        }
        return View();
    }

    /// <summary>Advanced search by id, name, connection type, period and contact number.</summary>
    [Authorize]
    public IActionResult Advanced(string kind, string id, string name, string type, DateTime? from, DateTime? to, string phone)
    {
        var vm = new AdvancedVm { Kind = kind ?? "order" };
        if (Request.Query.Count == 0) return View(vm);
        ConnType? ct = Enum.TryParse<ConnType>(type, out var t) ? t : null;
        id = id?.Trim().ToUpper();
        if (vm.Kind == "order")
        {
            var q = Db.Orders.Include(o => o.Customer).Include(o => o.Plan).AsQueryable();
            if (IsCustomer) q = q.Where(o => o.CustomerId == Uid);
            if (!string.IsNullOrEmpty(id)) q = q.Where(o => o.OrderNo == id);
            if (!string.IsNullOrEmpty(name)) q = q.Where(o => o.Customer.Name.Contains(name));
            if (!string.IsNullOrEmpty(phone)) q = q.Where(o => o.Customer.Phone.Contains(phone));
            if (ct != null) q = q.Where(o => o.ConnType == ct);
            var list = q.ToList();
            if (from != null) list = list.Where(o => o.ApplicationDate >= from).ToList();
            if (to != null) list = list.Where(o => o.ApplicationDate < to.Value.AddDays(1)).ToList();
            vm.Orders = list.OrderByDescending(o => o.ApplicationDate).ToList();
        }
        else
        {
            var q = Db.Connections.Include(c => c.Customer).Include(c => c.Plan).AsQueryable();
            if (IsCustomer) q = q.Where(c => c.CustomerId == Uid);
            if (!string.IsNullOrEmpty(id)) q = q.Where(c => c.AccountId == id);
            if (!string.IsNullOrEmpty(name)) q = q.Where(c => c.Customer.Name.Contains(name));
            if (!string.IsNullOrEmpty(phone)) q = q.Where(c => c.Customer.Phone.Contains(phone));
            if (ct != null) q = q.Where(c => c.ConnType == ct);
            var list = q.ToList();
            if (from != null) list = list.Where(c => c.StartDate >= from).ToList();
            if (to != null) list = list.Where(c => c.StartDate < to.Value.AddDays(1)).ToList();
            vm.Connections = list.OrderByDescending(c => c.StartDate).ToList();
            vm.Dues = vm.Connections.ToDictionary(c => c.Id, c => _bs.OutstandingFor(c.Id));
        }
        return View(vm);
    }

    /// <summary>Generic error page.</summary>
    public IActionResult Error() => View();
}

public class AdvancedVm
{
    public string Kind { get; set; }
    public List<Order> Orders { get; set; }
    public List<Connection> Connections { get; set; }
    public Dictionary<int, decimal> Dues { get; set; }
}
