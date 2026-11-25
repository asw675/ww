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
            if (!world.Tick() || HasActiveCombat()) return false;
            report("角色倒下，退出副本");
            bool wasInRealm = world.InRealm;
            if (!world.VerifiedWorld || world.Ui != null && world.Ui.Death)
                CloseRevivePopup();
            if (!world.Tick() || HasActiveCombat()) return false;
            wasInRealm |= world.InRealm;
            if ((!world.VerifiedWorld || world.InRealm) && !LeaveRealm()) return false;
            if (enteredFromWaypoint && (wasInRealm || !requireRealmForSkip)) return true;
            report("从图鉴定位回血信标");
            return TeleportToHeal();
        }

        private bool HasActiveCombat()
        {
            return world.TeamHud && (world.Hud.Locked || world.Hud.EnemyHealth);
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
            if (!world.ClickMenuButton(exit, route: route)) return false;
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
                if (cancelVisible() && world.ClickMenuButton(button)) { world.Sleep(500); return; }
                if (world.VerifiedWorld) return;
            }
            if (world.FindNear("btn_dialog_close", .80, out button) && world.ClickMenuButton(button))
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
            int route = world.InputRouteVersion;
            Func<int, bool> delay = milliseconds =>
            {
                if (world.Token.WaitHandle.WaitOne(milliseconds)) return false;
                // During a loading frame, the next observation may still succeed on the same route.
                world.Tick();
                return world.InputRouteVersion == route;
            };
            Func<double, double, bool> click = (x, y) => world.Tick() && world.ClickMenuButton(
                new Point((int)(world.Frame.Width * x), (int)(world.Frame.Height * y)), route: route);
            Func<Func<bool>, int, bool> wait = (condition, timeout) => world.WaitUntil(
                () => world.InputRouteVersion != route || condition(), timeout) && world.InputRouteVersion == route;

            if (!click(.13, .14) || !delay(500)) return false;
            if (!world.Tick() || world.InputRouteVersion != route ||
                !world.Input.TypeText(RecoveryBoss(world.Window.Language), world.Token) || !delay(300)) return false;
            if (!click(.20, .14) || !delay(300)) return false;
            if (!world.Key((char)13, route: route) || !delay(500)) return false;
            if (!click(.13, .24) || !delay(500)) return false;
            if (!click(.89, .92) || !delay(1000)) return false;

            List<Rectangle> points = null;
            Func<bool> mapOpen = () =>
            {
                Rectangle sidebar;
                bool selected;
                if (world.VerifiedWorld || world.Vision.InspectGuidebook(world.Frame, out sidebar, out selected)) return false;
                points = world.Vision.FindWaypoints(world.Frame, confidenceOrder: true);
                return points.Count > 0;
            };
            if (!wait(mapOpen, 4000))
            {
                // A late map frame must never receive a second press on the Detect position.
                Rectangle tab;
                bool selected;
                if (!world.Tick() || world.InputRouteVersion != route ||
                    !world.Vision.InspectSidebarGuidebook(world.Frame, "gray_book_all_monsters", out tab, out selected) ||
                    !selected || !click(.89, .92) || !delay(1000) || !wait(mapOpen, 4000)) return false;
            }

            // Detect may reveal waypoints before the map finishes centering on the boss.
            if (!delay(2000) || !wait(mapOpen, 1000)) return false;
            Rectangle point = points[0];
            Rectangle travel = Rectangle.Empty;
            Rectangle popup = Rectangle.Empty;
            Func<bool> waypointReady = () => world.FindNear("gray_teleport", .76, out travel) ||
                world.Captured && world.Vision.FindWaypointPopup(world.Frame, out popup);
            if (waypointReady())
            {
                if (!world.ClickMenuButton(point, route: route) || !delay(1000)) return false;
            }
            else if (!world.ClickMenuTransition(route, () =>
                {
                    if (!mapOpen()) return Rectangle.Empty;
                    return points.Find(candidate => candidate.IntersectsWith(point));
                }, waypointReady, 3000, 1000))
                return false;

            if (!wait(() => world.FindNear("gray_teleport", .76, out travel), 3000))
            {
                if (!world.ClickMenuTransition(route, () => !world.VerifiedWorld &&
                    world.Vision.FindWaypointPopup(world.Frame, out popup) ? popup : Rectangle.Empty,
                    () => world.FindNear("gray_teleport", .76, out travel), 3000, 1000)) return false;
            }
            Rectangle originalTravel = travel;
            if (!world.ClickMenuTransition(route, () =>
                {
                    Rectangle current;
                    if (world.VerifiedWorld || !world.FindNear("gray_teleport", .76, out current) ||
                        !originalTravel.IntersectsWith(current)) return Rectangle.Empty;
                    return new Rectangle(current.Right, current.Y, current.Width, current.Height);
                }, () => world.VerifiedWorld && !world.InRealm, 20000))
                return false;
            return delay(2000);
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
            if (!world.Tick()) return false;
            int route = world.InputRouteVersion;
            Func<bool> exited = () => world.VerifiedWorld && !world.InRealm;
            if (exited()) return true;
            bool recognizedLeave = false;
            Func<Rectangle> findExit = () =>
            {
                RealmExitDialog dialog = FarmVision.ReadRealmExitDialog(world.Frame, world.Ocr);
                recognizedLeave = dialog.Recognized;
                Rectangle template;
                return dialog.Recognized ? dialog.Exit :
                    world.Vision.FindLabel(world.Frame, "gray_confirm_exit_button", .70, out template) ?
                        template : Rectangle.Empty;
            };
            if (findExit().IsEmpty)
            {
                if (recognizedLeave || !world.Key((char)0x1B, route: route)) return false;
                world.Sleep(1000);
                if (!world.WaitUntil(() => world.InputRouteVersion != route || exited() || !findExit().IsEmpty, 3000))
                    return world.WaitUntil(() => world.InputRouteVersion != route || exited(), 120000) &&
                        world.InputRouteVersion == route;
            }
            if (world.InputRouteVersion != route) return false;
            if (world.ClickMenuTransition(route, findExit, exited, 5000, 500))
                return true;
            if (world.Tick() && world.InputRouteVersion == route && !findExit().IsEmpty)
                return false;
            return world.WaitUntil(() => world.InputRouteVersion != route || exited(), 120000) &&
                world.InputRouteVersion == route;
        }

    }
}
