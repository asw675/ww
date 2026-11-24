using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace TideCombat
{
    internal static class MergeMath
    {
        private static readonly Regex BatchCount = new Regex(@"(?<!\d)([0-9]{1,3})\s*[／/]\s*100(?!\d)");

        internal static bool IsFullBatch(string text)
        {
            int count;
            return TryReadBatch(text, out count) && count == 100;
        }

        internal static bool TryReadBatch(string text, out int count)
        {
            return TryReadBatchLines(new[] { new OcrLine { Text = text, Score = 1 } }, out count);
        }

        internal static bool TryReadBatchLines(IEnumerable<OcrLine> lines, out int count)
        {
            count = -1;
            foreach (OcrLine line in lines)
            {
                if (line.Score < .72f)
                    continue;
                foreach (Match match in BatchCount.Matches(line.Text ?? string.Empty))
                {
                    int candidate;
                    if (!int.TryParse(match.Groups[1].Value, out candidate) || candidate > 100 ||
                        count >= 0 && count != candidate)
                    {
                        count = -1;
                        return false;
                    }
                    count = candidate;
                }
            }
            return count >= 0;
        }

        internal static bool ExplicitlyInsufficient(string text)
        {
            string value = Normalize(text);
            if (!MentionsDiscarded(value))
                return false;
            return value.Contains("不足1000") || value.Contains("不满1000") || value.Contains("不滿1000") ||
                value.Contains("未达到1000") || value.Contains("未達到1000") ||
                value.Contains("未达1000") || value.Contains("未達1000") ||
                value.Contains("少于1000") || value.Contains("少於1000") ||
                value.IndexOf("fewerthan1000", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool MentionsDiscarded(string value)
        {
            return value.Contains("弃置") || value.Contains("棄置") ||
                value.IndexOf("discarded", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static string Normalize(string text)
        {
            return Regex.Replace(text ?? string.Empty, @"\s+", string.Empty);
        }
    }

    internal sealed class MergeReport
    {
        internal string State;
        internal int Batches;
        internal Size Size;
        internal bool Finished;
        internal string Failure;
        internal string Notice;
    }

    // Merges discarded echoes in verified full batches from the bag synthesis UI.
    internal sealed class MergeRunner : IDisposable
    {
        private readonly CancellationToken token;
        private readonly WorldSession world;
        private readonly bool ownsWorld;
        private readonly CombatSettings settings;
        private readonly IProgress<MergeReport> progress;
        private readonly Action<string, PixelFrame> recordEvidence;
        private readonly bool notifyIfInsufficient;
        internal bool Failed { get; private set; }
        internal bool NeedsMoreDiscarded { get; private set; }
        internal bool CompletionVerified { get; private set; }
        internal string Failure { get; private set; }
        private int batches, menuRoute;

        internal MergeRunner(CombatSettings settings, CancellationToken token, IProgress<MergeReport> progress,
            Action<string, PixelFrame> recordEvidence = null, bool notifyIfInsufficient = true, WorldSession session = null)
        {
            this.token = token;
            this.progress = progress;
            this.settings = settings;
            this.recordEvidence = recordEvidence;
            this.notifyIfInsufficient = notifyIfInsufficient;
            ownsWorld = session == null;
            world = session ?? new WorldSession(settings, token);
        }

        internal void Run()
        {
            Failed = NeedsMoreDiscarded = CompletionVerified = false;
            Failure = null;
            batches = 0;
            RunLog.Info("合并声骸", "开始");
            if (!EnsureWorld(loginPending: ownsWorld))
            {
                Fail("无法回到游戏世界。");
                return;
            }
            Report("打开背包");
            char bag = string.IsNullOrEmpty(settings.BagKey) ? 'B' : settings.BagKey[0];
            if (!world.Key(bag) || !world.WaitUntil(() => !world.VerifiedWorld, 5000))
            {
                Fail("未能通过快捷键 " + bag + " 打开背包。");
                return;
            }
            world.Sleep(3000);
            Rectangle cancel = Rectangle.Empty;
            if (!world.WaitUntil(() => ReadMergePrompt(out cancel), 3000))
            {
                if (world.Captured && !world.VerifiedWorld && !world.Vision.IsTerminal(world.Frame) && IsBagPage())
                {
                    NeedsMoreDiscarded = true;
                    Complete("背包未显示满 1000 个弃置声骸的合成入口");
                }
                else
                    Fail("打开背包后未确认弃置声骸合成提示或数量。");
                return;
            }
            Record("merge-prompt-before");
            if (NeedsMoreDiscarded)
            {
                Complete("画面确认弃置声骸不足 1000 个");
                return;
            }
            menuRoute = world.InputRouteVersion;
            if (!world.ClickOnRoute(cancel, menuRoute))
            {
                Fail("未能关闭已识别的弃置声骸提示。");
                return;
            }
            // click_dialog_left_button waits 2s and run() adds 2s more.
            world.Sleep(4000);
            if (!MenuClick(.034, .293, 1000) || !MenuClick(.580, .911, 4000) || !OpenMergePage())
            {
                Fail("未确认已进入合成页面。");
                return;
            }
            while (!token.IsCancellationRequested && !Failed && !CompletionVerified)
                MergeFullBatch();
        }

        private bool ReadMergePrompt(out Rectangle cancel)
        {
            cancel = Rectangle.Empty;
            if (world.VerifiedWorld)
                return false;
            string body = MergeMath.Normalize(string.Join(" ", world.Read(.20, .24, .80, .70)
                .Where(line => line.Score >= .70f).Select(line => line.Text)));
            if (MergeMath.ExplicitlyInsufficient(body))
            {
                NeedsMoreDiscarded = true;
                return true;
            }
            if (!MergeMath.MentionsDiscarded(body) || (!body.Contains("1000") && !body.Contains("合成") &&
                body.IndexOf("synthesis", StringComparison.OrdinalIgnoreCase) < 0))
                return false;
            return world.FindNear("cancel_button_hcenter_vcenter", .80, out cancel) ||
                world.FindNear("cancel_button_highlight_hcenter_vcenter", .80, out cancel);
        }

        private void Complete(string state)
        {
            Record("after-merge-verified");
            RunLog.Info("合并声骸", string.Format("{0} · 已确认合成批数={1}", state, batches));
            if (!EnsureWorld())
            {
                if (!token.IsCancellationRequested)
                    Fail("已核对合成结果，但未确认返回游戏世界。");
                return;
            }
            CompletionVerified = true;
            progress.Report(new MergeReport
            {
                State = state, Batches = batches, Finished = true,
                Notice = NeedsMoreDiscarded && notifyIfInsufficient ? "弃置声骸不足 1000 个" : null,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private bool OpenMergePage()
        {
            Report("打开合成页");
            return MenuClick(.602, .124, 500, centered: true) &&
                MenuClick(.520, .904, 2000, centered: true) &&
                MenuClick(.041, .918, 1000) &&
                MenuClick(.826, .840, 500) && MenuClick(.717, .204, 500) &&
                MenuClick(.041, .918, 500);
        }

        private bool MenuClick(double x, double y, int wait, bool centered = false)
        {
            if (!world.Tick() || world.VerifiedWorld || world.Terminal) return false;
            if (centered)
            {
                double contentWidth = Math.Min(world.Frame.Width, world.Frame.Height * (16.0 / 9));
                x = .5 + (x - .5) * contentWidth / world.Frame.Width;
            }
            var button = new Point((int)(x * world.Frame.Width), (int)(y * world.Frame.Height));
            if (!world.ClickOnRoute(button, menuRoute))
                return false;
            world.Sleep(wait);
            return !token.IsCancellationRequested;
        }

        private bool IsBagPage()
        {
            return world.Read(.02, .035, .34, .24).Where(line => line.Score >= .72f)
                .Select(line => MergeMath.Normalize(line.Text))
                .Any(text => text == "背包" || text == "道具背包" ||
                    string.Equals(text, "Backpack", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(text, "Inventory", StringComparison.OrdinalIgnoreCase));
        }

        private void MergeFullBatch()
        {
            Report("合成第 " + (batches + 1) + " 批");
            Rectangle all = Rectangle.Empty;
            if (!world.FindNear("echo_select_all", .81, out all) &&
                (!MenuClick(.310, .915, 500) ||
                    !world.WaitUntil(() => world.FindNear("echo_select_all", .81, out all), 3500)))
            {
                Fail("未识别到声骸选择页面和全选按钮。");
                return;
            }
            if (!world.ClickOnRoute(all, menuRoute))
            {
                Fail("未能点击已识别的声骸全选按钮。");
                return;
            }
            world.Sleep(1000);
            int count = -1;
            int previous = -1;
            if (!world.WaitUntil(() =>
            {
                if (!ReadBatchCount(out count))
                {
                    previous = -1;
                    return false;
                }
                bool stable = previous == count;
                previous = count;
                return stable;
            }, 3500))
            {
                token.ThrowIfCancellationRequested();
                Fail("未能稳定识别剩余合成批量，无法确认满批已全部合成。");
                return;
            }
            if (count < 100)
            {
                Complete("已确认剩余 " + count + "/100，暂无满批声骸可合成");
                return;
            }
            Record("merge-batch-before");
            if (!MenuClick(.782, .910, 0))
            {
                Fail("未能提交已确认的 100/100 合成批次。");
                return;
            }
            world.WaitForOptionalDialog(menuRoute);
            world.Sleep(3000);
            if (!world.WaitUntil(() => world.InputRouteVersion != menuRoute || HasMergeRewardReceipt(), 6500) ||
                world.InputRouteVersion != menuRoute)
            {
                Fail("已提交合成批次，但未识别到获得奖励或合成成功画面。");
                return;
            }
            Record("merge-batch-confirmed");
            bool returned = MenuClick(.496, .972, 1000, centered: true) &&
                world.WaitUntil(() => world.InputRouteVersion != menuRoute || !HasMergeRewardReceipt(), 4500) &&
                world.InputRouteVersion == menuRoute && world.Captured;
            // Count after the confirmed return, as in the reference's normal
            // path. On a close failure the verified reward still counts.
            batches++;
            if (!returned)
                Fail("已确认合成奖励，但未能返回合成页面核对剩余批量。");
        }

        private bool ReadBatchCount(out int count)
        {
            count = -1;
            return world.InputRouteVersion == menuRoute &&
                MergeMath.TryReadBatchLines(world.Read(.670, .660, .895, .958), out count) &&
                world.Captured && world.InputRouteVersion == menuRoute;
        }

        private bool HasMergeRewardReceipt()
        {
            return world.Read(.21, .12, .79, .38).Where(line => line.Score >= .72f)
                .Select(line => MergeMath.Normalize(line.Text))
                .Any(text => text == "获得道具" || text == "獲得道具" || text == "获得声骸" ||
                    text == "獲得聲骸" || text == "合成成功" ||
                    string.Equals(text, "ItemsObtained", StringComparison.OrdinalIgnoreCase));
        }


        private bool EnsureWorld(bool loginPending = false)
        {
            return new WorldRecovery(world, Report).EnsureMain(30000, loginPending) &&
                world.VerifiedWorld && !world.InRealm;
        }

        private void Report(string state)
        {
            RunLog.Info("合并声骸", string.Format("{0} · 已合成批数={1}", state, batches));
            progress.Report(new MergeReport
            {
                State = state, Batches = batches,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Fail(string message)
        {
            if (token.IsCancellationRequested)
                return;
            RunLog.Warn("合并声骸", "任务停止 · " + message);
            Record("after-merge-unverified");
            Failed = true;
            Failure = message;
            progress.Report(new MergeReport
            {
                State = message, Failure = message, Finished = true, Batches = batches,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Record(string stage)
        {
            if (recordEvidence != null && world.Captured)
                recordEvidence(stage, world.Frame);
        }

        public void Dispose()
        {
            if (ownsWorld) world.Dispose();
        }
    }
}
