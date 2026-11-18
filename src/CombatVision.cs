using System;
using System.Collections.Generic;
using System.Drawing;

namespace TideCombat
{
    internal sealed class CombatVision
    {
        private static readonly int[] Search = { 0, -2, 2, -4, 4 };
        private static readonly int[] IconSearch = { 0, -1, 1, -2, 2, -4, 4 };
        private static readonly int[] WideSearch = { -12, -8, -4, 0, 4, 8, 12 };
        private static readonly string[] Common = { "mouse_forte", "e_forte" };
        internal static readonly string[] SceneIndicators = { "world_earth_icon", "illusive_realm_exit",
            "abyss_start_marker", "abyss_tower_start_marker", "pick_up_f_hcenter_vcenter",
            "revive_confirm_hcenter_vcenter", "f_break", "target_box_short", "boss_break_lock", "boss_break_shield", "dialog_3_dots" };
        internal static readonly string[] CloudTargetLabels = { "has_target_cloud", "no_target_cloud" };
        internal static readonly string[] DesktopTargetLabels = { "has_target", "no_target", "has_target_169", "no_target_169" };
        internal static readonly string[] DesktopTargetAreas = { "box_target_enemy_long", "target_box_long2" };
        internal static readonly string[] HotkeyLabels = { "t", "e", "r", "q" };
        private readonly CombatSettings settings;
        private readonly int[] comparison = VisualSignature.Buffer();
        private readonly string[] known = new string[3];
        private readonly byte[][] lastPortrait = new byte[3][];
        private readonly ConcertoMeter concerto;
        private int frames;
        private bool toolWasVisible;

        internal CombatVision(CombatSettings settings, ConcertoMeter concerto = null)
        {
            this.settings = settings;
            this.concerto = concerto ?? new ConcertoMeter();
        }

        internal void Read(PixelFrame frame, HudSnapshot hud, bool activeFight, bool scanExecutionPrompt = true)
        {
            CharacterKit fighter = CharacterRoster.Find(hud.Fighters[hud.Current]);
            ReadConcerto(frame, hud);
            hud.Forte = EstimateForte(frame);
            hud.ForteFullColor = ReadFullForte(frame);
            hud.ChallengeSeconds = HudGlyphs.ReadChallengeClock(frame);
            hud.Countdown = hud.ChallengeSeconds > 0;
            ReadLevitator(frame, hud, activeFight);
            if (!hud.Locked && !hud.EnemyHealth && !activeFight)
                return;

            var middle = new AreaPixels(frame, new Rectangle(frame.Width / 2 - 178, 725, 365, 145));
            var right = new AreaPixels(frame, new Rectangle(frame.Width - 575, 755, 568, 101));
            foreach (string indicator in Common)
            {
                if (Find(frame, middle, indicator, indicator == "e_forte" ? .88 : .82) ||
                    FindLightPattern(frame, indicator, .79, frame.Width / 40, frame.Height * 15 / 1000))
                    hud.Indicators.Add(indicator);
            }
            if (FindLightPattern(frame, "f_break_full", .85, 8, 8))
                hud.Indicators.Add("f_break_full");
            if (hud.Shows("f_break_full")) hud.BreakPrompt = true;
            else if (scanExecutionPrompt)
            {
                hud.OrdinaryBreakSampled = true;
                hud.BreakPrompt = HasOrdinaryExecutionPrompt(frame);
            }
            if (fighter == null) return;
            foreach (string indicator in fighter.Indicators)
            {
                VisualPattern[] patterns = PatternBank.For(indicator);
                if (patterns.Length == 0) continue;
                AreaPixels region = patterns[0].Area.Left > 1000 ? right : middle;
                if (indicator == "douling_gen" || indicator == "douling_zhen" ||
                    indicator.StartsWith("roccia_air_", StringComparison.Ordinal))
                    continue;
                double confidence = indicator.StartsWith("lily_", StringComparison.Ordinal) ? .87 :
                    indicator == "luhesi_kick" || indicator.StartsWith("hsin_", StringComparison.Ordinal) ||
                    indicator.StartsWith("qingxiao_", StringComparison.Ordinal) ? .70 : .83;
                if (Find(frame, region, indicator, confidence))
                    hud.Indicators.Add(indicator);
            }
            if (fighter.Id == "Roccia")
                ReadRocciaCharges(frame, hud);
            if (fighter.Id == "Douling")
                hud.GuaCount = CountHexagrams(frame, middle);
            if (fighter.Id == "Lily")
            {
                if (hud.Shows("lily_slumberland"))
                {
                    hud.Indicators.Remove("lily_liberation");
                    hud.Indicators.Remove("mouse_forte");
                }
                if (hud.Shows("lily_slumberland") || hud.Shows("mouse_forte"))
                    hud.Indicators.Remove("lily_heavy");
            }
            ReadStances(frame, hud, fighter, right);
        }

        private void ReadLevitator(PixelFrame frame, HudSnapshot hud, bool activeFight)
        {
            if (!settings.CheckLevitator) return;
            var box = new Rectangle(frame.Width - 344, 786, 34, 34);
            if (!activeFight)
                toolWasVisible = Find(frame, new AreaPixels(frame, Rectangle.Inflate(box, 12, 6)), "edge_levitator", .65);
            hud.LevitatorKnown = toolWasVisible;
            if (toolWasVisible)
            {
                hud.Indicators.Add("edge_levitator");
                hud.Airborne = ColorRatio(frame, box, (r, g, b) => r >= 244 && g >= 244 && b >= 244) < .10;
            }
        }

        internal static char ReadSkillKey(PixelFrame frame, Badge badge)
        {
            double best = .70;
            char selected = badge.Glyph;
            foreach (string label in HotkeyLabels)
            foreach (VisualPattern pattern in PatternBank.For(label))
            foreach (int dy in Search)
            foreach (int dx in Search)
            {
                int width = pattern.Area.Width, height = pattern.Area.Height;
                var box = new Rectangle(badge.X - width / 2 + dx, badge.Y - height / 2 + dy, width, height);
                double score = pattern.LightSimilarity(frame, box);
                if (score <= best) continue;
                best = score;
                selected = char.ToUpperInvariant(label[0]);
            }
            return selected;
        }

        internal void ReadTransformation(PixelFrame frame, HudSnapshot hud, bool cloud)
        {
            ReadParty(frame, hud);
            string fighter = hud.Fighters[hud.Current];
            if (fighter == "Lily")
            {
                var skills = new AreaPixels(frame, new Rectangle(frame.Width - 575, 755, 568, 101));
                if (!Find(frame, skills, "lily_slumberland", .89)) return;
                hud.Indicators.Add("lily_slumberland");
                hud.Skills[2] = SkillState.Cooling;
            }
            else
            {
                if (fighter == "Lucilla") ReadLucillaTransform(frame, hud, hud.Current, cloud);
                return;
            }
            ReadConcerto(frame, hud);
            hud.Valid = true;
            hud.Detail = "Lily 梦乡中";
        }

        internal bool ReadLucillaTransform(PixelFrame frame, HudSnapshot hud, int slot, bool cloud)
        {
            int layout;
            int offset = cloud ? CloudLockOffset(frame, out layout) : HasShortTarget(frame) ? 329 : 0;
            if (offset == 0) return false;
            hud.Current = slot;
            hud.Fighters[slot] = "Lucilla";
            hud.LockOffset = offset;
            hud.Locked = hud.LucillaTransform = hud.Valid = true;
            hud.EnemyHealth = EnemyHud.HasHealth(frame, true, cloud) || HasBossMarker(frame);
            ReadConcerto(frame, hud);
            hud.Detail = "洛瑟菈变身中";
            return true;
        }

        internal bool HasShortTarget(PixelFrame frame)
        {
            return HasRegionSignal(frame, "target_box_short", .82, 14) || EnemyHud.ShortLockOffset(frame) > 0;
        }

        internal int DesktopLockOffset(PixelFrame frame, out bool recognized, out bool shifted, out int longLayout)
        {
            int labelOffset = frame.SourceWidth == 1600 ? 2 : 0;
            string lockedLabel = DesktopTargetLabels[labelOffset], unlockedLabel = DesktopTargetLabels[labelOffset + 1];
            VisualPattern[] locked = PatternBank.For(lockedLabel), unlocked = PatternBank.For(unlockedLabel);
            recognized = shifted = false;
            longLayout = 0;
            if (locked.Length == 0 || unlocked.Length == 0) return 0;
            Rectangle normal = locked[0].Map(frame.Width, 0);
            Rectangle icon = FindDesktopTargetIn(frame, Rectangle.Inflate(normal, 2, 2), locked, unlocked);
            if (!icon.IsEmpty)
            {
                recognized = true;
                return TargetOffset(frame, icon);
            }
            for (int layout = 0; layout < DesktopTargetAreas.Length; layout++)
            {
                foreach (VisualPattern area in PatternBank.For(DesktopTargetAreas[layout]))
                {
                    icon = FindDesktopTargetIn(frame, area.Map(frame.Width, 0), locked, unlocked);
                    if (icon.IsEmpty) continue;
                    recognized = true;
                    longLayout = layout + 1;
                    return TargetOffset(frame, icon);
                }
            }
            icon = FindDesktopTargetIn(frame, Rectangle.Inflate(normal, 2, normal.Height / 2), locked, unlocked);
            if (icon.IsEmpty) return 0;
            recognized = shifted = true;
            return TargetOffset(frame, icon);
        }

        private static int TargetOffset(PixelFrame frame, Rectangle icon)
        {
            int x = icon.X + icon.Width / 2, y = icon.Y + icon.Height / 2;
            return HasGold(frame, icon) || EnemyHud.HasDesktopLockColor(frame, x, y) ? frame.Width - x : 0;
        }

        private static Rectangle FindDesktopTargetIn(PixelFrame frame, Rectangle limits, VisualPattern[] locked,
            VisualPattern[] unlocked)
        {
            limits.Intersect(new Rectangle(0, 0, frame.Width, frame.Height));
            Rectangle found = Rectangle.Empty;
            double best = .60;
            for (int type = 0; type < 2; type++)
            {
                foreach (VisualPattern template in type == 0 ? locked : unlocked)
                {
                    int width = template.Area.Width, height = template.Area.Height;
                    for (int y = limits.Top; y <= limits.Bottom - height; y++)
                    {
                        for (int x = limits.Left; x <= limits.Right - width; x++)
                        {
                            var location = new Rectangle(x, y, width, height);
                            double score = template.LightSimilarity(frame, location);
                            if (score <= best) continue;
                            best = score;
                            found = location;
                        }
                    }
                }
            }
            return found;
        }

        internal int CloudLockOffset(PixelFrame frame, out int longLayout)
        {
            Rectangle icon = FindCloudTargetIcon(frame);
            longLayout = 0;
            if (icon.IsEmpty) return 0;
            for (int layout = 0; layout < DesktopTargetAreas.Length && longLayout == 0; layout++)
                foreach (VisualPattern area in PatternBank.For(DesktopTargetAreas[layout]))
                    if (area.Map(frame.Width, 0).Contains(icon)) longLayout = layout + 1;
            bool locked = frame.TargetColors != null ? frame.TargetColors.HasGold(icon, frame.Width, frame.Height) : HasGold(frame, icon);
            return locked ? frame.Width - (icon.X + icon.Width / 2) : 0;
        }

        private static bool HasGold(PixelFrame frame, Rectangle icon)
        {
            int gold = 0;
            for (int y = icon.Top; y < icon.Bottom; y++)
            {
                for (int x = icon.Left; x < icon.Right; x++)
                {
                    int r, g, b;
                    frame.Rgb(x, y, out r, out g, out b);
                    if (CloudTargetColors.IsGold(r, g, b)) gold++;
                }
            }
            return gold * 50 >= icon.Width * icon.Height;
        }

        internal Rectangle FindCloudTargetIcon(PixelFrame frame)
        {
            Rectangle bounds = CloudTargetColors.SearchBounds(frame.Width, frame.Height);
            var region = new AreaPixels(frame, bounds);
            Rectangle found = Rectangle.Empty;
            double score = .60;
            foreach (string label in CloudTargetLabels)
            {
                foreach (VisualPattern pattern in PatternBank.For(label))
                {
                    Rectangle normal = pattern.Map(frame.Width, 0);
                    FindCloudTargetIcon(frame, region, pattern, Rectangle.Inflate(normal, 13, 7), 1, ref found, ref score);
                }
            }
            if (score >= .87) return found;
            foreach (string label in CloudTargetLabels)
            {
                foreach (VisualPattern pattern in PatternBank.For(label))
                {
                    FindCloudTargetIcon(frame, region, pattern, bounds, 2, ref found, ref score);
                    if (!found.IsEmpty)
                        FindCloudTargetIcon(frame, region, pattern, Rectangle.Inflate(found, 2, 2), 1, ref found, ref score);
                }
            }
            return found;
        }

        private void FindCloudTargetIcon(PixelFrame frame, AreaPixels region, VisualPattern pattern, Rectangle bounds,
            int step, ref Rectangle found, ref double score)
        {
            int width = pattern.Area.Width, height = pattern.Area.Height;
            for (int y = bounds.Top; y <= bounds.Bottom - height; y += step)
            {
                for (int x = bounds.Left; x <= bounds.Right - width; x += step)
                {
                    var placement = new Rectangle(x, y, width, height);
                    if (!region.Contains(placement) || frame.Light(x + width / 2, y + height / 2) < 100) continue;
                    VisualSignature.Sample(region, placement, comparison);
                    if (pattern.Signature.Similarity(comparison) < .71) continue;
                    double similarity = pattern.LightSimilarity(frame, placement);
                    if (similarity <= score) continue;
                    found = placement;
                    score = similarity;
                }
            }
        }

        internal void ReadScene(PixelFrame frame, HudSnapshot hud)
        {
            int center = frame.Width / 2;
            var corner = new AreaPixels(frame, new Rectangle(0, 10, 74, 75));
            bool realm = Find(frame, corner, "illusive_realm_exit", .91);
            bool world = Find(frame, corner, "world_earth_icon", .86);
            hud.InRealm = realm && !world;
            hud.OpenWorld = world && !realm;
            hud.InChallenge = hud.Countdown || hud.InRealm;
            if (hud.OpenWorld || hud.Locked)
                return;
            var centerPrompt = new AreaPixels(frame, new Rectangle(center - 62, 385, 125, 101));
            if (!Find(frame, centerPrompt, "abyss_start_marker", .87) &&
                !Find(frame, centerPrompt, "abyss_tower_start_marker", .87))
                return;
            hud.InChallenge = true;
            hud.ChallengeMarker = true;
            hud.ChallengePrompt = HasRegionSignal(frame, "pick_up_f_hcenter_vcenter", .88, 15);
        }

        internal bool HasDeathPrompt(PixelFrame frame)
        {
            return HasRegionSignal(frame, "revive_confirm_hcenter_vcenter", .91, 10);
        }

        internal bool HasBossMarker(PixelFrame frame)
        {
            return HasRegionSignal(frame, "boss_break_shield", .84, 8) || HasRegionSignal(frame, "boss_break_lock", .84, 8);
        }

        internal bool HasOrdinaryExecutionPrompt(PixelFrame frame)
        {
            bool found = FindLightPattern(frame, "f_break", .78, 15, 15);
            if (!found)
            {
                var limits = Rectangle.FromLTRB(frame.Width / 5, frame.Height / 5, frame.Width * 3 / 4, frame.Height * 4 / 5);
                foreach (VisualPattern pattern in PatternBank.For("f_break"))
                {
                    if (FindLightPatternIn(frame, pattern, limits, .87).IsEmpty) continue;
                    found = true;
                    break;
                }
            }
            return found && !HasDialoguePrompt(frame);
        }

        private static bool HasDialoguePrompt(PixelFrame frame)
        {
            foreach (VisualPattern button in PatternBank.For("pick_up_f_hcenter_vcenter"))
            {
                Rectangle expected = button.Map(frame.Width, 0);
                var bounds = new Rectangle(expected.X - expected.Width * 3 / 10, expected.Y - expected.Height * 5,
                    expected.Width * 165 / 100, expected.Height * 15 / 2);
                Rectangle icon = FindLightPatternIn(frame, button, bounds, .80);
                if (icon.IsEmpty) continue;
                var text = new Rectangle(icon.X + icon.Width * 3, icon.Y - icon.Height, icon.Width * 28 / 10, icon.Height * 3);
                foreach (VisualPattern pattern in PatternBank.For("dialog_3_dots"))
                    if (!FindLightPatternIn(frame, pattern, text, .72).IsEmpty) return true;
            }
            return false;
        }

        private static bool FindLightPattern(PixelFrame frame, string indicator, double similarity, int horizontal, int vertical)
        {
            foreach (VisualPattern pattern in PatternBank.For(indicator))
            {
                Rectangle placement = pattern.Map(frame.Width, 0);
                if (!FindLightPatternIn(frame, pattern, Rectangle.Inflate(placement, horizontal, vertical), similarity).IsEmpty)
                    return true;
            }
            return false;
        }

        private static Rectangle FindLightPatternIn(PixelFrame frame, VisualPattern pattern, Rectangle limits, double similarity)
        {
            limits.Intersect(new Rectangle(0, 0, frame.Width, frame.Height));
            int width = pattern.Area.Width, height = pattern.Area.Height;
            for (int y = limits.Top; y <= limits.Bottom - height; y++)
            for (int x = limits.Left; x <= limits.Right - width; x++)
            {
                var trial = new Rectangle(x, y, width, height);
                if (pattern.HasLightEdge(frame, trial) && pattern.LightSimilarity(frame, trial) >= similarity &&
                    HasLitGlyph(frame, pattern.Label, trial)) return trial;
            }
            return Rectangle.Empty;
        }

        private void ReadRocciaCharges(PixelFrame frame, HudSnapshot hud)
        {
            var bounds = new Rectangle(frame.Width / 2 - 142, 804, 254, 46);
            var region = new AreaPixels(frame, bounds);
            const string firstLabel = "roccia_air_forte", secondLabel = "roccia_air_forte_second";
            Rectangle first = FindRocciaFace(frame, bounds, region, firstLabel, .70);
            Rectangle second = FindRocciaFace(frame, bounds, region, secondLabel, .80);
            if (!first.IsEmpty) hud.Indicators.Add(firstLabel);
            if (!second.IsEmpty) hud.Indicators.Add(secondLabel);
            if (!first.IsEmpty || !second.IsEmpty) hud.AirCharges = 1;
            if (!first.IsEmpty && !second.IsEmpty && Math.Abs(first.X - second.X) > Math.Max(first.Width, second.Width))
                hud.AirCharges = 2;
        }

        private Rectangle FindRocciaFace(PixelFrame frame, Rectangle bounds, AreaPixels region, string label, double score)
        {
            Rectangle best = Rectangle.Empty;
            foreach (VisualPattern pattern in PatternBank.For(label))
            {
                int width = pattern.Area.Width, height = pattern.Area.Height;
                for (int y = bounds.Top; y <= bounds.Bottom - height; y++)
                for (int x = bounds.Left; x <= bounds.Right - width; x++)
                {
                    var placement = new Rectangle(x, y, width, height);
                    if (!pattern.HasLightEdge(frame, placement)) continue;
                    double rank = pattern.LightSimilarity(frame, placement);
                    if (rank < score) continue;
                    VisualSignature.Sample(region, placement, comparison);
                    if (pattern.Signature.Similarity(comparison) < .78) continue;
                    best = placement;
                    score = rank;
                }
                if (!best.IsEmpty) continue;
                Rectangle expected = pattern.Map(frame.Width, 0);
                double localRank = .78;
                foreach (int dy in IconSearch)
                foreach (int dx in IconSearch)
                {
                    Rectangle placement = expected;
                    placement.Offset(dx, dy);
                    if (!region.Contains(placement) || !pattern.MatchesLight(frame, placement)) continue;
                    VisualSignature.Sample(region, placement, comparison);
                    double rank = pattern.Signature.Similarity(comparison);
                    if (rank < localRank) continue;
                    best = placement;
                    localRank = rank;
                }
            }
            return best;
        }

        private bool HasRegionSignal(PixelFrame frame, string label, double limit, int padding)
        {
            foreach (VisualPattern signal in PatternBank.For(label))
            {
                Rectangle bounds = Rectangle.Inflate(signal.Map(frame.Width, 0), padding, padding);
                var area = new AreaPixels(frame, bounds);
                if (Find(frame, area, label, limit)) return true;
            }
            return false;
        }

        private void ReadStances(PixelFrame frame, HudSnapshot hud, CharacterKit fighter, AreaPixels right)
        {
            int center = frame.Width / 2;
            if (fighter.Id == "Luhesi")
                hud.LuhesiLiberation = ColorRatio(frame, new Rectangle(frame.Width - 107, 786, 38, 28),
                    (r, g, b) => r >= 244 && g >= 244 && b >= 244) > 0;
            if (fighter.Id == "Xigelika")
                hud.XigelikaForte = ColorRatio(frame, new Rectangle(center + 147, 829, 14, 14),
                    (r, g, b) => r >= 244 && g >= 246 && b >= 250) > .10;
            if (fighter.Id == "Cantarella")
                hud.CantarellaForte = ColorRatio(frame, new Rectangle(center + 148, 825, 17, 19),
                    (r, g, b) => r >= 244 && g >= 246 && b >= 250) > .06;
            if (fighter.Id == "Linnai")
            {
                hud.LinnaiAccelerating = hud.LongActionLayout != 0 || HasShortTarget(frame);
                hud.LinnaiColorFull = ColorRatio(frame, new Rectangle(center + 89, 813, 13, 27),
                    (r, g, b) => r >= 244 && g >= 246 && b >= 250) > .06;
            }
            if (fighter.Id == "Cartethyia")
            {
                if (FindLightPattern(frame, "forte_cartethyia_sword3_half", .63, 4, 4)) hud.Indicators.Add("cartethyia_small");
                if (FindLightPattern(frame, "forte_cartethyia_sword2_half", .84, 4, 4)) hud.Indicators.Add("cartethyia_sword2_shape");
                if (CartethyiaAirCue(frame, center)) hud.Indicators.Add("cartethyia_air_cue");
            }
            if (fighter.Id == "Ciaccona")
                hud.ForteSegments = hud.Shows("mouse_forte") ? 3 : CountStripedForte(frame,
                    new Rectangle(center - 128, 828, 240, 9), 3, 12, 14, 100,
                    (r, g, b) => r >= 70 && r <= 100 && g >= 240 && b >= 180 && b <= 210);
            if (fighter.Id == "Changli")
                hud.ForteSegments = hud.Shows("mouse_forte") ? 4 : CountStripedForte(frame,
                    new Rectangle(center - 120, 835, 220, 5), 4, 9, 11, 400,
                    (r, g, b) => r >= 240 && g >= 85 && g <= 105 && b >= 95 && b <= 115);
            if (fighter.Id == "Lupa" && hud.ForteFullColor)
                hud.ForteSegments = CountStripedForte(frame, new Rectangle(center - 120, 835, 220, 5), 2, 19, 21, 400,
                    (r, g, b) => r >= 235 && g >= 75 && g <= 105 && b >= 75 && b <= 105);
            if (fighter.Id == "Zani")
            {
                double blazes = ColorRatio(frame, new Rectangle(center - 122, 839, 228, 2),
                    (r, g, b) => r >= 231 && g >= 239 && b >= 171 && b <= 201);
                hud.Blazes = Math.Ceiling(100 * blazes) / 100;
                double forte = ColorRatio(frame, new Rectangle(center - 122, 832, 231, 3),
                    (r, g, b) => r >= 239 && g >= 222 && b >= 156 && b <= 196);
                hud.ZaniForteCharge = Math.Round(forte, 3, MidpointRounding.AwayFromZero);
                hud.ZaniNightfallCharge = MaskedRingRatio(frame, center + 393, 805, 29, 34,
                    (r, g, b) => r >= 245 && g >= 245 && b >= 205 && b <= 225);
                foreach (VisualPattern pattern in PatternBank.For("target_enemy_long_inner"))
                {
                    var normal = new Rectangle(center + 404, 806, pattern.Area.Width, pattern.Area.Height);
                    var liberated = new Rectangle(center + 321, 806, pattern.Area.Width, pattern.Area.Height);
                    double ordinary = Best(right, pattern, normal, IconSearch, IconSearch, frame);
                    double transformed = Best(right, pattern, liberated, IconSearch, IconSearch, frame);
                    if (Math.Max(ordinary, transformed) >= .84 && Math.Abs(ordinary - transformed) >= .035)
                        hud.ZaniForm = ordinary > transformed ? 1 : 2;
                }
            }
            if (fighter.Id == "Camellya" && frame.Camellya != null)
            {
                hud.EnhancedColor = frame.Camellya.EphemeralReady();
                hud.CharacterCharge = frame.Camellya.Charge(false);
                hud.BuddingCharge = frame.Camellya.Charge(true);
            }
            if (fighter.Id == "Carlotta" || fighter.Id == "Zhezhi")
            {
                bool zhezhi = fighter.Id == "Zhezhi";
                var meter = new Rectangle(center - 124, zhezhi ? 836 : 834, 230, 4);
                hud.ForteSegments = zhezhi ? CountStripedForte(frame, meter, 3, 12, 14, 100,
                    (r, g, b) => r >= 185 && r <= 215 && g >= 240 && b >= 235) : CountStripedForte(frame, meter, 4, 9, 11, 100,
                    (r, g, b) => r >= 70 && r <= 100 && g >= 195 && g <= 225 && b >= 235);
                if (zhezhi)
                    hud.EnhancedColor = ColorRatio(frame, new Rectangle(center - 99, 832, 18, 9),
                        (r, g, b) => r >= 244 && g >= 244 && b >= 244) > .30;
            }
            if (fighter.Id == "Phoebe")
            {
                var star = new Rectangle(center - 12, 837, 10, 9);
                double light = ColorRatio(frame, star, (r, g, b) => r >= 235 && g >= 220 && g <= 250 && b >= 160 && b <= 190);
                double blue = ColorRatio(frame, star, (r, g, b) => r >= 240 && g >= 240 && b >= 240);
                if (light > .25 || blue > .25) hud.ChargeMode = light > blue ? 1 : 2;
                hud.PhoebeConfessionReady = MaskedRingRatio(frame, frame.Width - 248, 806, 28, 33,
                    (r, g, b) => r >= 124 && r <= 134 && g >= 176 && g <= 186 && b >= 250) > .15;
                var meter = new Rectangle(center - 120, 835, 220, 4);
                hud.PhoebeLightForte = CountStripedForte(frame, meter, 4, 0, 0, 25,
                    (r, g, b) => r >= 240 && g >= 240 && b >= 165 && b <= 195, true);
                hud.PhoebeBlueForte = CountStripedForte(frame, meter, 2, 0, 0, 50,
                    (r, g, b) => r >= 225 && g >= 225 && b >= 190 && b <= 225, true);
            }
        }

        private static double MaskedRingRatio(PixelFrame frame, int cx, int cy, int inner, int outer,
            Func<int, int, int, bool> matches)
        {
            int samples = 0, filled = 0;
            for (int y = -outer; y <= outer; y++)
            for (int x = -outer; x <= outer; x++)
            {
                int radius = x * x + y * y;
                if (radius <= inner * inner || radius > outer * outer) continue;
                int r, g, b;
                frame.Rgb(cx + x, cy + y, out r, out g, out b);
                samples++;
                if (matches(r, g, b)) filled++;
            }
            return (double)filled / samples;
        }

        private static bool CartethyiaAirCue(PixelFrame frame, int center)
        {
            int count = 0, total = 0, squared = 0, white = 0;
            for (int y = 832; y < 843; y++)
            for (int x = center + 158; x < center + 184; x++)
            {
                int light = frame.Light(x, y);
                total += light;
                squared += light * light;
                count++;
                int r, g, b;
                frame.Rgb(x, y, out r, out g, out b);
                if (r >= 244 && g >= 246 && b >= 250) white++;
            }
            double average = (double)total / count;
            return white > .15 * count && average > 190 && (double)squared / count - average * average < 45 * 45;
        }

        private static int CountStripedForte(PixelFrame frame, Rectangle meter, int sections, int minFrequency,
            int maxFrequency, int minAmplitude, Func<int, int, int, bool> matches, bool prefix = false)
        {
            int width = meter.Width / sections;
            double scale = (double)frame.SourceWidth / frame.Width;
            if (width * scale < 64) return 0;
            double normalizedAmplitude = minAmplitude / (scale * scale);
            var columns = new int[width];
            int nativeWidth = (int)Math.Round(meter.Width * scale);
            int count = prefix ? Math.Min(sections, (nativeWidth - 1) / (nativeWidth / sections)) : sections;
            for (int index = 0; index < count; index++)
            {
                int section = prefix ? index : sections - 1 - index;
                int left = meter.Left + section * width, filled = 0;
                Array.Clear(columns, 0, columns.Length);
                for (int x = 0; x < width; x++)
                for (int y = meter.Top; y < meter.Bottom; y++)
                {
                    int r, g, b;
                    frame.Rgb(left + x, y, out r, out g, out b);
                    if (!matches(r, g, b)) continue;
                    columns[x]++;
                    filled++;
                }
                if (filled == 0 || filled == width * meter.Height)
                {
                    if (prefix) return index;
                    continue;
                }
                int strongest = 0;
                double amplitude = 0;
                for (int frequency = 1; frequency <= width / 2; frequency++)
                {
                    double real = 0, imaginary = 0;
                    double step = frequency * Math.PI * 2 / width;
                    for (int x = 0; x < width; x++)
                    {
                        real += columns[x] * Math.Cos(step * x);
                        imaginary += columns[x] * Math.Sin(step * x);
                    }
                    double energy = real * real + imaginary * imaginary;
                    if (energy <= amplitude) continue;
                    strongest = frequency;
                    amplitude = energy;
                }
                bool present = amplitude >= normalizedAmplitude * normalizedAmplitude ||
                    minFrequency > 0 && strongest >= minFrequency && strongest <= maxFrequency;
                if (prefix && !present) return index;
                if (!prefix && present) return section + 1;
            }
            return prefix ? count : 0;
        }

        private static double ColorRatio(PixelFrame frame, Rectangle area, Func<int, int, int, bool> matches)
        {
            int count = 0;
            for (int y = area.Top; y < area.Bottom; y++)
            {
                for (int x = area.Left; x < area.Right; x++)
                {
                    int r, g, b;
                    frame.Rgb(x, y, out r, out g, out b);
                    if (matches(r, g, b)) count++;
                }
            }
            return (double)count / (area.Width * area.Height);
        }

        internal void ReadParty(PixelFrame frame, HudSnapshot hud)
        {
            frames++;
            for (int slot = 0; slot < known.Length; slot++)
            {
                string manual = settings.PartyCharacters[slot];
                if (CharacterRoster.Find(manual) != null)
                {
                    hud.Fighters[slot] = manual;
                    continue;
                }
                if (hud.Health[slot] <= 0)
                    continue;
                byte[] seen = PortraitReader.Sample(frame, slot);
                bool changed = lastPortrait[slot] == null || PortraitReader.Distance(lastPortrait[slot], seen) > 18;
                if (changed || known[slot] == null && frames % 13 == 1)
                {
                    string candidate = Identify(frame, slot);
                    if (candidate != null || changed)
                    {
                        known[slot] = candidate;
                        lastPortrait[slot] = seen;
                    }
                }
                hud.Fighters[slot] = known[slot];
            }
        }

        internal string Identify(PixelFrame frame, int slot)
        {
            var area = new AreaPixels(frame, new Rectangle(frame.Width - 160, 165 + slot * 110, 120, 80));
            var scores = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (VisualPattern pattern in PatternBank.Portraits)
            {
                double match = Best(area, pattern, pattern.Map(frame.Width, slot), Search);
                double existing;
                if (!scores.TryGetValue(pattern.Fighter, out existing) || match > existing)
                    scores[pattern.Fighter] = match;
            }
            string selected = null;
            double first = 0, second = 0;
            foreach (KeyValuePair<string, double> candidate in scores)
            {
                if (candidate.Value > first)
                {
                    second = first;
                    first = candidate.Value;
                    selected = candidate.Key;
                }
                else second = Math.Max(second, candidate.Value);
            }
            return first >= .83 && first - second >= .040 ? selected : null;
        }

        private bool Find(PixelFrame frame, AreaPixels region, string indicator, double confidence)
        {
            foreach (VisualPattern pattern in PatternBank.For(indicator))
            {
                Rectangle placed = pattern.Map(frame.Width, 0);
                if (Best(region, pattern, placed, IconSearch, IconSearch, frame) >= confidence)
                    return true;
                if (placed.Left >= frame.Width - 575 &&
                    Best(region, pattern, placed, WideSearch, IconSearch, frame) >= confidence + .025)
                    return true;
            }
            return false;
        }

        private double Best(AreaPixels area, VisualPattern pattern, Rectangle expected, int[] shifts)
        {
            return Best(area, pattern, expected, shifts, Search, default(PixelFrame));
        }

        private double Best(AreaPixels area, VisualPattern pattern, Rectangle expected, int[] shifts,
            int[] vertical, PixelFrame frame)
        {
            double best = 0;
            foreach (int dy in vertical)
            {
                foreach (int dx in shifts)
                {
                    var trial = new Rectangle(expected.X + dx, expected.Y + dy, expected.Width, expected.Height);
                    if (!area.Contains(trial)) continue;
                    VisualSignature.Sample(area, trial, comparison);
                    double score = pattern.Signature.Similarity(comparison);
                    if (score <= best || frame.Width > 0 && (!pattern.MatchesLight(frame, trial) || !HasLitGlyph(frame, pattern.Label, trial)))
                        continue;
                    best = score;
                    if (best >= .99) return best;
                }
            }
            return best;
        }

        private static bool HasLitGlyph(PixelFrame frame, string label, Rectangle area)
        {
            if (label != "mouse_forte" && label != "e_forte") return true;
            bool mouse = label == "mouse_forte";
            int bright = 0;
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                    if (frame.Pale(x, y, mouse ? 229 : 209, 58)) bright++;
            return bright >= area.Width * area.Height * (mouse ? .13 : .10);
        }

        private int CountHexagrams(PixelFrame frame, AreaPixels area)
        {
            // The whole row stays centered and the glyphs move left as more are filled.
            int center = frame.Width / 2 - 21;
            VisualPattern[] gen = PatternBank.For("douling_gen");
            VisualPattern[] zhen = PatternBank.For("douling_zhen");
            int selected = 0;
            double best = 0;
            for (int count = 1; count <= 4; count++)
            {
                double sum = 0, weakest = 1;
                int distinct = 0;
                for (int index = 0; index < count; index++)
                {
                    int x = center + (2 * index - count + 1) * 53 / 2;
                    double rank = Math.Max(GlyphAt(area, gen, x), GlyphAt(area, zhen, x));
                    sum += rank;
                    weakest = Math.Min(weakest, rank);
                    if (rank >= .72) distinct++;
                }
                double average = sum / count;
                // A four-symbol row can contain a dimmed glyph during consumption.
                bool aligned = count == 4 ? distinct >= 3 && weakest >= .55 : weakest >= .665;
                double minimumAverage = count == 1 ? .745 : count == 4 ? .775 : .785;
                if (!aligned || average < minimumAverage) continue;
                double score = average + count * .035;
                if (score <= best) continue;
                best = score;
                selected = count;
            }
            return selected;
        }

        private double GlyphAt(AreaPixels area, VisualPattern[] patterns, int x)
        {
            double best = 0;
            foreach (VisualPattern pattern in patterns)
            {
                var placement = new Rectangle(x, pattern.Area.Y, pattern.Area.Width, pattern.Area.Height);
                best = Math.Max(best, Best(area, pattern, placement, IconSearch, IconSearch, default(PixelFrame)));
            }
            return best;
        }

        private static double EstimateForte(PixelFrame frame)
        {
            int width = 226;
            int center = frame.Width / 2;
            var colored = new bool[width];
            for (int index = 0; index < width; index++)
            {
                int x = center - 118 + index;
                for (int y = 827; y <= 841; y++)
                {
                    int r, g, b;
                    frame.Rgb(x, y, out r, out g, out b);
                    int high = Math.Max(r, Math.Max(g, b)), low = Math.Min(r, Math.Min(g, b));
                    if (high >= 201 && high - low >= 54 && frame.Light(x, y - 6) < high - 22)
                    {
                        colored[index] = true;
                        break;
                    }
                }
            }
            int fills = 0, empty = 0;
            for (int from = 0; from < width; from += 4)
            {
                bool fill = false;
                for (int i = from; i < Math.Min(width, from + 4); i++) fill |= colored[i];
                if (fill)
                {
                    fills++;
                    empty = 0;
                }
                else if (++empty > 3) break;
            }
            return Math.Min(1, fills / 54.0);
        }

        private static bool ReadFullForte(PixelFrame frame)
        {
            int center = frame.Width / 2, white = 0;
            for (int y = 830; y < 840; y++)
            for (int x = center + 138; x < center + 163; x++)
            {
                int r, g, b;
                frame.Rgb(x, y, out r, out g, out b);
                if (r >= 244 && g >= 246 && b >= 250) white++;
            }
            return white > 20;
        }

        private void ReadConcerto(PixelFrame frame, HudSnapshot hud)
        {
            string current = hud.Fighters[hud.Current];
            ConcertoColor observed;
            hud.Concerto = concerto.Read(frame, CharacterKit.Ring(current), out observed);
            hud.ConcertoFull = hud.Concerto == 1;
            if (current != "Rover") return;
            switch (observed)
            {
                case ConcertoColor.Spectro: hud.RoverForm = "RoverSpectro"; break;
                case ConcertoColor.Electro: hud.RoverForm = "RoverElectro"; break;
                case ConcertoColor.Aero: hud.RoverForm = "RoverAero"; break;
                case ConcertoColor.Havoc: hud.RoverForm = "RoverHavoc"; break;
            }
        }
    }
}
