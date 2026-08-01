using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Authorization;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class UserAdminServiceTests
    {
        private const string Password = "Str0ng!Passw0rd";

        private static async Task<(UserAdminService Service, UserManager<IdentityUser> Users, IServiceScope Scope)>
            BuildAsync()
        {
            var provider = TestIdentityFactory.Build();
            var scope = provider.CreateScope();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in Roles.All)
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }

            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var service = new UserAdminService(users, NullLogger<UserAdminService>.Instance);

            return (service, users, scope);
        }

        private static async Task<IdentityUser> AddUserAsync(UserManager<IdentityUser> users, string email, string? role)
        {
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            await users.CreateAsync(user, Password);
            if (role is not null) await users.AddToRoleAsync(user, role);
            return user;
        }

        [Fact]
        public async Task CannotDemoteSelf()
        {
            var (service, users, scope) = await BuildAsync();
            using var _ = scope;
            var admin = await AddUserAsync(users, "admin@workshop.local", Roles.Admin);
            await AddUserAsync(users, "other@workshop.local", Roles.Admin); // so the last-admin guard can't be what fires

            var result = await service.ChangeRoleAsync(admin.Id, admin.Id, Roles.Viewer);

            Assert.Equal(UserAdminStatus.CannotDemoteSelf, result.Status);
            Assert.True(await users.IsInRoleAsync(admin, Roles.Admin));
        }

        [Fact]
        public async Task CannotDemoteLastAdmin()
        {
            var (service, users, scope) = await BuildAsync();
            using var _ = scope;
            var onlyAdmin = await AddUserAsync(users, "admin@workshop.local", Roles.Admin);
            // Acting id is a different user, so guard 1 (self-demotion) cannot be what rejects this.
            var actor = await AddUserAsync(users, "actor@workshop.local", Roles.Admin);
            await users.RemoveFromRoleAsync(actor, Roles.Admin);

            var result = await service.ChangeRoleAsync(actor.Id, onlyAdmin.Id, Roles.Viewer);

            Assert.Equal(UserAdminStatus.CannotRemoveLastAdmin, result.Status);
            Assert.True(await users.IsInRoleAsync(onlyAdmin, Roles.Admin));
        }

        [Fact]
        public async Task CanDemoteAdminWhenAnotherAdminExists()
        {
            var (service, users, scope) = await BuildAsync();
            using var _ = scope;
            var target = await AddUserAsync(users, "target@workshop.local", Roles.Admin);
            var actor = await AddUserAsync(users, "actor@workshop.local", Roles.Admin);

            var result = await service.ChangeRoleAsync(actor.Id, target.Id, Roles.Viewer);

            Assert.Equal(UserAdminStatus.Success, result.Status);
            Assert.False(await users.IsInRoleAsync(target, Roles.Admin));
            Assert.True(await users.IsInRoleAsync(target, Roles.Viewer));
        }

        // AddToRoleAsync is additive: without the explicit removal in ChangeRoleAsync a "demoted"
        // admin would simply hold both roles and keep every admin power.
        [Fact]
        public async Task ChangingRoleReplacesPreviousRoleRatherThanAdding()
        {
            var (service, users, scope) = await BuildAsync();
            using var _ = scope;
            var actor = await AddUserAsync(users, "actor@workshop.local", Roles.Admin);
            var target = await AddUserAsync(users, "target@workshop.local", Roles.Viewer);

            await service.ChangeRoleAsync(actor.Id, target.Id, Roles.Staff);

            var roles = await users.GetRolesAsync(target);
            Assert.Single(roles);
            Assert.Equal(Roles.Staff, roles[0]);
        }

        [Fact]
        public async Task AssignsRoleToUserWithNoRole()
        {
            var (service, users, scope) = await BuildAsync();
            using var _ = scope;
            var actor = await AddUserAsync(users, "actor@workshop.local", Roles.Admin);
            var orphan = await AddUserAsync(users, "orphan@workshop.local", null);

            var result = await service.ChangeRoleAsync(actor.Id, orphan.Id, Roles.Staff);

            Assert.Equal(UserAdminStatus.Success, result.Status);
            Assert.True(await users.IsInRoleAsync(orphan, Roles.Staff));
        }

        [Fact]
        public async Task RejectsUnknownRoleName()
        {
            var (service, users, scope) = await BuildAsync();
            using var _ = scope;
            var actor = await AddUserAsync(users, "actor@workshop.local", Roles.Admin);
            var target = await AddUserAsync(users, "target@workshop.local", Roles.Viewer);

            var result = await service.ChangeRoleAsync(actor.Id, target.Id, "Superuser");

            Assert.Equal(UserAdminStatus.UnknownRole, result.Status);
            Assert.True(await users.IsInRoleAsync(target, Roles.Viewer));
        }

        // Without the UpdateSecurityStampAsync call, the target's existing auth cookie keeps its
        // old role claims until the validator's next re-check — so a demoted admin stays an admin
        // in practice. This pins that call so a refactor can't quietly drop it.
        [Fact]
        public async Task RoleChangeRefreshesSecurityStamp()
        {
            var (service, users, scope) = await BuildAsync();
            using var _ = scope;
            var actor = await AddUserAsync(users, "actor@workshop.local", Roles.Admin);
            var target = await AddUserAsync(users, "target@workshop.local", Roles.Viewer);

            var before = await users.GetSecurityStampAsync(target);
            await service.ChangeRoleAsync(actor.Id, target.Id, Roles.Staff);
            var after = await users.GetSecurityStampAsync(await users.FindByIdAsync(target.Id) ?? target);

            Assert.NotEqual(before, after);
        }

        [Fact]
        public async Task CreateUserAssignsRequestedRole()
        {
            var (service, users, scope) = await BuildAsync();
            using var _ = scope;

            var result = await service.CreateUserAsync("new@workshop.local", Password, Roles.Staff);

            Assert.Equal(UserAdminStatus.Success, result.Status);
            var created = await users.FindByEmailAsync("new@workshop.local");
            Assert.NotNull(created);
            Assert.True(await users.IsInRoleAsync(created!, Roles.Staff));
        }

        [Fact]
        public async Task ListUsersReportsNullRoleForRolelessAccount()
        {
            var (service, users, scope) = await BuildAsync();
            using var _ = scope;
            await AddUserAsync(users, "orphan@workshop.local", null);
            await AddUserAsync(users, "staff@workshop.local", Roles.Staff);

            var listed = await service.ListUsersAsync();

            Assert.Null(listed.Single(u => u.Email == "orphan@workshop.local").Role);
            Assert.Equal(Roles.Staff, listed.Single(u => u.Email == "staff@workshop.local").Role);
        }

        [Fact]
        public async Task DeleteUserRemovesTheAccount()
        {
            var (service, users, scope) = await BuildAsync();
            using var _ = scope;
            var actor = await AddUserAsync(users, "actor@workshop.local", Roles.Admin);
            var target = await AddUserAsync(users, "target@workshop.local", Roles.Viewer);

            var result = await service.DeleteUserAsync(actor.Id, target.Id);

            Assert.Equal(UserAdminStatus.Success, result.Status);
            Assert.Null(await users.FindByIdAsync(target.Id));
        }

        [Fact]
        public async Task CannotDeleteSelf()
        {
            var (service, users, scope) = await BuildAsync();
            using var _ = scope;
            var admin = await AddUserAsync(users, "admin@workshop.local", Roles.Admin);
            await AddUserAsync(users, "other@workshop.local", Roles.Admin); // so the last-admin guard can't be what fires

            var result = await service.DeleteUserAsync(admin.Id, admin.Id);

            Assert.Equal(UserAdminStatus.CannotDeleteSelf, result.Status);
            Assert.NotNull(await users.FindByIdAsync(admin.Id));
        }

        [Fact]
        public async Task CannotDeleteLastAdmin()
        {
            var (service, users, scope) = await BuildAsync();
            using var _ = scope;
            var onlyAdmin = await AddUserAsync(users, "admin@workshop.local", Roles.Admin);
            // Acting id is a different user, so guard 1 (self-delete) cannot be what rejects this.
            var actor = await AddUserAsync(users, "actor@workshop.local", Roles.Admin);
            await users.RemoveFromRoleAsync(actor, Roles.Admin);

            var result = await service.DeleteUserAsync(actor.Id, onlyAdmin.Id);

            Assert.Equal(UserAdminStatus.CannotRemoveLastAdmin, result.Status);
            Assert.NotNull(await users.FindByIdAsync(onlyAdmin.Id));
        }

        [Fact]
        public async Task CanDeleteAdminWhenAnotherAdminExists()
        {
            var (service, users, scope) = await BuildAsync();
            using var _ = scope;
            var target = await AddUserAsync(users, "target@workshop.local", Roles.Admin);
            var actor = await AddUserAsync(users, "actor@workshop.local", Roles.Admin);

            var result = await service.DeleteUserAsync(actor.Id, target.Id);

            Assert.Equal(UserAdminStatus.Success, result.Status);
            Assert.Null(await users.FindByIdAsync(target.Id));
        }

        [Fact]
        public async Task DeleteReturnsNotFoundForUnknownUser()
        {
            var (service, users, scope) = await BuildAsync();
            using var _ = scope;
            var actor = await AddUserAsync(users, "actor@workshop.local", Roles.Admin);

            var result = await service.DeleteUserAsync(actor.Id, "no-such-id");

            Assert.Equal(UserAdminStatus.UserNotFound, result.Status);
        }

        // A role missing from Roles.All is never seeded and can never be assigned — it would fail
        // silently rather than loudly.
        [Fact]
        public void AllContainsEveryDeclaredRole()
        {
            Assert.Contains(Roles.Viewer, Roles.All);
            Assert.Contains(Roles.Staff, Roles.All);
            Assert.Contains(Roles.Admin, Roles.All);
            Assert.Equal(3, Roles.All.Length);
        }
    }
}
