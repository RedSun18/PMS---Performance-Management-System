# Workflow State Machine

The annual PM review cycle is a linear, seven-status state machine (`PmFormStatus`). Five of the
seven statuses are reachable through the normal Manager → Employee → Manager → HR → HR flow;
`Ready` is a legacy status accepted on read but never written by the current app.

## Normal lifecycle

```mermaid
stateDiagram-v2
    [*] --> Draft: Manager creates KPIs/competencies
    Draft --> PendingEmployeeAck: Manager sends to employee
    PendingEmployeeAck --> EmployeeAcknowledged: Employee acknowledges
    EmployeeAcknowledged --> SubmittedToHr: Manager enters achievement scores<br/>and submits (after 1 Dec gate)
    SubmittedToHr --> HrReview1Approved: HR Reviewer 1 approves
    HrReview1Approved --> Approved: HR Reviewer 2 approves<br/>(different reviewer — segregation of duties)
    Approved --> [*]

    EmployeeAcknowledged --> PendingEmployeeAck: HR Revert<br/>(HrRevertAsync, from SubmittedToHr/HrReview1Approved)
```

## Workflow Administration overrides

Six recovery actions, HR-Admin-only, each requiring a mandatory typed reason, each producing an
audit-log entry and a `PmFormStatusHistory` row:

```mermaid
flowchart TD
    Any(["Any status<br/>except Draft/Ready"]) -->|Return to Employee| PEA["PendingEmployeeAck<br/>ack fields cleared, form re-locked"]
    SubHr["SubmittedToHr /<br/>HrReview1Approved"] -->|"Return to Manager<br/>reuses HrRevertAsync"| EA["EmployeeAcknowledged"]
    Approved(["Approved"]) -->|Reopen Review| EA2["EmployeeAcknowledged<br/>HR1/HR2 sign-offs cleared"]
    AnyNotApproved(["Any status<br/>except Approved"]) -->|"Administrative Completion<br/>still runs full validation"| Approved2["Approved"]
    AnyStatus(["Any status"]) -->|Resend Notification<br/>no state change| AnyStatus
    Locked(["IsLocked = true"]) -->|Unlock Review<br/>no state change| Unlocked(["IsLocked = false"])
```

## State → stage/owner mapping (Workflow Administration UI)

| `PmFormStatus` | Displayed stage | Current owner | Tracker ordinal |
|---|---|---|---|
| `Draft` / `Ready` | KPI Creation | Manager | 0 |
| `PendingEmployeeAck` | Employee Acknowledgement | Employee | 1 |
| `EmployeeAcknowledged` | Manager Review | Manager | 2 |
| `SubmittedToHr` | HR Review — First | HR | 3 |
| `HrReview1Approved` | HR Review — Final | HR | 3 |
| `Approved` | Completed | — | 4 |

## Notifications by transition

```mermaid
flowchart LR
    A[Send to Employee] -->|AcknowledgementRequest| Emp1[Employee]
    B[Employee Acknowledges] -->|EmployeeAcknowledged| Mgr1[Manager]
    C[Submit to HR] -->|SubmittedToHr| HrTeam[All HR Admins]
    D[HR Reviewer 1 Approves] -->|Hr1Approved| HrTeam2[Remaining HR Admins]
    E[HR Reviewer 2 Approves] -->|FinalApproved| Emp2[Employee + Manager]
    F[HR Revert] -->|Reverted| Mgr2[Manager]
    G[Daily Reminder job] -->|Reminder| Owner[Current stage owner]
    H[Weekly Escalation job] -->|EscalationDigest, one email| HrTeam3[HR team — single digest]
```
