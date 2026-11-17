namespace NexusApp.Services;
public static class UI
{
    public static string Css(string s) => s switch
    {
        "Received" => "bg-secondary", "Feasible" => "bg-info text-dark", "NotFeasible" => "bg-danger", "Connected" => "bg-success", "Cancelled" => "bg-dark",
        "Active" => "bg-success", "TemporarilyInactive" => "bg-warning text-dark", "PermanentlyInactive" => "bg-danger", _ => "bg-secondary"
    };
}
