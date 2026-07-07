namespace BrassGame;

// Resolves player decisions the rules leave open (which coal mine on a tie, which beer, etc.).
public interface IChooser
{
    int Pick(string prompt, IList<string> options); // returns index
}

public class Player
{
    public int Index;
    public string Name = "";
    public string ColorName = "";  // Red / Yellow / Purple / Gray
    public int Money = 17;
    public int IncomeSpace = 10;   // level 0
    public int VP;
    public int Spent;              // this round, for turn order
    public int LinksLeft = 14;
    public List<Card> Hand = new();
    public Dictionary<Industry, List<TileSpec>> Mat = new(); // lowest level first

    public int IncomeLevel => Data.IncomeLevel(IncomeSpace);
    public TileSpec? NextTile(Industry i) => Mat[i].FirstOrDefault();
}

public class PlacedTile
{
    public required Player Owner;
    public required TileSpec Spec;
    public required string Loc;
    public required int SlotIdx;
    public int Cubes;
    public bool Flipped;
}

public class PlacedLink
{
    public required LinkDef Def;
    public required Player Owner;
}

public class MerchantSlot
{
    public required MerchDef Merch;
    public required int Index;
    public Good Good;
    public bool HasBeer;
    public (int X, int Y) Pos => Merch.SlotPos[Index];
    public string Label => $"{Merch.Name} ({Good})";
    public bool Accepts(Industry i) =>
        Good == Good.Any ? i is Industry.Cotton or Industry.Manufacturer or Industry.Pottery :
        Good == Good.Cotton ? i == Industry.Cotton :
        Good == Good.Manufacturer ? i == Industry.Manufacturer :
        Good == Good.Pottery && i == Industry.Pottery;
}

public record BuildOpt(LocDef Loc, int SlotIdx, TileSpec Spec, PlacedTile? Over, int MoneyCost);
public record SellableTile(PlacedTile Tile, List<MerchantSlot> Merchants);

public class Game
{
    public int PlayerCount;
    public List<Player> Players = new();
    public List<Player> Order = new();
    public Era Era = Era.Canal;
    public int Round = 1;
    public int TurnIdx;            // index into Order
    public int ActionsLeft;
    public bool GameOver;
    public List<Card> Deck = new();
    public List<Card> Discards = new();
    public List<PlacedTile> Tiles = new();
    public List<PlacedLink> Links = new();
    public List<MerchantSlot> MerchantSlots = new();
    public int[] CoalMarket = new int[8];  // index 1..7, cubes per price (max 2)
    public int[] IronMarket = new int[6];  // index 1..5
    public int WildLocSupply = 4, WildIndSupply = 4;
    public Action<string> Log = _ => { };
    // Optional per-player chooser routing (used by the web app; hotseat uses the shared chooser).
    public Func<Player, IChooser>? ChooserFor;
    public Random Rng;

    public Player Current => Order[TurnIdx];
    public int RoundsPerEra => PlayerCount == 2 ? 10 : PlayerCount == 3 ? 9 : 8;

    public Game(IEnumerable<(string Name, string Color)> players, int seed = 0)
    {
        Rng = seed == 0 ? new Random() : new Random(seed);
        foreach (var (name, color) in players)
        {
            var p = new Player { Index = Players.Count, Name = name, ColorName = color };
            foreach (Industry i in Enum.GetValues<Industry>())
                p.Mat[i] = Data.Tiles.Where(t => t.Ind == i)
                    .OrderBy(t => t.Level).SelectMany(t => Enumerable.Repeat(t, t.Count)).ToList();
            Players.Add(p);
        }
        PlayerCount = Players.Count;

        for (int pr = 1; pr <= 7; pr++) CoalMarket[pr] = 2;
        CoalMarket[1] = 1;
        for (int pr = 2; pr <= 5; pr++) IronMarket[pr] = 2;
        IronMarket[1] = 0;

        var pool = Data.MerchantPool(PlayerCount);
        Shuffle(pool);
        foreach (var m in Data.Merchants.Where(m => m.MinPlayers <= PlayerCount))
            for (int i = 0; i < m.SlotPos.Length; i++)
            {
                var g = pool[0]; pool.RemoveAt(0);
                MerchantSlots.Add(new MerchantSlot { Merch = m, Index = i, Good = g, HasBeer = g != Good.Blank });
            }

        Deck = Data.BuildDeck(PlayerCount);
        Shuffle(Deck);
        foreach (var p in Players)
        {
            for (int i = 0; i < 8; i++) DrawTo(p);
            Discards.Add(DrawCard()!); // setup discard pile seed card
        }
        Order = Players.ToList();
        Shuffle(Order);
        ActionsLeft = 1; // first Canal round: 1 action
    }

    void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    Card? DrawCard() { if (Deck.Count == 0) return null; var c = Deck[^1]; Deck.RemoveAt(Deck.Count - 1); return c; }
    void DrawTo(Player p) { var c = DrawCard(); if (c != null) p.Hand.Add(c); }

    // ---------- connectivity ----------

    Dictionary<string, List<string>> Adjacency()
    {
        var adj = new Dictionary<string, List<string>>();
        void Add(string a, string b)
        {
            if (!adj.TryGetValue(a, out var l)) adj[a] = l = new List<string>();
            l.Add(b);
        }
        foreach (var pl in Links)
            foreach (var a in pl.Def.Locs)
                foreach (var b in pl.Def.Locs)
                    if (a != b) Add(a, b);
        return adj;
    }

    public Dictionary<string, int> Distances(string from, LinkDef? pretend = null)
    {
        var adj = Adjacency();
        if (pretend != null)
            foreach (var a in pretend.Locs)
                foreach (var b in pretend.Locs)
                    if (a != b)
                    {
                        if (!adj.TryGetValue(a, out var l)) adj[a] = l = new List<string>();
                        l.Add(b);
                    }
        var dist = new Dictionary<string, int> { [from] = 0 };
        var q = new Queue<string>();
        q.Enqueue(from);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (!adj.TryGetValue(cur, out var ns)) continue;
            foreach (var n in ns)
                if (!dist.ContainsKey(n)) { dist[n] = dist[cur] + 1; q.Enqueue(n); }
        }
        return dist;
    }

    public bool Connected(string a, string b, LinkDef? pretend = null) => Distances(a, pretend).ContainsKey(b);

    // Merchant *spaces* count even without tiles (e.g. Nottingham in a 2p game).
    public bool ConnectedToMerchant(string loc, LinkDef? pretend = null)
    {
        var d = Distances(loc, pretend);
        return Data.Merchants.Any(m => d.ContainsKey(m.Name));
    }

    public HashSet<string> NetworkOf(Player p)
    {
        var set = new HashSet<string>();
        foreach (var t in Tiles.Where(t => t.Owner == p)) set.Add(t.Loc);
        foreach (var l in Links.Where(l => l.Owner == p)) set.UnionWith(l.Def.Locs);
        return set;
    }

    public bool HasNothingOnBoard(Player p) => !Tiles.Any(t => t.Owner == p) && !Links.Any(l => l.Owner == p);

    // ---------- coal / iron / beer ----------

    List<PlacedTile> MinesByDistance(string loc, LinkDef? pretend, out Dictionary<string, int> dist)
    {
        dist = Distances(loc, pretend);
        var d = dist;
        return Tiles.Where(t => t.Spec.Ind == Industry.Coal && !t.Flipped && t.Cubes > 0 && d.ContainsKey(t.Loc))
                    .OrderBy(t => d[t.Loc]).ToList();
    }

    public int CoalAvailableFree(string loc, LinkDef? pretend = null) =>
        MinesByDistance(loc, pretend, out _).Sum(t => t.Cubes);

    public int MarketCoalCost(int n)
    {
        int cost = 0, left = n;
        var mkt = (int[])CoalMarket.Clone();
        for (int pr = 1; pr <= 7 && left > 0; pr++)
            while (mkt[pr] > 0 && left > 0) { mkt[pr]--; cost += pr; left--; }
        cost += left * 8;
        return cost;
    }

    public int MarketIronCost(int n)
    {
        int cost = 0, left = n;
        var mkt = (int[])IronMarket.Clone();
        for (int pr = 1; pr <= 5 && left > 0; pr++)
            while (mkt[pr] > 0 && left > 0) { mkt[pr]--; cost += pr; left--; }
        cost += left * 6;
        return cost;
    }

    public int IronAvailableFree() =>
        Tiles.Where(t => t.Spec.Ind == Industry.Iron && !t.Flipped).Sum(t => t.Cubes);

    // Total extra money needed for coal+iron of a build at loc; -1 if coal unobtainable.
    public int ResourceCost(string loc, int coal, int iron, LinkDef? pretend = null)
    {
        int cost = 0;
        if (coal > 0)
        {
            int free = CoalAvailableFree(loc, pretend);
            int shortfall = Math.Max(0, coal - free);
            if (free == 0 && !ConnectedToMerchant(loc, pretend)) return -1;
            if (shortfall > 0)
            {
                if (!ConnectedToMerchant(loc, pretend)) return -1;
                cost += MarketCoalCost(shortfall);
            }
        }
        if (iron > 0)
        {
            int shortfall = Math.Max(0, iron - IronAvailableFree());
            if (shortfall > 0) cost += MarketIronCost(shortfall);
        }
        return cost;
    }

    void FlipProducer(PlacedTile t)
    {
        t.Flipped = true;
        AdvanceIncome(t.Owner, t.Spec.Income);
        Log($"{t.Owner.Name}'s {t.Spec} in {t.Loc} flips (+{t.Spec.Income} income spaces)");
    }

    public void ConsumeCoal(Player payer, string loc, int n, IChooser ch, LinkDef? pretend = null)
    {
        for (int k = 0; k < n; k++)
        {
            var mines = MinesByDistance(loc, pretend, out var dist);
            if (mines.Count > 0)
            {
                int best = dist[mines[0].Loc];
                var tied = mines.Where(m => dist[m.Loc] == best).ToList();
                var pick = tied.Count == 1 ? tied[0]
                    : tied[ch.Pick("Consume coal from which mine?", tied.Select(m => $"{m.Owner.Name}'s {m.Spec} in {m.Loc} ({m.Cubes} coal)").ToList())];
                pick.Cubes--;
                if (pick.Cubes == 0) FlipProducer(pick);
            }
            else
            {
                int pr = Enumerable.Range(1, 7).FirstOrDefault(i => CoalMarket[i] > 0);
                int cost = pr == 0 ? 8 : pr;
                if (pr > 0) CoalMarket[pr]--;
                Pay(payer, cost);
                Log($"{payer.Name} buys coal from the market for £{cost}");
            }
        }
    }

    public void ConsumeIron(Player payer, int n, IChooser ch)
    {
        for (int k = 0; k < n; k++)
        {
            var works = Tiles.Where(t => t.Spec.Ind == Industry.Iron && !t.Flipped && t.Cubes > 0).ToList();
            if (works.Count > 0)
            {
                var pick = works.Count == 1 ? works[0]
                    : works[ch.Pick("Consume iron from which works?", works.Select(w => $"{w.Owner.Name}'s {w.Spec} in {w.Loc} ({w.Cubes} iron)").ToList())];
                pick.Cubes--;
                if (pick.Cubes == 0) FlipProducer(pick);
            }
            else
            {
                int pr = Enumerable.Range(1, 5).FirstOrDefault(i => IronMarket[i] > 0);
                int cost = pr == 0 ? 6 : pr;
                if (pr > 0) IronMarket[pr]--;
                Pay(payer, cost);
                Log($"{payer.Name} buys iron from the market for £{cost}");
            }
        }
    }

    // Beer sources for a requirement at loc. merch != null when selling to it (its own beer usable).
    public List<(string Label, PlacedTile? Brewery, MerchantSlot? Merch)> BeerSources(Player p, string loc, MerchantSlot? merch)
    {
        var res = new List<(string, PlacedTile?, MerchantSlot?)>();
        foreach (var b in Tiles.Where(t => t.Spec.Ind == Industry.Brewery && !t.Flipped && t.Cubes > 0))
        {
            if (b.Owner == p)
                res.Add(($"Your brewery in {b.Loc} ({b.Cubes} beer)", b, null));
            else if (Connected(loc, b.Loc))
                res.Add(($"{b.Owner.Name}'s brewery in {b.Loc} ({b.Cubes} beer)", b, null));
        }
        if (merch != null && merch.HasBeer)
            res.Add(($"Merchant beer at {merch.Label} (bonus!)", null, merch));
        return res;
    }

    public void ConsumeBeer(Player p, string loc, MerchantSlot? merch, IChooser ch)
    {
        var sources = BeerSources(p, loc, merch);
        int i = sources.Count == 1 ? 0 : ch.Pick("Consume beer from:", sources.Select(s => s.Label).ToList());
        var (_, brewery, m) = sources[i];
        if (brewery != null)
        {
            brewery.Cubes--;
            if (brewery.Cubes == 0) FlipProducer(brewery);
        }
        else if (m != null)
        {
            m.HasBeer = false;
            ApplyMerchantBonus(p, m, ch);
        }
    }

    void ApplyMerchantBonus(Player p, MerchantSlot m, IChooser ch)
    {
        switch (m.Merch.Bonus)
        {
            case BonusKind.Money: p.Money += m.Merch.BonusVal; Log($"{p.Name} gains £{m.Merch.BonusVal} ({m.Merch.Name} bonus)"); break;
            case BonusKind.VP: p.VP += m.Merch.BonusVal; Log($"{p.Name} gains {m.Merch.BonusVal} VP ({m.Merch.Name} bonus)"); break;
            case BonusKind.Income: AdvanceIncome(p, m.Merch.BonusVal); Log($"{p.Name} advances income {m.Merch.BonusVal} spaces ({m.Merch.Name} bonus)"); break;
            case BonusKind.Develop:
                var opts = Enum.GetValues<Industry>()
                    .Select(i => p.NextTile(i)).Where(t => t != null && !t.Bulb).Cast<TileSpec>().ToList();
                if (opts.Count == 0) { Log($"{p.Name}: no tile to develop (Gloucester bonus wasted)"); break; }
                var pick = opts[ch.Pick("Gloucester bonus: develop (remove) which tile?", opts.Select(t => t.ToString()).ToList())];
                p.Mat[pick.Ind].Remove(pick);
                Log($"{p.Name} develops {pick} for free ({m.Merch.Name} bonus)");
                break;
        }
    }

    public void AdvanceIncome(Player p, int spaces) => p.IncomeSpace = Math.Min(99, p.IncomeSpace + spaces);

    void Pay(Player p, int amount) { p.Money -= amount; p.Spent += amount; }

    // ---------- BUILD ----------

    public List<BuildOpt> BuildOptions(Player p, Card card)
    {
        var res = new List<BuildOpt>();
        var network = NetworkOf(p);
        bool emptyBoard = HasNothingOnBoard(p);

        foreach (var loc in Data.Locations)
        {
            Industry[]? allowedInds = card switch
            {
                LocationCard lc => lc.Loc == loc.Name ? Enum.GetValues<Industry>() : null,
                WildLocationCard => loc.Farm ? null : Enum.GetValues<Industry>(),
                IndustryCard ic => (network.Contains(loc.Name) || emptyBoard) &&
                                   (!loc.Farm || ic.Inds.Contains(Industry.Brewery)) ? ic.Inds : null,
                WildIndustryCard => network.Contains(loc.Name) || emptyBoard ? Enum.GetValues<Industry>() : null,
                _ => null
            };
            if (allowedInds == null) continue;

            foreach (var ind in allowedInds.Distinct())
            {
                var spec = p.NextTile(ind);
                if (spec == null) continue;
                if (Era == Era.Canal && spec.RailOnly) continue;
                if (Era == Era.Rail && spec.CanalOnly) continue;

                // empty undeveloped slots (single-icon slots take priority within the location)
                var empty = Enumerable.Range(0, loc.Slots.Length)
                    .Where(si => loc.Slots[si].Allowed.Contains(ind) && !Tiles.Any(t => t.Loc == loc.Name && t.SlotIdx == si))
                    .ToList();
                var singles = empty.Where(si => loc.Slots[si].Allowed.Length == 1).ToList();
                if (singles.Count > 0) empty = singles;

                bool canalLimit = Era == Era.Canal && Tiles.Any(t => t.Owner == p && t.Loc == loc.Name);

                foreach (var si in empty)
                {
                    if (canalLimit) break;
                    AddIfAffordable(res, p, loc, si, spec, null);
                }

                // overbuild
                foreach (var t in Tiles.Where(t => t.Loc == loc.Name && t.Spec.Ind == ind && t.Spec.Level < spec.Level))
                {
                    if (t.Owner != p)
                    {
                        if (ind != Industry.Coal && ind != Industry.Iron) continue;
                        bool cubesExist = ind == Industry.Coal
                            ? CoalMarket.Sum() > 0 || Tiles.Any(x => x.Spec.Ind == Industry.Coal && x.Cubes > 0)
                            : IronMarket.Sum() > 0 || Tiles.Any(x => x.Spec.Ind == Industry.Iron && x.Cubes > 0);
                        if (cubesExist) continue;
                        if (Era == Era.Canal && Tiles.Any(x => x.Owner == p && x.Loc == loc.Name)) continue;
                    }
                    AddIfAffordable(res, p, loc, t.SlotIdx, spec, t);
                }
            }
        }
        return res;
    }

    void AddIfAffordable(List<BuildOpt> res, Player p, LocDef loc, int si, TileSpec spec, PlacedTile? over)
    {
        int rc = ResourceCost(loc.Name, spec.Coal, spec.Iron);
        if (rc < 0) return;
        int total = spec.Cost + rc;
        if (p.Money >= total) res.Add(new BuildOpt(loc, si, spec, over, total));
    }

    public void ExecBuild(Player p, Card card, BuildOpt o, IChooser ch)
    {
        DiscardForAction(p, card);
        if (o.Over != null)
        {
            Tiles.Remove(o.Over);
            Log($"{p.Name} overbuilds {o.Over.Owner.Name}'s {o.Over.Spec} in {o.Loc.Name}");
        }
        Pay(p, o.Spec.Cost);
        ConsumeCoal(p, o.Loc.Name, o.Spec.Coal, ch);
        ConsumeIron(p, o.Spec.Iron, ch);
        p.Mat[o.Spec.Ind].Remove(o.Spec);

        int cubes = o.Spec.Ind == Industry.Brewery ? (Era == Era.Canal ? 1 : 2) : o.Spec.Produces;
        var t = new PlacedTile { Owner = p, Spec = o.Spec, Loc = o.Loc.Name, SlotIdx = o.SlotIdx, Cubes = cubes };
        Tiles.Add(t);
        Log($"{p.Name} builds {o.Spec} in {o.Loc.Name} (£{o.MoneyCost})");

        // sell new coal/iron cubes to the market
        if (o.Spec.Ind == Industry.Iron || (o.Spec.Ind == Industry.Coal && ConnectedToMerchant(o.Loc.Name)))
        {
            var mkt = o.Spec.Ind == Industry.Iron ? IronMarket : CoalMarket;
            int max = o.Spec.Ind == Industry.Iron ? 5 : 7;
            int gained = 0;
            for (int pr = max; pr >= 1 && t.Cubes > 0; pr--)
                while (mkt[pr] < 2 && t.Cubes > 0) { mkt[pr]++; t.Cubes--; gained += pr; }
            if (gained > 0) { p.Money += gained; Log($"{p.Name} sells cubes to the market for £{gained}"); }
            if (t.Cubes == 0) FlipProducer(t);
        }
    }

    // ---------- NETWORK ----------

    public int LinkCost(bool second) => Era == Era.Canal ? 3 : second ? 10 : 5;

    public List<LinkDef> NetworkOptions(Player p, bool second)
    {
        var res = new List<LinkDef>();
        if (p.LinksLeft == 0) return res;
        var network = NetworkOf(p);
        bool anywhere = HasNothingOnBoard(p);
        foreach (var def in Data.Links)
        {
            if (Era == Era.Canal ? !def.Canal : !def.Rail) continue;
            if (Links.Any(l => l.Def == def)) continue;
            if (!anywhere && !def.Locs.Any(network.Contains)) continue;
            int cost = LinkCost(second);
            if (Era == Era.Rail)
            {
                int rc = ResourceCost(def.Locs[0], 1, 0, pretend: def);
                if (rc < 0) continue;
                cost += rc;
                if (second && !BeerForRail(p, def)) continue;
            }
            if (p.Money >= cost) res.Add(def);
        }
        return res;
    }

    bool BeerForRail(Player p, LinkDef def) =>
        Tiles.Any(t => t.Spec.Ind == Industry.Brewery && !t.Flipped && t.Cubes > 0 &&
            (t.Owner == p || def.Locs.Any(l => Connected(l, t.Loc, pretend: def))));

    public void ExecNetwork(Player p, LinkDef def, bool second, IChooser ch)
    {
        Pay(p, LinkCost(second));
        Links.Add(new PlacedLink { Def = def, Owner = p });
        p.LinksLeft--;
        Log($"{p.Name} builds a {(Era == Era.Canal ? "canal" : "rail")} link {def}");
        if (Era == Era.Rail)
        {
            ConsumeCoal(p, def.Locs[0], 1, ch, pretend: null); // link now placed, normal connectivity
            if (second)
            {
                var sources = Tiles.Where(t => t.Spec.Ind == Industry.Brewery && !t.Flipped && t.Cubes > 0 &&
                        (t.Owner == p || def.Locs.Any(l => Connected(l, t.Loc)))).ToList();
                var pick = sources.Count == 1 ? sources[0]
                    : sources[ch.Pick("Consume beer from which brewery?", sources.Select(b => $"{b.Owner.Name}'s brewery in {b.Loc} ({b.Cubes} beer)").ToList())];
                pick.Cubes--;
                if (pick.Cubes == 0) FlipProducer(pick);
            }
        }
    }

    // ---------- SELL ----------

    public List<SellableTile> SellOptions(Player p)
    {
        var res = new List<SellableTile>();
        foreach (var t in Tiles.Where(t => t.Owner == p && !t.Flipped &&
                     t.Spec.Ind is Industry.Cotton or Industry.Manufacturer or Industry.Pottery))
        {
            var dist = Distances(t.Loc);
            var merchants = MerchantSlots
                .Where(m => m.Accepts(t.Spec.Ind) && dist.ContainsKey(m.Merch.Name))
                .Where(m => BeerSources(p, t.Loc, m).Sum(s => s.Brewery?.Cubes ?? 1) >= t.Spec.Beer)
                .ToList();
            if (merchants.Count > 0) res.Add(new SellableTile(t, merchants));
        }
        return res;
    }

    public void ExecSell(Player p, PlacedTile t, MerchantSlot m, IChooser ch)
    {
        for (int i = 0; i < t.Spec.Beer; i++) ConsumeBeer(p, t.Loc, m, ch);
        t.Flipped = true;
        AdvanceIncome(p, t.Spec.Income);
        Log($"{p.Name} sells {t.Spec} in {t.Loc} to {m.Label} (+{t.Spec.Income} income spaces)");
    }

    // ---------- DEVELOP / LOAN / SCOUT / PASS ----------

    public List<TileSpec> DevelopOptions(Player p) =>
        Enum.GetValues<Industry>().Select(i => p.NextTile(i)).Where(t => t != null && !t.Bulb).Cast<TileSpec>().ToList();

    public bool CanDevelop(Player p, int count) =>
        DevelopOptions(p).Count > 0 &&
        p.Money >= (IronAvailableFree() >= count ? 0 : MarketIronCost(count - IronAvailableFree()));

    public void ExecDevelop(Player p, TileSpec spec, IChooser ch)
    {
        ConsumeIron(p, 1, ch);
        p.Mat[spec.Ind].Remove(spec);
        Log($"{p.Name} develops {spec}");
    }

    public bool CanLoan(Player p) => p.IncomeLevel - 3 >= -10;

    public void ExecLoan(Player p)
    {
        p.Money += 30;
        p.IncomeSpace = Data.HighestSpaceOfLevel(p.IncomeLevel - 3);
        Log($"{p.Name} takes a £30 loan (income level now {p.IncomeLevel})");
    }

    public bool CanScout(Player p) => p.Hand.Count >= 3 && !p.Hand.Any(c => c is WildLocationCard or WildIndustryCard)
        && WildLocSupply > 0 && WildIndSupply > 0;

    public void ExecScout(Player p, Card extra1, Card extra2)
    {
        DiscardFromHand(p, extra1);
        DiscardFromHand(p, extra2);
        p.Hand.Add(new WildLocationCard()); WildLocSupply--;
        p.Hand.Add(new WildIndustryCard()); WildIndSupply--;
        Log($"{p.Name} scouts (takes Wild Location + Wild Industry)");
    }

    // ---------- turn / round / era flow ----------

    public void DiscardForAction(Player p, Card card) => DiscardFromHand(p, card);

    void DiscardFromHand(Player p, Card card)
    {
        p.Hand.Remove(card);
        if (card is WildLocationCard) WildLocSupply++;
        else if (card is WildIndustryCard) WildIndSupply++;
        else Discards.Add(card);
    }

    public void SpendAction(Player p, Card card)
    {
        DiscardForAction(p, card);
        ActionsLeft--;
    }

    // Called by ExecBuild internally; other actions call SpendAction with their card.
    public void ActionDone() => ActionsLeft--;

    public bool TurnOver => ActionsLeft <= 0 || Current.Hand.Count == 0;

    // Advances to next player; returns true if a new round began, "gameOver" via flag.
    public void EndTurn(IChooser ch)
    {
        var p = Current;
        while (p.Hand.Count < 8 && Deck.Count > 0) DrawTo(p);
        TurnIdx++;
        if (TurnIdx >= Order.Count)
        {
            EndRound(ch);
            return;
        }
        ActionsLeft = Era == Era.Canal && Round == 1 ? 1 : 2;
    }

    void EndRound(IChooser ch)
    {
        // turn order: least spent first (stable)
        Order = Order.OrderBy(p => p.Spent).ToList();
        TurnIdx = 0; // keep Current valid even once the game ends

        bool eraDone = Deck.Count == 0 && Players.All(p => p.Hand.Count == 0);
        bool finalRound = eraDone && Era == Era.Rail;

        if (!finalRound)
            foreach (var p in Players)
            {
                int lvl = p.IncomeLevel;
                p.Money += lvl;
                if (lvl != 0) Log($"{p.Name} {(lvl > 0 ? "collects" : "pays")} £{Math.Abs(lvl)} income");
                while (p.Money < 0)
                {
                    var own = Tiles.Where(t => t.Owner == p).ToList();
                    if (own.Count == 0)
                    {
                        int loss = Math.Min(p.VP, -p.Money);
                        p.VP -= loss;
                        Log($"{p.Name} cannot pay: loses {loss} VP");
                        p.Money = 0;
                        break;
                    }
                    var pch = ChooserFor?.Invoke(p) ?? ch;
                    var pick = own[pch.Pick($"{p.Name}: shortfall £{-p.Money}. Remove which industry (worth half cost)?",
                        own.Select(t => $"{t.Spec} in {t.Loc} (+£{t.Spec.Cost / 2})").ToList())];
                    Tiles.Remove(pick);
                    p.Money += pick.Spec.Cost / 2;
                    Log($"{p.Name} removes {pick.Spec} in {pick.Loc} to cover shortfall");
                }
            }

        foreach (var p in Players) p.Spent = 0;

        if (eraDone) { EndEra(); return; }

        Round++;
        TurnIdx = 0;
        ActionsLeft = Era == Era.Canal && Round == 1 ? 1 : 2;
        Log($"--- Round {Round} ({Era} Era) --- order: {string.Join(", ", Order.Select(o => o.Name))}");
    }

    void EndEra()
    {
        Log($"=== End of {Era} Era: scoring ===");
        foreach (var l in Links.ToList())
        {
            int vp = l.Def.Locs.Sum(loc => Data.IsMerchant(loc) ? 2
                : Tiles.Where(t => t.Loc == loc && t.Flipped).Sum(t => Data.LinkPoints.TryGetValue((t.Spec.Ind, t.Spec.Level), out var lp) ? lp : 0));
            l.Owner.VP += vp;
            Log($"{l.Owner.Name} scores {vp} VP for link {l.Def}");
        }
        Links.Clear();
        foreach (var t in Tiles.Where(t => t.Flipped))
        {
            t.Owner.VP += t.Spec.VP;
            Log($"{t.Owner.Name} scores {t.Spec.VP} VP for {t.Spec} in {t.Loc}");
        }

        if (Era == Era.Canal)
        {
            Tiles.RemoveAll(t => t.Spec.Level == 1);
            foreach (var m in MerchantSlots.Where(m => m.Good != Good.Blank)) m.HasBeer = true;
            Deck = Data.BuildDeck(PlayerCount);
            Shuffle(Deck);
            Discards.Clear();
            foreach (var p in Players)
            {
                p.Hand.Clear();
                p.LinksLeft = 14;   // ponytail: rail era gets its own 14-link budget, distinct from canal era
                for (int i = 0; i < 8; i++) DrawTo(p);
            }
            Era = Era.Rail;
            Round = 1;
            TurnIdx = 0;
            ActionsLeft = 2;
            Log($"=== Rail Era begins === order: {string.Join(", ", Order.Select(o => o.Name))}");
        }
        else
        {
            GameOver = true;
            var ranked = Players.OrderByDescending(p => p.VP).ThenByDescending(p => p.IncomeLevel)
                .ThenByDescending(p => p.Money).ToList();
            Log($"=== GAME OVER === Winner: {ranked[0].Name}");
            foreach (var p in ranked)
                Log($"{p.Name}: {p.VP} VP, income {p.IncomeLevel}, £{p.Money}");
        }
    }
}
