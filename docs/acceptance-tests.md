# Acceptance Tests

Automated tests live in `tests/Aic.Pm.Tests` (xUnit). Sections marked **[auto]** are covered by
automated tests; **[manual]** are browser walkthroughs for release sign-off. Personas:

| Persona | Seed account | Role |
|---|---|---|
| HR admin | `adm12` (also adm22, adm4, adm2, adm16, adm10) | `HR_ADMIN` |
| Non-admin HR employee | any ADM-department employee account | none |
| Direct manager | `0854` (manages 1353, 1495, 1504) | manager via assignment |
| Employee | `1504` | own form only |
| Exception employees | `1058`, `1470` | perspective-rule exempt |

## A. Workflow state machine [auto]

1. New form → Save Draft ⇒ status `DRAFT`, history row appended, audit fields set.
2. Send to Employee from DRAFT ⇒ `PENDING_EMPLOYEE_ACK`, `previous_status=DRAFT`,
   `status_change_date=today`, form locked, exactly one email logged to the employee.
3. Duplicate Send (stale second browser) ⇒ rejected with “already sent”, no second email.
4. Acknowledge by the employee ⇒ `EMPLOYEE_ACKNOWLEDGE`, emp_ack fields set, unlock, manager mail.
5. Acknowledge by anyone other than the employee ⇒ rejected.
6. Acknowledge when DB status is no longer `PENDING_EMPLOYEE_ACK` (stale page) ⇒ rejected with
   current-status message (regression test for the legacy stale-browser bug).
7. Submit to HR before 1 Dec of eval year ⇒ rejected; on/after 1 Dec with all achievements ⇒
   `SUBMITTED_TO_HR`.
8. Submit with any achievement = 0 ⇒ rejected listing the missing KPI/COMP names.
9. HR1 approve ⇒ `HR_REVIEW_1_APPROVED` with hr1 fields; HR1 action fired twice (double-click)
   ⇒ second attempt is a no-op.
10. HR2 final approve by the same person as HR1 ⇒ rejected (segregation of duties); by a
    different HR admin ⇒ `APPROVED`, locked.
11. HR revert (from SUBMITTED_TO_HR and from HR_REVIEW_1_APPROVED) ⇒ `EMPLOYEE_ACKNOWLEDGE`,
    unlocked.
12. Cancel & Delete on DRAFT ⇒ form and items removed, history retained; on any other status ⇒
    rejected.
13. Save content while `EMPLOYEE_ACKNOWLEDGE` ⇒ status unchanged (documented deviation).
14. Every transition writes exactly one status-history row; `previous_status` matches.

## B. Permissions & visibility [auto + manual]

1. [auto] `IsHrAdmin` true for exactly adm22, adm12, adm4, adm2, adm16, adm10 (case-insensitive);
   false for any other user including other ADM-department accounts.
2. [auto] Manager-of checks: 854→1504 true; 854→907 false; anyone→self “act as manager” false.
3. [auto] Self-manager exception (656, 1031): may act as manager on own form.
4. [auto] Branch viewer 1541: view-only on PRO/BR employees, no edit anywhere.
5. [manual] Employee login: own form auto-loads; department & employee selectors disabled;
   achievement/weighted/action columns and summary cards hidden; rating visible only when
   APPROVED.
6. [manual] Direct manager: employee selector remains enabled after loading a form (browse staff
   back-to-back without refresh); department selector disabled; Save Draft / Send to Employee /
   Delete visible on staff DRAFT forms; none of these on the manager’s own form.
7. [manual] HR admin: both selectors enabled, all forms viewable with score cards and validation
   remarks, HR action panel only in SUBMITTED_TO_HR / HR_REVIEW_1_APPROVED; no content editing.
8. [manual] Non-admin HR employee behaves exactly like a regular employee.
9. [manual] PM Form Summary: reachable by HR admins only; others get access denied.

## C. Validation & scoring [auto]

1. KPI count outside 4–8 ⇒ invalid; weights ≠ 100 ⇒ invalid; duplicate KPI ⇒ rejected;
   weight outside master min/max ⇒ rejected with range message.
2. Perspectives: 2 distinct ⇒ invalid; 3 ⇒ valid; employees 1058 and 1470 valid with < 3
   (exception is data-driven — removing the exception row reactivates the rule).
3. COMP count outside 3–5 ⇒ invalid; weights ≠ 100 ⇒ invalid; duplicate ⇒ rejected.
4. Weighted item = round-half-away-from-zero(weight × achievement / 100): (20, 97) ⇒ 19;
   (15, 50) ⇒ 8.
5. Scores: kpi_weight_tot 60/40 grade-7 employee with Σweighted 90 KPI / 100 COMP ⇒
   KPI 54.00, COMP 40.00, overall 94.00, rating “Exceed Expectations”.
6. Rating bands: 0→Pending, 1..59→Unsatisfactory, 59/60 boundary, 79/80, 89/90, 94/95,
   100→Exceptional.
7. Card colour rule: 0 ⇒ grey, 1–99 ⇒ red, 100 ⇒ green (asserted via the CSS-class helper).
8. Job-family resolution by grade from `rf_lastsrl` lists (grade 9 ⇒ 80/20; grade 5 ⇒ 0/100;
   grade in no family ⇒ error). 50/50 exception list overrides.
9. KPI tab hidden iff grade < 6 and KPI weight total = 0.
10. Achievement gate: before eval-year Dec 1 submitted achievement values are discarded (stored
    0), on/after they persist.

## D. Reference numbers & import [auto]

1. Generator: emp 907/2026 HDR ⇒ `PM20260907HDR01`; emp 1504 KPI seq 2 ⇒ `PM20261504KPI02`.
2. Existing HDR keeps its stored ref_no on update, including legacy unpadded `PM2026907HDR01`.
3. Lookup is by (emp, year, type) — a form saved under an unpadded ref is found and updated, never
   duplicated.
4. Import reconciliation: 185 forms / 248 KPI / 888 COMP / 134 KPI masters; statuses 160 EMPLOYEE_ACKNOWLEDGE +
   23 DRAFT + 2 PENDING_EMPLOYEE_ACK; `eval_year` trimmed to 2026; CHAR padding trimmed;
   `'Y     '` → true; sample form `PM20261022HDR01` has 5 COMP and 0 KPI rows and job family
   “Specialists & Professionals” with 0/100 weights.
5. Importer is idempotent: second run changes no row counts.

## E. Email dispatch [auto]

1. Each transition produces exactly one message spec; recipients deduplicated (To beats CC).
2. Empty recipient list ⇒ no send, one `email_logs` row with status `SKIPPED_NO_RECIPIENT`.
3. Dev mode writes the rendered body to `email_logs` only; headings use explicit dark colour
   (light-mode Outlook rule) — asserted on the template output.

## F. HR PM Form Summary [auto + manual]

1. [auto] Query returns one row per HDR per employee/year (never KPI/COMP rows), handles padded
   legacy eval_year, exposes current status + status_change_date.
2. [manual] Filters: department / employee / year / status combine correctly; “View Form” opens
   the PM Form for that employee.

## G. Non-functional

1. [auto] All data access parameterized (no string-concatenated SQL anywhere in the solution).
2. [auto] Concurrency: two contexts race a transition; loser gets a concurrency failure, DB holds
   exactly one transition and one history row.
3. [manual] Arabic text renders correctly on the PM Form and masters (spot: KPI001 Arabic name),
   and the language toggle switches labels/RTL direction.
