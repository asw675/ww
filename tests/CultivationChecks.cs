using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace TideCombat
{
    internal static class CultivationChecks
    {
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static OcrLine Text(string value, int x, int y)
        {
            return new OcrLine { Text = value, Box = new Rectangle(x, y, 90, 24), Score = .95f };
        }

        internal static void ReadRows()
        {
            var numbers = CultivationVision.ParseQuantity(" ２ ／ ３ ");
            Require(numbers != null && numbers.Item1 == 2 && numbers.Item2 == 3 &&
                CultivationVision.ParseQuantity("6/0") == null, "normalize the numbers and reject an empty goal");
            Require(CultivationVision.IsPage(new[] { Text("培养目标", 100, 100), Text("已完成共鸣者培养", 500, 170) }),
                "accept the fully cultivated character's page without material rows");
            Require(!CultivationVision.HasMaterialRows(new[] { Text("已完成共鸣者培养", 500, 170) }) &&
                CultivationVision.HasMaterialRows(new[] { Text("所需材料", 500, 170) }),
                "separate completed cultivation from an active material list");
            var echoPage = new[] { Text("培养目标", 100, 100), Text("声骸培养", 500, 170) };
            Require(CultivationVision.IsPage(echoPage) && !CultivationVision.HasMaterialRows(echoPage),
                "a selected echo-only cultivation page has no resonator material rows");
            var lines = new List<OcrLine>
            {
                Text("梦魔聚落", 640, 300), Text("直接挑战", 1400, 302),
                Text("已击败残象： 48／48", 642, 330),
                Text("产出梦魇聚落", 640, 374),
                Text("氤柔无音区", 640, 540), Text("直接挑战", 1400, 542)
            };
            var rows = CultivationVision.EchoRows(lines, new Size(1600, 900));
            Require(rows.Count == 2 && !rows[0].Tacet && rows[0].Done && rows[0].Button.Y == 302 &&
                rows[1].Tacet && !rows[1].Done && rows[1].Button.Y == 542,
                "pair each challenge button with its row and tolerate the reference nightmare OCR variant");

            foreach (string count in new[] { "0/0", "49/48" })
            {
                lines[2] = Text("已击败残象：" + count, 642, 330);
                rows = CultivationVision.EchoRows(lines, new Size(1600, 900));
                Require(rows[0].Done, "a visible nightmare row is cleared when defeated reaches or exceeds total");
            }
            lines[2] = Text("已击败残象：48/48", 642, 330);
            lines.RemoveAt(1);
            rows = CultivationVision.EchoRows(lines, new Size(1600, 900));
            Require(rows.Count == 1 && rows[0].Tacet, "ignore rows without a nearby challenge button");
            lines.Insert(1, Text("直接挑战", 1400, 302));
            lines.Insert(3, Text("已击败残象：1/1000", 640, 342));
            rows = CultivationVision.EchoRows(lines, new Size(1600, 900));
            Require(!rows[0].Done && rows[0].Defeated == 1 && rows[0].Total == 1000,
                "use the last visible count for the nightmare row");
        }

        internal static void Rewards()
        {
            var goal = new CultivationGoal { Type = CultivationVision.Skills, Name = "材料甲", Owned = 2, Required = 6, WeeklyLeft = 1 };
            var goals = new List<CultivationGoal> { goal };
            Require(CultivationVision.PendingTypes(goals).Single() == goal.Type, "unmet and available material is pending");
            foreach (int invalid in new[] { 1, 2 })
            {
                bool rejected = false;
                try { CultivationVision.UpdateInventory(goals, new Dictionary<string, int> { { goal.Name, invalid } }); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected && goal.Owned == 2 && goal.WeeklyLeft == 1, "do not count a claim without inventory progress");
            }
            bool wrongMaterial = false;
            try { CultivationVision.UpdateInventory(goals, new Dictionary<string, int> { { "材料乙", 9 } }); }
            catch (InvalidOperationException) { wrongMaterial = true; }
            Require(wrongMaterial, "do not count another material in the reward slot");
            CultivationVision.ApplyClaim(goals, goal.Type, new Dictionary<string, int> { { goal.Name, 4 } }, 121, 62, 60);
            Require(goal.Owned == 4 && goal.WeeklyLeft == 0 && !CultivationVision.PendingTypes(goals).Any(),
                "exhausting weekly claims stops the row");
            goals.Add(new CultivationGoal { Type = goal.Type, Name = "乙", Required = 3, WeeklyLeft = 1 });
            Require(!CultivationVision.PendingTypes(goals).Any(), "the lowest weekly count applies to the whole row");
            goal.WeeklyLeft = null;
            goals[1].WeeklyLeft = null;
            Require(!CultivationVision.PendingTypes(goals).Any(), "unread weekly counts cannot start a skill run");
            goal.Type = CultivationVision.Ascension;
            goal.WeeklyLeft = 0;
            Require(CultivationVision.PendingTypes(goals).Single() == goal.Type, "ascension materials have no weekly cap");
        }

        internal static void PartialInventory()
        {
            var a = new CultivationGoal { Type = CultivationVision.Skills, Name = "甲", Owned = 5, Required = 28, WeeklyLeft = 2 };
            var b = new CultivationGoal { Type = CultivationVision.Skills, Name = "乙", Owned = 2, Required = 10, WeeklyLeft = 2 };
            var c = new CultivationGoal { Type = CultivationVision.Ascension, Name = "丙", Owned = 6, Required = 6 };
            var goals = new List<CultivationGoal> { a, b, c };
            foreach (var invalid in new[] {
                new Dictionary<string,int> { { "乙", 3 }, { "甲", 4 } },
                new Dictionary<string,int> { { "甲", 5 }, { "乙", 2 } },
                new Dictionary<string,int> { { "丙", 7 } },
                new Dictionary<string,int> { { "甲", 8 }, { "丁", 1 } } })
            {
                bool rejected = false;
                try { CultivationVision.UpdateInventory(goals, invalid); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected && a.Owned == 5 && b.Owned == 2 && c.Owned == 6,
                    "无效回执不应使部分库存提前改变");
            }
            var inventory = new Dictionary<string,int> { { "甲", 8 } };
            bool tooHigh = false;
            try { CultivationVision.ApplyClaim(goals, a.Type, inventory, 121, 63, 60); }
            catch (InvalidOperationException) { tooHigh = true; }
            Require(tooHigh && a.WeeklyLeft == 2 && b.WeeklyLeft == 2 && a.Owned == 5, "剩余波片过高时保留领取前状态");
            CultivationVision.ApplyClaim(goals, a.Type, inventory, 121, 60, 60);
            Require(a.Owned == 8 && b.Owned == 2 && a.WeeklyLeft == 1 && b.WeeklyLeft == 1 && !c.WeeklyLeft.HasValue,
                "回执只更新读到的材料，同一行共享剩余周次数");
        }
    }
}
