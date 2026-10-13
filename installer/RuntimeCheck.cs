using System;
using Windows.Globalization;
using Windows.Media.Ocr;
static class RuntimeCheck {
 [STAThread] static int Main() {
  try {return OcrEngine.TryCreateFromLanguage(new Language("zh-Hans"))==null?2:0;}
  catch {return 3;}
 }
}
