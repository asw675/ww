using System;
using System.Drawing;
using System.Linq;
using System.Reflection;

namespace TideCombat
{
    internal static class CombatReferenceChecks
    {
        internal static void Run(Action<string, Action> check)
        {
            check("reference cooldown OCR columns and off-field frozen time", CooldownColumns);
            check("reference team numeral count and first missing slot", TeamNumbers);
            check("RGB normalized correlation and embedded reference features", ColorCorrelation);
            check("reference target search windows and portrait scene cache", ReferenceTemplateSearch);
            check("reference health rectangles and boss uniqueness on raw pixels", HealthRectangles);
            check("reference concerto polygon and raw-area calibration", ConcertoContour);
            check("combat polling follows each frame and allows unknown-health candidates", PollingAndSwitch);
        }

        private static void Require(bool expected, string message)
        {
            if (!expected) throw new InvalidOperationException(message);
        }

        private static void CooldownColumns()
        {
            var lines = new[] { new OcrLine { Box = new Rectangle(10,0,20,10), Text = "CD12.3" },
                new OcrLine { Box = new Rectangle(80,0,20,10), Text = "3.0" },
                new OcrLine { Box = new Rectangle(150,0,20,10), Text = "0.2" } };
            double[] values = CooldownOcr.Parse(lines, 2560, 900, 2272);
            Require(values.SequenceEqual(new[] { 12.3, 3.0, .2 }) &&
                CooldownOcr.Parse(new OcrLine[0], 2560, 900, 2272).All(v => v == 0),
                "right-aligned ultrawide columns and missing OCR default");
            Rectangle nativeCrop = CooldownOcr.ReferenceRegion(1601,901);
            double[] edge = CooldownOcr.Parse(new[] { new OcrLine { Box = new Rectangle(1376-nativeCrop.Left,0,10,10), Text = "1.2" } },
                1601,901,nativeCrop.Left);
            Require(nativeCrop.Left == 1313 && nativeCrop.Width == 240 && nativeCrop.Height == 63 && edge[0] == 0 && edge[1] == 1.2,
                "relative crop rounds its extent; width_of_screen truncates the column boundary independently");
            var actor = new FighterMemory { Id = "Carlotta", Resource = 1 };
            var hud = new HudSnapshot();
            hud.Cooldowns[0] = 2;
            actor.ObserveCooldowns(hud, 1000);
            var clock = new FreezeClock();
            clock.Record(1100, 1100, 2200);
            Require(Math.Abs(actor.RemainingCooldown(Skill.Resonance, clock, 2800) - 1.2) < .0001 &&
                !actor.CarlottaReady(clock, 2800) && actor.CarlottaReady(clock, 3850),
                "off-field cooldown subtracts only elapsed non-frozen time and uses the 0.2s threshold");
        }

        private static void TeamNumbers()
        {
            int current, count;
            Require(!PartyHud.ResolveReferenceNumbers(new[] { false, false, false }, out current, out count) && count == 1,
                "zero numerals is not a reference team");
            Require(PartyHud.ResolveReferenceNumbers(new[] { true, false, false }, out current, out count) && current == 1 && count == 2,
                "one numeral is a two-person team and first missing slot is active");
            Require(PartyHud.ResolveReferenceNumbers(new[] { true, false, true }, out current, out count) && current == 1 && count == 3,
                "two numerals identify a three-person team");
            Require(!PartyHud.ResolveReferenceNumbers(new[] { true, true, true }, out current, out count) && current == -1,
                "all numbered has no active slot");
        }

        private static void ColorCorrelation()
        {
            byte[] template = new byte[4 * 4 * 3], same = new byte[4 * 4 * 4], inverted = new byte[same.Length];
            for (int i = 0; i < 16; i++)
            {
                byte r = (byte)(10 + i * 8), g = (byte)(18 + (i % 4) * 32), b = (byte)(20 + (i / 4) * 45);
                template[3 * i] = same[4 * i + 2] = r;
                template[3 * i + 1] = same[4 * i + 1] = g;
                template[3 * i + 2] = same[4 * i] = b;
                inverted[4 * i + 2] = (byte)(255 - r); inverted[4 * i + 1] = (byte)(255 - g); inverted[4 * i] = (byte)(255 - b);
            }
            var box = new Rectangle(0,0,4,4);
            var pattern = new VisualPattern("sample", null, box, null, null, rgb: template);
            Require(Math.Abs(pattern.ColorCorrelation(new PixelFrame(same,4,4),box) - 1) < .00001 &&
                Math.Abs(pattern.ColorCorrelation(new PixelFrame(inverted,4,4),box) + 1) < .00001,
                "normalized per-channel color correlation agrees with exact and anti-correlated samples");
            Require(PatternBank.Portraits.All(p => p.HasColorCorrelation) &&
                CombatVision.DesktopTargetLabels.All(label => PatternBank.For(label).All(p => p.HasColorCorrelation)),
                "all portrait and lock labels have regenerated numeric RGB features");
            Require(PatternBank.Portraits.All(p => p.Reference != null) &&
                CombatVision.DesktopTargetLabels.All(label => PatternBank.For(label).All(p => p.Reference != null)),
                "untrimmed original annotations are available independently of legacy rank features");
            var random = new Random(21);
            byte[] region = new byte[19*17*4], sample = new byte[7*5*3];
            for(int trial=0;trial<10;trial++)
            {
                random.NextBytes(region); random.NextBytes(sample);
                PixelFrame source = new PixelFrame(region,19,17);
                for(int y=0;y<5;y++) for(int x=0;x<7;x++)
                {
                    int i=((y+3)*19+x+8)*4,j=(y*7+x)*3;
                    if(x!=2 || y!=1) { region[i]=sample[j+2];region[i+1]=sample[j+1];region[i+2]=sample[j]; }
                }
                var untrimmed=new VisualPattern("test",null,new Rectangle(0,0,7,5),null,null,rgb:sample);
                double brute=0;
                for(int y=0;y<=12;y++) for(int x=0;x<=12;x++) brute=Math.Max(brute,untrimmed.ColorCorrelation(source,new Rectangle(x,y,7,5)));
                double dense=untrimmed.BestColorCorrelation(new ColorMatchArea(source,new Rectangle(0,0,19,17)),.6);
                Require(Math.Abs(brute-dense)<1e-8, "dense matcher must give the same maximum as evaluating every placement");
            }
        }

        private static void ReferenceTemplateSearch()
        {
            Rectangle area = new Rectangle(2868,1917,61,58);
            Require(CombatVision.ScaleTargetArea(area,1.1,1.1) == new Rectangle(2865,1914,67,64) &&
                CombatVision.ScaleTargetArea(area,1.1,2) == new Rectangle(2865,1888,67,116),
                "normal and bear layouts use the reference scale ratios and centered rounding");
            const int width = 1600, height = 900;
            var vision = new CombatVision(CombatSettings.Defaults());
            PixelFrame first = MakePortrait("Hiyuki", width, height), next = MakePortrait("Lingyang", width, height);
            var hud = new HudSnapshot { PartySize = 2, Current = 0 };
            hud.Health[0] = 1;
            vision.ReadParty(first, hud);
            Require(hud.Fighters[0] == "Hiyuki", "an edge-of-ROI untrimmed portrait is searched, not just +/- 4 near the expected center");
            vision.ReadParty(next, hud, true);
            Require(hud.Fighters[0] == "Hiyuki", "active combat keeps the loaded character identity");
            vision.ReadParty(next, hud);
            Require(hud.Fighters[0] == "Lingyang", "out-of-combat load checks the changed frame and replaces identity");
        }

        private static PixelFrame MakePortrait(string fighter, int width, int height)
        {
            VisualPattern pattern = PatternBank.Portraits.First(p => p.Fighter == fighter);
            VisualPattern complete = pattern.Reference.At(pattern.Label, pattern.Fighter, width, height);
            byte[] rgb = (byte[])typeof(VisualPattern).GetField("rgb", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(complete);
            byte[] pixels = new byte[width * height * 4];
            Rectangle region = CombatVision.PortraitBox(new PixelFrame(pixels, width, height), 0);
            int left = region.Right - complete.Area.Width, top = region.Bottom - complete.Area.Height;
            for (int y = 0; y < complete.Area.Height; y++) for (int x = 0; x < complete.Area.Width; x++)
            {
                int s = (y * complete.Area.Width + x) * 3, d = ((top+y) * width + left+x) * 4;
                pixels[d] = rgb[s+2]; pixels[d+1] = rgb[s+1]; pixels[d+2] = rgb[s]; pixels[d+3] = 255;
            }
            return new PixelFrame(pixels,width,height);
        }

        private static void HealthRectangles()
        {
            const int width = 1600, height = 900;
            byte[] pixels = new byte[width * height * 4];
            Action<Rectangle, int, int, int> paint = (box, r, g, b) =>
            {
                for (int y = box.Top; y < box.Bottom; y++) for (int x = box.Left; x < box.Right; x++)
                {
                    int i = (y * width + x) * 4;
                    pixels[i] = (byte)b; pixels[i+1] = (byte)g; pixels[i+2] = (byte)r;
                }
            };
            paint(new Rectangle(100,720,50,4),207,75,60);
            Require(EnemyHud.HasHealth(new PixelFrame(pixels,width,height), false), "full-frame reference RGB rectangle");
            Array.Clear(pixels,0,pixels.Length);
            paint(new Rectangle(100,720,8,4),207,75,60);
            var frame = new PixelFrame(pixels,width,height);
            Require(!EnemyHud.HasHealth(frame,false) && EnemyHud.HasHealth(frame,true) && !EnemyHud.HasHealth(frame,true,true),
                "in-combat width exception but cloud aspect filter remains");
            Array.Clear(pixels,0,pixels.Length);
            paint(new Rectangle(650,30,100,6),250,60,40);
            Require(EnemyHud.HasHealth(new PixelFrame(pixels,width,height),false), "single boss rectangle in reference ROI");
            paint(new Rectangle(850,30,80,6),250,60,40);
            Require(!EnemyHud.HasHealth(new PixelFrame(pixels,width,height),false), "two boss rectangles are not unique");
            Array.Clear(pixels,0,pixels.Length);
            paint(new Rectangle(100,720,41,3),207,75,60);
            Require(EnemyHud.HasHealth(new PixelFrame(pixels,width,height),false), "reference width/height helper truncates instead of rounding the limits");
            Array.Clear(pixels,0,pixels.Length);
            paint(new Rectangle(100,720,100,5),207,75,60);
            for(int y=721; y<=723; y++) for(int x=148; x<=150; x++) if (x!=149 || y!=722)
                paint(new Rectangle(x,y,1,1),0,0,0);
            for (int x=110; x<127; x++) paint(new Rectangle(x,720,1,1),0,0,0);
            Require(EnemyHud.HasHealth(new PixelFrame(pixels,width,height),false),
                "count every matching pixel of the contour's bounding mask (including its disconnected center)");
            Array.Clear(pixels,0,pixels.Length);
            byte[] raw = new byte[1920*1080*4];
            for(int y=500;y<505;y++) for(int x=400;x<460;x++) { int i=(y*1920+x)*4; raw[i]=60;raw[i+1]=75;raw[i+2]=207; }
            var unscaled = new PixelFrame(pixels,width,height,sourceWidth:1920,sourceHeight:1080,echoPixels:raw);
            Require(EnemyHud.HasHealth(unscaled,false), "use the actual source mask, not normalized display pixels");
            Require(EnemyHud.BossHealthRegion(1600,900) == new Rectangle(529,24,527,59) &&
                EnemyHud.BossHealthRegion(2560,900) == new Rectangle(1009,24,527,59),
                "boss ROI uses independent rounded extent and centered 16:9 coordinates");
        }

        private static void ConcertoContour()
        {
            const int dim = 126;
            int[] labels = new int[dim * dim];
            for (int y = 0; y < dim; y++) for (int x = 0; x < dim; x++)
            {
                int dx = x - dim/2, dy = y - dim/2, radius = dx*dx + dy*dy;
                if (radius >= 44*44 && radius <= 53*53) labels[y*dim+x] = 1;
            }
            Require(ConcertoMeter.IsReferenceFullRing(dim, dim, labels, 1), "full ring produces a convex approximated exterior");
            for (int y = dim/2; y < dim; y++) for (int x = dim/2; x < dim; x++) labels[y*dim+x] = 0;
            Require(!ConcertoMeter.IsReferenceFullRing(dim,dim,labels,1), "open ring has a non-convex exterior");
            var meter = new ConcertoMeter();
            Require(meter.CalibrateRaw(ConcertoColor.Electro, 100, true) == 1 &&
                meter.CalibrateRaw(ConcertoColor.Electro, 85, false) == .85 &&
                meter.CalibrateRaw(ConcertoColor.Electro, 200, false) == .99 &&
                meter.CalibrateRaw(ConcertoColor.Havoc, 85, false) == 0, "raw pixel-area ratio, per-attribute baseline and 0.99 cap");
        }

        private static void PollingAndSwitch()
        {
            Require(BattleLoop.PollingDelay(false,10) == 90 && BattleLoop.PollingDelay(false,120) == 0 &&
                BattleLoop.PollingDelay(true,0) == 0 && BattleLoop.PollingDelay(true,60) == 0 &&
                BattleLoop.PollingDelay(true,60,false) == 8 && BattleLoop.PollingDelay(false,120,false) == 8,
                "100ms idle trigger, no added delay after a fresh combat frame, backoff after capture failure");
            var planner = new BattlePlanner(CombatSettings.Defaults());
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var roster = (FighterMemory[])typeof(BattlePlanner).GetField("roster",flags).GetValue(planner);
            typeof(BattlePlanner).GetField("activeSlot",flags).SetValue(planner,0);
            roster[0].Id = "Chixia"; roster[1].Id = "Camellya";
            var hud = new HudSnapshot { PartySize = 2, Current = 0, ConcertoFull = true };
            hud.Fighters[0] = "Chixia"; hud.Fighters[1] = "Camellya";
            MethodInfo next = typeof(BattlePlanner).GetMethod("NextSlot", flags);
            Require((int)next.Invoke(planner,new object[] { hud,1000L }) == -1, "explicit zero health cannot trigger unsafe switch");
            hud.HealthUnknown[1] = true;
            Require((int)next.Invoke(planner,new object[] { hud,1000L }) == 1, "confirmed party slot with missing health is still a reference candidate");
            roster[1].Id = hud.Fighters[1] = "Jinhsi";
            roster[1].Phase = 1;
            MethodInfo must = typeof(BattlePlanner).GetMethod("MustMakeRoomForTeammate", flags);
            Require((bool)must.Invoke(planner,new object[]{hud,1000L}), "known MUST teammate remains eligible with unreadable health");
            hud.HealthUnknown[1] = false;
            Require(!(bool)must.Invoke(planner,new object[]{hud,1000L}), "confirmed-dead MUST teammate remains excluded");
        }
    }
}
