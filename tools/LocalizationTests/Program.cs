using Runic.Localization;
using System.Text.Json;

class Program
{
    static int checks;
    static void Check(bool result, string name) { if(!result) throw new Exception(name); checks++; Console.WriteLine("PASS "+name); }
    static void Main()
    {
        string english=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"../../..","English.json"));
        var files=new Dictionary<string,string> {
            ["German"]="{\"hello\":\"Hallo\",\"amount\":\"Zu {1}: {0} Gegenstände.\"}",
            ["French"]="{\"hello\":\"Bonjour\",\"amount\":\"Invalid {9}\"}",
            ["Spanish"]="{broken", ["Swedish"]="null",
            ["Japanese"]="{\"hello\":\"こんにちは\",\"multiline\":\"一行目\\n二行目\"}"
        };
        int reads=0;
        var catalog=new LanguageCatalog(english,l=> {reads++;return files.GetValueOrDefault(l);});
        catalog.Select("German");Check(catalog.Get("hello")=="Hallo","selected language");
        Check(catalog.Get("multiline")=="First\nSecond","missing key falls back to English");
        Check(catalog.Format("amount",3,"Chest")=="Zu Chest: 3 Gegenstände.","translation can reorder parameters");
        int before=reads;for(int i=0;i<1000;i++){catalog.Select("German");catalog.Get("hello");}
        Check(reads==before,"no file reads on repeated UI lookups");
        catalog.Select("Norwegian");Check(catalog.Get("hello")=="Hello","missing language file");
        catalog.Select("Spanish");Check(catalog.Get("hello")=="Hello","malformed language file");
        catalog.Select("Swedish");Check(catalog.Get("hello")=="Hello","null language document");
        catalog.Select("French");Check(catalog.Format("amount",3,"Chest")=="Moved 3 items to Chest.","invalid placeholder falls back");
        files["French"]="{\"amount\":\"Lost the parameters\",\"hello\":\"\"}";
        catalog.Select("English");catalog.Select("French");
        Check(catalog.Format("amount",3,"Chest")=="Moved 3 items to Chest.","missing placeholders fall back");
        Check(catalog.Get("hello")=="Hello","empty translation falls back");
        catalog.Select("Japanese");Check(catalog.Get("hello")=="こんにちは"&&catalog.Get("multiline")=="一行目\n二行目","Unicode and newlines");
        Check(catalog.Format("literal",7)=="Keep {braces} 7.","literal braces preserved");
        before=reads;catalog.Select("../../escape");Check(reads==before&&catalog.Get("hello")=="Hello","language cannot escape translation directory");
        catalog.Select(null);Check(catalog.Get("hello")=="Hello","no selected language defaults to English");
        var second=new LanguageCatalog("{\"hello\":\"Other mod\"}",l=>null);
        second.Select("German");Check(second.Get("hello")=="Other mod","per-mod English dictionaries remain independent");

        string directory=Path.Combine(AppContext.BaseDirectory,"Translations","LocalizationTests");Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"German.json"),files["German"]);
        Localization.instance=new Localization {Language="German"};
        Check(RunicText.Get("hello")=="Hallo","runtime follows Valheim selected language");
        Localization.instance.Language="Absent";Check(RunicText.Get("hello")=="Hello","embedded English survives missing external English file");
        Localization.instance=null;Check(RunicText.Get("hello")=="Hello","server/early startup English fallback");

        var picker=new PickerFixture();picker.Map.Zoom=.21f;picker.Map.Center=42;
        picker.Complete(3);Check(picker.Map.Zoom==.21f&&picker.Map.Center==42&&picker.Map.Pins==3,"delayed portal results add pins without recentering or zooming");
        var empty=new PickerFixture();empty.Complete(0);Check(empty.Cancelled&&empty.Map.Pins==0,"empty portal directory retains cancellation behavior");
        Console.WriteLine($"{checks}/{checks} checks passed.");
    }
}
public class Localization
{
    public static Localization instance;
    public string Language;
    public string GetSelectedLanguage()=>Language;
}
partial class PickerFixture
{
    public sealed class Pin { public bool m_doubleSize,m_animate; }
    public sealed class FakeMap
    {
        public float Zoom;public int Center,Pins;
        public Pin AddPin(int position,Minimap.PinType type,string label,bool save,bool check){Pins++;return new Pin();}
    }
    class PickerCandidate {public int Position;public string Label="Destination";public Pin Pin;}
    class PickerSession {public List<PickerCandidate> Candidates=new();public FakeMap Map=new();public object Player;public string NetworkId="network";}
    PickerSession _mapPicker=new();
    public FakeMap Map=>_mapPicker.Map;
    public bool Cancelled;
    void CancelMapPicker(string reason,bool restore){Cancelled=true;}
    static void CaptureAndEnablePickerIcon(PickerSession session){}
    static void Message(object player,string text){}
    public void Complete(int count){var candidates=new List<PickerCandidate>();for(int i=0;i<count;i++)candidates.Add(new PickerCandidate());CompletePicker(candidates,false);}
}
static class Minimap {public enum PinType{Icon3}}
static class PortalMapMarkerSprite {public static void Apply(PickerFixture.Pin pin){}}
