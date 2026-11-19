using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace TideCombat
{
    internal static class GardenMath
    {
        internal static bool IsCompleted(string text)
        {
            return text != null && text.Contains("6000");
        }

        internal static bool IsFinished(IEnumerable<string> texts)
        {
            int count = 0;
            foreach (string text in texts)
            {
                if (string.IsNullOrEmpty(text))
                    continue;
                int index = 0;
                while ((index = text.IndexOf("6000", index, StringComparison.Ordinal)) >= 0)
                {
                    count++;
                    index += 4;
                }
            }
            return count != 1;
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
        private readonly IProgress<GardenReport> progress;
        private int actions;
        private long lastActivity;
        internal bool Failed { get; private set; }
        internal string Failure { get; private set; }

        internal GardenRunner(CombatSettings settings, CancellationToken token, IProgress<GardenReport> progress)
        {
            this.token = token;
            this.progress = progress;
            world = new WorldSession(settings, token);
        }

        internal void Run()
        {
            if (!EnsureWorld())
            {
                Fail("无法回到游戏世界。");
                return;
            }
            Report("打开每周乐园");
            if (!OpenWeeklyPage())
            {
                Fail("无法打开每周乐园页面。");
                return;
            }
            if (WeeklyCompleted())
            {
                Report("本周乐园积分已满");
                Finish();
                return;
            }
            world.Click(0.246, 0.486);
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
                    actions++;
                    lastActivity = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
                    if (!Handle(label, box))
                        break;
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
            Finish();
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
                    if (FindWide("garden_get_gold", out Rectangle gold))
                        world.Click(gold);
                    else if (FindWide("garden_get_purple", out Rectangle purple))
                        world.Click(purple);
                    else
                        world.Click(0.5, 0.2);
                    world.Sleep(1000);
                    if (world.FindNear("garden_get_confirm_gray", .80, out Rectangle confirm))
                        world.Click(confirm);
                    return true;
                }
                case "garden_not_interested":
                {
                    var matches = world.FindAll("garden_not_interested", .2, .05, .9, .7, 8, .82, .72);
                    if (matches.Count > 0)
                    {
                        Rectangle target = matches[matches.Count - 1];
                        world.Click(target);
                        world.Sleep(1000);
                    }
                    if (world.FindNear("garden_not_interested_confirm", .80, out Rectangle confirm))
                        world.Click(confirm);
                    return true;
                }
                case "garden_start_game":
                    Blessing();
                    break;
            }
            world.Click(box);
            world.Sleep(1000);
            return true;
        }

        private void Blessing()
        {
            world.Click(965.0 / 1920, 860.0 / 1080);
            world.Sleep(2000);
            world.Click(700.0 / 1920, 666.0 / 1080);
            world.Sleep(2000);
            world.Click(1600.0 / 1920, 900.0 / 1080);
            world.Sleep(2000);
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
                texts.Add(line.Text);
            if (!GardenMath.IsFinished(texts))
            {
                world.Click(restart);
                lastActivity = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
                return false;
            }
            world.Click(back);
            world.Sleep(1000);
            world.WaitFind("garden_start_game", 5000);
            world.Key((char)0x1B);
            world.Sleep(1000);
            if (world.WaitFind("gray_book_quest", 30000))
            {
                world.Click(0.927, 0.893);
                world.Sleep(2000);
                world.Click(0.927, 0.893);
                world.Sleep(1000);
            }
            return true;
        }

        private bool OpenWeeklyPage()
        {
            EnsureWorld();
            world.FunctionKey(2);
            world.Sleep(400);
            world.WaitUntil(delegate { return world.Ui != null && (world.Ui.Book || world.Ui.BookMonsters); }, 5000);
            Rectangle quest;
            if (world.FindNear("gray_book_quest", .80, out quest))
            {
                world.Click(quest);
                world.Sleep(1000);
            }
            world.Click(0.343, 0.129);
            world.Sleep(1000);
            world.Click(0.927, 0.893);
            world.Sleep(3000);
            world.Click(0.927, 0.893);
            world.Sleep(2000);
            return true;
        }

        private bool WeeklyCompleted()
        {
            var lines = world.Read(0.102, 0.793, 0.284, 0.956);
            foreach (OcrLine line in lines)
                if (GardenMath.IsCompleted(line.Text))
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
            for (int attempt = 0; attempt < 3 && !world.TeamHud; attempt++)
            {
                world.Key((char)0x1B);
                world.Sleep(700);
            }
            return world.TeamHud;
        }

        private void Report(string state)
        {
            progress.Report(new GardenReport
            {
                State = state, Actions = actions,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Finish()
        {
            progress.Report(new GardenReport
            {
                State = "乐园任务完成", Actions = actions, Finished = true,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Fail(string message)
        {
            Failed = true;
            Failure = message;
            progress.Report(new GardenReport
            {
                State = message, Failure = message, Actions = actions, Finished = true,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        public void Dispose()
        {
            world.Dispose();
        }
    }
}
