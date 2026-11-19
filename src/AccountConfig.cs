using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Web.Script.Serialization;

namespace TideCombat
{
    internal sealed class AccountSnapshot
    {
        internal FarmOptions Farm;
        internal DailyOptions Daily;
        internal EnhanceOptions Enhance;
        internal DomainOptions Domain;
        internal NightmareOptions Nightmare;
        internal ChangeOptions Change;
        internal bool AutoTarget, AutoRevive, UseLiberation, RotateParty, PreferConcerto;
        internal bool SwitchAtLowHealth, SupportOpener, DetectSkillKeys, CheckLevitator;
        internal GameSource Source;
        internal string ResonanceKey, EchoKey, LiberationKey, ToolKey;
    }

    // Applies the per-account "config" object of MultiAccountConfig.json to the
    // shared settings and restores the previous values afterwards.
    internal static class AccountConfig
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        internal static AccountSnapshot Snapshot(CombatSettings settings)
        {
            return new AccountSnapshot
            {
                Farm = Clone(settings.Farm), Daily = Clone(settings.Daily), Enhance = Clone(settings.Enhance),
                Domain = Clone(settings.Domain), Nightmare = Clone(settings.Nightmare), Change = Clone(settings.Change),
                AutoTarget = settings.AutoTarget, AutoRevive = settings.AutoRevive,
                UseLiberation = settings.UseLiberation, RotateParty = settings.RotateParty,
                PreferConcerto = settings.PreferConcerto, SwitchAtLowHealth = settings.SwitchAtLowHealth,
                SupportOpener = settings.SupportOpener, DetectSkillKeys = settings.DetectSkillKeys,
                CheckLevitator = settings.CheckLevitator, Source = settings.Source,
                ResonanceKey = settings.ResonanceKey, EchoKey = settings.EchoKey,
                LiberationKey = settings.LiberationKey, ToolKey = settings.ToolKey
            };
        }

        internal static void Restore(CombatSettings settings, AccountSnapshot snapshot)
        {
            settings.Farm = snapshot.Farm;
            settings.Daily = snapshot.Daily;
            settings.Enhance = snapshot.Enhance;
            settings.Domain = snapshot.Domain;
            settings.Nightmare = snapshot.Nightmare;
            settings.Change = snapshot.Change;
            settings.AutoTarget = snapshot.AutoTarget;
            settings.AutoRevive = snapshot.AutoRevive;
            settings.UseLiberation = snapshot.UseLiberation;
            settings.RotateParty = snapshot.RotateParty;
            settings.PreferConcerto = snapshot.PreferConcerto;
            settings.SwitchAtLowHealth = snapshot.SwitchAtLowHealth;
            settings.SupportOpener = snapshot.SupportOpener;
            settings.DetectSkillKeys = snapshot.DetectSkillKeys;
            settings.CheckLevitator = snapshot.CheckLevitator;
            settings.Source = snapshot.Source;
            settings.ResonanceKey = snapshot.ResonanceKey;
            settings.EchoKey = snapshot.EchoKey;
            settings.LiberationKey = snapshot.LiberationKey;
            settings.ToolKey = snapshot.ToolKey;
        }

        internal static int Apply(CombatSettings settings, Dictionary<string, object> config)
        {
            if (config == null)
                return 0;
            int applied = 0;
            foreach (KeyValuePair<string, object> pair in config)
            {
                var values = pair.Value as Dictionary<string, object>;
                if (values == null)
                    continue;
                switch (pair.Key.ToLowerInvariant())
                {
                    case "farm": applied += ApplyObject(settings.Farm, values); break;
                    case "daily": applied += ApplyObject(settings.Daily, values); break;
                    case "enhance": applied += ApplyObject(settings.Enhance, values); break;
                    case "domain": applied += ApplyObject(settings.Domain, values); break;
                    case "nightmare": applied += ApplyObject(settings.Nightmare, values); break;
                    case "change": applied += ApplyObject(settings.Change, values); break;
                    case "combat": applied += ApplyCombat(settings, values); break;
                }
            }
            return applied;
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
                catch (Exception) { }
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
            if (type.IsEnum)
            {
                if (value is string text) return Enum.Parse(type, text, true);
                return Enum.ToObject(type, ToInt(value));
            }
            if (type == typeof(string[]))
            {
                var list = value as ArrayList;
                if (list == null) return new[] { Convert.ToString(value) };
                var result = new string[list.Count];
                for (int index = 0; index < list.Count; index++)
                    result[index] = Convert.ToString(list[index]);
                return result;
            }
            return value;
        }

        private static bool ToBool(object value)
        {
            if (value is bool flag) return flag;
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
