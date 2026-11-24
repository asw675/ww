using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;

namespace TideCombat
{
    internal enum GuideView { NoFrame, World, Terminal, Guidebook, Other }

    internal enum GuideTab { Boss, Quest, Monsters }

    internal sealed class GuidebookNavigation
    {
        private const string LogArea = "索拉指南";
        private readonly WorldSession world;
        private readonly Action<string> report;
        private readonly GuideTab target;
        private string TabName { get { return target == GuideTab.Quest ? "任务图鉴" : target == GuideTab.Monsters ? "全部怪物" : "Boss 图鉴"; } }
        internal bool ChangedPage { get; private set; }
        internal string Failure { get; private set; }

        internal GuidebookNavigation(WorldSession world, Action<string> report, GuideTab target = GuideTab.Boss)
        {
            this.world = world;
            this.target = target;
            this.report = report;
        }

        internal bool Open(Func<bool> returnToWorld = null)
        {
            Failure = null;
            ChangedPage = false;
            report("打开索拉指南");
            if (world.Tick() && world.Ui != null && world.Ui.MonthlyCard && !world.DismissMonthlyCard(requireTeam: true))
            {
                Failure = "打开图鉴前未能关闭月卡弹窗。";
                return false;
            }
            Rectangle tab;
            bool selected;
            GuideView view;
            bool requestedForeground = false;
            if (!ReadBookView(out tab, out selected, out view))
            {
                ChangedPage = true;
                LogGuideView("打开索拉指南前的画面", view);
                if (tab.IsEmpty && view != GuideView.World && view != GuideView.Terminal)
                    WaitForUsableBookView(2200, out tab, out selected, out view,
                        allowTerminal: true, requireForeground: false);
                if (tab.IsEmpty && view == GuideView.Other && returnToWorld != null)
                {
                    if (!returnToWorld())
                    {
                        Failure = "未识别到图鉴入口，且未确认返回大世界。";
                        return false;
                    }
                    ReadBookView(out tab, out selected, out view);
                }
                TryReachGuide(ref tab, ref selected, ref view, requireForeground: false);
                if (tab.IsEmpty && CanRecoverBookInForeground())
                {
                    requestedForeground = true;
                    if (RecoverBookInForeground())
                    {
                        report("等待前台稳定画面，确认终端、大世界或索拉指南");
                        WaitForUsableBookView(5000, out tab, out selected, out view,
                            allowTerminal: true, requireForeground: true);
                        LogGuideView("前台重试确认画面", view);
                        TryReachGuide(ref tab, ref selected, ref view, requireForeground: true);
                    }
                }
                if (tab.IsEmpty)
                {
                    if (!world.Token.IsCancellationRequested)
                    {
                        if (view == GuideView.Guidebook)
                            SetFailure("已识别到索拉指南，但未找到" + TabName + "页签。");
                        else if (view == GuideView.Terminal)
                            SetFailure("已识别到游戏终端，但未能通过终端进入索拉指南或返回大世界。");
                        else if (view == GuideView.NoFrame)
                            SetFailure("等待索拉指南时未取得可用游戏画面。");
                        else if (world.Window.Mode == GameSource.Background)
                            SetFailure("后台操作后未确认索拉指南已打开，请切回游戏后重试。");
                        else if (view == GuideView.Other)
                            SetFailure("前台游戏画面未识别为终端、大世界或索拉指南。");
                        else
                            SetFailure("未确认索拉指南已打开。请检查游戏界面和索拉指南快捷键。");
                    }
                    return false;
                }
            }
            if (selected)
            {
                RunLog.Info(LogArea, "识别到已选中的" + TabName + "页签");
                return true;
            }

            ChangedPage = true;
            report("切换到" + TabName);
            if (world.ClickMenuButton(tab) && WaitForSelectedBookTab(4500))
                return true;
            if (!requestedForeground && CanRecoverBookInForeground())
                RecoverBookInForeground();
            if (!world.Token.IsCancellationRequested && world.Window.Mode != GameSource.Background &&
                RetryBookTabClick())
                return true;
            if (!world.Token.IsCancellationRequested)
                SetFailure("已找到索拉指南，但未确认切换到" + TabName + "。");
            return false;
        }

        private bool CanRecoverBookInForeground()
        {
            return !world.Token.IsCancellationRequested && world.Settings.Source == GameSource.AutoLocal &&
                world.Window.Mode == GameSource.Background;
        }

        private bool RecoverBookInForeground()
        {
            report("后台操作尚未得到确认，尝试重新读取游戏前台画面");
            return world.TryRecoverForegroundCapture();
        }

        private bool RetryBookTabClick()
        {
            Rectangle tab;
            bool selected;
            if (!FindBookTab(out tab, out selected))
                return false;
            if (selected)
                return true;
            var center = new Point(tab.X + tab.Width / 2, tab.Y + tab.Height / 2);
            return world.ClickMenuButton(center, retry: true) && WaitForSelectedBookTab(4500);
        }

        private bool WaitForSelectedBookTab(int timeoutMs)
        {
            bool confirmed = world.WaitUntil(delegate
            {
                Rectangle ignored;
                bool active;
                return InspectTab(out ignored, out active) && active;
            }, timeoutMs);
            if (confirmed)
                RunLog.Info(LogArea, "已确认切换到" + TabName);
            return confirmed;
        }

        private void TryReachGuide(ref Rectangle tab, ref bool selected, ref GuideView view, bool requireForeground)
        {
            if (!tab.IsEmpty || world.Token.IsCancellationRequested)
                return;
            GuideView initial = view;
            if (initial == GuideView.World)
                TryOpenBookFromWorld(out tab, out selected, out view, requireForeground);
            if (tab.IsEmpty && view == GuideView.Terminal && !world.Token.IsCancellationRequested)
                TryOpenBookFromTerminal(out tab, out selected, out view, requireForeground);
            if (tab.IsEmpty && view == GuideView.World && initial != GuideView.World && !world.Token.IsCancellationRequested)
                TryOpenBookFromWorld(out tab, out selected, out view, requireForeground);
        }

        private void TryOpenBookFromTerminal(out Rectangle tab, out bool selected, out GuideView view,
            bool requireForeground)
        {
            if (ReadBookView(out tab, out selected, out view) || view != GuideView.Terminal)
                return;

            int route = world.InputRouteVersion;
            Rectangle tile;
            bool matchedIcon = world.Vision.FindTerminalGuide(world.Frame, out tile);
            if (!matchedIcon)
                tile = FindTerminalGuideTile(world.Read(.28, .48, .90, .92));
            if (ReadBookView(out tab, out selected, out view) || view != GuideView.Terminal ||
                world.InputRouteVersion != route)
                return;
            if (!tile.IsEmpty)
            {
                RunLog.Info(LogArea, "已定位终端索拉指南入口，匹配方式=" + (matchedIcon ? "图标" : "文字") + "，位置=" + tile);
                report("从游戏终端打开索拉指南");
                var center = new Point(tile.X + tile.Width / 2, tile.Y + tile.Height / 2);
                bool clicked = world.ClickMenuButton(center, retry: requireForeground, requireForeground: requireForeground);
                if (clicked)
                    WaitForBookTab(3200, out tab, out selected, out view);
                else
                    ReadBookView(out tab, out selected, out view);
                LogBookAttempt("终端索拉指南入口", clicked, world.Window.Mode, tab, view);
            }
            else
            {
                RunLog.Info(LogArea, "终端已识别，未找到可信的索拉指南入口");
            }

            if (!tab.IsEmpty || view != GuideView.Terminal || world.Token.IsCancellationRequested ||
                ReadBookView(out tab, out selected, out view) || view != GuideView.Terminal)
                return;
            report("关闭已识别的游戏终端");
            bool escaped = world.Key((char)0x1B, requireForeground: requireForeground);
            if (escaped)
                WaitForUsableBookView(3200, out tab, out selected, out view,
                    allowTerminal: false, requireForeground: requireForeground);
            else
                ReadBookView(out tab, out selected, out view);
            LogBookAttempt("关闭终端 Esc", escaped, world.Window.Mode, tab, view);
        }

        private static Rectangle FindTerminalGuideTile(List<OcrLine> lines)
        {
            Rectangle found = Rectangle.Empty;
            foreach (OcrLine line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.Text) || line.Score < .70f || line.Box.IsEmpty)
                    continue;
                string text = Regex.Replace(line.Text, @"\s+", "");
                if (text != "索拉指南" && !string.Equals(text, "Guidebook", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!found.IsEmpty)
                    return Rectangle.Empty;
                found = line.Box;
            }
            return found;
        }

        private void WaitForUsableBookView(int timeoutMs, out Rectangle tab, out bool selected, out GuideView view,
            bool allowTerminal, bool requireForeground)
        {
            Rectangle match = Rectangle.Empty;
            bool active = false, sawGuide = false;
            GuideView observed = GuideView.NoFrame, stable = GuideView.NoFrame;
            int stableFrames = 0, samples = 0, route = world.InputRouteVersion;
            bool ready = world.WaitUntil(() =>
            {
                if (requireForeground && (world.Window.Mode != GameSource.Foreground || world.InputRouteVersion != route))
                {
                    observed = GuideView.NoFrame;
                    stableFrames = 0;
                    return false;
                }
                samples++;
                if (InspectBookView(out match, out active, out observed))
                    return true;
                sawGuide |= observed == GuideView.Guidebook;
                if (sawGuide || (observed != GuideView.World && (!allowTerminal || observed != GuideView.Terminal)))
                {
                    stableFrames = 0;
                    return false;
                }
                stableFrames = observed == stable ? stableFrames + 1 : 1;
                stable = observed;
                return stableFrames >= 2;
            }, timeoutMs);
            tab = ready ? match : Rectangle.Empty;
            selected = ready && active;
            view = observed;
            if (!ready && sawGuide)
                view = GuideView.Guidebook;
            else if (!ready && (observed == GuideView.World || allowTerminal && observed == GuideView.Terminal))
                view = GuideView.Other;
            RunLog.Info(LogArea, "稳定画面检查完成，采样=" + samples + "，确认=" + ready +
                "，结果=" + DescribeGuideView(view));
        }

        private void TryOpenBookFromWorld(out Rectangle tab, out bool selected, out GuideView view,
            bool requireForeground = false)
        {
            if (ReadBookView(out tab, out selected, out view) || view != GuideView.World)
                return;
            bool sent = world.GuideKey(requireForeground);
            GameSource mode = world.Window.Mode;
            if (sent)
                WaitForBookTab(4000, out tab, out selected, out view, stopAtTerminal: true);
            else
                ReadBookView(out tab, out selected, out view);
            LogBookAttempt(world.Settings.GuidebookKey ?? "F2", sent, mode, tab, view);

            if (!tab.IsEmpty || view != GuideView.World || world.Token.IsCancellationRequested)
                return;
            report("尝试通过游戏内入口打开索拉指南");
            // The menu shortcuts are in the world HUD. Avoid sending this click from other game screens.
            if (ReadBookView(out tab, out selected, out view) || view != GuideView.World)
                return;
            sent = world.ClickWithAlt(.77, .05, 50);
            mode = world.Window.Mode;
            if (sent)
                WaitForBookTab(4000, out tab, out selected, out view);
            else
                ReadBookView(out tab, out selected, out view);
            LogBookAttempt("游戏内入口", sent, mode, tab, view);
        }

        private void LogBookAttempt(string action, bool sent, GameSource mode, Rectangle tab, GuideView view)
        {
            RunLog.Info(LogArea, "索拉指南操作=" + action + "，已投递=" + sent + "，模式=" + mode +
                "，结果=" + (tab.IsEmpty ? DescribeGuideView(view) : "已识别" + TabName + "页签"));
        }

        internal static string DescribeGuideView(GuideView view)
        {
            switch (view)
            {
                case GuideView.World: return "仍在大世界";
                case GuideView.Terminal: return "游戏终端";
                case GuideView.Guidebook: return "索拉指南已打开，尚未匹配目标页签";
                case GuideView.Other: return "有游戏画面，界面未识别";
                default: return "暂无可用游戏画面";
            }
        }

        internal bool FindBookTab(out Rectangle tab, out bool selected)
        {
            tab = Rectangle.Empty;
            selected = false;
            return world.Tick() && InspectTab(out tab, out selected) && !tab.IsEmpty;
        }

        private bool ReadBookView(out Rectangle tab, out bool selected, out GuideView view)
        {
            if (world.Tick())
                return InspectBookView(out tab, out selected, out view);
            tab = Rectangle.Empty;
            selected = false;
            view = GuideView.NoFrame;
            return false;
        }

        internal bool InspectBookView(out Rectangle tab, out bool selected, out GuideView view)
        {
            if (world.Terminal)
            {
                tab = Rectangle.Empty;
                selected = false;
                view = GuideView.Terminal;
            }
            else if (InspectTab(out tab, out selected))
                view = GuideView.Guidebook;
            else if (world.VerifiedWorld && !world.InRealm)
                view = GuideView.World;
            else
                view = GuideView.Other;
            return !tab.IsEmpty;
        }

        private bool InspectTab(out Rectangle tab, out bool selected)
        {
            if (target == GuideTab.Boss)
                return world.Vision.InspectGuidebook(world.Frame, out tab, out selected);
            return world.Vision.InspectSidebarGuidebook(world.Frame, target == GuideTab.Quest ? "gray_book_quest" : "gray_book_all_monsters", out tab, out selected);
        }

        private void LogGuideView(string action, GuideView view)
        {
            FarmUi ui = world.Ui;
            RunLog.Info(LogArea, string.Format(
                "{0}={1}，模式={2}，队伍HUD={3}，大世界标记={4}，副本标记={5}，挑战={6}，领奖={7}，月卡={8}，界面标记={9}",
                action, DescribeGuideView(view), world.Window.Mode, world.TeamHud, ui != null && ui.OpenWorld,
                ui != null && ui.InRealm, ui != null && ui.Challenge, ui != null && ui.Claim,
                ui != null && ui.MonthlyCard, ui == null ? "无" : ui.Detail));
        }

        private bool WaitForBookTab(int timeoutMs, out Rectangle tab, out bool selected, out GuideView view, bool stopAtTerminal = false)
        {
            Rectangle match = Rectangle.Empty;
            bool active = false;
            GuideView observed = GuideView.NoFrame;
            bool found = world.WaitUntil(() => InspectBookView(out match, out active, out observed) ||
                (stopAtTerminal && observed == GuideView.Terminal), timeoutMs);
            tab = match;
            selected = active;
            view = observed;
            return found && !tab.IsEmpty;
        }

        private void SetFailure(string reason) { Failure = reason; }
    }
}
