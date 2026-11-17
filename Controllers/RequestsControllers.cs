using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusApp.Data;
using NexusApp.Models;
using NexusApp.Services;

namespace NexusApp.Controllers;

/// <summary>Plan-change requests: customers or retail staff ask, technical staff or admin approve / reject.</summary>
[Authorize]
public class PlanChangesController : AppController
{
    private readonly BillingService _bs;
    public PlanChangesController(AppDb db, BillingService bs) : base(db) { _bs = bs; }

    /// <summary>Lists requests visible to the signed-in user (customers see only their own).</summary>
    public IActionResult Index()
    {
        var q = Db.PlanChanges.Include(p => p.Connection).ThenInclude(c => c.Customer).Include(p => p.Connection).ThenInclude(c => c.Plan).Include(p => p.NewPlan).AsQueryable();
        if (IsCustomer) q = q.Where(p => p.Connection.CustomerId == Uid);
        return View(q.ToList().OrderByDescending(p => p.RequestedOn).ToList());
    }

    /// <summary>Request form: choose a connection and a new plan of the same type.</summary>
    [Authorize(Roles = "Customer,Retail")]
    public IActionResult Create(int? connectionId)
    {
        FillLists(); ViewBag.Selected = connectionId; return View();
    }

    /// <summary>Shows the form / saves a new record.</summary>
    [Authorize(Roles = "Customer,Retail")]
    [HttpPost]
    public IActionResult Create(int connectionId, int newPlanId, string reason)
    {
        var c = Db.Connections.Find(connectionId); if (c == null) return NotFound();
        if (IsCustomer && c.CustomerId != Uid) return Forbid();
        var err = _bs.RequestPlanChange(c, newPlanId, reason, IsCustomer ? null : Uid);
        if (err != null) { Err(err); FillLists(); ViewBag.Selected = connectionId; return View(); }
        Ok("Plan change request submitted."); return RedirectToAction("Index");
    }

    /// <summary>Approves or rejects a plan change request.</summary>
    [Authorize(Roles = "Technical,Admin")]
    [HttpPost]
    public IActionResult Decide(int id, bool approve, string note)
    {
        var p = Db.PlanChanges.Find(id); if (p == null || p.Status != RequestStatus.Pending) return NotFound();
        _bs.DecidePlanChange(p, approve, note, Uid);
        Ok("Request " + (approve ? "approved" : "rejected") + "."); return RedirectToAction("Index");
    }

    private void FillLists()
    {
        var q = Db.Connections.Include(c => c.Customer).Include(c => c.Plan).Where(c => c.Status != ConnStatus.PermanentlyInactive);
        if (IsCustomer) q = q.Where(c => c.CustomerId == Uid);
        ViewBag.Connections = q.ToList();
        ViewBag.Plans = Db.Plans.Where(p => p.Active).ToList().OrderBy(p => p.Rental).ToList();
    }
}

/// <summary>Customer notifications about orders, bills, connection status and requests.</summary>
[Authorize(Roles = "Customer")]
public class NotificationsController : AppController
{
    public NotificationsController(AppDb db) : base(db) { }

    /// <summary>Shows notifications and marks them as read.</summary>
    public IActionResult Index()
    {
        var list = Db.Notifications.Where(n => n.CustomerId == Uid).ToList().OrderByDescending(n => n.CreatedOn).ToList();
        var model = list.Select(n => (n.Message, n.Link, n.CreatedOn, n.IsRead)).ToList();
        foreach (var n in list) n.IsRead = true;
        Db.SaveChanges();
        return View(model);
    }
}
