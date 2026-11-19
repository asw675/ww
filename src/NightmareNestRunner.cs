using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading;

namespace TideCombat
{
    public sealed class NightmareOptions
    {
        public bool TacetNest { get; set; }
        public bool Purification { get; set; }
        public bool CaptureOnce { get; set; }

        public static NightmareOptions Defaults()
        {
            return new NightmareOptions { TacetNest = true, Purification = true };
        }

        public void Validate()
        {
            if (!TacetNest && !Purification)
                throw new System.IO.InvalidDataException("请至少选择一种梦魇巢穴。");
        }
    }

    internal enum NestTab { TacetNest, TacetScroll, Purification, PurificationScroll }

    internal sealed class NestTarget
    {
        internal Point Point;
        internal string Key;
        internal NestTab Tab;
    }

    internal static class NestMath
    {
        private static readonly int[] Nests = { 24, 36, 48, 41 };
        private static readonly Regex Count = new Regex(@"(\d{1,2})/(\d{1,2})");

        internal static bool TryIncomplete(string text, out int denominator)
        {
            denominator = 0;
            if (string.IsNullOrEmpty(text))
                return false;
            foreach (Match match in Count.Matches(text))
            {
                int numerator = int.Parse(match.Groups[1].Value);
                int total = int.Parse(match.Groups[2].Value);
                if (numerator != 0 || Array.IndexOf(Nests, total) < 0)
                    continue;
                denominator = total;
                return true;
            }
            return false;
        }

        internal static string CacheKey(NestTab tab, int denominator, int rowY)
        {
            int slot = (int)Math.Round(rowY / 900.0 / 0.02);
            return tab + ":" + denominator + ":" + slot;
        }
    }

    internal sealed class NightmareReport
    {
        internal string State;
        internal int Nests;
        internal Size Size;
        internal bool Finished;
        internal string Failure;
    }

    // Visits every incomplete nightmare nest in the guidebook: teleport, fight,
    // collect the echo, and skip unreachable nests after repeated deaths.
    internal sealed class NightmareNestRunner : IDisposable
    {
        private readonly CombatSettings settings;
        private readonly NightmareOptions options;
        private readonly CancellationToken token;
        private readonly WorldSession world;
        private readonly IProgress<NightmareReport> progress;
        private readonly List<NestTab> queue = new List<NestTab>();
        private readonly HashSet<string> unreachable = new HashSet<string>();
        private readonly Dictionary<string, int> deaths = new Dictionary<string, int>();
        private EchoDetector detector;
        internal bool Failed { get; private set; }
        internal string Failure { get; private set; }
        private int nests;
        private bool captureDone;

        internal NightmareNestRunner(CombatSettings settings, NightmareOptions options, CancellationToken token,
            IProgress<NightmareReport> progress)
        {
            this.settings = settings;
            this.options = options;
            this.token = token;
            this.progress = progress;
            world = new WorldSession(settings, token);
            BuildQueue();
            try
            {
                if (System.IO.File.Exists(EchoModel.ModelPath))
                    detector = new EchoDetector(EchoModel.ModelPath);
            }
            catch (Exception) { detector = null; }
        }

        private void BuildQueue()
        {
            if (options.TacetNest)
            {
                queue.Add(NestTab.TacetNest);
                queue.Add(NestTab.TacetScroll);
            }
            if (options.Purification)
            {
                queue.Add(NestTab.Purification);
                queue.Add(NestTab.PurificationScroll);
            }
        }

        internal void Run()
        {
            if (!EnsureWorld())
            {
                Fail("无法回到游戏世界。");
                return;
            }
            while (!token.IsCancellationRequested)
            {
                NestTarget target = NextNest();
                if (target == null)
                    break;
                Report("处理巢穴 " + (nests + 1));
                FightNest(target);
                nests++;
                if (captureDone)
                    break;
            }
            EnsureWorld();
            string final = captureDone ? "已捕获声骸，任务结束" : "全部巢穴处理完成";
            Report(final);
            progress.Report(new NightmareReport
            {
                State = final, Nests = nests, Finished = true,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private NestTarget NextNest()
        {
            OpenBook();
            while (queue.Count > 0)
            {
                NestTab tab = queue[0];
                SelectTab(tab);
                var lines = world.Read(0.35, 0.25, 1, 0.96);
                foreach (OcrLine line in lines)
                {
                    if (!NestMath.TryIncomplete(line.Text, out int denominator))
                        continue;
                    string key = NestMath.CacheKey(tab, denominator, line.Box.Y);
                    if (unreachable.Contains(key))
                        continue;
                    var target = new NestTarget
                    {
                        Tab = tab,
                        Key = key,
                        Point = new Point((int)(world.Frame.Width * .9), line.Box.Y - (int)(line.Box.Height * .9))
                    };
                    Report("发现未完成巢穴");
                    return target;
                }
                queue.RemoveAt(0);
            }
            return null;
        }

        private void OpenBook()
        {
            EnsureWorld();
            world.FunctionKey(2);
            world.Sleep(400);
            world.WaitUntil(delegate { return world.Ui != null && (world.Ui.Book || world.Ui.BookMonsters); }, 5000);
            Rectangle tab;
            if (world.FindNear("gray_book_boss", .80, out tab))
                world.Click(tab);
            world.Sleep(1500);
        }

        private void SelectTab(NestTab tab)
        {
            switch (tab)
            {
                case NestTab.TacetNest:
                    world.Click(0.24, 0.83);
                    break;
                case NestTab.TacetScroll:
                    world.Click(0.24, 0.83);
                    world.Sleep(500);
                    world.Click(0.973, 0.8806);
                    break;
                case NestTab.Purification:
                    world.Click(0.356, 0.882);
                    world.Sleep(500);
                    world.Click(0.24, 0.86);
                    break;
                default:
                    world.Click(0.356, 0.882);
                    world.Sleep(500);
                    world.Click(0.24, 0.86);
                    world.Sleep(500);
                    world.Click(0.9732, 0.54);
                    break;
            }
            world.Sleep(1500);
        }

        private void FightNest(NestTarget target)
        {
            world.Click(target.Point);
            world.Sleep(1000);
            bool team = world.WaitUntil(delegate
            {
                return world.FindNear("team_start_challenge", .80, out _) ||
                    world.FindNear("team_close", .80, out _);
            }, 8000);
            if (team)
            {
                TeamChallenge();
                WaitTeam(120000);
            }
            else
            {
                if (!Travel(target))
                    return;
                for (int i = 0; i < 3 && !token.IsCancellationRequested; i++)
                {
                    world.Tick();
                    if (world.Ui == null || !world.Ui.FPrompt)
                        break;
                    world.Key('F');
                    world.Sleep(1200);
                    WaitTeam(40000);
                }
                world.Sleep(1500);
                RunForward(10);
            }

            int waitCombat = 10000;
            while (!token.IsCancellationRequested)
            {
                CombatOutcome outcome = CombatRunner.Engage(world, settings, token, 300000, waitCombat);
                waitCombat = 1000;
                if (outcome == CombatOutcome.Dead)
                {
                    int count = deaths.ContainsKey(target.Key) ? deaths[target.Key] + 1 : 1;
                    deaths[target.Key] = count;
                    if (count >= 3)
                    {
                        unreachable.Add(target.Key);
                        Report("死亡 3 次，跳过该巢穴");
                    }
                    return;
                }
                bool dropped = CollectEcho();
                if (dropped && options.CaptureOnce)
                {
                    captureDone = true;
                    Report("已捕获声骸，结束梦魇巢穴任务");
                    return;
                }
                if (!dropped && !team)
                    dropped = RetryAfterTeleport(target);
                world.Sleep(3000);
                world.Tick();
                bool combatAgain = world.Hud != null && (world.Hud.EnemyHealth || world.Hud.Locked);
                if (!combatAgain)
                    break;
            }
            world.Sleep(1000);
        }

        private bool CollectEcho()
        {
            if (WalkFor('S', 2500))
                return Pickup();
            if (WalkFor('W', 5000))
                return Pickup();
            return YoloCollect(30000);
        }

        private bool RetryAfterTeleport(NestTarget target)
        {
            EnsureWorld();
            OpenBook();
            SelectTab(target.Tab);
            world.Click(target.Point);
            world.Sleep(1500);
            if (!Travel(target))
                return false;
            RunForward(2);
            if (WalkFor('S', 2500))
                return Pickup();
            if (WalkFor('W', 5000))
                return Pickup();
            return YoloCollect(30000);
        }

        private bool Pickup()
        {
            world.Tick();
            if (world.Ui == null || !world.Ui.FPrompt)
                return false;
            world.Key('F');
            world.Sleep(900);
            return true;
        }

        private bool WalkFor(char direction, int milliseconds)
        {
            world.KeyDown(direction);
            var watch = Stopwatch.StartNew();
            try
            {
                while (watch.ElapsedMilliseconds < milliseconds && !token.IsCancellationRequested)
                {
                    world.Tick();
                    if (world.Ui != null && world.Ui.FPrompt)
                        return true;
                    world.Token.WaitHandle.WaitOne(100);
                }
            }
            finally
            {
                world.KeyUp(direction);
            }
            return false;
        }

        private bool YoloCollect(int milliseconds)
        {
            if (detector == null)
                return false;
            var watch = Stopwatch.StartNew();
            long lastDetect = -1000, lastTurn = -1000;
            while (watch.ElapsedMilliseconds < milliseconds && !token.IsCancellationRequested)
            {
                world.Tick();
                if (world.Ui != null && world.Ui.FPrompt && Pickup())
                    return true;
                long now = watch.ElapsedMilliseconds;
                if (now - lastDetect >= 400)
                {
                    lastDetect = now;
                    List<EchoBox> echoes;
                    try { echoes = detector.Detect(world.Frame, .5f); }
                    catch (Exception) { return false; }
                    if (echoes.Count > 0)
                    {
                        int center = world.Frame.Width / 2;
                        EchoBox best = echoes[0];
                        float distance = Math.Abs(best.Box.X + best.Box.Width / 2 - center);
                        foreach (EchoBox echo in echoes)
                        {
                            float candidate = Math.Abs(echo.Box.X + echo.Box.Width / 2 - center);
                            if (candidate < distance) { best = echo; distance = candidate; }
                        }
                        int dx = best.Box.X + best.Box.Width / 2 - center;
                        if (Math.Abs(dx) > world.Frame.Width * .08)
                            world.Key(dx > 0 ? 'D' : 'A');
                        else
                        {
                            world.KeyDown('W');
                            world.Token.WaitHandle.WaitOne(250);
                            world.KeyUp('W');
                        }
                        continue;
                    }
                }
                if (now - lastTurn >= 900)
                {
                    lastTurn = now;
                    world.Key('A');
                }
                world.Token.WaitHandle.WaitOne(80);
            }
            return false;
        }

        private bool Travel(NestTarget target)
        {
            Rectangle button;
            bool found = WaitTravel(1000, out button);
            if (found)
            {
                world.Click(button);
                world.Sleep(1000);
                Rectangle confirm;
                if (world.FindNear("confirm_btn_hcenter_vcenter", .80, out confirm))
                {
                    world.Click(confirm);
                    world.Sleep(1000);
                }
                if (world.WaitUntil(delegate { return world.TeamHud || world.InRealm; }, 120000))
                    return true;
            }
            unreachable.Add(target.Key);
            Report("巢穴不可达，跳过");
            world.Key((char)0x1B);
            world.Sleep(800);
            return false;
        }

        private bool WaitTravel(int timeoutMs, out Rectangle button)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                foreach (string label in new[] { "gray_teleport", "fast_travel_custom", "remove_custom" })
                {
                    if (!world.FindNear(label, .72, out button)) continue;
                    return true;
                }
                world.Token.WaitHandle.WaitOne(100);
            }
            button = Rectangle.Empty;
            return false;
        }

        private void TeamChallenge()
        {
            if (world.WaitClick("team_start_challenge", 4000))
            {
                world.Sleep(1000);
                SkipDialog();
            }
        }

        private void SkipDialog()
        {
            Rectangle box;
            if (world.FindNear("skip_dialog_check", .80, out box))
            {
                world.Click(box);
                world.Sleep(400);
            }
            if (world.FindNear("skip_dialog_confirm", .80, out box))
            {
                world.Click(box);
                world.Sleep(600);
            }
        }

        private void RunForward(int seconds)
        {
            world.KeyDown('W');
            try
            {
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < seconds * 1000 && !token.IsCancellationRequested)
                {
                    world.Tick();
                    if (world.Hud != null && (world.Hud.EnemyHealth || world.Hud.Locked))
                        return;
                    world.Token.WaitHandle.WaitOne(100);
                }
            }
            finally
            {
                world.KeyUp('W');
            }
        }

        private void WaitTeam(int timeoutMs)
        {
            world.WaitUntil(delegate { return world.TeamHud; }, timeoutMs);
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
            progress.Report(new NightmareReport
            {
                State = state, Nests = nests,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Fail(string message)
        {
            Failed = true;
            Failure = message;
            progress.Report(new NightmareReport
            {
                State = message, Failure = message, Finished = true, Nests = nests,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        public void Dispose()
        {
            if (detector != null) detector.Dispose();
            world.Dispose();
        }
    }
}
