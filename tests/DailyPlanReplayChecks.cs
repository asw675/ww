using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TideCombat
{
    internal static class DailyPlanReplayChecks
    {
        // Each simulated child supplies the same kind of evidence as its real
        // task. This replay exercises scheduling and retry, not game recognition.
        private sealed class StepScript
        {
            internal readonly string Entry, Action, Result;

            internal StepScript(string entry, string action, string result)
            {
                Entry = entry;
                Action = action;
                Result = result;
            }
        }

        private static readonly Dictionary<DailyTask, StepScript> Scripts = new Dictionary<DailyTask, StepScript>
        {
            { DailyTask.Nightmare, new StepScript("图鉴聚落入口", "当前聚落开战并清敌", "同名聚落击败进度或新吸收回执") },
            { DailyTask.Stamina, new StepScript("图鉴所选副本", "赢得挑战并领取", "体力扣减、奖励回执或已核实余额不足") },
            { DailyTask.DailyRewards, new StepScript("日常活跃度页", "领取已定位奖励", "没有剩余可领取奖励") },
            { DailyTask.Mail, new StepScript("邮箱", "领取已定位附件", "结算回执或没有可领附件") },
            { DailyTask.BattlePass, new StepScript("先约电台", "读取任务与等级两页并领取", "两页都没有待领取项或已确认赛季关闭") },
            { DailyTask.Garden, new StepScript("周常乐园页", "完成乐园轮次并领取", "积分达到上限且周常奖励已领取") },
            { DailyTask.Merge, new StepScript("背包合成页", "提交已核实满批", "同批合成回执与剩余不足一批") },
            { DailyTask.Farm4C, new StepScript("所选 4C 首领", "击败并结算该轮", "已核实结算次数达到设定轮数") },
            { DailyTask.MonthlyCard, new StepScript("明确月卡弹窗", "点击当前弹窗", "月卡弹窗消失且队伍画面恢复") }
        };

        private static readonly DailyTask[] CustomOrder =
        {
            DailyTask.MonthlyCard, DailyTask.Mail, DailyTask.Stamina, DailyTask.Farm4C, DailyTask.Nightmare,
            DailyTask.Garden, DailyTask.DailyRewards, DailyTask.Merge, DailyTask.BattlePass
        };

        private enum Fault
        {
            None, FirstEntry, FirstAction, FirstResult, RepeatedResult, PendingCommit, FirstExit,
            WrongWindow, Cancel
        }

        private sealed class UnverifiedFrame : InvalidOperationException
        {
            internal UnverifiedFrame(string message) : base(message) { }
        }

        private sealed class Attempt
        {
            internal readonly DailyTask Task;
            internal readonly int Index;
            internal readonly bool Retry;
            internal readonly List<string> Frames = new List<string>();

            internal Attempt(DailyTask task, int index, bool retry)
            {
                Task = task;
                Index = index;
                Retry = retry;
            }
        }

        private sealed class Replay
        {
            private const string WorldFrame = "原游戏窗口结束画面";
            private static readonly IntPtr GameWindow = new IntPtr(314);
            private const uint GameProcess = 159;
            private readonly DailyTask[] tasks;
            private readonly int faultAt;
            private readonly Fault fault;
            internal readonly List<Attempt> Attempts = new List<Attempt>();
            internal readonly List<DailyTask> Completed = new List<DailyTask>();
            internal readonly List<int> Stops = new List<int>();
            internal readonly Dictionary<DailyTask, int> Actions = new Dictionary<DailyTask, int>();
            internal UnverifiedFrame Failure;
            internal bool Cancelled;

            internal Replay(DailyTask[] tasks, int faultAt = -1, Fault fault = Fault.None)
            {
                this.tasks = tasks;
                this.faultAt = faultAt;
                this.fault = fault;
            }

            internal void Run(bool cancelBeforeStart = false)
            {
                using (var cancellation = new CancellationTokenSource())
                {
                    if (cancelBeforeStart) cancellation.Cancel();
                    try
                    {
                        DailyPlan.RunOrderedTasks(tasks, cancellation.Token,
                            (task, index) => RunStep(task, index, cancellation), Stops.Add);
                    }
                    catch (UnverifiedFrame error) { Failure = error; }
                    catch (OperationCanceledException error)
                    {
                        Require(error.CancellationToken == cancellation.Token, "unexpected cancellation source");
                        Cancelled = true;
                    }
                }
            }

            private void RunStep(DailyTask task, int index, CancellationTokenSource cancellation)
            {
                Fault current = faultAt == index ? fault : Fault.None;
                StepScript script = Scripts[task];
                bool confirmed = false, operationPending = false;
                IntPtr seenWindow = GameWindow;
                DailyPlan.ExecuteWithOneRetry(retry =>
                {
                    var attempt = new Attempt(task, index, retry);
                    Attempts.Add(attempt);
                    if (!confirmed)
                    {
                        if (!retry && current == Fault.FirstEntry) throw new UnverifiedFrame(script.Entry);
                        attempt.Frames.Add(script.Entry);
                        if (!retry && current == Fault.FirstAction) throw new UnverifiedFrame(script.Action);
                        attempt.Frames.Add(script.Action);

                        // These cases model a missing result with an explicit
                        // pre-commit readback that allows re-entering the item.
                        if (current == Fault.RepeatedResult || !retry && current == Fault.FirstResult)
                            throw new UnverifiedFrame(script.Result);

                        if (current == Fault.Cancel)
                        {
                            cancellation.Cancel();
                            cancellation.Token.ThrowIfCancellationRequested();
                        }
                        if (current == Fault.WrongWindow)
                        {
                            seenWindow = new IntPtr(271);
                            throw new UnverifiedFrame(script.Result);
                        }

                        int count;
                        Actions.TryGetValue(task, out count);
                        Actions[task] = count + 1;
                        if (current == Fault.PendingCommit)
                        {
                            operationPending = true;
                            throw new UnverifiedFrame(script.Result);
                        }
                        attempt.Frames.Add(script.Result);
                        confirmed = true;
                    }
                    if (!retry && current == Fault.FirstExit) throw new UnverifiedFrame(WorldFrame);
                    attempt.Frames.Add(WorldFrame);
                    Completed.Add(task);
                }, error => !operationPending && !cancellation.IsCancellationRequested &&
                    TaskContinuation.SameWindow(GameWindow, GameProcess, seenWindow, GameProcess));
            }
        }

        internal static void ReferenceAndCustomPlans()
        {
            DailyOptions options = DailyOptions.Defaults();
            AssertPlan(options.GetReferenceRunTasks(false, 20), DailyTask.Stamina, DailyTask.DailyRewards,
                DailyTask.Mail, DailyTask.BattlePass, DailyTask.Garden);
            AssertPlan(options.GetReferenceRunTasks(true, 20), DailyTask.DailyRewards, DailyTask.Mail,
                DailyTask.BattlePass, DailyTask.Garden);

            options.AutoNightmare = true;
            AssertPlan(options.GetReferenceRunTasks(false, 20), DailyTask.Nightmare, DailyTask.Stamina,
                DailyTask.DailyRewards, DailyTask.Mail, DailyTask.BattlePass, DailyTask.Garden);
            options.MergeIfNeeded = true;
            options.Farm4C = true;
            AssertPlan(options.GetReferenceRunTasks(false, 20), DailyTask.Nightmare, DailyTask.Stamina,
                DailyTask.DailyRewards, DailyTask.Mail, DailyTask.BattlePass, DailyTask.Garden, DailyTask.Merge, DailyTask.Farm4C);
            AssertPlan(options.GetReferenceRunTasks(false, DailyPlan.StaminaGoal), DailyTask.Nightmare,
                DailyTask.DailyRewards, DailyTask.Mail, DailyTask.BattlePass, DailyTask.Garden, DailyTask.Merge, DailyTask.Farm4C);

            options.Tasks = (DailyTask[])CustomOrder.Clone();
            options.Validate();
            AssertPlan(options.GetTasks(), CustomOrder);
        }

        internal static void VerificationFailuresAndCancellation()
        {
            for (int index = 0; index < CustomOrder.Length; index++)
            {
                DailyTask task = CustomOrder[index];
                StepScript script = Scripts[task];
                foreach (Fault fault in new[] { Fault.FirstEntry, Fault.FirstAction, Fault.FirstResult, Fault.FirstExit })
                {
                    var recovered = new Replay(CustomOrder, index, fault);
                    recovered.Run();
                    AssertFinished(recovered, CustomOrder);
                    Attempt[] retriedItem = recovered.Attempts.Where(attempt => attempt.Task == task).ToArray();
                    Require(retriedItem.Length == 2 && !retriedItem[0].Retry && retriedItem[1].Retry &&
                        retriedItem.All(attempt => attempt.Index == index) &&
                        recovered.Attempts[index] == retriedItem[0] && recovered.Attempts[index + 1] == retriedItem[1],
                        task + "/" + fault + " did not retry the same position once before the next task");
                    Require((fault == Fault.FirstExit) == retriedItem[0].Frames.Contains(script.Result) &&
                        (fault == Fault.FirstExit) == !retriedItem[1].Frames.Contains(script.Action),
                        task + "/" + fault + " did not preserve the verified result across exit retry");
                }

                foreach (Fault fault in new[] { Fault.RepeatedResult, Fault.PendingCommit, Fault.WrongWindow })
                {
                    var stopped = new Replay(CustomOrder, index, fault);
                    stopped.Run();
                    int attempts = fault == Fault.RepeatedResult ? 2 : 1;
                    Require(stopped.Failure != null && !stopped.Cancelled &&
                        stopped.Completed.SequenceEqual(CustomOrder.Take(index)) && stopped.Attempts.Count == index + attempts &&
                        stopped.Attempts.Skip(index).All(attempt => attempt.Task == task),
                        task + "/" + fault + " touched a later item or lost the failure");
                    if (index + 1 < CustomOrder.Length)
                        Require(stopped.Stops.SequenceEqual(new[] { index }), task + "/" + fault + " reported the wrong stop index");
                    Require(stopped.Actions.ContainsKey(task) == (fault == Fault.PendingCommit) &&
                        (!stopped.Actions.ContainsKey(task) || stopped.Actions[task] == 1),
                        task + "/" + fault + " repeated a pending operation or issued input on the wrong window");
                }

                var cancelled = new Replay(CustomOrder, index, Fault.Cancel);
                cancelled.Run();
                Require(cancelled.Cancelled && cancelled.Failure == null && cancelled.Attempts.Count == index + 1 &&
                    cancelled.Completed.SequenceEqual(CustomOrder.Take(index)) && !cancelled.Actions.ContainsKey(task),
                    task + " dispatched a subsequent operation or a retry after cancellation");
            }

            var preCancelled = new Replay(CustomOrder);
            preCancelled.Run(cancelBeforeStart: true);
            Require(preCancelled.Cancelled && preCancelled.Attempts.Count == 0, "pre-cancelled plan started a task");
        }

        private static void AssertPlan(DailyTask[] actual, params DailyTask[] expected)
        {
            var replay = new Replay(actual);
            replay.Run();
            AssertFinished(replay, expected);
            Require(replay.Attempts.Select(attempt => attempt.Task).SequenceEqual(expected) &&
                replay.Attempts.Select(attempt => attempt.Index).SequenceEqual(Enumerable.Range(0, expected.Length)) &&
                replay.Attempts.All(attempt => !attempt.Retry), "verified tasks should run once in the configured order");
            for (int i = 0; i < replay.Attempts.Count; i++)
            {
                StepScript script = Scripts[expected[i]];
                Require(replay.Attempts[i].Frames.Take(3).SequenceEqual(new[] { script.Entry, script.Action, script.Result }) &&
                    replay.Attempts[i].Frames.Count == 4, expected[i] + " advanced before entry, action, result and exit");
            }
        }

        private static void AssertFinished(Replay replay, DailyTask[] expected)
        {
            Require(replay.Failure == null && !replay.Cancelled && replay.Stops.Count == 0 &&
                replay.Completed.SequenceEqual(expected), "plan did not complete all verified tasks in order");
            Require(expected.All(task => replay.Actions.ContainsKey(task) && replay.Actions[task] == 1),
                "plan failed to retain already verified operations");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
