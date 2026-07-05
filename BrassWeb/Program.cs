using BrassGame;
using BrassWeb;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles(); // serves wwwroot, including wwwroot/img

// static map data for the client renderer
app.MapGet("/api/map", () => new
{
    locations = Data.Locations.Select(l => new
    {
        l.Name, l.X, l.Y, l.Farm,
        slots = l.Slots.Select(s => new { s.X, s.Y, allowed = s.Allowed.Select(a => a.ToString()) })
    }),
    links = Data.Links.Select(k => new { id = k.Id, locs = k.Locs, k.Canal, k.Rail, k.X, k.Y }),
    matSlots = Data.MatSlots.Select(kv => new { ind = kv.Key.Item1.ToString(), level = kv.Key.Item2, x = kv.Value.X, y = kv.Value.Y }),
});

app.MapHub<GameHub>("/hub");
app.Run();
