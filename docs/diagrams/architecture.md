# System & Layer Architecture

## System architecture

```mermaid
flowchart TB
    subgraph Client["Client"]
        Browser["Browser (EN / AR, RTL-aware)"]
    end

    subgraph WebHost["PerformanceManagement.Web (ASP.NET Core 8 Razor Pages, Kestrel)"]
        Pipeline["Middleware pipeline:<br/>Exception handler → HSTS/HTTPS → Forwarded headers →<br/>Security headers → Static files → Routing → Auth →<br/>Localization → Authorization → Session"]
        Pages["Razor Pages<br/>(Dashboard, PM Form, PM Form Summary, Employees,<br/>Reference Master, Reports, Settings, Users,<br/>Workflow Administration, Jobs, Admin/LoginAs)"]
        Quartz["Quartz.NET in-process scheduler<br/>(6 registered jobs, RAMJobStore)"]
    end

    subgraph CoreLib["PerformanceManagement.Core (class library)"]
        Services["Domain services:<br/>WorkflowService · WorkflowAdminService · PermissionService<br/>FormValidationService · RatingService · ReportDataService<br/>ReportExportService · EmailService · NotificationService<br/>AuditService · SettingsService · FormLinkService · PdfRenderer"]
        EFCore["EF Core 8 (PmDbContext)"]
    end

    subgraph External["External processes / services"]
        Postgres[("PostgreSQL 16")]
        Smtp["SMTP relay"]
        Chromium["Headless Chromium (PuppeteerSharp)<br/>PDF rendering"]
        ClosedXML["ClosedXML<br/>Excel (.xlsx) generation"]
        DataProtection["ASP.NET Core Data Protection<br/>(encrypts SMTP + Login-As passwords at rest)"]
    end

    Browser -->|HTTPS| Pipeline --> Pages
    Pages --> Services
    Quartz --> Services
    Services --> EFCore --> Postgres
    Services --> Smtp
    Services --> Chromium
    Services --> ClosedXML
    Services --> DataProtection
```

## Layer architecture & project dependencies

```mermaid
flowchart LR
    subgraph Solution["PerformanceManagement.sln"]
        Web["PerformanceManagement.Web<br/>Razor Pages · Program.cs · wwwroot · Jobs"]
        Core["PerformanceManagement.Core<br/>Domain entities · Services · Data (EF Core + Migrations)<br/>Resources (EmailResource, PdfResource)"]
        Importer["PerformanceManagement.Importer<br/>One-time legacy CSV import CLI"]
        Tests["PerformanceManagement.Tests<br/>xUnit, in-memory SQLite fixture (TestHost)"]
    end

    Web --> Core
    Importer --> Core
    Tests --> Core
```

*Dependency direction is strictly one-way: `Web` and `Importer` both depend on `Core`; `Core` has no dependency back on either. `Core` contains all business logic and is the only project the automated test suite exercises directly.*

## Email pipeline

```mermaid
flowchart LR
    Trigger["Workflow transition<br/>or scheduled job<br/>(reminder / escalation)"] --> Spec["EmailSpec<br/>(template key, To, Cc, subject, body, idempotency key)"]
    Spec --> Dispatch["EmailService.DispatchAsync"]
    Dispatch --> Dedup{"Dev redirect<br/>configured?"}
    Dedup -->|Yes| Redirect["Send to the configured<br/>redirect address only"]
    Dedup -->|No| Real["Send to real To / Cc recipients"]
    Redirect --> Smtp["SMTP relay<br/>(System Settings)"]
    Real --> Smtp
    Dispatch --> Log[("EmailLog table<br/>SENT / FAILED / LOGGED /<br/>DISABLED / SKIPPED_*")]
```

## Reporting pipeline (PDF / Excel)

```mermaid
flowchart LR
    ReportsPage["Reports page<br/>(Employee / Department / Manager / Overall)"] --> DataSvc["ReportDataService<br/>(fresh query per request, never cached)"]
    DataSvc --> ExportSvc["ReportExportService"]
    ExportSvc -->|PDF| PdfRenderer["PdfRenderer<br/>renders HTML via headless Chromium (PuppeteerSharp)"]
    ExportSvc -->|Excel| ClosedXML["ClosedXML workbook builder"]
    PdfRenderer --> PdfFile["employee/department/manager/overall report .pdf"]
    ClosedXML --> XlsxFile[".xlsx workbook"]
```
