namespace PerformanceManagement.Web.Validation;

/// <summary>Small shared input checks used by more than one page (Users/Edit, Employees/Edit).</summary>
public static class InputValidation
{
    /// <summary>Enforced everywhere a password is set — self-service change, admin-created account, and admin reset — so none of the three can bypass it.</summary>
    public const int MinPasswordLength = 6;

    /// <summary>Minimal format check — not a deliverability guarantee, just catches obviously malformed input
    /// before it's saved and silently fails to receive mail later with no feedback to the person who typed it.</summary>
    public static bool IsValidEmail(string email)
    {
        try { return new System.Net.Mail.MailAddress(email).Address == email; }
        catch (FormatException) { return false; }
    }
}
