namespace MainProjectNumoPart.Authorization
{
    // The single source of truth for role names. Before this existed the literal "Admin" was
    // repeated across the seeder, an endpoint, a page attribute and three Razor views — a typo
    // in any of them fails open (IsInRole returns false, the control just disappears) rather
    // than loudly, which is the worst way for an authorization bug to behave.
    public static class Roles
    {
        // const rather than static readonly: these are used as [Authorize(Roles = ...)] attribute
        // arguments, which the compiler requires to be compile-time constants.
        public const string Viewer = "Viewer";
        public const string Staff = "Staff";
        public const string Admin = "Admin";

        // A comma-separated list in [Authorize(Roles = ...)] means OR, not AND — this is
        // "Staff or Admin", not "both". Concatenating consts is itself a const, so it remains
        // legal as an attribute argument.
        public const string StaffOrAdmin = Staff + "," + Admin;

        // Drives role seeding and the role picker in the admin UI. A role missing from here is
        // one that never gets created and can never be assigned, so keep it in step with the
        // constants above (RolesTests pins this).
        public static readonly string[] All = { Viewer, Staff, Admin };
    }
}
