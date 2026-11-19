using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace TideCombat
{
    // Configuration mirroring the reference "batch enhance echoes" task.
    public sealed class EnhanceOptions
    {
        public bool RequireBothCrit { get; set; }
        public bool RequireValidBeforeCrit { get; set; }
        public double CritTotalMinimum { get; set; }
        public double FirstCritMinimum { get; set; }
        public int ValidCountMinimum { get; set; }
        public bool FirstMustBeValid { get; set; }
        public string[] ValidStats { get; set; }
        public bool PauseAfterSuccess { get; set; }

        public static readonly string[] AllStats =
        {
            "暴击伤害", "暴击", "攻击百分比", "生命百分比", "防御百分比", "攻击", "生命", "防御",
            "共鸣效率", "普攻伤害加成", "重击伤害加成", "共鸣解放伤害加成", "共鸣技能伤害加成"
        };

        public static EnhanceOptions Defaults()
        {
            return new EnhanceOptions
            {
                RequireBothCrit = true, RequireValidBeforeCrit = true, CritTotalMinimum = 13.8,
                FirstCritMinimum = 6.9, ValidCountMinimum = 3, FirstMustBeValid = true,
                ValidStats = new[] { "暴击", "暴击伤害", "攻击百分比" }, PauseAfterSuccess = true
            };
        }

        public void Validate()
        {
            if (ValidStats == null || ValidStats.Length == 0)
                ValidStats = new[] { "暴击", "暴击伤害", "攻击百分比" };
            if (CritTotalMinimum < 0 || CritTotalMinimum > 60 || FirstCritMinimum < 0 || FirstCritMinimum > 30 ||
                ValidCountMinimum < 1 || ValidCountMinimum > 5)
                throw new InvalidDataException("强化条件无效。");
            ValidStats = ValidStats.Where(s => !string.IsNullOrEmpty(s)).ToArray();
        }
    }

    internal sealed class EchoStat
    {
        internal string Name;
        internal double Value;
        internal int Y;
    }

    internal static class EchoRules
    {
        internal static double ParseNumber(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            string normalized = text.Replace('％', '%').Replace(' ', '\0');
            int percent = normalized.IndexOf('%');
            if (percent >= 0)
                normalized = normalized.Substring(0, percent);
            double value;
            return double.TryParse(normalized.Trim('\0'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        internal static List<EchoStat> Parse(List<OcrLine> lines)
        {
            var properties = new List<OcrLine>();
            var values = new List<OcrLine>();
            foreach (OcrLine line in lines)
            {
                string text = line.Text ?? string.Empty;
                if (IsNumber(text))
                    values.Add(line);
                else if (CountChinese(text) >= 2 && !text.Contains("辅音"))
                    properties.Add(line);
            }
            var stats = new List<EchoStat>();
            var remaining = new List<OcrLine>(values);
            foreach (OcrLine property in properties)
            {
                OcrLine paired = null;
                double best = double.MaxValue;
                foreach (OcrLine value in remaining)
                {
                    double distance = Math.Abs(property.Box.Y - value.Box.Y);
                    if (distance >= best) continue;
                    best = distance;
                    paired = value;
                }
                if (paired != null)
                    remaining.Remove(paired);
                stats.Add(new EchoStat
                {
                    Name = Normalize(property.Text, paired == null ? string.Empty : paired.Text),
                    Value = ParseNumber(paired == null ? "0" : paired.Text),
                    Y = property.Box.Y
                });
            }
            return stats;
        }

        private static bool IsNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            int digits = 0;
            foreach (char character in text)
            {
                if (char.IsDigit(character)) { digits++; continue; }
                if (character == '.' || character == '%' || character == '％' || character == ' ') continue;
                return false;
            }
            return digits > 0;
        }

        private static int CountChinese(string text)
        {
            int count = 0;
            foreach (char character in text)
                if (character >= '\u4e00' && character <= '\u9fff')
                    count++;
            return count;
        }

        private static string Normalize(string property, string value)
        {
            string p = property ?? string.Empty;
            bool percent = value != null && (value.Contains("%") || value.Contains("％"));
            if (p.Contains("暴击伤害")) return "暴击伤害";
            if (p.Contains("暴击")) return "暴击";
            if (p.Contains("攻击")) return "攻击" + (percent ? "百分比" : string.Empty);
            if (p.Contains("生命")) return "生命" + (percent ? "百分比" : string.Empty);
            if (p.Contains("防御")) return "防御" + (percent ? "百分比" : string.Empty);
            if (p.Contains("效率")) return "共鸣效率";
            if (p.Contains("普攻")) return "普攻伤害加成";
            if (p.Contains("重击")) return "重击伤害加成";
            if (p.Contains("解放")) return "共鸣解放伤害加成";
            if (p.Contains("技能")) return "共鸣技能伤害加成";
            return p;
        }

        // Behaviour derived from the reference rules, implemented as a small profile
        // plus an ordered list of independent checks instead of a single scan loop.
        private sealed class Profile
        {
            internal int Count;
            internal int Invalid;
            internal int ValidCount;
            internal int Remaining;
            internal bool HasRate, HasDamage;
            internal double Rate, Damage;
            internal bool FirstCritSeen;
            internal string FirstCritName;
            internal double FirstCritValue;
            internal bool FirstCheckedSeen;
            internal string FirstCheckedName;
            internal double FirstCheckedValue;
            internal string InvalidBeforeCrit;
            internal bool FirstStatValid;
        }

        private static Profile Build(List<EchoStat> stats, EnhanceOptions options, HashSet<string> valid)
        {
            var profile = new Profile { Count = stats.Count, Remaining = 5 - stats.Count, FirstStatValid = true };
            bool requireOrder = options.RequireValidBeforeCrit && valid.Contains("暴击") && valid.Contains("暴击伤害");
            bool encounteredCrit = false;
            for (int index = 0; index < stats.Count; index++)
            {
                EchoStat stat = stats[index];
                bool isCrit = stat.Name == "暴击" || stat.Name == "暴击伤害";
                bool isValid = valid.Contains(stat.Name);
                if (requireOrder && !encounteredCrit)
                {
                    if (!isCrit && !isValid && profile.InvalidBeforeCrit == null)
                        profile.InvalidBeforeCrit = stat.Name;
                    if (isCrit)
                        encounteredCrit = true;
                }
                if (!isValid)
                    profile.Invalid++;
                if (index == 0)
                    profile.FirstStatValid = isValid;
                if (!isCrit)
                    continue;
                if (!profile.FirstCritSeen)
                {
                    profile.FirstCritSeen = true;
                    profile.FirstCritName = stat.Name;
                    profile.FirstCritValue = stat.Value;
                }
                if (!profile.FirstCheckedSeen && isValid)
                {
                    profile.FirstCheckedSeen = true;
                    profile.FirstCheckedName = stat.Name;
                    profile.FirstCheckedValue = stat.Value;
                }
                if (stat.Name == "暴击")
                {
                    profile.HasRate = true;
                    profile.Rate += stat.Value;
                }
                else
                {
                    profile.HasDamage = true;
                    profile.Damage += stat.Value;
                }
            }
            profile.ValidCount = profile.Count - profile.Invalid;
            return profile;
        }

        internal static bool Evaluate(List<EchoStat> stats, EnhanceOptions options, out string reason)
        {
            reason = string.Empty;
            if (options == null)
                options = EnhanceOptions.Defaults();
            var valid = new HashSet<string>(options.ValidStats ?? new string[0]);
            Profile profile = Build(stats, options, valid);
            foreach (Func<Profile, EnhanceOptions, string> rule in Rules)
            {
                string failure = rule(profile, options);
                if (failure == null)
                    continue;
                reason = failure;
                return false;
            }
            return true;
        }

        private static readonly Func<Profile, EnhanceOptions, string>[] Rules =
        {
            (profile, options) => profile.InvalidBeforeCrit == null ? null : "双爆前含无效_" + profile.InvalidBeforeCrit,
            (profile, options) =>
            {
                if (!profile.FirstCheckedSeen)
                    return null;
                double value = profile.FirstCheckedName == "暴击伤害"
                    ? profile.FirstCheckedValue / 2 : profile.FirstCheckedValue;
                return value >= options.FirstCritMinimum ? null :
                    "首条" + (profile.FirstCheckedName == "暴击" ? "暴击" : "爆伤") + "不足_" +
                    profile.FirstCheckedValue.ToString("0.0");
            },
            (profile, options) =>
            {
                if (!options.RequireBothCrit)
                    return null;
                int missing = (profile.HasRate ? 0 : 1) + (profile.HasDamage ? 0 : 1);
                return profile.Remaining < missing ? "无法凑齐双爆_缺" + missing : null;
            },
            (profile, options) =>
            {
                if (!profile.HasRate || !profile.HasDamage)
                    return null;
                double score = profile.Rate + profile.Damage / 2;
                return score >= options.CritTotalMinimum ? null : "双爆总计不足_" + score.ToString("0.0");
            },
            (profile, options) =>
            {
                if (profile.Count != 1 || !options.FirstMustBeValid || profile.Invalid != 1)
                    return null;
                return "首条无效";
            },
            (profile, options) =>
            {
                return profile.ValidCount + profile.Remaining >= options.ValidCountMinimum ? null :
                    "有效词条不足_上限" + (profile.ValidCount + profile.Remaining);
            }
        };
    }

    internal enum EnhanceScan { None, Cultivate, EchoSkill, StageInsert, EnhanceButtons, Dialog, Stats }

    internal sealed class EnhanceFrame
    {
        internal int Width = 1600, Height = 900;
        internal List<OcrLine> Lines = new List<OcrLine>();
        internal bool HasCultivate = false;
        internal Rectangle CultivateBox = Rectangle.Empty;
        internal bool LevelZero = false;
        internal bool HasStageInsert = false;
        internal Rectangle StageBox = Rectangle.Empty;
        internal bool HasEnhanceTune = false;
        internal bool HasPlainEnhance = false;
        internal Rectangle EnhanceBox = Rectangle.Empty;
        internal bool Locked = false;
        internal bool Dropped = false;
        internal bool LockKnown = false;
        internal bool DropKnown = false;
    }

    internal enum EnhanceAction { None, Click, Key }

    internal struct EnhanceDecision
    {
        internal EnhanceAction Action;
        internal Point Point;
        internal char Key;
    }

    internal sealed class EchoEnhanceTask
    {
        private enum Step { WaitBag, SelectEcho, Enhancing, AddMaterial, PressEnhance, Dialog, Stats, Trash, Lock, Escaping, Done, Failed }
        private readonly EnhanceOptions options;
        private Step step = Step.WaitBag;
        private long stepAt, nextAction;
        private int sortToggles;
        private string reason = string.Empty;

        internal EchoEnhanceTask(EnhanceOptions options)
        {
            this.options = options;
        }

        internal string Status { get; private set; }
        internal string Failure { get; private set; }
        internal bool Finished { get { return step == Step.Done || step == Step.Failed; } }
        internal bool Paused { get; private set; }

        internal void Resume()
        {
            Paused = false;
            step = Step.Escaping;
            stepAt = 0;
            nextAction = 0;
        }
        internal int Enhanced { get; private set; }
        internal int Discarded { get; private set; }
        internal string ScreenshotKind { get; private set; }
        internal string ScreenshotReason { get; private set; }

        internal void ClearScreenshot()
        {
            ScreenshotKind = null;
            ScreenshotReason = null;
        }

        internal bool WantsIcons { get { return step == Step.Trash || step == Step.Lock; } }

        internal EnhanceScan Scan
        {
            get
            {
                switch (step)
                {
                    case Step.WaitBag:
                    case Step.Enhancing:
                    case Step.Escaping:
                        return EnhanceScan.Cultivate;
                    case Step.SelectEcho:
                        return EnhanceScan.EchoSkill;
                    case Step.AddMaterial:
                        return EnhanceScan.StageInsert;
                    case Step.PressEnhance:
                        return EnhanceScan.EnhanceButtons;
                    case Step.Dialog:
                        return EnhanceScan.Dialog;
                    case Step.Stats:
                        return EnhanceScan.Stats;
                    case Step.Trash:
                    case Step.Lock:
                        return EnhanceScan.None;
                    default:
                        return EnhanceScan.None;
                }
            }
        }

        internal EnhanceDecision Observe(EnhanceFrame frame, long now)
        {
            Status = Describe();
            if (Finished || Paused)
                return default(EnhanceDecision);
            switch (step)
            {
                case Step.WaitBag: return WaitBag(frame, now);
                case Step.SelectEcho: return SelectEcho(frame, now);
                case Step.Enhancing: return Enhancing(frame, now);
                case Step.AddMaterial: return AddMaterial(frame, now);
                case Step.PressEnhance: return PressEnhance(frame, now);
                case Step.Dialog: return HandleDialog(frame, now);
                case Step.Stats: return HandleStats(frame, now);
                case Step.Trash: return Trash(frame, now);
                case Step.Lock: return Lock(frame, now);
                case Step.Escaping: return Escaping(frame, now);
                default: return default(EnhanceDecision);
            }
        }

        private EnhanceDecision WaitBag(EnhanceFrame frame, long now)
        {
            if (!frame.HasCultivate)
            {
                if (now - stepAt > 6000)
                    return Fail("请先在背包的声骸界面筛选并按等级从 0 排序，再开始任务。");
                return default(EnhanceDecision);
            }
            step = Step.SelectEcho;
            stepAt = now;
            nextAction = now;
            sortToggles = 0;
            return default(EnhanceDecision);
        }

        private EnhanceDecision SelectEcho(EnhanceFrame frame, long now)
        {
            if (now < nextAction)
                return default(EnhanceDecision);
            if (frame.LevelZero)
            {
                step = Step.Enhancing;
                stepAt = now;
                nextAction = now + 300;
                return default(EnhanceDecision);
            }
            switch (sortToggles)
            {
                case 0:
                    sortToggles = 1;
                    nextAction = now + 600;
                    return Click(0.13, 0.21, frame);
                case 1:
                case 2:
                    sortToggles++;
                    nextAction = now + 500;
                    return Click(0.35, 0.917, frame);
                case 3:
                    sortToggles = 4;
                    nextAction = now + 600;
                    return Click(0.13, 0.21, frame);
            }
            step = Step.Done;
            Status = "没有可强化的声骸，任务结束";
            return default(EnhanceDecision);
        }

        private EnhanceDecision Enhancing(EnhanceFrame frame, long now)
        {
            if (now < nextAction)
                return default(EnhanceDecision);
            if (!frame.HasCultivate)
            {
                step = Step.AddMaterial;
                stepAt = now;
                nextAction = now + 300;
                return default(EnhanceDecision);
            }
            if (now - stepAt > 5000)
                return Fail("找不到「培养」按钮。");
            nextAction = now + 500;
            return Click(frame.CultivateBox, now);
        }

        private EnhanceDecision AddMaterial(EnhanceFrame frame, long now)
        {
            if (now - stepAt > 5000)
                return Fail("请在强化设置中开启「阶段放入」。");
            if (frame.HasStageInsert)
            {
                nextAction = now + 300;
                return Click(frame.StageBox, now);
            }
            if (now - stepAt > 1200)
            {
                step = Step.PressEnhance;
                stepAt = now;
                nextAction = now + 300;
            }
            return default(EnhanceDecision);
        }

        private EnhanceDecision PressEnhance(EnhanceFrame frame, long now)
        {
            if (now < nextAction)
                return default(EnhanceDecision);
            if (frame.HasEnhanceTune)
            {
                step = Step.Dialog;
                stepAt = now;
                nextAction = now + 1500;
                return Click(frame.EnhanceBox, now);
            }
            if (frame.HasPlainEnhance)
                return Fail("请在强化设置中开启「同时调谐」。");
            if (now - stepAt > 4000)
                return Fail("找不到「强化并调谐」按钮。");
            nextAction = now + 400;
            return default(EnhanceDecision);
        }

        private EnhanceDecision HandleDialog(EnhanceFrame frame, long now)
        {
            if (frame.Lines != null)
            {
                foreach (OcrLine line in frame.Lines)
                {
                    string text = line.Text ?? string.Empty;
                    if (text.Contains("不再提示"))
                    {
                        nextAction = now + 500;
                        return Click(0.49, 0.55, frame);
                    }
                    if (text.Contains("调谐成功") || text.Contains("点击任"))
                    {
                        nextAction = now + 800;
                        return Click(line.Box, now);
                    }
                }
            }
            if (now - stepAt > 2000)
            {
                step = Step.Stats;
                stepAt = now;
                nextAction = now + 300;
            }
            return default(EnhanceDecision);
        }

        private EnhanceDecision HandleStats(EnhanceFrame frame, long now)
        {
            if (now < nextAction)
                return default(EnhanceDecision);
            List<EchoStat> stats = EchoRules.Parse(frame.Lines ?? new List<OcrLine>());
            if (stats.Count == 0)
            {
                if (now - stepAt > 4000)
                    return Fail("无法读取声骸词条。");
                nextAction = now + 500;
                return default(EnhanceDecision);
            }
            bool pass = EchoRules.Evaluate(stats, options, out string why);
            reason = why ?? string.Empty;
            if (!pass)
            {
                step = Step.Trash;
                stepAt = now;
                nextAction = now;
                return default(EnhanceDecision);
            }
            step = stats.Count >= 5 ? Step.Lock : Step.Enhancing;
            stepAt = now;
            nextAction = now + 300;
            return default(EnhanceDecision);
        }

        private EnhanceDecision Trash(EnhanceFrame frame, long now)
        {
            if (now - stepAt > 5000)
                return Fail("无法确认声骸的弃置状态。");
            if (frame.Dropped)
            {
                Discarded++;
                ScreenshotKind = "failed";
                ScreenshotReason = reason;
                step = Step.Escaping;
                stepAt = now;
                nextAction = now;
                return default(EnhanceDecision);
            }
            if (now >= nextAction)
            {
                nextAction = now + 1000;
                return Key('Z');
            }
            return default(EnhanceDecision);
        }

        private EnhanceDecision Lock(EnhanceFrame frame, long now)
        {
            if (now - stepAt > 5000)
                return Fail("无法确认声骸的锁定状态。");
            if (frame.Locked)
            {
                Enhanced++;
                ScreenshotKind = "success";
                ScreenshotReason = string.Empty;
                if (options.PauseAfterSuccess)
                {
                    Paused = true;
                    Status = "找到符合条件的声骸，任务已暂停";
                    return default(EnhanceDecision);
                }
                step = Step.Escaping;
                stepAt = now;
                nextAction = now;
                return default(EnhanceDecision);
            }
            if (now >= nextAction)
            {
                nextAction = now + 1000;
                return Key('C');
            }
            return default(EnhanceDecision);
        }

        private EnhanceDecision Escaping(EnhanceFrame frame, long now)
        {
            if (frame.HasCultivate)
            {
                step = Step.WaitBag;
                stepAt = now;
                nextAction = now;
                return default(EnhanceDecision);
            }
            if (now - stepAt > 10000)
                return Fail("返回背包超时。");
            if (now >= nextAction)
            {
                nextAction = now + 900;
                return Key('\u001b');
            }
            return default(EnhanceDecision);
        }

        private EnhanceDecision Fail(string message)
        {
            step = Step.Failed;
            Failure = message;
            Status = message;
            return default(EnhanceDecision);
        }

        private EnhanceDecision Click(Rectangle box, long now)
        {
            return new EnhanceDecision
            {
                Action = EnhanceAction.Click,
                Point = new Point(box.X + box.Width / 2, box.Y + box.Height / 2)
            };
        }

        private EnhanceDecision Click(double x, double y, EnhanceFrame frame)
        {
            return new EnhanceDecision
            {
                Action = EnhanceAction.Click,
                Point = new Point((int)(x * frame.Width), (int)(y * frame.Height))
            };
        }

        private EnhanceDecision Key(char key)
        {
            return new EnhanceDecision { Action = EnhanceAction.Key, Key = key };
        }

        private string Describe()
        {
            switch (step)
            {
                case Step.WaitBag: return "等待背包声骸界面";
                case Step.SelectEcho: return "选择 0 级声骸";
                case Step.Enhancing: return "点击培养";
                case Step.AddMaterial: return "阶段放入材料";
                case Step.PressEnhance: return "强化并调谐";
                case Step.Dialog: return "处理调谐弹窗";
                case Step.Stats: return "读取词条";
                case Step.Trash: return "弃置不合格声骸";
                case Step.Lock: return "锁定合格声骸";
                case Step.Escaping: return "返回背包";
                case Step.Done: return "已完成";
                default: return "已停止";
            }
        }
    }
}
