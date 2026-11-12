using System.Globalization;
using System.Text.RegularExpressions;

namespace PopBar;

internal sealed record UnitAmount(decimal Value,string Unit);
internal static class UnitConversion
{
    sealed record Unit(string Symbol,string Dimension,decimal Factor,string[] Aliases);
    static readonly Unit[] units={
        new("°C","temperature",1,new[]{"°C","℃","摄氏度","攝氏度"}),
        new("°F","temperature",1,new[]{"°F","℉","华氏度","華氏度"}),
        new("K","temperature",1,new[]{"K","kelvin","开尔文","開爾文"}),
        new("mm","length",0.001m,new[]{"mm","毫米"}),
        new("cm","length",0.01m,new[]{"cm","厘米","公分"}),
        new("m","length",1,new[]{"m","米","公尺"}),
        new("km","length",1000,new[]{"km","千米","公里"}),
        new("in","length",0.0254m,new[]{"in","inch","inches","英寸","英吋"}),
        new("ft","length",0.3048m,new[]{"ft","foot","feet","英尺"}),
        new("yd","length",0.9144m,new[]{"yd","yard","yards","码","碼"}),
        new("mi","length",1609.344m,new[]{"mi","mile","miles","英里"}),
        new("mg","mass",0.000001m,new[]{"mg","毫克"}),
        new("g","mass",0.001m,new[]{"g","克"}),
        new("kg","mass",1,new[]{"kg","千克","公斤"}),
        new("lb","mass",0.45359237m,new[]{"lb","lbs","pound","pounds","磅"}),
        new("oz","mass",0.028349523125m,new[]{"oz","ounce","ounces","盎司"}),
        new("mL","volume",0.001m,new[]{"ml","毫升"}),
        new("L","volume",1,new[]{"l","liter","liters","litre","litres","升","公升"}),
        new("US gal","volume",3.785411784m,new[]{"US gal","美制加仑","美制加侖"})
    };
    static readonly Regex pattern=new(@"(?<![A-Za-z0-9_.,$¥￥])(?<value>[+-]?(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)(?:\.[0-9]+)?)\s*(?<unit>"+
        string.Join("|",units.SelectMany(unit=>unit.Aliases).OrderByDescending(alias=>alias.Length).Select(Regex.Escape))+@")(?=$|[^A-Za-z0-9_²³/^])",
        RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    internal static bool IsQuantity(string text)
    {
        var match=pattern.Match(text);
        return match.Success && match.Index==0 && match.Length==text.Length;
    }
    internal static IReadOnlyList<UnitAmount> Parse(string text)
    {
        var result=new List<UnitAmount>();
        if(text.Length>65536) return result;
        text=text.Replace('−','-');
        foreach(Match match in pattern.Matches(text)) {
            if(!decimal.TryParse(match.Groups["value"].Value,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint|NumberStyles.AllowThousands,CultureInfo.InvariantCulture,out var value)) continue;
            var unit=units.First(unit=>unit.Aliases.Contains(match.Groups["unit"].Value,StringComparer.OrdinalIgnoreCase));
            result.Add(new(value,unit.Symbol));
            if(result.Count==20) break;
        }
        return result;
    }
    internal static decimal To(decimal value,string source,string target)
    {
        var from=units.Single(unit=>unit.Symbol==source); var to=units.Single(unit=>unit.Symbol==target);
        if(from.Dimension!=to.Dimension) throw new FormatException(T("SmartInvalidInput"));
        if(from.Dimension!="temperature") return value*from.Factor/to.Factor;
        decimal kelvin=source switch { "°C"=>value+273.15m,"°F"=>(value-32)*5/9+273.15m,_=>value };
        if(kelvin<0) throw new FormatException(T("SmartAbsoluteZero"));
        return target switch { "°C"=>kelvin-273.15m,"°F"=>(kelvin-273.15m)*9/5+32,_=>kelvin };
    }
    internal static string Convert(string text)
    {
        var values=Parse(text);
        if(values.Count==0) throw new FormatException(T("SmartInvalidInput"));
        var results=new List<string>();
        foreach(var value in values) {
            var from=units.Single(unit=>unit.Symbol==value.Unit);
            var lines=new List<string> { value.Value.ToString("G29",CultureInfo.InvariantCulture)+" "+value.Unit };
            foreach(var unit in units.Where(unit=>unit.Dimension==from.Dimension&&unit.Symbol!=from.Symbol)) {
                var converted=To(value.Value,from.Symbol,unit.Symbol);
                var rounded=decimal.Round(converted,8,MidpointRounding.AwayFromZero);
                var formatted=rounded==0&&converted!=0?converted.ToString("G8",CultureInfo.InvariantCulture):rounded.ToString("0.########",CultureInfo.InvariantCulture);
                lines.Add((converted==rounded?"= ":"≈ ")+formatted+" "+unit.Symbol);
            }
            results.Add(string.Join("\n",lines));
        }
        if(values.Count==20) results.Add(T("SmartUnitLimit"));
        return string.Join("\n\n",results);
    }
}
