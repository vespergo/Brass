using System.Drawing.Drawing2D;

namespace BrassGame;

public class SetupForm : Form
{
    public List<(string Name, string Color)> PlayerNames = new();
    const int CoverH = 170; // top space reserved for the cover image
    readonly NumericUpDown num = new() { Minimum = 2, Maximum = 4, Value = 2, Left = 150, Top = 12 + CoverH, Width = 60 };
    readonly TextBox[] names = new TextBox[4];
    static readonly string[] Colors = { "Red", "Yellow", "Purple", "Gray" };

    public SetupForm()
    {
        Text = "Brass: Birmingham — New Game";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(320, 230 + CoverH);
        MaximizeBox = false;

        // Cover image lives in img/ next to the binary.
        var cover = Image.FromFile(Path.Combine(AppContext.BaseDirectory, "img", "cover.jpg"));
        var pic = new PictureBox { Dock = DockStyle.Top, Height = CoverH, SizeMode = PictureBoxSizeMode.Zoom, Image = cover };
        Controls.Add(pic);

        Controls.Add(new Label { Text = "Players:", Left = 12, Top = 15 + CoverH, Width = 130 });
        Controls.Add(num);
        for (int i = 0; i < 4; i++)
        {
            Controls.Add(new Label { Text = $"{Colors[i]}:", Left = 12, Top = 50 + i * 30 + CoverH, Width = 130 });
            names[i] = new TextBox { Left = 150, Top = 47 + i * 30 + CoverH, Width = 150, Text = $"Player {i + 1}" };
            Controls.Add(names[i]);
        }
        var ok = new Button { Text = "Start", Left = 150, Top = 180 + CoverH, Width = 80, DialogResult = DialogResult.OK };
        ok.Click += (_, _) =>
        {
            for (int i = 0; i < (int)num.Value; i++)
                PlayerNames.Add((names[i].Text.Trim().Length > 0 ? names[i].Text.Trim() : $"Player {i + 1}", Colors[i]));
        };
        Controls.Add(ok);
        AcceptButton = ok;
        num.ValueChanged += (_, _) => { for (int i = 0; i < 4; i++) names[i].Enabled = i < num.Value; };
        names[2].Enabled = names[3].Enabled = false;
    }
}

// Simple list-choice dialog used for all in-game decisions.
public class ChoiceForm : Form
{
    readonly ListBox list = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10) };
    public int Choice => list.SelectedIndex;

    ChoiceForm(string prompt, IList<string> options, bool cancellable)
    {
        Text = "Brass";
        ClientSize = new Size(460, Math.Min(500, 120 + options.Count * 22));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        var lbl = new Label { Text = prompt, Dock = DockStyle.Top, Height = 44, Padding = new Padding(8), Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        var pnl = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var ok = new Button { Text = "OK", Left = 260, Top = 8, Width = 85, DialogResult = DialogResult.OK, Enabled = false };
        pnl.Controls.Add(ok);
        if (cancellable)
            pnl.Controls.Add(new Button { Text = "Cancel", Left = 355, Top = 8, Width = 85, DialogResult = DialogResult.Cancel });
        foreach (var o in options) list.Items.Add(o);
        list.SelectedIndexChanged += (_, _) => ok.Enabled = list.SelectedIndex >= 0;
        list.DoubleClick += (_, _) => { if (list.SelectedIndex >= 0) DialogResult = DialogResult.OK; };
        Controls.Add(list); Controls.Add(lbl); Controls.Add(pnl);
        AcceptButton = ok;
    }

    public static int Show(IWin32Window owner, string prompt, IList<string> options, bool cancellable = false)
    {
        using var f = new ChoiceForm(prompt, options, cancellable);
        return f.ShowDialog(owner) == DialogResult.OK ? f.Choice : -1;
    }
}

public class DialogChooser : IChooser
{
    readonly Form owner;
    public DialogChooser(Form owner) => this.owner = owner;
    public int Pick(string prompt, IList<string> options)
    {
        if (options.Count == 1) return 0;
        int r;
        do { r = ChoiceForm.Show(owner, prompt, options); } while (r < 0); // mandatory choice
        return r;
    }
}

public class MatForm : Form
{
    public MatForm(Player p)
    {
        Text = $"{p.Name}'s Mat ({p.ColorName})";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        var matImg = Image.FromFile(Path.Combine(AppContext.BaseDirectory, "img", "player_mat.jpg"));
        ClientSize = new Size(matImg.Width, matImg.Height);
        Controls.Add(new MatPanel { Dock = DockStyle.Fill, MatImg = matImg, Player = p, BackColor = Color.Black });
    }
}

class MatPanel : Panel
{
    public Image? MatImg;
    public Player? Player;
    static readonly Font badgeFont = new Font("Segoe UI", 9, FontStyle.Bold);
    public MatPanel() => DoubleBuffered = true;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        if (MatImg == null || Player == null) return;
        g.DrawImage(MatImg, 0, 0, MatImg.Width, MatImg.Height);

        float tile = 84f; // native display size of one tile thumbnail
        foreach (Industry ind in Enum.GetValues<Industry>())
        {
            var specs = Player.Mat[ind];
            foreach (var grp in specs.GroupBy(s => s.Level))
            {
                if (!Data.MatSlots.TryGetValue((ind, grp.Key), out var pos)) continue;
                var img = MainForm.TileImage(Player.ColorName, ind, grp.Key, false);
                if (img == null) continue;
                float cx = pos.X, cy = pos.Y;
                var r = new RectangleF(cx - tile / 2, cy - tile / 2, tile, tile);
                g.DrawImage(img, r);
                if (grp.Count() > 1)
                    g.DrawString($"×{grp.Count()}", badgeFont, Brushes.White, r.Right - 20, r.Bottom - 18);
            }
        }
    }
}

public class MainForm : Form
{
    enum Mode { Cover, Idle, BuildPick, NetworkPick, SellPick }

    readonly Game game;
    readonly DialogChooser chooser;
    readonly Image board;
    readonly Panel boardPanel;
    readonly ListBox lstHand = new() { Font = new Font("Segoe UI", 10) };
    readonly ListBox lstLog = new() { Font = new Font("Segoe UI", 8.5f), IntegralHeight = false };
    readonly Label lblStatus = new() { Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = false };
    readonly Label lblPlayers = new() { Font = new Font("Consolas", 9.5f), AutoSize = false };
    readonly Button btnBuild = new() { Text = "Build" }, btnNet = new() { Text = "Network" }, btnSell = new() { Text = "Sell" },
        btnDev = new() { Text = "Develop" }, btnLoan = new() { Text = "Loan" }, btnScout = new() { Text = "Scout" },
        btnPass = new() { Text = "Pass" }, btnCancel = new() { Text = "Cancel" }, btnStart = new() { Text = "Start turn" },
        btnMat = new() { Text = "Player Mat" };

    Mode mode = Mode.Cover;
    List<BuildOpt> buildOpts = new();
    Card? buildCard;
    List<LinkDef> netOpts = new();
    List<SellableTile> sellOpts = new();
    bool soldAny;

    static readonly Dictionary<string, Color> PlayerColors = new()
    {
        ["Red"] = Color.FromArgb(200, 40, 40),
        ["Yellow"] = Color.FromArgb(220, 180, 20),
        ["Purple"] = Color.FromArgb(140, 60, 170),
        ["Gray"] = Color.FromArgb(130, 130, 130),
    };

    // Tile art lives in img/ next to the binary: {Color}_{Industry}_{Level}[_back].jpg
    static readonly Dictionary<(string, Industry, int, bool), Image?> tileImgCache = new();
    static string IndFile(Industry i) => i switch
    {
        Industry.Cotton => "Cotton", Industry.Coal => "Coal", Industry.Iron => "Iron",
        Industry.Manufacturer => "Manufacture", Industry.Pottery => "Pottery", _ => "Beer"
    };
    public static Image? TileImage(string color, Industry ind, int level, bool flipped)
    {
        var key = (color, ind, level, flipped);
        if (tileImgCache.TryGetValue(key, out var img)) return img;
        string path = Path.Combine(AppContext.BaseDirectory, "img", $"{color}_{IndFile(ind)}_{level}{(flipped ? "_back" : "")}.jpg");
        img = File.Exists(path) ? Image.FromFile(path) : null;
        tileImgCache[key] = img;
        return img;
    }

    // Coal market cube art: img/coal_cube.png. Positions are board-space coords,
    // price £1 (bottom row) first; two cube slots per price level (£1..£7).
    static readonly (int X, int Y)[][] CoalMarketPos =
    {
        new[] { (1683, 1036), (1726, 1036) }, // £1
        new[] { (1684, 974),  (1726, 974)  }, // £2
        new[] { (1684, 915),  (1725, 916)  }, // £3
        new[] { (1683, 856),  (1725, 856)  }, // £4
        new[] { (1683, 798),  (1726, 798)  }, // £5
        new[] { (1682, 739),  (1725, 739)  }, // £6
        new[] { (1680, 680),  (1725, 679)  }, // £7
    };
    static Image? coalCubeImg;
    public static Image? CoalCubeImage()
    {
        if (coalCubeImg != null) return coalCubeImg;
        string path = Path.Combine(AppContext.BaseDirectory, "img", "coal_cube.png");
        coalCubeImg = File.Exists(path) ? Image.FromFile(path) : null;
        return coalCubeImg;
    }

    // Iron market cube art: img/iron_cube.png. Positions are board-space coords,
    // price £1 (bottom row) first; two cube slots per price level (£1..£5).
    static readonly (int X, int Y)[][] IronMarketPos =
    {
        new[] { (1791, 1035), (1834, 1035) }, // £1
        new[] { (1791, 975),  (1834, 975)  }, // £2
        new[] { (1791, 917),  (1836, 917)  }, // £3
        new[] { (1791, 858),  (1836, 857)  }, // £4
        new[] { (1791, 798),  (1834, 800)  }, // £5
    };
    static Image? ironCubeImg;
    public static Image? IronCubeImage()
    {
        if (ironCubeImg != null) return ironCubeImg;
        string path = Path.Combine(AppContext.BaseDirectory, "img", "iron_cube.png");
        ironCubeImg = File.Exists(path) ? Image.FromFile(path) : null;
        return ironCubeImg;
    }

    // Player portrait art: img/Player_<color>.png. Turn-order track slots (board space).
    static readonly (int X, int Y)[] TurnOrderPos =
    {
        (213, 1338), (213, 1494), (213, 1655), (213, 1816),
    };

    // Victory point track: 100 spaces running clockwise around the board border,
    // starting bottom-left (space 0) up the left edge. Player.VP wraps via % 100.
    static readonly (int X, int Y)[] VpTrackPos =
    {
        (42, 1847), (42, 1775), (42, 1704), (42, 1634), (42, 1561),
        (42, 1494), (43, 1422), (42, 1350), (42, 1280), (42, 1208),
        (42, 1136), (40, 1055), (41, 998),  (42, 915),  (41, 857),
        (41, 772),  (42, 713),  (41, 632),  (42, 576),  (42, 490),
        (42, 433),  (42, 351),  (42, 294),  (42, 209),  (41, 153),
        (141, 42),  (197, 44),  (279, 42),  (336, 42),  (423, 42),
        (479, 43),  (585, 43),  (642, 42),  (698, 42),  (816, 42),
        (870, 42),  (928, 42),  (1045, 42), (1102, 42), (1158, 42),
        (1272, 40), (1329, 42), (1387, 42), (1508, 42), (1567, 42),
        (1623, 44), (1735, 42), (1793, 42), (1850, 42), (1957, 176),
        (1959, 233), (1959, 289), (1959, 403), (1958, 461), (1957, 517),
        (1959, 633), (1958, 691), (1960, 748), (1959, 864), (1959, 918),
        (1959, 976), (1958, 1094), (1958, 1150), (1957, 1205), (1956, 1262),
        (1957, 1375), (1955, 1429), (1957, 1488), (1956, 1544), (1958, 1655),
        (1958, 1713), (1959, 1766), (1959, 1824), (1854, 1957), (1799, 1955),
        (1742, 1957), (1685, 1957), (1587, 1958), (1531, 1957), (1475, 1955),
        (1419, 1956), (1321, 1957), (1264, 1955), (1208, 1955), (1152, 1956),
        (1059, 1955), (1003, 1955), (946, 1955),  (889, 1955),  (791, 1956),
        (736, 1957),  (678, 1956),  (624, 1956),  (522, 1958),  (465, 1958),
        (409, 1957),  (353, 1958),  (258, 1957),  (202, 1958),  (144, 1955),
    };
    static readonly Dictionary<string, Image?> playerImgCache = new();
    public static Image? PlayerImage(string color)
    {
        if (playerImgCache.TryGetValue(color, out var img)) return img;
        string path = Path.Combine(AppContext.BaseDirectory, "img", $"Player_{color}.png");
        img = File.Exists(path) ? Image.FromFile(path) : null;
        playerImgCache[color] = img;
        return img;
    }

    // VP track marker art: img/Player_<color>_Point.png
    static readonly Dictionary<string, Image?> playerPointImgCache = new();
    public static Image? PlayerPointImage(string color)
    {
        if (playerPointImgCache.TryGetValue(color, out var img)) return img;
        string path = Path.Combine(AppContext.BaseDirectory, "img", $"Player_{color}_Point.png");
        img = File.Exists(path) ? Image.FromFile(path) : null;
        playerPointImgCache[color] = img;
        return img;
    }

    // Income marker art: img/<Color>_Income.png (shares the VP track spaces)
    static readonly Dictionary<string, Image?> playerIncomeImgCache = new();
    public static Image? PlayerIncomeImage(string color)
    {
        if (playerIncomeImgCache.TryGetValue(color, out var img)) return img;
        string path = Path.Combine(AppContext.BaseDirectory, "img", $"{color}_Income.png");
        img = File.Exists(path) ? Image.FromFile(path) : null;
        playerIncomeImgCache[color] = img;
        return img;
    }

    // Merchant tile art: img/Merchant_<Good>.png
    static readonly Dictionary<Good, Image?> merchImgCache = new();
    public static Image? MerchantImage(Good g)
    {
        if (merchImgCache.TryGetValue(g, out var img)) return img;
        string path = Path.Combine(AppContext.BaseDirectory, "img", $"Merchant_{g}.png");
        img = File.Exists(path) ? Image.FromFile(path) : null;
        merchImgCache[g] = img;
        return img;
    }

    public MainForm(List<(string Name, string Color)> players)
    {
        Text = "Brass: Birmingham";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1200, 800);
        chooser = new DialogChooser(this);

        board = Image.FromFile(Path.Combine(AppContext.BaseDirectory, "img", "main_board.jpg"));

        game = new Game(players);
        game.Log = s => { lstLog.Items.Add(s); lstLog.TopIndex = lstLog.Items.Count - 1; };

        boardPanel = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(25, 25, 30) };
        boardPanel.Paint += PaintBoard;
        boardPanel.MouseClick += BoardClick;
        boardPanel.Resize += (_, _) => boardPanel.Invalidate();

        var side = new Panel { Dock = DockStyle.Right, Width = 400, Padding = new Padding(6) };
        lblPlayers.SetBounds(6, 6, 386, 118);
        lblStatus.SetBounds(6, 128, 386, 58);
        lstHand.SetBounds(6, 190, 386, 150);
        int bx = 6, by = 348;
        var btns = new[] { btnBuild, btnNet, btnSell, btnDev, btnLoan, btnScout, btnPass, btnCancel };
        for (int i = 0; i < btns.Length; i++)
        {
            btns[i].SetBounds(bx + (i % 4) * 96, by + (i / 4) * 34, 92, 30);
            side.Controls.Add(btns[i]);
        }
        btnMat.SetBounds(bx, by + 68, 92, 30);
        btnStart.SetBounds(6, 190, 386, 60);
        btnStart.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        lstLog.SetBounds(6, 458, 386, 486);
        side.Controls.AddRange(new Control[] { lblPlayers, lblStatus, lstHand, btnStart, btnMat, lstLog });
        side.Resize += (_, _) => lstLog.Height = side.ClientSize.Height - 464;

        Controls.Add(boardPanel);
        Controls.Add(side);

        btnBuild.Click += (_, _) => StartBuild();
        btnNet.Click += (_, _) => StartNetwork();
        btnSell.Click += (_, _) => StartSell();
        btnDev.Click += (_, _) => DoDevelop();
        btnLoan.Click += (_, _) => DoLoan();
        btnScout.Click += (_, _) => DoScout();
        btnPass.Click += (_, _) => DoPass();
        btnCancel.Click += (_, _) => CancelMode();
        btnStart.Click += (_, _) => { mode = Mode.Idle; Refresh_(); };
        btnMat.Click += (_, _) => new MatForm(game.Current).ShowDialog(this);

        game.Log($"--- Round 1 (Canal Era) --- order: {string.Join(", ", game.Order.Select(o => o.Name))} (1 action each this round)");
        Refresh_();
    }

    class DoubleBufferedPanel : Panel { public DoubleBufferedPanel() => DoubleBuffered = true; }

    Card? SelectedCard => lstHand.SelectedIndex >= 0 && lstHand.SelectedIndex < game.Current.Hand.Count
        ? game.Current.Hand[lstHand.SelectedIndex] : null;

    // ---------- rendering ----------

    float Sc => Math.Min(boardPanel.ClientSize.Width, boardPanel.ClientSize.Height) / 2000f;
    PointF Map(int x, int y) => new(x * Sc, y * Sc);
    RectangleF SlotRect(int x, int y) { float s = 88 * Sc; var p = Map(x, y); return new RectangleF(p.X - s / 2, p.Y - s / 2, s, s); }
    RectangleF LinkRect(LinkDef d) { float w = 64 * Sc, h = 40 * Sc; var p = Map(d.X, d.Y); return new RectangleF(p.X - w / 2, p.Y - h / 2, w, h); }

    void PaintBoard(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int size = (int)(2000 * Sc);
        g.DrawImage(board, 0, 0, size, size);

        using var slotPen = new Pen(Color.FromArgb(90, Color.White), 1.5f);
        using var hlPen = new Pen(Color.Gold, 3.5f);
        var fontSmall = new Font("Segoe UI", Math.Max(6f, 11 * Sc * 2), FontStyle.Bold);

        // links
        foreach (var def in Data.Links)
        {
            if (game.Era == Era.Canal ? !def.Canal : !def.Rail)
                if (!game.Links.Any(l => l.Def == def)) continue;
            var r = LinkRect(def);
            var placed = game.Links.FirstOrDefault(l => l.Def == def);
            if (placed != null)
            {
                using var b = new SolidBrush(PlayerColors[placed.Owner.ColorName]);
                g.FillEllipse(b, r);
                g.DrawEllipse(Pens.Black, r);
                DrawCenteredText(g, def.Canal && def.Rail ? (game.Era == Era.Canal ? "C" : "R") : def.Canal ? "C" : "R", fontSmall, Brushes.White, r);
            }
            else
            {
                using var b = new SolidBrush(Color.FromArgb(70, Color.White));
                g.FillEllipse(b, r);
            }
            if (mode == Mode.NetworkPick && netOpts.Contains(def)) g.DrawEllipse(hlPen, r);
        }

        // location slots + tiles
        foreach (var loc in Data.Locations)
            for (int si = 0; si < loc.Slots.Length; si++)
            {
                var slot = loc.Slots[si];
                var r = SlotRect(slot.X, slot.Y);
                var t = game.Tiles.FirstOrDefault(x => x.Loc == loc.Name && x.SlotIdx == si);
                if (t != null)
                {
                    var img = TileImage(t.Owner.ColorName, t.Spec.Ind, t.Spec.Level, t.Flipped);
                    if (img != null) g.DrawImage(img, r);
                    else { using var b = new SolidBrush(PlayerColors[t.Owner.ColorName]); g.FillRectangle(b, r); }
                    g.DrawRectangle(Pens.Black, r.X, r.Y, r.Width, r.Height);
                    if (t.Cubes > 0)
                        g.DrawString(t.Cubes.ToString(), fontSmall, t.Spec.Ind == Industry.Brewery ? Brushes.Orange : Brushes.Cyan,
                            r.Right - 16 * Sc * 2, r.Bottom - 18 * Sc * 2);
                }
                else
                {
                    g.DrawRectangle(slotPen, r.X, r.Y, r.Width, r.Height);
                }
                if (mode == Mode.BuildPick && buildOpts.Any(o => o.Loc == loc && o.SlotIdx == si))
                    g.DrawRectangle(hlPen, r.X, r.Y, r.Width, r.Height);
                if (mode == Mode.SellPick && t != null && sellOpts.Any(s => s.Tile == t))
                    g.DrawRectangle(hlPen, r.X, r.Y, r.Width, r.Height);
            }

        // merchants
        foreach (var m in game.MerchantSlots)
        {
            var r = SlotRect(m.Pos.X, m.Pos.Y);
            using var b = new SolidBrush(Color.FromArgb(230, 60, 50, 40));
            g.FillRectangle(b, r);
            g.DrawRectangle(Pens.Gold, r.X, r.Y, r.Width, r.Height);
            var mi = MerchantImage(m.Good);
            if (mi != null) g.DrawImage(mi, r);
            else DrawCenteredText(g, m.Good switch
            {
                Good.Blank => "—",
                Good.Any => "ANY",
                Good.Cotton => "COT",
                Good.Manufacturer => "MAN",
                _ => "POT"
            }, fontSmall, Brushes.Gold, r);
            if (m.HasBeer)
            {
                using var beer = new SolidBrush(Color.Orange);
                g.FillEllipse(beer, r.Right - 14 * Sc * 2, r.Top, 12 * Sc * 2, 12 * Sc * 2);
            }
        }

        // markets summary box
        var mr = new RectangleF(size + 4, 4, 10, 10);
        string coalNext = NextPrice(game.CoalMarket, 7, 8), ironNext = NextPrice(game.IronMarket, 5, 6);
        var info = $"Coal market: {game.CoalMarket.Sum()} cubes, next £{coalNext}    Iron market: {game.IronMarket.Sum()} cubes, next £{ironNext}";
        g.DrawString(info, fontSmall, Brushes.White, 8, size - 22);

        // coal market cubes on the board (price £1..£7, up to 2 cubes each)
        var cube = CoalCubeImage();
        if (cube != null)
        {
            float cs = 40 * Sc;
            for (int pr = 1; pr <= 7; pr++)
            {
                var slots = CoalMarketPos[pr - 1];
                for (int i = 0; i < game.CoalMarket[pr]; i++)
                {
                    var p = Map(slots[i].X, slots[i].Y);
                    g.DrawImage(cube, p.X - cs / 2, p.Y - cs / 2, cs, cs);
                }
            }
        }

        // iron market cubes on the board (price £1..£5, up to 2 cubes each)
        var icube = IronCubeImage();
        if (icube != null)
        {
            float cs = 40 * Sc;
            for (int pr = 1; pr <= 5; pr++)
            {
                var slots = IronMarketPos[pr - 1];
                for (int i = 0; i < game.IronMarket[pr]; i++)
                {
                    var p = Map(slots[i].X, slots[i].Y);
                    g.DrawImage(icube, p.X - cs / 2, p.Y - cs / 2, cs, cs);
                }
            }
        }

        // turn-order track: player portraits at lower-left, spent-this-round overlaid.
        float ps = 140 * Sc;
        for (int i = 0; i < game.Order.Count; i++)
        {
            var pos = TurnOrderPos[i];
            var p = Map(pos.X, pos.Y);
            var rect = new RectangleF(p.X - ps / 2, p.Y - ps / 2, ps, ps);
            var img = PlayerImage(game.Order[i].ColorName);
            if (img != null) g.DrawImage(img, rect);
            else { using var b = new SolidBrush(PlayerColors[game.Order[i].ColorName]); g.FillRectangle(b, rect); }
            g.DrawRectangle(Pens.Black, rect.X, rect.Y, rect.Width, rect.Height);
            if (i == game.TurnIdx) g.DrawRectangle(hlPen, rect.X, rect.Y, rect.Width, rect.Height);
            var spent = game.Order[i].Spent;
            var label = $"£{spent}";
            var sz = g.MeasureString(label, fontSmall);
            var tr = new RectangleF(rect.X, rect.Bottom - sz.Height - 2 * Sc, rect.Width, sz.Height + 4 * Sc);
            using var bg = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
            g.FillRectangle(bg, tr);
            g.DrawString(label, fontSmall, Brushes.Gold, tr.X + (tr.Width - sz.Width) / 2, tr.Y + 2 * Sc);
        }

        // VP track: one marker per player at VP % 100, offset within the space when tied.
        float vs = 44 * Sc;
        foreach (var group in game.Players.GroupBy(pl => ((pl.VP % 100) + 100) % 100))
        {
            var pos = VpTrackPos[group.Key];
            var center = Map(pos.X, pos.Y);
            var tied = group.ToList();
            for (int i = 0; i < tied.Count; i++)
            {
                float ox = (i % 2) * vs * 0.5f - (tied.Count > 1 ? vs * 0.25f : 0);
                float oy = (i / 2) * vs * 0.5f - (tied.Count > 2 ? vs * 0.25f : 0);
                var rect = new RectangleF(center.X + ox - vs / 2, center.Y + oy - vs / 2, vs, vs);
                var img = PlayerPointImage(tied[i].ColorName);
                if (img != null) g.DrawImage(img, rect);
                else { using var b = new SolidBrush(PlayerColors[tied[i].ColorName]); g.FillEllipse(b, rect); }
            }
        }

        // Income track: one marker per player at IncomeSpace, same track as VP markers.
        foreach (var group in game.Players.GroupBy(pl => pl.IncomeSpace))
        {
            var pos = VpTrackPos[group.Key];
            var center = Map(pos.X, pos.Y);
            var tied = group.ToList();
            for (int i = 0; i < tied.Count; i++)
            {
                float ox = (i % 2) * vs * 0.5f - (tied.Count > 1 ? vs * 0.25f : 0);
                float oy = (i / 2) * vs * 0.5f - (tied.Count > 2 ? vs * 0.25f : 0);
                var rect = new RectangleF(center.X + ox - vs / 2, center.Y + oy - vs / 2, vs, vs);
                var img = PlayerIncomeImage(tied[i].ColorName);
                if (img != null) g.DrawImage(img, rect);
                else { using var b = new SolidBrush(PlayerColors[tied[i].ColorName]); g.FillRectangle(b, rect); }
            }
        }

        fontSmall.Dispose();
    }

    static string NextPrice(int[] mkt, int max, int empty)
    {
        for (int p = 1; p <= max; p++) if (mkt[p] > 0) return p.ToString();
        return empty.ToString();
    }

    static void DrawCenteredText(Graphics g, string s, Font f, Brush b, RectangleF r)
    {
        var sz = g.MeasureString(s, f);
        g.DrawString(s, f, b, r.X + (r.Width - sz.Width) / 2, r.Y + (r.Height - sz.Height) / 2);
    }

    // ---------- interaction ----------

    void BoardClick(object? sender, MouseEventArgs e)
    {
        if (mode == Mode.BuildPick)
        {
            foreach (var o in buildOpts)
            {
                var slot = o.Loc.Slots[o.SlotIdx];
                if (SlotRect(slot.X, slot.Y).Contains(e.Location))
                {
                    var opts = buildOpts.Where(x => x.Loc == o.Loc && x.SlotIdx == o.SlotIdx).ToList();
                    BuildOpt pick;
                    if (opts.Count == 1) pick = opts[0];
                    else
                    {
                        int r = ChoiceForm.Show(this, $"Build what in {o.Loc.Name}?",
                            opts.Select(x => $"{x.Spec} (£{x.MoneyCost}{(x.Over != null ? ", overbuild" : "")})").ToList(), cancellable: true);
                        if (r < 0) return;
                        pick = opts[r];
                    }
                    game.ExecBuild(game.Current, buildCard!, pick, chooser);
                    game.ActionDone();
                    FinishAction();
                    return;
                }
            }
        }
        else if (mode == Mode.NetworkPick)
        {
            foreach (var def in netOpts)
                if (LinkRect(def).Contains(e.Location))
                {
                    bool first = !netSecond;
                    if (first)
                    {
                        game.SpendAction(game.Current, netCard!);
                        game.ExecNetwork(game.Current, def, false, chooser);
                        if (game.Era == Era.Rail)
                        {
                            netOpts = game.NetworkOptions(game.Current, true);
                            if (netOpts.Count > 0 && MessageBox.Show(this,
                                "Build a second rail link? (+£10, +1 coal, +1 beer from a brewery)",
                                "Network", MessageBoxButtons.YesNo) == DialogResult.Yes)
                            {
                                netSecond = true;
                                lblStatus.Text = "Click the second rail link.";
                                boardPanel.Invalidate();
                                return;
                            }
                        }
                        FinishAction();
                    }
                    else
                    {
                        game.ExecNetwork(game.Current, def, true, chooser);
                        FinishAction();
                    }
                    return;
                }
        }
        else if (mode == Mode.SellPick)
        {
            foreach (var s in sellOpts)
            {
                var slot = Data.Loc(s.Tile.Loc).Slots[s.Tile.SlotIdx];
                if (SlotRect(slot.X, slot.Y).Contains(e.Location))
                {
                    var m = s.Merchants.Count == 1 ? s.Merchants[0]
                        : s.Merchants[chooser.Pick("Sell to which merchant?", s.Merchants.Select(x => x.Label + (x.HasBeer ? " [beer]" : "")).ToList())];
                    game.ExecSell(game.Current, s.Tile, m, chooser);
                    soldAny = true;
                    sellOpts = game.SellOptions(game.Current);
                    if (sellOpts.Count == 0) { FinishAction(); return; }
                    lblStatus.Text = "Sell another tile, or Cancel to stop.";
                    boardPanel.Invalidate();
                    UpdatePanels();
                    return;
                }
            }
        }
    }

    Card? netCard;
    bool netSecond;

    void StartBuild()
    {
        var card = SelectedCard;
        if (card == null) { lblStatus.Text = "Select a card first."; return; }
        buildOpts = game.BuildOptions(game.Current, card);
        if (buildOpts.Count == 0) { lblStatus.Text = $"No legal builds with '{card.Label}'."; return; }
        buildCard = card;
        mode = Mode.BuildPick;
        lblStatus.Text = "Click a highlighted slot to build.";
        SetButtons();
        boardPanel.Invalidate();
    }

    void StartNetwork()
    {
        var card = SelectedCard;
        if (card == null) { lblStatus.Text = "Select a card first."; return; }
        netOpts = game.NetworkOptions(game.Current, false);
        if (netOpts.Count == 0) { lblStatus.Text = "No legal links (check money/coal)."; return; }
        netCard = card;
        netSecond = false;
        mode = Mode.NetworkPick;
        lblStatus.Text = $"Click a highlighted link (£{game.LinkCost(false)}{(game.Era == Era.Rail ? " + coal" : "")}).";
        SetButtons();
        boardPanel.Invalidate();
    }

    void StartSell()
    {
        var card = SelectedCard;
        if (card == null) { lblStatus.Text = "Select a card first."; return; }
        sellOpts = game.SellOptions(game.Current);
        if (sellOpts.Count == 0) { lblStatus.Text = "Nothing sellable (connection/beer)."; return; }
        game.SpendAction(game.Current, card);
        soldAny = false;
        mode = Mode.SellPick;
        lblStatus.Text = "Click a highlighted tile to sell it.";
        SetButtons();
        boardPanel.Invalidate();
    }

    void DoDevelop()
    {
        var card = SelectedCard;
        if (card == null) { lblStatus.Text = "Select a card first."; return; }
        if (!game.CanDevelop(game.Current, 1)) { lblStatus.Text = "Cannot develop (no tiles or no money for iron)."; return; }
        var opts = game.DevelopOptions(game.Current);
        int r = ChoiceForm.Show(this, "Develop: remove which tile? (1 iron each)", opts.Select(t => t.ToString()).ToList(), cancellable: true);
        if (r < 0) return;
        game.SpendAction(game.Current, card);
        game.ExecDevelop(game.Current, opts[r], chooser);
        opts = game.DevelopOptions(game.Current);
        if (opts.Count > 0 && game.CanDevelop(game.Current, 1))
        {
            int r2 = ChoiceForm.Show(this, "Remove a second tile? (Cancel = no)", opts.Select(t => t.ToString()).ToList(), cancellable: true);
            if (r2 >= 0) game.ExecDevelop(game.Current, opts[r2], chooser);
        }
        FinishAction();
    }

    void DoLoan()
    {
        var card = SelectedCard;
        if (card == null) { lblStatus.Text = "Select a card first."; return; }
        if (!game.CanLoan(game.Current)) { lblStatus.Text = "Income would fall below -10."; return; }
        if (MessageBox.Show(this, "Take a £30 loan (-3 income levels)?", "Loan", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        game.SpendAction(game.Current, card);
        game.ExecLoan(game.Current);
        FinishAction();
    }

    void DoScout()
    {
        var card = SelectedCard;
        if (card == null) { lblStatus.Text = "Select a card first."; return; }
        if (!game.CanScout(game.Current)) { lblStatus.Text = "Cannot scout (need 3 cards, no wilds held)."; return; }
        var rest = game.Current.Hand.Where(c => c != card).ToList();
        int r1 = ChoiceForm.Show(this, "Scout: discard 1st extra card", rest.Select(c => c.Label).ToList(), cancellable: true);
        if (r1 < 0) return;
        var c1 = rest[r1];
        rest.RemoveAt(r1);
        int r2 = ChoiceForm.Show(this, "Scout: discard 2nd extra card", rest.Select(c => c.Label).ToList(), cancellable: true);
        if (r2 < 0) return;
        game.SpendAction(game.Current, card);
        game.ExecScout(game.Current, c1, rest[r2]);
        FinishAction();
    }

    void DoPass()
    {
        var card = SelectedCard;
        if (card == null) { lblStatus.Text = "Select a card to discard for passing."; return; }
        game.SpendAction(game.Current, card);
        game.Log($"{game.Current.Name} passes");
        FinishAction();
    }

    void CancelMode()
    {
        if (mode == Mode.SellPick && soldAny) { FinishAction(); return; } // stop selling; action already spent
        if (mode == Mode.SellPick) // no sale yet: refund the card
        {
            game.Current.Hand.Add(RetrieveLastDiscard());
            game.ActionsLeft++;
        }
        if (mode == Mode.NetworkPick && netSecond) { FinishAction(); return; } // decline second link
        mode = Mode.Idle;
        Refresh_();
    }

    Card RetrieveLastDiscard()
    {
        var c = game.Discards[^1];
        game.Discards.RemoveAt(game.Discards.Count - 1);
        return c;
    }

    void FinishAction()
    {
        mode = Mode.Idle;
        var p = game.Current;
        if (game.TurnOver)
        {
            game.EndTurn(chooser);
            if (game.GameOver)
            {
                Refresh_();
                var ranked = game.Players.OrderByDescending(x => x.VP).ThenByDescending(x => x.IncomeLevel).ThenByDescending(x => x.Money).First();
                MessageBox.Show(this, $"Game over! Winner: {ranked.Name} ({ranked.VP} VP)", "Brass: Birmingham");
                return;
            }
            mode = Mode.Cover;
        }
        Refresh_();
    }

    void SetButtons()
    {
        bool idle = mode == Mode.Idle;
        btnBuild.Enabled = btnNet.Enabled = btnSell.Enabled = btnDev.Enabled =
            btnLoan.Enabled = btnScout.Enabled = btnPass.Enabled = idle;
        btnCancel.Enabled = mode is Mode.BuildPick or Mode.NetworkPick or Mode.SellPick;
        btnStart.Visible = mode == Mode.Cover;
        lstHand.Visible = mode != Mode.Cover;
    }

    void Refresh_()
    {
        var p = game.Current;
        Text = $"Brass: Birmingham — {game.Era} Era, Round {game.Round}/{game.RoundsPerEra}";
        lblPlayers.Text = string.Join(Environment.NewLine,
            game.Order.Select(x => $"{(x == p ? "►" : " ")} {x.Name,-10} ({x.ColorName[0]})  £{x.Money,-3} inc {x.IncomeLevel,-3} VP {x.VP,-3} links {x.LinksLeft}"))
            + Environment.NewLine + $"Deck: {game.Deck.Count}   Coal next £{NextPrice(game.CoalMarket, 7, 8)}   Iron next £{NextPrice(game.IronMarket, 5, 6)}";
        if (mode == Mode.Cover)
        {
            lblStatus.Text = $"{p.Name}'s turn ({game.ActionsLeft} action{(game.ActionsLeft > 1 ? "s" : "")}). Hand hidden — click Start turn.";
            btnStart.Text = $"Start {p.Name}'s turn";
        }
        else if (mode == Mode.Idle)
        {
            lblStatus.Text = $"{p.Name}: {game.ActionsLeft} action{(game.ActionsLeft > 1 ? "s" : "")} left. Select a card, then an action.";
        }
        lstHand.Items.Clear();
        foreach (var c in p.Hand) lstHand.Items.Add(c.Label);
        SetButtons();
        UpdatePanels();
    }

    void UpdatePanels() => boardPanel.Invalidate();
}

