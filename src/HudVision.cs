using System;
using System.Drawing;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TideCombat
{
    internal struct PixelFrame
    {
        private readonly byte[] pixels, echoPixels;
        private static long nextCaptureId;
        internal readonly long CaptureId;
        internal readonly int Width, Height, SourceWidth, SourceHeight;
        internal readonly CloudTargetColors TargetColors;
        internal readonly CamellyaColors Camellya;
        internal readonly FramePatch Concerto, MonthlyCard;

        internal PixelFrame(byte[] pixels, int width, int height, CloudTargetColors targetColors = null, int sourceWidth = 0,
            CamellyaColors camellya = null, FramePatch concerto = null, int sourceHeight = 0, byte[] echoPixels = null, FramePatch monthlyCard = null)
        {
            this.pixels = pixels;
            this.echoPixels = echoPixels;
            CaptureId = System.Threading.Interlocked.Increment(ref nextCaptureId);
            Width = width;
            Height = height;
            SourceWidth = sourceWidth == 0 ? width : sourceWidth;
            SourceHeight = sourceHeight == 0 ? height : sourceHeight;
            TargetColors = targetColors;
            Camellya = camellya;
            Concerto = concerto;
            MonthlyCard = monthlyCard;
        }

        internal bool HasSourcePixels { get { return echoPixels != null; } }
        internal PixelFrame FullResolution
        {
            get { return echoPixels == null ? this : new PixelFrame(echoPixels, SourceWidth, SourceHeight); }
        }

        internal void CopyBgra(Rectangle area, byte[] destination)
        {
            Rectangle clipped = Rectangle.Intersect(area, new Rectangle(0, 0, Width, Height));
            int offset = 0;
            for (int y = clipped.Top; y < clipped.Bottom; y++)
            {
                for (int x = clipped.Left; x < clipped.Right; x++)
                {
                    int red, green, blue;
                    Rgb(x, y, out red, out green, out blue);
                    destination[offset++] = (byte)blue;
                    destination[offset++] = (byte)green;
                    destination[offset++] = (byte)red;
                    destination[offset++] = 255;
                }
            }
        }

        internal void Rgb(int x, int y, out int red, out int green, out int blue)
        {
            int offset = (y * Width + x) * 4;
            blue = pixels[offset];
            green = pixels[offset + 1];
            red = pixels[offset + 2];
        }

        internal int Light(int x, int y)
        {
            int red, green, blue;
            Rgb(x, y, out red, out green, out blue);
            return (3 * red + 6 * green + blue) / 10;
        }

        internal void FillHealthMask(Rectangle area, bool boss, byte[] mask)
        {
            int index = 0;
            for (int y = area.Top; y < area.Bottom; y++)
            {
                int start = (y * Width + area.Left) * 4;
                int end = start + area.Width * 4;
                for (int i = start; i < end; i += 4)
                {
                    int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
                    bool color = boss ? r >= 245 && g >= 30 && g <= 185 && b >= 4 && b <= 75 :
                        r >= 174 && r <= 225 && g >= 55 && g <= 85 && b >= 55 && b <= 76;
                    mask[index++] = color ? (byte)1 : (byte)0;
                }
            }
        }

        internal bool Pale(int x, int y, int minimum, int saturation)
        {
            int red, green, blue;
            Rgb(x, y, out red, out green, out blue);
            int low = Math.Min(red, Math.Min(green, blue));
            return low >= minimum && Math.Max(red, Math.Max(green, blue)) - low <= saturation;
        }
    }

    internal sealed class HudSnapshot
    {
        internal bool Valid, Locked, EnemyHealth, ConcertoFull;
        internal int Current = -1;
        internal int PartySize;
        internal bool TeamVisible;
        internal readonly SkillState[] Skills = new SkillState[3];
        internal readonly double[] Cooldowns = { double.NaN, double.NaN, double.NaN };
        internal readonly bool[] SkillLit = new bool[3];
        internal readonly char[] Keys = new char[3];
        internal readonly double[] Health = new double[3];
        internal readonly bool[] HealthUnknown = new bool[3];
        internal readonly CharacterProfile[] Profiles = new CharacterProfile[3];
        internal readonly string[] Fighters = new string[3];
        internal readonly HashSet<string> Indicators = new HashSet<string>(StringComparer.Ordinal);
        internal double Concerto, Forte, EchoWhiteRatio, ResonanceWhiteRatio;
        internal double ChallengeSeconds = double.NaN;
        internal double Blazes, CharacterCharge = -1, BuddingCharge = -1;
        internal double ZaniNightfallCharge, ZaniForteCharge;
        internal bool EnhancedColor, ForteFullColor, PhoebeConfessionReady;
        internal int ChargeMode, ZaniForm;
        internal int AirCharges, GuaCount, LockOffset, ForteSegments, PhoebeLightForte, PhoebeBlueForte;
        internal int LongActionLayout;
        internal string RoverForm;
        internal bool Airborne, LevitatorKnown, Countdown, BreakPrompt, OrdinaryBreakSampled, InChallenge;
        internal bool ChallengePrompt, ChallengeMarker, ChallengeInteractVisible, OpenWorld, InRealm, Dead;
        internal bool TeamHudHidden, LucillaTransform, BearTargetShifted;
        internal bool XigelikaForte, LinnaiAccelerating, LinnaiColorFull, CantarellaForte;
        internal bool LuhesiLiberation;
        internal bool Shows(string indicator) { return Indicators.Contains(indicator); }
        internal string LayoutText = "未识别";
        internal string Detail = "等待队伍与技能栏";
    }

    internal sealed class HudVision
    {
        private readonly CombatSettings settings;
        private readonly CombatVision combatVision;
        private readonly OcrReader cooldownOcr;
        private char detectedEcho, detectedLiberation;
        private bool skillKeysVerified;

        internal HudVision(CombatSettings settings, ConcertoMeter concerto = null, OcrReader cooldownOcr = null)
        {
            this.settings = settings;
            combatVision = new CombatVision(settings, concerto);
            this.cooldownOcr = cooldownOcr;
        }

        internal HudSnapshot Read(PixelFrame frame, bool allowShortEnemyBar = false, bool cloud = false, int lucillaSlot = -1,
            bool scanExecutionPrompt = true, bool? activeFight = null)
        {
            var snapshot = new HudSnapshot();
            if (frame.Height != FrameReader.StandardHeight || frame.Width < 1565 || frame.Width > 2232)
            {
                combatVision.ResetParty();
                return snapshot;
            }

            bool party = PartyHud.ReadReferenceNumbers(frame, snapshot);
            Badge left = Badge.Find(frame, frame.Width - 251, 847, 30, 10, settings.ResonanceKey);
            string skillGlyphs = "EQRT" + settings.EchoKey + settings.LiberationKey;
            Badge middle = Badge.Find(frame, frame.Width - 168, 847, 23, 10, skillGlyphs);
            Badge right = Badge.Find(frame, frame.Width - 87, 847, 16, 10, skillGlyphs);
            bool ordinarySkillsVisible = left.Found && middle.Found && right.Found;
            if (!party)
            {
                combatVision.ClearChallengeMarker();
                snapshot.Detail = left.Found ? "技能栏可见，队伍栏未确认" : "未确认队伍栏和技能栏";
                if (allowShortEnemyBar)
                {
                    snapshot.Dead = combatVision.HasDeathPrompt(frame);
                    if (!snapshot.Dead && lucillaSlot >= 0 && !left.Found && !middle.Found && !right.Found &&
                        combatVision.ReadLucillaTransform(frame, snapshot, lucillaSlot, cloud))
                        return snapshot;
                    snapshot.TeamHudHidden = !snapshot.Dead && !left.Found && !middle.Found && !right.Found && HasSceneContents(frame);
                }
                if (!snapshot.TeamHudHidden) combatVision.ResetParty();
                return snapshot;
            }
            if (!ordinarySkillsVisible)
            {
                snapshot.Detail = "队伍栏可见，技能栏未确认";
                if (allowShortEnemyBar)
                    combatVision.ReadTransformation(frame, snapshot, cloud);
                return snapshot;
            }

            ReadSkill(frame, left, Skill.Resonance, snapshot);
            ReadSkill(frame, middle, Skill.Echo, snapshot);
            ReadSkill(frame, right, Skill.Liberation, snapshot);
            combatVision.ReadParty(frame, snapshot, activeFight ?? allowShortEnemyBar);
            if (cloud) snapshot.LockOffset = combatVision.CloudLockOffset(frame, out snapshot.LongActionLayout);
            else
            {
                bool recognized;
                snapshot.LockOffset = combatVision.DesktopLockOffset(frame, out recognized, out snapshot.BearTargetShifted,
                    out snapshot.LongActionLayout, snapshot.Fighters[snapshot.Current] == "Lucilla");
            }
            bool shortAction = !cloud && (snapshot.LockOffset == 0 || !skillKeysVerified && settings.DetectSkillKeys) &&
                combatVision.HasShortTarget(frame);
            snapshot.Locked = snapshot.LockOffset > 0;
            snapshot.EnemyHealth = EnemyHud.HasHealth(frame, allowShortEnemyBar, cloud, !snapshot.Locked) || combatVision.HasBossMarker(frame);
            if (cooldownOcr != null && (snapshot.Locked || snapshot.EnemyHealth || (activeFight ?? allowShortEnemyBar)))
            {
                try
                {
                    double[] cooldown = CooldownOcr.Read(frame, cooldownOcr);
                    for (int skill = 0; skill < cooldown.Length; skill++)
                    {
                        snapshot.Cooldowns[skill] = cooldown[skill];
                        snapshot.Skills[skill] = SkillHud.CooldownState(cooldown[skill] > 0, cooldown[skill], snapshot.SkillLit[skill]);
                    }
                }
                catch (Microsoft.ML.OnnxRuntime.OnnxRuntimeException error)
                { RunLog.Error("战斗识别", "参考冷却数值 OCR 失败，保留字形识别", error); }
            }
            if (settings.DetectSkillKeys && !skillKeysVerified && (snapshot.Locked || snapshot.EnemyHealth || allowShortEnemyBar))
            {
                if (!shortAction)
                {
                    detectedEcho = CombatVision.ReadSkillKey(frame, middle);
                    detectedLiberation = CombatVision.ReadSkillKey(frame, right);
                    char echo = settings.KeyFor(Skill.Echo), liberation = settings.KeyFor(Skill.Liberation);
                    if (echo != liberation)
                    {
                        if (detectedEcho == '\0' && detectedLiberation == echo) detectedEcho = liberation;
                        if (detectedLiberation == '\0' && detectedEcho == liberation) detectedLiberation = echo;
                    }
                }
                skillKeysVerified = true;
            }
            snapshot.Keys[(int)Skill.Resonance] = settings.KeyFor(Skill.Resonance);
            snapshot.Keys[(int)Skill.Echo] = settings.DetectSkillKeys && detectedEcho != '\0' ? detectedEcho : settings.KeyFor(Skill.Echo);
            snapshot.Keys[(int)Skill.Liberation] = settings.DetectSkillKeys && detectedLiberation != '\0' ? detectedLiberation : settings.KeyFor(Skill.Liberation);
            snapshot.LayoutText = LabelKey(snapshot.Keys[0]) + " " + LabelKey(snapshot.Keys[1]) + " " + LabelKey(snapshot.Keys[2]);
            if (settings.Characters.Count > 0)
                PortraitReader.Identify(frame, snapshot, settings.Characters);
            combatVision.Read(frame, snapshot, allowShortEnemyBar, scanExecutionPrompt, activeFight);
            combatVision.ReadScene(frame, snapshot);
            snapshot.Valid = true;
            snapshot.Detail = Array.IndexOf(snapshot.Keys, '\0') >= 0
                ? "部分技能键未确认，可手动指定按键" : "已确认游戏 HUD";
            return snapshot;
        }

        private static string LabelKey(char key) { return key != '\0' ? key.ToString() : "？"; }

        private static void ReadSkill(PixelFrame frame, Badge badge, Skill skill, HudSnapshot snapshot)
        {
            int index = (int)skill;
            snapshot.Skills[index] = SkillHud.Read(frame, badge, out snapshot.Cooldowns[index], out snapshot.SkillLit[index]);
            if ((skill == Skill.Echo || skill == Skill.Resonance) && badge.Found)
            {
                int left = badge.X + (skill == Skill.Echo ? -27 : -22);
                int height = skill == Skill.Echo ? 49 : 48;
                int white = 0;
                for (int y = badge.Y - 65; y < badge.Y - 65 + height; y++)
                for (int x = left; x < left + 56; x++)
                    if (frame.Pale(x, y, 244, 11)) white++;
                if (skill == Skill.Echo) snapshot.EchoWhiteRatio = white / (56.0 * height);
                else snapshot.ResonanceWhiteRatio = white / (56.0 * height);
            }
        }

        private static bool HasSceneContents(PixelFrame frame)
        {
            int low = 255, high = 0, visible = 0;
            for (int y = 140; y <= 710; y += 57)
                for (int x = frame.Width / 5; x <= frame.Width * 4 / 5; x += 71)
                {
                    int light = frame.Light(x, y);
                    low = Math.Min(low, light);
                    high = Math.Max(high, light);
                    if (light >= 14) visible++;
                }
            return visible >= 12 && high - low >= 24;
        }
    }

    internal static class CooldownOcr
    {
        private static readonly Regex Decimal = new Regex(@"\d{1,2}\.\d", RegexOptions.CultureInvariant);

        private static double RelativeX(int width, int height, double x)
        {
            double referenceWidth = Math.Min(width, height * (16.0 / 9));
            return 1 - (1 - x) * referenceWidth / width;
        }

        internal static Rectangle ReferenceRegion(int width, int height)
        {
            double left = RelativeX(width, height, .82), right = RelativeX(width, height, .97);
            // The reference rounds the extent independently of the origin.
            return new Rectangle((int)Math.Round(left * width), (int)Math.Round(.86 * height),
                (int)Math.Round((right - left) * width), (int)Math.Round((.93 - .86) * height));
        }

        internal static double[] Read(PixelFrame original, OcrReader ocr)
        {
            PixelFrame source = original.FullResolution;
            Rectangle crop = ReferenceRegion(source.Width, source.Height);
            var pixels = new byte[crop.Width * crop.Height * 4];
            for (int y = 0; y < crop.Height; y++)
            for (int x = 0; x < crop.Width; x++)
            {
                int r, g, b;
                source.Rgb(crop.Left + x, crop.Top + y, out r, out g, out b);
                byte value = r <= 240 && g <= 240 && b <= 240 ? (byte)255 : (byte)0;
                int index = (y * crop.Width + x) * 4;
                pixels[index] = pixels[index + 1] = pixels[index + 2] = value;
                pixels[index + 3] = 255;
            }
            var isolated = new PixelFrame(pixels, crop.Width, crop.Height);
            List<OcrLine> lines = ocr.Read(isolated, new Rectangle(0, 0, crop.Width, crop.Height));
            return Parse(lines, source.Width, source.Height, crop.Left);
        }

        internal static double[] Parse(IEnumerable<OcrLine> lines, int width, int height, int cropLeft)
        {
            var cooldown = new double[3];
            foreach (OcrLine line in lines)
            {
                string text = line.Text ?? string.Empty;
                Match match = Decimal.Match(text);
                if (!match.Success) continue;
                double seconds;
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) &&
                    !double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) seconds = 1;
                int x = cropLeft + line.Box.X;
                // width_of_screen truncates its product, unlike relative_box
                // which rounds the crop origin and extent.
                int skill = x < (int)(RelativeX(width, height, .86) * width) ? 0 :
                    x > (int)(RelativeX(width, height, .91) * width) ? 2 : 1;
                cooldown[skill] = seconds;
            }
            return cooldown;
        }
    }

    internal static class PartyHud
    {
        // Equivalent to BaseWWTask.in_team: one or two visible side-slot
        // numerals determine team size and first unnumbered active slot.
        // Health extraction is information for the planner, not a team gate.
        internal static bool ReadReferenceNumbers(PixelFrame frame, HudSnapshot snapshot)
        {
            var numbers = new bool[3];
            int[] comparison = VisualSignature.Buffer();
            for (int slot = 0; slot < numbers.Length; slot++)
                numbers[slot] = HasBrightNumber(frame, TaskIndicators.TeamNumbers[slot], comparison);
            int current, count;
            if (!ResolveReferenceNumbers(numbers, out current, out count)) return false;
            snapshot.Current = current;
            snapshot.PartySize = count;
            snapshot.TeamVisible = true;
            for (int slot = 0; slot < count; slot++)
            {
                int fill = ReadHealthStrip(frame, slot);
                snapshot.Health[slot] = Math.Min(1.0, fill / 65.0);
                // No visible white body does not prove a character is dead.
                // The explicit death cue/manual recovery path remains separate.
                snapshot.HealthUnknown[slot] = fill == 0;
            }
            return true;
        }

        internal static bool ResolveReferenceNumbers(bool[] numbers, out int current, out int count)
        {
            current = -1;
            int numbered = 0;
            for (int slot = 0; slot < numbers.Length; slot++)
            {
                if (numbers[slot]) numbered++;
                else if (current < 0) current = slot;
            }
            count = numbered + 1;
            if (numbered == 1 || numbered == 2) return true;
            current = -1;
            return false;
        }

        internal static bool Read(PixelFrame frame, HudSnapshot snapshot)
        {
            var candidates = new bool[3];
            int count = 0;
            int alive = 0;
            bool gap = false;
            for (int slot = 0; slot < 3; slot++)
            {
                int fill = ReadHealthStrip(frame, slot);
                Badge key = Badge.Find(frame, frame.Width - 146, 185 + slot * 110, 9, 9,
                    ((char)('1' + slot)).ToString());
                bool present = fill > 0 || (key.Found && key.Glyph != '\0');
                if (!present)
                {
                    gap = true;
                    continue;
                }
                if (gap)
                    return false;
                count++;
                snapshot.Health[slot] = Math.Min(1.0, fill / 65.0);
                if (fill > 0)
                {
                    alive++;
                    candidates[slot] = !key.Found;
                }
            }
            if (count == 0 || alive == 0)
                return false;
            for (int slot = 0; slot < count; slot++)
            {
                if (!candidates[slot])
                    continue;
                if (snapshot.Current >= 0)
                {
                    snapshot.Current = -1;
                    return false;
                }
                snapshot.Current = slot;
            }
            if (snapshot.Current < 0 || !OwnHealthVisible(frame)) return false;
            snapshot.PartySize = count;
            snapshot.TeamVisible = true;
            return true;
        }

        internal static bool TryReadTemplateFallback(PixelFrame frame, HudSnapshot snapshot)
        {
            // The normal read already found all three live health strips. The template
            // path only resolves the active slot when a generic badge match is ambiguous.
            if (snapshot.Health[0] <= 0 || snapshot.Health[1] <= 0 || snapshot.Health[2] <= 0 || !OwnHealthVisible(frame))
                return false;

            int numbered = 0, current = -1;
            int[] comparison = VisualSignature.Buffer();
            for (int slot = 0; slot < 3; slot++)
            {
                if (HasBrightNumber(frame, TaskIndicators.TeamNumbers[slot], comparison))
                    numbered++;
                else
                    current = slot;
            }
            if (numbered != 2)
                return false;

            snapshot.Current = current;
            snapshot.PartySize = 3;
            return true;
        }

        private static bool HasBrightNumber(PixelFrame frame, string label, int[] comparison)
        {
            foreach (VisualPattern pattern in PatternBank.For(label))
            {
                Rectangle expected = pattern.Map(frame.Width, 0);
                var pixels = new AreaPixels(frame, Rectangle.Inflate(expected, 6, 6));
                for (int dy = -6; dy <= 6; dy++)
                for (int dx = -6; dx <= 6; dx++)
                {
                    var trial = new Rectangle(expected.X + dx, expected.Y + dy, expected.Width, expected.Height);
                    if (!pixels.Contains(trial) || !pattern.HasLightEdge(frame, trial, .50))
                        continue;
                    if ((pattern.HasColorCorrelation ? pattern.ColorCorrelation(frame, trial) : pattern.LightSimilarity(frame, trial)) >= .80)
                        return true;
                }
            }
            return false;
        }

        private static bool BrightBadge(PixelFrame frame, Rectangle bounds)
        {
            int pale = 0;
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            for (int x = bounds.Left; x < bounds.Right; x++)
                if (frame.Pale(x, y, 200, 48)) pale++;
            return pale * 2 >= bounds.Width * bounds.Height;
        }

        private static int ReadHealthStrip(PixelFrame frame, int slot)
        {
            int left = frame.Width - 136;
            int y = 241 + slot * 110;
            int best = 0;
            for (int row = y - 5; row <= y + 6; row++)
            {
                for (int x = left - 5; x <= left + 6; x++)
                {
                    if (!frame.Pale(x, row, 147, 52))
                        continue;
                    int end = x + 1;
                    while (end < left + 69 && frame.Pale(end, row, 147, 52))
                        end++;
                    int length = end - x;
                    if (length > best && length >= 6 && HasLowerBorder(frame, x, row, length))
                        best = length;
                    x = end;
                }
            }
            return best;
        }

        private static bool HasLowerBorder(PixelFrame frame, int left, int row, int length)
        {
            for (int offset = 1; offset <= 4; offset++)
            {
                int contrast = 0;
                for (int segment = 1; segment <= 4; segment++)
                {
                    int x = left + segment * length / 5;
                    if (frame.Light(x, row + offset) < frame.Light(x, row) - 45)
                        contrast++;
                }
                if (contrast >= 3)
                    return true;
            }
            return false;
        }

        private static bool OwnHealthVisible(PixelFrame frame)
        {
            int left = frame.Width / 2 - 132;
            for (int y = 853; y <= 872; y++)
            {
                int bright = 0;
                for (int x = left; x < left + 110; x++)
                {
                    bright = frame.Pale(x, y, 165, 49) ? bright + 1 : 0;
                    if (bright >= 14 && frame.Light(x - 5, y + 5) < frame.Light(x - 5, y) - 30)
                        return true;
                }
            }
            return false;
        }

    }

    internal static class EnemyHud
    {
        private static readonly int[] LockOffsets = { 390, 442, 472, 521, 560, 644 };

        internal static bool HasLock(PixelFrame frame, int skillKeyY)
        {
            return LockOffset(frame, skillKeyY) > 0;
        }

        internal static int LockOffset(PixelFrame frame, int skillKeyY)
        {
            // The lock symbol moves horizontally when extra actions are present.
            foreach (int offset in LockOffsets)
            {
                int match = SearchLock(frame, offset, skillKeyY);
                if (match > 0) return match;
            }
            return 0;
        }

        internal static int ShortLockOffset(PixelFrame frame) { return SearchLock(frame, 329, 847); }

        internal static bool HasDesktopLockColor(PixelFrame frame, int centerX, int centerY)
        {
            for (int y = centerY - 5; y <= centerY + 5; y++)
                for (int x = centerX - 6; x <= centerX + 6; x++)
                    if (IsLockIcon(frame, x, y)) return true;
            return false;
        }

        private static int SearchLock(PixelFrame frame, int offset, int skillKeyY)
        {
            for (int x = frame.Width - offset - 19; x <= frame.Width - offset + 19; x++)
            {
                for (int y = skillKeyY - 43; y <= skillKeyY - 29; y++)
                {
                    if (!Gold(frame, x - 9, y) && !Gold(frame, x + 9, y) &&
                        !Gold(frame, x, y + 9) && !Gold(frame, x, y - 9))
                        continue;
                    if (IsLockIcon(frame, x, y)) return frame.Width - x;
                }
            }
            return 0;
        }

        private static bool IsLockIcon(PixelFrame frame, int x, int y)
        {
            int goldLeft = 0, goldRight = 0, goldBottom = 0, goldTop = 0, white = 0, upperWhite = 0;
            for (int dy = -11; dy <= 11; dy++)
            {
                for (int dx = -11; dx <= 11; dx++)
                {
                    int square = dx * dx + dy * dy;
                    if (square < 67 && frame.Pale(x + dx, y + dy, 183, 58))
                    {
                        white++;
                        if (dy < 0)
                            upperWhite++;
                    }
                    if (square < 50 || square > 143 || !Gold(frame, x + dx, y + dy))
                        continue;
                    if (dx < -3) goldLeft++;
                    if (dx > 3) goldRight++;
                    if (dy > 4) goldBottom++;
                    if (dy < -5) goldTop++;
                }
            }
            if (white < 32 || white > 147 || upperWhite < 9 ||
                goldLeft < 1 || goldRight < 1 || goldLeft + goldRight < 4 ||
                (goldBottom < 3 && goldTop < 4))
                return false;
            return LocateMouseBadge(frame, x, y + 28) || LocateMouseBadge(frame, x, y + 33) ||
                LocateMouseBadge(frame, x, y + 37);
        }

        private static bool LocateMouseBadge(PixelFrame frame, int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                int left = 0, right = 0;
                for (int step = -5; step <= 5; step++)
                {
                    if (frame.Pale(x - 3, y + dy + step, 176, 67)) left++;
                    if (frame.Pale(x + 3, y + dy + step, 176, 67)) right++;
                }
                int edges = (frame.Light(x - 3, y + dy) + frame.Light(x + 3, y + dy)) / 2;
                if (left >= 6 && right >= 6 && frame.Light(x, y + dy) + 21 < edges)
                    return true;
            }
            return false;
        }

        private static bool Gold(PixelFrame frame, int x, int y)
        {
            int red, green, blue;
            frame.Rgb(x, y, out red, out green, out blue);
            return red >= 136 && green >= 107 && green <= red + 8 &&
                red - green < 86 && green - blue >= 27;
        }

        internal static bool HasHealth(PixelFrame frame, bool allowShort, bool cloud = false, bool searchFullFrame = true)
        {
            // Reference tests color rectangles over the entire frame even when
            // lock-on is already known. Use source pixels where the capture
            // route provides them, then the same screen-relative limits.
            frame = frame.FullResolution;
            var viewport = new Rectangle(0, 0, frame.Width, frame.Height);
            if (FindColorRectangles(frame, viewport, allowShort, cloud, false) > 0) return true;
            Rectangle boss = BossHealthRegion(frame.Width, frame.Height);
            boss.Intersect(viewport);
            return FindColorRectangles(frame, boss, allowShort, cloud, true) == 1;
        }

        internal static Rectangle BossHealthRegion(int width, int height)
        {
            double sx = width / 3840.0, sy = height / 2160.0;
            if (Math.Abs(width / (double)height - 16.0 / 9) <= .01)
                return new Rectangle((int)Math.Round(1269 * sx), (int)Math.Round(58 * sy),
                    (int)Math.Round(1264 * sx), (int)Math.Round(142 * sy));
            // box_of_screen(..., hcenter=True, vcenter=True) centers the
            // declared 16:9 base screen within other aspect ratios.
            double scale = Math.Min(sx, sy);
            return new Rectangle((int)Math.Round(width / 2.0 + (1269 - 1920) * scale),
                (int)Math.Round(height / 2.0 + (58 - 1080) * scale),
                (int)Math.Round(1264 * scale), (int)Math.Round(142 * scale));
        }

        [ThreadStatic]
        private static byte[] healthMask;

        private sealed class ColorComponent
        {
            internal int Left, Right, Top, Bottom, Pixels;
            internal ColorComponent Parent;

            internal ColorComponent Root()
            {
                if (Parent != null) Parent = Parent.Root();
                return Parent ?? this;
            }

            internal void Union(ColorComponent other)
            {
                other = other.Root();
                if (other == this) return;
                other.Parent = this;
                Left = Math.Min(Left, other.Left);
                Right = Math.Max(Right, other.Right);
                Top = Math.Min(Top, other.Top);
                Bottom = Math.Max(Bottom, other.Bottom);
                Pixels += other.Pixels;
            }
        }

        private struct ColorRun
        {
            internal int Left, Right;
            internal ColorComponent Component;
        }

        private static int FindColorRectangles(PixelFrame frame, Rectangle area, bool active, bool cloud, bool boss)
        {
            int minWidth = Math.Max(1, (int)(frame.Width * (active ? 12.0 : 100.0) / 3840));
            int ordinaryMinHeight = Math.Max(1, (int)(frame.Height * 9.0 / 2160));
            double minHeight = boss ? ordinaryMinHeight * 1.3 : ordinaryMinHeight;
            double fill = cloud ? (boss ? .85 : .60) : .95;
            int length = area.Width * area.Height;
            if (healthMask == null || healthMask.Length < length) healthMask = new byte[length];
            frame.FillHealthMask(area, boss, healthMask);
            // RETR_LIST considers both exterior contours and enclosed holes.
            // A hole uses 4-connected background; its contour runs along the
            // neighboring foreground boundary, extending its box one pixel.
            int valid = CountMaskContours(healthMask, area.Width, area.Height, false,
                minWidth, minHeight, ordinaryMinHeight * 3, fill, cloud, boss);
            if ((!boss && valid > 0) || (boss && valid > 1)) return valid;
            return valid + CountMaskContours(healthMask, area.Width, area.Height, true,
                minWidth, minHeight, ordinaryMinHeight * 3, fill, cloud, boss);
        }

        private static int CountMaskContours(byte[] mask, int width, int height, bool holes,
            int minWidth, double minHeight, int maxHeight, double fill, bool cloud, bool boss)
        {
            var previous = new List<ColorRun>();
            var current = new List<ColorRun>();
            var live = new HashSet<ColorComponent>();
            var completed = new HashSet<ColorComponent>();
            int valid = 0;
            byte selected = holes ? (byte)0 : (byte)1;
            for (int y = 0; y < height; y++)
            {
                current.Clear();
                int previousIndex = 0;
                int offset = y * width;
                for (int x = 0; x < width;)
                {
                    if (mask[offset + x] != selected) { x++; continue; }
                    int left = x++;
                    while (x < width && mask[offset + x] == selected) x++;
                    while (previousIndex < previous.Count && (holes ? previous[previousIndex].Right <= left : previous[previousIndex].Right < left)) previousIndex++;
                    ColorComponent component = null;
                    for (int i = previousIndex; i < previous.Count && (holes ? previous[i].Left < x : previous[i].Left <= x); i++)
                    {
                        ColorComponent linked = previous[i].Component.Root();
                        if (component == null) component = linked;
                        else component.Union(linked);
                    }
                    if (component == null)
                    {
                        component = new ColorComponent { Left = left, Right = x, Top = y, Bottom = y + 1 };
                    }
                    component.Left = Math.Min(component.Left, left);
                    component.Right = Math.Max(component.Right, x);
                    component.Bottom = y + 1;
                    component.Pixels += x - left;
                    current.Add(new ColorRun { Left = left, Right = x, Component = component });
                }
                // Finalize components as soon as none of their runs reach the
                // new row. The active set stays proportional to a few scanlines,
                // not to all colored noise components in a 4K frame.
                live.Clear();
                completed.Clear();
                foreach (ColorRun run in current) live.Add(run.Component.Root());
                foreach (ColorRun run in previous)
                {
                    ColorComponent component = run.Component.Root();
                    if (live.Contains(component) || !completed.Add(component)) continue;
                    if (!Qualifies(component, mask, width, height, holes, minWidth, minHeight, maxHeight, fill, cloud, boss)) continue;
                    valid++;
                    if (!boss || valid > 1) return valid;
                }
                List<ColorRun> reuse = previous;
                previous = current;
                current = reuse;
            }
            completed.Clear();
            foreach (ColorRun run in previous)
            {
                ColorComponent component = run.Component.Root();
                if (!completed.Add(component) || !Qualifies(component, mask, width, height, holes, minWidth, minHeight, maxHeight, fill, cloud, boss)) continue;
                valid++;
                if (!boss || valid > 1) return valid;
            }
            return valid;
        }

        private static bool Qualifies(ColorComponent component, byte[] mask, int maskWidth, int maskHeight, bool holes,
            int minWidth, double minHeight, int maxHeight,
            double fill, bool cloud, bool boss)
        {
            if (holes && (component.Left == 0 || component.Top == 0 || component.Right == maskWidth || component.Bottom == maskHeight))
                return false;
            int left = component.Left - (holes ? 1 : 0), right = component.Right + (holes ? 1 : 0);
            int top = component.Top - (holes ? 1 : 0), bottom = component.Bottom + (holes ? 1 : 0);
            int width = right - left, height = bottom - top;
            if (width < minWidth || height < minHeight || !boss && height > maxHeight || !boss && cloud && width < height * 3)
                return false;
            double needed = width * height * fill;
            // Reference counts every matching pixel in the contour's bounding
            // mask, even a disconnected patch inside it. Only rescan when
            // the exterior component's count alone could be insufficient.
            if (!holes && component.Pixels >= needed) return true;
            int count = 0;
            for (int y = top; y < bottom; y++)
                for (int i = y * maskWidth + left, end = i + width; i < end; i++) count += mask[i];
            return count >= needed;
        }

        private static bool ScanBars(PixelFrame frame, int left, int right, int top, int bottom, int minWidth,
            bool boss, bool cloud)
        {
            for (int y = top; y <= bottom; y++)
            {
                int start = left;
                int last = left - 5;
                for (int x = left; x <= right; x++)
                {
                    bool color = x < right && IsHealthColor(frame, x, y, boss);
                    if (color)
                    {
                        if (x - last > 3)
                            start = x;
                        last = x;
                    }
                    else if ((x - last == 3 || x == right) && last - start >= minWidth &&
                        IsStrip(frame, start, last, y, boss, cloud))
                        return true;
                }
            }
            return false;
        }

        private static bool IsStrip(PixelFrame frame, int start, int end, int y, bool boss, bool cloud)
        {
            int width = end - start;
            int samples = Math.Min(20, width / 2);
            int fill = 0, outline = 0, thick = 0, border = 0, text = 0;
            for (int index = 0; index < samples; index++)
            {
                int x = start + (index + 1) * width / (samples + 1);
                if (IsHealthColor(frame, x, y, boss) && IsHealthColor(frame, x, y + 2, boss))
                    fill++;
                if (IsHealthColor(frame, x, y - 4, boss) && IsHealthColor(frame, x, y + 8, boss))
                    thick++;
                if (!IsHealthColor(frame, x, y - 3, boss) || !IsHealthColor(frame, x, y + 6, boss))
                    outline++;
                if (frame.Light(x, y - 3) + 26 < frame.Light(x, y) ||
                    frame.Light(x, y + 5) + 26 < frame.Light(x, y))
                    border++;
                for (int height = 10; height <= 25; height += 3)
                {
                    if (!frame.Pale(x, y - height, 179, 63))
                        continue;
                    text++;
                    break;
                }
            }
            return fill >= samples * (cloud ? 0.60 : 0.75) && thick < samples / 3 && outline >= samples * 0.55 &&
                (border >= samples * 0.55 || text >= samples * 0.20);
        }

        private static bool IsHealthColor(PixelFrame frame, int x, int y, bool boss)
        {
            int red, green, blue;
            frame.Rgb(x, y, out red, out green, out blue);
            if (boss)
                return red >= 218 && green >= 35 && green <= 192 && blue <= 102 &&
                    red - green >= 53 && red - blue >= 120;
            return red >= 154 && red <= 250 && green >= 33 && green <= 112 && blue >= 32 && blue <= 108 &&
                red - Math.Max(green, blue) >= 80 && Math.Abs(green - blue) <= 43;
        }
    }
}
