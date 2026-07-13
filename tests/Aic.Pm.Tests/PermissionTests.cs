using Aic.Pm.Core.Domain;

namespace Aic.Pm.Tests;

/// <summary>Acceptance tests §B (permissions and visibility flags).</summary>
public class PermissionTests : IAsyncLifetime
{
    private readonly TestHost _h = new();

    public async Task InitializeAsync() => await _h.SeedAsync();
    public Task DisposeAsync() { _h.Dispose(); return Task.CompletedTask; }

    // ---- B.1 explicit HR admin list -------------------------------------------
    [Theory]
    [InlineData("adm22", true)]
    [InlineData("adm12", true)]
    [InlineData("adm4", true)]
    [InlineData("adm2", true)]
    [InlineData("adm16", true)]
    [InlineData("adm10", true)]
    [InlineData("ADM12", true)]     // case-insensitive
    [InlineData("adm99", false)]    // other admin accounts are NOT PM HR admins
    [InlineData("u1504", false)]
    [InlineData("", false)]
    public async Task IsHrAdmin_matches_exactly_the_approved_accounts(string user, bool expected) =>
        Assert.Equal(expected, await _h.Permissions.IsHrAdminAsync(user));

    // ---- B.2 direct-manager resolution -----------------------------------------
    [Fact]
    public async Task Manager_map_drives_manager_checks()
    {
        Assert.Equal("854", await _h.Permissions.GetManagerOfAsync("1504"));

        var mgr = await _h.PermsAsync("854", "1504");
        Assert.True(mgr.IsDirectManager);
        Assert.True(mgr.CanActAsManager);
        Assert.True(mgr.CanViewFullScores);

        var notMgr = await _h.PermsAsync("548", "1504");
        Assert.False(notMgr.IsDirectManager);
        Assert.False(notMgr.CanActAsManager);
        Assert.False(notMgr.CanView);
    }

    [Fact]
    public async Task Nobody_gets_manager_controls_on_their_own_form()
    {
        // 854 manages others, but on their own form they are an employee
        var self = await _h.PermsAsync("854", "854");
        Assert.True(self.IsSelf);
        Assert.False(self.CanActAsManager);
        Assert.False(self.CanViewFullScores);

        // Even an HR admin viewing their own form gets no HR actions (legacy isEmployee guard)
        _h.Db.Employees.Add(new Employee { EmpCode = "adm12", LatinName = "HR Admin 12" });
        await _h.Db.SaveChangesAsync();
        var hrSelf = await _h.Permissions.GetFormPermissionsAsync("adm12", "adm12", "adm12");
        Assert.True(hrSelf.IsHrAdmin);
        Assert.False(hrSelf.CanActAsHr);
    }

    // ---- B.3 self-manager exception (656, 1031) ---------------------------------
    [Fact]
    public async Task Self_manager_exception_allows_own_form_management()
    {
        // Seeded: 656 → 656 in the manager map + SELF_MANAGER exception
        var p = await _h.PermsAsync("656", "656");
        Assert.True(p.IsDirectManager);
        Assert.False(p.IsSelf);            // suppressed by the exception
        Assert.True(p.CanActAsManager);
    }

    // ---- B.4 branch viewer (1541) --------------------------------------------------
    [Fact]
    public async Task Branch_viewer_gets_view_only_on_branch_employees()
    {
        // 1370 is PRO/BR; 1541 is not their mapped manager (1370 → 1541 IS mapped in the
        // HR list, so pick an unmapped branch employee for the pure-viewer path)
        _h.Db.Employees.Add(new Employee { EmpCode = "9001", LatinName = "Branch Emp", DeptCode = "PRO", SectionCode = "BR" });
        await _h.Db.SaveChangesAsync();

        var p = await _h.PermsAsync("1541", "9001");
        Assert.True(p.IsBranchViewer);
        Assert.True(p.CanView);
        Assert.False(p.CanActAsManager);   // view-only: no editing anywhere

        // Not a branch employee → no access via the exception
        var q = await _h.PermsAsync("1541", "1504");
        Assert.False(q.IsBranchViewer);
        Assert.False(q.CanView);
    }

    // ---- job family resolution incl. 50/50 exception -------------------------------
    [Fact]
    public async Task Job_family_resolution_by_grade_and_5050_exception()
    {
        var grade7 = await _h.JobFamilies.ResolveAsync("1504", "7");
        Assert.True(grade7.Configured);
        Assert.Equal(("Middle Management", 60, 40), (grade7.FamilyName, grade7.KpiWeight, grade7.CompWeight));

        var grade5 = await _h.JobFamilies.ResolveAsync("1504", "5");
        Assert.Equal((0, 100), (grade5.KpiWeight, grade5.CompWeight));

        // 1058 is on the 50/50 exception list (grade 7 would normally be 60/40)
        var exception = await _h.JobFamilies.ResolveAsync("1058", "7");
        Assert.Equal((50, 50), (exception.KpiWeight, exception.CompWeight));

        var unknown = await _h.JobFamilies.ResolveAsync("1504", "X");
        Assert.False(unknown.Configured);
    }
}
