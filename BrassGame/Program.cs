namespace BrassGame;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selftest") { SelfTest.Run(); return; }
        ApplicationConfiguration.Initialize();
        if (args.Length > 0 && args[0] == "--demo")
        {
            Application.Run(new MainForm(new() { ("Alice", "Red"), ("Bob", "Yellow") }));
            return;
        }
        using var setup = new SetupForm();
        if (setup.ShowDialog() != DialogResult.OK) return;
        Application.Run(new MainForm(setup.PlayerNames));
    }
}

// ponytail: random-bot smoke test — plays full games headless, writes selftest.log
static class SelfTest
{
    class RandomChooser : IChooser
    {
        readonly Random r;
        public RandomChooser(Random r) => this.r = r;
        public int Pick(string prompt, IList<string> options) => r.Next(options.Count);
    }

    public static void Run()
    {
        var log = new StreamWriter(Path.Combine(AppContext.BaseDirectory, "selftest.log"));
        int failures = 0;
        for (int seed = 1; seed <= 5; seed++)
        {
            foreach (int nPlayers in new[] { 2, 3, 4 })
            {
                try
                {
                    PlayGame(seed * 100 + nPlayers, nPlayers, log);
                    log.WriteLine($"seed {seed} {nPlayers}p: OK");
                }
                catch (Exception ex)
                {
                    failures++;
                    log.WriteLine($"seed {seed} {nPlayers}p: FAIL {ex}");
                }
            }
        }
        log.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURES");
        log.Close();
    }

    static void PlayGame(int seed, int nPlayers, StreamWriter log)
    {
        var rng = new Random(seed);
        var ch = new RandomChooser(rng);
        var colors = new[] { "Red", "Yellow", "Purple", "Gray" };
        var g = new Game(Enumerable.Range(0, nPlayers).Select(i => ($"P{i + 1}", colors[i])), seed);
        int guard = 0;
        while (!g.GameOver)
        {
            if (++guard > 20000) throw new Exception("game did not terminate");
            var p = g.Current;
            if (g.TurnOver) { g.EndTurn(ch); continue; }
            DoRandomAction(g, p, ch, rng);
            if (p.Money < 0) throw new Exception($"{p.Name} money negative: {p.Money}");
        }
        var total = g.Players.Sum(p => p.VP);
        log.WriteLine($"  seed {seed}: {nPlayers}p done, total VP {total}, winner VP {g.Players.Max(p => p.VP)}");
        if (total <= 0) throw new Exception("no VP scored at all — engine suspect");
    }

    static void DoRandomAction(Game g, Player p, IChooser ch, Random rng)
    {
        // try actions in random priority until one works; pass as fallback
        var order = new[] { "build", "network", "sell", "develop", "loan", "scout" }.OrderBy(_ => rng.Next()).ToList();
        foreach (var act in order)
        {
            switch (act)
            {
                case "build":
                    foreach (var card in p.Hand.OrderBy(_ => rng.Next()).ToList())
                    {
                        var opts = g.BuildOptions(p, card);
                        if (opts.Count == 0) continue;
                        g.ExecBuild(p, card, opts[rng.Next(opts.Count)], ch);
                        g.ActionDone();
                        return;
                    }
                    break;
                case "network":
                    var links = g.NetworkOptions(p, false);
                    if (links.Count > 0)
                    {
                        g.SpendAction(p, p.Hand[rng.Next(p.Hand.Count)]);
                        g.ExecNetwork(p, links[rng.Next(links.Count)], false, ch);
                        if (g.Era == Era.Rail && rng.Next(2) == 0)
                        {
                            var second = g.NetworkOptions(p, true);
                            if (second.Count > 0) g.ExecNetwork(p, second[rng.Next(second.Count)], true, ch);
                        }
                        return;
                    }
                    break;
                case "sell":
                    var sells = g.SellOptions(p);
                    if (sells.Count > 0)
                    {
                        g.SpendAction(p, p.Hand[rng.Next(p.Hand.Count)]);
                        while (sells.Count > 0)
                        {
                            var s = sells[rng.Next(sells.Count)];
                            g.ExecSell(p, s.Tile, s.Merchants[rng.Next(s.Merchants.Count)], ch);
                            if (rng.Next(2) == 0) break;
                            sells = g.SellOptions(p);
                        }
                        return;
                    }
                    break;
                case "develop":
                    if (g.CanDevelop(p, 1) && rng.Next(3) == 0)
                    {
                        g.SpendAction(p, p.Hand[rng.Next(p.Hand.Count)]);
                        var dev = g.DevelopOptions(p);
                        g.ExecDevelop(p, dev[rng.Next(dev.Count)], ch);
                        return;
                    }
                    break;
                case "loan":
                    if (g.CanLoan(p) && p.Money < 10)
                    {
                        g.SpendAction(p, p.Hand[rng.Next(p.Hand.Count)]);
                        g.ExecLoan(p);
                        return;
                    }
                    break;
                case "scout":
                    if (g.CanScout(p) && rng.Next(6) == 0)
                    {
                        var card = p.Hand[rng.Next(p.Hand.Count)];
                        g.SpendAction(p, card);
                        var rest = p.Hand.OrderBy(_ => rng.Next()).Take(2).ToList();
                        g.ExecScout(p, rest[0], rest[1]);
                        return;
                    }
                    break;
            }
        }
        g.SpendAction(p, p.Hand[rng.Next(p.Hand.Count)]); // pass
    }
}
