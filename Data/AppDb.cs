using Microsoft.EntityFrameworkCore;
using NexusApp.Models;

namespace NexusApp.Data;

/// <summary>EF Core context. SQLite is used for the local demo (file nexus.db);
/// the same model can target SQL Server by swapping the provider in Program.cs.</summary>
public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> o) : base(o) { }
    public DbSet<City> Cities => Set<City>();
    public DbSet<RetailShop> Shops => Set<RetailShop>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<ChargeSettings> Settings => Set<ChargeSettings>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Connection> Connections => Set<Connection>();
    public DbSet<ConnectionMaterial> Materials => Set<ConnectionMaterial>();
    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<PurchaseOrder> Purchases => Set<PurchaseOrder>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<PlanChange> PlanChanges => Set<PlanChange>();
    public DbSet<UsageRecord> Usage => Set<UsageRecord>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        // store enums as readable text in the database
        c.Properties<Role>().HaveConversion<string>();
        c.Properties<ConnType>().HaveConversion<string>();
        c.Properties<OrderStatus>().HaveConversion<string>();
        c.Properties<ConnStatus>().HaveConversion<string>();
        c.Properties<BillingCycle>().HaveConversion<string>();
        c.Properties<PayMode>().HaveConversion<string>();
        c.Properties<DocType>().HaveConversion<string>();
        c.Properties<ProductType>().HaveConversion<string>();
        c.Properties<RequestStatus>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Order>().HasIndex(o => o.OrderNo).IsUnique();
        b.Entity<Connection>().HasIndex(c => c.AccountId).IsUnique();
        b.Entity<Employee>().HasIndex(e => e.Username).IsUnique();
        b.Entity<Order>().HasOne(o => o.LandlinePlan).WithMany().HasForeignKey(o => o.LandlinePlanId);
        b.Entity<Order>().HasOne(o => o.Plan).WithMany().HasForeignKey(o => o.PlanId);
        b.Entity<Order>().HasOne(o => o.Equipment).WithMany().HasForeignKey(o => o.EquipmentProductId);
        b.Entity<Connection>().HasOne(c => c.Equipment).WithMany().HasForeignKey(c => c.EquipmentProductId);
        b.Entity<Connection>().HasOne(c => c.Plan).WithMany().HasForeignKey(c => c.PlanId);
        b.Entity<Connection>().HasOne(c => c.Order).WithMany().HasForeignKey(c => c.OrderId);
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }
}
