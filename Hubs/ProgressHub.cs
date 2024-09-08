using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

public class ProgressHub : Hub
{
    public async Task SendProgress(string message)
    {
        await Clients.All.SendAsync("ReceiveProgress", message);
    }
}
