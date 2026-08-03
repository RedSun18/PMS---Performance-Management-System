using System.Linq;
using PerformanceManagement.Core.Domain;

namespace PerformanceManagement.DemoSeeder;

/// <summary>
/// Purely fictional reference data for the Apex Corporation demo environment — no
/// department name, code, KPI, competency, or person here corresponds to any real company,
/// employee, or organization. Every array is a fixed, ordered list so that selecting from it
/// by index (as <see cref="DemoSeeder"/> does, driven by a seeded <see cref="Random"/>) is
/// fully reproducible run over run.
/// </summary>
public static class DemoReferenceData
{
    public static readonly (string Code, string NameEn)[] Departments =
    {
        ("CLM", "Claims Management"),
        ("UWR", "Underwriting"),
        ("SLS", "Sales & Distribution"),
        ("MKT", "Marketing & Brand"),
        ("FIN", "Finance & Accounting"),
        ("HRD", "Human Resources"),
        ("ITD", "Information Technology"),
        ("LGL", "Legal & Compliance"),
        ("RSK", "Risk Management"),
        ("ACT", "Actuarial Services"),
        ("CSR", "Customer Service"),
        ("OPS", "Operations"),
        ("AUD", "Internal Audit"),
        ("STR", "Corporate Strategy"),
        ("INV", "Investments & Treasury"),
    };

    public static readonly (string Code, string Description)[] Designations =
    {
        ("CO",  "Chief Officer"),
        ("VP",  "Vice President"),
        ("SDIR","Senior Director"),
        ("DIR", "Director"),
        ("SMGR","Senior Manager"),
        ("MGR", "Manager"),
        ("SAN", "Senior Analyst"),
        ("AN",  "Analyst"),
        ("CRD", "Coordinator"),
        ("ASO", "Associate"),
    };

    /// <summary>Two topical sections per department (e.g. IT → Software Development /
    /// Infrastructure &amp; Support) so a seeded employee's Department/Section/Designation read as
    /// a coherent, realistic org structure — without an actual Department→Section foreign key
    /// (Section stays the same flat, standalone reference table it always was; this mapping is
    /// seeder-only, used purely to pick a sensible section per employee).</summary>
    public static readonly IReadOnlyDictionary<string, (string Code, string Description)[]> SectionsByDept =
        new Dictionary<string, (string, string)[]>
        {
            ["CLM"] = new[] { ("CLP", "Claims Processing"), ("CLI", "Claims Investigation") },
            ["UWR"] = new[] { ("CUW", "Commercial Underwriting"), ("PUW", "Personal Lines Underwriting") },
            ["SLS"] = new[] { ("DSL", "Direct Sales"), ("BKR", "Broker Relations") },
            ["MKT"] = new[] { ("DMK", "Digital Marketing"), ("BRC", "Brand & Communications") },
            ["FIN"] = new[] { ("APY", "Accounts Payable"), ("FPA", "Financial Planning & Analysis") },
            ["HRD"] = new[] { ("TAC", "Talent Acquisition"), ("ERL", "Employee Relations") },
            ["ITD"] = new[] { ("SWD", "Software Development"), ("INF", "Infrastructure & Support") },
            ["LGL"] = new[] { ("CLG", "Corporate Legal"), ("RGC", "Regulatory Compliance") },
            ["RSK"] = new[] { ("ERM", "Enterprise Risk"), ("CDR", "Credit Risk") },
            ["ACT"] = new[] { ("PRA", "Pricing Actuarial"), ("RSA", "Reserving Actuarial") },
            ["CSR"] = new[] { ("CTC", "Contact Center"), ("CLS", "Client Support") },
            ["OPS"] = new[] { ("PEX", "Process Excellence"), ("OSP", "Operations Support") },
            ["AUD"] = new[] { ("FAU", "Financial Audit"), ("OAU", "Operational Audit") },
            ["STR"] = new[] { ("STP", "Strategic Planning"), ("BDV", "Business Development") },
            ["INV"] = new[] { ("PTM", "Portfolio Management"), ("TRO", "Treasury Operations") },
        };

    public static readonly (string Code, string Description)[] Sections =
        SectionsByDept.Values.SelectMany(s => s).ToArray();

    /// <summary>Grade "1" = most senior, "8" = entry level — mirrors the shape (numeric grade
    /// string, comma-separated grade lists on job families) of the real system without reusing
    /// any of its actual codes or weight splits.</summary>
    public static readonly (string Code, string NameEn, string GradesCsv, int KpiWeight, int CompWeight)[] JobFamilies =
    {
        ("JF1", "Executive Leadership",          "1",   70, 30),
        ("JF2", "Senior Management",             "2",   65, 35),
        ("JF3", "Middle Management",             "3,4", 55, 45),
        ("JF4", "Professional & Specialist",     "5,6", 45, 55),
        ("JF5", "Entry Level & Support",         "7,8", 35, 65),
    };

    public static readonly (string Code, string NameEn, int MinScore, int MaxScore)[] RatingScales =
    {
        ("1", "Outstanding",         90, 100),
        ("2", "Very Good",           80, 89),
        ("3", "Good",                65, 79),
        ("4", "Needs Improvement",    0, 64),
    };

    /// <summary>Perspective: F financial / C customer / I internal process / L learning & growth.</summary>
    public static readonly (string KpiId, string Name, string Perspective)[] Kpis =
    {
        ("KPI001", "Revenue Growth",                     "F"),
        ("KPI002", "Underwriting Profitability",         "F"),
        ("KPI003", "Cost Efficiency Ratio",               "F"),
        ("KPI004", "Investment Return",                   "F"),
        ("KPI005", "Budget Adherence",                     "F"),
        ("KPI006", "Customer Satisfaction Score",           "C"),
        ("KPI007", "Policy Renewal Rate",                    "C"),
        ("KPI008", "Claims Resolution Time",                  "C"),
        ("KPI009", "Net Promoter Score",                       "C"),
        ("KPI010", "Cross-Sell Ratio",                          "C"),
        ("KPI011", "Process Automation Rate",                     "I"),
        ("KPI012", "Claims Processing Accuracy",                    "I"),
        ("KPI013", "Risk Exposure Reduction",                        "I"),
        ("KPI014", "Compliance Adherence",                            "I"),
        ("KPI015", "Digital Transformation Progress",                  "I"),
        ("KPI016", "Employee Training Completion",                       "L"),
        ("KPI017", "Talent Retention Rate",                                "L"),
        ("KPI018", "Leadership Development Participation",                  "L"),
    };

    /// <summary>CompType: B behavioral / T technical.</summary>
    public static readonly (string CompId, string Name, string CompType)[] Competencies =
    {
        ("COM001", "Leadership",                    "B"),
        ("COM002", "Communication",                 "B"),
        ("COM003", "Teamwork & Collaboration",       "B"),
        ("COM004", "Adaptability",                   "B"),
        ("COM005", "Integrity & Ethics",              "B"),
        ("COM006", "Customer Focus",                   "B"),
        ("COM007", "Analytical Thinking",               "T"),
        ("COM008", "Technical Proficiency",              "T"),
        ("COM009", "Risk Assessment",                     "T"),
        ("COM010", "Data-Driven Decision Making",           "T"),
        ("COM011", "Project Management",                     "T"),
        ("COM012", "Regulatory Knowledge",                     "T"),
    };

    // ---------------------------------------------------------------- Multi-national name pools
    // Grouped so DemoSeeder can round-robin across nationalities for a believable mixed
    // workforce. First/last names are combined by index, never sourced from any real person.

    public static readonly string[][] FirstNamesByGroup =
    {
        new[] { "John", "James", "Robert", "Michael", "William", "David", "Richard", "Thomas", "Sarah", "Emily", "Jessica", "Jennifer", "Elizabeth", "Amanda", "Laura" },
        new[] { "Michael", "David", "Kevin", "Jason", "Grace", "Amy", "Helen", "Jenny", "Lucy", "Susan", "Wei", "Ming", "Hui", "Ling", "Bo" },
        new[] { "Priya", "Raj", "Amit", "Anita", "Sanjay", "Deepa", "Vikram", "Neha", "Arun", "Kavita", "Rohan", "Meera", "Sunil", "Pooja", "Ravi" },
        new[] { "Omar", "Hassan", "Ahmed", "Ali", "Khalid", "Youssef", "Karim", "Tariq", "Fatima", "Amina", "Layla", "Nour", "Zainab", "Rania", "Huda" },
        new[] { "Maria", "Carlos", "Jose", "Ana", "Luis", "Carmen", "Miguel", "Sofia", "Diego", "Isabella", "Javier", "Camila", "Antonio", "Valentina", "Ricardo" },
        new[] { "Kwame", "Amara", "Chidi", "Zola", "Kofi", "Amina", "Tunde", "Ngozi", "Sipho", "Aisha", "Emeka", "Folake", "Jabari", "Adaeze", "Kamau" },
        new[] { "Hans", "Ingrid", "Lars", "Sofia", "Anders", "Elena", "Dimitri", "Katarina", "Stefan", "Natasha", "Marco", "Giulia", "Pierre", "Chantal", "Nikolai" },
        new[] { "James", "Anderson", "Emily", "David", "Sarah", "Michael", "Jennifer", "Robert", "Laura", "William", "Amanda", "Thomas", "Elizabeth", "Richard", "Michelle" },
    };

    public static readonly string[][] LastNamesByGroup =
    {
        new[] { "Smith", "Johnson", "Williams", "Brown", "Jones", "Davis", "Miller", "Wilson", "Anderson", "Taylor", "Moore", "Martin", "Jackson", "White", "Harris" },
        new[] { "Chen", "Wang", "Li", "Zhang", "Liu", "Wu", "Huang", "Zhou", "Kim", "Park", "Lee", "Choi", "Tanaka", "Suzuki", "Yamamoto" },
        new[] { "Patel", "Sharma", "Kumar", "Singh", "Gupta", "Reddy", "Rao", "Mehta", "Joshi", "Nair", "Iyer", "Chatterjee", "Desai", "Malhotra", "Verma" },
        new[] { "Hassan", "Ali", "Khan", "Rahman", "Farouk", "Aziz", "Mansour", "Saleh", "Ibrahim", "Nasser", "Haddad", "Karam", "Youssef", "Qureshi", "Malik" },
        new[] { "Garcia", "Rodriguez", "Martinez", "Hernandez", "Lopez", "Gonzalez", "Perez", "Sanchez", "Ramirez", "Torres", "Flores", "Rivera", "Gomez", "Diaz", "Reyes" },
        new[] { "Okafor", "Mensah", "Adeyemi", "Nwosu", "Diallo", "Osei", "Kamau", "Mwangi", "Abara", "Eze", "Bello", "Chukwu", "Owusu", "Mutua", "Njoroge" },
        new[] { "Muller", "Schmidt", "Andersson", "Nowak", "Kowalski", "Rossi", "Bianchi", "Novak", "Petrov", "Ivanov", "Dubois", "Bernard", "Larsen", "Hansen", "Nilsson" },
        new[] { "Wilson", "Davis", "Clark", "Lewis", "Robinson", "Walker", "Young", "King", "Scott", "Green", "Baker", "Adams", "Nelson", "Carter", "Mitchell" },
    };
}
