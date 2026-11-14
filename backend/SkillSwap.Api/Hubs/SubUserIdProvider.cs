using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace SkillSwap.Api.Hubs;

/// <summary>SignalR user identifiers come from the JWT "sub" claim (MapInboundClaims is off).</summary>
public class SubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? connection.User?.FindFirst("sub")?.Value;
}
