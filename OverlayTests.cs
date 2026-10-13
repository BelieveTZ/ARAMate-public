using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace ConfidenceDesktop {
    public static class OverlayTests {
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        public static async Task<string> RunSelection(string input,string report){
            var store=new DataStore();store.Load(DataStore.DefaultRoot);var ocr=new LocalOcr();
            using(var bitmap=new Bitmap(input)){
                var lines=await ocr.Read(bitmap);var matched=SelectionMatcher.Match(lines,store,201,"high",bitmap.Width,bitmap.Height);
                Assert(matched!=null,"Braum label must be recognized in selection screenshot");
                Assert(matched.Row.Rank==146&&matched.Row.OldRank==145,"Braum must show full board rank 146 and original 145");
                var baseline=store.Groups(201,"high").Single(g=>g.Path=="champions/champion/stats").Rows.Single();
                Assert(baseline.Rank==matched.Row.Rank&&baseline.OldRank==matched.Row.OldRank,"Table and selection badge must use identical ranks");
                Assert(Math.Abs(baseline.Lower-matched.Row.Lower)<1e-12,"Table and badge lower bounds must match");
                Assert(SelectionMatcher.Match(lines,store,31,"high",bitmap.Width,bitmap.Height)==null,"Wrong hero must not place a badge");
                Assert(SelectionMatcher.Match(new List<TextLine>(),store,201,"high",bitmap.Width,bitmap.Height)==null,"Missing label must hide badge");
                using(var preview=new Bitmap(bitmap)){using(var g=Graphics.FromImage(preview))BadgePaint.Draw(g,matched.Bounds,matched);preview.Save(Path.ChangeExtension(report,".png"),ImageFormat.Png);}
                return "PASS: selection screenshot → Braum label → full hero board\r\nNew rank="+matched.Row.Rank+"; original rank="+matched.Row.OldRank+"; lower="+Statistics.Pct(matched.Row.Lower)+"; n="+matched.Row.Count+"\r\nPASS: single hero table parity; wrong hero rejection; missing label rejection";
            }
        }
        public static async Task<string> Run(string input,string report){
            var store=new DataStore();store.Load(DataStore.DefaultRoot);var ocr=new LocalOcr();var notes=new StringBuilder();
            using(var image=new Bitmap(input)){
                var lines=await ocr.Read(image);var reading=CardMatcher.Find(lines,store.Resources,image.Width,image.Height);
                Assert(reading.Cards.Count==3,"Expected three candidate titles");
                Assert(string.Join("|",reading.Cards.Select(c=>c.Name))=="无限循环往复|和我一起困在这里|海牛阿福的勇士","Candidate order/names differ");
                int stage=CardMatcher.InferStage(reading,store.ReadHero(31,"high"));Assert(stage==1,"Must identify stage 1 from at least two official rates");
                var results=CardMatcher.Rank(reading,store,31,"high",stage,"已核对阶段");
                Assert(results.Select(r=>r.Row.Rank).SequenceEqual(new[]{13,69,52}),"Expected new ranks 13,69,52");
                Assert(Math.Abs(results[0].Row.Lower-.45560349317510085)<1e-12,"Expected lower bound 45.560349%");
                using(var preview=new Bitmap(image)){using(var g=Graphics.FromImage(preview))foreach(var result in results)BadgePaint.Draw(g,result.Bounds,result);preview.Save(Path.ChangeExtension(report,".png"),ImageFormat.Png);}
                notes.AppendLine("PASS: screenshot → local Chinese OCR → three candidates → stage 1 → ranks 13/69/52 → overlay rendering");
                foreach(var r in results)notes.AppendLine(r.Card.Name+" | #"+r.Row.Rank+" | "+Statistics.Pct(r.Row.Lower)+" | n="+r.Row.Count);
                foreach(var c in reading.Cards)c.ObservedWin=double.NaN;
                Assert(CardMatcher.InferStage(reading,store.ReadHero(31,"high"))==-1,"No rates must not guess stage");
                var fewer=new List<TextLine>{lines.First(l=>CardMatcher.Normalize(l.Text)=="无限循环往复")};
                Assert(CardMatcher.Find(fewer,store.Resources,image.Width,image.Height).Cards.Count==0,"Single title must not trigger overlay");
                Assert(CardMatcher.Find(new List<TextLine>(),store.Resources,image.Width,image.Height).Cards.Count==0,"No candidates must hide overlay");
                using(var scaled=new Bitmap(image,new Size(image.Width*3/4,image.Height*3/4))){var small=CardMatcher.Find(await ocr.Read(scaled),store.Resources,scaled.Width,scaled.Height);Assert(small.Cards.Count==3,"Three titles at 75% resolution");}
                notes.AppendLine("PASS: missing stage evidence, one/no candidate rejection, 75% resolution OCR");
            }
            return notes.ToString();
        }
    }
}
