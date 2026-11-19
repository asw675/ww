using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading;

namespace TideCombat
{
    public enum DomainKind { Tacet, Forgery, Simulation }

    public sealed class DomainOptions
    {
        public DomainKind Kind { get; set; }
        public int Index { get; set; }
        public string Material { get; set; }
        public int MaxStamina { get; set; }

        public static readonly string[] Materials = { "共鸣者经验", "武器经验", "贝币" };

        public static DomainOptions Defaults()
        {
            return new DomainOptions { Kind = DomainKind.Tacet, Index = 1, Material = Materials[2] };
        }

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(DomainKind), Kind) || Index < 1 || Index > 21)
                throw new System.IO.InvalidDataException("体力本配置无效。");
            if (Material == null || Array.IndexOf(Materials, Material) < 0)
                Material = Materials[2];
        }
    }

    internal static class DomainMath
    {
        internal static void ParseStamina(IEnumerable<string> texts, out int current, out int backup, out int total)
        {
            current = backup = total = 0;
            foreach (string text in texts)
            {
                string value = text ?? string.Empty;
                Match pair = Regex.Match(value, @"(?<!\d)(\d+)\s*/\s*(\d+)(?!\d)");
                if (pair.Success)
                {
                    current = int.Parse(pair.Groups[1].Value);
                    continue;
                }
                Match number = Regex.Match(value, @"(?<!\d)(\d+)(?!\d)");
                if (number.Success && backup == 0)
                    backup = int.Parse(number.Groups[1].Value);
            }
            total = current + backup;
        }

        internal static double ScrollOffset(double top, int serial, int total, int[] structure)
        {
            if (total <= 0)
                return .1963;
            double start = Math.Max(.1, Math.Min(.5, top));
            int groups = structure == null ? 1 : structure.Length;
            int crossed = 0, sum = 0;
            if (structure != null)
            {
                foreach (int group in structure)
                {
                    sum += group;
                    if (serial > sum) crossed++;
                    else break;
                }
            }
            double item = (.8806 - start - Math.Max(0, groups - 1) * .028) / total;
            return Math.Min(.8806, start + item * serial + crossed * .028);
        }
    }

    internal sealed class DomainReport
    {
        internal string State;
        internal int Runs;
        internal int UsedStamina;
        internal Size Size;
        internal bool Finished;
        internal string Failure;
    }

    // Port of ok-ww DomainTask / TacetTask / ForgeryTask / SimulationTask.
    internal sealed class DomainRunner : IDisposable
    {
        private static readonly int[] TacetStructure = { 4, 5, 5, 7 };
        private static readonly int[] ForgeryStructure = { 5, 5, 5, 5 };
        private readonly CombatSettings settings;
        private readonly DomainOptions options;
        private readonly CancellationToken token;
        private readonly WorldSession world;
        private readonly IProgress<DomainReport> progress;
        private readonly int once;
        private int usedStamina, runs;
        private bool healPoint;

        internal DomainRunner(CombatSettings settings, DomainOptions options, CancellationToken token,
            IProgress<DomainReport> progress)
        {
            this.settings = settings;
            this.options = options;
            this.token = token;
            this.progress = progress;
            world = new WorldSession(settings, token);
            once = options.Kind == DomainKind.Tacet ? 60 : 40;
        }

        internal void Run()
        {
            if (!EnsureWorld())
            {
                Fail("无法回到游戏世界。");
                return;
            }
            int mustUse = options.MaxStamina, recovery = 0;
            while (!token.IsCancellationRequested)
            {
                Report("读取体力");
                if (!OpenBookAndStamina(out int current, out int backup, out int total))
                {
                    Fail("无法读取体力或打开图鉴。");
                    return;
                }
                if (total < once || (mustUse == 0 && current < once))
                {
                    Report("体力不足，任务结束");
                    return;
                }
                Report("传送进本");
                if (!TeleportIntoDomain())
                {
                    Fail("传送进副本失败。");
                    return;
                }
                world.Sleep(1000);
                bool finished = FarmInDomain(ref mustUse);
                runs++;
                if (finished)
                {
                    Report("体力用完，任务结束");
                    return;
                }
                recovery++;
                if (recovery >= 3)
                {
                    Fail("死亡恢复次数超限，停止刷取。");
                    return;
                }
                Report("死亡恢复后重新进本");
                EnsureWorld();
                world.Sleep(1000);
            }
        }

        private bool FarmInDomain(ref int mustUse)
        {
            while (!token.IsCancellationRequested)
            {
                Report("进入战斗");
                WalkUntilF(4000);
                PickF(true);
                CombatOutcome combat = CombatRunner.Engage(world, settings, token, 300000, 5000);
                if (combat != CombatOutcome.Finished)
                {
                    EnsureWorld();
                    if (!healPoint)
                    {
                        TeleportToHeal();
                        healPoint = true;
                    }
                    return false;
                }
                world.Sleep(3000);
                WalkToTreasure();
                PickF(false);
                if (!HasClaimStamina())
                {
                    EscCancel();
                    continue;
                }
                if (!UseStamina(ref mustUse, out bool canContinue))
                    return false;
                world.Sleep(4000);
                if (!canContinue)
                {
                    world.Click(options.Kind == DomainKind.Tacet ? 0.365 : 0.42, 0.853);
                    EnsureWorld();
                    return true;
                }
                world.Click(options.Kind == DomainKind.Tacet ? 0.640 : 0.68,
                    options.Kind == DomainKind.Tacet ? 0.851 : 0.84);
                SkipDialog();
                WaitTeam();
                world.Sleep(1000);
            }
            return false;
        }

        private bool OpenBookAndStamina(out int current, out int backup, out int total)
        {
            current = backup = total = 0;
            EnsureWorld();
            world.FunctionKey(2);
            world.Sleep(300);
            if (!world.WaitUntil(delegate { return world.Ui != null && (world.Ui.Book || world.Ui.BookMonsters); }, 5000))
                return false;
            Rectangle tab;
            if (world.FindNear("gray_book_boss", .80, out tab))
                world.Click(tab);
            world.Sleep(1500);
            world.Click(0.24, KindRow());
            world.Sleep(2000);
            if (ReadStamina(out current, out backup, out total))
                return true;
            world.Click(0.04, 0.4);
            world.Sleep(1000);
            return ReadStamina(out current, out backup, out total);
        }

        private double KindRow()
        {
            switch (options.Kind)
            {
                case DomainKind.Forgery: return .40;
                case DomainKind.Simulation: return .30;
                default: return .73;
            }
        }

        private bool ReadStamina(out int current, out int backup, out int total)
        {
            current = backup = total = 0;
            List<OcrLine> lines = world.Read(0.49, 0, 0.92, 0.10);
            var texts = new List<string>(lines.Count);
            foreach (OcrLine line in lines)
                texts.Add(line.Text);
            DomainMath.ParseStamina(texts, out current, out backup, out total);
            Report(string.Format("体力 {0}+{1}", current, backup));
            return current + backup > 0;
        }

        private bool TeleportIntoDomain()
        {
            switch (options.Kind)
            {
                case DomainKind.Tacet:
                    if (!ClickBookTarget(options.Index, 21, TacetStructure)) return false;
                    break;
                case DomainKind.Forgery:
                    if (!ClickBookTarget(options.Index, 20, ForgeryStructure)) return false;
                    world.Click(0.891, 0.910);
                    world.Sleep(1000);
                    break;
                default:
                    world.Click(0.9730, 0.8806);
                    world.Sleep(800);
                    int index = options.Material == DomainOptions.Materials[0] ? 0 :
                        options.Material == DomainOptions.Materials[1] ? 1 : 2;
                    world.Click(0.898, 0.533 + index * 0.14);
                    world.Sleep(800);
                    world.Click(0.93, 0.90);
                    world.Sleep(800);
                    break;
            }
            TeamChallenge();
            return world.WaitUntil(delegate { return world.TeamHud || world.InRealm; }, 120000);
        }

        private bool ClickBookTarget(int serial, int total, int[] structure)
        {
            world.Sleep(500);
            List<Rectangle> rows = world.FindAll("boss_proceed", .86, .18, 1, .90, 4, .88, .74);
            int index = serial - 1;
            if (index < rows.Count)
            {
                world.Click(rows[index]);
                world.Sleep(1000);
                return true;
            }
            double top = world.Ui != null && world.Ui.ScrollTop > 0 ? world.Ui.ScrollTop : .1963;
            double offset = DomainMath.ScrollOffset(top, serial, total, structure);
            world.Click(0.973, offset);
            world.Sleep(1200);
            rows = world.FindAll("boss_proceed", .86, .18, 1, .90, 4, .88, .74);
            if (rows.Count == 0)
                return false;
            if (index >= rows.Count)
            {
                double lowest = 0;
                foreach (Rectangle row in rows)
                    lowest = Math.Max(lowest, (row.Y + row.Height / 2.0) / 900.0);
                if (lowest < .73)
                {
                    world.Click(0.973, Math.Min(.8806, offset + .018));
                    world.Sleep(1200);
                    rows = world.FindAll("boss_proceed", .86, .18, 1, .90, 4, .88, .74);
                    if (rows.Count == 0)
                        return false;
                }
            }
            world.Click(rows[Math.Min(index, rows.Count - 1)]);
            world.Sleep(1000);
            return true;
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

        private bool WalkUntilF(int timeoutMs)
        {
            world.KeyDown('W');
            try
            {
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < timeoutMs && !token.IsCancellationRequested)
                {
                    world.Tick();
                    if (world.Ui != null && world.Ui.FPrompt)
                    {
                        world.KeyUp('W');
                        world.Key('F');
                        world.Sleep(1200);
                        return true;
                    }
                    if (world.Hud != null && (world.Hud.EnemyHealth || world.Hud.Locked))
                    {
                        world.KeyUp('W');
                        return true;
                    }
                    token.WaitHandle.WaitOne(100);
                }
            }
            finally
            {
                world.KeyUp('W');
            }
            return false;
        }

        private void PickF(bool handleClaim)
        {
            world.Tick();
            if (world.Ui == null || !world.Ui.FPrompt)
                return;
            world.Key('F');
            world.Sleep(900);
            if (!handleClaim)
                return;
            if (world.FindNear("claim_cancel_button_hcenter_vcenter", .80, out Rectangle claim))
            {
                world.Key((char)0x1B);
                world.Sleep(600);
            }
        }

        private bool HasClaimStamina()
        {
            Rectangle box;
            return world.FindNear("claim_stamina_sign", .82, out box);
        }

        private void WalkToTreasure()
        {
            var watch = Stopwatch.StartNew();
            world.KeyDown('W');
            try
            {
                while (watch.ElapsedMilliseconds < 6000 && !token.IsCancellationRequested)
                {
                    world.Tick();
                    if (world.Ui != null && world.Ui.Treasure)
                    {
                        int center = world.Frame.Width / 2;
                        int dx = world.Ui.TreasureBox.X + world.Ui.TreasureBox.Width / 2 - center;
                        if (Math.Abs(dx) > world.Frame.Width * .06)
                        {
                            world.KeyUp('W');
                            world.Key(dx > 0 ? 'D' : 'A');
                            world.KeyDown('W');
                        }
                    }
                    if (world.Ui != null && world.Ui.FPrompt)
                        break;
                    token.WaitHandle.WaitOne(120);
                }
            }
            finally
            {
                world.KeyUp('W');
            }
            PickF(false);
        }

        private bool UseStamina(ref int mustUse, out bool canContinue)
        {
            canContinue = false;
            ReadStamina(out int current, out int backup, out int total);
            bool useDouble = current >= once * 2 || (mustUse > once && total >= once * 2);
            int used = useDouble ? once * 2 : once;
            Rectangle button;
            bool clicked = useDouble
                ? world.FindNear("confirm_btn_hcenter_vcenter", .80, out button)
                : world.FindNear("cancel_button_hcenter_vcenter", .80, out button);
            if (clicked)
                world.Click(button);
            else
                return false;
            world.Sleep(800);
            if (world.FindNear("gem_add_stamina", .80, out Rectangle gem))
            {
                world.Click(0.70, 0.71);
                world.Sleep(800);
                world.Click(0.70, 0.71);
                world.Sleep(800);
                world.Key((char)0x1B);
                world.Sleep(600);
                if (clicked) world.Click(button);
            }
            current -= used;
            total -= used;
            mustUse -= used;
            usedStamina += used;
            if (options.MaxStamina > 0 && usedStamina >= options.MaxStamina)
                canContinue = false;
            else if (total < once)
                canContinue = false;
            else if (mustUse <= 0 && current < once)
                canContinue = false;
            else
                canContinue = true;
            return true;
        }

        private void TeleportToHeal()
        {
            world.Key('M');
            world.Sleep(2200);
            Rectangle point;
            if (!world.FindIn("map_way_point", .25, .20, .75, .80, 5, .86, .74, out point))
                return;
            world.Click(point);
            world.Sleep(1500);
            Rectangle travel;
            if (world.FindNear("gray_teleport", .72, out travel))
                world.Click(travel);
            else
                world.Click(0.89, 0.92);
            world.Sleep(2000);
            SkipDialog();
            WaitTeam();
        }

        private void EscCancel()
        {
            world.Key((char)0x1B);
            world.Sleep(800);
            if (world.FindNear("cancel_button_hcenter_vcenter", .80, out Rectangle cancel))
            {
                world.Click(cancel);
                world.Sleep(1000);
            }
            WaitTeam();
        }

        private void WaitTeam()
        {
            world.WaitUntil(delegate { return world.TeamHud; }, 120000);
        }

        private bool EnsureWorld()
        {
            if (!world.Tick())
                return false;
            if (world.Ui != null && world.Ui.InRealm)
            {
                world.Key((char)0x1B);
                world.Sleep(800);
                if (world.FindNear("gray_confirm_exit_button", .74, out Rectangle confirm))
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
            progress.Report(new DomainReport
            {
                State = state, Runs = runs, UsedStamina = usedStamina,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Fail(string message)
        {
            progress.Report(new DomainReport
            {
                State = message, Failure = message, Runs = runs, UsedStamina = usedStamina,
                Finished = true, Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        public void Dispose()
        {
            world.Dispose();
        }
    }
}
