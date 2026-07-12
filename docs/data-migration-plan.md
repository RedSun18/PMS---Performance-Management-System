# Data Migration Plan (Informix → PostgreSQL)

## 1. Inputs actually available

| File under `References/Database/` | Declared content | **Actual content (verified)** |
|---|---|---|
| `pm_form_records-informix-inserts.sql` / `-csv` | pm_form_records | pm_form_records **subset** (~200 rows) + full `CREATE TABLE` DDL |
| `empmaster-informix-inserts.sql` / `-csv` | empmaster | **Mislabeled — contains the full `pm_form_records` export**: 1,321 rows (185 HDR / 248 KPI / 888 COMP), 185 distinct employees. No empmaster data exists in this snapshot. |
| `kpi_master-informix-inserts.sql` / `-csv` | kpi_master | kpi_master, 134 rows, EN+AR |
| `competency_master-informix-inserts.sql` / `-csv` | competency_master | competency_master, 77 rows, EN+AR |
| `reference-informix-inserts.sql` / `-csv` | reference | Only `ADM/KPI` rows: 6 job families (subtype `J`) + 6 rating ranges (subtype `R`) |
| `informix-core-schema.sql` | full DDL | **Missing.** DDL recovered from the `CREATE TABLE` headers inside the insert exports. |

HDR status distribution in the export: `EMPLOYEE_ACKNOWLEDGE` 160, `DRAFT` 23,
`PENDING_EMPLOYEE_ACK` 2 — matching the handoff. No HR-stage rows exist.

## 2. Gaps and how each is handled

| Gap | Handling | Follow-up before production cutover |
|---|---|---|
| No `empmaster` export | `employees` synthesized from the 185 HDR snapshots (code, name, designation, dept, section, grade, join date) plus every manager code referenced by the HR manager map. Synthesized rows are flagged `source = 'HDR_SNAPSHOT'`. | Export real `empmaster` (active + terminated), re-run importer with `--employees` to overwrite snapshot-sourced rows. |
| No `ta_users` export | `app_users` seeded: the six HR admin accounts (adm22, adm12, adm4, adm2, adm16, adm10) and one account per employee (username = 4-digit padded employee code). All seeded accounts get a forced-change development password; **no production credentials are imported** (policy). | Decide the production identity source (AD/SSO or migrated ta_users) and load real emails. |
| No email addresses | `employees.email` left empty ⇒ mail dispatch skips and logs. | Load from ta_users export. |
| No `reference` DPT/DSG/SEC rows | Departments seeded from the legacy `DeptName()` hardcoded map (19 codes incl. COM/Compliance). Designation/section codes harvested from HDR data with description = code; editable in Reference Master. | Export the three reference code types and re-import descriptions. |
| No direct-manager table | `manager_assignments` seeded from the HR-provided map embedded in `KPIForm.aspx.vb` (178 employee→manager pairs, first-named manager). | HR to confirm the list is current at cutover. |
| Informix `TEXT`/`CHAR` semantics | `TEXT` → `text`; `CHAR(n)` values trimmed on import; `eval_year` `'2026  '` → `2026::int`; `is_active CHAR(6) 'Y     '` → boolean; empty strings in date columns → NULL (Informix −617 class of issues cannot recur). | — |
| Arabic encoding | Exports are UTF-8 and verified readable (spot-checked kpi_master/competency_master AR columns). Stored as UTF-8; the legacy `Arabic_win` cp1256 repair is not needed at runtime. | Re-verify after any fresh export. |

## 3. Importer

`src/Aic.Pm.Importer` (console, idempotent, upsert by natural key):

```
dotnet run --project src/Aic.Pm.Importer -- --data "References/Database" [--wipe]
```

Order and keys:

1. `departments`, `designations`, `sections` (seed + harvest) — key: code
2. `job_families`, `rating_scales` from `reference-informix-csv` — key: rf_codeno / code
3. `kpi_masters` from `kpi_master-informix-csv` — key: kpi_id
4. `competency_masters` from `competency_master-informix-csv` — key: comp_id
5. `employees` from HDR rows of `empmaster-informix-csv` (the full pm_form_records file) — key: emp_code
6. `manager_assignments` from the embedded HR map — key: emp_code
7. `employee_exceptions` seed (perspective 1058/1470; 50/50 list; self-manager 656/1031; branch viewer 1541)
8. `app_users` + `user_roles` seed (six HR admins; per-employee dev accounts)
9. `pm_forms` / `pm_form_kpis` / `pm_form_competencies` from the full pm_form_records CSV —
   key: `(emp_code, eval_year, record_type, record_seq)`; original ref_no kept in `legacy_ref_no`.
   One transaction **per form** (HDR + its items), matching the handoff’s all-or-nothing rule.
10. `pm_form_status_history`: one baseline row per imported form
    (`previous_status → status` at `status_change_date`, actor = legacy `upd_by`, note = "imported").

Parsing rules: dates are `dd/MM/yyyy`; `record_seq` NULL on HDR → 1; numeric NULL → 0 for weights
/scores where legacy treated NULL as 0; strings trimmed; CSV read with proper quoting
(fields contain commas, quotes and newlines).

## 4. Reference-number policy

- Imported rows keep their exact legacy `ref_no` (padded or unpadded) — **no normalisation pass**.
- New records always generate padded numbers (`PM` + year + 4-digit code + type + 2-digit seq).
- If a normalisation is ever required, precondition: collision audit
  (`SELECT padded_ref, count(*) …`), then rewrite each HDR/KPI/COMP set in a single transaction.
  Out of scope for this phase; operational queries never depend on ref_no format.

## 5. Verification / reconciliation (run after every import)

The importer prints and `docs/acceptance-tests.md` §D asserts:

- Row counts: 185 forms, 248 KPI items, 888 COMP items, 134 KPI masters,
  77 competency masters, 6 job families, 6 rating scales.
- Status counts equal the source (160/23/2).
- Σ item weights per form equals the legacy stored totals where the legacy form was complete;
  discrepancies are listed, not “fixed”.
- Every form’s employee exists; every manager assignment resolves to an employee.
- Spot checks: `PM20261022HDR01` (EMPLOYEE_ACKNOWLEDGE, comp-only, 5 COMP rows),
  employee 1058/1470 exception rows present, unpadded legacy refs (e.g. `PM2026907…`) preserved.

## 6. Security constraints honoured

- No production secrets, connection strings, SMTP credentials or ODBC DSNs are imported or stored.
- Dev credentials are clearly marked, forced-change, and documented in the README.
- The `References/` snapshot stays out of version control (`.gitignore`), per the export
  checklist’s “outside source control” rule; the importer reads it from the working tree path.
