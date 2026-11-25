using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

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
            var options = new EnhanceOptions();
            options.SetDefaults();
            return options;
        }

        [OnDeserializing]
        private void ReadDefaults(StreamingContext context) { SetDefaults(); }

        private void SetDefaults()
        {
            RequireBothCrit = RequireValidBeforeCrit = FirstMustBeValid = PauseAfterSuccess = true;
            CritTotalMinimum = 13.8;
            FirstCritMinimum = 6.9;
            ValidCountMinimum = 3;
            ValidStats = new[] { "暴击", "暴击伤害", "攻击百分比" };
        }

        public void Validate()
        {
            if (ValidStats == null)
                ValidStats = new[] { "暴击", "暴击伤害", "攻击百分比" };
            ValidStats = ValidStats.Where(s => !string.IsNullOrWhiteSpace(s)).Select(EnhanceText.Normalize)
                .Distinct().ToArray();
            if (!(CritTotalMinimum >= 0 && CritTotalMinimum <= 60) ||
                !(FirstCritMinimum >= 0 && FirstCritMinimum <= 30) || ValidCountMinimum < 0 || ValidCountMinimum > 5 ||
                ValidStats.Any(stat => Array.IndexOf(AllStats, stat) < 0))
                throw new InvalidDataException("强化条件无效。");
        }
    }

    internal static class EnhanceText
    {
        internal static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            return OcrText.Simplify(text.Trim().Normalize(NormalizationForm.FormKC));
        }
    }

    internal sealed class EchoStat
    {
        internal string Name;
        internal string DisplayValue;
        internal double Value;
        internal int Y;
    }

    internal static class EchoRules
    {
        private static readonly Regex Property = new Regex(@"[\u4e00-\u9fff]{2,}");
        // Background sparkles can put a stray dot before a complete decimal or a dash at the end.
        private static readonly Regex Number = new Regex(
            @"\A(?:[.·] *(?=[0-9]+\.[0-9]))?([0-9]+(?:\.[0-9]+)?) *(%?)(?: *[-−–—])?\z");

        internal static List<EchoStat> Parse(List<OcrLine> lines)
        {
            var properties = new List<OcrLine>();
            var values = new List<OcrLine>();
            foreach (OcrLine line in lines)
            {
                string text = EnhanceText.Normalize(line.Text);
                Match number = Number.Match(text);
                if (number.Success)
                {
                    values.Add(new OcrLine
                    {
                        Box = line.Box, Text = number.Groups[1].Value + number.Groups[2].Value, Score = line.Score
                    });
                }
                else if (!text.Contains("辅音"))
                {
                    Match property = Property.Match(text);
                    if (property.Success && Array.IndexOf(EnhanceOptions.AllStats, Normalize(property.Value, "")) >= 0)
                        properties.Add(new OcrLine { Box = line.Box, Text = property.Value, Score = line.Score });
                }
            }
            var stats = new List<EchoStat>();
            if (properties.Count > 5)
                return stats;
            properties.Sort((left, right) => left.Box.Y.CompareTo(right.Box.Y));
            var remaining = new List<OcrLine>(values);
            foreach (OcrLine property in properties)
            {
                OcrLine paired = null;
                double best = double.MaxValue;
                foreach (OcrLine value in remaining)
                {
                    double distance = Math.Abs(property.Box.Y - value.Box.Y);
                    if (distance > Math.Max(8, (property.Box.Height + value.Box.Height) / 2) || distance >= best)
                        continue;
                    best = distance;
                    paired = value;
                }
                if (paired == null)
                    return new List<EchoStat>();
                remaining.Remove(paired);
                string name = Normalize(property.Text, paired.Text);
                double amount;
                if (!double.TryParse(paired.Text.TrimEnd('%'), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out amount) || amount <= 0 ||
                    Array.IndexOf(EnhanceOptions.AllStats, name) < 0 || stats.Any(stat => stat.Name == name))
                    return new List<EchoStat>();
                stats.Add(new EchoStat
                {
                    Name = name,
                    DisplayValue = paired.Text,
                    Value = amount,
                    Y = property.Box.Y
                });
            }
            return stats;
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

    internal enum EnhanceScan { None, Cultivate, BagCandidate, StageInsert, EnhanceButtons, Dialog, Stats }
    internal enum EchoBagOrder { Unknown, Ascending, Descending }
    internal enum EchoMark { Unknown, Unmarked, Marked }

    internal sealed class EnhanceStatRow
    {
        internal readonly string Name, Value;
        internal readonly bool Valid;

        internal EnhanceStatRow(string name, string value, bool valid)
        {
            Name = name;
            Value = value;
            Valid = valid;
        }
    }

    internal sealed class EnhanceStageReadout
    {
        internal readonly int Round, InvalidCount;
        internal readonly string Verdict, Reason;
        internal readonly EnhanceStatRow[] Rows;

        internal EnhanceStageReadout(int round, List<EchoStat> stats, EnhanceOptions options, bool passed, string reason)
        {
            Round = round;
            var valid = new HashSet<string>(options.ValidStats ?? new string[0]);
            Rows = stats.Select(stat => new EnhanceStatRow(stat.Name, stat.DisplayValue,
                valid.Contains(stat.Name))).ToArray();
            InvalidCount = Rows.Count(row => !row.Valid);
            Verdict = passed ? (stats.Count == 5 ? "锁定" : "继续强化") : "弃置";
            Reason = reason;
        }
    }

    internal sealed class EnhanceFrame
    {
        internal int Width = 1600, Height = 900;
        internal int MaterialCount = -1, CurrentLevel = -1, PreviewLevel = -1, TargetStatCount;
        internal int BagLevel = -1;
        internal bool HasEchoSkillTitle;
        internal bool ZeroLevelCandidate
        {
            get { return FirstEchoSelected && (BagLevel == 0 || BagLevel < 0 && HasEchoSkillTitle); }
        }
        internal EchoBagOrder BagOrder;
        internal List<OcrLine> Lines = new List<OcrLine>();
        internal string Preview;
        internal bool Background, AutoBackground;
        internal bool HasCultivate, FirstEchoSelected, HasEnhancePanel, HasStageInsert, HasClearMaterials, HasEnhanceTune, HasPlainEnhance;
        internal Rectangle CultivateBox, StageBox, EnhanceBox, CheckBox, ConfirmBox, DismissBox;
        internal bool OverflowDialog;
        internal EchoMark LockMark, DropMark;
        internal bool MaterialsStaged { get { return HasClearMaterials || HasEnhanceTune || MaterialCount > 0; } }
    }

    internal enum EnhanceAction { None, Click, Key, ActivateGame }

    internal struct EnhanceDecision
    {
        internal EnhanceAction Action;
        internal Point Point;
        internal char Key;
        internal string Label;
        internal int SettleMs;
        internal bool UseWindowMessage;
        internal Action<long> Sent;
    }

    internal sealed class EchoEnhanceTask
    {
        private enum Step { WaitBag, SelectEcho, Enhancing, AddMaterial, PressEnhance, Dialog, Stats, Trash, Lock, Escaping, Done, Failed }
        private enum SelectionStep { SelectFirst, CheckFirst, WaitForDescending, WaitForAscending, CheckAfterSort }
        private const string LogArea = "强化声骸";
        private const int SelectionTimeoutMs = 15000;
        private const int SelectionRetryDelayMs = 1600;
        private const int CultivateRetryDelayMs = 500;
        private const int StageRetryDelayMs = 300;
        private const int MenuSettleMs = 120;
        private const int ResultQuietMs = 2000;
        private const int ResultStatsSettleMs = 100;
        private const int ResultDismissSettleMs = 1000;
        private const int OverflowConfirmSettleMs = 200;
        private const int ResultRetryDelayMs = 1000;
        private const int BackgroundResultRetryDelayMs = 4500;
        private const int ResultCloseTimeoutMs = 12000;
        private readonly EnhanceOptions options;
        private Step step = Step.WaitBag;
        private SelectionStep selection;
        private long stepAt, nextAction, roundAt, cultivateClickAt, stageClickAt, materialStartedAt, resultReadAt, resultClickAt;
        private long resultShownAt = -1, escapeClickAt = -1, overflowSeenAt = -1;
        private long selectionInputAt;
        private int lastBagLevel, bagLevelSamples;
        private bool selectionRetried;
        private int cultivateClicks, resultClicks, resultSamples;
        private int materialCount, stageClicks, emptyMaterialSamples;
        private int confirmedStatCount;
        private int submittedLevel = -1;
        private string submittedPreview;
        private bool foregroundRequested, dialogSeen, resultClickInBackground;
        private EchoMark lastMark;
        private int markerSamples;
        private bool overflowChecked, overflowConfirmed;
        private string reason = string.Empty;

        internal EchoEnhanceTask(EnhanceOptions options)
        {
            this.options = options;
        }

        internal string Status { get; private set; }
        internal string Failure { get; private set; }
        internal bool Finished { get { return step == Step.Done || step == Step.Failed; } }
        internal bool Paused { get; private set; }
        internal int Enhanced { get; private set; }
        internal int Discarded { get; private set; }
        internal int ItemNumber { get; private set; }
        internal int Round { get; private set; }
        internal EnhanceStageReadout LastStage { get; private set; }
        internal string ScreenshotKind { get; private set; }
        internal string ScreenshotReason { get; private set; }

        internal void Resume(long now)
        {
            if (!Paused)
                return;
            Paused = false;
            Move(Step.Escaping, now);
        }

        internal void ClearScreenshot()
        {
            ScreenshotKind = null;
            ScreenshotReason = null;
        }

        internal bool WantsLock { get { return step == Step.Lock; } }
        internal bool WantsDiscard { get { return step == Step.Trash; } }
        internal bool HasUnclosedResult { get { return resultShownAt >= 0; } }
        internal bool CheckingBagOrder
        {
            get { return selection == SelectionStep.WaitForDescending || selection == SelectionStep.WaitForAscending; }
        }
        internal int RequiredStatCount { get; private set; }

        internal bool HasPendingUpgrade(EnhanceFrame frame)
        {
            return frame.MaterialsStaged || frame.PreviewLevel >= 0 ||
                (frame.CurrentLevel >= 0 && frame.CurrentLevel < submittedLevel);
        }

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
                        return EnhanceScan.BagCandidate;
                    case Step.AddMaterial:
                        return EnhanceScan.StageInsert;
                    case Step.PressEnhance:
                        return EnhanceScan.EnhanceButtons;
                    case Step.Dialog:
                        return EnhanceScan.Dialog;
                    case Step.Stats:
                        return EnhanceScan.Stats;
                    default:
                        return EnhanceScan.None;
                }
            }
        }

        internal bool CheckEnhancePage { get { return step == Step.Enhancing || step == Step.Escaping; } }
        internal bool CheckCultivateCandidate { get { return step == Step.Enhancing; } }

        internal EnhanceDecision Observe(EnhanceFrame frame, long now)
        {
            if (Finished || Paused || now < nextAction)
                return default(EnhanceDecision);
            if (frame.DismissBox.IsEmpty)
                resultSamples = 0;
            if (step == Step.AddMaterial || step == Step.PressEnhance ||
                step == Step.Trash || step == Step.Lock || step == Step.Escaping)
            {
                if (!frame.DismissBox.IsEmpty)
                    return HandleLateResult(frame, now);
                if (HasUnclosedResult && now < resultReadAt)
                    return default(EnhanceDecision);
            }
            switch (step)
            {
                case Step.WaitBag: return WaitBag(frame, now);
                case Step.SelectEcho: return SelectEcho(frame, now);
                case Step.Enhancing: return Enhancing(frame, now);
                case Step.AddMaterial: return AddMaterial(frame, now);
                case Step.PressEnhance: return PressEnhance(frame, now);
                case Step.Dialog:
                case Step.Stats: return HandleResult(frame, now);
                case Step.Trash: return MarkEcho(frame.DropMark, false, now);
                case Step.Lock: return MarkEcho(frame.LockMark, true, now);
                case Step.Escaping: return Escaping(frame, now);
                default: return default(EnhanceDecision);
            }
        }

        private EnhanceDecision WaitBag(EnhanceFrame frame, long now)
        {
            Status = "等待背包声骸界面";
            if (!frame.HasCultivate)
                return Fail("请先在背包声骸页筛选并按等级升序排列。");
            selection = SelectionStep.SelectFirst;
            Move(Step.SelectEcho, now);
            return default(EnhanceDecision);
        }

        private EnhanceDecision SelectEcho(EnhanceFrame frame, long now)
        {
            Status = CheckingBagOrder ? "确认背包等级排序" : "确认首格声骸等级";
            if (now - stepAt > SelectionTimeoutMs)
                return FailSelection(frame);
            if (!frame.HasCultivate)
            {
                bagLevelSamples = 0;
                return default(EnhanceDecision);
            }

            switch (selection)
            {
                case SelectionStep.SelectFirst:
                    return ClickFirstEcho(frame, SelectionStep.CheckFirst, now);
                case SelectionStep.WaitForDescending:
                    return ConfirmBagOrder(frame, EchoBagOrder.Descending, now);
                case SelectionStep.WaitForAscending:
                    return ConfirmBagOrder(frame, EchoBagOrder.Ascending, now);
                default:
                    return ConfirmFirstEcho(frame, now);
            }
        }

        private EnhanceDecision ConfirmFirstEcho(EnhanceFrame frame, long now)
        {
            bool afterSort = selection == SelectionStep.CheckAfterSort;
            if (!frame.FirstEchoSelected || (frame.BagLevel < 0 && (!frame.HasEchoSkillTitle || lastBagLevel > 0)))
            {
                bagLevelSamples = 0;
                if (now - selectionInputAt < SelectionRetryDelayMs)
                    return default(EnhanceDecision);
                if (!selectionRetried)
                    return ClickFirstEcho(frame, selection, now, retry: true);
                return afterSort ? default(EnhanceDecision) : ResetBagOrder(frame, now);
            }

            if (frame.ZeroLevelCandidate)
            {
                ItemNumber++;
                Round = 0;
                LastStage = null;
                reason = string.Empty;
                confirmedStatCount = 0;
                cultivateClicks = 0;
                ResetResultDismissal();
                RunLog.Info(LogArea, "确认列表第一格等级为 +0");
                Evidence("before");
                Move(Step.Enhancing, now);
                return default(EnhanceDecision);
            }
            if (!afterSort)
                return ResetBagOrder(frame, now);
            if (frame.BagOrder != EchoBagOrder.Ascending)
            {
                bagLevelSamples = 0;
                return default(EnhanceDecision);
            }

            if (lastBagLevel != frame.BagLevel)
                bagLevelSamples = 0;
            lastBagLevel = frame.BagLevel;
            if (++bagLevelSamples < 2)
                return default(EnhanceDecision);
            string readout = BagReadout(frame);
            RunLog.Info(LogArea, "确认当前筛选没有 0 级声骸 · " + readout);
            Evidence("selection", readout);
            Move(Step.Done, now);
            Status = "没有可强化的 0 级声骸，首格为 +" + frame.BagLevel + "，任务结束";
            return default(EnhanceDecision);
        }

        private EnhanceDecision ResetBagOrder(EnhanceFrame frame, long now)
        {
            if (frame.BagOrder == EchoBagOrder.Unknown)
                return default(EnhanceDecision);
            return ClickBagSort(frame, frame.BagOrder == EchoBagOrder.Ascending
                ? EchoBagOrder.Descending : EchoBagOrder.Ascending, now);
        }

        private EnhanceDecision ConfirmBagOrder(EnhanceFrame frame, EchoBagOrder expected, long now)
        {
            if (frame.BagOrder == expected)
                return expected == EchoBagOrder.Ascending ? ClickFirstEcho(frame, SelectionStep.CheckAfterSort, now) :
                    ClickBagSort(frame, EchoBagOrder.Ascending, now);
            if (frame.BagOrder != EchoBagOrder.Unknown && !selectionRetried && now - selectionInputAt >= SelectionRetryDelayMs)
                return ClickBagSort(frame, expected, now, retry: true);
            return default(EnhanceDecision);
        }

        private EnhanceDecision ClickFirstEcho(EnhanceFrame frame, SelectionStep next, long now, bool retry = false)
        {
            return ClickBagControl(new Point((int)(frame.Width * .13), (int)(frame.Height * .21)),
                (retry ? "重试" : "") + "选择列表第一格", next, now, retry);
        }

        private EnhanceDecision ClickBagSort(EnhanceFrame frame, EchoBagOrder expected, long now, bool retry = false)
        {
            bool ascending = expected == EchoBagOrder.Ascending;
            return ClickBagControl(new Point((int)(frame.Width * .35), (int)(frame.Height * .917)),
                (retry ? "重试" : "") + "切换等级排序为" + (ascending ? "升序" : "降序"),
                ascending ? SelectionStep.WaitForAscending : SelectionStep.WaitForDescending, now, retry);
        }

        private EnhanceDecision ClickBagControl(Point point, string label, SelectionStep next, long now, bool retry)
        {
            var decision = Click(point, label, now, 550, sent =>
            {
                selection = next;
                selectionRetried = retry;
                selectionInputAt = sent;
                lastBagLevel = -1;
                bagLevelSamples = 0;
                nextAction = sent + 500;
            });
            decision.SettleMs = MenuSettleMs;
            decision.UseWindowMessage = !retry;
            return decision;
        }

        private EnhanceDecision FailSelection(EnhanceFrame frame)
        {
            RunLog.Warn(LogArea, "未确认声骸候选 · " + BagReadout(frame));
            if (CheckingBagOrder)
                return Fail("未确认背包等级排序已切换。");
            if (!frame.FirstEchoSelected)
                return Fail("未确认背包列表第一格已选中。");
            if (frame.BagLevel < 0)
                return Fail("未确认首格声骸的等级。");
            return Fail("未确认背包已按等级升序排列。");
        }

        private static string BagReadout(EnhanceFrame frame)
        {
            string level = frame.BagLevel < 0 ? "未识别" : "+" + frame.BagLevel;
            string order = frame.BagOrder == EchoBagOrder.Ascending ? "等级升序" :
                frame.BagOrder == EchoBagOrder.Descending ? "等级降序" : "未确认等级排序";
            return "首格选中=" + frame.FirstEchoSelected + " · 等级=" + level + " · 排序=" + order +
                " · OCR=" + Readout(frame.Lines);
        }

        private EnhanceDecision Enhancing(EnhanceFrame frame, long now)
        {
            Status = "打开培养界面";
            if (frame.HasEnhancePanel && !frame.HasCultivate)
            {
                BeginMaterial(now);
                return default(EnhanceDecision);
            }
            if (now - stepAt >= 5000)
                return Fail("未确认打开声骸强化界面。");
            if (!frame.HasCultivate)
                return default(EnhanceDecision);
            if (!frame.ZeroLevelCandidate)
            {
                Status = "重新核对将要培养的首格声骸";
                return default(EnhanceDecision);
            }
            // The reference retries the visible Cultivate button every 0.5s
            // for up to five seconds. Still require a fresh selected +0 card
            // and the visible button before each navigation action.
            if (cultivateClicks > 0 && now - cultivateClickAt < CultivateRetryDelayMs)
                return default(EnhanceDecision);

            EnhanceDecision decision = Click(frame.CultivateBox, cultivateClicks == 0 ? "打开培养" : "重试打开培养",
                now, 500, sent =>
                {
                    cultivateClicks++;
                    cultivateClickAt = sent;
                });
            decision.SettleMs = MenuSettleMs;
            decision.UseWindowMessage = cultivateClicks == 0;
            return decision;
        }

        private void BeginMaterial(long now)
        {
            stageClicks = emptyMaterialSamples = 0;
            materialStartedAt = now;
            materialCount = -1;
            submittedPreview = null;
            foregroundRequested = dialogSeen = false;
            Move(Step.AddMaterial, now);
        }

        private EnhanceDecision AddMaterial(EnhanceFrame frame, long now)
        {
            Status = stageClicks == 0 ? "阶段放入材料" : "等待阶段放入生效";
            ReadMaterialState(frame, now);
            if (stageClicks > 0 && frame.MaterialsStaged)
            {
                Evidence("materials", frame.MaterialCount >= 0
                    ? "预选材料数量=" + frame.MaterialCount : "已识别材料预选控件");
                Move(Step.PressEnhance, now);
                return default(EnhanceDecision);
            }
            if (now - materialStartedAt >= 5000)
            {
                RunLog.Warn(LogArea, "未确认材料预选 · OCR=" + Readout(frame.Lines));
                return Fail(materialCount == 0
                    ? "阶段放入后，投入材料仍为 0/50。游戏没有预选声骸经验材料。"
                    : "未确认本轮阶段放入。请检查强化设置和材料。");
            }
            return ClickStageWhenReady(frame, now);
        }

        private EnhanceDecision PressEnhance(EnhanceFrame frame, long now)
        {
            Status = "等待「强化并调谐」";
            ReadMaterialState(frame, now);
            if (frame.HasEnhanceTune && frame.MaterialCount != 0)
                return SubmitEnhance(frame, now);
            if (now - stepAt <= 4500)
                return default(EnhanceDecision);
            RunLog.Warn(LogArea, "未找到组合按钮 · 上一次读到的预选材料数量=" + materialCount +
                " · OCR=" + Readout(frame.Lines));
            if (materialCount == 0 && !frame.MaterialsStaged)
                return Fail("预选材料已清空，未能继续强化并调谐。");
            return Fail(frame.HasPlainEnhance
                ? "游戏仍只显示「强化」。请检查本次预选能否达到下一调谐档。"
                : "找不到「强化并调谐」按钮。");
        }

        private void ReadMaterialState(EnhanceFrame frame, long now)
        {
            if (stageClicks == 0)
                return;
            emptyMaterialSamples = now - stageClickAt >= StageRetryDelayMs &&
                frame.HasStageInsert && frame.MaterialCount == 0 && !frame.MaterialsStaged ? emptyMaterialSamples + 1 : 0;
            if (frame.MaterialCount < 0 || frame.MaterialCount == materialCount)
                return;
            materialCount = frame.MaterialCount;
            RunLog.Info(LogArea, "已读取本档投入材料数量 " + materialCount);
        }

        private EnhanceDecision ClickStageWhenReady(EnhanceFrame frame, long now)
        {
            if (frame.MaterialsStaged || now - materialStartedAt >= 5000 ||
                (stageClicks > 0 && emptyMaterialSamples < 2))
                return default(EnhanceDecision);
            EnhanceDecision decision = Click(frame.StageBox, stageClicks == 0 ? "阶段放入" : "再次阶段放入", now, 300, sent =>
            {
                stageClickAt = sent;
                stageClicks++;
                emptyMaterialSamples = 0;
                materialCount = -1;
                Move(Step.AddMaterial, sent, 300);
            });
            decision.SettleMs = MenuSettleMs;
            decision.UseWindowMessage = stageClicks == 0;
            return decision;
        }

        private EnhanceDecision HandleResult(EnhanceFrame frame, long now)
        {
            Status = "等待游戏确认强化并调谐";
            if (frame.OverflowDialog || !frame.DismissBox.IsEmpty)
                return HandleDialog(frame, now);
            EnhanceDecision wait;
            if (HandleUnchangedPreview(frame, now, out wait))
                return wait;
            if (now < resultReadAt)
                return default(EnhanceDecision);
            if (step == Step.Stats)
                return ReadStats(frame, now);
            Move(Step.Stats, now);
            return default(EnhanceDecision);
        }

        private void WaitForResultQuiet(long now, int actionSettleMs = 0)
        {
            // The source waits for a result-closing input to settle, then
            // performs another full 2s dialog OCR wait and sleeps .1s before
            // reading stats. Re-observing a still-present dialog must never
            // shorten a previously scheduled post-input wait.
            resultReadAt = Math.Max(resultReadAt,
                now + actionSettleMs + ResultQuietMs + ResultStatsSettleMs);
        }

        private void ResultActionSent(long sent, int settleMs)
        {
            nextAction = Math.Max(nextAction, sent + settleMs);
            WaitForResultQuiet(sent, settleMs);
            if (step != Step.Dialog && step != Step.Stats)
                stepAt = resultReadAt;
        }

        private EnhanceDecision HandleDialog(EnhanceFrame frame, long now)
        {
            dialogSeen = true;
            WaitForResultQuiet(now);
            if (!frame.OverflowDialog)
                return DismissResult(frame, now);
            if (now - roundAt > 12000)
                return Fail("未确认材料溢出弹窗已经关闭。");
            Status = "处理材料溢出提示";
            if (frame.ConfirmBox.IsEmpty || overflowConfirmed)
            {
                if (!overflowChecked) overflowSeenAt = -1;
                return default(EnhanceDecision);
            }
            if (!overflowChecked)
            {
                // BaseWWTask.click_skip_dialog_confirm waits .5s after both
                // controls are present, checks the box, waits .5s, confirms,
                // then waits .2s. Never guess a missing control.
                if (frame.CheckBox.IsEmpty)
                {
                    overflowSeenAt = -1;
                    return default(EnhanceDecision);
                }
                if (overflowSeenAt < 0)
                    overflowSeenAt = now;
                if (now - overflowSeenAt < 500)
                    return default(EnhanceDecision);
                return ClickOverflowControl(frame.CheckBox, "勾选本次登录不再提示", now, 500, sent =>
                {
                    overflowChecked = true;
                    nextAction = Math.Max(nextAction, sent + 500);
                });
            }
            return ClickOverflowControl(frame.ConfirmBox, "确认材料溢出提示", now, OverflowConfirmSettleMs, sent =>
            {
                overflowConfirmed = true;
                ResultActionSent(sent, OverflowConfirmSettleMs);
            });
        }

        private EnhanceDecision ClickOverflowControl(Rectangle box, string label, long now, int delay, Action<long> sent)
        {
            EnhanceDecision decision = Click(box, label, now, delay, sent);
            decision.SettleMs = MenuSettleMs;
            decision.UseWindowMessage = true;
            return decision;
        }

        private EnhanceDecision HandleLateResult(EnhanceFrame frame, long now)
        {
            WaitForResultQuiet(now);
            stepAt = resultReadAt;
            lastMark = EchoMark.Unknown;
            markerSamples = 0;
            return DismissResult(frame, now);
        }

        private EnhanceDecision DismissResult(EnhanceFrame frame, long now)
        {
            if (!HasUnclosedResult)
                resultShownAt = now;
            if (now - resultShownAt > ResultCloseTimeoutMs)
                return Fail("未确认调谐结果页已经关闭。");

            Status = resultClicks == 0 ? "关闭调谐结果" : "等待游戏关闭调谐结果";
            if (resultClicks > 0)
            {
                int retryDelay = resultClickInBackground ? BackgroundResultRetryDelayMs : ResultRetryDelayMs;
                if (resultClicks >= 2 || now - resultClickAt < retryDelay || ++resultSamples < 2)
                    return default(EnhanceDecision);
                if (frame.AutoBackground && !foregroundRequested)
                {
                    foregroundRequested = true;
                    resultSamples = 0;
                    nextAction = now + 800;
                    return new EnhanceDecision { Action = EnhanceAction.ActivateGame, Label = "申请切到游戏前台关闭调谐结果" };
                }
            }

            EnhanceDecision dismiss = Click(frame.DismissBox, resultClicks == 0 ? "关闭调谐结果" : "重试关闭调谐结果",
                now, ResultDismissSettleMs, sent =>
                {
                    resultClicks++;
                    resultClickAt = sent;
                    resultClickInBackground = frame.Background;
                    resultSamples = 0;
                    ResultActionSent(sent, ResultDismissSettleMs);
                });
            dismiss.SettleMs = MenuSettleMs;
            dismiss.UseWindowMessage = resultClicks > 0 && !resultClickInBackground;
            return dismiss;
        }

        private void ResetResultDismissal()
        {
            resultShownAt = -1;
            resultClicks = resultSamples = 0;
        }

        private EnhanceDecision SubmitEnhance(EnhanceFrame frame, long now)
        {
            EnhanceDecision decision = Click(frame.EnhanceBox, "强化并调谐",
                now, 500, sent =>
                {
                    submittedPreview = frame.Preview;
                    submittedLevel = frame.PreviewLevel;
                    RequiredStatCount = Math.Max(confirmedStatCount + 1, frame.TargetStatCount);
                    roundAt = sent;
                    resultReadAt = sent + 1500 + ResultQuietMs + ResultStatsSettleMs;
                    overflowChecked = overflowConfirmed = false;
                    overflowSeenAt = -1;
                    ResetResultDismissal();
                    Move(Step.Dialog, sent, 1500);
                });
            decision.SettleMs = MenuSettleMs;
            decision.UseWindowMessage = true;
            return decision;
        }

        private bool HandleUnchangedPreview(EnhanceFrame frame, long now, out EnhanceDecision decision)
        {
            decision = default(EnhanceDecision);
            if (dialogSeen || !frame.HasEnhanceTune ||
                submittedPreview == null || frame.Preview != submittedPreview ||
                EchoRules.Parse(frame.Lines).Count >= RequiredStatCount)
                return false;
            Status = "等待游戏执行强化并调谐";
            if (now - roundAt > 13000)
            {
                decision = Fail("游戏未执行「强化并调谐」，声骸仍停在强化预览。");
                return true;
            }
            // As in the reference, submit each upgrade only once. Keep checking
            // the result to distinguish delayed feedback from a missing click,
            // but do not risk spending materials again on a stale preview.
            return true;
        }

        private EnhanceDecision ReadStats(EnhanceFrame frame, long now)
        {
            Status = "读取并核对词条";
            List<EchoStat> stats = EchoRules.Parse(frame.Lines);
            bool pendingUpgrade = HasPendingUpgrade(frame);
            if (pendingUpgrade || stats.Count < RequiredStatCount)
            {
                if (now - roundAt > 13000 && now - resultReadAt > ResultQuietMs)
                {
                    RunLog.Warn(LogArea, "词条未确认 · OCR=" + Readout(frame.Lines));
                    return Fail(frame.HasEnhanceTune || pendingUpgrade
                        ? "游戏仍停在强化预览或结算画面，未确认本轮升级结果。"
                        : "未确认本轮新增词条，已停止强化。");
                }
                return default(EnhanceDecision);
            }
            confirmedStatCount = stats.Count;
            ResetResultDismissal();
            Round++;
            bool pass = EchoRules.Evaluate(stats, options, out reason);
            LastStage = new EnhanceStageReadout(Round, stats, options, pass, reason);
            RunLog.Info(LogArea, string.Format("第 {0} 只第 {1} 档 · OCR={2} · 配对={3} · 判定={4}",
                ItemNumber, Round, Readout(frame.Lines),
                string.Join("，", stats.Select(stat => stat.Name + "=" + stat.Value.ToString(CultureInfo.InvariantCulture))),
                pass ? (stats.Count == 5 ? "锁定" : "继续") : "弃置 · " + reason));
            Evidence("stages", pass ? null : reason);
            if (!pass)
                Move(Step.Trash, now);
            else if (stats.Count == 5)
                Move(Step.Lock, now);
            else
                BeginMaterial(now);
            return default(EnhanceDecision);
        }

        private static string Readout(List<OcrLine> lines)
        {
            return string.Join(" | ", lines.Select(line => line.Text + "@" + line.Box.Y));
        }

        private EnhanceDecision MarkEcho(EchoMark mark, bool locking, long now)
        {
            Status = locking ? "确认声骸锁定状态" : "确认声骸弃置状态";
            if (mark != lastMark)
                markerSamples = 0;
            lastMark = mark;
            if (mark == EchoMark.Unknown || ++markerSamples < 2)
                return now - stepAt > 5000
                    ? Fail(locking ? "无法确认声骸锁定状态。" : "无法确认声骸弃置状态。")
                    : default(EnhanceDecision);
            if (mark == EchoMark.Marked)
            {
                if (locking)
                    Enhanced++;
                else
                    Discarded++;
                Evidence(locking ? "success" : "failed", locking ? null : reason);
                if (locking && options.PauseAfterSuccess)
                {
                    Paused = true;
                    Status = "找到符合条件的声骸，已暂停，可点击继续";
                }
                else
                    Move(Step.Escaping, now);
                return default(EnhanceDecision);
            }
            if (now - stepAt > 5000)
                return Fail(locking ? "无法确认声骸锁定成功。" : "无法确认声骸弃置成功。");
            EnhanceDecision decision = Key(locking ? 'C' : 'Z', locking ? "锁定当前声骸" : "弃置当前声骸", now, 1000);
            decision.Sent = sent => { markerSamples = 0; };
            return decision;
        }

        private EnhanceDecision Escaping(EnhanceFrame frame, long now)
        {
            Status = "返回背包声骸页";
            if (frame.HasCultivate)
            {
                Move(Step.WaitBag, now, 100);
                return default(EnhanceDecision);
            }
            if (now - stepAt > 10000)
                return Fail("返回背包超时。");
            if (!frame.HasEnhancePanel || escapeClickAt >= 0 && now - escapeClickAt < 4000)
                return default(EnhanceDecision);
            EnhanceDecision escape = Key((char)0x1b, "返回背包", now, 200);
            escape.Sent = sent => escapeClickAt = sent;
            return escape;
        }

        private void Move(Step next, long now, int delay = 0)
        {
            step = next;
            stepAt = now;
            nextAction = now + delay;
            if (next == Step.Escaping)
                escapeClickAt = -1;
            lastMark = EchoMark.Unknown;
            markerSamples = 0;
        }

        internal EnhanceDecision Fail(string message)
        {
            step = Step.Failed;
            Failure = Status = message;
            Evidence("error", message);
            return default(EnhanceDecision);
        }

        private void Evidence(string kind, string details = null)
        {
            ScreenshotKind = kind;
            ScreenshotReason = details;
        }

        private EnhanceDecision Click(Rectangle box, string label, long now, int delay, Action<long> sent = null)
        {
            return box.IsEmpty ? default(EnhanceDecision) :
                Click(new Point(box.X + box.Width / 2, box.Y + box.Height / 2), label, now, delay, sent);
        }

        private EnhanceDecision Click(Point point, string label, long now, int delay, Action<long> sent = null)
        {
            nextAction = now + delay;
            return new EnhanceDecision { Action = EnhanceAction.Click, Point = point, Label = label, Sent = sent };
        }

        private EnhanceDecision Key(char key, string label, long now, int delay)
        {
            nextAction = now + delay;
            return new EnhanceDecision { Action = EnhanceAction.Key, Key = key, Label = label };
        }
    }
}
