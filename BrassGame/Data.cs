namespace BrassGame;

public enum Industry { Cotton, Coal, Iron, Manufacturer, Pottery, Brewery }
public enum Era { Canal, Rail }
public enum Good { Blank, Cotton, Manufacturer, Pottery, Any }
public enum BonusKind { Money, VP, Income, Develop }

public record TileSpec(Industry Ind, int Level, int Count, int Cost, int Coal, int Iron,
    int VP, int Income, int Beer, int Produces, bool CanalOnly = false, bool RailOnly = false, bool Bulb = false)
{
    public override string ToString() => $"{Ind} L{Level}";
}

public record Slot(int X, int Y, Industry[] Allowed);
public record LocDef(string Name, int X, int Y, Slot[] Slots, bool Farm = false);
public record MerchDef(string Name, int X, int Y, int MinPlayers, BonusKind Bonus, int BonusVal, (int X, int Y)[] SlotPos);
// Locs may have 3 entries (Worcester-Kidderminster line also serves the farm brewery).
public record LinkDef(string[] Locs, bool Canal, bool Rail, int X, int Y)
{
    public string Id => string.Join("-", Locs);
    public override string ToString() => string.Join(" / ", Locs);
}

public abstract record Card { public abstract string Label { get; } }
public record LocationCard(string Loc) : Card { public override string Label => Loc; }
public record IndustryCard(Industry[] Inds, string Name) : Card { public override string Label => Name; }
public record WildLocationCard : Card { public override string Label => "Wild Location"; }
public record WildIndustryCard : Card { public override string Label => "Wild Industry"; }

public static class Data
{
    // Player mat tile roster, verified against img/player_mat.jpg.
    public static readonly TileSpec[] Tiles =
    {
        new(Industry.Cotton, 1, 3, 12, 0, 0,  5, 5, 1, 0, CanalOnly: true),
        new(Industry.Cotton, 2, 2, 14, 1, 0,  5, 4, 1, 0),
        new(Industry.Cotton, 3, 3, 16, 1, 1,  9, 3, 1, 0),
        new(Industry.Cotton, 4, 3, 18, 1, 1, 12, 2, 1, 0),

        new(Industry.Coal, 1, 1,  5, 0, 0, 1, 4, 0, 2, CanalOnly: true),
        new(Industry.Coal, 2, 2,  7, 0, 0, 2, 7, 0, 3),
        new(Industry.Coal, 3, 2,  8, 0, 1, 3, 6, 0, 4),
        new(Industry.Coal, 4, 2, 10, 0, 1, 4, 5, 0, 5),

        new(Industry.Iron, 1, 1,  5, 1, 0, 3, 3, 0, 4, CanalOnly: true),
        new(Industry.Iron, 2, 1,  7, 1, 0, 5, 3, 0, 4),
        new(Industry.Iron, 3, 1,  9, 1, 0, 7, 2, 0, 5),
        new(Industry.Iron, 4, 1, 12, 1, 0, 9, 1, 0, 6),

        new(Industry.Manufacturer, 1, 1,  8, 1, 0,  3, 5, 1, 0, CanalOnly: true),
        new(Industry.Manufacturer, 2, 2, 10, 0, 1,  5, 1, 1, 0),
        new(Industry.Manufacturer, 3, 1, 12, 2, 0,  4, 4, 0, 0),
        new(Industry.Manufacturer, 4, 1,  8, 0, 1,  3, 6, 1, 0),
        new(Industry.Manufacturer, 5, 2, 16, 1, 0,  8, 2, 2, 0),
        new(Industry.Manufacturer, 6, 1, 20, 0, 0,  7, 6, 1, 0),
        new(Industry.Manufacturer, 7, 1, 16, 1, 1,  9, 4, 0, 0),
        new(Industry.Manufacturer, 8, 2, 20, 0, 2, 11, 1, 1, 0),

        new(Industry.Pottery, 1, 1, 17, 0, 1, 10, 5, 1, 0, Bulb: true),
        new(Industry.Pottery, 2, 1,  0, 1, 0,  1, 1, 1, 0),
        new(Industry.Pottery, 3, 1, 22, 2, 0, 11, 5, 2, 0, Bulb: true),
        new(Industry.Pottery, 4, 1,  0, 1, 0,  1, 1, 1, 0),
        new(Industry.Pottery, 5, 1, 24, 2, 0, 20, 5, 2, 0, RailOnly: true),

        // Breweries produce 1 beer in the Canal Era, 2 in the Rail Era (handled in engine).
        new(Industry.Brewery, 1, 2, 5, 0, 1,  4, 4, 0, 1, CanalOnly: true),
        new(Industry.Brewery, 2, 2, 7, 0, 1,  5, 5, 0, 1),
        new(Industry.Brewery, 3, 2, 9, 0, 1,  7, 5, 0, 1),
        new(Industry.Brewery, 4, 1, 9, 0, 1, 10, 5, 0, 1, RailOnly: true),
    };

    static Slot S(int x, int y, params Industry[] a) => new(x, y, a);
    const Industry Cot = Industry.Cotton, Co = Industry.Coal, Ir = Industry.Iron,
        Man = Industry.Manufacturer, Pot = Industry.Pottery, Br = Industry.Brewery;

    // Coordinates are in the 2000x2000 space of img/main_board.jpg.
    public static readonly LocDef[] Locations =
    {
        new("Belper", 1467, 262, new[]{ S(1409 ,197,Cot,Man), S(1500,197,Co), S(1592,197,Pot) }),
        new("Derby", 1520, 589, new[]{ S(1517,426,Cot,Br), S(1469,517,Cot,Man), S(1563,517,Ir) }),
        new("Leek", 1090, 222, new[]{ S(1060,160,Cot,Man), S(1152,160,Cot,Co) }),
        new("Stoke", 827, 365, new[]{ S(833,211,Cot,Man), S(786,296,Pot,Ir), S(875,296,Man) }),
        new("Stone", 620, 575, new[]{ S(576,512,Cot,Br), S(667,512,Man,Co) }),
        new("Uttoxeter", 1100, 540, new[]{ S(1085,474,Man,Br), S(1176,474,Cot,Br) }),
        new("Stafford", 770, 762, new[]{ S(760,696,Man,Br), S(850,696,Pot) }),
        new("Burton", 1365, 813, new[]{ S(1320,755,Man,Co), S(1410,755,Br) }),
        new("Cannock", 950, 955, new[]{ S(907,896,Man,Co), S(997,896,Co) }),
        new("Tamworth", 1394, 1061, new[]{ S(1346,992,Cot,Co), S(1439,992,Cot,Co) }),
        new("Walsall", 1064, 1186, new[]{ S(1021,1122,Ir,Man), S(1109,1122,Man,Br) }),
        new("Wolverhampton", 746, 1141, new[]{ S(701,1075,Man), S(792,1075,Man,Co) }),
        new("Coalbrookdale", 474, 1227, new[]{ S(474,1072,Ir,Br), S(421,1165,Ir), S(520,1165,Co) }),
        new("Dudley", 837, 1376, new[]{ S(789,1318,Co), S(880,1318,Ir) }),
        new("Birmingham", 1258, 1461, new[]{ S(1205,1305,Cot,Man), S(1296,1305,Man), S(1205,1400,Ir), S(1296,1400,Man) }),
        new("Coventry", 1621, 1520, new[]{ S(1619,1365,Pot), S(1573,1450,Man,Co), S(1661,1450,Ir,Man) }),
        new("Nuneaton", 1573, 1264, new[]{ S(1525,1200,Man,Br), S(1619,1200,Cot,Co) }),
        new("Redditch", 1170, 1696, new[]{ S(1125,1628,Man,Co), S(1216,1628,Ir) }),
        new("Kidderminster", 701, 1589, new[]{ S(653,1520,Cot,Co), S(741,1520,Cot) }),
        new("Worcester", 722, 1848, new[]{ S(680,1781,Cot), S(768,1781,Cot) }),
        new("FarmN", 605, 940, new[]{ S(605,872,Br) }, Farm: true),
        new("FarmS", 525, 1735, new[]{ S(525,1669,Br) }, Farm: true),
    };

    public static readonly MerchDef[] Merchants =
    {
        new("Warrington", 560, 147, 3, BonusKind.Money, 5, new[]{ (507,267), (608,267) }),
        new("Nottingham", 1792, 269, 4, BonusKind.VP, 3, new[]{ (1752,412), (1858,412) }),
        new("Shrewsbury", 189, 1043, 2, BonusKind.VP, 4, new[]{ (190,1173) }),
        new("Gloucester", 1018, 1829, 2, BonusKind.Develop, 0, new[]{ (1160,1848), (1256,1848) }),
        new("Oxford", 1467, 1672, 2, BonusKind.Income, 2, new[]{ (1603,1696), (1693,1696) }),
    };

    static LinkDef Both(string a, string b, int x, int y) => new(new[] { a, b }, true, true, x, y);
    static LinkDef Canal(string a, string b, int x, int y) => new(new[] { a, b }, true, false, x, y);
    static LinkDef Rail(string a, string b, int x, int y) => new(new[] { a, b }, false, true, x, y);

    public static readonly LinkDef[] Links =
    {
        Both("Belper","Derby",1530,326),
        Rail("Belper","Leek",1250,160),
        Both("Derby","Nottingham",1640,420),
        Rail("Derby","Uttoxeter",1280,530),
        Both("Derby","Burton",1512,660),
        Both("Leek","Stoke",940,176),
        Both("Stoke","Warrington",705,195),
        Both("Stoke","Stone",730,420),
        Rail("Stone","Uttoxeter",860,500),
        Both("Stone","Burton",1030,600),
        Both("Stone","Stafford",630,666),
        Both("Stafford","Cannock",950,780),
        Both("Cannock","FarmN",750,860),
        Both("Cannock","Wolverhampton",800,960),
        Both("Cannock","Walsall",1078,1000),
        Rail("Cannock","Burton",1120,830),
        Both("Burton","Tamworth",1395,884),
        Canal("Burton","Walsall",1180,930),
        Both("Tamworth","Birmingham",1390,1175),
        Both("Tamworth","Nuneaton",1560,1043),
        Rail("Nuneaton","Birmingham",1420,1250),
        Rail("Nuneaton","Coventry",1600,1290),
        Both("Birmingham","Coventry",1430,1440),
        Both("Birmingham","Oxford",1420,1530),
        Rail("Birmingham","Redditch",1230,1520),
        Both("Birmingham","Worcester",975,1570),
        Both("Birmingham","Walsall",1114,1264),
        Both("Birmingham","Dudley",1038,1343),
        Both("Redditch","Oxford",1322,1655),
        Both("Redditch","Gloucester",1017,1710),
        Both("Gloucester","Worcester",882,1840),
        new(new[]{"Worcester","Kidderminster","FarmS"}, true, true, 680,1660),
        Both("Kidderminster","Dudley",712,1415),
        Both("Kidderminster","Coalbrookdale",500,1363),
        Both("Dudley","Wolverhampton",750,1220),
        Both("Wolverhampton","Walsall",900,1090),
        Both("Wolverhampton","Coalbrookdale",586,1070),
        Both("Coalbrookdale","Shrewsbury",335,1076),
    };

    // Location card counts at 2/3/4 players. Deck totals 40/54/64.
    static readonly (string Loc, int C2, int C3, int C4)[] LocCards =
    {
        ("Belper",0,0,2), ("Derby",0,0,3), ("Leek",0,2,2), ("Stoke",0,3,3), ("Stone",0,2,2), ("Uttoxeter",0,1,2),
        ("Stafford",2,2,2), ("Burton",2,2,2), ("Cannock",2,2,2), ("Tamworth",1,1,1), ("Walsall",1,1,1),
        ("Coalbrookdale",3,3,3), ("Dudley",2,2,2), ("Kidderminster",2,2,2), ("Wolverhampton",2,2,2),
        ("Worcester",2,2,2), ("Birmingham",3,3,3), ("Coventry",3,3,3), ("Nuneaton",1,1,1), ("Redditch",1,1,1),
    };

    static readonly (Industry[] Inds, string Name, int C2, int C3, int C4)[] IndCards =
    {
        (new[]{ Ir }, "Iron Works", 4, 4, 4),
        (new[]{ Co }, "Coal Mine", 2, 2, 3),
        (new[]{ Br }, "Brewery", 5, 5, 5),
        (new[]{ Pot }, "Pottery", 2, 2, 3),
        (new[]{ Cot, Man }, "Cotton Mill / Manufacturer", 0, 6, 8),
    };

    public static List<Card> BuildDeck(int players)
    {
        var deck = new List<Card>();
        foreach (var (loc, c2, c3, c4) in LocCards)
            for (int i = 0; i < (players == 2 ? c2 : players == 3 ? c3 : c4); i++)
                deck.Add(new LocationCard(loc));
        foreach (var (inds, name, c2, c3, c4) in IndCards)
            for (int i = 0; i < (players == 2 ? c2 : players == 3 ? c3 : c4); i++)
                deck.Add(new IndustryCard(inds, name));
        return deck;
    }

    // Merchant tile pool: 2p base, +2 at 3p, +2 at 4p.
    public static List<Good> MerchantPool(int players)
    {
        var pool = new List<Good> { Good.Any, Good.Cotton, Good.Manufacturer, Good.Blank, Good.Blank };
        if (players >= 3) pool.AddRange(new[] { Good.Manufacturer, Good.Pottery });
        if (players >= 4) pool.AddRange(new[] { Good.Any, Good.Cotton });
        return pool;
    }

    public static LocDef Loc(string name) => Locations.First(l => l.Name == name);
    public static MerchDef? Merch(string name) => Merchants.FirstOrDefault(m => m.Name == name);
    public static bool IsMerchant(string name) => Merchants.Any(m => m.Name == name);

    // Player-mat slot centers in the 1000x800 space of img/player_mat.jpg, keyed by (industry, level).
    // ponytail: filled in incrementally as coords are provided; industries without entries fall back to a legend.
    public static readonly Dictionary<(Industry, int), (int X, int Y)> MatSlots = new()
    {
        [(Industry.Cotton, 1)] = (300, 560),
        [(Industry.Cotton, 2)] = (300, 456),
        [(Industry.Cotton, 3)] = (300, 353),
        [(Industry.Cotton, 4)] = (300, 246),
        [(Industry.Pottery, 1)] = (520, 560),
        [(Industry.Pottery, 2)] = (520, 457),
        [(Industry.Pottery, 3)] = (520, 352),
        [(Industry.Pottery, 4)] = (520, 246),
        [(Industry.Pottery, 5)] = (681, 247),
        [(Industry.Iron, 1)] = (734, 560),
        [(Industry.Iron, 2)] = (905, 560),
        [(Industry.Iron, 3)] = (905, 454),
        [(Industry.Iron, 4)] = (905, 353),
        [(Industry.Manufacturer, 1)] = (80, 270),
        [(Industry.Manufacturer, 2)] = (80, 170),
        [(Industry.Manufacturer, 3)] = (80, 65),
        [(Industry.Manufacturer, 4)] = (247, 65),
        [(Industry.Manufacturer, 5)] = (413, 65),
        [(Industry.Manufacturer, 6)] = (582, 65),
        [(Industry.Manufacturer, 7)] = (747, 65),
        [(Industry.Manufacturer, 8)] = (914, 65),
        [(Industry.Coal, 1)] = (353, 740),
        [(Industry.Coal, 2)] = (521, 740),
        [(Industry.Coal, 3)] = (689, 740),
        [(Industry.Coal, 4)] = (860, 740),
        [(Industry.Brewery, 1)] = (84, 732),
        [(Industry.Brewery, 2)] = (84, 626),
        [(Industry.Brewery, 3)] = (84, 520),
        [(Industry.Brewery, 4)] = (84, 416),
    };

    // Progress track: spaces 0..99 -> income level -10..30.
    public static int IncomeLevel(int space) =>
        space <= 10 ? space - 10 :
        space <= 30 ? (space - 9) / 2 :
        space <= 60 ? 10 + (space - 28) / 3 :
        20 + (space - 57) / 4;

    public static int HighestSpaceOfLevel(int lvl) =>
        lvl <= 0 ? lvl + 10 :
        lvl <= 10 ? 10 + 2 * lvl :
        lvl <= 20 ? 30 + 3 * (lvl - 10) :
        60 + 4 * (lvl - 20);
}
     