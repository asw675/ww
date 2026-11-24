using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace TideCombat
{
    internal static class GardenMath
    {
        private static readonly Regex Points = new Regex(@"(?<!\d)([0-9]{1,4})[／/]6000(?!\d)");

        internal static bool IsCompleted(string text)
        {
            string value = Normalize(text);
            if (value.Contains("已达到上限") || value.Contains("已達到上限") || value.Contains("已达上限") ||
                value.Contains("已達上限") || value.IndexOf("LimitReached", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            Match match = Points.Match(value);
            if (match.Success)
                return match.Groups[1].Value == "6000";
            return value == "6000" || value == "本周积分6000" || value == "本週積分6000";
        }

        internal static bool IsFinished(IEnumerable<string> texts)
        {
            return Regex.Matches(string.Join(" ", texts), "6000").Count != 1;
        }

        internal static bool HasWeeklyLimit(IEnumerable<OcrLine> lines)
        {
            return lines.Any(line => line.Score >= .70f && IsCompleted(line.Text));
        }

        internal static string Normalize(string text)
        {
            return Regex.Replace(text ?? string.Empty, @"\s+", string.Empty);
        }

        internal static Point PreferredPoint(Size frame, Rectangle detected, double x, double y)
        {
            Point reference = new Point((int)(frame.Width * x), (int)(frame.Height * y));
            return detected.Contains(reference) ? reference : new Point(detected.X + detected.Width / 2,
                detected.Y + detected.Height / 2);
        }

        internal static int WeeklyClaimSettleMs(bool initial, int attempt)
        {
            return (initial ? 3000 : 2000) - 1000 * attempt;
        }
    }

    internal sealed class GardenReport
    {
        internal string State;
        internal int Actions;
        internal Size Size;
        internal bool Finished;
        internal string Failure;
    }

    // Clicks garden weekly events until the 6000-point limit is reached.
    internal sealed class GardenRunner : IDisposable
    {
        private static readonly string[] Priority = { "garden_get_skip" };
        // The reference searches all garden event templates except two dependent
        // confirmation buttons. Gold and purple may be found even when Skip is
        // not detected. Confirmation is handled after its own initiating action.
        internal static readonly string[] EventLabels =
        {
            "garden_not_interested", "garden_start_game", "garden_confirm", "garden_continue_game",
            "garden_new_stage", "garden_next_day", "garden_shop_buy", "garden_shop_close",
            "garden_tip_close", "garden_unpause", "garden_get_skip", "garden_get_gold", "garden_get_purple"
        };

        private readonly CancellationToken token;
        private readonly WorldSession world;
        private readonly bool ownsWorld;
        private readonly IProgress<GardenReport> progress;
        private readonly Action<string, PixelFrame> recordEvidence;
        private int actions;
        private long lastActivity;
        private bool weeklyRewardVerified;
        internal bool Failed { get; private set; }
        internal string Failure { get; private set; }
        internal bool CompletionVerified { get; private set; }
        internal bool AlreadyCompleted { get; private set; }

        internal GardenRunner(CombatSettings settings, CancellationToken token, IProgress<GardenReport> progress,
            Action<string, PixelFrame> recordEvidence = null, WorldSession session = null)
        {
            this.token = token;
            this.progress = progress;
            this.recordEvidence = recordEvidence;
            ownsWorld = session == null;
            world = session ?? new WorldSession(settings, token);
        }

        internal void Run()
        {
            if (token.IsCancellationRequested) return;
            world.Sleep(500);
            if (token.IsCancellationRequested) return;
            RunLog.Info("周常乐园", "开始");
            Report("打开每周乐园");
            Rectangle garden;
            if (!OpenWeeklyPage(out garden))
            {
                Fail("无法打开每周乐园页面。");
                return;
            }
            int route = world.InputRouteVersion;
            Record("garden-weekly-before");
            if (!ClaimWeeklyRewards(route, initial: true))
            {
                Fail("周常乐园页面中的可领取奖励未得到确认。");
                return;
            }
            if (WeeklyCompleted(route))
            {
                if (!weeklyRewardVerified)
                {
                    Fail("已达到周积分上限，但未确认奖励已经领取。");
                    return;
                }
                AlreadyCompleted = true;
                CompletionVerified = true;
                Record("after-garden-verified");
                Finish();
                return;
            }
            if (!FindGardenEntry(out garden) || !world.ClickOnRoute(GardenMath.PreferredPoint(
                new Size(world.Frame.Width, world.Frame.Height), garden, .246, .486), route))
            {
                Fail("未能点击已识别的周常乐园入口。");
                return;
            }
            world.Sleep(1000);
            while (!token.IsCancellationRequested)
            {
                world.Sleep(100);
                string label;
                Rectangle box;
                bool found = Match(out label, out box);
                world.Sleep(200);
                if (found)
                {
                    if (!Handle(label, box))
                    {
                        if (!token.IsCancellationRequested)
                            Fail("未能发送已识别的乐园操作 " + label + "。");
                        break;
                    }
                    actions++;
                    lastActivity = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
                    continue;
                }
                if (TryFinish())
                    break;
                long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
                if (lastActivity == 0)
                    lastActivity = now;
                if (now - lastActivity > 45000)
                {
                    Fail("45 秒没有识别到乐园操作或结束界面，停止等待。");
                    break;
                }
                world.Sleep(200);
            }
            if (CompletionVerified || token.IsCancellationRequested)
                Finish();
            else if (!Failed)
                Fail("未确认本周乐园积分达到上限。");
        }

        private bool Match(out string label, out Rectangle box)
        {
            if (!world.Tick())
            {
                label = null;
                box = Rectangle.Empty;
                return false;
            }
            label = world.Vision.BestMatchScored(world.Frame, EventLabels, Priority, 20, .82, .72, out box);
            return label != null;
        }

        private bool Handle(string label, Rectangle box)
        {
            int route = world.InputRouteVersion;
            Report("处理 " + label);
            switch (label)
            {
                case "garden_get_skip":
                {
                    world.Sleep(1000);
                    Rectangle gold, purple;
                    bool selected;
                    if (FindWide("garden_get_gold", out gold))
                        selected = world.Click(gold);
                    else if (FindWide("garden_get_purple", out purple))
                        selected = world.Click(purple);
                    else
                        selected = world.Click(0.5, 0.2);
                    if (!selected)
                        return false;
                    world.Sleep(1000);
                    return ConfirmIfVisible("garden_get_confirm_gray", route);
                }
                case "garden_not_interested":
                {
                    var matches = world.FindAll("garden_not_interested", .2, .05, .9, .7, 8, .82, .72);
                    if (matches.Count > 0)
                    {
                        Rectangle target = matches[matches.Count - 1];
                        if (!world.Click(target))
                            return false;
                        world.Sleep(1000);
                    }
                    return matches.Count > 0 && ConfirmIfVisible("garden_not_interested_confirm", route);
                }
                case "garden_start_game":
                    if (!Blessing())
                        return false;
                    break;
            }
            if (!world.Click(box))
                return false;
            world.Sleep(1000);
            return true;
        }

        private bool ConfirmIfVisible(string label, int route)
        {
            Rectangle confirm = Rectangle.Empty;
            bool found = world.WaitUntil(() => world.InputRouteVersion == route &&
                world.FindNear(label, .80, out confirm), 1500);
            if (token.IsCancellationRequested || world.InputRouteVersion != route || !world.Captured)
                return false;
            // Match the reference's post-confirm settle time without clicking an
            // arbitrary location if the expected confirmation never appears.
            if (!found)
                return false;
            Point target = new Point(confirm.X + confirm.Width / 2, confirm.Y + confirm.Height / 2);
            foreach (VisualPattern pattern in PatternBank.For(label))
            {
                Rectangle expected = pattern.Map(world.Frame.Width, 0);
                Point reference = new Point(expected.X + expected.Width / 2, expected.Y + expected.Height / 2);
                if (confirm.Contains(reference))
                {
                    target = reference;
                    break;
                }
            }
            if (!world.ClickOnRoute(target, route))
                return false;
            world.Sleep(1000);
            return !token.IsCancellationRequested && world.InputRouteVersion == route;
        }

        private bool Blessing()
        {
            if (!world.Click(965.0 / 1920, 860.0 / 1080))
                return false;
            world.Sleep(2000);
            if (!world.Click(700.0 / 1920, 666.0 / 1080))
                return false;
            world.Sleep(2000);
            if (!world.Click(1600.0 / 1920, 900.0 / 1080))
                return false;
            world.Sleep(2000);
            return true;
        }

        private bool TryFinish()
        {
            int route = world.InputRouteVersion;
            Rectangle restart, back;
            if (!world.FindNear("a_garden_restart", .80, out restart) ||
                !world.FindNear("a_garden_back", .80, out back))
                return false;
            world.Sleep(2000);
            token.ThrowIfCancellationRequested();
            if (!world.Tick() || world.InputRouteVersion != route)
            {
                Fail("确认乐园结算时游戏窗口已变更或画面不可用。");
                return true;
            }
            if (!world.FindNear("a_garden_restart", .80, out restart) ||
                !world.FindNear("a_garden_back", .80, out back))
                return false;
            var lines = world.Read(0.373, 0.346, 0.859, 0.615);
            var texts = new List<string>(lines.Count);
            foreach (OcrLine line in lines)
                if (line.Score >= .65f)
                    texts.Add(line.Text);
            if (!GardenMath.IsFinished(texts))
            {
                if (!world.ClickOnRoute(restart, route))
                {
                    Fail("识别到乐园未达标，但未能发送重试点击。");
                    return true;
                }
                lastActivity = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
                return false;
            }
            Record("garden-result");
            if (!world.ClickOnRoute(back, route))
            {
                Fail("未能从结算画面返回周常乐园页面。");
                return true;
            }
            world.Sleep(1000);
            if (world.WaitUntil(() =>
            {
                Rectangle start;
                return world.InputRouteVersion == route && world.FindNear("garden_start_game", .80, out start);
            }, 5000))
            {
                if (!world.Key((char)0x1B, route: route))
                {
                    Fail("未能退出乐园入口画面。");
                    return true;
                }
                world.Sleep(800);
            }
            Rectangle quest = Rectangle.Empty;
            bool guide = world.WaitUntil(() => world.InputRouteVersion == route &&
                world.Vision.FindGuidebookQuest(world.Frame, out quest), 30000);
            token.ThrowIfCancellationRequested();
            Rectangle garden;
            if (!guide || !FindGardenEntry(out garden) && !SelectWeeklyTab(route, out garden))
            {
                Fail("退出后未确认回到周常乐园页面。");
                return true;
            }
            if (!WeeklyCompleted(route))
            {
                Fail("返回周常页面后未确认积分达到上限。");
                return true;
            }
            if (!ClaimWeeklyRewards(route) || !weeklyRewardVerified)
            {
                Fail("结算后的可领取奖励未得到确认。");
                return true;
            }
            token.ThrowIfCancellationRequested();
            CompletionVerified = true;
            Record("after-garden-verified");
            return true;
        }

        private bool ClaimWeeklyRewards(int route, bool initial = false)
        {
            // The reference clicks the weekly reward area twice. Handle that
            // same maximum number of claims, but verify each receipt before
            // checking for a second item instead of blindly clicking twice.
            weeklyRewardVerified = false;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!VerifyWeeklyPage(route))
                    return false;
                Rectangle claim;
                if (!FindWeeklyClaim(out claim))
                {
                    // An empty claim OCR is not proof of receipt. At the weekly
                    // limit require an actual received or claimed indication.
                    if (!VerifyWeeklyPage(route))
                        return false;
                    if (!weeklyRewardVerified)
                        weeklyRewardVerified = HasWeeklyClaimedStatus();
                    if (WeeklyCompleted(route) && !weeklyRewardVerified)
                        return false;
                    return world.InputRouteVersion == route && world.Captured;
                }
                Record("garden-reward-before");
                if (!world.ClickOnRoute(GardenMath.PreferredPoint(new Size(world.Frame.Width, world.Frame.Height),
                    claim, .927, .893), route))
                    return false;
                long clickedAt = world.Now;
                if (!world.WaitUntil(() => world.InputRouteVersion == route && HasWeeklyClaimConfirmation(), 5000))
                    return false;
                weeklyRewardVerified = true;
                Record("garden-reward-confirmed");
                if (HasWeeklyRewardReceipt() && (!world.Key((char)0x1B, route: route) ||
                    !world.WaitUntil(() => VerifyWeeklyPage(route), 5000)))
                    return false;
                int remainingDelay = (int)Math.Max(0, GardenMath.WeeklyClaimSettleMs(initial, attempt) - (world.Now - clickedAt));
                if (remainingDelay > 0)
                    world.Sleep(remainingDelay);
            }
            Rectangle remaining;
            return !FindWeeklyClaim(out remaining) && VerifyWeeklyPage(route);
        }

        private bool VerifyWeeklyPage(int route)
        {
            Rectangle entry;
            return world.InputRouteVersion == route && FindGardenEntry(out entry) && world.Captured &&
                world.InputRouteVersion == route;
        }

        private bool FindWeeklyClaim(out Rectangle button)
        {
            button = Rectangle.Empty;
            foreach (OcrLine line in world.Read(.83, .80, .99, .96))
            {
                string text = GardenMath.Normalize(line.Text);
                if (line.Score < .72f || (text != "领取" && text != "領取" && text != "一键领取" && text != "一鍵領取" &&
                    !string.Equals(text, "Claim", StringComparison.OrdinalIgnoreCase)))
                    continue;
                button = line.Box;
                return !button.IsEmpty;
            }
            return false;
        }

        private bool HasWeeklyClaimConfirmation()
        {
            return HasWeeklyRewardReceipt() || HasWeeklyClaimedStatus();
        }

        private bool HasWeeklyClaimedStatus()
        {
            Rectangle entry;
            if (!FindGardenEntry(out entry))
                return false;
            return world.Read(.83, .80, .99, .96).Any(line => line.Score >= .72f &&
                (GardenMath.Normalize(line.Text) == "已领取" || GardenMath.Normalize(line.Text) == "已領取" ||
                 string.Equals(GardenMath.Normalize(line.Text), "Claimed", StringComparison.OrdinalIgnoreCase)));
        }

        private bool HasWeeklyRewardReceipt()
        {
            return world.Read(.26, .13, .74, .38).Any(line => line.Score >= .72f &&
                (GardenMath.Normalize(line.Text) == "获得道具" || GardenMath.Normalize(line.Text) == "獲得道具" ||
                 string.Equals(GardenMath.Normalize(line.Text), "ItemsObtained", StringComparison.OrdinalIgnoreCase)));
        }

        private bool OpenWeeklyPage(out Rectangle garden)
        {
            garden = Rectangle.Empty;
            if (ownsWorld && !EnsureWorld())
                return false;
            var guide = new GuidebookNavigation(world, Report, GuideTab.Quest);
            if (!guide.Open(EnsureWorld))
                return false;
            int route = world.InputRouteVersion;
            world.Sleep(1000);
            return SelectWeeklyTab(route, out garden);
        }

        private bool SelectWeeklyTab(int route, out Rectangle garden)
        {
            garden = Rectangle.Empty;
            Rectangle weekly = Rectangle.Empty;
            if (world.WaitUntil(() => world.InputRouteVersion == route && FindWeeklyTab(out weekly), 5000))
            {
                if (!world.ClickOnRoute(GardenMath.PreferredPoint(new Size(world.Frame.Width, world.Frame.Height),
                    weekly, .343, .129), route))
                    return false;
                world.Sleep(1000);
            }
            else
            {
                // Keep the source's known navigation coordinate as an OCR
                // fallback, but only on the selected quest-guide page.
                if (!world.Tick() || world.InputRouteVersion != route ||
                    !world.Vision.IsGuidebookQuestSelected(world.Frame))
                    return false;
                Point known = new Point((int)(world.Frame.Width * .343), (int)(world.Frame.Height * .129));
                if (!world.ClickOnRoute(known, route))
                    return false;
                world.Sleep(1000);
            }
            Rectangle entry = Rectangle.Empty;
            bool valid = world.WaitUntil(() => world.InputRouteVersion == route && FindGardenEntry(out entry), 5000);
            garden = entry;
            return valid;
        }

        private bool FindWeeklyTab(out Rectangle tab)
        {
            tab = Rectangle.Empty;
            foreach (OcrLine line in world.Read(.22, .067, .53, .201))
            {
                string text = GardenMath.Normalize(line.Text);
                if (line.Score < .70f || (text != "周度游历" && text != "周度遊歷" && text != "周常" && text != "週常" && text != "每周任务" &&
                    text != "每週任務" && text != "周常任务" && !string.Equals(text, "Weekly", StringComparison.OrdinalIgnoreCase)))
                    continue;
                tab = line.Box;
                return !tab.IsEmpty;
            }
            return false;
        }

        private bool FindGardenEntry(out Rectangle entry)
        {
            entry = Rectangle.Empty;
            foreach (OcrLine line in world.Read(.10, .21, .57, .75))
            {
                string text = GardenMath.Normalize(line.Text);
                if (line.Score < .70f || (!text.Contains("乐园") && !text.Contains("樂園") &&
                    !text.Contains("游园") && !text.Contains("遊園") &&
                    text.IndexOf("Garden", StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                entry = line.Box;
                return !entry.IsEmpty;
            }
            return false;
        }

        private bool WeeklyCompleted(int route)
        {
            var lines = world.Read(0.102, 0.793, 0.284, 0.956);
            return world.InputRouteVersion == route && GardenMath.HasWeeklyLimit(lines);
        }

        private bool FindWide(string label, out Rectangle box)
        {
            return world.FindIn(label, .05, .05, .95, .95, 8, .82, .72, out box);
        }

        private bool EnsureWorld()
        {
            return new WorldRecovery(world, Report).EnsureMain(30000, loginPending: ownsWorld) &&
                world.VerifiedWorld && !world.InRealm;
        }

        private void Report(string state)
        {
            RunLog.Info("周常乐园", string.Format("{0} · 已处理操作={1}", state, actions));
            progress.Report(new GardenReport
            {
                State = state, Actions = actions,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Finish()
        {
            RunLog.Info("周常乐园", string.Format("结束 · 操作数={0} · 已取消={1} · 已失败={2}",
                actions, token.IsCancellationRequested, Failed));
            progress.Report(new GardenReport
            {
                State = token.IsCancellationRequested ? "乐园任务已取消" :
                    (AlreadyCompleted ? "周常页面已显示 6000" : "周常乐园结算完成"),
                Actions = actions, Finished = true,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Fail(string message)
        {
            if (token.IsCancellationRequested)
                return;
            RunLog.Warn("周常乐园", "任务停止 · " + message);
            Record("after-garden-unverified");
            Failed = true;
            Failure = message;
            progress.Report(new GardenReport
            {
                State = message, Failure = message, Actions = actions, Finished = true,
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
