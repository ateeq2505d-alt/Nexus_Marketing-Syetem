using System.Reflection;
using System.Security.Claims;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NexusApp.Data;
using NexusApp.Models;

namespace NexusApp.Controllers;

public abstract class AppController : Controller
{
    protected readonly AppDb Db;
    protected AppController(AppDb db) { Db = db; }

    protected int Uid => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
    protected bool IsCustomer => User.IsInRole("Customer");
    protected int? ShopId => int.TryParse(User.FindFirstValue("ShopId"), out var s) ? s : null;
    protected void Ok(string m) => TempData["ok"] = m;
    protected void Err(string m) => TempData["err"] = m;
}

/// <summary>One row action button shown in the generic list.</summary>
public record RowAction(string Label, string Action, string Css = "btn-outline-secondary");

public class CrudIndexVm
{
    public string Title { get; set; }
    public string Q { get; set; }
    public List<object> Items { get; set; }
    public List<PropertyInfo> Props { get; set; }
    public Dictionary<string, Dictionary<int, string>> Lookups { get; set; }
    public List<RowAction> Actions { get; set; } = new();
    public bool CanCreate { get; set; } = true;
    public string Hint { get; set; }
}

public class CrudFormVm
{
    public string Title { get; set; }
    public object Model { get; set; }
    public List<PropertyInfo> Props { get; set; }
    public Dictionary<string, Dictionary<int, string>> Lookups { get; set; }
}

/// <summary>Generic insert / update / delete / search screens for the master data tables.</summary>
public abstract class CrudController<T> : AppController where T : class, new()
{
    protected CrudController(AppDb db) : base(db) { }
    protected abstract DbSet<T> Set { get; }
    protected abstract string Title { get; }
    protected virtual IQueryable<T> Query => Set;
    protected virtual Dictionary<string, Dictionary<int, string>> Lookups() => new();
    protected virtual List<RowAction> Actions => new();
    protected virtual bool CanCreate => true;
    protected virtual string Hint => null;
    protected virtual void BeforeSave(T e, bool isNew) { }

    private static readonly Type[] Simple = { typeof(string), typeof(int), typeof(decimal), typeof(bool), typeof(DateTime), typeof(double) };
    private static bool IsSimple(Type t) { t = Nullable.GetUnderlyingType(t) ?? t; return t.IsEnum || Simple.Contains(t); }
    private static List<PropertyInfo> Props(bool forForm) => typeof(T).GetProperties()
        .Where(p => p.CanWrite && IsSimple(p.PropertyType) && !(p.GetCustomAttribute<ScaffoldColumnAttribute>()?.Scaffold == false && (forForm || p.Name != "Id") ))
        .Where(p => !(forForm && p.Name == "Id")).ToList();

    private int IdOf(object o) => (int)o.GetType().GetProperty("Id").GetValue(o);

    /// <summary>Lists records (with optional search / status filter).</summary>
    public IActionResult Index(string q)
    {
        var all = Query.AsNoTracking().ToList().Cast<object>().ToList();
        var props = typeof(T).GetProperties().Where(p => IsSimple(p.PropertyType) && p.GetCustomAttribute<ScaffoldColumnAttribute>()?.Scaffold != false && p.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>() == null).ToList();
        var lk = Lookups();
        if (!string.IsNullOrWhiteSpace(q))
            all = all.Where(o => string.Join(" ", props.Select(p => Display(p, o, lk))).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        return View("~/Views/Crud/Index.cshtml", new CrudIndexVm { Title = Title, Q = q, Items = all, Props = props, Lookups = lk, Actions = Actions, CanCreate = CanCreate, Hint = Hint });
    }

    private static string Display(PropertyInfo p, object o, Dictionary<string, Dictionary<int, string>> lk)
    {
        var v = p.GetValue(o);
        if (v == null) return "";
        if (lk.TryGetValue(p.Name, out var d) && v is int i) return d.GetValueOrDefault(i, i.ToString());
        return v.ToString();
    }

    /// <summary>Shows the form / saves a new record.</summary>
    public IActionResult Create() => FormView(new T(), "New " + Title);

    /// <summary>Shows the form / saves a new record.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(IFormCollection _)
    {
        var e = new T();
        await TryUpdateModelAsync(e);
        BeforeSave(e, true);
        if (!ModelState.IsValid) return FormView(e, "New " + Title);
        try { Set.Add(e); Db.SaveChanges(); }
        catch (DbUpdateException ex) { ModelState.AddModelError("", ex.InnerException?.Message ?? ex.Message); return FormView(e, "New " + Title); }
        Ok(Title + " saved."); return RedirectToAction("Index");
    }

    /// <summary>Shows the form / saves changes to an existing record.</summary>
    public IActionResult Edit(int id)
    {
        var e = Set.Find(id); if (e == null) return NotFound();
        return FormView(e, "Edit " + Title);
    }

    /// <summary>Shows the form / saves changes to an existing record.</summary>
    [HttpPost]
    public async Task<IActionResult> Edit(int id, IFormCollection _)
    {
        var e = Set.Find(id); if (e == null) return NotFound();
        await TryUpdateModelAsync(e);
        BeforeSave(e, false);
        if (!ModelState.IsValid) return FormView(e, "Edit " + Title);
        try { Db.SaveChanges(); }
        catch (DbUpdateException ex) { ModelState.AddModelError("", ex.InnerException?.Message ?? ex.Message); return FormView(e, "Edit " + Title); }
        Ok(Title + " updated."); return RedirectToAction("Index");
    }

    /// <summary>Deletes a record (refused if it is referenced elsewhere).</summary>
    [HttpPost]
    public IActionResult Delete(int id)
    {
        var e = Set.Find(id); if (e == null) return NotFound();
        try { Set.Remove(e); Db.SaveChanges(); Ok("Deleted."); }
        catch (DbUpdateException) { Db.ChangeTracker.Clear(); Err("This record is in use by other records and cannot be deleted."); }
        return RedirectToAction("Index");
    }

    protected IActionResult FormView(T e, string title) =>
        View("~/Views/Crud/Form.cshtml", new CrudFormVm { Title = title, Model = e, Props = Props(true), Lookups = Lookups() });
}
