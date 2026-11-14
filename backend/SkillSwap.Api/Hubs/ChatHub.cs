using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SkillSwap.Api.Hubs;

[Authorize]
public class ChatHub(AppDbContext db) : Hub
{
    public async Task JoinSwap(Guid swapId)
    {
        var me = GetUserId();
        var allowed = await db.Swaps.AnyAsync(s =>
            s.Id == swapId && (s.ProposerId == me || s.ReceiverId == me));
        if (!allowed)
            throw new HubException("You are not part of this swap.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(swapId));
    }

    public Task LeaveSwap(Guid swapId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(swapId));

    public async Task SendMessage(Guid swapId, string text)
    {
        var me = GetUserId();
        text = (text ?? "").Trim();
        if (text.Length is < 1 or > 2000)
            throw new HubException("Messages must be between 1 and 2000 characters.");

        var swap = await db.Swaps.FirstOrDefaultAsync(s => s.Id == swapId);
        if (swap is null)
            throw new HubException("Swap not found.");
        if (swap.ProposerId != me && swap.ReceiverId != me)
            throw new HubException("You are not part of this swap.");
        if (swap.Status != SwapStatus.Active)
            throw new HubException("You can only message in an Active swap.");

        var message = new Message
        {
            SwapId = swapId,
            SenderId = me,
            Text = text,
            SentAt = DateTime.UtcNow
        };
        db.Messages.Add(message);

        var partnerId = swap.ProposerId == me ? swap.ReceiverId : swap.ProposerId;
        db.Notifications.Add(new Notification
        {
            UserId = partnerId,
            Type = NotificationType.NewMessage,
            Text = "New message in your swap",
            SwapId = swapId,
            IsRead = false
        });

        await db.SaveChangesAsync();

        var dto = new MessageDto(message.Id, message.SwapId, message.SenderId, message.Text, message.SentAt);
        await Clients.Group(GroupName(swapId)).SendAsync("ReceiveMessage", dto);
        await Clients.User(partnerId.ToString()).SendAsync("ReceiveMessage", dto);
    }

    public Task Typing(Guid swapId) =>
        Clients.OthersInGroup(GroupName(swapId)).SendAsync("UserTyping", swapId, GetUserId());

    private Guid GetUserId() =>
        Guid.Parse(Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? Context.User?.FindFirst("sub")?.Value
                   ?? throw new HubException("Missing user id claim."));

    private static string GroupName(Guid swapId) => $"swap:{swapId}";
}
