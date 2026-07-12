# PM Form Workflow State Machine

Statuses are **database-authoritative machine state**. The UI never uses a display label as
workflow input. Every transition:

1. opens one transaction,
2. re-reads the current HDR (`pm_forms`) row `FOR UPDATE` and validates the expected source status
   (stale-page protection; optimistic `row_version` check on top),
3. writes `status`, `previous_status`, `status_change_date`, `updated_by`, `updated_at` and the
   transition-specific actor/signature fields,
4. appends one `pm_form_status_history` row,
5. commits, and only then dispatches **exactly one** email with deduplicated, non-empty recipients
   (empty recipient list ⇒ skip + `email_logs` entry, never an SMTP call).

## 1. States

```
DRAFT ──SendToEmployee──▶ PENDING_EMPLOYEE_ACK ──Acknowledge──▶ EMPLOYEE_ACKNOWLEDGE
  ▲                                                                    │        ▲
  │ (new form / Cancel&Delete removes form)                            │        │
  │                                             SubmitToHR (≥ 1 Dec)   │        │ HR Revert
  │                                                                    ▼        │
  └─────────(no transition back to DRAFT after ack)          SUBMITTED_TO_HR ───┤
                                                                       │        │
                                                          HR1 Approve  ▼        │
                                                             HR_REVIEW_1_APPROVED
                                                                       │
                                                          HR2 Final    ▼
                                                                   APPROVED  (terminal)
```

`READY` is accepted when reading legacy data (treated like `DRAFT` for delete/edit gating) but is
never written.

## 2. Transition table

| # | Action | From → To | Who may perform | Guards (server-side, inside transaction) | Fields written | Email (post-commit) |
|---|---|---|---|---|---|---|
| T1 | Save Draft | *(none)*/`DRAFT` → `DRAFT` | Direct manager of the employee, **not** on own form, not branch-viewer | Form not locked. No content validation (drafts may be incomplete). | header + full KPI/COMP replace-set, `is_locked=false`, audit | none |
| T1b | Save (content) | `EMPLOYEE_ACKNOWLEDGE` → `EMPLOYEE_ACKNOWLEDGE` | same as T1 | Achievement values accepted only if the Dec-1 gate is open; otherwise stored as 0 | content + audit; **status unchanged** (documented deviation, legacy-mapping §6) | none |
| T2 | Send to Employee | *(none)*/`DRAFT` → `PENDING_EMPLOYEE_ACK` | Direct manager, not own form | Not already `PENDING_EMPLOYEE_ACK` (duplicate-send guard). If job-family KPI weight > 0: ≥1 KPI and KPI weights total 100. ≥1 competency and COMP weights total 100. | header+items, `is_locked=true`, audit | To employee, CC manager: “ACTION REQUIRED: Review Your Performance Objectives” |
| T3 | Acknowledge | `PENDING_EMPLOYEE_ACK` → `EMPLOYEE_ACKNOWLEDGE` | The employee whose form it is (only) | DB status must still be `PENDING_EMPLOYEE_ACK` (two-browser stale-page scenario returns a clear “form changed” error). | `emp_ack_by`, `emp_ack_date=today`, `emp_ack_sign=empcode`, `emp_ack_comments` (optional), `is_locked=false` | To manager, CC employee: “Objectives Acknowledged” |
| T4 | Submit to HR | `EMPLOYEE_ACKNOWLEDGE` → `SUBMITTED_TO_HR` | Direct manager, not own form | Date ≥ 1 Dec of eval year. Every KPI and COMP row has achievement > 0. Full validation: KPI count 4–8 & weight 100 (when required), COMP count 3–5 & weight 100, ≥3 distinct perspectives unless employee is exempt (1058/1470 seeds). Job family must be configured for the grade. | header+items, scores recalculated server-side, `is_locked=true` | To HR, CC manager: “PM Form Ready for HR Review” |
| T5 | HR Review 1 Approve | `SUBMITTED_TO_HR` → `HR_REVIEW_1_APPROVED` | HR administrator (role `HR_ADMIN`; seeded adm22, adm12, adm4, adm2, adm16, adm10), not own form | Reviewer-1 name required. Action must match current DB status (double-click guard). | `hr1_reviewer_name`, `hr1_review_date`, `hr1_sign`, `hr1_remarks` | To HR rep 2: “Ready for Final HR Review” |
| T6 | HR Final Approve | `HR_REVIEW_1_APPROVED` → `APPROVED` | HR administrator **different from HR reviewer 1** (segregation of duties, compared by employee code) | Reviewer-2 name required; same status/double-click guard. | `hr2_*` fields, `is_locked=true`, `overall_rating_code` finalized | To employee, CC manager: “APPROVED (Final)” with score + rating |
| T7 | HR Revert | `SUBMITTED_TO_HR` or `HR_REVIEW_1_APPROVED` → `EMPLOYEE_ACKNOWLEDGE` | HR administrator | — | status fields only, `is_locked=false` | To manager, CC HR1+HR2: “PM Form Requires Revision” with HR comments |
| T8 | Cancel & Delete | `DRAFT`/`READY` → *(form removed)* | Direct manager, not own form | DB status re-read must be `DRAFT`/`READY` (legacy stale-state fix). Confirmation required. | HDR + all KPI/COMP rows deleted; history rows retained with note | none |

Anything not listed is rejected server-side with the current status echoed back.

## 3. Permission model (summary)

- **HR administrators** = exactly the accounts holding the `HR_ADMIN` role. Seeded: `adm22,
  adm12, adm4, adm2, adm16, adm10`. Only they see all departments/employees, the department
  selector, the PM Form Summary page, and HR workflow actions. Other HR-department employees are
  ordinary employees here.
- **Direct manager** = `manager_assignments.manager_emp_code` for the selected employee. Managers
  keep the employee selector enabled to browse their assigned staff without refresh; the
  department selector stays fixed for them.
- **Self-view rule**: `canActAsManager = isDirectManager AND NOT viewingOwnForm AND NOT
  branchViewer`. Nobody gets Save/Send/Delete/achievement entry on their own form, including
  managers and HR admins. (Data-driven exceptions `SELF_MANAGER` for 656 and 1031 replicate the
  temporary legacy arrangement.)
- **Employee visibility**: employees see their form read-only; achievement, weighted score and
  action columns and the summary cards are hidden; the overall rating shows only when status is
  `APPROVED`. HR admins viewing others see scores, summary cards and validation remarks.
- **Branch viewer** (exception `BRANCH_VIEWER`, seeded 1541 → dept PRO / section BR): may browse
  and view those forms, never edit.

## 4. Date gate

Achievement (%) entry, final scoring and Submit-to-HR are unavailable until **1 December of the
evaluation year** (`eval_year-12-01`). Enforced server-side on every save/submit (values arriving
early are discarded exactly as legacy `NormalizeAchievementScore` did), not just disabled in the
UI. Comments are explicitly never locked by this gate.

## 5. Assumptions documented (HR stages not present in exported data)

The export contains only `DRAFT`, `PENDING_EMPLOYEE_ACK`, `EMPLOYEE_ACKNOWLEDGE`. Behaviour for
T4–T7 is derived from production code (`btnSubmitToManager_Click`, `btnSubmitHRAction_Click`,
`SaveHRAction`, `SetHRSectionAccess`), specifically:

1. HR action options are status-dependent: at `SUBMITTED_TO_HR` → *Approve (Send to HR 2)* or
   *Revert to Manager*; at `HR_REVIEW_1_APPROVED` → *Final Approval* or *Revert to Manager*.
2. Revert targets `EMPLOYEE_ACKNOWLEDGE` (not DRAFT), even though the legacy email says “reverted
   to DRAFT”. The code value wins; the email template text was corrected.
3. Segregation of duties between HR1 and HR2 is enforced (legacy warned and disabled the HR2
   panel; new app also rejects server-side).
4. Auto-fill of HR reviewer name/sign/date from the logged-in user is preserved.
5. `form_locked = true` for `PENDING_EMPLOYEE_ACK`, `SUBMITTED_TO_HR`, `APPROVED`; false for
   `DRAFT`, `EMPLOYEE_ACKNOWLEDGE` (matches legacy `BuildHeaderUpdateQuery` + acknowledge path).

## 6. Scoring rules (server-side, recomputed on every save/submit)

- Item weighted score = `round(weight × achievement / 100, 0, away-from-zero)`.
- KPI score = `Σ weighted × (kpi_weight_total / 100)`; COMP score likewise; overall = sum, all
  rounded to 2 decimals for display.
- Rating = `rating_scales` row where `min ≤ round(overall) ≤ max`
  (1–59 Unsatisfactory, 60–79 Needs Improvement, 80–89 Meets Expectations,
  90–94 Exceed Expectations, 95–100 Exceptional, 0–0 Pending).
- Card colours (weights and score cards): **0 → grey, 1–99 → red, 100 → green**.
- Job-family weight split by grade (from `job_families`): 9→80/20 (Executive), 6/7/8→60/40 …
  grade lists come from `rf_lastsrl`; grade without a family ⇒ validation error “Job Family is not
  configured”. 50/50 exception employees (seeded list) always get 50/50.
- KPI tab hidden when grade < 6 **and** KPI weight total = 0 (legacy `SetupTabsByJobFamily`).

## 7. Concurrency

`pm_forms` uses PostgreSQL `xmin` as EF Core concurrency token. A transition that loses the race
returns “This form was changed by another user; the page has been refreshed” and re-displays
current DB state — the legacy stale-acknowledgement bug cannot recur.
