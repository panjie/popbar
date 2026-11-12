using PopBar;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

internal static class SmartChecks
{
    internal static async Task<int> Run()
    {
        int count=0;
        void Check(bool value,string name) { if(!value) throw new Exception(name); Console.WriteLine("PASS "+name); count++; }
        async Task Fails(Func<Task> action,string name) { try { await action(); } catch { Check(true,name); return; } throw new Exception(name); }
        Check(SmartTransforms.Calculate("128 × 0.85")=="108.8","calculator decimal accuracy");
        Check(UnitConversion.To(32,"°F","°C")==0 && UnitConversion.To(100,"°C","°F")==212,"temperature freezing and boiling conversions");
        Check(UnitConversion.To(-40,"°C","°F")==-40 && UnitConversion.To(0,"K","°C")==-273.15m,"negative temperature and absolute zero");
        Check(UnitConversion.To(-459.67m,"°F","K")==0,"Fahrenheit absolute zero remains exact");
        Check(UnitConversion.To(1,"in","cm")==2.54m&&UnitConversion.To(1,"mi","km")==1.609344m,"exact length conversion factors");
        Check(UnitConversion.To(1,"lb","kg")==0.45359237m&&UnitConversion.To(1,"US gal","L")==3.785411784m,"mass and explicitly US gallon conversions");
        Check(UnitConversion.Parse("气温 −40℃，长度 1,000 cm，重量 2.5公斤").Select(x=>x.Unit).SequenceEqual(new[]{"°C","cm","kg"}),"embedded quantities Chinese units and Unicode minus");
        Check(UnitConversion.Parse("abc25kg 20ms 10m² 10m^2 30m/s 100").Count==0,"unit suffix boundaries reject unrelated and compound units");
        Check(Actions.Link("https://example.com/2.5kg")!=null,"URLs containing a quantity remain normal links");
        Check(UnitConversion.Convert("32℉").Contains("= 0 °C")&&UnitConversion.Convert("1000mL").Contains("= 1 L"),"unit preview includes target quantities");
        await Fails(()=>Task.FromResult(UnitConversion.Convert("-274°C")),"temperatures below absolute zero fail clearly");
        await Fails(()=>Task.FromResult(UnitConversion.To(1,"kg","m")),"incompatible dimensions rejected");
        Check(UnitConversion.Parse(string.Join(" ",Enumerable.Repeat("1kg",30))).Count==20,"unit extraction bounded");
        Check(SmartActions.DetectAll("98.6°F",new Settings()).Any(x=>x.Kind==SmartActionKind.Unit)&&SmartActions.DetectAll("2.5kg",new Settings()).Any(x=>x.Kind==SmartActionKind.Unit),"decimal quantities recognized as units rather than domains");
        Check(SmartTransforms.Calculate("-(2+3)*4 + 2^3^2")=="492","calculator precedence parentheses and right-associative powers");
        Check(SmartTransforms.Calculate("0.1+0.2")=="0.3" && SmartTransforms.Calculate("200 * 15%")=="30","decimal fractions and percentages");
        Check(SmartTransforms.Calculate("-2^2")=="-4" && SmartTransforms.Calculate("2^-3")=="0.125","unary minus and negative exponents");
        await Fails(()=>Task.FromResult(SmartTransforms.Calculate("1/0")),"division by zero rejected");
        await Fails(()=>Task.FromResult(SmartTransforms.Calculate("2^101")),"oversized powers rejected");
        await Fails(()=>Task.FromResult(SmartTransforms.Calculate("(1+2")),"unbalanced expressions rejected");
        await Fails(()=>Task.FromResult(SmartTransforms.Calculate(new string('(',80)+"1"+new string(')',80))),"calculator nesting bounded");
        Check(!SmartTransforms.IsCalculation("2026-11-12")&&!SmartTransforms.IsCalculation("some words")&&!SmartTransforms.IsCalculation("Process.Start(1)"),"dates and code are not mathematical expressions");
        const string json="{\"name\":\"你好\",\"items\":[1,2],\"text\":\"<script>\"}";
        Check(SmartTransforms.Json(json).Contains('\n')&&SmartTransforms.Json(json).Contains("你好"),"JSON formatted with readable Unicode");
        Check(SmartTransforms.Json(SmartTransforms.Json(json),true)==json,"JSON formatting preserves values and compact roundtrip");
        Check(SmartTransforms.IsJson("```json\n"+json+"\n```")&&!SmartTransforms.IsJson("{bad}"),"fenced JSON accepted and invalid JSON excluded");
        var amounts=MoneyParser.Parse("售价 $1,234.50，另收 2万日元；价格 100；€20","CNY");
        Check(amounts.Count==4&&amounts[0].Amount==1234.5m&&amounts[0].Currency=="USD"&&amounts[1].Amount==20000&&amounts[1].Currency=="JPY","currency symbols grouping and Chinese multipliers");
        Check(amounts[2].Assumed&&amounts[2].Currency=="CNY"&&amounts[3].Currency=="EUR","money word fallback and explicit currency");
        Check(MoneyParser.Parse("¥100","JPY").Single() is { Currency:"JPY",Assumed:true },"ambiguous yen symbol follows configured source");
        Check(MoneyParser.Parse("price 100 USD","JPY").Single() is { Currency:"USD",Assumed:false },"explicit currency overrides money word default");
        Check(MoneyParser.Parse("100元 HK$20 NT$30 50円","USD").Select(x=>x.Currency).SequenceEqual(new[]{"CNY","HKD","TWD","JPY"}),"East Asian currency units");
        Check(MoneyParser.Parse("hello 100 1,00 USD USDabc100", "CNY").Count==0,"plain numbers and malformed amounts excluded");
        Check(MoneyParser.Parse(string.Join(" ",Enumerable.Repeat("$1",50)),"CNY").Count==20,"multiple money amounts bounded");
        var defaults=JsonSerializer.Deserialize<Settings>("{}")!;
        Check(defaults.CurrencyConversion&&defaults.CurrencyTarget=="CNY"&&defaults.CurrencySource=="CNY","currency defaults enabled with CNY target");
        var saved=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { CurrencyConversion=false,CurrencyTarget="JPY",CurrencySource="USD" }))!;
        Check(!saved.CurrencyConversion&&saved.CurrencyTarget=="JPY"&&saved.CurrencySource=="USD","currency preferences persist");
        Check(!SmartActions.DetectAll("$100",saved).Any(x=>x.Kind==SmartActionKind.Currency)&&SmartActions.DetectAll("$100",defaults).Any(x=>x.Kind==SmartActionKind.Currency),"currency button honors toggle");
        Check(SmartActions.DetectAll("https://example.com",defaults).Count==0&&SmartActions.DetectAll(@"C:\work\file.txt",defaults).Count==0,"smart transforms still exclude URLs and paths");
        Check(SmartActions.DetectAll("(1+2)*3",defaults).Any(x=>x.Kind==SmartActionKind.Calculate)&&SmartActions.DetectAll(json,defaults).Any(x=>x.Kind==SmartActionKind.Json),"transform buttons selected by content");
        Check(SmartActions.DetectAll("1700000000",defaults).Count==0&&SmartActions.DetectAll("5L2g5aW9",defaults).Count==0&&SmartActions.DetectAll("hello world",defaults).Count==0,"removed timestamp and encoding features leave no buttons");
        Check(SmartActions.DetectAll("1/2",defaults).Any(x=>x.Kind==SmartActionKind.Calculate),"division is not mistaken for a numeric IPv4 URL");
        Check(MoneyParser.Parse("500百万円","CNY").Single() is { Amount:500000000m,Currency:"JPY",Assumed:false },"Japanese million-yen notation");
        Check(MoneyParser.Parse("売上高は５００百万円です。","CNY").Single() is { Amount:500000000m,Currency:"JPY" },"full-width Japanese financial sentence");
        Check(MoneyParser.Parse("3千万円 2.5億円 1兆円","CNY").Select(x=>x.Amount).SequenceEqual(new[]{30000000m,250000000m,1000000000000m}),"Japanese thousand-ten-thousand hundred-million and trillion magnitudes");
        Check(MoneyParser.Parse("1億2000万500円","CNY").Single().Amount==120000500m,"compound Japanese amount is parsed as one amount");
        Check(MoneyParser.Parse("-500百万円","CNY").Single().Amount==-500000000m,"negative scaled money amount");
        Check(MoneyParser.Parse("1万2億円","CNY").Count==0,"invalid magnitude order is not partially converted");
        Check(SmartActions.DetectAll("500百万円",defaults).Single().Kind==SmartActionKind.Currency,"Japanese financial notation shows the currency action only");
        Check(SmartActions.DetectAll("2.5億円",defaults).Single().Kind==SmartActionKind.Currency&&Actions.Link("https://example.com/2.5USD")!=null,"decimal currency amounts are not domains and explicit URLs remain links");
        Check(Actions.Link("127.0.0.1")!=null&&Actions.Link("https://example.com")!=null,"normal IPv4 and URL actions retained");
        const string aiSelection="売上は五億円です。";
        var aiSettings=new Settings { Model="test-model" };
        Check(SmartActions.DetectAll(aiSelection,aiSettings).Count==0
            && SmartActions.DetectAll("500百万円",aiSettings).Single().Kind==SmartActionKind.Currency
            && SmartActions.DetectAll(aiSelection,defaults).Count==0,"local toolbar has no standalone AI action");
        var missing=AiSmartAnalysis.Supported.ToHashSet();
        const string aiJson="""{"money":[{"amount":500000000,"currency":"JPY","source":"五億円"}]}""";
        using var model=new MoneyModelHandler(aiSelection,aiJson);
        var suggested=await AiSmartAnalysis.Analyze(aiSettings,aiSelection,missing,default,model);
        var extracted=suggested.Single().Amounts!;
        Check(extracted.Single() is { Amount:500000000m,Currency:"JPY",Original:"五億円" },"AI stream extracts an amount anchored to the selection");
        var aiConverted=await CurrencyConversion.Convert(extracted,"CNY",default,(from,to,_)=> {
            Check(from=="JPY"&&to=="CNY","rate lookup uses the extracted currency pair");
            return Task.FromResult(new CurrencyRate(from,to,0.05m,new DateOnly(2026,1,2)));
        });
        Check(aiConverted.Contains("25,000,000.00 CNY"),"AI-extracted amount is converted with the fetched rate");
        Check(AiSmartAnalysis.Parse("{}",aiSelection,missing).Count==0,"AI may return no supported action");
        Check(AiSmartAnalysis.Parse(aiJson.Replace("五億円","別の金額"),aiSelection,missing).Count==0,"unanchored AI result rejected");
        Check(AiSmartAnalysis.Parse(aiJson.Replace("JPY","BTC"),aiSelection,missing).Count==0,"unsupported AI currency rejected");
        Check(AiSmartAnalysis.Parse(aiJson.Replace("\"amount\":","\"rate\":0.05,\"amount\":"),aiSelection,missing).Count==0,"model-supplied rates rejected");
        const string moreSuggestions="""{"unit":{"source":"華氏三十二度","value":"32 °F"},"calculation":{"source":"二十三乘四","value":"23*4"},"json":"{\"a\":1}","command":"whoami"}""";
        var extra=AiSmartAnalysis.Parse(moreSuggestions,"華氏三十二度；二十三乘四；設定 {\"a\":1}",missing);
        Check(extra.Select(action=>action.Kind).ToHashSet().SetEquals(new[]{SmartActionKind.Unit,SmartActionKind.Calculate,SmartActionKind.Json}),"AI can discover units, arithmetic and original JSON without suggesting commands");
        Check(AiSmartAnalysis.Parse(moreSuggestions,"華氏三十二度；二十三乘四；設定 {\"a\":1}",new HashSet<SmartActionKind>{SmartActionKind.Unit}).Single().Kind==SmartActionKind.Unit,"only missing actions are accepted");
        using var transport=new HttpClient(new RateHandler("{\"base\":\"USD\",\"quote\":\"CNY\",\"rate\":7.25,\"date\":\"2026-01-02\"}"));
        var rate=await CurrencyConversion.Fetch("USD","CNY",default,transport);
        Check(rate.Rate==7.25m&&rate.Date==new DateOnly(2026,1,2),"reference API rate and date validated");
        int calls=0;
        var converted=await CurrencyConversion.Convert(MoneyParser.Parse("$100 $2","CNY"),"CNY",default,(from,to,token)=> { calls++; return Task.FromResult(rate); });
        Check(calls==1&&converted.Contains("725.00 CNY")&&converted.Contains("2026-01-02")&&converted.Contains("Frankfurter"),"currency conversion reuses rates and shows source date");
        Check((await CurrencyConversion.Convert(MoneyParser.Parse("100元","CNY"),"CNY",default,(_,_,_)=>throw new Exception())).Contains("100.00 CNY"),"same currency works without network");
        using var bad=new HttpClient(new RateHandler("{\"base\":\"EUR\",\"quote\":\"CNY\",\"rate\":7,\"date\":\"2026-01-02\"}"));
        await Fails(()=>CurrencyConversion.Fetch("USD","CNY",default,bad),"mismatched API currency rejected");
        using var unavailable=new HttpClient(new RateHandler("{}",HttpStatusCode.ServiceUnavailable));
        await Fails(()=>CurrencyConversion.Fetch("USD","CNY",default,unavailable),"rate service failure propagated");
        using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
        await Fails(()=>CurrencyConversion.Convert(amounts,"USD",cancellation.Token),"conversion cancellation honored");
        return count;
    }
    sealed class MoneyModelHandler(string selection,string response):HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            using var payload=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var messages=payload.RootElement.GetProperty("messages");
            if(messages.GetArrayLength()!=2 || messages[0].GetProperty("role").GetString()!="system" ||
               messages[1].GetProperty("role").GetString()!="user" || messages[1].GetProperty("content").GetString()!=selection)
                throw new Exception("Unexpected AI money request");
            var delta=JsonSerializer.Serialize(new { choices=new[]{new { delta=new { content=response } }} });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent("data: "+delta+"\n\ndata: [DONE]\n\n",Encoding.UTF8,"text/event-stream") };
        }
    }
    sealed class RateHandler(string response,HttpStatusCode status=HttpStatusCode.OK):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.RequestUri!.AbsoluteUri!="https://api.frankfurter.dev/v2/rate/usd/cny") throw new Exception("Unexpected URL / leaked source text");
            return Task.FromResult(new HttpResponseMessage(status) { Content=new StringContent(response,Encoding.UTF8,"application/json") });
        }
    }
}
