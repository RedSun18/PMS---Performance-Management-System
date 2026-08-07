using System.Text.Json;

namespace PerformanceManagement.Web.Pages.PmForm;

/// <summary>
/// Per-employee/year editing buffer for the PM Form, kept in session between postbacks —
/// the modern equivalent of the legacy per-employee session DataTables. Nothing is
/// database-authoritative here; workflow actions persist through WorkflowService.
/// </summary>
public class WorkingSet
{
    public string EmpCode { get; set; } = "";
    public int EvalYear { get; set; }
    /// <summary>DB row version the buffer was loaded from (stale-buffer detection).</summary>
    public int LoadedVersion { get; set; } = -1;

    public string? SelfAssessment { get; set; }
    public string? DevelopmentPlan { get; set; }
    public string? PromotionRecommendation { get; set; }
    public string? PromotionComments { get; set; }

    public List<Item> Kpis { get; set; } = new();
    public List<Item> Comps { get; set; } = new();

    public class Item
    {
        public int Seq { get; set; }
        /// <summary>Perspective (F/C/I/L) for KPIs; comp type (B/T) for competencies.</summary>
        public string Kind { get; set; } = "";
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Definition { get; set; }
        public string? DefinitionAr { get; set; }
        public string? Formula { get; set; }
        public string? FormulaAr { get; set; }
        public string? Target { get; set; }
        public int Weight { get; set; }
        public int Achievement { get; set; }
        public string? Comments { get; set; }
    }

    public void Resequence()
    {
        for (var i = 0; i < Kpis.Count; i++) Kpis[i].Seq = i + 1;
        for (var i = 0; i < Comps.Count; i++) Comps[i].Seq = i + 1;
    }

    private static string Key(string empCode, int year) => $"pmform:{empCode.Trim()}:{year}";

    public static WorkingSet? Load(ISession session, string empCode, int year)
    {
        var json = session.GetString(Key(empCode, year));
        return json is null ? null : JsonSerializer.Deserialize<WorkingSet>(json);
    }

    public void Save(ISession session) =>
        session.SetString(Key(EmpCode, EvalYear), JsonSerializer.Serialize(this));

    public static void Clear(ISession session, string empCode, int year) =>
        session.Remove(Key(empCode, year));
}
