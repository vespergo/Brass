using System.Collections.Concurrent;
using BrassGame;
using Microsoft.AspNetCore.SignalR;

namespace BrassWeb;

public class User
{
    public required string ConnId;
    public required string Name;
    public Room? Room;
}

public class Room
{
    public required string Id;
    public required Game Game;
    public string?[] Seats = new string?[4];   // conn id per player index (null = disconnected)
    public List<string> LogHistory = new();
    public SemaphoreSlim Lock = new(1, 1);
    public TaskCompletionSource<int>? PendingChoice;
    public string? PendingChoiceConn;
    public string? PendingPrompt;
    public List<string>? PendingOptions;
}

// Blocks (on a background task) until the target browser answers, times out to option 0 so a
// vanished player can't freeze the game forever.  ponytail: 2-min timeout, no per-turn clock.
public class WebChooser : IChooser
{
    readonly Room room;
    readonly IHubContext<GameHub> hub;
    readonly int seat;
    public WebChooser(Room room, IHubContext<GameHub> hub, int seat) { this.room = room; this.hub = hub; this.seat = seat; }

    public int Pick(string prompt, IList<string> options)
    {
        if (options.Count == 1) return 0;
        var conn = room.Seats[seat];
        if (conn == null) return 0; // player gone: take the first option
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        room.PendingChoice = tcs;
        room.PendingChoiceConn = conn;
        room.PendingPrompt = prompt;
        room.PendingOptions = options.ToList();
        hub.Clients.Client(conn).SendAsync("choice", prompt, options);
        var done = Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMinutes(2))).GetAwaiter().GetResult();
        room.PendingChoice = null;
        room.PendingChoiceConn = null;
        room.PendingPrompt = null;
        room.PendingOptions = null;
        int r = done == tcs.Task ? tcs.Task.Result : 0;
        return r >= 0 && r < options.Count ? r : 0;
    }
}

public class GameHub : Hub
{
    static readonly ConcurrentDictionary<string, User> Users = new();          // by conn id
    static readonly ConcurrentDictionary<string, Room> Rooms = new();
    static int roomCounter;
    readonly IHubContext<GameHub> hubCtx;
    public GameHub(IHubContext<GameHub> hubCtx) => this.hubCtx = hubCtx;

    User? Me => Users.TryGetValue(Context.ConnectionId, out var u) ? u : null;

    // ---------- lobby ----------

    public async Task Join(string name)
    {
        name = name.Trim();
        if (name.Length == 0) name = "Anon";
        if (name.Length > 20) name = name[..20];

        // reattach to a running game with a vacant seat of the same name
        foreach (var room in Rooms.Values)
        {
            var idx = Array.FindIndex(room.Game.Players.ToArray(), p => p.Name == name);
            if (idx >= 0 && room.Seats[idx] == null && !room.Game.GameOver)
            {
                var u = new User { ConnId = Context.ConnectionId, Name = name, Room = room };
                Users[Context.ConnectionId] = u;
                room.Seats[idx] = Context.ConnectionId;
                await Clients.Caller.SendAsync("logHistory", room.LogHistory);
                await BroadcastState(room);
                await BroadcastLobby();
                return;
            }
        }

        while (Users.Values.Any(u => u.Name == name)) name += "'";
        Users[Context.ConnectionId] = new User { ConnId = Context.ConnectionId, Name = name };
        await Clients.Caller.SendAsync("joined", name);
        await BroadcastLobby();
    }

    public async Task StartGame(List<string> names)
    {
        var me = Me;
        if (me == null || me.Room != null) return;
        if (!names.Contains(me.Name)) { await Err("Include yourself in the game."); return; }
        var users = names.Distinct()
            .Select(n => Users.Values.FirstOrDefault(u => u.Name == n && u.Room == null))
            .Where(u => u != null).Cast<User>().ToList();
        if (users.Count < 2 || users.Count > 4) { await Err("Pick 2-4 lobby players."); return; }

        var colors = new[] { "Red", "Yellow", "Purple", "Gray" };
        var room = new Room
        {
            Id = $"g{Interlocked.Increment(ref roomCounter)}",
            Game = new Game(users.Select((u, i) => (u.Name, colors[i])))
        };
        room.Game.Log = s =>
        {
            room.LogHistory.Add(s);
            foreach (var c in room.Seats.Where(c => c != null))
                hubCtx.Clients.Client(c!).SendAsync("log", s);
        };
        room.Game.ChooserFor = p => new WebChooser(room, hubCtx, p.Index);
        for (int i = 0; i < users.Count; i++)
        {
            room.Seats[i] = users[i].ConnId;
            users[i].Room = room;
        }
        Rooms[room.Id] = room;
        room.Game.Log($"Game started: {string.Join(", ", users.Select(u => u.Name))}");
        room.Game.Log($"--- Round 1 (Canal Era) --- order: {string.Join(", ", room.Game.Order.Select(o => o.Name))} (1 action each this round)");
        await BroadcastState(room);
        await BroadcastLobby();
    }

    public async Task LeaveGame()
    {
        var me = Me;
        if (me?.Room == null) return;
        var room = me.Room;
        int seat = Array.IndexOf(room.Seats, Context.ConnectionId);
        if (seat >= 0) room.Seats[seat] = null;
        me.Room = null;
        if (room.Seats.All(s => s == null) && room.Game.GameOver) Rooms.TryRemove(room.Id, out _);
        await Clients.Caller.SendAsync("joined", me.Name);
        await BroadcastLobby();
    }

    public override async Task OnDisconnectedAsync(Exception? ex)
    {
        if (Users.TryRemove(Context.ConnectionId, out var u) && u.Room != null)
        {
            int seat = Array.IndexOf(u.Room.Seats, Context.ConnectionId);
            if (seat >= 0) u.Room.Seats[seat] = null;
            // answer a pending choice with default so the game continues
            if (u.Room.PendingChoiceConn == Context.ConnectionId)
                u.Room.PendingChoice?.TrySetResult(0);
        }
        await BroadcastLobby();
        await base.OnDisconnectedAsync(ex);
    }

    Task Err(string msg) => Clients.Caller.SendAsync("error", msg);

    async Task BroadcastLobby()
    {
        var lobby = Users.Values.Where(u => u.Room == null).Select(u => u.Name).OrderBy(n => n).ToList();
        foreach (var u in Users.Values.Where(u => u.Room == null))
            await hubCtx.Clients.Client(u.ConnId).SendAsync("lobby", lobby);
    }

    // ---------- game state ----------

    static object StateFor(Room room, int seat)
    {
        var g = room.Game;
        return new
        {
            era = g.Era.ToString(),
            round = g.Round,
            roundsPerEra = g.RoundsPerEra,
            deck = g.Deck.Count,
            coal = new { count = g.CoalMarket.Sum(), next = NextPrice(g.CoalMarket, 7, 8) },
            iron = new { count = g.IronMarket.Sum(), next = NextPrice(g.IronMarket, 5, 6) },
            coalMkt = g.CoalMarket,
            ironMkt = g.IronMarket,
            actionsLeft = g.ActionsLeft,
            currentIndex = g.Current.Index,
            you = seat,
            gameOver = g.GameOver,
            players = g.Order.Select(p => new
            {
                index = p.Index, p.Name, color = p.ColorName, money = p.Money,
                income = p.IncomeLevel, incomeSpace = p.IncomeSpace, spent = p.Spent,
                vp = p.VP, links = p.LinksLeft, handCount = p.Hand.Count,
                connected = room.Seats[p.Index] != null,
                mat = p.Mat.SelectMany(kv => kv.Value.GroupBy(t => t.Level)
                    .Select(grp => new { ind = kv.Key.ToString(), level = grp.Key, count = grp.Count() })),
            }),
            hand = seat >= 0 ? g.Players[seat].Hand.Select(c => c.Label) : Enumerable.Empty<string>(),
            tiles = g.Tiles.Select(t => new
            {
                t.Loc, slot = t.SlotIdx, ind = t.Spec.Ind.ToString(), level = t.Spec.Level,
                owner = t.Owner.Index, cubes = t.Cubes, flipped = t.Flipped,
            }),
            links = g.Links.Select(l => new { id = l.Def.Id, owner = l.Owner.Index }),
            merchants = g.MerchantSlots.Select(m => new
            {
                x = m.Pos.X, y = m.Pos.Y, good = m.Good.ToString(), beer = m.HasBeer,
            }),
        };
    }

    static string NextPrice(int[] mkt, int max, int empty)
    {
        for (int p = 1; p <= max; p++) if (mkt[p] > 0) return p.ToString();
        return empty.ToString();
    }

    async Task BroadcastState(Room room)
    {
        for (int i = 0; i < 4; i++)
            if (room.Seats[i] != null)
                await hubCtx.Clients.Client(room.Seats[i]!).SendAsync("state", StateFor(room, i));
    }

    // ---------- actions ----------

    (Room room, Game g, Player p)? MyTurn()
    {
        var me = Me;
        if (me?.Room == null) return null;
        var g = me.Room.Game;
        if (g.GameOver) return null;
        int seat = Array.IndexOf(me.Room.Seats, Context.ConnectionId);
        if (seat != g.Current.Index) return null;
        return (me.Room, g, g.Current);
    }

    // Runs a possibly-blocking game action on a background task. Fire-and-forget: the hub method
    // returns immediately so the same client's ChoiceReply invocations can still be dispatched
    // (SignalR serializes invocations per client by default). The room lock serializes the game.
    Task RunAction(Room room, Action act)
    {
        string connId = Context.ConnectionId;
        _ = Task.Run(async () =>
        {
            if (!await room.Lock.WaitAsync(0)) return; // an action is already in progress
            try
            {
                act();
                await AfterAction(room);
            }
            catch (Exception ex) { await hubCtx.Clients.Client(connId).SendAsync("error", ex.ToString()); }
            finally { room.Lock.Release(); }
        });
        return Task.CompletedTask;
    }

    async Task AfterAction(Room room)
    {
        var g = room.Game;
        if (!g.GameOver && g.TurnOver)
        {
            var ch = new WebChooser(room, hubCtx, g.Current.Index);
            g.EndTurn(ch);
        }
        await BroadcastState(room);
        await BroadcastLobby();
    }

    public Task<List<object>> GetBuildOptions(int cardIdx)
    {
        var t = MyTurn();
        if (t == null || cardIdx < 0 || cardIdx >= t.Value.p.Hand.Count) return Task.FromResult(new List<object>());
        var (_, g, p) = t.Value;
        var opts = g.BuildOptions(p, p.Hand[cardIdx]);
        return Task.FromResult(opts.Select((o, i) => (object)new
        {
            i, loc = o.Loc.Name, slot = o.SlotIdx,
            label = $"{o.Spec} (£{o.MoneyCost}{(o.Over != null ? ", overbuild" : "")})",
        }).ToList());
    }

    public Task Build(int cardIdx, int optIdx)
    {
        var t = MyTurn();
        if (t == null) return Task.CompletedTask;
        var (room, g, p) = t.Value;
        return RunAction(room, () =>
        {
            if (cardIdx < 0 || cardIdx >= p.Hand.Count) return;
            var card = p.Hand[cardIdx];
            var opts = g.BuildOptions(p, card); // deterministic re-computation
            if (optIdx < 0 || optIdx >= opts.Count) return;
            g.ExecBuild(p, card, opts[optIdx], new WebChooser(room, hubCtx, p.Index));
            g.ActionDone();
        });
    }

    public Task<List<string>> GetNetworkOptions()
    {
        var t = MyTurn();
        if (t == null) return Task.FromResult(new List<string>());
        var (_, g, p) = t.Value;
        return Task.FromResult(g.NetworkOptions(p, false).Select(l => l.Id).ToList());
    }

    public Task Network(int cardIdx, string linkId)
    {
        var t = MyTurn();
        if (t == null) return Task.CompletedTask;
        var (room, g, p) = t.Value;
        return RunAction(room, () =>
        {
            if (cardIdx < 0 || cardIdx >= p.Hand.Count) return;
            var def = g.NetworkOptions(p, false).FirstOrDefault(l => l.Id == linkId);
            if (def == null) return;
            var ch = new WebChooser(room, hubCtx, p.Index);
            g.SpendAction(p, p.Hand[cardIdx]);
            g.ExecNetwork(p, def, false, ch);
            if (g.Era == Era.Rail)
            {
                var second = g.NetworkOptions(p, true);
                if (second.Count > 0)
                {
                    int pick = PickSecondRail(room, p, second);
                    if (pick > 0) g.ExecNetwork(p, second[pick - 1], true, ch);
                }
            }
        });
    }

    // Second rail link: client renders the candidates on the map ("networkPick" event) and replies
    // via ChoiceReply. Index 0 = stop, 1..N = second[pick-1]. Same TCS plumbing as WebChooser.Pick.
    int PickSecondRail(Room room, Player p, List<LinkDef> second)
    {
        var conn = room.Seats[p.Index];
        if (conn == null) return 0;
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        room.PendingChoice = tcs;
        room.PendingChoiceConn = conn;
        room.PendingPrompt = "Build a second rail link? (+£10, +1 coal, +1 beer)";
        room.PendingOptions = new[] { "No, stop here" }.Concat(second.Select(l => l.ToString())).ToList();
        hubCtx.Clients.Client(conn).SendAsync("networkPick", room.PendingPrompt, second.Select(l => l.Id).ToList());
        var done = Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMinutes(2))).GetAwaiter().GetResult();
        room.PendingChoice = null; room.PendingChoiceConn = null; room.PendingPrompt = null; room.PendingOptions = null;
        int r = done == tcs.Task ? tcs.Task.Result : 0;
        return r >= 0 && r <= second.Count ? r : 0;
    }

    static List<object> SellOptsJson(List<SellableTile> opts) =>
        opts.Select((s, i) => (object)new
        {
            i, loc = s.Tile.Loc, slot = s.Tile.SlotIdx,
            label = $"{s.Tile.Spec} in {s.Tile.Loc}",
            merchants = s.Merchants.Select(m => new { label = m.Label, hasBeer = m.HasBeer }).ToList(),
        }).ToList();

    public Task<List<object>> GetSellOptions()
    {
        var t = MyTurn();
        if (t == null) return Task.FromResult(new List<object>());
        var (_, g, p) = t.Value;
        return Task.FromResult(SellOptsJson(g.SellOptions(p)));
    }

    // First sell is chosen client-side (so Cancel is free); the card is only spent here once a tile is picked.
    public Task DoSell(int cardIdx, int optIdx, int merchantIdx)
    {
        var t = MyTurn();
        if (t == null) return Task.CompletedTask;
        var (room, g, p) = t.Value;
        return RunAction(room, () =>
        {
            if (cardIdx < 0 || cardIdx >= p.Hand.Count) return;
            var opts = g.SellOptions(p);
            if (optIdx < 0 || optIdx >= opts.Count) return;
            var s = opts[optIdx];
            if (merchantIdx < 0 || merchantIdx >= s.Merchants.Count) return;
            var ch = new WebChooser(room, hubCtx, p.Index);
            g.SpendAction(p, p.Hand[cardIdx]);
            g.ExecSell(p, s.Tile, s.Merchants[merchantIdx], ch);
            // Additional sells re-arm the board (sellPick event) and wait for a tile click or
            // Cancel, instead of a modal loop. One action still covers every sell; the turn ends
            // only when the player stops or runs out of sellable tiles.
            while ((opts = g.SellOptions(p)).Count > 0)
            {
                int pick = PickAnotherSell(room, p, opts);
                if (pick <= 0) break;
                var x = opts[pick - 1];
                var m = x.Merchants.Count == 1 ? x.Merchants[0]
                    : x.Merchants[ch.Pick("Sell to which merchant?", x.Merchants.Select(mm => mm.Label + (mm.HasBeer ? " [beer]" : "")).ToList())];
                g.ExecSell(p, x.Tile, m, ch);
            }
        });
    }

    // Re-arms the seller's board with fresh options and waits for a tile click (1..N) or Stop
    // (0). The live state is sent too so the just-sold tile flips before the next pick.
    int PickAnotherSell(Room room, Player p, List<SellableTile> opts)
    {
        var conn = room.Seats[p.Index];
        if (conn == null) return 0;
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        room.PendingChoice = tcs;
        room.PendingChoiceConn = conn;
        room.PendingPrompt = "Sell another tile?";
        room.PendingOptions = new[] { "Stop selling" }.Concat(opts.Select(x => $"{x.Tile.Spec} in {x.Tile.Loc}")).ToList();
        hubCtx.Clients.Client(conn).SendAsync("sellPick", SellOptsJson(opts), StateFor(room, p.Index));
        var done = Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMinutes(2))).GetAwaiter().GetResult();
        room.PendingChoice = null; room.PendingChoiceConn = null; room.PendingPrompt = null; room.PendingOptions = null;
        int r = done == tcs.Task ? tcs.Task.Result : 0;
        return r >= 0 && r <= opts.Count ? r : 0;
    }

    public Task<List<string>> GetDevelopOptions()
    {
        var t = MyTurn();
        if (t == null) return Task.FromResult(new List<string>());
        var (_, g, p) = t.Value;
        if (!g.CanDevelop(p, 1)) return Task.FromResult(new List<string>());
        return Task.FromResult(g.DevelopOptions(p).Select(x => x.ToString()).ToList());
    }

    public Task Develop(int cardIdx, int firstIdx)
    {
        var t = MyTurn();
        if (t == null) return Task.CompletedTask;
        var (room, g, p) = t.Value;
        return RunAction(room, () =>
        {
            if (cardIdx < 0 || cardIdx >= p.Hand.Count || !g.CanDevelop(p, 1)) return;
            var opts = g.DevelopOptions(p);
            if (firstIdx < 0 || firstIdx >= opts.Count) return;
            var ch = new WebChooser(room, hubCtx, p.Index);
            g.SpendAction(p, p.Hand[cardIdx]);
            g.ExecDevelop(p, opts[firstIdx], ch);
            var opts2 = g.DevelopOptions(p);
            if (opts2.Count > 0 && g.CanDevelop(p, 1))
            {
                int pick = ch.Pick("Develop a second tile? (1 more iron)",
                    new[] { "No" }.Concat(opts2.Select(x => x.ToString())).ToList());
                if (pick > 0) g.ExecDevelop(p, opts2[pick - 1], ch);
            }
        });
    }

    public Task Loan(int cardIdx)
    {
        var t = MyTurn();
        if (t == null) return Task.CompletedTask;
        var (room, g, p) = t.Value;
        return RunAction(room, () =>
        {
            if (cardIdx < 0 || cardIdx >= p.Hand.Count || !g.CanLoan(p)) return;
            g.SpendAction(p, p.Hand[cardIdx]);
            g.ExecLoan(p);
        });
    }

    public Task Scout(int cardIdx, int extra1, int extra2)
    {
        var t = MyTurn();
        if (t == null) return Task.CompletedTask;
        var (room, g, p) = t.Value;
        return RunAction(room, () =>
        {
            if (!g.CanScout(p)) return;
            var idx = new[] { cardIdx, extra1, extra2 };
            if (idx.Distinct().Count() != 3 || idx.Any(i => i < 0 || i >= p.Hand.Count)) return;
            var cards = idx.Select(i => p.Hand[i]).ToList();
            g.SpendAction(p, cards[0]);
            g.ExecScout(p, cards[1], cards[2]);
        });
    }

    public Task Pass(int cardIdx)
    {
        var t = MyTurn();
        if (t == null) return Task.CompletedTask;
        var (room, g, p) = t.Value;
        return RunAction(room, () =>
        {
            if (cardIdx < 0 || cardIdx >= p.Hand.Count) return;
            g.SpendAction(p, p.Hand[cardIdx]);
            g.Log($"{p.Name} passes");
        });
    }

    public Task ChoiceReply(int idx)
    {
        var me = Me;
        if (me?.Room != null && me.Room.PendingChoiceConn == Context.ConnectionId)
            me.Room.PendingChoice?.TrySetResult(idx);
        return Task.CompletedTask;
    }
}
