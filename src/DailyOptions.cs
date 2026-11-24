using System;
using System.Collections.Generic;
using System.Drawing;
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
            // v3.8.4 determines daily needs before starting either task, then
            // handles Nightmare Nest before spending stamina. Retain the WW-only
            // cultivation path's stamina-first order (it may do nightmare itself).
            bool cultivate = Farm == DomainKind.Cultivation;
            bool nightmare = AutoNightmare || FarmNightmareEcho;
            if (!cultivate && nightmare) tasks.Add(DailyTask.Nightmare);
            tasks.Add(DailyTask.Stamina);
            tasks.Add(DailyTask.DailyRewards);
            if (cultivate && nightmare) tasks.Add(DailyTask.Nightmare);
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

        internal bool IsReferencePlan
        {
            get
            {
                return Tasks == null && Farm != DomainKind.Cultivation &&
                    GetNightmareMode() == (AutoNightmare ? DailyNightmareMode.All : DailyNightmareMode.AsNeeded);
            }
        }

        // Reference tasks are conditional calls, not artificial skipped steps;
        // the decision uses the single verified initial daily-progress snapshot.
        internal DailyTask[] GetReferenceRunTasks(bool rewardReady, int usedStamina)
        {
            if (!IsReferencePlan) return GetTasks();
            var tasks = new List<DailyTask>();
            foreach (DailyTask task in GetTasks())
            {
                if (task == DailyTask.Nightmare && !(AutoNightmare ||
                    FarmNightmareEcho && !rewardReady && Farm != DomainKind.Tacet)) continue;
                if (task == DailyTask.Stamina && !DailyPlan.NeedsStamina(rewardReady, usedStamina)) continue;
                tasks.Add(task);
            }
            return tasks.ToArray();
        }
    }

    internal static class DailyPlan
    {
        internal const int StaminaGoal = 180;
        private static readonly Regex StaminaCounter = new Regex(@"^([0-9]{1,3})/180$");
        private static readonly Regex ActivityCounter = new Regex(
            @"^(?:(?:[今每]日)?活[跃躍](?:度)?[:：]?)?([0-9]{1,3})(/100)?$");

        // This WW-only task has no v3.8.4 counterpart. Keep its prior
        // reward/capture ordering based on actual cultivation stamina use.
        internal static void OrderCultivationRewardsAndNightmare(DailyTask[] tasks, bool spentStamina)
        {
            int rewards = Array.IndexOf(tasks, DailyTask.DailyRewards);
            int nightmare = Array.IndexOf(tasks, DailyTask.Nightmare);
            if (rewards < 0 || nightmare < 0 || (rewards < nightmare) == spentStamina)
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

        internal static bool RecoverableReferenceTask(DailyTask task)
        {
            return task == DailyTask.Nightmare || task == DailyTask.Garden || task == DailyTask.Merge;
        }

        internal static bool CanAttemptRecovery(DailyTask task, bool referencePlan, bool cultivation, bool normalUnverifiedResult)
        {
            // A normal but unverified child return is distinct from a failed
            // input/combat/route operation. The caller must still confirm a
            // safe world boundary before continuing; the result stays failed.
            if (normalUnverifiedResult && (task == DailyTask.Stamina || task == DailyTask.Nightmare)) return true;
            return referencePlan ? RecoverableReferenceTask(task) :
                !(task == DailyTask.Stamina && !cultivation) && task != DailyTask.Farm4C;
        }

        internal static bool TryReadStamina(IEnumerable<OcrLine> lines, out int used)
        {
            bool uncertain;
            return TryReadStamina(lines, out used, out uncertain);
        }

        internal static bool TryReadStamina(IEnumerable<OcrLine> lines, out int used, out bool uncertain)
        {
            return TryReadCounter(lines, StaminaCounter, StaminaGoal, out used, out uncertain);
        }

        internal static bool TryReadPoints(IEnumerable<OcrLine> lines, out int points)
        {
            bool uncertain;
            return TryReadPoints(lines, out points, out uncertain);
        }

        internal static bool TryReadPoints(IEnumerable<OcrLine> lines, out int points, out bool uncertain)
        {
            return TryReadCounter(lines, ActivityCounter, null, out points, out uncertain);
        }

        // Prefer a reference fixed coordinate only if it is contained by the
        // currently recognized control. This prevents a stale coordinate from
        // targeting a different button when layout or localization differs.
        internal static Point ReferenceControlPoint(Rectangle control, Size size, double x, double y, bool centered = false)
        {
            double width = centered ? Math.Min(size.Width, size.Height * (16.0 / 9)) : size.Width;
            var reference = new Point((int)(size.Width * .5 + (x - .5) * width), (int)(size.Height * y));
            return control.Contains(reference) ? reference : new Point(control.Left + control.Width / 2,
                control.Top + control.Height / 2);
        }

        private static bool TryReadCounter(IEnumerable<OcrLine> lines, Regex pattern, int? maximum, out int value,
            out bool uncertain)
        {
            int? observed = null;
            value = 0;
            uncertain = false;
            foreach (OcrLine line in lines)
            {
                if (line.Box.IsEmpty)
                    continue;
                Match match = pattern.Match(OcrText.Compact(line.Text));
                int number;
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out number))
                    continue;
                if (line.Score < .70f || pattern == ActivityCounter && match.Groups[2].Success && number > 100)
                {
                    uncertain = true;
                    continue;
                }
                if (observed.HasValue && observed.Value != number)
                {
                    uncertain = true;
                    return false;
                }
                observed = number;
            }
            if (!observed.HasValue)
                return false;
            value = Math.Min(maximum ?? int.MaxValue, observed.Value);
            return true;
        }
    }
}
