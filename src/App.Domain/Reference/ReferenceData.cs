namespace App.Domain.Reference;

/// <summary>The seeded reference data (design.md §4). The seeder upserts it on every start.</summary>
public static class ReferenceData
{
    public static readonly IReadOnlyList<(string Slug, string Name, string[] Aliases)> Cities =
    [
        ("prishtine", "Prishtinë", ["Pristina", "Prishtina", "Priština"]),
        ("prizren", "Prizren", []),
        ("peje", "Pejë", ["Peja", "Peć"]),
        ("gjakove", "Gjakovë", ["Gjakova", "Đakovica"]),
        ("mitrovice", "Mitrovicë", ["Mitrovica"]),
        ("mitrovice-veriore", "Mitrovicë e Veriut", ["North Mitrovica"]),
        ("ferizaj", "Ferizaj", ["Uroševac"]),
        ("gjilan", "Gjilan", ["Gnjilane"]),
        ("podujeve", "Podujevë", ["Podujeva"]),
        ("vushtrri", "Vushtrri", []),
        ("suhareke", "Suharekë", ["Suhareka"]),
        ("rahovec", "Rahovec", []),
        ("drenas", "Drenas", ["Gllogoc"]),
        ("lipjan", "Lipjan", []),
        ("malisheve", "Malishevë", []),
        ("kamenice", "Kamenicë", []),
        ("viti", "Viti", []),
        ("decan", "Deçan", []),
        ("istog", "Istog", []),
        ("kline", "Klinë", []),
        ("skenderaj", "Skenderaj", []),
        ("dragash", "Dragash", []),
        ("fushe-kosove", "Fushë Kosovë", []),
        ("kacanik", "Kaçanik", []),
        ("shtime", "Shtime", []),
        ("obiliq", "Obiliq", []),
        ("novoberde", "Novobërdë", []),
        ("gracanice", "Graçanicë", []),
        ("hani-i-elezit", "Hani i Elezit", []),
        ("junik", "Junik", []),
        ("kllokot", "Kllokot", []),
        ("mamushe", "Mamushë", []),
        ("partesh", "Partesh", []),
        ("ranillug", "Ranillug", []),
        ("shterpce", "Shtërpcë", []),
        ("zubin-potok", "Zubin Potok", []),
        ("zvecan", "Zveçan", []),
        ("leposaviq", "Leposaviq", []),
    ];

    public static readonly IReadOnlyList<(string Slug, string Name, ItemKind Kind)> Categories =
    [
        ("muzike", "Muzikë & koncerte", ItemKind.Event),
        ("teater", "Teatër & film", ItemKind.Event),
        ("art", "Art & ekspozita", ItemKind.Event),
        ("sport", "Sport", ItemKind.Event),
        ("familje", "Familje & fëmijë", ItemKind.Event),
        ("ushqim", "Ushqim & pije", ItemKind.Event),
        ("nate", "Jeta e natës", ItemKind.Event),
        ("edukim", "Konferenca & edukim", ItemKind.Event),
        ("teknologji", "Teknologji", ItemKind.Deal),
        ("ushqim", "Ushqim & pije", ItemKind.Deal),
        ("mode", "Modë", ItemKind.Deal),
        ("shtepi", "Shtëpi", ItemKind.Deal),
        ("sherbime", "Shërbime", ItemKind.Deal),
        ("udhetime", "Udhëtime", ItemKind.Deal),
    ];
}
