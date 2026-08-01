using PerformanceManagement.Core.Domain;

namespace PerformanceManagement.Core.Data;

/// <summary>
/// Deterministic seeds derived from legacy source (see docs/data-migration-plan.md §2).
/// </summary>
public static class SeedData
{
    // NOTE: the legacy production system recognized six named accounts as PM Form HR
    // administrators (adm22, adm12, adm4, adm2, adm16, adm10 — see docs/legacy-mapping.md
    // and docs/workflow-state-machine.md §3). This standalone rebuild is deliberately
    // decoupled from the legacy account list: development seeds a single
    // configurable administrator account instead (see DatabaseSeeder.SeedCoreAsync).
    // A real deployment assigns the Roles.HrAdmin role to whichever accounts the
    // organization designates — role membership, not username pattern-matching.

    /// <summary>Departments from the legacy system's hardcoded department-name map (reference DPT rows were not exported).</summary>
    public static readonly (string Code, string NameEn)[] Departments =
    {
        ("AC",  "Finance Dept."),
        ("INV", "Investment Dept."),
        ("ADM", "HR & Administration Dept."),
        ("CRC", "Credit Control Dept."),
        ("BDM", "Business Development Dept."),
        ("PRO", "Sales & Branches Dept."),
        ("LIF", "Life & Medical Insurance Dept."),
        ("MT",  "Motor Insurance Dept."),
        ("MAR", "Marine & Aviation Insurance Dept."),
        ("FGA", "Fire & General Accidents Insurance Dept."),
        ("RIN", "Reinsurance Dept."),
        ("EDP", "Data & Technology Dept."),
        ("IA",  "Internal Audit Dept."),
        ("LGL", "Legal Dept."),
        ("RMD", "Risk Management Dept."),
        ("COM", "Compliance Dept."),
        ("AAD", "Actuarial Advisory Dept."),
        ("TPM", "Top Management"),
        ("DRO", "Producers-Direct Cont.")
    };

    /// <summary>
    /// Perspective-rule exemptions and other data-driven exceptions
    /// (legacy hardcoded lists in KPIForm.aspx.vb).
    /// </summary>
    public static readonly (string EmpCode, string RuleCode, string Reason)[] Exceptions =
    {
        ("1058", ExceptionRule.PerspectiveMinExempt, "Approved temporary exception."),
        ("1470", ExceptionRule.PerspectiveMinExempt, "Approved temporary exception."),
        // Grade < 6 employees granted a 50/50 KPI/Competency mix
        ("1553", ExceptionRule.Kpi5050, "50/50 KPI-competency mix exception"),
        ("1376", ExceptionRule.Kpi5050, "50/50 KPI-competency mix exception"),
        ("1454", ExceptionRule.Kpi5050, "50/50 KPI-competency mix exception"),
        ("1470", ExceptionRule.Kpi5050, "50/50 KPI-competency mix exception"),
        ("1303", ExceptionRule.Kpi5050, "50/50 KPI-competency mix exception"),
        ("1450", ExceptionRule.Kpi5050, "50/50 KPI-competency mix exception"),
        ("1550", ExceptionRule.Kpi5050, "50/50 KPI-competency mix exception"),
        ("1523", ExceptionRule.Kpi5050, "50/50 KPI-competency mix exception"),
        ("1058", ExceptionRule.Kpi5050, "50/50 KPI-competency mix exception"),
        ("1579", ExceptionRule.Kpi5050, "50/50 KPI-competency mix exception"),
        // Temporary: self-mapped direct managers while actual managers are on leave
        ("656",  ExceptionRule.SelfManager, "Temporary: actual manager 452 on leave"),
        ("1031", ExceptionRule.SelfManager, "Temporary: actual manager 1444 on leave"),
        // Temporary: view-only access to branch (PRO/BR) employee forms
        ("1541", ExceptionRule.BranchViewer, "Temporary: view-only on branch employee forms")
    };

    /// <summary>
    /// HR-provided direct manager map (KPI_Direct_Managers_List_DEPT.xlsx), transcribed from
    /// s_DirectManagerMap in Personnel/KPIForm.aspx.vb. Key = employee, value = manager.
    /// Some designated managers are not formal org-chart managers.
    /// </summary>
    public static readonly (string EmpCode, string ManagerEmpCode)[] DirectManagerMap =
    {
        ("452", "1340"), ("548", "964"), ("656", "656"), ("666", "964"), ("671", "1273"),
        ("714", "1031"), ("740", "671"), ("748", "671"), ("798", "1266"), ("816", "964"),
        ("854", "1266"), ("858", "1266"), ("907", "666"), ("917", "1273"), ("937", "656"),
        ("964", "1340"), ("969", "1525"), ("977", "964"), ("1022", "937"), ("1031", "1031"),
        ("1032", "1525"), ("1052", "1525"), ("1058", "548"), ("1059", "1372"), ("1079", "1525"),
        ("1082", "1031"), ("1094", "671"), ("1095", "1340"), ("1105", "1477"), ("1121", "1457"),
        ("1143", "1457"), ("1157", "1547"), ("1160", "1525"), ("1169", "1031"), ("1173", "1525"),
        ("1175", "1457"), ("1178", "1283"), ("1204", "548"), ("1228", "1547"), ("1236", "1320"),
        ("1245", "1372"), ("1246", "1372"), ("1256", "1372"), ("1258", "1031"), ("1260", "1031"),
        ("1266", "1340"), ("1273", "1340"), ("1283", "671"), ("1284", "1477"), ("1286", "1289"),
        ("1289", "1463"), ("1291", "656"), ("1303", "1344"), ("1305", "1344"), ("1308", "671"),
        ("1320", "1463"), ("1326", "1266"), ("1327", "656"), ("1340", "964"), ("1341", "1289"),
        ("1344", "666"), ("1350", "907"), ("1351", "548"), ("1352", "1419"), ("1353", "854"),
        ("1360", "937"), ("1361", "1463"), ("1362", "1289"), ("1364", "1266"), ("1367", "1372"),
        ("1368", "1266"), ("1370", "1541"), ("1372", "1031"), ("1376", "666"), ("1377", "1525"),
        ("1382", "1525"), ("1385", "656"), ("1390", "1289"), ("1398", "656"), ("1403", "1541"),
        ("1404", "1525"), ("1405", "907"), ("1411", "1525"), ("1414", "1525"), ("1415", "1517"),
        ("1416", "1541"), ("1419", "964"), ("1423", "1547"), ("1427", "748"), ("1428", "1547"),
        ("1429", "964"), ("1431", "1547"), ("1434", "1289"), ("1437", "1320"), ("1440", "1419"),
        ("1444", "1340"), ("1445", "1525"), ("1446", "1289"), ("1447", "1517"), ("1450", "1095"),
        ("1451", "977"), ("1454", "1344"), ("1456", "671"), ("1457", "1320"), ("1460", "1169"),
        ("1463", "1340"), ("1465", "1260"), ("1466", "1344"), ("1469", "1289"), ("1470", "1344"),
        ("1471", "1266"), ("1473", "1320"), ("1474", "1273"), ("1475", "1525"), ("1477", "1340"),
        ("1478", "1260"), ("1479", "1372"), ("1480", "1547"), ("1482", "1266"), ("1484", "1169"),
        ("1485", "964"), ("1487", "1169"), ("1488", "1517"), ("1491", "1260"), ("1492", "1547"),
        ("1493", "1419"), ("1494", "1095"), ("1495", "854"), ("1496", "977"), ("1499", "1456"),
        ("1500", "1517"), ("1501", "1260"), ("1502", "1273"), ("1503", "1525"), ("1504", "854"),
        ("1505", "1525"), ("1510", "1457"), ("1511", "937"), ("1512", "1308"), ("1513", "548"),
        ("1514", "1289"), ("1517", "964"), ("1520", "1095"), ("1521", "1273"), ("1522", "1517"),
        ("1523", "666"), ("1524", "1283"), ("1525", "548"), ("1526", "748"), ("1528", "656"),
        ("1529", "1372"), ("1530", "1525"), ("1531", "1525"), ("1532", "1456"), ("1533", "1260"),
        ("1534", "1547"), ("1536", "1344"), ("1538", "1289"), ("1541", "964"), ("1543", "1541"),
        ("1546", "1419"), ("1547", "666"), ("1549", "1477"), ("1550", "666"), ("1551", "1260"),
        ("1552", "1372"), ("1553", "1419"), ("1554", "1260"), ("1555", "1372"), ("1556", "1308"),
        ("1557", "1517"), ("1558", "1541"), ("1559", "1273"), ("1560", "1517"), ("1562", "1344"),
        ("1564", "1266"), ("1565", "1289"), ("1567", "1517"), ("1570", "1541"), ("1571", "1517"),
        ("1572", "1517"), ("1573", "1289"), ("1574", "1266"), ("1575", "1320"), ("1576", "1289"),
        ("1577", "1456"), ("1579", "1429"), ("1580", "1525")
    };
}
