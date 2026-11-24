using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace TideCombat
{
    public enum DailyTask { Nightmare, Stamina, DailyRewards, Mail, BattlePass, Garden, Merge, Farm4C, MonthlyCard }
    public enum DailyNightmareMode { AsNeeded, CaptureOnce, All }

    public sealed class DailyOptions
    {
        public DomainKind Farm { get; set; }
        public int TacetIndex { get; set; }
        public int ForgeryIndex { get; set; }
        public string Material { get; set; }
        public bool FarmNightmareEcho { get; set; }
        public bool AutoNightmare { get; set; }
        public bool CheckGarden { get; set; }
        public bool MergeIfNeeded { get; set; }
        public bool Farm4C { get; set; }
        public bool ExitAfterTask { get; set; }
        public DailyTask[] Tasks { get; set; }
        public DailyNightmareMode? NightmareMode { get; set; }

        public static DailyOptions Defaults()
        {
            var options = new DailyOptions();
            options.SetDefaults();
            return options;
        }

        [OnDeserializing]
        private void ReadDefaults(StreamingContext context) { SetDefaults(); }

        private void SetDefaults()
        {
            Farm = DomainKind.Tacet;
            TacetIndex = ForgeryIndex = 1;
            Material = DomainOptions.Materials[2];
            FarmNightmareEcho = CheckGarden = true;
        }

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(DomainKind), Farm) ||
                TacetIndex < 1 || TacetIndex > 21 || ForgeryIndex < 1 || ForgeryIndex > 20)
                throw new InvalidDataException("副本类型或序号无效。");
            if (Material == null || Array.IndexOf(DomainOptions.Materials, Material) < 0)
                Material = DomainOptions.Materials[2];
            if (NightmareMode.HasValue && !Enum.IsDefined(typeof(DailyNightmareMode), NightmareMode.Value))
                throw new InvalidDataException("梦魇巢穴执行方式无效。");
            if (Tasks == null)
                return;
            if (Tasks.Length == 0)
                throw new InvalidDataException("请至少勾选一个一条龙任务。");
            var seen = new HashSet<DailyTask>();
            foreach (DailyTask task in Tasks)
                if (!Enum.IsDefined(typeof(DailyTask), task) || !seen.Add(task))
                    throw new InvalidDataException("一条龙任务无效或重复。");
        }

        internal DailyTask[] GetTasks()
        {
            if (Tasks != null)
                return Tasks;

            var tasks = new List<DailyTask>();
            bool claimFirst = Farm == DomainKind.Tacet || Farm == DomainKind.Cultivation;
            tasks.Add(DailyTask.Stamina);
            if (!claimFirst && !AutoNightmare && FarmNightmareEcho)
                tasks.Add(DailyTask.Nightmare);
            tasks.Add(DailyTask.DailyRewards);
            if (AutoNightmare || (claimFirst && FarmNightmareEcho))
                tasks.Add(DailyTask.Nightmare);
            tasks.Add(DailyTask.Mail);
            tasks.Add(DailyTask.BattlePass);
            if (CheckGarden) tasks.Add(DailyTask.Garden);
            if (MergeIfNeeded) tasks.Add(DailyTask.Merge);
            if (Farm4C) tasks.Add(DailyTask.Farm4C);
            return tasks.ToArray();
        }

        internal DailyNightmareMode GetNightmareMode()
        {
            return NightmareMode ?? (AutoNightmare ? DailyNightmareMode.All : DailyNightmareMode.AsNeeded);
        }
    }

    internal static class DailyPlan
    {
        internal const int StaminaGoal = 180;
        private static readonly Regex StaminaCounter = new Regex(@"^([0-9]{1,3})/180$");
        private static readonly Regex ActivityCounter = new Regex(
            @"^(?:(?:[今每]日)?活[跃躍](?:度)?[:：]?)?([0-9]{1,3})(/100)?$");

        internal static void OrderRewardsAndNightmare(DailyTask[] tasks, bool claimedTacet)
        {
            int rewards = Array.IndexOf(tasks, DailyTask.DailyRewards);
            int nightmare = Array.IndexOf(tasks, DailyTask.Nightmare);
            if (rewards < 0 || nightmare < 0 || (rewards < nightmare) == claimedTacet)
                return;
            tasks[rewards] = DailyTask.Nightmare;
            tasks[nightmare] = DailyTask.DailyRewards;
        }

        internal static string Label(DailyTask task)
        {
            switch (task)
            {
                case DailyTask.Nightmare: return "梦魇巢穴";
                case DailyTask.Stamina: return "刷体力本";
                case DailyTask.DailyRewards: return "领取日常奖励";
                case DailyTask.Mail: return "领取邮件";
                case DailyTask.BattlePass: return "领取战令";
                case DailyTask.Garden: return "周常乐园";
                case DailyTask.Merge: return "合并弃置声骸";
                case DailyTask.Farm4C: return "刷 4C 声骸";
                case DailyTask.MonthlyCard: return "处理月卡弹窗";
                default: throw new ArgumentOutOfRangeException("task");
            }
        }

        internal static bool RewardReady(int points) { return points >= 100; }

        internal static bool NeedsStamina(bool rewardReady, int usedStamina)
        {
            return !rewardReady && usedStamina < StaminaGoal;
        }

        internal static bool TryReadStamina(IEnumerable<OcrLine> lines, out int used)
        {
            return TryReadCounter(lines, StaminaCounter, StaminaGoal, out used);
        }

        internal static bool TryReadPoints(IEnumerable<OcrLine> lines, out int points)
        {
            return TryReadCounter(lines, ActivityCounter, null, out points);
        }

        private static bool TryReadCounter(IEnumerable<OcrLine> lines, Regex pattern, int? maximum, out int value)
        {
            int? observed = null;
            value = 0;
            foreach (OcrLine line in lines)
            {
                if (line.Box.IsEmpty || line.Score < .70f)
                    continue;
                Match match = pattern.Match(OcrText.Compact(line.Text));
                int number;
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out number))
                    continue;
                if (pattern == ActivityCounter && match.Groups[2].Success && number > 100)
                    continue;
                if (observed.HasValue && observed.Value != number)
                    return false;
                observed = number;
            }
            if (!observed.HasValue)
                return false;
            value = Math.Min(maximum ?? int.MaxValue, observed.Value);
            return true;
        }
    }
}
