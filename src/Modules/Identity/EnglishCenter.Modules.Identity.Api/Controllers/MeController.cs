using EnglishCenter.Modules.Identity.Application.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.Identity.Api.Controllers;

[ApiController]
[Route("api/identity/me")]
[Authorize]
[IdentityErrors]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MeController(IdentityService identity) : ControllerBase
{
    [HttpGet]
    public Task<ProfileResponse> Get(CancellationToken ct) => identity.GetProfileAsync(ct);

    [HttpPut]
    public Task<ProfileResponse> Update(UpdateProfileRequest request, CancellationToken ct) =>
        identity.UpdateProfileAsync(request, ct);
}
