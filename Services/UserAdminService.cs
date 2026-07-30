using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MainProjectNumoPart.Services
{
    public enum UserAdminStatus
    {
        Success,
        UserNotFound,
        UnknownRole,
        NoChange,
        CannotDemoteSelf,
        CannotRemoveLastAdmin,
        IdentityError
    }

    public record UserAdminResult(UserAdminStatus Status, string Message)
    {
        public bool IsError => Status is not (UserAdminStatus.Success or UserAdminStatus.NoChange);
    }

    // Role is null when the user has no role at all — accounts created before roles existed are
    // in this state, and so is an account whose role assignment failed partway through creation.
    public record UserSummary(string Id, string Email, string? Role);

    // All account-administration logic lives here rather than in the page model so the guards
    // below are unit-testable against a real UserManager (see UserAdminServiceTests). A guard
    // that only exists inside a Razor page handler is a guard nobody can test.
    public class UserAdminService
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<UserAdminService> _logger;

        public UserAdminService(UserManager<IdentityUser> userManager, ILogger<UserAdminService> logger)
        {
            _userManager = userManager;
            _logger = logger;
        }

        // N+1 by construction: one query for users, then one GetRolesAsync per user. At this
        // app's scale (a workshop, tens of accounts) that's a handful of trivial indexed reads,
        // and it handles the "somehow has two roles" case honestly instead of a join that would
        // need grouping to avoid duplicate rows. Deliberate — don't "optimise" it.
        public async Task<List<UserSummary>> ListUsersAsync()
        {
            var users = await _userManager.Users.OrderBy(u => u.Email).ToListAsync();

            var summaries = new List<UserSummary>(users.Count);
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                summaries.Add(new UserSummary(
                    user.Id,
                    user.Email ?? user.UserName ?? "(no email)",
                    roles.Count == 0 ? null : string.Join(", ", roles)));
            }

            return summaries;
        }

        public async Task<UserAdminResult> ChangeRoleAsync(string actingUserId, string targetUserId, string newRole)
        {
            // Every guard runs before any mutation — a half-applied role change (old role removed,
            // new one rejected) would leave the account with no access at all.
            if (!Roles.All.Contains(newRole))
            {
                return new UserAdminResult(UserAdminStatus.UnknownRole, "Unknown role.");
            }

            var user = await _userManager.FindByIdAsync(targetUserId);
            if (user is null)
            {
                return new UserAdminResult(UserAdminStatus.UserNotFound, "User not found.");
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Count == 1 && currentRoles[0] == newRole)
            {
                return new UserAdminResult(UserAdminStatus.NoChange, $"{user.Email} is already {newRole}.");
            }

            // Guard 1 — self-lockout. Checked before the last-admin guard because it produces the
            // more actionable message for the case that actually happens in practice.
            if (string.Equals(user.Id, actingUserId, StringComparison.Ordinal) && newRole != Roles.Admin)
            {
                return new UserAdminResult(UserAdminStatus.CannotDemoteSelf,
                    "You cannot remove your own Admin role. Ask another admin to do it.");
            }

            // Guard 2 — never leave the system with zero admins. Strictly redundant today: reaching
            // this page proves the actor is an Admin, so if the target is the only Admin the target
            // IS the actor and guard 1 already caught it. Kept because it is the invariant that
            // actually matters, and it becomes load-bearing the moment a delete-user path or any
            // non-interactive role change is added.
            if (currentRoles.Contains(Roles.Admin) && newRole != Roles.Admin)
            {
                var admins = await _userManager.GetUsersInRoleAsync(Roles.Admin);
                if (admins.Count <= 1)
                {
                    return new UserAdminResult(UserAdminStatus.CannotRemoveLastAdmin,
                        "This is the last Admin account. Promote another user to Admin first.");
                }
            }

            // Replace rather than add. "One role per user" is this app's convention, not something
            // Identity enforces — AddToRoleAsync is purely additive, so without the removal a
            // demoted admin would simply hold both roles and keep every admin power.
            if (currentRoles.Count > 0)
            {
                var removed = await _userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!removed.Succeeded) return Failure(removed);
            }

            var added = await _userManager.AddToRoleAsync(user, newRole);
            if (!added.Succeeded) return Failure(added);

            // Without this the target's existing auth cookie keeps its old role claims until the
            // validator's next re-check. See the SecurityStampValidatorOptions comment in Program.cs.
            await _userManager.UpdateSecurityStampAsync(user);

            _logger.LogInformation(
                "Role change: {TargetEmail} ({TargetId}) {OldRoles} -> {NewRole}, by {ActorId}",
                user.Email, user.Id,
                currentRoles.Count == 0 ? "(none)" : string.Join(",", currentRoles),
                newRole, actingUserId);

            return new UserAdminResult(UserAdminStatus.Success, $"{user.Email} is now {newRole}.");
        }

        public async Task<UserAdminResult> CreateUserAsync(string email, string password, string role)
        {
            if (!Roles.All.Contains(role))
            {
                return new UserAdminResult(UserAdminStatus.UnknownRole, "Unknown role.");
            }

            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            var created = await _userManager.CreateAsync(user, password);
            if (!created.Succeeded) return Failure(created);

            var added = await _userManager.AddToRoleAsync(user, role);
            if (!added.Succeeded)
            {
                // Deliberately NOT deleting the user here. The account exists and is usable; it
                // just has no role, which the users table renders explicitly and an admin can fix
                // in place with one dropdown. Rolling back would destroy a valid account over a
                // recoverable problem.
                _logger.LogWarning("Created {Email} but failed to assign role {Role}: {Errors}",
                    email, role, string.Join("; ", added.Errors.Select(e => e.Description)));

                return new UserAdminResult(UserAdminStatus.Success,
                    $"Account created for {email}, but the role could not be assigned. Set it in the table above.");
            }

            _logger.LogInformation("User created: {Email} ({UserId}) with role {Role}", email, user.Id, role);

            return new UserAdminResult(UserAdminStatus.Success, $"Account created for {email} as {role}.");
        }

        private static UserAdminResult Failure(IdentityResult result) =>
            new(UserAdminStatus.IdentityError, string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
