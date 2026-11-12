using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PopBar;

internal static class SmartTransforms
{
    static readonly CultureInfo invariant=CultureInfo.InvariantCulture;

    internal static bool IsCalculation(string text)
    {
        text=NormalizeMath(text);
        return text.Length<=256 && text.Any(char.IsDigit) && text.IndexOfAny(new[]{'+','-','*','/','^','%'})>=0
            && !Regex.IsMatch(text,@"^\d{4}-\d{1,2}-\d{1,2}$")
            && Regex.IsMatch(text,@"\A[0-9.\s()+*/^%=-]+\z");
    }
    static string NormalizeMath(string text)=>text.Trim().Normalize(NormalizationForm.FormKC).Replace('×','*').Replace('÷','/').Replace('−','-').TrimEnd('=').Trim();
    internal static string Calculate(string text)=>new Calculator(NormalizeMath(text)).Evaluate().ToString("G29",invariant);
    sealed class Calculator(string text)
    {
        int position,depth;
        void Space() { while(position<text.Length && char.IsWhiteSpace(text[position])) position++; }
        bool Eat(char value) { Space(); if(position>=text.Length||text[position]!=value) return false; position++; return true; }
        decimal Expression() { var n=Product(); while(true) { if(Eat('+')) n+=Product(); else if(Eat('-')) n-=Product(); else return n; } }
        decimal Product() { var n=Unary(); while(true) { if(Eat('*')) n*=Unary(); else if(Eat('/')) n/=Unary(); else return n; } }
        decimal Unary() {
            if(++depth>64) throw new FormatException(T("SmartInvalidInput"));
            try { if(Eat('+')) return Unary(); if(Eat('-')) return -Unary(); return Power(); } finally { depth--; }
        }
        decimal Power() {
            var n=Atom(); while(Eat('%')) n/=100;
            if(!Eat('^')) return n;
            var exponent=Unary();
            if(exponent!=decimal.Truncate(exponent)||Math.Abs(exponent)>100) throw new FormatException(T("SmartExponent"));
            decimal value=1; for(int i=0;i<(int)Math.Abs(exponent);i++) value*=n;
            return exponent<0?1/value:value;
        }
        decimal Atom() {
            if(Eat('(')) { var n=Expression(); if(!Eat(')')) throw new FormatException(T("SmartInvalidInput")); return n; }
            Space(); int start=position;
            while(position<text.Length&&(char.IsAsciiDigit(text[position])||text[position]=='.')) position++;
            if(start==position||!decimal.TryParse(text[start..position],NumberStyles.AllowDecimalPoint,invariant,out var value)) throw new FormatException(T("SmartInvalidInput"));
            return value;
        }
        internal decimal Evaluate() {
            if(text.Length>256) throw new FormatException(T("SmartInvalidInput"));
            var result=Expression(); Space(); if(position!=text.Length) throw new FormatException(T("SmartInvalidInput")); return result;
        }
    }
    static string JsonInput(string text) {
        text=text.Trim();
        if(text.StartsWith("```json",StringComparison.OrdinalIgnoreCase)&&text.EndsWith("```")) text=text[7..^3].Trim();
        return text;
    }
    internal static bool IsJson(string text) {
        text=JsonInput(text);
        if(text.Length==0 || text[0] is not ('{' or '[')) return false;
        try { using var doc=JsonDocument.Parse(text,new JsonDocumentOptions { MaxDepth=64 }); return true; } catch(JsonException) { return false; }
    }
    internal static string Json(string text,bool compact=false) {
        using var doc=JsonDocument.Parse(JsonInput(text),new JsonDocumentOptions { MaxDepth=64 });
        return JsonSerializer.Serialize(doc.RootElement,new JsonSerializerOptions { WriteIndented=!compact,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
