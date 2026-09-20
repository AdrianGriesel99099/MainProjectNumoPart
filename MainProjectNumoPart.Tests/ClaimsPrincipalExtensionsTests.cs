using System.Collections.Generic;
using System.Security.Claims;
using MainProjectNumoPart.Authorization;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class ClaimsPrincipalExtensionsTests
    {
        private static ClaimsPrincipal BuildUser(string? userId, params string[] roles)
        {
            var claims = new List<Claim>();
            if (userId is not null)
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            }
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        }

        [Fact]
        public void CanDeleteNote_TrueForTheNotesOwnAuthor()
        {
            var user = BuildUser("u1", Roles.Staff);

            Assert.True(user.CanDeleteNote("u1"));
        }

        [Fact]
        public void CanDeleteNote_FalseForADifferentNonAdminUser()
        {
            var user = BuildUser("u2", Roles.Staff);

            Assert.False(user.CanDeleteNote("u1"));
        }

        [Fact]
        public void CanDeleteNote_TrueForAnAdminRegardlessOfAuthor()
        {
            var user = BuildUser("admin1", Roles.Admin);

            Assert.True(user.CanDeleteNote("u1"));
        }

        [Fact]
        public void CurrentUserId_ReturnsTheNameIdentifierClaim()
        {
            var user = BuildUser("u1", Roles.Staff);

            Assert.Equal("u1", user.CurrentUserId());
        }

        [Fact]
        public void CurrentUserId_NullWhenSignedOut()
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity());

            Assert.Null(user.CurrentUserId());
        }
    }
}
