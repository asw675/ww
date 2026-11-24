using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

namespace TideCombat
{
    // Account tasks work on a private copy of the user's current settings.
    internal static class AccountConfig
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();
        private static readonly Dictionary<string, string> ReferenceEnhanceKeys = new Dictionary<string, string>
        {
            { "必须有双爆", "RequireBothCrit" },
            { "双爆出现之前必须全有效词条", "RequireValidBeforeCrit" },
            { "双爆总计>=", "CritTotalMinimum" }, { "首条双爆>=", "FirstCritMinimum" },
            { "有效词条>=", "ValidCountMinimum" }, { "第一条必须为有效词条", "FirstMustBeValid" },
            { "有效词条", "ValidStats" }, { "Pause after Success", "PauseAfterSuccess" }
        };

        internal static CombatSettings ForRun(CombatSettings settings) { return Clone(settings); }

        internal static int Apply(CombatSettings settings, Dictionary<string, object> config, string task = null)
        {
            if (config == null)
                return 0;
            int applied = ApplyEnhance(settings.Enhance, config) + ApplyReferenceTask(settings, config, task);
            foreach (KeyValuePair<string, object> pair in config)
            {
                var values = pair.Value as Dictionary<string, object>;
                if (values == null)
                    continue;
                switch (pair.Key.ToLowerInvariant())
                {
                    case "farm": applied += ApplyFarmOverrides(settings.Farm, values); break;
                    case "daily": applied += ApplyObject(settings.Daily, values); break;
                    case "domain": applied += ApplyObject(settings.Domain, values); break;
                    case "nightmare": applied += ApplyObject(settings.Nightmare, values); break;
                    case "change": applied += ApplyObject(settings.Change, values); break;
                    case "cultivation": applied += ApplyObject(settings.Cultivation, values); break;
                    case "combat": applied += ApplyCombat(settings, values); break;
                }
            }
            return applied;
        }

        private static int ApplyReferenceTask(CombatSettings settings, Dictionary<string, object> config, string task)
        {
            const string tacetIndex = "Which Tacet Suppression to Farm", forgeryIndex = "Which Forgery Challenge to Farm";
            const string material = "Material Selection", farm = "Which to Farm";
            object choice;
            switch (task)
            {
                case "FarmEchoTask": return ApplyReferenceFarm(settings.Farm, config);
                case "TacetTask":
                    return ApplyReferenceField(settings.Domain, config, tacetIndex, "Index");
                case "ForgeryTask":
                    return ApplyReferenceField(settings.Domain, config, forgeryIndex, "Index");
                case "SimulationTask":
                    return ApplyReferenceField(settings.Domain, config, material, "Material", ReferenceMaterial);
                case "CultivationTask":
                    return ApplyReferenceField(settings.Cultivation, config, "Ascension Materials", "Ascension") +
                        ApplyReferenceField(settings.Cultivation, config, "Skill Materials", "Skills") +
                        ApplyReferenceField(settings.Cultivation, config, "Echo Set Tacet Suppression", "Tacet") +
                        ApplyReferenceField(settings.Cultivation, config, "Echo Set Nightmare Nest", "Nightmare");
                case "NightmareNestTask":
                    if (!config.TryGetValue(farm, out choice))
                        return 0;
                    IList nests = ReferenceSelection(choice, farm);
                    settings.Nightmare.Purification = nests.Contains("Nightmare Purification");
                    settings.Nightmare.TacetNest = nests.Contains("Tacet Discord Nest");
                    settings.Nightmare.Validate();
                    return 2;
                case "DailyTask":
                    return ApplyReferenceDaily(settings.Daily, config, farm, tacetIndex, forgeryIndex, material);
                default:
                    return 0;
            }
        }

        private static int ApplyReferenceFarm(FarmOptions farm, Dictionary<string, object> config)
        {
            int count = ApplyReferenceField(farm, config, "Boss Level", "BossLevel") +
                ApplyReferenceField(farm, config, "Repeat Farm Count", "RepeatCount") +
                ApplyReferenceField(farm, config, "Combat Wait Time", "WaitSeconds") +
                ApplyReferenceField(farm, config, "Switch to Healer before and after Combat", "SwitchHealer") +
                ApplyReferenceField(farm, config, "Use Liberation", "UseLiberation") +
                ApplyReferenceField(farm, config, "Which Weekly Boss to Teleport", "WeeklyChallengeIndex") +
                ApplyReferenceField(farm, config, "Which Boss Challenge to Teleport", "BossChallengeIndex") +
                ApplyReferenceField(farm, config, "Which Special Nightmare to Teleport", "SpecialNightmareIndex") +
                ApplyReferenceField(farm, config, "Boss", "Boss", value =>
                    Enum.Parse(typeof(FarmBoss), (value as string ?? "").Replace(" ", "").Replace(":", ""), true)) +
                ApplyReferenceField(farm, config, "Echo Pickup Method", "Pickup", value =>
                    Enum.Parse(typeof(FarmPickupMode), Equals(value, "Run in Circle") ? "Circle" : value as string, true));
            object teleport;
            if (config.TryGetValue("Teleport to Boss", out teleport))
            {
                farm.BossChallengeIndex = farm.IndexFor(FarmTeleportMode.BossChallenge);
                farm.WeeklyChallengeIndex = farm.IndexFor(FarmTeleportMode.WeeklyChallenge);
                farm.SpecialNightmareIndex = farm.IndexFor(FarmTeleportMode.SpecialNightmare);
                switch (teleport as string)
                {
                    case "No": farm.TeleportMode = FarmTeleportMode.Off; break;
                    case "Boss Challenge": farm.TeleportMode = FarmTeleportMode.BossChallenge; break;
                    case "Weekly Challenge": farm.TeleportMode = FarmTeleportMode.WeeklyChallenge; break;
                    case "Special Nightmare": farm.TeleportMode = FarmTeleportMode.SpecialNightmare; break;
                    default: throw new InvalidDataException("未知的 4C 传送方式。");
                }
                farm.Teleport = farm.TeleportMode != FarmTeleportMode.Off;
                count++;
            }
            return count;
        }

        private static int ApplyReferenceDaily(DailyOptions daily, Dictionary<string, object> config,
            string farm, string tacetIndex, string forgeryIndex, string material)
        {
            int applied = ApplyReferenceField(daily, config, farm, "Farm", ReferenceDomain) +
                ApplyReferenceField(daily, config, tacetIndex, "TacetIndex") +
                ApplyReferenceField(daily, config, forgeryIndex, "ForgeryIndex") +
                ApplyReferenceField(daily, config, material, "Material", ReferenceMaterial);
            int nightmare = ApplyReferenceField(daily, config, "Farm Nightmare Nest for Daily Echo", "FarmNightmareEcho");
            const string additional = "Additional Tasks to Run After Daily Task";
            object saved;
            bool hasAdditional = config.TryGetValue(additional, out saved);
            if (hasAdditional)
            {
                IList tasks = ReferenceSelection(saved, additional);
                daily.CheckGarden = tasks.Contains("Check Weekly Garden");
                daily.AutoNightmare = tasks.Contains("Auto Farm all Nightmare Nest");
                daily.MergeIfNeeded = tasks.Contains("Merge Echo If discarded > 1000");
                daily.Farm4C = tasks.Contains("Teleport and Farm 4C Echo");
                applied += 4;
            }
            if (hasAdditional || nightmare > 0)
            {
                daily.Tasks = null;
                daily.NightmareMode = null;
            }
            return applied + nightmare;
        }

        private static int ApplyReferenceField(object target, Dictionary<string, object> config, string key,
            string propertyName, Func<object, object> translate = null)
        {
            object value;
            if (target == null || !config.TryGetValue(key, out value))
                return 0;
            PropertyInfo property = Find(target.GetType(), propertyName);
            try
            {
                property.SetValue(target, translate == null ? ConvertValue(value, property.PropertyType) : translate(value), null);
                return 1;
            }
            catch (Exception error)
            {
                if (!(error is InvalidDataException || error is FormatException || error is ArgumentException ||
                    error is OverflowException || error is InvalidCastException))
                    throw;
                throw new InvalidDataException("原版账号配置无效 · " + key, error);
            }
        }

        private static IList ReferenceSelection(object value, string key)
        {
            var items = value as IList;
            if (items == null)
                throw new InvalidDataException("原版账号配置必须是列表 · " + key);
            return items;
        }

        private static object ReferenceMaterial(object value)
        {
            switch (value as string)
            {
                case "Resonator EXP": return DomainOptions.Materials[0];
                case "Weapon EXP": return DomainOptions.Materials[1];
                case "Shell Credit": return DomainOptions.Materials[2];
                default: throw new InvalidDataException("未知的材料类型。");
            }
        }

        private static object ReferenceDomain(object value)
        {
            switch (value as string)
            {
                case "Tacet Suppression": return DomainKind.Tacet;
                case "Forgery Challenge": return DomainKind.Forgery;
                case "Simulation Challenge": return DomainKind.Simulation;
                case "Cultivation Target": return DomainKind.Cultivation;
                default: throw new InvalidDataException("未知的体力任务。");
            }
        }

        internal static EnhanceOptions EnhanceForAccount(EnhanceOptions defaults, Dictionary<string, object> config)
        {
            EnhanceOptions options = Clone(defaults) ?? EnhanceOptions.Defaults();
            if (config != null)
                ApplyEnhance(options, config);
            options.Validate();
            return options;
        }

        internal static void WriteEnhance(Dictionary<string, object> config, EnhanceOptions options)
        {
            WriteOptions(config, "Enhance", options);
        }

        internal static void WriteOptions(Dictionary<string, object> config, string section, object options)
        {
            string key = section;
            foreach (string name in config.Keys)
                if (string.Equals(name, section, StringComparison.OrdinalIgnoreCase)) key = name;
            var fields = (Dictionary<string, object>)Serializer.DeserializeObject(Serializer.Serialize(options));
            object value;
            var saved = config.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
            if (saved == null) config[key] = fields;
            else
            {
                var replaced = new HashSet<string>(fields.Keys, StringComparer.OrdinalIgnoreCase);
                foreach (string oldName in new List<string>(saved.Keys))
                    if (replaced.Contains(oldName)) saved.Remove(oldName);
                foreach (var pair in fields) saved[pair.Key] = pair.Value;
            }
        }

        private static int ApplyEnhance(EnhanceOptions options, Dictionary<string, object> config)
        {
            var reference = new Dictionary<string, object>();
            Dictionary<string, object> local = null;
            foreach (KeyValuePair<string, object> pair in config)
            {
                string field;
                if (ReferenceEnhanceKeys.TryGetValue(pair.Key, out field))
                    reference[field] = pair.Value;
                else if (string.Equals(pair.Key, "Enhance", StringComparison.OrdinalIgnoreCase))
                    local = pair.Value as Dictionary<string, object>;
            }
            int applied = ApplyObject(options, reference);
            return local == null ? applied : applied + ApplyObject(options, local);
        }

        private static int ApplyFarmOverrides(FarmOptions farm, Dictionary<string, object> values)
        {
            if (farm == null) return 0;
            farm.BossChallengeIndex = farm.IndexFor(FarmTeleportMode.BossChallenge);
            farm.WeeklyChallengeIndex = farm.IndexFor(FarmTeleportMode.WeeklyChallenge);
            farm.SpecialNightmareIndex = farm.IndexFor(FarmTeleportMode.SpecialNightmare);
            int count = ApplyObject(farm, values);
            string category = farm.TeleportMode + "Index";
            var fields = new HashSet<string>(values.Keys, StringComparer.OrdinalIgnoreCase);
            if (fields.Contains("BossIndex") && !fields.Contains(category))
            {
                PropertyInfo index = Find(typeof(FarmOptions), category);
                if (index != null) index.SetValue(farm, farm.BossIndex, null);
            }
            return count;
        }

        private static int ApplyObject(object target, Dictionary<string, object> values)
        {
            if (target == null)
                return 0;
            int applied = 0;
            Type type = target.GetType();
            foreach (KeyValuePair<string, object> pair in values)
            {
                PropertyInfo property = Find(type, pair.Key);
                if (property == null || !property.CanWrite)
                    continue;
                try
                {
                    property.SetValue(target, ConvertValue(pair.Value, property.PropertyType), null);
                    applied++;
                }
                catch (Exception)
                {
                    if (property.PropertyType == typeof(DailyTask[]) || property.PropertyType == typeof(DailyNightmareMode?))
                        RunLog.Warn("多账号", "已跳过无效的一条龙账号配置 · 字段=" + property.Name);
                }
            }
            return applied;
        }

        private static int ApplyCombat(CombatSettings settings, Dictionary<string, object> values)
        {
            int applied = 0;
            foreach (KeyValuePair<string, object> pair in values)
            {
                try
                {
                    switch (pair.Key.ToLowerInvariant())
                    {
                        case "autotarget": settings.AutoTarget = ToBool(pair.Value); break;
                        case "autorevive": settings.AutoRevive = ToBool(pair.Value); break;
                        case "useliberation": settings.UseLiberation = ToBool(pair.Value); break;
                        case "rotateparty": settings.RotateParty = ToBool(pair.Value); break;
                        case "preferconcerto": settings.PreferConcerto = ToBool(pair.Value); break;
                        case "switchatlowhealth": settings.SwitchAtLowHealth = ToBool(pair.Value); break;
                        case "supportopener": settings.SupportOpener = ToBool(pair.Value); break;
                        case "detectskillkeys": settings.DetectSkillKeys = ToBool(pair.Value); break;
                        case "checklevitator": settings.CheckLevitator = ToBool(pair.Value); break;
                        case "source": settings.Source = (GameSource)Enum.ToObject(typeof(GameSource), ToInt(pair.Value)); break;
                        case "resonancekey": settings.ResonanceKey = Convert.ToString(pair.Value); break;
                        case "echokey": settings.EchoKey = Convert.ToString(pair.Value); break;
                        case "liberationkey": settings.LiberationKey = Convert.ToString(pair.Value); break;
                        case "guidebookkey": case "guidebook key": case "索拉指南":
                            settings.GuidebookKey = Convert.ToString(pair.Value); break;
                        case "toolkey": settings.ToolKey = Convert.ToString(pair.Value); break;
                        default: continue;
                    }
                    applied++;
                }
                catch (Exception) { }
            }
            return applied;
        }

        private static PropertyInfo Find(Type type, string name)
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    return property;
            return null;
        }

        private static object ConvertValue(object value, Type type)
        {
            if (type == typeof(bool)) return ToBool(value);
            if (type == typeof(int)) return ToInt(value);
            if (type == typeof(double)) return Convert.ToDouble(value);
            if (type == typeof(decimal)) return Convert.ToDecimal(value);
            if (type == typeof(string)) return Convert.ToString(value);
            if (type == typeof(DailyTask[])) return ConvertDailyTasks(value);
            if (type == typeof(DailyNightmareMode?))
                return value == null ? null : ConvertDailyEnum(value, typeof(DailyNightmareMode));
            if (type.IsEnum)
            {
                string text = value as string;
                if (text != null) return Enum.Parse(type, text, true);
                return Enum.ToObject(type, ToInt(value));
            }
            if (type == typeof(string[]))
            {
                if (value == null)
                    return null;
                var list = value as IList;
                if (list == null) return new[] { Convert.ToString(value) };
                var result = new string[list.Count];
                for (int index = 0; index < list.Count; index++)
                    result[index] = Convert.ToString(list[index]);
                return result;
            }
            return value;
        }

        private static DailyTask[] ConvertDailyTasks(object value)
        {
            if (value == null)
                return null;
            var items = value as IList;
            if (items == null || items.Count == 0)
                throw new InvalidDataException("一条龙任务须为非空数组。");
            var tasks = new DailyTask[items.Count];
            var selected = new HashSet<DailyTask>();
            for (int index = 0; index < tasks.Length; index++)
            {
                var task = (DailyTask)ConvertDailyEnum(items[index], typeof(DailyTask));
                if (!selected.Add(task))
                    throw new InvalidDataException("一条龙任务不能重复。");
                tasks[index] = task;
            }
            return tasks;
        }

        private static object ConvertDailyEnum(object value, Type enumType)
        {
            string text = value as string;
            object parsed;
            if (text != null && text.IndexOf(',') < 0)
                parsed = Enum.Parse(enumType, text, true);
            else if (value is int || value is long || value != null && value.GetType() == enumType)
                parsed = Enum.ToObject(enumType, ToInt(value));
            else
                throw new InvalidDataException("一条龙配置枚举须为名称或整数。");
            if (!Enum.IsDefined(enumType, parsed))
                throw new InvalidDataException("一条龙配置枚举无效。");
            return parsed;
        }

        private static bool ToBool(object value)
        {
            if (value is bool) return (bool)value;
            return Convert.ToBoolean(value);
        }

        private static int ToInt(object value)
        {
            return Convert.ToInt32(value);
        }

        private static T Clone<T>(T value) where T : class
        {
            if (value == null)
                return null;
            return Serializer.Deserialize<T>(Serializer.Serialize(value));
        }
    }
}
