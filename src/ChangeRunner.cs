using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace TideCombat
{
    public sealed class ChangeOptions
    {
        public string Target { get; set; }

        public static readonly string[] Targets =
        {
            "攻击", "暴击伤害", "暴击", "生命", "防御", "共鸣效率", "冷凝伤害加成", "热熔伤害加成",
            "导电伤害加成", "气动伤害加成", "衍射伤害加成", "湮灭伤害加成"
        };

        public static ChangeOptions Defaults()
        {
            return new ChangeOptions { Target = "攻击" };
        }

        public void Validate()
        {
            if (Target == null || Array.IndexOf(Targets, Target) < 0)
                throw new System.IO.InvalidDataException("目标属性无效。");
        }
    }

    internal static class ChangeMath
    {
        private static readonly Regex Stat = new Regex(
            @"^(?:主(?:音)?属性:?)?([\u4e00-\u9fff]+)(?:\+?\d+(?:\.\d+)?%?)?$");
        private static readonly string[][] Fixes =
        {
            new[] { "凝夜自霜", "凝夜白霜" },
            new[] { "主属性灭伤害加成", "主属性湮灭伤害加成" },
            new[] { "主属性行射伤害加成", "主属性衍射伤害加成" },
            new[] { "灭伤害加成", "主属性湮灭伤害加成" }
        };

        internal static string Fix(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            foreach (string[] fix in Fixes)
            {
                if (text.Contains(fix[0]))
                    return text.Replace(fix[0], fix[1]);
            }
            return text;
        }

        internal static bool IsTarget(string target, string current)
        {
            return !string.IsNullOrEmpty(target) && ReadStat(current) == target;
        }

        internal static string ReadStat(string value)
        {
            Match match = Stat.Match(Fix(OcrText.Compact(value)));
            string name = match.Success ? match.Groups[1].Value : string.Empty;
            switch (name)
            {
                case "攻击力":
                case "攻击力百分比":
                case "攻击百分比":
                    return "攻击";
                case "生命值":
                case "生命值百分比":
                case "生命百分比":
                    return "生命";
                case "防御力":
                case "防御力百分比":
                case "防御百分比":
                    return "防御";
                case "暴击率":
                    return "暴击";
                case "治疗效果加成":
                    return name;
                default:
                    return Array.IndexOf(ChangeOptions.Targets, name) >= 0 ? name : null;
            }
        }

        internal static OcrLine FindTarget(IEnumerable<OcrLine> lines, string target)
        {
            OcrLine result = null;
            foreach (OcrLine line in lines)
            {
                if (line.Score < .65f || line.Box.IsEmpty || !IsTarget(target, line.Text))
                    continue;
                if (result != null && !result.Box.IntersectsWith(line.Box))
                    return null;
                result = line;
            }
            return result;
        }
    }

    internal sealed class ChangeReport
    {
        internal string State;
        internal int Changed;
        internal Size Size;
        internal bool Finished;
        internal string Failure;
    }

    // Re-rolls the main stat of level-0 echoes in the bag via the tuning UI.
    internal sealed class ChangeRunner : IDisposable
    {
        private readonly CombatSettings settings;
        private readonly ChangeOptions options;
        private readonly CancellationToken token;
        private readonly WorldSession world;
        private readonly IProgress<ChangeReport> progress;
        internal bool Failed { get; private set; }
        internal string Failure { get; private set; }
        private int changed;

        internal ChangeRunner(CombatSettings settings, ChangeOptions options, CancellationToken token,
            IProgress<ChangeReport> progress)
        {
            this.settings = settings;
            this.options = options;
            this.token = token;
            this.progress = progress;
            world = new WorldSession(settings, token);
        }

        internal void Run()
        {
            RunLog.Info("改主属性", "开始");
            Rectangle cultivate;
            if (!FindCultivate(out cultivate))
            {
                Fail("请先在背包声骸界面筛选要改主属性的声骸，并按等级从 0 排序。");
                return;
            }
            while (!token.IsCancellationRequested)
            {
                if (!IsLevelZero())
                {
                    RunLog.Info("改主属性", "没有可修改的声骸，任务结束 · 已修改=" + changed);
                    progress.Report(new ChangeReport
                    {
                        State = "没有可修改的声骸，任务结束", Changed = changed, Finished = true,
                        Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
                    });
                    return;
                }
                if (!ChangeOne(cultivate))
                    return;
                changed++;
                if (world.Tick())
                    Screenshots.Save(world.Frame, "success", string.Empty,
                        new Rectangle(144, 81, 448, 414));
                Report("已修改 " + changed + " 个");
                if (!ReturnToBag())
                {
                    Fail("返回背包超时。");
                    return;
                }
                if (!FindCultivate(out cultivate))
                {
                    Fail("返回背包后未识别到培养按钮。");
                    return;
                }
            }
            if (token.IsCancellationRequested)
                RunLog.Info("改主属性", "任务取消 · 已修改=" + changed);
        }

        private bool ChangeOne(Rectangle cultivate)
        {
            Report("打开培养界面");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 5000 && !token.IsCancellationRequested)
            {
                world.Click(cultivate);
                world.Sleep(500);
                if (!FindCultivate(out cultivate))
                    break;
            }
            if (WaitText(null, "声骸强化", 5000) == null)
            {
                Fail("未能打开声骸强化界面。");
                return false;
            }
            world.Sleep(500);

            string current = ReadCurrentStat();
            if (current == null)
            {
                Fail("无法读取当前主属性。");
                return false;
            }
            if (current.Contains(options.Target))
            {
                Fail("目标属性与当前主属性一致，请修改背包筛选条件。");
                return false;
            }

            world.Click(0.04, 0.41);
            if (WaitText(null, "主音属性", 5000) == null)
            {
                Fail("未找到主音属性面板。");
                return false;
            }
            world.Sleep(800);
            world.Click(0.52, 0.71);
            OcrLine target = WaitText(null, options.Target, 5000, mainStat: true);
            if (target == null || !world.Click(target.Box))
            {
                Fail(target == null ? "未找到唯一的目标属性选项。" : "未成功点击目标属性选项。");
                return false;
            }
            world.Sleep(500);
            if (!ClickText(null, "确认", 5000))
            {
                Fail("未找到确认按钮。");
                return false;
            }
            world.Sleep(2000);
            if (!ClickText(new RectangleF(.37f, .82f, .27f, .17f), "数据重构", 5000))
            {
                Fail("未找到数据重构按钮。");
                return false;
            }
            world.Sleep(500);
            if (WaitText(null, "获得声骸", 5000) == null)
            {
                Fail("未出现获得声骸提示。");
                return false;
            }
            return true;
        }

        private bool ReturnToBag()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Rectangle box;
            while (watch.ElapsedMilliseconds < 5000 && !token.IsCancellationRequested)
            {
                if (FindCultivate(out box))
                {
                    world.Sleep(100);
                    return true;
                }
                world.Key((char)0x1B);
                world.Sleep(700);
            }
            return FindCultivate(out box);
        }

        private bool FindCultivate(out Rectangle box)
        {
            box = Rectangle.Empty;
            var lines = world.Read(0.82, 0.86, 0.97, 0.96);
            foreach (OcrLine line in lines)
            {
                if (line.Text == null || !line.Text.Contains("培养"))
                    continue;
                box = line.Box;
                return true;
            }
            return false;
        }

        private bool IsLevelZero()
        {
            var lines = world.Read(0.66, 0.48, 0.77, 0.56);
            foreach (OcrLine line in lines)
                if (line.Text != null && line.Text.Contains("声骸技能"))
                    return true;
            return false;
        }

        private string ReadCurrentStat()
        {
            OcrLine first = null;
            return world.WaitUntil(() =>
            {
                first = world.Read(.09, .20, .15, .26).FirstOrDefault();
                return first != null && !string.IsNullOrWhiteSpace(first.Text);
            }, 5000) ? first.Text : null;
        }

        private OcrLine WaitText(RectangleF? area, string text, int timeoutMs, bool mainStat = false)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs && !token.IsCancellationRequested)
            {
                var lines = area.HasValue
                    ? world.Read(area.Value.X, area.Value.Y, area.Value.Right, area.Value.Bottom)
                    : world.Read(0, 0, 1, 1);
                if (mainStat)
                {
                    OcrLine target = ChangeMath.FindTarget(lines, text);
                    if (target != null)
                        return target;
                }
                else
                    foreach (OcrLine line in lines)
                        if (ChangeMath.Fix(line.Text).Contains(text))
                            return line;
                world.Sleep(400);
            }
            return null;
        }

        private bool ClickText(RectangleF? area, string text, int timeoutMs)
        {
            OcrLine line = WaitText(area, text, timeoutMs);
            return line != null && world.Click(line.Box);
        }

        private void Report(string state)
        {
            RunLog.Info("改主属性", state);
            progress.Report(new ChangeReport
            {
                State = state, Changed = changed,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        private void Fail(string message)
        {
            if (token.IsCancellationRequested)
                return;
            Failed = true;
            Failure = message;
            RunLog.Warn("改主属性", string.Format("任务停止 · {0} · 已修改={1}", message, changed));
            if (world.Tick())
                Screenshots.Save(world.Frame, "failed", message, new Rectangle(144, 81, 448, 414));
            progress.Report(new ChangeReport
            {
                State = message, Failure = message, Changed = changed, Finished = true,
                Size = world.Captured ? new Size(world.Frame.Width, world.Frame.Height) : Size.Empty
            });
        }

        public void Dispose()
        {
            world.Dispose();
        }
    }
}
