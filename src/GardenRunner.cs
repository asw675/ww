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

        internal static string Normalize(string text)
        {
            return Regex.Replace(text ?? string.Empty, @"\s+", string.Empty);
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
        private static readonly string[] Priority = { "garden_get_skip", "garden_not_interested_confirm" };
        private static readonly string[] Labels =
        {
            "garden_not_interested", "garden_start_game", "garden_confirm", "garden_continue_game",
            "garden_new_stage", "garden_next_day", "garden_shop_buy", "garden_shop_close",
            "garden_tip_close", "garden_unpause", "garden_get_skip", "garden_not_interested_confirm"
        };

        private readonly CancellationToken token;
        private readonly WorldSession world;
        private readonly bool ownsWorld;
        private readonly IProgress<GardenReport> progress;
        private readonly Action<string, PixelFrame> recordEvidence;
        private int actions;
        private long lastActivity;
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
            Record("garden-weekly-before");
            if (!ClaimWeeklyRewards())
            {
                Fail("周常乐园页面中的可领取奖励未得到确认。");
                return;
            }
            if (WeeklyCompleted())
            {
                AlreadyCompleted = true;
                CompletionVerified = true;
                Record("after-garden-verified");
                Finish();
                return;
            }
            if (!world.Click(garden))
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
            label = world.Vision.BestMatchScored(world.Frame, Labels, Priority, 20, .82, .72, out box);
            return label != null;
        }

        private bool Handle(string label, Rectangle box)
        {
            Report("处理 " + label);
            switch (label)
            {
                case "garden_get_skip":
                {
                    world.Sleep(1000);
                    Rectangle gold, purple, confirm;
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
                    return !world.FindNear("garden_get_confirm_gray", .80, out confirm) || world.Click(confirm);
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
                    Rectangle confirm;
                    return world.FindNear("garden_not_interested_confirm", .80, out confirm)
                        ? world.Click(confirm) : matches.Count > 0;
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
            Rectangle restart, back;
            if (!world.FindNear("a_garden_restart", .80, out restart) ||
                !world.FindNear("a_garden_back", .80, out back))
                return false;
            world.Sleep(2000);
            var lines = world.Read(0.373, 0.346, 0.859, 0.615);
            var texts = new List<string>(lines.Count);
            foreach (OcrLine line in lines)
                if (line.Score >= .65f)
                    texts.Add(line.Text);
            if (!GardenMath.IsFinished(texts))
            {
                if (!world.Click(restart))
                {
                    Fail("识别到乐园未达标，但未能发送重试点击。");
                    return true;
                }
                lastActivity = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
                return false;
            }
            Record("garden-result-limit");
            if (!world.Click(back))
            {
                Fail("结算已达标，但未能返回周常乐园页面。");
                return true;
            }
            world.Sleep(1000);
            if (world.WaitFind("garden_start_game", 5000))
            {
                var entranceLimit = world.Read(.015, .10, .29, .28);
                if (entranceLimit.Any(line => line.Score >= .70f && GardenMath.IsCompleted(line.Text)))
                    Record("garden-entrance-limit");
                world.Key((char)0x1B);
                world.Sleep(800);
            }
            Rectangle quest = Rectangle.Empty;
            if (world.WaitUntil(() => world.Vision.FindGuidebookQuest(world.Frame, out quest), 30000))
            {
                if (!world.Click(.927, .893)) { Fail("周常奖励点击未发送。"); return true; }
                world.Sleep(2000);
                if (!world.Click(.927, .893)) { Fail("周常奖励第二次点击未发送。"); return true; }
                world.Sleep(1000);
            }
            token.ThrowIfCancellationRequested();
            CompletionVerified = true;
            Record("after-garden-verified");
            return true;
        }

        private bool ClaimWeeklyRewards()
        {
            Rectangle claim;
            if (!FindWeeklyClaim(out claim))
                return true;
            Record("garden-reward-before");
            if (!world.Click(claim))
                return false;
            if (!world.WaitUntil(HasWeeklyClaimConfirmation, 5000))
                return false;
            Record("garden-reward-confirmed");
            if (HasWeeklyRewardReceipt())
            {
                if (!world.Key((char)0x1B) ||
                    !world.WaitUntil(() => { Rectangle entry; return FindGardenEntry(out entry); }, 5000))
                    return false;
            }
            // Another claim is still outstanding. The caller can keep this state as an unverified outcome.
            return !FindWeeklyClaim(out claim);
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
            if (HasWeeklyRewardReceipt())
                return true;
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
            var guide = new GuidebookNavigation(world, Report, GuideTab.Quest);
            if (!guide.Open(EnsureWorld))
                return false;
            Rectangle weekly = Rectangle.Empty;
            if (!world.WaitUntil(() => FindWeeklyTab(out weekly), 5000) || !world.Click(weekly))
                return false;
            Rectangle entry = Rectangle.Empty;
            bool valid = world.WaitUntil(() => FindGardenEntry(out entry), 5000);
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

        private bool WeeklyCompleted()
        {
            var lines = world.Read(0.102, 0.793, 0.284, 0.956);
            foreach (OcrLine line in lines)
                if (line.Score >= .70f && (line.Text ?? string.Empty).Contains("6000"))
                    return true;
            return false;
        }

        private bool FindWide(string label, out Rectangle box)
        {
            return world.FindIn(label, .05, .05, .95, .95, 8, .82, .72, out box);
        }

        private bool EnsureWorld()
        {
            if (!world.Tick())
                return false;
            if (world.Ui != null && world.Ui.InRealm)
            {
                world.Key((char)0x1B);
                world.Sleep(800);
                Rectangle confirm;
                if (world.FindNear("gray_confirm_exit_button", .74, out confirm))
                {
                    world.Click(confirm);
                    world.Sleep(1200);
                }
            }
            for (int attempt = 0; attempt < 3 && !world.VerifiedWorld; attempt++)
            {
                world.Key((char)0x1B);
                world.Sleep(700);
            }
            return world.VerifiedWorld && !world.InRealm;
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
