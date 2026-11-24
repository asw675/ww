using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;

namespace TideCombat
{
    public enum BattleRole { Main, Secondary, Support }
    public enum Skill { Resonance, Echo, Liberation }
    public enum SkillOrder { ResonanceFirst, LiberationFirst, EchoFirst }
    public enum SkillState { Unknown, Unavailable, Cooling, Ready }
    public enum GameSource { Foreground, Background, Cloud, ManagedCloud, AutoLocal }

    public sealed class Rotation
    {
        public BattleRole Role { get; set; }
        public double FieldSeconds { get; set; }
        public SkillOrder Order { get; set; }

        private static readonly Skill[] ResonanceFirst = { Skill.Resonance, Skill.Liberation, Skill.Echo };
        private static readonly Skill[] LiberationFirst = { Skill.Liberation, Skill.Resonance, Skill.Echo };
        private static readonly Skill[] EchoFirst = { Skill.Echo, Skill.Resonance, Skill.Liberation };

        public static Rotation ForRole(BattleRole role)
        {
            switch (role)
            {
                case BattleRole.Support:
                    return new Rotation { Role = role, FieldSeconds = 2.5, Order = SkillOrder.EchoFirst };
                case BattleRole.Secondary:
                    return new Rotation { Role = role, FieldSeconds = 2.2, Order = SkillOrder.ResonanceFirst };
                default:
                    return new Rotation { Role = BattleRole.Main, FieldSeconds = 4.0, Order = SkillOrder.LiberationFirst };
            }
        }

        internal Skill[] Priority()
        {
            switch (Order)
            {
                case SkillOrder.LiberationFirst: return LiberationFirst;
                case SkillOrder.EchoFirst: return EchoFirst;
                default: return ResonanceFirst;
            }
        }

        internal void Validate()
        {
            if (!Enum.IsDefined(typeof(BattleRole), Role) || !Enum.IsDefined(typeof(SkillOrder), Order) ||
                double.IsNaN(FieldSeconds) || FieldSeconds < 1 || FieldSeconds > 15)
                throw new InvalidDataException("轮转配置无效。驻场时间须在 1–15 秒之间。");
        }
    }

    public sealed class CharacterProfile
    {
        public string Name { get; set; }
        public Rotation Rotation { get; set; }
        public byte[] Portrait { get; set; }
    }

    public sealed class CombatSettings
    {
        [OnDeserializing]
        private void ReadDefaults(StreamingContext context)
        {
            Source = GameSource.AutoLocal;
            AutoRevive = true;
            CheckLevitator = true;
            ToolKey = "T";
            BagKey = "B";
            GuidebookKey = "F2";
            JumpKey = "SPACE";
            DodgeKey = "LSHIFT";
            WheelKey = "TAB";
            Farm = FarmOptions.Defaults();
            Enhance = EnhanceOptions.Defaults();
            Domain = DomainOptions.Defaults();
            Nightmare = NightmareOptions.Defaults();
            Change = ChangeOptions.Defaults();
            Daily = DailyOptions.Defaults();
            Cultivation = CultivationOptions.Defaults();
            WorldAssist = new WorldAssistOptions();
        }

        public bool AutoTarget { get; set; }
        public bool AutoRevive { get; set; }
        public bool UseLiberation { get; set; }
        public bool CheckLevitator { get; set; }
        [IgnoreDataMember]
        internal bool HasAccountLiberationOverride { get; set; }
        public bool RotateParty { get; set; }
        public bool PreferConcerto { get; set; }
        public bool SwitchAtLowHealth { get; set; }
        public bool DetectSkillKeys { get; set; }
        public string ResonanceKey { get; set; }
        public string EchoKey { get; set; }
        public string LiberationKey { get; set; }
        public string ToolKey { get; set; }
        public string BagKey { get; set; }
        public string GuidebookKey { get; set; }
        public string JumpKey { get; set; }
        public string DodgeKey { get; set; }
        public string WheelKey { get; set; }

        internal ushort JumpVirtualKey() { return ControlVirtualKey(JumpKey, "SPACE", "跳跃"); }
        internal ushort DodgeVirtualKey() { return ControlVirtualKey(DodgeKey, "LSHIFT", "闪避"); }
        internal ushort WheelVirtualKey() { return ControlVirtualKey(WheelKey, "TAB", "轮盘"); }

        internal ushort GuidebookVirtualKey()
        {
            return ControlVirtualKey(GuidebookKey, "F2", "图鉴");
        }

        private static ushort ControlVirtualKey(string binding, string defaultKey, string name)
        {
            string key = string.IsNullOrWhiteSpace(binding) ? defaultKey : binding.Trim().ToUpperInvariant();
            switch (key)
            {
                case "LSHIFT": key = "LSHIFTKEY"; break;
                case "RSHIFT": key = "RSHIFTKEY"; break;
                case "SHIFT": key = "SHIFTKEY"; break;
                case "LCTRL": key = "LCONTROLKEY"; break;
                case "RCTRL": key = "RCONTROLKEY"; break;
                case "CTRL": key = "CONTROLKEY"; break;
                case "LALT": key = "LMENU"; break;
                case "RALT": key = "RMENU"; break;
                case "ALT": key = "MENU"; break;
                case "ESC": key = "ESCAPE"; break;
            }
            int number;
            if (key.Length == 1 && FarmInput.VirtualKey(key[0]) != 0)
                return FarmInput.VirtualKey(key[0]);
            if (key.StartsWith("F", StringComparison.Ordinal) && int.TryParse(key.Substring(1), out number) &&
                number >= 1 && number <= 24 && key == "F" + number)
                return (ushort)(0x6F + number);
            System.Windows.Forms.Keys named;
            if (char.IsLetter(key[0]) && !key.Contains(",") && Enum.TryParse(key, true, out named) &&
                Enum.IsDefined(typeof(System.Windows.Forms.Keys), named) && (int)named >= 8 && (int)named <= 254)
                return (ushort)named;
            throw new InvalidDataException(name + "快捷键须为单个键，例如 Space、G 或 Tab。");
        }
        public bool SupportOpener { get; set; }
        public bool IunoC6 { get; set; }
        public bool RocciaC6 { get; set; }
        public bool ChisaDps { get; set; }
        public bool SuomingDps { get; set; }
        public GameSource Source { get; set; }
        public FarmOptions Farm { get; set; }
        public EnhanceOptions Enhance { get; set; }
        public DomainOptions Domain { get; set; }
        public NightmareOptions Nightmare { get; set; }
        public ChangeOptions Change { get; set; }
        public DailyOptions Daily { get; set; }
        // Older WW releases reused Daily.ExitAfterTask in both account modes.
        // A missing nullable field is migrated once on load; a saved explicit
        // false must remain distinct from the ordinary daily setting.
        public bool? MultiAccountExitAfterTask { get; set; }
        internal bool ExitAfterMultiAccount { get { return MultiAccountExitAfterTask ?? (Daily != null && Daily.ExitAfterTask); } }
        public CultivationOptions Cultivation { get; set; }
        public WorldAssistOptions WorldAssist { get; set; }
        public string[] PartyCharacters { get; set; }
        public Rotation[] Slots { get; set; }
        public List<CharacterProfile> Characters { get; set; }

        public static CombatSettings Defaults()
        {
            return new CombatSettings
            {
                Source = GameSource.AutoLocal,
                AutoTarget = true,
                AutoRevive = true,
                UseLiberation = true,
                CheckLevitator = true,
                RotateParty = true,
                PreferConcerto = true,
                SwitchAtLowHealth = false,
                DetectSkillKeys = true,
                ResonanceKey = "E",
                EchoKey = "Q",
                LiberationKey = "R",
                ToolKey = "T",
                BagKey = "B",
                GuidebookKey = "F2",
                JumpKey = "SPACE",
                DodgeKey = "LSHIFT",
                WheelKey = "TAB",
                SupportOpener = true,
                Farm = FarmOptions.Defaults(),
                Enhance = EnhanceOptions.Defaults(),
                Domain = DomainOptions.Defaults(),
                Nightmare = NightmareOptions.Defaults(),
                Change = ChangeOptions.Defaults(),
                Daily = DailyOptions.Defaults(),
                MultiAccountExitAfterTask = false,
                Cultivation = CultivationOptions.Defaults(),
                WorldAssist = new WorldAssistOptions(),
                PartyCharacters = new string[3],
                Slots = new[] { Rotation.ForRole(BattleRole.Main), Rotation.ForRole(BattleRole.Secondary),
                    Rotation.ForRole(BattleRole.Support) },
                Characters = new List<CharacterProfile>()
            };
        }

        internal void Validate()
        {
            var keys = new[] { ResonanceKey, EchoKey, LiberationKey };
            if (keys.Any(key => key == null || key.Length != 1 || key[0] < 'A' || key[0] > 'Z') ||
                keys.Distinct().Count() != keys.Length)
                throw new InvalidDataException("三个技能键须为不同的英文字母。");
            if (ToolKey == null || ToolKey.Length != 1 || ToolKey[0] < 'A' || ToolKey[0] > 'Z')
                throw new InvalidDataException("探索工具键须为英文字母。");
            if (string.IsNullOrWhiteSpace(GuidebookKey)) GuidebookKey = "F2";
            GuidebookVirtualKey();
            if (string.IsNullOrWhiteSpace(JumpKey)) JumpKey = "SPACE";
            if (string.IsNullOrWhiteSpace(DodgeKey)) DodgeKey = "LSHIFT";
            if (string.IsNullOrWhiteSpace(WheelKey)) WheelKey = "TAB";
            JumpVirtualKey();
            DodgeVirtualKey();
            WheelVirtualKey();
            if (BagKey == null || BagKey.Length != 1 || BagKey[0] < 'A' || BagKey[0] > 'Z')
                BagKey = "B";
            if (PartyCharacters == null)
            {
                // Version one did not have a built-in roster.
                PartyCharacters = new string[3];
                SupportOpener = true;
            }
            if (Farm == null)
                Farm = FarmOptions.Defaults();
            if (Enhance == null)
                Enhance = EnhanceOptions.Defaults();
            if (Domain == null)
                Domain = DomainOptions.Defaults();
            if (Nightmare == null)
                Nightmare = NightmareOptions.Defaults();
            if (Change == null)
                Change = ChangeOptions.Defaults();
            if (Daily == null)
                Daily = DailyOptions.Defaults();
            if (!MultiAccountExitAfterTask.HasValue)
                MultiAccountExitAfterTask = Daily.ExitAfterTask;
            if (Cultivation == null)
                Cultivation = CultivationOptions.Defaults();
            if (WorldAssist == null)
                WorldAssist = new WorldAssistOptions();
            Farm.Validate();
            Enhance.Validate();
            Daily.Validate();
            if (Slots == null || Slots.Length != 3 || PartyCharacters.Length != 3 ||
                Slots.Any(slot => slot == null) || Characters == null || !Enum.IsDefined(typeof(GameSource), Source))
                throw new InvalidDataException("配置文件缺少技能布局或队伍设置。");

            foreach (Rotation slot in Slots)
                slot.Validate();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CharacterProfile profile in Characters)
            {
                if (profile == null || string.IsNullOrWhiteSpace(profile.Name) || !names.Add(profile.Name) ||
                    profile.Rotation == null || profile.Portrait == null ||
                    profile.Portrait.Length != PortraitReader.SampleLength)
                    throw new InvalidDataException("角色档案的名称、头像或轮转数据无效。");
                profile.Rotation.Validate();
            }
        }

        internal char KeyFor(Skill skill)
        {
            switch (skill)
            {
                case Skill.Resonance: return ResonanceKey[0];
                case Skill.Echo: return EchoKey[0];
                default: return LiberationKey[0];
            }
        }
    }

    internal static class SettingsFile
    {
        internal static readonly string Location = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CombatSettings.json");
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(CombatSettings));

        internal static CombatSettings Load(string path)
        {
            if (!File.Exists(path))
                return CombatSettings.Defaults();
            using (var stream = File.OpenRead(path))
            {
                var settings = (CombatSettings)Serializer.ReadObject(stream);
                if (settings == null)
                    throw new InvalidDataException("配置文件为空。");
                settings.Validate();
                return settings;
            }
        }

        internal static void Save(string path, CombatSettings settings)
        {
            settings.Validate();
            WriteAtomic(path, stream => Serializer.WriteObject(stream, settings));
        }

        internal static void WriteAtomic(string path, Action<Stream> write)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    write(stream);
                    stream.Flush(true);
                }
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }
}
