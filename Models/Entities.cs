using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NexusApp.Models;

// ---------- Enumerations ----------
public enum Role { Admin, Retail, Technical, Accounts }
public enum ConnType { D, B, T }   // D = Dial-up, B = Broadband, T = Telephone (landline) only
public enum OrderStatus { Received, Feasible, NotFeasible, Connected, Cancelled }
public enum ConnStatus { Active, TemporarilyInactive, PermanentlyInactive }
public enum BillingCycle { Monthly, Quarterly, HalfYearly, Yearly }
public enum PayMode { Cash, Card, Cheque, Online }
public enum DocType { AddressProof, ApplicationForm, Receipt }
public enum ProductType { Modem, Router, Telephone, Other }

// ---------- Masters ----------
public class City
{
    [ScaffoldColumn(false)] public int Id { get; set; }
    [Required] public string Name { get; set; }
    [Required, RegularExpression(@"^\d{3}$", ErrorMessage = "City code must be exactly 3 digits")]
    [DisplayName("City code (3 digits)")] public string Code { get; set; }
}

public class RetailShop
{
    [ScaffoldColumn(false)] public int Id { get; set; }
    [Required] public string Name { get; set; }
    public int CityId { get; set; }
    public string Address { get; set; }
    public string Phone { get; set; }
    [ScaffoldColumn(false)] public City City { get; set; }
}

public class Vendor
{
    [ScaffoldColumn(false)] public int Id { get; set; }
    [Required] public string Name { get; set; }
    [DisplayName("Contact person")] public string ContactPerson { get; set; }
    public string Phone { get; set; }
    public string Email { get; set; }
    public string Address { get; set; }
}

public class Product
{
    [ScaffoldColumn(false)] public int Id { get; set; }
    [Required] public string Name { get; set; }
    public ProductType Type { get; set; }
    public int VendorId { get; set; }
    [DisplayName("Stock available")] public int Stock { get; set; }
    [DisplayName("Unit price ($)")] public decimal UnitPrice { get; set; }
    [DisplayName("Replacement charge ($)")] public decimal ReplacementCharge { get; set; }
    [ScaffoldColumn(false)] public Vendor Vendor { get; set; }
}

public class Plan
{
    [ScaffoldColumn(false)] public int Id { get; set; }
    [Required] public string Name { get; set; }
    [DisplayName("Connection type")] public ConnType ConnType { get; set; }
    public string Description { get; set; }
    [DisplayName("Rental / pack price ($)")] public decimal Rental { get; set; }
    [DisplayName("Billing cycle / validity")] public BillingCycle Cycle { get; set; }
    [DisplayName("Hours (hourly packs, 0 = unlimited)")] public int Hours { get; set; }
    [DisplayName("Speed Kbps (0 = n/a)")] public int SpeedKbps { get; set; }
    [DisplayName("Local call $/min")] public decimal LocalRate { get; set; }
    [DisplayName("STD call $/min")] public decimal StdRate { get; set; }
    [DisplayName("Mobile messaging $/min")] public decimal SmsRate { get; set; }
    public bool Active { get; set; } = true;
    [NotMapped, ScaffoldColumn(false)] public int CycleMonths => Cycle switch { BillingCycle.Monthly => 1, BillingCycle.Quarterly => 3, BillingCycle.HalfYearly => 6, _ => 12 };
}

public class ChargeSettings
{
    [ScaffoldColumn(false)] public int Id { get; set; }
    [DisplayName("Security deposit - Dial-up ($)")] public decimal DepositDialUp { get; set; } = 325;
    [DisplayName("Security deposit - Broadband ($)")] public decimal DepositBroadband { get; set; } = 500;
    [DisplayName("Security deposit - Landline ($)")] public decimal DepositLandline { get; set; } = 250;
    [DisplayName("Service tax (%)")] public decimal TaxPercent { get; set; } = 12.24m;
    [DisplayName("Max feasible distance (km)")] public decimal MaxDistanceKm { get; set; } = 5;
    [DisplayName("Days after due date before auto-suspension")] public int GraceDays { get; set; } = 15;
    [DisplayName("Bill payment period (days)")] public int PaymentDays { get; set; } = 15;
}

public class Employee
{
    [ScaffoldColumn(false)] public int Id { get; set; }
    [Required] public string Name { get; set; }
    public Role Role { get; set; }
    [DisplayName("Retail shop (retail staff)")] public int? RetailShopId { get; set; }
    [Required] public string Username { get; set; }
    [ScaffoldColumn(false)] public string PasswordHash { get; set; }
    [NotMapped, DataType(DataType.Password), DisplayName("Password (leave blank to keep)")] public string NewPassword { get; set; }
    public string Phone { get; set; }
    public string Email { get; set; }
    public bool Active { get; set; } = true;
}

public class Customer
{
    [ScaffoldColumn(false)] public int Id { get; set; }
    [Required] public string Name { get; set; }
    [Required] public string Phone { get; set; }
    public string Email { get; set; }
    public string Address { get; set; }
    public int CityId { get; set; }
    [DisplayName("Corporate / bulk customer")] public bool IsCorporate { get; set; }
    [DisplayName("Company name")] public string CompanyName { get; set; }
    [ScaffoldColumn(false)] public string Username { get; set; }
    [ScaffoldColumn(false)] public string PasswordHash { get; set; }
    [ScaffoldColumn(false)] public DateTime RegisteredOn { get; set; } = DateTime.Now;
    [ScaffoldColumn(false)] public City City { get; set; }
}

// ---------- Orders / connections ----------
public class Order
{
    public int Id { get; set; }
    public string OrderNo { get; set; }
    public int CustomerId { get; set; }
    public int? ShopId { get; set; }
    public int? PlacedByEmployeeId { get; set; }
    public ConnType ConnType { get; set; }
    public int PlanId { get; set; }
    public int? LandlinePlanId { get; set; }
    public bool HasNexusLandline { get; set; }
    public int? EquipmentProductId { get; set; }
    public int Quantity { get; set; } = 1;
    public DateTime ApplicationDate { get; set; } = DateTime.Now;
    public OrderStatus Status { get; set; } = OrderStatus.Received;
    public decimal? DistanceKm { get; set; }
    public bool? ServerAvailable { get; set; }
    public bool? LandlineFeasible { get; set; }
    public bool? InternetFeasible { get; set; }
    public string Remarks { get; set; }
    public DateTime? StatusDate { get; set; }
    public int? UpdatedByEmployeeId { get; set; }
    public Customer Customer { get; set; }
    public RetailShop Shop { get; set; }
    public Plan Plan { get; set; }
    public Plan LandlinePlan { get; set; }
    public Product Equipment { get; set; }
    [NotMapped] public bool NeedsInternetCheck => ConnType != ConnType.T;
    [NotMapped] public bool NeedsLandlineCheck => ConnType == ConnType.T || (ConnType == ConnType.D && !HasNexusLandline);
}

public class Connection
{
    public int Id { get; set; }
    public string AccountId { get; set; }
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public int CityId { get; set; }
    public ConnType ConnType { get; set; }
    public int PlanId { get; set; }
    public int? EquipmentProductId { get; set; }
    public ConnStatus Status { get; set; } = ConnStatus.Active;
    public string StatusReason { get; set; }
    public bool SuspendedForNonPayment { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Now;
    public decimal DiscountPercent { get; set; }
    public decimal SecurityDeposit { get; set; }
    public bool InitialBilled { get; set; }
    public int? LinkedConnectionId { get; set; }
    /// <summary>Start of the current plan / hourly-pack validity (reset when a plan change is approved).</summary>
    public DateTime PlanStartDate { get; set; } = DateTime.Now;
    /// <summary>Security deposit returned to the customer on withdrawal / cancellation.</summary>
    public decimal DepositRefunded { get; set; }
    public DateTime? RefundedOn { get; set; }
    public Order Order { get; set; }
    public Customer Customer { get; set; }
    public City City { get; set; }
    public Plan Plan { get; set; }
    public Product Equipment { get; set; }
    public List<Bill> Bills { get; set; } = new();
}

public class ConnectionMaterial
{
    public int Id { get; set; }
    public int ConnectionId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; } = 1;
    public DateTime Date { get; set; } = DateTime.Now;
    public bool IsReplacement { get; set; }
    public decimal Charge { get; set; }
    public bool Billed { get; set; }
    public Product Product { get; set; }
    public Connection Connection { get; set; }
}

public class Bill
{
    public int Id { get; set; }
    public string BillNo { get; set; }
    public int ConnectionId { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal RentalCharge { get; set; }
    public decimal SecurityDeposit { get; set; }
    public int LocalMinutes { get; set; }
    public int StdMinutes { get; set; }
    public int SmsMinutes { get; set; }
    public decimal CallCharges { get; set; }
    public decimal ReplacementCharges { get; set; }
    public decimal Discount { get; set; }
    public decimal PreviousDue { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public bool CarriedForward { get; set; }
    public DateTime GeneratedDate { get; set; } = DateTime.Now;
    public DateTime DueDate { get; set; }
    public int? GeneratedById { get; set; }
    public Connection Connection { get; set; }
    public List<Payment> Payments { get; set; } = new();
    [NotMapped] public decimal Due => CarriedForward ? 0 : Total - Paid;
}

public class Payment
{
    public int Id { get; set; }
    public string ReceiptNo { get; set; }
    public int BillId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidOn { get; set; } = DateTime.Now;
    public PayMode Mode { get; set; }
    public int? ReceivedById { get; set; }
    public int? ShopId { get; set; }
    public Bill Bill { get; set; }
}

public class Feedback
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string OrderNo { get; set; }
    [Range(1, 5)] public int Rating { get; set; } = 5;
    [Required] public string Comments { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public Customer Customer { get; set; }
}

public class PurchaseOrder
{
    [ScaffoldColumn(false)] public int Id { get; set; }
    public int VendorId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; } = 10;
    [DisplayName("Unit cost ($)")] public decimal UnitCost { get; set; }
    [DisplayName("Amount paid to vendor ($)")] public decimal AmountPaid { get; set; }
    [ScaffoldColumn(false)] public DateTime OrderDate { get; set; } = DateTime.Now;
    [ScaffoldColumn(false)] public bool Received { get; set; }
    [ScaffoldColumn(false)] public Vendor Vendor { get; set; }
    [ScaffoldColumn(false)] public Product Product { get; set; }
}

public enum RequestStatus { Pending, Approved, Rejected }

/// <summary>A customer's request to move an existing connection to another plan of the same type.</summary>
public class PlanChange
{
    public int Id { get; set; }
    public int ConnectionId { get; set; }
    public int NewPlanId { get; set; }
    public string Reason { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public DateTime RequestedOn { get; set; } = DateTime.Now;
    public DateTime? DecidedOn { get; set; }
    public int? RequestedByEmployeeId { get; set; }
    public int? DecidedById { get; set; }
    public string DecisionNote { get; set; }
    public Connection Connection { get; set; }
    public Plan NewPlan { get; set; }
}

/// <summary>Hours of internet usage recorded against an hourly-pack connection.</summary>
public class UsageRecord
{
    public int Id { get; set; }
    public int ConnectionId { get; set; }
    public decimal Hours { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public int? RecordedById { get; set; }
    public Connection Connection { get; set; }
}

/// <summary>In-app message to a customer about an order, bill, connection or request.</summary>
public class Notification
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Message { get; set; }
    public string Link { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.Now;
    public bool IsRead { get; set; }
}

public class Document
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string OrderNo { get; set; }
    public DocType Type { get; set; }
    public string FileName { get; set; }
    public string StoredPath { get; set; }
    public int Year { get; set; }
    public string CityCode { get; set; }
    public DateTime UploadedOn { get; set; } = DateTime.Now;
    public Customer Customer { get; set; }
}
