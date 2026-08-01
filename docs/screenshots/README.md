# Screenshots — landscape recapture (v1.0.0 RC2)

All screenshots in this folder were captured fresh against a live instance of the application to
replace distorted (stretched) screenshots in `Performance_Management_System.pptx`.

**Capture settings** (identical for every shot): 1600×900 browser viewport, 100% zoom, PNG,
`deviceScaleFactor` 1 (no upscaling), admin account, English UI unless noted.

## `raw/`

The untouched, full-viewport captures — one per page/state, exactly as the browser rendered
it. These are the ones to reuse for anything new: docs, future decks, README images, etc.

| File | Page |
|---|---|
| `dashboard.png` | Dashboard (English) |
| `arabic_dashboard.png` | Dashboard (Arabic, RTL) |
| `employee_master.png` | Employee Master — list |
| `kpi_master.png` / `kpi_master_grid.png` | KPI Master — add form / search+grid (scrolled) |
| `competency_master.png` / `competency_master_grid.png` | Competency Master — add form / search+grid (scrolled) |
| `job_families_rating_scales.png` | Reference Master — Job Families & Rating Scales tab |
| `pmform_top.png` | PM Form — employee selection, info, status |
| `pmform_ack.png` | PM Form — scored down to the Acknowledgement/Signatures section |
| `workflow_admin_details_top.png` | Workflow Administration Details — employee info, status, progress tracker |
| `workflow_admin_details_actions.png` | Workflow Administration Details — timeline tail, audit history, action buttons |
| `scheduled_jobs.png` | Scheduled Jobs |
| `login_as.png` | Administrator — Login As |

## `pptx_crops/`

The exact images embedded in the PPTX, derived from `raw/` by simple top-edge cropping only
(never resizing/stretching) to match each slide's picture-frame aspect ratio. Kept here for
traceability — if the deck's layout changes again, regenerate from `raw/` rather than editing
these directly.

## Notes

- Employee `1557` (Aryan Ramesh Shekar Bhandary) is a real seeded record in the local dev
  database, mid-workflow (`Pending Employee Acknowledgment`) — used for the PM Form and
  Workflow Administration Details captures so the screenshots show genuine, non-fabricated data.
- Mermaid-rendered diagrams (architecture, workflow state machine, sequence diagrams, deployment)
  are a separate asset class under `docs/diagrams/rendered/` and are not part of this recapture —
  they were evaluated and are not visibly distorted.
