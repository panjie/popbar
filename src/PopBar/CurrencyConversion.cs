using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PopBar;

internal sealed record MoneyAmount(decimal Amount,string Currency,string Original,bool Assumed);
internal static class MoneyParser
{
    internal static readonly string[] Currencies={"CNY","JPY","USD","EUR","GBP","HKD","TWD","KRW","AUD","CAD","CHF","SGD"};
    internal static string Normalize(string? currency)=>Currencies.Contains(currency)?currency!:"CNY";
    internal static string NormalizeText(string text)=>text.Normalize(System.Text.NormalizationForm.FormKC).Replace('−','-');
    internal static bool IsAmount(string text)
    {
        var amounts=Parse(text,"CNY");
        return amounts.Count==1 && amounts[0].Original==NormalizeText(text);
    }
    const string unit=@"(?:人民币|人民幣|日元|日圆|日圓|美元|美金|欧元|歐元|英镑|英鎊|港币|港幣|港元|台币|台幣|台元|韩元|韓元|澳元|加元|瑞郎|新加坡元|CNY|RMB|JPY|USD|EUR|GBP|HKD|TWD|KRW|AUD|CAD|CHF|SGD|US\$|HK\$|NT\$|[$¥￥€£]|元|円)";
    const string digits=@"(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)(?:\.[0-9]+)?";
    const string magnitude=@"(?:[十百千]?[万萬亿億兆]|[十百千kKmM])";
    const string number=@"[+-]?"+digits+@"(?:\s*"+magnitude+@"(?:\s*"+digits+@"\s*"+magnitude+@")*(?:\s*"+digits+@")?)?";
    static readonly Regex parts=new(@"(?<number>"+digits+@")\s*(?<scale>"+magnitude+@")?",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    static readonly Regex money=new(@"(?<![A-Za-z0-9_.,])(?:(?<unit>"+unit+@")\s*(?<amount>"+number+@")|(?<amount>"+number+@")\s*(?<unit>"+unit+@")|(?:价格|價格|售价|售價|金额|金額|费用|費用|price|cost)\s*[:：]?\s*(?<amount>"+number+@")(?:\s*(?<unit>"+unit+@"))?)(?![A-Za-z0-9_]|[.,][0-9])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    internal static IReadOnlyList<MoneyAmount> Parse(string text,string? assumedCurrency)
    {
        var result=new List<MoneyAmount>();
        if(text.Length>65536) return result;
        text=NormalizeText(text);
        foreach(Match match in money.Matches(text)) {
            if(!TryAmount(match.Groups["amount"].Value,out var value)) continue;
            var token=match.Groups["unit"].Value.ToUpperInvariant();
            var currency=token switch {
                "人民币" or "人民幣" or "RMB" or "元"=>"CNY",
                "日元" or "日圆" or "日圓" or "円"=>"JPY",
                "美元" or "美金" or "$" or "US$"=>"USD",
                "欧元" or "歐元" or "€"=>"EUR","英镑" or "英鎊" or "£"=>"GBP",
                "港币" or "港幣" or "港元" or "HK$"=>"HKD","台币" or "台幣" or "台元" or "NT$"=>"TWD",
                "韩元" or "韓元"=>"KRW","澳元"=>"AUD","加元"=>"CAD","瑞郎"=>"CHF","新加坡元"=>"SGD",
                "" or "¥" or "￥"=>Normalize(assumedCurrency),_=>token
            };
            result.Add(new(value,currency,match.Value,token is "" or "¥" or "￥"));
            if(result.Count==20) break;
        }
        return result;
    }
    static bool TryAmount(string text,out decimal value)
    {
        value=0;
        var negative=text.StartsWith('-');
        decimal previousScale=decimal.MaxValue;
        try {
            foreach(Match part in parts.Matches(text.TrimStart('+','-'))) {
                if(!decimal.TryParse(part.Groups["number"].Value,NumberStyles.AllowThousands|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var coefficient)) return false;
                var suffix=part.Groups["scale"].Value;
                decimal scale=suffix.Length==0?1:suffix[^1] switch {
                    '万' or '萬'=>10000,'亿' or '億'=>100000000,'兆'=>1000000000000,
                    '千' or 'k' or 'K'=>1000,'百'=>100,'十'=>10,'m' or 'M'=>1000000,_=>1
                };
                if(suffix.Length==2) scale*=suffix[0] switch { '十'=>10,'百'=>100,'千'=>1000,_=>1 };
                // Compound amounts, e.g. 1億2000万500円, must use descending magnitudes.
                if(scale>=previousScale) return false;
                previousScale=scale; value+=coefficient*scale;
            }
            if(negative) value=-value;
            return true;
        } catch(OverflowException) { return false; }
    }
}

internal sealed record CurrencyRate(string Base,string Quote,decimal Rate,DateOnly Date);
internal static class CurrencyConversion
{
    static readonly HttpClient client=new(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(15),MaxResponseContentBufferSize=65536 };
    static readonly Dictionary<string,(DateTimeOffset Fetched,CurrencyRate Rate)> cache=new();
    internal static async Task<CurrencyRate> Fetch(string from,string to,CancellationToken token,HttpClient? transport=null)
    {
        if(!MoneyParser.Currencies.Contains(from)||!MoneyParser.Currencies.Contains(to)) throw new FormatException(T("SmartInvalidInput"));
        if(from==to) return new(from,to,1,DateOnly.FromDateTime(DateTime.UtcNow));
        var key=from+"/"+to;
        if(transport==null) lock(cache) {
            if(cache.TryGetValue(key,out var saved)&&DateTimeOffset.UtcNow-saved.Fetched<TimeSpan.FromHours(1)) return saved.Rate;
        }
        using var response=await (transport??client).GetAsync("https://api.frankfurter.dev/v2/rate/"+key.ToLowerInvariant(),token);
        response.EnsureSuccessStatusCode();
        var json=await response.Content.ReadAsStringAsync(token);
        if(json.Length>65536) throw new InvalidDataException(T("SmartRateFailed"));
        using var document=JsonDocument.Parse(json);
        var root=document.RootElement;
        if(!string.Equals(root.GetProperty("base").GetString(),from,StringComparison.OrdinalIgnoreCase)
            || !string.Equals(root.GetProperty("quote").GetString(),to,StringComparison.OrdinalIgnoreCase)
            || !root.GetProperty("rate").TryGetDecimal(out var rate)||rate<=0
            || !DateOnly.TryParseExact(root.GetProperty("date").GetString(),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)
            || date>DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))) throw new InvalidDataException(T("SmartRateFailed"));
        var result=new CurrencyRate(from,to,rate,date);
        if(transport==null) lock(cache) cache[key]=(DateTimeOffset.UtcNow,result);
        return result;
    }
    internal static async Task<string> Convert(IReadOnlyList<MoneyAmount> amounts,string target,CancellationToken token,
        Func<string,string,CancellationToken,Task<CurrencyRate>>? fetch=null)
    {
        fetch??=((from,to,cancellation)=>Fetch(from,to,cancellation));
        var rates=new Dictionary<string,CurrencyRate>();
        foreach(var source in amounts.Select(x=>x.Currency).Distinct()) {
            token.ThrowIfCancellationRequested();
            rates[source]=source==target?new(source,target,1,DateOnly.FromDateTime(DateTime.UtcNow)):await fetch(source,target,token);
        }
        var lines=new List<string>();
        foreach(var amount in amounts) {
            var rate=rates[amount.Currency];
            var converted=decimal.Round(amount.Amount*rate.Rate,target=="JPY"?0:2,MidpointRounding.AwayFromZero);
            lines.Add(amount.Amount.ToString("G29",CultureInfo.InvariantCulture)+" "+amount.Currency+" ≈ "+converted.ToString(target=="JPY"?"N0":"N2",CultureInfo.InvariantCulture)+" "+target);
            if(amount.Assumed) lines.Add(T("SmartAssumedCurrency",amount.Original,amount.Currency));
        }
        foreach(var rate in rates.Values.Where(rate=>rate.Base!=rate.Quote))
            lines.Add($"1 {rate.Base} = {rate.Rate.ToString("G29",CultureInfo.InvariantCulture)} {rate.Quote} · {rate.Date:yyyy-MM-dd}");
        if(rates.Values.Any(rate=>rate.Base!=rate.Quote)) lines.Add(T("SmartRateSource"));
        if(amounts.Count==20) lines.Add(T("SmartMoneyLimit"));
        return string.Join("\n",lines);
    }
}
