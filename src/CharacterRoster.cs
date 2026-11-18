using System;
using System.Collections.Generic;
using System.Linq;

namespace TideCombat
{
    internal sealed class CharacterKit
    {
        internal readonly string Id, Name;
        internal readonly BattleRole Role;
        internal readonly Func<TurnContext, IEnumerable<Move>> Play;
        internal readonly string[] Indicators;
        internal readonly int BuffMilliseconds;

        internal CharacterKit(string id, string name, BattleRole role, Func<TurnContext, IEnumerable<Move>> play,
            int buffMilliseconds, params string[] indicators)
        {
            Id = id;
            Name = name;
            Role = role;
            Play = play;
            BuffMilliseconds = buffMilliseconds > 0 ? buffMilliseconds : role == BattleRole.Support ? 28000 : 14000;
            Indicators = indicators;
        }

        internal BattleRole RoleFor(CombatSettings settings)
        {
            if ((Id == "Iuno" && settings.IunoC6) || (Id == "Roccia" && settings.RocciaC6) ||
                (Id == "Chisa" && settings.ChisaDps) || (Id == "Suoming" && settings.SuomingDps))
                return BattleRole.Main;
            return Role;
        }

        internal int BuffDuration(CombatSettings settings)
        {
            return (Id == "Chisa" && settings.ChisaDps || Id == "Suoming" && settings.SuomingDps) ? 14000 : BuffMilliseconds;
        }

        internal static int IntroDuration(string id)
        {
            switch (id)
            {
                case "Ciaccona": return 730;
                case "Cartethyia": return 600;
                case "Zani": return 1420;
                case "RoverHavoc": return 640;
                case "RoverSpectro": return 920;
                case "RoverAero": return 520;
                default: return 900;
            }
        }

        internal static bool HasFireRing(string id)
        {
            return Ring(id) == ConcertoColor.Fusion;
        }

        internal static ConcertoColor Ring(string id)
        {
            switch (id)
            {
                case "Encore": case "Changli": case "Chixia": case "Mortefi": case "Brant": case "Lupa":
                case "Galbrena": case "Denia": case "Mornye": case "Aemeath": case "JingRan": return ConcertoColor.Fusion;
                case "Yinlin": case "Yuanwu": case "Calcharo": case "Xiangliyao": case "Augusta": case "Douling":
                case "Lumi": case "Rebecca": case "Hsin": case "Suoming": case "RoverElectro": return ConcertoColor.Electro;
                case "YangYangSp": case "Taoqi": case "Danjin": case "Camellya": case "Roccia": case "Cantarella":
                case "Phrolova": case "Chisa": case "RoverHavoc": return ConcertoColor.Havoc;
                case "Sanhua": case "Baizhi": case "Lingyang": case "Zhezhi": case "Youhu": case "Carlotta":
                case "Hiyuki": case "Lucilla": return ConcertoColor.Glacio;
                case "Verina": case "ShoreKeeper": case "Jinhsi": case "Phoebe": case "Zani": case "Linnai":
                case "Luhesi": case "Lucy": case "Lily": case "RoverSpectro": return ConcertoColor.Spectro;
                case "Aalto": case "Yangyang": case "Jianxin": case "Jiyan": case "Ciaccona": case "Cartethyia": case "Iuno":
                case "Qiuyuan": case "Xigelika": case "Qingxiao": case "RoverAero": return ConcertoColor.Aero;
                default: return ConcertoColor.Unknown;
            }
        }

        public override string ToString() { return Name; }
    }

    internal static class CharacterRoster
    {
        private static readonly BattleRole Main = BattleRole.Main;
        private static readonly BattleRole Sub = BattleRole.Secondary;
        private static readonly BattleRole Support = BattleRole.Support;
        internal static readonly CharacterKit Unrecognized = new CharacterKit(null, "未识别角色", Main, Rotations.Standard, 0);

        internal static readonly CharacterKit[] All =
        {
            Kit("Aalto", "秋水", Sub, Rotations.Aalto),
            Kit("Aemeath", "爱弥斯", Main, Rotations.Aemeath, "aemeath_e1 aemeath_e2 aemeath_lib2"),
            Kit("Augusta", "奥古斯塔", Main, Rotations.Augusta, "Augusta_lib1 Augusta_lib2 Augusta_jump_attack target_enemy_long_inner"),
            Kit("Baizhi", "白芷", Support, Rotations.Baizhi),
            Kit("Brant", "布兰特", Support, Rotations.Brant),
            Kit("Calcharo", "卡卡罗", Main, Rotations.Calcharo),
            Kit("Camellya", "椿", Main, Rotations.Camellya, "camellya_budding"),
            Kit("Cantarella", "坎特蕾拉", Support, Rotations.Cantarella),
            Kit("Carlotta", "珂莱塔", Main, Rotations.Carlotta),
            Kit("Cartethyia", "卡提希娅", Main, Rotations.Cartethyia, "forte_cartethyia_sword1 forte_cartethyia_sword2 forte_cartethyia_sword3 forte_cartethyia_space lib_cartethyia_big"),
            Kit("Changli", "长离", Main, Rotations.Changli),
            new CharacterKit("Chisa", "千咲", Support, Rotations.Chisa, 20000),
            Kit("Chixia", "炽霞", Main, Rotations.Standard),
            Kit("Ciaccona", "夏空", Sub, Rotations.Ciaccona),
            Kit("Danjin", "丹瑾", Sub, Rotations.Danjin),
            new CharacterKit("Denia", "达妮娅", Sub, Rotations.Denia, 14000, "denia_end_lib"),
            Kit("Douling", "卜灵", Support, Rotations.Douling, "douling_gen douling_zhen"),
            Kit("Encore", "安可", Main, Rotations.Encore),
            Kit("Galbrena", "嘉贝莉娜", Main, Rotations.Galbrena),
            Kit("Hiyuki", "绯雪", Main, Rotations.Hiyuki, "hiyuki_left hiyuki_right hiyuki_lib_forte"),
            Kit("Hsin", "心", Main, Rotations.Hsin, "hsin_lib1 hsin_lib2 hsin_h1 hsin_h2"),
            Kit("Iuno", "尤诺", Sub, Rotations.Iuno, "iuno_heavy iuno_jump"),
            Kit("Jianxin", "鉴心", Support, Rotations.Jianxin),
            Kit("JingRan", "景燃", Main, Rotations.JingRan),
            Kit("Jinhsi", "今汐", Main, Rotations.Jinhsi),
            Kit("Jiyan", "忌炎", Main, Rotations.Jiyan),
            new CharacterKit("Lily", "Lily", Support, Rotations.Lily, 30000, "lily_heavy", "lily_liberation", "lily_slumberland"),
            Kit("Lingyang", "凌阳", Main, Rotations.Lingyang),
            Kit("Linnai", "琳奈", Sub, Rotations.Linnai),
            Kit("Lucilla", "洛瑟菈", Sub, Rotations.Lucilla),
            Kit("Lucy", "露西", Main, Rotations.Lucy),
            Kit("Luhesi", "陆·赫斯", Main, Rotations.Luhesi, "luhesi_kick"),
            Kit("Lumi", "灯灯", Sub, Rotations.Lumi),
            Kit("Lupa", "露帕", Sub, Rotations.Lupa, "lupa_wolf_icon1 lupa_wolf_icon2"),
            Kit("Mornye", "莫宁", Support, Rotations.Mornye),
            Kit("Mortefi", "莫特斐", Sub, Rotations.Mortefi),
            Kit("Phoebe", "菲比", Sub, Rotations.Phoebe),
            Kit("Phrolova", "弗洛洛", Main, Rotations.Phrolova),
            Kit("Qingxiao", "清霄", Main, Rotations.Qingxiao, "qingxiao_e qingxiao_h1 qingxiao_h2"),
            Kit("Qiuyuan", "仇远", Sub, Rotations.Qiuyuan),
            Kit("Rebecca", "丽贝卡", Sub, Rotations.Rebecca),
            Kit("Roccia", "洛可可", Sub, Rotations.Roccia, "roccia_air_forte roccia_air_forte_second"),
            Kit("Rover", "漂泊者（识别属性）", Main, Rotations.Rover),
            Kit("RoverSpectro", "漂泊者·衍射", Main, Rotations.RoverSpectro),
            Kit("RoverHavoc", "漂泊者·湮灭", Main, Rotations.RoverHavoc),
            Kit("RoverAero", "漂泊者·气动", Main, Rotations.RoverAero),
            Kit("RoverElectro", "漂泊者·导电", Sub, Rotations.RoverElectro),
            Kit("Sanhua", "散华", Sub, Rotations.Sanhua),
            Kit("ShoreKeeper", "守岸人", Support, Rotations.ShoreKeeper),
            new CharacterKit("Suisui", "穗穗", Support, Rotations.Suisui, 28000, "suisui_forte2", "suisui_forte3", "suisui_e1"),
            new CharacterKit("Suoming", "Suoming", Sub, Rotations.Suoming, 8000, "suoming_forte_e"),
            Kit("Taoqi", "桃祈", Support, Rotations.Taoqi),
            Kit("Verina", "维里奈", Support, Rotations.Verina),
            Kit("Xiangliyao", "相里要", Main, Rotations.Xiangliyao),
            Kit("Xigelika", "西格莉卡", Main, Rotations.Xigelika),
            Kit("Yangyang", "秧秧", Sub, Rotations.Yangyang),
            Kit("YangYangSp", "秧秧·玄翎", Main, Rotations.YangyangSp),
            Kit("Yinlin", "吟霖", Sub, Rotations.Yinlin),
            Kit("Youhu", "釉瑚", Support, Rotations.Standard),
            Kit("Yuanwu", "渊武", Sub, Rotations.Yuanwu),
            Kit("Zani", "赞妮", Main, Rotations.Zani, "target_enemy_long_inner"),
            Kit("Zhezhi", "折枝", Sub, Rotations.Zhezhi),
        };

        private static readonly Dictionary<string, CharacterKit> ById = All.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

        private static CharacterKit Kit(string id, string name, BattleRole role, Func<TurnContext, IEnumerable<Move>> play,
            string indicators = "")
        {
            return new CharacterKit(id, name, role, play, 0,
                indicators.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        internal static CharacterKit Find(string id)
        {
            CharacterKit result;
            return !string.IsNullOrEmpty(id) && ById.TryGetValue(id, out result) ? result : null;
        }

        internal static string FromPortrait(string name)
        {
            switch (name)
            {
                case "chang_changli":
                case "char_changli2": return "Changli";
                case "yangyang_sp": return "YangYangSp";
                case "char_rover_male": return "Rover";
                case "char_moning":
                case "char_moning_new": return "Mornye";
                case "char_chouyuan": return "Qiuyuan";
                default:
                    CharacterKit result = Find(name.StartsWith("char_", StringComparison.Ordinal)
                        ? name.Substring(5).TrimEnd('2') : name);
                    return result == null ? null : result.Id;
            }
        }
    }
}
