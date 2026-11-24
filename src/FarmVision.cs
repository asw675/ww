using System.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace TideCombat
{
    internal enum RechallengeChoice { None, Selected, Above, Below, RewardSelected }

    internal sealed class FarmUi
    {
        internal bool FPrompt;
        internal Rectangle FBox;
        internal RechallengeChoice Rechallenge;
        internal Rectangle RechallengeButton;
        internal bool Absorb;
        internal bool Claim;
        internal Rectangle ClaimCancelBox, CloudDisconnectBox;
        internal bool Challenge;
        internal bool TeamStart;
        internal Rectangle ChallengeBox, TeamStartBox, StartBattleBox, ConfirmBox, SkipCheckBox;
        internal bool SkipDialog;
        internal bool Death;
        internal Rectangle DeathBox;
        internal bool Treasure { get; set; }
        internal Rectangle TreasureBox { get; set; }
        internal bool Terminal;
        internal Rectangle TerminalGuide;
        internal bool Book;
        internal bool BookSelected;
        internal Rectangle BookBox;
        internal bool BookMonsters, BookMonstersSelected;
        internal Rectangle BookMonstersBox;
        internal List<Rectangle> BossRows = new List<Rectangle>();
        internal bool InRealm;
        internal bool OpenWorld;
        internal bool Teleport;
        internal Rectangle TeleportBox;
        internal Rectangle WaypointTravelBox;
        internal Rectangle WaypointPopupBox;
        internal bool MonthlyCard;
        internal Rectangle MonthlyCardBox;
        internal bool ClaimReward;
        internal bool HasMapMarker;
        internal Rectangle MapMarker;
        internal List<Rectangle> HealWaypoints;
        internal Rectangle ClaimBox;
        internal double ScrollTop = GuidebookList.DefaultTop;
        internal int Evidence;
        internal string Detail = "等待画面";
    }

    internal static class FarmIndicators
    {
        // Signatures that the 4C farm layer matches. Keep this list in sync with the
        // rows added to data/Signatures.tsv; tools/BuildSignatures.cs regenerates them.
        internal static readonly string[] All =
        {
            "pick_up_f_hcenter_vcenter", "absorb_zh_CN", "absorb_en_US",
            "claim_cancel_button_hcenter_vcenter", "cancel_button_hcenter_vcenter",
            "confirm_btn_hcenter_vcenter", "confirm_btn_highlight_hcenter_vcenter",
            "team_start_challenge", "gray_button_challenge", "gray_start_battle",
            "skip_dialog_check", "skip_dialog_confirm", "revive_confirm_hcenter_vcenter",
            "treasure_icon", "gray_book_boss", "gray_book_boss_highlight", "gray_book_quest", "gray_book_weekly",
            "gray_book_all_monsters", "boss_proceed", "gray_teleport",
            "illusive_realm_exit", "world_earth_icon", "monthly_card", "claim_stamina_sign",
            "boss_check_mark_minimap", "boss_check_mark", "map_way_point", "map_way_point_big", "remove_custom", "team_close", "btn_dialog_close",
            "fast_travel_custom", "gem_add_stamina", "gray_confirm_exit_button",
            "pick_up_xinyao_icon", "skip_dialog", "skip_dialog_new", "skip_quest_confirm", "skip_quest_confirm_new",
            "message", "message_dialog"
        };
    }

    // Whole-frame integral index so several template searches can share one pass.
    internal sealed class FrameIndex
    {
        private int width, height;
        private long indexedCapture;
        private int[] luminance, blueRed, greenMagenta;
        private readonly int[] values = new int[49 * 3];

        internal void Load(PixelFrame frame)
        {
            if (frame.CaptureId != 0 && frame.CaptureId == indexedCapture) return;
            if (width != frame.Width || height != frame.Height)
            {
                width = frame.Width;
                height = frame.Height;
                int length = (width + 1) * (height + 1);
                luminance = new int[length];
                blueRed = new int[length];
                greenMagenta = new int[length];
            }
            int stride = width + 1;
            for (int y = 1; y <= height; y++)
            {
                int sumLight = 0, sumBlueRed = 0, sumGreen = 0;
                for (int x = 1; x <= width; x++)
                {
                    int r, g, b;
                    frame.Rgb(x - 1, y - 1, out r, out g, out b);
                    sumLight += (r * 3 + g * 6 + b) / 10;
                    sumBlueRed += b - r;
                    sumGreen += g - (r + b) / 2;
                    int index = y * stride + x;
                    luminance[index] = sumLight + luminance[index - stride];
                    blueRed[index] = sumBlueRed + blueRed[index - stride];
                    greenMagenta[index] = sumGreen + greenMagenta[index - stride];
                }
            }
            indexedCapture = frame.CaptureId;
        }

        internal int[] Sample(Rectangle area)
        {
            int stride = width + 1;
            for (int gy = 0; gy < 7; gy++)
            {
                int top = area.Top + gy * area.Height / 7;
                int bottom = area.Top + (gy + 1) * area.Height / 7;
                int heightCell = Math.Max(1, bottom - top);
                int y0 = Math.Max(0, Math.Min(height - 1, top));
                heightCell = Math.Min(heightCell, height - y0);
                for (int gx = 0; gx < 7; gx++)
                {
                    int left = area.Left + gx * area.Width / 7;
                    int right = area.Left + (gx + 1) * area.Width / 7;
                    int widthCell = Math.Max(1, right - left);
                    int x0 = Math.Max(0, Math.Min(width - 1, left));
                    widthCell = Math.Min(widthCell, width - x0);
                    int a = y0 * stride + x0;
                    int b = a + widthCell;
                    int c = a + heightCell * stride;
                    int d = c + widthCell;
                    int size = widthCell * heightCell;
                    values[gy * 7 + gx] = Div(luminance[d] + luminance[a] - luminance[b] - luminance[c], size);
                    values[49 + gy * 7 + gx] = Div(blueRed[d] + blueRed[a] - blueRed[b] - blueRed[c], size);
                    values[98 + gy * 7 + gx] = Div(greenMagenta[d] + greenMagenta[a] - greenMagenta[b] - greenMagenta[c], size);
                }
            }
            return values;
        }

        private static int Div(int sum, int size)
        {
            return sum / Math.Max(1, size);
        }
    }

    internal sealed class FarmVision
    {
        private static readonly string[] BossGuideTabs = { "gray_book_boss", "gray_book_boss_highlight" };
        private static readonly string[] GuideSidebarTabs = { "gray_book_quest", "gray_book_weekly", "gray_book_all_monsters" };
        private struct Candidate
        {
            internal double Score;
            internal Rectangle Rect;
            internal Candidate(double score, Rectangle rect) { Score = score; Rect = rect; }
        }

        private static readonly string[] OptionalButtons = { "confirm_btn_hcenter_vcenter", "confirm_btn_highlight_hcenter_vcenter" };
        private static readonly double[] CompactScale = { .75 };
        private static readonly int[] Small = { 0, -2, 2, -4, 4, -6, 6, -8, 8, -10, 10, -12, 12 };
        private readonly FrameIndex index = new FrameIndex();

        internal FarmUi Read(PixelFrame frame, bool detailed)
        {
            var ui = new FarmUi();
            index.Load(frame);
            Rectangle ignored, confirmBox;
            ui.FPrompt = FindNear(frame, "pick_up_f_hcenter_vcenter", 20, .86, .70, out ui.FBox);
            ui.Absorb = FindNear(frame, "absorb_zh_CN", 30, .86, .70, out ignored) ||
                FindNear(frame, "absorb_en_US", 30, .86, .70, out ignored);
            ui.Claim = FindNear(frame, "claim_cancel_button_hcenter_vcenter", 8, .88, .72, out ui.ClaimCancelBox) ||
                FindNear(frame, "cancel_button_hcenter_vcenter", 8, .88, .72, out ignored);
            if (ui.Claim)
                ui.Evidence |= 2;
            ui.TeamStart = FindNear(frame, "team_start_challenge", 12, .72, .70, out ui.TeamStartBox);
            bool grayChallenge = FindNear(frame, "gray_button_challenge", 12, .80, .70, out ui.ChallengeBox);
            bool grayStart = FindNear(frame, "gray_start_battle", 12, .80, .70, out ui.StartBattleBox);
            ui.Challenge = ui.TeamStart || grayChallenge || grayStart;
            if (ui.Challenge)
                ui.Evidence |= 4;
            bool check = FindOptionalCheckbox(frame, out ui.SkipCheckBox);
            bool confirm = FindNear(frame, "skip_dialog_confirm", 8, .86, .70, out confirmBox) ||
                FindOptionalButton(frame, .78, out confirmBox);
            ui.ConfirmBox = confirmBox;
            ui.SkipDialog = check && confirm;
            ui.Death = FindNear(frame, "revive_confirm_hcenter_vcenter", 8, .92, .78, out ui.DeathBox);
            if (ui.Death)
                ui.Evidence |= 1;
            ui.InRealm = FindNear(frame, "illusive_realm_exit", 8, .86, .72, out ignored);
            ui.OpenWorld = !ui.InRealm && FindNear(frame, "world_earth_icon", 8, .86, .72, out ignored);
            if (!ui.InRealm && !ui.OpenWorld)
                ui.InRealm = FindNear(frame, "illusive_realm_exit", 20, .72, .70, out ignored) &&
                    PartyHud.Read(frame, new HudSnapshot());
            if (!detailed)
            {
                ui.Detail = ui.FPrompt ? "可交互" : "探索中";
                return ui;
            }
            ui.Book = FindNear(frame, "gray_book_boss", 12, .80, .70, out ui.BookBox) ||
                FindNear(frame, "gray_book_boss_highlight", 12, .80, .70, out ui.BookBox);
            ui.BookSelected = ui.Book && IsGoldGuideTab(frame, ui.BookBox);
            ui.BookMonsters = FindNear(frame, "gray_book_all_monsters", 10, .86, .72, out ui.BookMonstersBox);
            if (ui.Book)
                ui.Evidence |= 16;
            bool waypointTravel = FindNear(frame, "gray_teleport", 12, .76, .70, out ui.WaypointTravelBox);
            ui.Teleport = FindNear(frame, "fast_travel_custom", 12, .76, .70, out ui.TeleportBox);
            if (!ui.Teleport && waypointTravel)
            {
                ui.Teleport = true;
                ui.TeleportBox = ui.WaypointTravelBox;
            }
            if (!ui.Teleport)
                ui.Teleport = FindNear(frame, "remove_custom", 12, .76, .70, out ui.TeleportBox);
            if (ui.Teleport)
                ui.Evidence |= 32;
            List<Rectangle> markers = FindAll(frame, "boss_check_mark", Ratio(frame, .3, .3, .7, .7), 4, .82, .74);
            ui.HasMapMarker = markers.Count > 0;
            if (ui.HasMapMarker)
                ui.MapMarker = markers[0];
            Rectangle treasure;
            ui.Treasure = FindTreasure(frame, out treasure);
            ui.TreasureBox = treasure;
            if (ui.Treasure)
                ui.Evidence |= 8;
            ui.MonthlyCard = FindMonthlyCard(frame, out ui.MonthlyCardBox);
            ui.ClaimReward = FindNear(frame, "claim_stamina_sign", 10, .86, .74, out ui.ClaimBox);
            if (ui.BookSelected)
            {
                ui.BossRows = FindBookRows(frame);
                ui.ScrollTop = GuidebookList.ScrollTop(frame);
            }
            ui.Detail = ui.Death ? "死亡" : ui.Claim ? "领奖弹窗" : ui.Challenge ? "挑战界面" :
                ui.Book ? "图鉴" : ui.Teleport ? "传送按钮" : ui.FPrompt ? "可交互" :
                ui.InRealm ? "副本内" : ui.OpenWorld ? "大世界" : "探索中";
            return ui;
        }

        internal bool FindOptionalButton(PixelFrame frame, double threshold, out Rectangle box)
        {
            index.Load(frame);
            foreach (string label in OptionalButtons)
                if (FindNear(frame, label, 12, threshold, .70, out box) ||
                    FindCompactControl(frame, label, Math.Max(.78, threshold), out box)) return true;
            box = Rectangle.Empty;
            return false;
        }

        internal bool FindOptionalCheckbox(PixelFrame frame, out Rectangle box)
        {
            index.Load(frame);
            return FindNear(frame, "skip_dialog_check", 12, .78, .70, out box) ||
                FindCompactControl(frame, "skip_dialog_check", .78, out box);
        }

        private bool FindCompactControl(PixelFrame frame, string label, double threshold, out Rectangle box)
        {
            foreach (VisualPattern pattern in PatternBank.For(label))
            {
                Rectangle normal = pattern.Area;
                var compact = new Rectangle(frame.Width / 2 + (int)Math.Round((normal.X - 800) * .75),
                    frame.Height / 2 + (int)Math.Round((normal.Y - 450) * .75),
                    (int)Math.Round(normal.Width * .75), (int)Math.Round(normal.Height * .75));
                if (BestMatchInRegion(frame, new[] { label }, Rectangle.Inflate(compact, 9, 9),
                    1, threshold, .74, out box, CompactScale) != null) return true;
            }
            box = Rectangle.Empty;
            return false;
        }

        internal bool FindLabel(PixelFrame frame, string label, double threshold, out Rectangle found)
        {
            index.Load(frame);
            return FindNear(frame, label, 20, threshold, .70, out found);
        }

        internal List<Rectangle> FindWaypoints(PixelFrame frame, bool confidenceOrder = false)
        {
            Rectangle area = Ratio(frame, .1, .1, .9, .9);
            index.Load(frame);
            var hits = new List<Candidate>();
            foreach (string label in new[] { "map_way_point", "map_way_point_big" })
                foreach (VisualPattern pattern in PatternBank.For(label))
                {
                    var tile = new Rectangle(area.Location, pattern.Area.Size);
                    for (tile.Y = area.Top; tile.Bottom <= area.Bottom; tile.Y++)
                        for (tile.X = area.Left; tile.Right <= area.Right; tile.X++)
                        {
                            // Both glyphs have a bright green and blue core. The smaller
                            // cyan glyph has little red, while the large glyph is white.
                            int red, green, blue;
                            frame.Rgb(tile.X + tile.Width / 2, tile.Y + tile.Height / 3, out red, out green, out blue);
                            if (green < 168 || blue < 168 || !pattern.HasLightEdge(frame, tile, .70)) continue;
                            double rank = pattern.Signature.Similarity(index.Sample(tile));
                            if (rank < .65) continue;
                            double light = pattern.LightSimilarity(frame, tile);
                            if (light >= .70)
                                hits.Add(new Candidate(confidenceOrder ? rank * .45 + light * .55 : light, tile));
                        }
                }
            hits.Sort((a, b) => b.Score.CompareTo(a.Score));
            int margin = Math.Max(1, (int)Math.Round(30.0 * frame.Height / frame.SourceHeight));
            return SortWaypoints(hits.Select(hit => hit.Rect), new Size(frame.Width, frame.Height), margin, confidenceOrder);
        }

        internal bool FindWaypointPopup(PixelFrame frame, out Rectangle box)
        {
            // Reference annotation map_way_point_pop_up_box (3840×2160):
            // (2439,837)-(2639,1379). Only select a waypoint within that
            // overlapping-marker list, never another waypoint on the map.
            Rectangle area = Ratio(frame, 2439.0 / 3840, 837.0 / 2160, 2639.0 / 3840, 1379.0 / 2160);
            return BestMatchInRegion(frame, new[] { "map_way_point" }, area, 1, .65, .70, out box) != null;
        }

        internal static List<Rectangle> SortWaypoints(IEnumerable<Rectangle> candidates, Size size, int margin = 30,
            bool confidenceOrder = false)
        {
            var waypoints = new List<Rectangle>();
            foreach (Rectangle candidate in candidates)
            {
                if (candidate.IsEmpty || waypoints.Exists(other => Math.Abs(other.X - candidate.X) <= margin &&
                    Math.Abs(other.Y - candidate.Y) <= margin))
                    continue;
                waypoints.Add(candidate);
            }
            if (!confidenceOrder)
                waypoints.Sort((left, right) => DistanceSquared(left, size).CompareTo(DistanceSquared(right, size)));
            return waypoints;
        }

        private static long DistanceSquared(Rectangle box, Size size)
        {
            long dx = 2L * box.X + box.Width - size.Width;
            long dy = 2L * box.Y + box.Height - size.Height;
            return dx * dx + dy * dy;
        }

        private bool FindTreasure(PixelFrame frame, out Rectangle found)
        {
            Rectangle area = Ratio(frame, .03, .10, .97, .86);
            found = Rectangle.Empty;
            double best = 0;
            foreach (VisualPattern pattern in PatternBank.For("treasure_icon"))
            {
                int width = pattern.Area.Width, height = pattern.Area.Height;
                int lastX = area.Right - width, lastY = area.Bottom - height;
                for (int y = area.Top; y <= lastY; y++)
                    for (int x = area.Left; x <= lastX; x++)
                    {
                        var sample = new Rectangle(x, y, width, height);
                        // Cheap contrast filtering keeps a pixel-aligned search affordable.
                        if (pattern.HasLight && !pattern.HasLightEdge(frame, sample, .5))
                            continue;
                        double score = pattern.Signature.Similarity(index.Sample(sample));
                        if (score < .88 || score <= best || !PassesLight(frame, pattern, sample, .80))
                            continue;
                        best = score;
                        found = sample;
                    }
            }
            return !found.IsEmpty;
        }

        internal static RechallengeChoice ReadRechallenge(PixelFrame frame, Rectangle key, OcrReader ocr)
        {
            if (key.IsEmpty || ocr == null)
                return RechallengeChoice.None;
            var area = new Rectangle(key.Right, key.Top - 3 * key.Height, 15 * key.Width, 8 * key.Height);
            try { return ChooseRechallenge(ocr.Read(frame, area), key); }
            catch (Microsoft.ML.OnnxRuntime.OnnxRuntimeException) { return RechallengeChoice.None; }
        }

        internal static RechallengeChoice ChooseRechallenge(List<OcrLine> lines, Rectangle key)
        {
            RechallengeChoice choice = RechallengeChoice.None;
            foreach (OcrLine line in lines)
            {
                if (line.Score < .62f || line.Box.IsEmpty || string.IsNullOrWhiteSpace(line.Text) ||
                    line.Box.Left < key.Right - key.Width / 3)
                    continue;
                string text = NormalizeLabel(line.Text);
                int dy = line.Box.Top + line.Box.Height / 2 - (key.Top + key.Height / 2);
                bool selected = Math.Abs(dy) <= Math.Max(8, key.Height * .65);
                if (IsRechallengeLabel(text))
                    return selected ? RechallengeChoice.Selected : dy < 0 ? RechallengeChoice.Above : RechallengeChoice.Below;
                switch (text)
                {
                    case "领取奖励": case "領取獎勵": case "claimreward": case "claimrewards":
                        if (selected)
                            choice = RechallengeChoice.RewardSelected;
                        break;
                }
            }
            return choice;
        }

        internal static Rectangle ReadRechallengeDialog(PixelFrame frame, OcrReader ocr)
        {
            if (ocr == null)
                return Rectangle.Empty;
            int center = frame.Width / 2;
            var region = new Rectangle(center - 440, (int)(frame.Height * .43), 700, (int)(frame.Height * .27));
            List<OcrLine> lines;
            try { lines = ocr.Read(frame, region); }
            catch (Microsoft.ML.OnnxRuntime.OnnxRuntimeException) { return Rectangle.Empty; }

            bool leaveHeading = false;
            Rectangle restart = Rectangle.Empty;
            foreach (OcrLine line in lines)
            {
                if (line.Score < .65f || string.IsNullOrWhiteSpace(line.Text))
                    continue;
                int x = line.Box.Left + line.Box.Width / 2;
                double y = (line.Box.Top + line.Box.Height / 2.0) / frame.Height;
                string text = NormalizeLabel(line.Text).TrimEnd('?', '？');
                if (Math.Abs(x - center) <= 140 && y >= .45 && y <= .55 &&
                    (text == "确认离开" || text == "確認離開" || text == "确定离开" || text == "確定離開" ||
                     text == "confirmexit" || text == "confirmleave" || text == "leavechallenge"))
                    leaveHeading = true;
                if (x >= center - 420 && x <= center - 120 && y >= .59 && y <= .68 && IsRechallengeLabel(text))
                    restart = line.Box;
            }
            return leaveHeading ? restart : Rectangle.Empty;
        }

        private static string NormalizeLabel(string text)
        {
            return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", "").ToLowerInvariant();
        }

        private static bool IsRechallengeLabel(string text)
        {
            return text == "重新挑战" || text == "重新挑戰" || text == "再次挑战" || text == "再次挑戰" ||
                text == "rechallenge" || text == "restartchallenge" || text == "retrychallenge";
        }

        internal bool FindPickupKey(PixelFrame frame, out Rectangle found)
        {
            index.Load(frame);
            found = Rectangle.Empty;
            double best = .80;
            foreach (VisualPattern pattern in PatternBank.For("pick_up_f_hcenter_vcenter"))
            {
                Rectangle expected = pattern.Map(frame.Width, 0);
                int horizontal = expected.Width / 2;
                for (int y = expected.Top - expected.Height * 5; y <= expected.Top + expected.Height * 2; y++)
                    for (int x = expected.Left - horizontal; x <= expected.Left + horizontal; x++)
                    {
                        var trial = new Rectangle(x, y, expected.Width, expected.Height);
                        if (!Inside(frame, trial))
                            continue;
                        double score = pattern.Signature.Similarity(index.Sample(trial));
                        if (score <= best || !PassesLight(frame, pattern, trial, .70))
                            continue;
                        best = score;
                        found = trial;
                    }
            }
            return !found.IsEmpty;
        }

        internal static bool HasEchoReceipt(PixelFrame frame)
        {
            Rectangle area = Ratio(frame, .078, .488, .094, .514);
            foreach (string label in TaskIndicators.EchoReceiptDigits)
                foreach (VisualPattern pattern in PatternBank.For(label))
                    for (int y = area.Top; y <= area.Bottom - pattern.Area.Height; y++)
                        for (int x = area.Left; x <= area.Right - pattern.Area.Width; x++)
                        {
                            var sample = new Rectangle(x, y, pattern.Area.Width, pattern.Area.Height);
                            if (pattern.HasLight && pattern.LightSimilarity(frame, sample, negativeThreshold: 80) >= .66)
                                return true;
                        }
            return false;
        }

        internal bool FindGuidebookBoss(PixelFrame frame, out Rectangle found, out bool selected)
        {
            return InspectGuidebook(frame, out found, out selected) && !found.IsEmpty;
        }

        internal void ReadGuideEntrance(PixelFrame frame, FarmUi ui)
        {
            ui.Terminal = IsTerminal(frame);
            if (ui.Terminal)
            {
                FindTerminalGuide(frame, out ui.TerminalGuide);
                ui.Book = ui.BookSelected = ui.BookMonsters = false;
            }
            else if (!ui.Book && FindGuidebookBoss(frame, out ui.BookBox, out ui.BookSelected))
            {
                ui.Book = true;
            }
        }

        internal void ReadBlockingClaim(PixelFrame frame, FarmUi ui, bool hasHud)
        {
            if (hasHud || ui == null || ui.Terminal || ui.Book || ui.Challenge || ui.SkipDialog || ui.ClaimReward ||
                !ui.ClaimCancelBox.IsEmpty) return;
            if (FindClaimCancel(frame, out ui.ClaimCancelBox)) ui.Claim = true;
        }

        private readonly MonthlyCardVision monthlyCard = new MonthlyCardVision();

        internal bool HasTeamNumbers(PixelFrame frame)
        {
            index.Load(frame);
            int count = 0;
            foreach (string label in TaskIndicators.TeamNumbers)
            {
                Rectangle box;
                if (FindNear(frame, label, 6, .74, .80, out box)) count++;
            }
            return count == 1 || count == 2;
        }

        internal bool FindMonthlyCard(PixelFrame frame, out Rectangle found)
        {
            return monthlyCard.Find(frame, out found);
        }

        internal bool FindClaimCancel(PixelFrame frame, out Rectangle found)
        {
            index.Load(frame);
            foreach (VisualPattern pattern in PatternBank.For("claim_cancel_button_hcenter_vcenter"))
            {
                Rectangle area = pattern.Map(frame.Width, 0);
                area.Inflate(frame.Width / 20, frame.Height / 10);
                if (FindFirst(frame, pattern.Label, area, 2, .80, .70, out found)) return true;
            }
            found = Rectangle.Empty;
            return false;
        }

        internal void ReadBossDestination(PixelFrame frame, FarmUi ui)
        {
            if (ui.Book || ui.Terminal || ui.Teleport || ui.Death || ui.Claim || ui.Challenge) return;
            Rectangle close;
            // Immediately after selecting a guide row, the level selector can show
            // its close button before either the difficulty list or the start button.
            ui.Challenge = FindLabel(frame, "team_close", .80, out close);
        }

        internal void ReadMonsterGuide(PixelFrame frame, FarmUi ui)
        {
            if (ui.Terminal) return;
            Rectangle tab;
            bool selected;
            if (!InspectSidebarGuidebook(frame, "gray_book_all_monsters", out tab, out selected) || tab.IsEmpty) return;
            ui.BookMonsters = true;
            ui.BookMonstersBox = tab;
            ui.BookMonstersSelected = selected;
        }

        internal bool IsTerminal(PixelFrame frame)
        {
            index.Load(frame);
            return MatchTerminal(frame);
        }

        internal bool FindTerminalGuide(PixelFrame frame, out Rectangle guide)
        {
            guide = Rectangle.Empty;
            index.Load(frame);
            return MatchTerminal(frame) && MatchTerminalIcon(frame, "terminal_guide", .87, .78, out guide);
        }

        internal bool FindTerminalMail(PixelFrame frame, out Rectangle mail)
        {
            mail = Rectangle.Empty;
            index.Load(frame);
            return MatchTerminal(frame) && MatchTerminalIcon(frame, "terminal_mail", .86, .78, out mail);
        }

        private bool MatchTerminal(PixelFrame frame)
        {
            Rectangle ignored;
            // The namecard art behind the title varies. Keep the icon's light mask strict.
            return MatchTerminalIcon(frame, "terminal_title_mark", .82, .86, out ignored) &&
                (MatchTerminalIcon(frame, "terminal_close", .88, .78, out ignored) ||
                    MatchTerminalIcon(frame, "esc_setting", .88, .77, out ignored));
        }

        private bool MatchTerminalIcon(PixelFrame frame, string label, double threshold, double lightMinimum,
            out Rectangle found)
        {
            Point ignored;
            return FindGuideTab(frame, label, Point.Empty, 20, 20, threshold, lightMinimum,
                out found, out ignored) > 0;
        }

        internal bool FindGuidebookQuest(PixelFrame frame, out Rectangle quest)
        {
            return FindSidebarTab(frame, "gray_book_quest", out quest);
        }

        private bool FindSidebarTab(PixelFrame frame, string label, out Rectangle tab)
        {
            index.Load(frame);
            Point ignored, sidebarOffset;
            return FindGuideTab(frame, label, Point.Empty, 12, 12, .80, .70, out tab, out ignored) > 0 ||
                TryGuideSidebarOffset(frame, out sidebarOffset) &&
                FindGuideTab(frame, label, sidebarOffset, 12, 12, .80, .70, out tab, out ignored) > 0;
        }

        internal bool IsGuidebookQuestSelected(PixelFrame frame)
        {
            Rectangle quest;
            return FindGuidebookQuest(frame, out quest) && IsGoldGuideTab(frame, quest);
        }

        internal bool InspectQuestGuidebook(PixelFrame frame, out Rectangle quest, out bool selected)
        {
            return InspectSidebarGuidebook(frame, "gray_book_quest", out quest, out selected);
        }

        internal bool InspectSidebarGuidebook(PixelFrame frame, string label, out Rectangle tab, out bool selected)
        {
            if (FindSidebarTab(frame, label, out tab))
            {
                selected = IsGoldGuideTab(frame, tab);
                return true;
            }
            selected = false;
            Rectangle boss;
            bool active;
            return InspectGuidebook(frame, out boss, out active);
        }

        internal bool InspectGuidebook(PixelFrame frame, out Rectangle bossTab, out bool selected)
        {
            index.Load(frame);
            bossTab = FindBossGuideTab(frame, Point.Empty);
            Point sidebarOffset;
            bool guidebookOpen = !bossTab.IsEmpty;
            if (!guidebookOpen && TryGuideSidebarOffset(frame, out sidebarOffset))
            {
                // The sidebar can identify the open book even if the Boss icon is obscured.
                guidebookOpen = true;
                bossTab = FindBossGuideTab(frame, sidebarOffset);
            }
            selected = !bossTab.IsEmpty && IsGoldGuideTab(frame, bossTab);
            return guidebookOpen;
        }

        private Rectangle FindBossGuideTab(PixelFrame frame, Point offset)
        {
            Rectangle bestBox = Rectangle.Empty;
            double bestScore = 0;
            foreach (string label in BossGuideTabs)
            {
                Rectangle box;
                Point ignored;
                double score = FindGuideTab(frame, label, offset, 12, 12, .80, .70, out box, out ignored);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestBox = box;
                }
            }
            return bestBox;
        }

        private bool TryGuideSidebarOffset(PixelFrame frame, out Point offset)
        {
            var matched = new List<Point>(GuideSidebarTabs.Length);
            foreach (string label in GuideSidebarTabs)
            {
                Rectangle ignored;
                Point current;
                if (FindGuideTab(frame, label, Point.Empty, 16, 36, .84, .73, out ignored, out current) <= 0)
                    continue;
                foreach (Point prior in matched)
                {
                    if (Math.Abs(prior.X - current.X) > 8 || Math.Abs(prior.Y - current.Y) > 12)
                        continue;
                    offset = new Point((prior.X + current.X) / 2, (prior.Y + current.Y) / 2);
                    return true;
                }
                matched.Add(current);
            }
            offset = Point.Empty;
            return false;
        }

        private double FindGuideTab(PixelFrame frame, string label, Point offset, int horizontalRadius, int verticalRadius,
            double threshold, double lightMinimum, out Rectangle found, out Point foundOffset)
        {
            found = Rectangle.Empty;
            foundOffset = Point.Empty;
            double best = 0;
            foreach (VisualPattern pattern in PatternBank.For(label))
            {
                Rectangle expected = pattern.Map(frame.Width, 0);
                for (int dy = offset.Y - verticalRadius; dy <= offset.Y + verticalRadius; dy += 2)
                    for (int dx = offset.X - horizontalRadius; dx <= offset.X + horizontalRadius; dx += 2)
                    {
                        var trial = new Rectangle(expected.X + dx, expected.Y + dy, expected.Width, expected.Height);
                        if (!Inside(frame, trial))
                            continue;
                        double score = pattern.Signature.Similarity(index.Sample(trial));
                        if (score < threshold || score <= best || !PassesLight(frame, pattern, trial, lightMinimum))
                            continue;
                        best = score;
                        found = trial;
                        foundOffset = new Point(dx, dy);
                    }
            }
            return best;
        }

        private static bool IsGoldGuideTab(PixelFrame frame, Rectangle found)
        {
            // The inactive and active icons have nearly identical silhouettes.
            // The selected tab changes from blue-grey to gold.
            int gold = 0;
            for (int y = found.Top; y < found.Bottom; y++)
                for (int x = found.Left; x < found.Right; x++)
                {
                    int r, g, b;
                    frame.Rgb(x, y, out r, out g, out b);
                    if (r >= 165 && g >= 140 && r >= b + 20 && g >= b + 14 && r - g <= 65)
                        gold++;
                }
            return gold >= found.Width * found.Height * .07;
        }

        internal string BestMatchScored(PixelFrame frame, string[] labels, string[] priority, int radius,
            double threshold, double lightMinimum, out Rectangle box)
        {
            box = Rectangle.Empty;
            index.Load(frame);
            string bestLabel = null;
            double bestScore = 0;
            foreach (string candidate in labels)
            {
                Rectangle found;
                double score = FindNearScore(frame, candidate, radius, threshold, lightMinimum, out found);
                if (score <= 0)
                    continue;
                bool boosted = priority != null && Array.IndexOf(priority, candidate) >= 0;
                double ranked = score + (boosted ? 10 : 0);
                if (ranked <= bestScore)
                    continue;
                bestScore = ranked;
                bestLabel = candidate;
                box = found;
            }
            return bestLabel;
        }

        private double FindNearScore(PixelFrame frame, string label, int radius, double threshold,
            double lightMinimum, out Rectangle found)
        {
            found = Rectangle.Empty;
            double best = 0;
            foreach (VisualPattern pattern in PatternBank.For(label))
            {
                Rectangle expected = pattern.Map(frame.Width, 0);
                foreach (int dy in Small)
                {
                    if (Math.Abs(dy) > radius) continue;
                    foreach (int dx in Small)
                    {
                        if (Math.Abs(dx) > radius) continue;
                        var trial = new Rectangle(expected.X + dx, expected.Y + dy, expected.Width, expected.Height);
                        if (!Inside(frame, trial)) continue;
                        double score = pattern.Signature.Similarity(index.Sample(trial));
                        if (score < threshold || score <= best) continue;
                        if (!PassesLight(frame, pattern, trial, lightMinimum)) continue;
                        best = score;
                        found = trial;
                    }
                }
            }
            return best;
        }

        internal bool BestMatch(PixelFrame frame, string[] labels, int radius, double threshold,
            double lightMinimum, out string label, out Rectangle box)
        {
            label = null;
            box = Rectangle.Empty;
            index.Load(frame);
            foreach (string candidate in labels)
            {
                if (!FindNear(frame, candidate, radius, threshold, lightMinimum, out box))
                    continue;
                label = candidate;
                return true;
            }
            return false;
        }

        internal bool FindFirst(PixelFrame frame, string label, Rectangle area, int step, double threshold,
            double lightMinimum, out Rectangle found)
        {
            List<Rectangle> all = FindAll(frame, label, area, step, threshold, lightMinimum);
            found = all.Count > 0 ? all[0] : Rectangle.Empty;
            return all.Count > 0;
        }

        internal string BestMatchInRegion(PixelFrame frame, string[] labels, Rectangle area, int step,
            double threshold, double lightMinimum, out Rectangle found, double[] scales = null,
            Func<string, Rectangle, bool> validate = null)
        {
            index.Load(frame);
            found = Rectangle.Empty;
            string label = null;
            double best = 0, other = 0;
            Rectangle limit = Rectangle.Intersect(area, new Rectangle(0, 0, frame.Width, frame.Height));
            foreach (string candidate in labels)
            {
                double candidateScore = 0;
                Rectangle candidateBox = Rectangle.Empty;
                foreach (VisualPattern pattern in PatternBank.For(candidate))
                {
                    int sizes = scales == null ? 1 : scales.Length;
                    for (int indexScale = 0; indexScale < sizes; indexScale++)
                    {
                        double scale = scales == null ? 1 : scales[indexScale];
                        int width = (int)Math.Round(pattern.Area.Width * scale);
                        int height = (int)Math.Round(pattern.Area.Height * scale);
                        for (int y = limit.Top; y <= limit.Bottom - height; y += step)
                            for (int x = limit.Left; x <= limit.Right - width; x += step)
                            {
                                var box = new Rectangle(x, y, width, height);
                                double score = pattern.Signature.Similarity(index.Sample(box));
                                if (score < threshold)
                                    continue;
                                if (pattern.HasLight)
                                {
                                    double light = pattern.LightSimilarity(frame, box);
                                    if (light < lightMinimum)
                                        continue;
                                    score = score * .45 + light * .55;
                                }
                                if (score <= candidateScore || validate != null && !validate(candidate, box))
                                    continue;
                                candidateScore = score;
                                candidateBox = box;
                            }
                    }
                }
                if (candidateScore <= best)
                {
                    other = Math.Max(other, candidateScore);
                    continue;
                }
                other = best;
                best = candidateScore;
                label = candidate;
                found = candidateBox;
            }
            if (other > 0 && best - other < .025)
            {
                found = Rectangle.Empty;
                return null;
            }
            return label;
        }

        internal List<Rectangle> FindAll(PixelFrame frame, string label, Rectangle area, int step, double threshold,
            double lightMinimum)
        {
            index.Load(frame);
            var results = new List<Rectangle>();
            foreach (VisualPattern pattern in PatternBank.For(label))
            {
                int width = pattern.Area.Width, height = pattern.Area.Height;
                Rectangle limit = Rectangle.Intersect(area, new Rectangle(0, 0, frame.Width, frame.Height));
                if (limit.Width < width || limit.Height < height)
                    continue;
                var candidates = new List<Candidate>(8);
                for (int y = limit.Top; y <= limit.Bottom - height; y += step)
                    for (int x = limit.Left; x <= limit.Right - width; x += step)
                    {
                        var trial = new Rectangle(x, y, width, height);
                        double score = pattern.Signature.Similarity(index.Sample(trial));
                        if (score >= threshold)
                            Add(candidates, score, trial);
                    }
                candidates.Sort((left, right) => right.Score.CompareTo(left.Score));
                foreach (Candidate candidate in candidates)
                {
                    if (!PassesLight(frame, pattern, candidate.Rect, lightMinimum))
                        continue;
                    if (Overlaps(results, candidate.Rect))
                        continue;
                    results.Add(candidate.Rect);
                }
            }
            results.Sort((left, right) => left.Y.CompareTo(right.Y));
            return results;
        }

        private bool FindNear(PixelFrame frame, string label, int radius, double threshold, double lightMinimum,
            out Rectangle found)
        {
            found = Rectangle.Empty;
            foreach (VisualPattern pattern in PatternBank.For(label))
            {
                Rectangle expected = pattern.Map(frame.Width, 0);
                var candidates = new List<Candidate>(8);
                foreach (int dy in Small)
                {
                    if (Math.Abs(dy) > radius) continue;
                    foreach (int dx in Small)
                    {
                        if (Math.Abs(dx) > radius) continue;
                        var trial = new Rectangle(expected.X + dx, expected.Y + dy, expected.Width, expected.Height);
                        if (!Inside(frame, trial)) continue;
                        double score = pattern.Signature.Similarity(index.Sample(trial));
                        if (score >= threshold)
                            Add(candidates, score, trial);
                    }
                }
                candidates.Sort((left, right) => right.Score.CompareTo(left.Score));
                foreach (Candidate candidate in candidates)
                {
                    if (!PassesLight(frame, pattern, candidate.Rect, lightMinimum))
                        continue;
                    found = candidate.Rect;
                    return true;
                }
            }
            return false;
        }

        private static bool PassesLight(PixelFrame frame, VisualPattern pattern, Rectangle rect, double minimum)
        {
            return !pattern.HasLight || pattern.LightSimilarity(frame, rect) >= minimum;
        }

        private static bool Overlaps(List<Rectangle> existing, Rectangle rect)
        {
            foreach (Rectangle item in existing)
                if (Math.Abs(item.X - rect.X) < rect.Width && Math.Abs(item.Y - rect.Y) < rect.Height)
                    return true;
            return false;
        }

        private static void Add(List<Candidate> candidates, double score, Rectangle rect)
        {
            if (candidates.Count < 24)
            {
                candidates.Add(new Candidate(score, rect));
                return;
            }
            int worst = 0;
            for (int index = 1; index < candidates.Count; index++)
                if (candidates[index].Score < candidates[worst].Score)
                    worst = index;
            if (score > candidates[worst].Score)
                candidates[worst] = new Candidate(score, rect);
        }

        internal List<Rectangle> FindBookRows(PixelFrame frame)
        {
            Rectangle area = GuidebookList.Area(new Size(frame.Width, frame.Height), .9113, .229, .9613, .861);
            return FindAll(frame, "boss_proceed", area, 2, .80, .74);
        }

        private static Rectangle Ratio(PixelFrame frame, double left, double top, double right, double bottom)
        {
            return new Rectangle((int)(frame.Width * left), (int)(frame.Height * top),
                Math.Max(1, (int)(frame.Width * (right - left))), Math.Max(1, (int)(frame.Height * (bottom - top))));
        }

        private static bool Inside(PixelFrame frame, Rectangle rect)
        {
            return rect.Left >= 0 && rect.Top >= 0 && rect.Right <= frame.Width && rect.Bottom <= frame.Height;
        }
    }
}
