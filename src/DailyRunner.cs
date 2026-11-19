using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading;

namespace TideCombat
{
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

        public static DailyOptions Defaults()
        {
            return new DailyOptions
            {
                Farm = DomainKind.Tacet, TacetIndex = 1, ForgeryIndex = 1, Material = DomainOptions.Materials[2],
                FarmNightmareEcho = true, AutoNightmare = false, CheckGarden = true, MergeIfNeeded = false,
                Farm4C = false
            };
        }

        public void Validate()
        {
            if (TacetIndex < 1 || TacetIndex > 21 || ForgeryIndex < 1 || ForgeryIndex > 20)
                throw new System.IO.InvalidDataException("副本序号无效。");
            if (Material == null || Array.IndexOf(DomainOptions.Materials, Material) < 0)
                Material = DomainOptions.Materials[2];
        }
    }

    internal static class DailyPlan
    {
        internal static bool RewardReady(int points) { return points >= 100; }

        internal static bool NeedsStamina(bool rewardReady, int usedStamina)
        {
            return !rewardReady && usedStamina < 180;
        }

        internal static bool NeedsNightmare(bool autoNightmare, bool farmEcho, bool rewardReady, DomainKind farm)
        {
            return autoNightmare || (farmEcho && !rewardReady && farm != DomainKind.Tacet);
        }
    }

    internal sealed class DailyReport
    {
        internal string State;
        internal int UsedStamina;
        internal int Points;
        internal Size Size;
        internal bool Finished;
        internal string Failure;
    }

    // Orchestrates the daily routine: progress check, optional nightmare nest,
    // stamina domains, rewards, mail, battle pass and optional extra tasks.
    internal sealed class DailyRunner : IDisposable
    {
        private static readonly Regex ProgressPattern = new Regex(@"^(\d+)/180$");
        private readonly CombatSettings settings;
        private readonly DailyOptions options;
        private readonly CancellationToken token;
        private readonly WorldSession world;
        private readonly IProgress<DailyReport> progress;
        private int usedStamina, points;

        internal DailyRunner(CombatSettings settings, DailyOptions options, CancellationToken token,
            IProgress<DailyReport> progress)
        {
            this.settings = settings;
            this.options = options;
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
            bool rewardReady = OpenDaily(out usedStamina);
            bool needStamina = DailyPlan.NeedsStamina(rewardReady, usedStamina);
            bool needNightmare = DailyPlan.NeedsNightmare(options.AutoNightmare, options.FarmNightmareEcho,
                rewardReady, options.Farm);
            Report("日常进度读取完成");

            if (needNightmare)
            {
                Report("刷梦魇巢穴");
                var nightmare = new NightmareOptions
                {
                    TacetNest = true, Purification = true, CaptureOnce = !options.AutoNightmare
                };
                RunNested(delegate
                {
                    using (var runner = new NightmareNestRunner(settings, nightmare, token,
                        new Progress<NightmareReport>(delegate { })))
                        runner.Run();
                });
                EnsureWorld();
            }

            if (needStamina)
            {
                Report("刷体力本");
                var domain = new DomainOptions
                {
                    Kind = options.Farm,
                    Index = options.Farm == DomainKind.Tacet ? options.TacetIndex :
                        options.Farm == DomainKind.Forgery ? options.ForgeryIndex : 1,
                    Material = options.Material,
                    MaxStamina = 180 - usedStamina
                };
                int before = usedStamina;
                int reported = 0;
                RunNested(delegate
                {
                    var recorder = new Progress<DomainReport>(delegate (DomainReport report)
                    {
                        if (report.UsedStamina > 0)
                            reported = report.UsedStamina;
                    });
                    using (var runner = new DomainRunner(settings, domain, token, recorder))
                        runner.Run();
                });
                usedStamina = Math.Min(180, before + Math.Max(0, reported));
            }

            Report("领取日常奖励");
            ClaimDaily();
            Report("领取邮件");
            ClaimMail();
            Report("领取战令");
            ClaimBattlePass();

            if (options.CheckGarden)
            {
                Report("检查周常乐园");
                RunNested(delegate
                {
                    using (var runner = new GardenRunner(settings, token, new Progress<GardenReport>(delegate { })))
                        runner.Run();
                });
                EnsureWorld();
            }
            if (options.MergeIfNeeded)
            {
                Report("合并弃置声骸");
                RunNested(delegate
                {
                    using (var runner = new MergeRunner(settings, token, new Progress<MergeReport>(delegate { })))
                        runner.Run();
                });
                EnsureWorld();
            }
            if (options.Farm4C)
            {
                Report("刷 4C 声骸");
                FarmLoop.Run(settings, settings.Farm, new Progress<FarmReport>(delegate { }), token, null);
            }
            Report("一条龙完成");
            progress.Report(new DailyReport
            {
                State = "一条龙完成", UsedStamina = usedStamina, Points = points, Finished = true,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private bool OpenDaily(out int used)
        {
            used = 0;
            if (!OpenQuestBook())
            {
                Fail("无法打开任务图鉴。");
                return false;
            }
            world.Click(0.17, 0.12);
            world.Sleep(1000);
            used = ReadProgress();
            if (used < 0)
            {
                world.Click(0.974, 0.6);
                world.Sleep(1000);
                used = ReadProgress();
            }
            if (used < 0)
                used = 0;
            points = ReadPoints();
            Report(string.Format("日常进度 {0}/180 · 积分 {1}", used, points));
            return DailyPlan.RewardReady(points);
        }

        private int ReadProgress()
        {
            var lines = world.Read(0.1, 0.1, 0.5, 0.75);
            foreach (OcrLine line in lines)
            {
                Match match = ProgressPattern.Match(ChangeMath.Fix(line.Text ?? string.Empty).Trim());
                if (match.Success)
                    return int.Parse(match.Groups[1].Value);
            }
            return -1;
        }

        private int ReadPoints()
        {
            var lines = world.Read(0.19, 0.8, 0.30, 0.93);
            foreach (OcrLine line in lines)
            {
                string digits = Regex.Replace(ChangeMath.Fix(line.Text ?? string.Empty), @"\D", "");
                if (digits.Length > 0 && int.TryParse(digits, out int value))
                    return value;
            }
            return 0;
        }

        private bool OpenQuestBook()
        {
            EnsureWorld();
            world.FunctionKey(2);
            world.Sleep(400);
            world.WaitUntil(delegate { return world.Ui != null && (world.Ui.Book || world.Ui.BookMonsters); }, 5000);
            Rectangle quest;
            if (!world.FindNear("gray_book_quest", .80, out quest))
                return false;
            world.Click(quest);
            world.Sleep(1200);
            return true;
        }

        private void ClaimDaily()
        {
            if (!OpenQuestBook())
                return;
            Rectangle proceed;
            if (!world.FindIn("boss_proceed", .803, .189, .960, .312, 4, .84, .70, out proceed))
            {
                world.Click(0.885, 0.250);
                world.Sleep(2000);
            }
            world.Click(0.930, 0.882);
            world.Sleep(1000);
            EnsureWorld();
        }

        private void ClaimMail()
        {
            world.Key((char)0x1B);
            world.Sleep(1500);
            world.Click(0.64, 0.95);
            world.Sleep(1000);
            world.Click(0.14, 0.9);
            world.Sleep(1000);
            EnsureWorld();
        }

        private void ClaimBattlePass()
        {
            Report("战令");
            world.KeyDown('\u0012');
            world.Sleep(80);
            world.Click(0.86, 0.05);
            world.KeyUp('\u0012');
            world.Sleep(1000);
            if (!HasDigits(0.2, 0.13, 0.32, 0.22))
            {
                EnsureWorld();
                return;
            }
            world.Click(0.04, 0.3);
            world.Sleep(1000);
            world.Click(0.68, 0.91);
            world.Sleep(3000);
            world.Click(0.04, 0.17);
            world.Sleep(2000);
            world.Click(0.68, 0.91);
            world.Sleep(2000);
            world.WaitUntil(delegate
            {
                if (HasDigits(0.2, 0.13, 0.32, 0.22))
                    return true;
                world.Click(0.68, 0.91);
                world.Sleep(1000);
                return false;
            }, 5000);
            EnsureWorld();
        }

        private bool HasDigits(double left, double top, double right, double bottom)
        {
            var lines = world.Read(left, top, right, bottom);
            foreach (OcrLine line in lines)
                if (Regex.IsMatch(ChangeMath.Fix(line.Text ?? string.Empty), @"\d+"))
                    return true;
            return false;
        }

        private void RunNested(Action action)
        {
            try { action(); }
            catch (InputElevationRequiredException) { throw; }
            catch (Exception error) { Report("子任务失败：" + error.Message); }
        }

        private bool EnsureWorld()
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (!world.Tick())
                    return false;
                if (world.TeamHud)
                    return true;
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
                    continue;
                }
                world.Key((char)0x1B);
                world.Sleep(800);
            }
            return world.TeamHud;
        }

        private void Report(string state)
        {
            progress.Report(new DailyReport
            {
                State = state, UsedStamina = usedStamina, Points = points,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Fail(string message)
        {
            progress.Report(new DailyReport
            {
                State = message, Failure = message, Finished = true, UsedStamina = usedStamina, Points = points,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        public void Dispose()
        {
            world.Dispose();
        }
    }
}
