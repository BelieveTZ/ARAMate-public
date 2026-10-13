using System;using System.IO;using System.Linq;using System.Text;using System.Text.RegularExpressions;using System.Web.Script.Serialization;using ConfidenceDesktop;
class PaletteExport {
 static int Main(string[] args){
  var palette=ThemePalette.Piltover.Css();string path=args[0],html=File.ReadAllText(path);
  html=Regex.Replace(html,@":root\{[^}]*\}",":root{color-scheme:dark;"+string.Join(";",palette.Select(p=>"--"+p.Key+":"+p.Value))+";--teal:#00b69f}");
  File.WriteAllText(path,html,new UTF8Encoding(true));
  File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(ThemePalette.All.ToDictionary(p=>p.Id,p=>p.Css())),new UTF8Encoding(false));return 0;
 }
}
