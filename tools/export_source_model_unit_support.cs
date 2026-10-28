using System;
using System.IO;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
public static class DumpUnitSupport
{
    static string dataDirectory;
    static string Data(string f) => File.ReadAllText(Path.Combine(dataDirectory, f));
    public static void Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("data-directory output-tsv");
        dataDirectory = args[0];
        var units = CharacterUnitCatalog.FromJson(Data("units.json"));
        var map = OriginalMapBaseStatCatalog.FromJson(Data("original-2323C-map-base-stats.json"),units);
        var six = new Original323SpecialAuraCatalog(map); string audit=Data("original-2323C-all-unit-aura-audit.json");
        var auras = new Original323CompositeAuraCatalog(map, Original323ArmorAuraCatalog.FromJson(audit,map,six),
            Original323NativeSpeedAuraCatalog.FromJson(audit,map),Original323NativeCommandAuraCatalog.FromJson(audit,map));
        var random = new SeededNonOriginalProcRandom(1);
        var fx = new SourceModelUnitEffects(Original323NativePassiveCatalog.FromJson(Data("original-2323C-native-pure-passives.json"),map),
            auras,random,Data("practice-native-inherited-defaults.json"));
        var rules = new SourceModelBasicCombatRules(map,random); rules.Enable();
        using (var output = new StreamWriter(args[1]))
        foreach (var row in map.All)
        {
            OriginalVerifiedAttackBinding binding;
            output.WriteLine(row.UnitId+"\t"+(rules.TryResolve(units.Require(row.UnitId),null,out binding)?"1":"0")+"\t"+
                string.Join("|",fx.Describe(row.UnitId,row.OriginalRawcode)));
        }
    }
}
