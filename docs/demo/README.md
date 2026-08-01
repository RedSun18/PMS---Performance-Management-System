# Demo Documentation — Apex Corporation

Everything in this folder is generated **exclusively from the public Demo environment**
("Apex Corporation," fictional data) — nothing here comes from the real Development database.
It exists alongside the original documentation set in `docs/` without replacing it; the
original guides continue to describe the real system in general terms and are unaffected by
this folder.

For how to run/reset the Demo environment itself (credentials, seeding, HTTPS setup), see
[`../DEMO.md`](../DEMO.md).

## What's here

| File | What it is |
|---|---|
| `Performance_Management_System.pptx` | The product deck, rebuilt with every screenshot recaptured from Demo. Slide order, layout, and wording are unchanged from the original — only branding, screenshots, and the document's embedded author metadata were updated. |
| `User_Guide.pdf` | End-user guide (Employees, Managers, HR Reviewers), all 8 screenshots recaptured from Demo. |
| `Administrator_Guide.pdf` | HR Administrator guide, all 13 screenshots recaptured from Demo. |
| `Technical_Architecture_Guide.pdf` | Copied through unchanged — its diagrams (architecture, solution structure, sequence flows, ER diagram, state machine, deployment) are generic and were already free of any real company name, employee name, or screenshot. |
| `Database_Guide.pdf` | Copied through unchanged, same reasoning (ER diagrams only, no real data). |
| `Workflow_Guide.pdf` | Copied through unchanged, same reasoning (state-machine/flow diagrams only). |
| `Deployment_Guide.pdf` | Copied through unchanged, same reasoning (one generic deployment diagram). |
| `sample_employee_report.pdf` | A real PDF export generated against the Demo database (Kofi Diallo, 2025 review) — the same export used as the PPTX/User Guide sample. |
| `screenshots/` | The full set of 22 individual Demo screenshots (1600×900, PNG) used to build the above — Dashboard, every master-data page, PM Form at several workflow stages, Reports, Settings, Login, Landing Page, Search, Localization (Arabic), Workflow Administration (search, details, timeline, audit), Scheduled Jobs, and Login As. |

## Why only two guides were rebuilt

Every guide was checked, both its embedded images and its text layer, for real company names,
real employee names, and real email addresses. `User_Guide.pdf` and `Administrator_Guide.pdf`
were the only two containing actual application screenshots (30 and 36 embedded images
respectively) — and those screenshots showed real employee names, a real manager's name, and a
real email address from the Development database. Both were fully rebuilt: every embedded
screenshot was surgically replaced with a same-aspect-ratio Demo capture (no stretching, no
cropped chrome), leaving the surrounding page text, layout, and pagination untouched.

The other four guides (`Technical_Architecture_Guide.pdf`, `Database_Guide.pdf`,
`Workflow_Guide.pdf`, `Deployment_Guide.pdf`) contain only generic architecture/sequence/ER/
workflow diagrams with no company branding, no screenshots, and no real names anywhere in their
text — confirmed by extracting and grepping every page's text and every embedded image before
deciding not to touch them. They're included here verbatim so the full six-guide set is
available in one place.

## Branding

Every regenerated asset shows **Apex Corporation** — logo, footer ("© 2026 Apex Corporation —
Demo Environment. All data shown is fictional."), sample emails (`@apexcorp.demo`), and sample
employees, none of which correspond to any real person or organization.
