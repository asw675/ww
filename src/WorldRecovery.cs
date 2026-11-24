using System;
using System.Collections.Generic;
using System.Drawing;

namespace TideCombat
{
    internal sealed class WorldRecovery
    {
        private readonly WorldSession world;
        private readonly Action<string> report;

        internal WorldRecovery(WorldSession world, Action<string> report)
        {
            this.world = world;
            this.report = report;
        }

        internal bool Recover(bool enteredFromWaypoint, bool requireRealmForSkip)
        {
            report("角色倒下，退出副本");
            if (!world.Tick()) return false;
            bool wasInRealm = world.InRealm;
            if (!world.VerifiedWorld || world.Ui != null && world.Ui.Death)
                CloseRevivePopup();
            if (!world.Tick()) return false;
            wasInRealm |= world.InRealm;
            if ((!world.VerifiedWorld || world.InRealm) && !LeaveRealm()) return false;
            if (enteredFromWaypoint && (wasInRealm || !requireRealmForSkip)) return true;
            report("从图鉴定位回血信标");
            return TeleportToHeal();
        }

        internal bool ClearFarmInterruption()
        {
            if (!world.Tick()) return false;
            int route = world.InputRouteVersion;
            Rectangle cancel;
            Func<bool> claim = () => world.Captured && !world.TeamHud &&
                world.Vision.FindClaimCancel(world.Frame, out cancel);
            if (world.WaitUntil(claim, 1500))
            {
                world.Sleep(500);
                if (route != world.InputRouteVersion || !claim() || !world.Key((char)27)) return false;
                world.Sleep(500);
                report("已关闭领奖弹窗");
                return !world.Token.IsCancellationRequested && route == world.InputRouteVersion;
            }
            if (world.Token.IsCancellationRequested || !world.Tick() || route != world.InputRouteVersion) return false;
            if (world.Ui != null && world.Ui.MonthlyCard) return world.DismissMonthlyCard();
            return ReconnectCloud(route) ?? false;
        }

        private bool? ReconnectCloud(int route)
        {
            bool browser = world.Window.Mode == GameSource.Cloud || world.Window.Mode == GameSource.ManagedCloud;
            if (!browser || world.TeamHud) return null;
            bool disconnected;
            Rectangle exit = WorldAssist.CloudDisconnectButton(world.Read(.20, .25, .80, .75, centered: true), out disconnected);
            if (exit.IsEmpty) return null;
            if (!world.ClickOnRoute(exit, route, 20)) return false;
            report("云游戏已断线，等待重新进入大世界");
            world.Sleep(3000);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var loginOptions = new WorldAssistOptions { Login = true, PickUp = false, SkipDialog = false, RestoreCursor = false };
            using (var login = new WorldAssist(loginOptions, world.Vision, world.Ocr, new GameUpdateRecovery(world.Window, world.Token, true),
                recoveringLogin: true))
            {
                return world.WaitUntil(() =>
                {
                    if (world.InputRouteVersion != route) return true;
                    if (world.Vision.HasTeamNumbers(world.Frame)) return true;
                    if (world.Ui != null && world.Ui.MonthlyCard)
                        world.DismissMonthlyCard();
                    else if (login.NeedsNativeFrame(clock.ElapsedMilliseconds) && world.Tick(nativePixels: true) && world.InputRouteVersion == route)
                        login.Observe(world.Frame, world.Hud ?? new HudSnapshot(), false, clock.ElapsedMilliseconds,
                            point => world.ClickOnRoute(point, route), () => false, true);
                    return false;
                }, 600000) && world.InputRouteVersion == route && world.Captured && world.Vision.HasTeamNumbers(world.Frame);
            }
        }

        private static string RecoveryBoss(GameLanguage language)
        {
            switch (language)
            {
                case GameLanguage.English: return "Crownless";
                case GameLanguage.TraditionalChinese: return "無冠者";
                default: return "无冠者";
            }
        }

        private void CloseRevivePopup()
        {
            Rectangle button = Rectangle.Empty;
            Func<bool> cancelVisible = () => world.FindNear("cancel_button_hcenter_vcenter", .70, out button) ||
                world.FindNear("cancel_button_highlight_hcenter_vcenter", .70, out button);
            if (world.WaitUntil(cancelVisible, 1200))
            {
                world.Sleep(200);
                if (cancelVisible() && world.Click(button)) { world.Sleep(500); return; }
                if (world.VerifiedWorld) return;
            }
            if (world.FindNear("btn_dialog_close", .80, out button) && world.Click(button))
            {
                world.Sleep(500);
                return;
            }
            if (world.Key((char)0x1B)) world.Sleep(3000);
        }

        private bool TeleportToHeal()
        {
            var monsters = new GuidebookNavigation(world, report, GuideTab.Monsters);
            if (!monsters.Open(EnsureWorld)) return false;
            if (!world.Click(.13, .14)) return false;
            world.Sleep(500);
            if (!world.TypeText(RecoveryBoss(world.Window.Language))) return false;
            world.Sleep(300);
            if (!world.Click(.20, .14)) return false;
            world.Sleep(300);
            if (!world.Key((char)13)) return false;
            world.Sleep(500);
            if (!world.Click(.13, .24)) return false;
            world.Sleep(500);
            if (!world.Click(.89, .92)) return false;
            world.Sleep(1000);
            List<Rectangle> points = null;
            Func<bool> mapOpen = () =>
            {
                Rectangle sidebar;
                bool selected;
                if (world.VerifiedWorld || world.Vision.InspectGuidebook(world.Frame, out sidebar, out selected)) return false;
                points = world.Vision.FindWaypoints(world.Frame);
                return points.Count > 0;
            };
            if (!world.WaitUntil(mapOpen, 4000))
            {
                // A late map frame must never receive a second press on the Detect position.
                Rectangle tab;
                bool selected;
                if (!world.Tick() || !world.Vision.InspectSidebarGuidebook(world.Frame, "gray_book_all_monsters", out tab, out selected) ||
                    !selected || !world.Click(.89, .92)) return false;
                world.Sleep(1000);
                if (!world.WaitUntil(mapOpen, 4000)) return false;
            }
            foreach (Rectangle point in points)
            {
                if (!world.Click(point)) return false;
                world.Sleep(1000);
                Rectangle travel = Rectangle.Empty;
                if (world.WaitUntil(() => world.FindNear("gray_teleport", .76, out travel), 3000))
                {
                    if (!world.Click(new Point(travel.X + 3 * travel.Width / 2, travel.Y + travel.Height / 2))) return false;
                    if (!world.WaitUntil(() => world.VerifiedWorld && !world.InRealm, 20000)) return false;
                    world.Sleep(2000);
                    return true;
                }
                if (!world.Click(.06, .5)) return false;
                world.Sleep(1000);
            }
            return false;
        }

        internal bool EnsureWorld()
        {
            if (!world.Tick())
                return false;
            if (world.Ui != null && world.Ui.MonthlyCard && !world.DismissMonthlyCard(requireTeam: true))
                return false;
            if (world.Ui != null && world.Ui.Death)
            {
                CloseRevivePopup();
            }
            if (world.InRealm) return LeaveRealm();
            for (int attempt = 0; attempt < 3 && !world.VerifiedWorld; attempt++)
            {
                world.Key((char)0x1B);
                world.Sleep(700);
            }
            return world.VerifiedWorld && !world.InRealm;
        }

        internal bool EnsureMain(int timeoutMs, bool loginPending = false)
        {
            if (!loginPending && !world.Tick()) return false;
            int route = world.InputRouteVersion;
            bool ready = false;
            bool cloud = world.Settings.Source == GameSource.Cloud || world.Settings.Source == GameSource.ManagedCloud;
            var login = loginPending ? new LoginScreen(new GameUpdateRecovery(world.Window, world.Token, cloud, report).Confirm) : null;
            world.WaitUntil(() =>
            {
                if (route != world.InputRouteVersion)
                {
                    if (login == null) return true;
                    login.Reset();
                    route = world.InputRouteVersion;
                }
                if (world.Vision.HasTeamNumbers(world.Frame))
                {
                    ready = !world.InRealm || LeaveRealm();
                    return true;
                }
                if (world.Ui != null && world.Ui.MonthlyCard)
                    return !world.DismissMonthlyCard();
                if (login != null)
                {
                    if (!world.Tick(nativePixels: true) || world.InputRouteVersion != route) return false;
                    bool clicked = login.Observe(world.Frame, world.Now,
                        source => world.Ocr.Read(source, new Rectangle(0, 0, source.Width, source.Height)),
                        point => world.ClickOnRoute(point, route, 20));
                    if (login.ConfirmationTime >= 0)
                    {
                        world.Sleep((int)Math.Max(0, login.ConfirmationTime - world.Now));
                        return false;
                    }
                    if (clicked)
                    {
                        if (login.AfterClickDelay > 0) world.Sleep(login.AfterClickDelay);
                        return false;
                    }
                    if (!world.Captured || world.InputRouteVersion != route) return false;
                }
                bool? connected = ReconnectCloud(route);
                if (connected.HasValue)
                {
                    ready = connected.Value;
                    return true;
                }
                if (!world.Key((char)27, 20, route: route)) return true;
                world.Sleep(2000);
                return false;
            }, loginPending ? 600000 : timeoutMs);
            if (ready) world.Sleep(500);
            return ready && !world.Token.IsCancellationRequested && route == world.InputRouteVersion;
        }

        private bool LeaveRealm()
        {
            if (!world.Key((char)0x1B)) return false;
            world.Sleep(1000);
            Rectangle exit = Rectangle.Empty;
            if (!world.WaitUntil(() => world.VerifiedWorld && !world.InRealm ||
                world.FindNear("gray_confirm_exit_button", .70, out exit), 3000))
                return world.WaitUntil(() => world.VerifiedWorld && !world.InRealm, 120000);
            if (world.VerifiedWorld && !world.InRealm) return true;
            world.Sleep(500);
            if (!world.FindNear("gray_confirm_exit_button", .70, out exit))
                return world.WaitUntil(() => world.VerifiedWorld && !world.InRealm, 120000);
            if (!world.Click(exit)) return false;
            return world.WaitUntil(() => world.VerifiedWorld && !world.InRealm, 120000);
        }

    }
}
