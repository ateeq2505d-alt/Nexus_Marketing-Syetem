using NexusApp.Models;
using NexusApp.Services;

namespace NexusApp.Data;

/// <summary>Seeds masters (plans from the specification), demo users and a few sample orders/connections/bills.</summary>
public static class Seed
{
    public static void Run(AppDb db)
    {
        db.Database.EnsureCreated();
        if (db.Settings.Any()) return;
        db.Settings.Add(new ChargeSettings());

        var cities = new[] { new City { Name = "Metro City", Code = "101" }, new City { Name = "Lakeside", Code = "102" }, new City { Name = "Hillview", Code = "103" } };
        db.Cities.AddRange(cities);
        db.SaveChanges();
        db.Shops.AddRange(
            new RetailShop { Name = "Nexus Retail - Metro Central", CityId = cities[0].Id, Address = "12 Main Street", Phone = "555-0101" },
            new RetailShop { Name = "Nexus Retail - Lakeside", CityId = cities[1].Id, Address = "4 Lake Road", Phone = "555-0102" });
        var v1 = new Vendor { Name = "NetGear Supplies", ContactPerson = "A. Khan", Phone = "555-0201", Email = "sales@netgear-supplies.example", Address = "Industrial Area 5" };
        var v2 = new Vendor { Name = "ModemWorld Ltd", ContactPerson = "R. Patel", Phone = "555-0202", Email = "orders@modemworld.example", Address = "Tech Park 2" };
        db.Vendors.AddRange(v1, v2);
        db.SaveChanges();
        var modem = new Product { Name = "56K Dial-up Modem", Type = ProductType.Modem, VendorId = v2.Id, Stock = 40, UnitPrice = 30, ReplacementCharge = 35 };
        var router = new Product { Name = "Broadband Router X200", Type = ProductType.Router, VendorId = v1.Id, Stock = 25, UnitPrice = 65, ReplacementCharge = 80 };
        var phone = new Product { Name = "Desk Telephone Set", Type = ProductType.Telephone, VendorId = v1.Id, Stock = 3, UnitPrice = 15, ReplacementCharge = 20 };
        db.Products.AddRange(modem, router, phone);

        // ---- Plans exactly as in the specification ----
        Plan P(string n, ConnType t, decimal r, BillingCycle c, string d, int hrs = 0, int kbps = 0, decimal l = 0, decimal s = 0, decimal m = 0) =>
            new Plan { Name = n, ConnType = t, Rental = r, Cycle = c, Description = d, Hours = hrs, SpeedKbps = kbps, LocalRate = l, StdRate = s, SmsRate = m };
        db.Plans.AddRange(
            P("Dial-Up 10 Hrs", ConnType.D, 50, BillingCycle.Monthly, "10 hours, validity 1 month", 10),
            P("Dial-Up 30 Hrs", ConnType.D, 130, BillingCycle.Quarterly, "30 hours, validity 3 months", 30),
            P("Dial-Up 60 Hrs", ConnType.D, 260, BillingCycle.HalfYearly, "60 hours, validity 6 months", 60),
            P("Dial-Up Unlimited 28 Kbps - Monthly", ConnType.D, 75, BillingCycle.Monthly, "Unlimited usage at 28 Kbps", 0, 28),
            P("Dial-Up Unlimited 28 Kbps - Quarterly", ConnType.D, 150, BillingCycle.Quarterly, "Unlimited usage at 28 Kbps", 0, 28),
            P("Dial-Up Unlimited 56 Kbps - Monthly", ConnType.D, 100, BillingCycle.Monthly, "Unlimited usage at 56 Kbps", 0, 56),
            P("Dial-Up Unlimited 56 Kbps - Quarterly", ConnType.D, 180, BillingCycle.Quarterly, "Unlimited usage at 56 Kbps", 0, 56),
            P("Broadband 30 Hrs", ConnType.B, 175, BillingCycle.Monthly, "30 hours, validity 1 month", 30),
            P("Broadband 60 Hrs", ConnType.B, 315, BillingCycle.HalfYearly, "60 hours, validity 6 months", 60),
            P("Broadband Unlimited 64 Kbps - Monthly", ConnType.B, 225, BillingCycle.Monthly, "Unlimited usage at 64 Kbps", 0, 64),
            P("Broadband Unlimited 64 Kbps - Quarterly", ConnType.B, 400, BillingCycle.Quarterly, "Unlimited usage at 64 Kbps", 0, 64),
            P("Broadband Unlimited 128 Kbps - Monthly", ConnType.B, 350, BillingCycle.Monthly, "Unlimited usage at 128 Kbps", 0, 128),
            P("Broadband Unlimited 128 Kbps - Quarterly", ConnType.B, 445, BillingCycle.Quarterly, "Unlimited usage at 128 Kbps", 0, 128),
            P("Landline Local Plan (Unlimited)", ConnType.T, 75, BillingCycle.Yearly, "Local plan: yearly rental + call charges 55c/min", 0, 0, 0.55m),
            P("Landline Monthly Plan", ConnType.T, 35, BillingCycle.Monthly, "Monthly rental + call charges 75c/min", 0, 0, 0.75m),
            P("Landline STD Plan - Monthly", ConnType.T, 125, BillingCycle.Monthly, "Local 70c, STD $2.25, mobile messaging $1.00 per minute", 0, 0, 0.70m, 2.25m, 1.00m),
            P("Landline STD Plan - Half-Yearly", ConnType.T, 420, BillingCycle.HalfYearly, "Local 60c, STD $2.00, mobile messaging $1.15 per minute", 0, 0, 0.60m, 2.00m, 1.15m),
            P("Landline STD Plan - Yearly", ConnType.T, 780, BillingCycle.Yearly, "Local 60c, STD $1.75, mobile messaging $1.25 per minute (yearly rental not given in spec - assumed $780, editable by admin)", 0, 0, 0.60m, 1.75m, 1.25m));
        db.SaveChanges();

        var shop1 = db.Shops.First();
        db.Employees.AddRange(
            new Employee { Name = "Alice Admin", Role = Role.Admin, Username = "admin", PasswordHash = Pwd.Hash("Admin@123"), Phone = "555-1000", Email = "admin@nexus.example" },
            new Employee { Name = "Ravi Retail", Role = Role.Retail, Username = "retail1", PasswordHash = Pwd.Hash("Retail@123"), RetailShopId = shop1.Id, Phone = "555-1001" },
            new Employee { Name = "Tina Tech", Role = Role.Technical, Username = "tech1", PasswordHash = Pwd.Hash("Tech@123"), Phone = "555-1002" },
            new Employee { Name = "Adam Accounts", Role = Role.Accounts, Username = "acc1", PasswordHash = Pwd.Hash("Acc@123"), Phone = "555-1003" });
        var c1 = new Customer { Name = "John Smith", Phone = "555-2001", Email = "john@example.com", Address = "7 Elm Street", CityId = cities[0].Id, Username = "cust1", PasswordHash = Pwd.Hash("Cust@123") };
        var c2 = new Customer { Name = "Acme Traders", Phone = "555-2002", Email = "it@acme.example", Address = "Plot 9, Business Bay", CityId = cities[1].Id, IsCorporate = true, CompanyName = "Acme Traders Ltd", Username = "acme", PasswordHash = Pwd.Hash("Cust@123") };
        db.Customers.AddRange(c1, c2);
        db.SaveChanges();

        // ---- sample workflow data ----
        var ids = new IdService(db);
        var bs = new BillingService(db, ids);
        var techId = db.Employees.First(e => e.Role == Role.Technical).Id;
        var accId = db.Employees.First(e => e.Role == Role.Accounts).Id;
        var retailId = db.Employees.First(e => e.Role == Role.Retail).Id;
        var bb = db.Plans.First(p => p.Name.StartsWith("Broadband Unlimited 64 Kbps - Monthly"));
        var du = db.Plans.First(p => p.Name.StartsWith("Dial-Up Unlimited 56 Kbps - Monthly"));
        var ll = db.Plans.First(p => p.Name == "Landline Monthly Plan");

        Order NewOrder(Customer c, ConnType t, Plan p, Plan land, bool hasLand, Product eq, int qty, int daysAgo)
        {
            var o = new Order { OrderNo = ids.NextOrderNo(t), CustomerId = c.Id, ShopId = shop1.Id, PlacedByEmployeeId = retailId, ConnType = t, PlanId = p.Id, LandlinePlanId = land?.Id, HasNexusLandline = hasLand, EquipmentProductId = eq?.Id, Quantity = qty, ApplicationDate = DateTime.Now.AddDays(-daysAgo) };
            db.Orders.Add(o); db.SaveChanges(); o.Customer = c; return o;
        }
        // 1) Broadband - connected, billed and part paid
        var o1 = NewOrder(c1, ConnType.B, bb, null, false, router, 1, 40);
        bs.RecordFeasibility(o1, 2.5m, true, "Server capacity available", techId); bs.CreateConnections(o1, techId);
        var conn1 = db.Connections.First(c => c.OrderId == o1.Id);
        var b1 = bs.GenerateBill(conn1, DateTime.Today.AddDays(-35), 0, 0, 0, 0, accId);
        bs.RecordPayment(b1, 300, PayMode.Cash, retailId, shop1.Id);
        // 2) Dial-up with new landline - pending feasibility
        NewOrder(c1, ConnType.D, du, ll, false, modem, 1, 2);
        // 3) Telephone only - not feasible
        var o3 = NewOrder(c1, ConnType.T, ll, null, false, null, 1, 10);
        bs.RecordFeasibility(o3, 9m, false, "Exchange is beyond the permitted distance", techId);
        // 4) Corporate bulk order of 12 broadband connections - feasible, awaiting connection creation
        var o4 = NewOrder(c2, ConnType.B, bb, null, false, router, 12, 5);
        bs.RecordFeasibility(o4, 3m, true, "Corporate park covered", techId);
        // 5) Overdue bill example on a landline (will be auto-suspended by the overdue sweep)
        var o5 = NewOrder(c2, ConnType.T, ll, null, false, phone, 1, 70);
        bs.RecordFeasibility(o5, 1m, true, "OK", techId); bs.CreateConnections(o5, techId);
        var conn5 = db.Connections.First(c => c.OrderId == o5.Id);
        var b5 = bs.GenerateBill(conn5, DateTime.Today.AddDays(-65), 120, 0, 0, 0, accId);
        b5.DueDate = DateTime.Today.AddDays(-50); db.SaveChanges();
        bs.SuspendOverdue();

        // 6) Hourly pack (dial-up 30 hrs) with some usage, customer already holds a Nexus landline
        var pack = db.Plans.First(p => p.Name == "Dial-Up 30 Hrs");
        var o6 = NewOrder(c1, ConnType.D, pack, null, true, modem, 1, 20);
        bs.RecordFeasibility(o6, 1.5m, true, "OK", techId); bs.CreateConnections(o6, techId);
        var conn6 = db.Connections.First(c => c.OrderId == o6.Id);
        bs.RecordUsage(conn6.Id, 12.5m, techId);
        // 7) A pending plan-change request: broadband 64K -> 128K
        bs.RequestPlanChange(conn1, db.Plans.First(p => p.Name == "Broadband Unlimited 128 Kbps - Monthly").Id, "Need more speed for work from home", null);

        db.Feedbacks.Add(new Feedback { CustomerId = c1.Id, OrderNo = o1.OrderNo, Rating = 5, Comments = "Quick installation and friendly staff." });
        db.Purchases.Add(new PurchaseOrder { VendorId = v1.Id, ProductId = phone.Id, Quantity = 20, UnitCost = 12, AmountPaid = 0 });
        db.SaveChanges();
    }
}
