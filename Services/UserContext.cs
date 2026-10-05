using System.Security.Claims;
using CampusTransit.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CampusTransit.Services;

/// <summary>Resolves the signed-in <see cref="AppUser"/> for a Blazor circuit.</summary>
public class UserContext(AuthenticationStateProvider authenticationState, AppDbContext db)
{
    public async Task<AppUser?> GetUserAsync()
    {
        var state = await authenticationState.GetAuthenticationStateAsync();
        var principal = state.User;

        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(id, out var userId))
        {
            return null;
        }

        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
    }
}
