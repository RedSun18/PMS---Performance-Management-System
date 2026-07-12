# Legacy → Standalone Mapping

This document maps every legacy artifact that the standalone Mac KPI/Performance Management
system recreates. The **primary legacy source of truth** is
`References/Personnel/KPIForm.aspx` / `KPIForm.aspx.vb` (production PM Form).
`KPIFormTest/NEW/SPA` variants were used for comparison only; no behaviour was taken from them
that contradicts production.

## 1. Page mapping

| Legacy page (WebForms) | New page (Razor Pages) | Notes |
|---|---|---|
| `Personnel/KPIForm.aspx` + `.vb` | `/PmForm` (`Pages/PmForm/Index.cshtml`) | Full PM Form: employee selection, KPI tab, Competency tab, self-assessment/development plan, promotion feedback, performance summary, acknowledgement, HR review 1/2, HR action. |
| `Personnel/PMFormSummary.aspx` + `.vb` | `/PmFormSummary` | HR tracking grid, one row per HDR per employee/year, filters: department, employee, year, status. |
| `Personnel/EmpMasterEnteryMgmt.aspx` + `.vb` | `/Employees` | Employee Master. The legacy page is a full HR/payroll master (documents, allowances, bank, leave). Only the PM-relevant subset is recreated (see §5). |
| `Personnel/EmpREFMaster.aspx` + `.vb` | `/ReferenceMaster` | Reference Master: KPI Master CRUD, Competency Master CRUD, KPI reference codes (job families, rating scales). Legacy leave/loan/deduction tabs are out of PM scope. |
| `Adm02.master` | `Pages/Shared/_Layout.cshtml` | Shell/navigation only. |
| `App_Code/aiccom.vb`, `App_Code/aicdbo.vb` | **Not ported.** | Helper semantics re-implemented as injectable services (see §4). No runtime dependency on AICAPPS/DevExpress/Informix. |

## 2. Database mapping (Informix → PostgreSQL)

Legacy stores the whole form in one table `pm_form_records` with `record_type` in
(`HDR`,`KPI`,`COMP`). Per the handoff recommendation, the new schema is normalized while
preserving legacy reference-number compatibility.

| Legacy (Informix `beams:`) | New (PostgreSQL) | Notes |
|---|---|---|
| `pm_form_records` where `record_type='HDR'` | `pm_forms` | One row per employee/eval year. `ref_no` kept as `legacy_ref_no` (unique). `eval_year CHAR(6)` (`'2026  '`) → `eval_year integer` (trimmed on import). Adds `row_version` (optimistic concurrency, `xmin`). |
| `pm_form_records` where `record_type='KPI'` | `pm_form_kpis` | Item snapshot columns preserved: perspective, kpi_code, kpi_name, kpi_definition, formula_metric_kpi, target, item_weight, achievement_score, weighted_calculation, comments, record_seq, legacy ref_no. |
| `pm_form_records` where `record_type='COMP'` | `pm_form_competencies` | comp_type, comp_code, comp_name; legacy stored the competency description in `kpi_definition` — mapped to `description`. |
| *(none — only `previous_status` + one date)* | `pm_form_status_history` | New per handoff: every transition appends (from, to, actor, at, note). |
| `kpi_master` | `kpi_masters` | All 24 columns kept (EN + AR fields, dept CSV list or `*`, min/max weight, status A/I, audit). |
| `competency_master` | `competency_masters` | All columns kept. |
| `reference` (`rf_codetype='ADM'`, `rf_moduleno='KPI'`) | `job_families` (subtype `J`), `rating_scales` (subtype `R`) | `J`: rf_codeno JF001–JF006, EN/AR descriptions, `rf_lastsrl` = comma list of grades, `rf_frac` = KPI weight %, `rf_toac` = COMP weight %. `R`: code 1–6, EN/AR descriptions, `rf_frac`..`rf_toac` = score range (0–0 = "Pending"). |
| `reference` (`rf_codetype='DPT'`) | `departments` | **Not exported.** Seeded from the hardcoded `DeptName()` map in `aiccom.vb` (AC, INV, ADM, CRC, BDM, PRO, LIF, MT, MAR, FGA, RIN, EDP, IA, LGL, RMD, COM, AAD, TPM, DRO). |
| `reference` (`rf_codetype='DSG'`, `'SEC'`) | `designations`, `sections` | **Not exported.** Codes derived from HDR data; descriptions default to the code and are editable in Reference Master. Documented data gap. |
| `empmaster` | `employees` | **Not exported** (see data-migration-plan §2). PM-relevant subset: emp_code, latin_name, arabic_name, designation_code, dept_code, section_code, grade, join_date, term_date. |
| `ta_users` | `app_users` | App-owned auth: username, password hash, employee link, email. Legacy assumption "HR dept member = HR admin" is **not** ported. |
| `ta_auth_mast` | `manager_assignments` | Direct-manager resolution is data-driven. Primary source: the HR-provided map hardcoded in `KPIForm.aspx.vb` (`s_DirectManagerMap`, 178 entries). The `ta_auth_mast` fallback hierarchy is *not* recreated; unmapped employees simply have no manager until HR assigns one. |
| *(hardcoded lists in code)* | `employee_exceptions` | Data-driven per handoff: perspective-rule exemptions (1058, 1470), 50/50 job-family exceptions (1553, 1376, 1454, 1470, 1303, 1450, 1550, 1523, 1058, 1579), temporary self-manager (656, 1031), branch viewer (1541 → PRO/BR view-only). Each row has rule code, reason, effective dates. |
| *(none)* | `email_logs` | Per handoff: centralized templates + delivery logging/idempotency key. Dev default is log-only (no SMTP). |

### `pm_form_records` HDR column map

| Legacy column | New (`pm_forms`) | Legacy column | New (`pm_forms`) |
|---|---|---|---|
| ref_no | legacy_ref_no | emp_ack_by | emp_ack_by |
| empcd | emp_code | emp_ack_date | emp_ack_date |
| empname | emp_name_snapshot | emp_ack_sign | emp_ack_sign |
| eval_year (CHAR 6) | eval_year (int) | emp_ack_comments | emp_ack_comments |
| em_design | designation_snapshot | hr_app_by / dt / sign | hr1_reviewer_name / hr1_review_date / hr1_sign |
| deptcd | dept_code | hr_remarks | hr1_remarks |
| dept_sec | section_code | hr_app_by_2 / dt_2 / sign_2 | hr2_reviewer_name / hr2_review_date / hr2_sign |
| app_by | manager_emp_code (appraiser) | hr_remarks_2 | hr2_remarks |
| em_grade | grade_snapshot | previous_status | previous_status |
| em_join_dt | join_date_snapshot | status_change_date | status_change_date |
| job_family | job_family | form_locked (Y/N) | is_locked (bool) |
| kpi_weight_tot / comp_weight_tot | kpi_weight_total / comp_weight_total | is_active (CHAR 6 'Y     ') | is_active (bool) |
| kpi_score / comp_score / performance_score | same names (numeric(5,2)) | cre_by / cre_dt | created_by / created_at |
| overall_rating_code | overall_rating_code | upd_by / upd_date | updated_by / updated_at |
| status | status | promotion_recommendation | promotion_recommendation (YES/BORDERLINE/NO) |
| self_assm_text / dev_plan_text | self_assessment / development_plan | promotion_comments | promotion_comments |
| mgr_sign / empsign | manager_sign / employee_sign | last_reminded_date | last_reminded_date |

## 3. Reference-number compatibility

Canonical format (per handoff, enforced for all **new** records):

```
PM + trimmed eval year + employee code padded to 4 digits + record type + 2-digit sequence
employee 907  HDR   -> PM20260907HDR01
employee 1504 KPI 2 -> PM20261504KPI02
```

Historical rows include unpadded 3-digit codes (`PM2026907HDR01`). Rules preserved:

- Lookups are always by `(emp_code, eval_year, record_type)`, **never** by parsing ref_no.
- On save of an existing form the stored HDR ref_no is reused verbatim (legacy `GenerateFormRefNo`
  H-11 fix), so old-format numbers survive updates.
- New forms always generate padded numbers.
- Import preserves original ref_no in `legacy_ref_no` without normalisation (a normalisation would
  need a collision audit and one transaction per HDR/KPI/COMP set — deferred, see migration plan).

## 4. Helper/service mapping

| Legacy helper (aiccom/aicdbo) | New service |
|---|---|
| `emp_id(usr)` / `UserEmpId` | `ICurrentUserService.EmployeeCode` (from claims) |
| `UserName(usr)` | `ICurrentUserService.DisplayName` |
| `empname(empcd)` | `IEmployeeDirectory.GetName` |
| `UserEmailId(empcd)` (+ dept fallback) | `IEmployeeDirectory.GetEmail` (no dept-mail fallback; empty ⇒ mail skipped + logged) |
| `DeptName`, `EmpDesigDesc`, `EmpSecDesc` | lookups on `departments`/`designations`/`sections` tables |
| `InitDepartment()` | `departments` query |
| `IsHRPrivilegedUser` (adm22, adm12, adm4, adm2, adm16, adm10) | `HR_ADMIN` role rows in `user_roles` (seeded with exactly those six accounts) |
| `GetDirectManager` (HR map → ta_auth_mast fallback) | `IManagerResolver` reading `manager_assignments` |
| `GetRatingCode` + `reference` R rows | `IRatingService` reading `rating_scales` |
| `Arabic_win` (1256↔1252 mojibake repair) | Not needed: PostgreSQL/UTF-8 end-to-end; importer decodes once at load time |
| `LanguageManager` (en/ar, RTL) | Culture cookie + `ILanguageService`; AR fields fall back to EN when blank (same rule as legacy) |
| `aicdbo.SelTable/UpdTable/...` (raw ODBC, string-concatenated SQL) | EF Core 8 + parameterized queries; every state transition in one DB transaction |

## 5. Employee Master scope decision

The legacy `EmpMasterEnteryMgmt` page manages the corporate HR master (passports, GOSI, bank,
allowances, documents…). None of that data was exported and none of it is used by the PM Form.
The standalone Employee Master therefore manages exactly the fields the PM system reads:

> employee code, Latin name, Arabic name, department, section, designation, grade, join date,
> termination date (active flag), direct manager assignment, linked login account.

This is a deliberate, documented reduction — the *visible information structure* the PM flow
depends on (code, name, dept/section, designation, grade, join date, manager) is identical.

## 6. Known legacy defects deliberately not replicated

| Legacy behaviour | New behaviour | Rationale |
|---|---|---|
| “Save as Draft” while status is `EMPLOYEE_ACKNOWLEDGE` rewrites status to `DRAFT`, which then hides the Submit-to-HR button | Saving content in `EMPLOYEE_ACKNOWLEDGE` keeps the status; only content + audit fields update | Handoff requires deliberate transitions; the legacy regression makes submission impossible after saving achievements. Recorded in workflow doc. |
| Status decisions read from display label text in some paths | All transitions re-read the HDR row inside the transaction and use optimistic concurrency | Handoff “status is machine state”. |
| `IsAchievementEntryOpen` uses the **current** calendar year (`DateTime.Today.Year`) | Gate is 1 December of the **evaluation year** (viewing a past year’s form after that date stays open) | Business rule stated as “1 December of the evaluation year”; legacy behaves identically whenever eval year = current year, which is the only case the legacy UI allows to be edited. |
| Duplicate mail / empty recipient sends | Single post-commit dispatch, deduplicated recipients, empty ⇒ skipped and logged | Handoff mail design. |
| HDR `record_seq` sometimes NULL | Always 1 for HDR | Compatibility not required; import tolerates NULL. |
| KPI/COMP rows deleted+reinserted on every save with fresh ref_nos | Same visible result (rows replaced); implemented as replace-set inside one transaction, ref_nos regenerated padded | Matches legacy data shape. |

## 7. Status vocabulary (verified)

From production code constants and the export (185 HDR rows):

| Code | In export? | Meaning |
|---|---|---|
| `DRAFT` | 23 | Manager building the form |
| `PENDING_EMPLOYEE_ACK` | 2 | Sent to employee |
| `EMPLOYEE_ACKNOWLEDGE` | 160 | Employee acknowledged (note: legacy constant value, not “…ACKNOWLEDGED”) |
| `SUBMITTED_TO_HR` | 0 | Manager submitted year-end scores |
| `HR_REVIEW_1_APPROVED` | 0 | First HR reviewer approved |
| `APPROVED` | 0 | Final HR approval, form locked |
| `READY` | 0 | Legacy vestige; accepted on read, never written by the new app |
| `SEND_TO_EMPLOYEE` | never stored | Legacy *validation profile* name only, not a status |

HR-stage behaviour (SUBMITTED_TO_HR onwards) is derived **from legacy code**, not from data,
because the export contains no rows in those states. Assumptions are listed in
`workflow-state-machine.md` §5.
