# Authentication & Authorization Flow

Cookie-based authentication (`Microsoft.AspNetCore.Authentication.Cookies`), two roles
(`HR_ADMIN`, `VIEWER` — any authenticated user without either role is treated as a regular
employee/manager), and a global fallback policy requiring authentication on every page not
explicitly marked `[AllowAnonymous]`.

## Login sequence

```mermaid
sequenceDiagram
    actor User
    participant Login as Account/Login
    participant DB as PostgreSQL (AppUsers)
    participant Audit as AuditService

    User->>Login: POST username + password
    Login->>DB: find AppUser by UserName (case-insensitive)
    alt account locked out
        Login->>Audit: "Login Blocked: Account Locked"
        Login-->>User: generic error (no lockout-vs-bad-password distinction)
    else wrong password
        Login->>DB: increment FailedLoginAttempts<br/>(sets LockedOutUntil at threshold)
        Login->>Audit: "Login Failed"
        Login-->>User: generic error
    else success
        Login->>DB: reset FailedLoginAttempts, clear lockout
        Login->>Audit: "Login Succeeded"
        Login->>Login: build claims (UserName, DisplayName,<br/>EmpCode, roles, MustChangePassword)
        Login-->>User: Set-Cookie (auth ticket)<br/>redirect to ReturnUrl or /Dashboard<br/>(forced to /Account/ChangePassword first if required)
    end
```

## Per-request authorization

```mermaid
flowchart TD
    Request["Incoming request"] --> Authn["UseAuthentication<br/>(reads/validates the cookie)"]
    Authn --> Fallback{"[AllowAnonymous]<br/>on this page?"}
    Fallback -->|No, and not authenticated| LoginRedirect["302 → /Account/Login"]
    Fallback -->|Yes, or authenticated| RoleCheck{"Page/handler requires<br/>a specific role?"}
    RoleCheck -->|Role missing| AccessDenied["403 → /AccessDenied"]
    RoleCheck -->|OK| RecordCheck{"Per-record check<br/>needed? (PM Form)"}
    RecordCheck -->|"PermissionService says no<br/>(not self/manager/HR/branch-viewer)"| AccessDenied
    RecordCheck -->|OK| Handler["Page handler executes"]
```

*Role gating is declarative (`[Authorize(Roles = Roles.HrAdmin)]` or
`HrAdminOrViewer`) on Settings, Users, Jobs, Workflow Administration, Reference Master, Reports,
and Admin/LoginAs. PM Form's per-record authorization is additionally evaluated in code via
`PermissionService.GetFormPermissionsAsync`, since "can this user act on this specific
employee's form" depends on the manager-assignment table, not a static role.*

## "Login As" impersonation

```mermaid
sequenceDiagram
    actor Admin as HR Admin
    participant Page as Admin/LoginAs
    participant Settings as SettingsService
    participant DB as PostgreSQL

    Admin->>Page: enter verification password (separate from account password)
    Page->>Settings: decrypt stored LoginAsVerificationPasswordProtected
    Page->>Page: constant-time compare
    alt wrong
        Page->>DB: audit "Login As: Verification Failed"
        Page-->>Admin: error
    else correct
        Page-->>Admin: 5-minute verified session window, user picker shown
        Admin->>Page: choose target user
        Page->>DB: insert ImpersonationLog (StartedAt, SessionId, IP)
        Page->>Page: sign out admin, sign in AS target<br/>(claims built fresh from target's own roles —<br/>never inherits the admin's privileges)
        Page-->>Admin: redirected into the app as the target user,<br/>banner shows "Return to Administrator"
        Admin->>Page: click "Return to Administrator"
        Page->>DB: close ImpersonationLog row (EndedAt)
        Page->>Page: sign out target, sign in AS admin<br/>(claims re-derived from DB, never trusted from breadcrumb)
    end
```
