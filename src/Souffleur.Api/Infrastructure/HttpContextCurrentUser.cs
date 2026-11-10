using System.Security.Claims;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Infrastructure;

public class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? Id =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id)
            ? id
            : null;
}
