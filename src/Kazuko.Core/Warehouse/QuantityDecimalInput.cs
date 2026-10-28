using System.Globalization;

namespace Kazuko.Core.Warehouse;

/// <summary>Grouped vi-VN quantities; never let NumberStyles.Number discard an arbitrary decimal dot.</summary>
public static class QuantityDecimalInput
{
    public static bool TryParse(string? text,out decimal value)
    {
        value=0;if(string.IsNullOrWhiteSpace(text))return false;
        var input=text.Trim();var sign="";
        if(input[0] is '-' or '+'){sign=input[..1];input=input[1..];}
        if(input.Length==0||input.Any(c=>!char.IsAsciiDigit(c)&&c is not ('.' or ',')))return false;
        string normalized;
        if(input.Contains(','))
        {
            var parts=input.Split(',');if(parts.Length!=2||!Digits(parts[1])||parts[1].Length>4)return false;
            if(!Whole(parts[0],out var whole))return false;
            normalized=whole+"."+parts[1];
        }
        else
        {
            var parts=input.Split('.');
            if(parts.Length==2 && !(parts[0].Length is >0 and <=3&&parts[0][0]!='0'&&parts[1].Length==3))
            {
                if(!Digits(parts[0])||!Digits(parts[1])||parts[1].Length>4)return false;
                normalized=parts[0]+"."+parts[1];
            }
            else if(!Whole(input,out normalized))return false;
        }
        return decimal.TryParse(sign+normalized,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value);
    }
    private static bool Digits(string text)=>text.All(char.IsAsciiDigit);
    private static bool Whole(string text,out string normalized)
    {
        normalized="";var groups=text.Split('.');
        if(groups.Any(g=>g.Length==0||!Digits(g)))return false;
        if(groups.Length>1&&(groups[0].Length>3||groups[0][0]=='0'||groups.Skip(1).Any(g=>g.Length!=3)))return false;
        normalized=string.Concat(groups);return true;
    }
}
