using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading;

namespace TideCombat
{
    internal static class MergeMath
    {
        private static readonly Regex FullBatch = new Regex(@"(?<!\d)100\s*/\s*100(?!\d)");

        internal static bool IsFullBatch(string text)
        {
            return !string.IsNullOrEmpty(text) && FullBatch.IsMatch(text);
        }
    }

    internal sealed class MergeReport
    {
        internal string State;
        internal int Batches;
        internal Size Size;
        internal bool Finished;
        internal string Failure;
    }

    // Merges discarded echoes in full batches of 100 from the bag's synthesis UI.
    internal sealed class MergeRunner : IDisposable
    {
        private readonly CancellationToken token;
        private readonly WorldSession world;
        private readonly CombatSettings settings;
        private readonly IProgress<MergeReport> progress;
        internal bool Failed { get; private set; }
        internal string Failure { get; private set; }
        private int batches;

        internal MergeRunner(CombatSettings settings, CancellationToken token, IProgress<MergeReport> progress)
        {
            this.token = token;
            this.progress = progress;
            this.settings = settings;
            world = new WorldSession(settings, token);
        }

        internal void Run()
        {
            if (!EnsureWorld())
            {
                Fail("无法回到游戏世界。");
                return;
            }
            Report("打开背包");
            char bag = string.IsNullOrEmpty(settings.BagKey) ? 'B' : settings.BagKey[0];
            world.Key(bag);
            world.Sleep(400);
            if (!world.WaitUntil(delegate { return !world.TeamHud; }, 5000))
            {
                EnsureWorld();
                Fail("无法用 " + bag + " 打开背包（请在设置里确认背包键）。");
                return;
            }
            world.Sleep(3000);

            Rectangle cancel;
            bool dialog = world.FindNear("cancel_button_hcenter_vcenter", .80, out cancel) ||
                world.FindNear("cancel_button_highlight_hcenter_vcenter", .80, out cancel);
            if (!dialog)
            {
                EnsureWorld();
                Fail("需要至少 1000 个弃置声骸才能合成。");
                return;
            }
            world.Click(cancel);
            world.Sleep(2000);
            world.Click(0.034, 0.293);
            world.Sleep(1000);
            world.Click(0.580, 0.911);
            world.Sleep(4000);

            OpenMergePage();
            while (!token.IsCancellationRequested && MergeFullBatch())
                batches++;
            Report("弃置声骸已全部合成");
            EnsureWorld();
            progress.Report(new MergeReport
            {
                State = "弃置声骸已全部合成", Batches = batches, Finished = true,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void OpenMergePage()
        {
            Report("打开合成页");
            world.Click(0.602, 0.124);
            world.Sleep(500);
            world.Click(0.520, 0.904);
            world.Sleep(2000);
            world.Click(0.041, 0.918);
            world.Sleep(1000);
            world.Click(0.826, 0.840);
            world.Sleep(500);
            world.Click(0.717, 0.204);
            world.Sleep(500);
            world.Click(0.041, 0.918);
            world.Sleep(500);
        }

        private bool MergeFullBatch()
        {
            Report("合成第 " + (batches + 1) + " 批");
            world.Click(0.310, 0.915);
            world.Sleep(500);
            if (!world.WaitClick("echo_select_all", 3000))
                return false;
            world.Sleep(1000);

            var lines = world.Read(0.670, 0.660, 0.895, 0.958);
            bool full = false;
            foreach (OcrLine line in lines)
                if (MergeMath.IsFullBatch(line.Text))
                {
                    full = true;
                    break;
                }
            if (!full)
                return false;

            world.Click(0.782, 0.910);
            world.Sleep(300);
            SkipDialog();
            world.Sleep(3000);
            world.Click(0.496, 0.972);
            world.Sleep(1000);
            return true;
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

        private bool EnsureWorld()
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (!world.Tick())
                    break;
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
            progress.Report(new MergeReport
            {
                State = state, Batches = batches,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Fail(string message)
        {
            Failed = true;
            Failure = message;
            progress.Report(new MergeReport
            {
                State = message, Failure = message, Finished = true, Batches = batches,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        public void Dispose()
        {
            world.Dispose();
        }
    }
}
