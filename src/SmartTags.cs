using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Shengyi;

// Recognises the entities people most often copy, using nothing but local regular expressions and
// checksum arithmetic. No network, no model, no key: 绳忆 promises that clipboard data never leaves
// the machine, and these nine kinds are all cheap to decide with high precision on device.
//
// Tags are canonical lowercase ids stored in ClipItem.Tags; the display name is chosen at render
// time from the UI culture. This file has no WPF dependency so the console self-test can compile it.
static class SmartTags
{
    public const string Phone = "phone";
    public const string Email = "email";
    public const string Express = "express";
    public const string IdCard = "idcard";
    public const string BankCard = "bankcard";
    public const string Address = "address";
    public const string Date = "date";
    public const string Amount = "amount";
    public const string Order = "order";

    // Order matters: it is the order of the filter chips, the badge priority and the tag list.
    static readonly (string id, string en, string zh, string[] aliases)[] Defs =
    {
        (Phone,    "Phone",   "手机号", new[] { "mobile", "tel", "telephone", "cell", "电话", "手机", "号码" }),
        (Email,    "Email",   "邮箱",   new[] { "mail", "e-mail", "邮件", "电子邮件" }),
        (Express,  "Express", "快递单", new[] { "tracking", "waybill", "shipment", "parcel", "快递", "运单", "物流" }),
        (IdCard,   "ID card", "身份证", new[] { "id", "identity", "idno", "证件", "证件号" }),
        (BankCard, "Card",    "银行卡", new[] { "card", "bank", "creditcard", "卡号", "账号" }),
        (Address,  "Address", "地址",   new[] { "addr", "location", "住址", "收货地址" }),
        (Date,     "Date",    "日期",   new[] { "time", "day", "时间", "年月日" }),
        (Amount,   "Amount",  "金额",   new[] { "money", "price", "cost", "total", "价格", "价钱", "款项" }),
        (Order,    "Order",   "订单号", new[] { "orderno", "trade", "订单", "单号", "交易号", "流水号" }),
    };

    public static readonly string[] All = Defs.Select(d => d.id).ToArray();

    static readonly Dictionary<string, int> ByName = BuildNames();
    static bool Zh => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";

    static Dictionary<string, int> BuildNames()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Defs.Length; i++)
        {
            var d = Defs[i];
            map[d.id] = i;
            map[d.en] = i;
            map[d.zh] = i;
            foreach (var a in d.aliases) map[a] = i;
        }
        return map;
    }

    public static bool IsKnown(string tag) => ByName.ContainsKey(tag);

    // Accepts the id, either display language, or a common alias — so tag:phone, tag:手机 and
    // tag:手机号 all mean the same thing. Unknown words pass through untouched.
    public static string Canonical(string tag) =>
        ByName.TryGetValue(tag.Trim(), out int i) ? Defs[i].id : tag.Trim();

    public static string Name(string tag) =>
        ByName.TryGetValue(tag, out int i) ? (Zh ? Defs[i].zh : Defs[i].en) : tag;

    // The tag shown on the card corner when several were recognised.
    public static string? Primary(IEnumerable<string>? tags)
    {
        if (tags == null) return null;
        var set = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
        foreach (var id in All) if (set.Contains(id)) return Name(id);
        return tags.FirstOrDefault();
    }

    const RegexOptions O = RegexOptions.CultureInvariant;
    const RegexOptions Oi = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    static readonly Regex PhoneRx = new(@"(?<!\d)(1[3-9]\d)[\s-]?(\d{4})[\s-]?(\d{4})(?!\d)", O);
    static readonly Regex EmailRx = new(@"(?<![A-Za-z0-9._%+-])[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}(?![A-Za-z0-9-])", O);
    static readonly Regex IdRx = new(@"(?<![0-9A-Za-z])(\d{17}[\dXx])(?![0-9A-Za-z])", O);
    static readonly Regex CardRx = new(@"(?<![0-9A-Za-z])(\d{4}(?:[ -]?\d{4}){3}(?:[ -]?\d{1,3})?)(?![0-9A-Za-z])", O);
    static readonly Regex ExpressKeywordRx = new(@"(?:快递|运单|物流|单号|waybill|tracking|shipping)", Oi);
    static readonly Regex ExpressEmsRx = new(@"(?<![0-9A-Za-z])([A-Z]{2}\d{9}[A-Z]{2})(?![0-9A-Za-z])", O);
    static readonly Regex ExpressPrefixRx = new(@"(?<![0-9A-Za-z])((?:SF|YT|ZTO|STO|YD|JD|JT|EMS|DB|UC)\d{8,16})(?![0-9A-Za-z])", Oi);
    static readonly Regex LongTokenRx = new(@"(?<![0-9A-Za-z])([A-Za-z0-9]{8,24})(?![0-9A-Za-z])", O);
    static readonly Regex AddressRunRx = new(@"[\u4e00-\u9fa5][\u4e00-\u9fa5\d\-A-Za-z]{5,59}", O);
    static readonly Regex AdminRx = new(@"[\u4e00-\u9fa5]{2}(?:省|自治区|特别行政区|市|区|县|镇|街道|乡|村)", O);
    static readonly Regex LocalityRx = new(@"(?:路|街|巷|大道|号|栋|幢|单元|室|楼|层|弄|大厦|小区|苑|园)", O);
    static readonly Regex DateYmdRx = new(@"(?<!\d)((?:19|20)\d{2})\s*[-/.年]\s*(\d{1,2})\s*[-/.月]\s*(\d{1,2})\s*日?(?!\d)", O);
    static readonly Regex DateMdRx = new(@"(?<!\d)(\d{1,2})\s*月\s*(\d{1,2})\s*日(?!\d)", O);
    static readonly Regex AmountSymbolRx = new(@"(?:¥|￥|\$|USD|CNY|RMB)\s*\d[\d,]*(?:\.\d{1,2})?(?![\d.])", Oi);
    static readonly Regex AmountWordRx = new(@"(?<![\d.])\d[\d,]*(?:\.\d{1,2})?\s*(?:元|块钱?|万元|美元|人民币|港币|欧元)", O);
    static readonly Regex OrderRx = new(@"(?:订单(?:号|编号)?|交易号|流水号|order\s*(?:no|id|number)?|trade\s*no)\s*[:：#]?\s*([A-Za-z0-9_-]{6,32})", Oi);

    static readonly int[] IdWeights = { 7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2 };
    const string IdCheck = "10X98765432";

    // Returns null when nothing was recognised, so clips.json stays lean for ordinary text.
    public static List<string>? Analyze(ClipItem item)
    {
        string text = item.Kind == ClipKind.Files
            ? string.Join(" ", (item.Files ?? new()).Select(f => System.IO.Path.GetFileName(f.TrimEnd('\\', '/'))))
            : item.Text ?? "";
        if (text.Length == 0) return null;
        if (text.Length > 20000) text = text[..20000]; // same cap as the search index

        var found = new HashSet<string>(StringComparer.Ordinal);

        if (PhoneRx.IsMatch(text)) found.Add(Phone);
        if (EmailRx.IsMatch(text)) found.Add(Email);

        foreach (Match m in IdRx.Matches(text))
            if (ValidIdCard(m.Groups[1].Value)) { found.Add(IdCard); break; }

        foreach (Match m in CardRx.Matches(text))
        {
            string digits = Digits(m.Groups[1].Value);
            if (digits.Length is >= 16 and <= 19 && Luhn(digits) && !IdRx.IsMatch(digits))
            { found.Add(BankCard); break; }
        }

        if (ExpressEmsRx.IsMatch(text) || ExpressPrefixRx.IsMatch(text)) found.Add(Express);
        else if (ExpressKeywordRx.IsMatch(text))
        {
            foreach (Match m in LongTokenRx.Matches(text))
            {
                string token = m.Groups[1].Value;
                if (!token.Any(char.IsDigit)) continue;
                // A bare 11-digit phone number is not a waybill number.
                if (token.Length < 12 && !token.Any(char.IsLetter)) continue;
                found.Add(Express);
                break;
            }
        }

        foreach (Match m in AddressRunRx.Matches(text))
            if (m.Value.Length >= 6 && AdminRx.IsMatch(m.Value) && LocalityRx.IsMatch(m.Value))
            { found.Add(Address); break; }

        if (AnyValidDate(text)) found.Add(Date);
        if (AmountSymbolRx.IsMatch(text) || AmountWordRx.IsMatch(text)) found.Add(Amount);
        if (OrderRx.IsMatch(text)) found.Add(Order);

        return found.Count == 0 ? null : All.Where(found.Contains).ToList();
    }

    static string Digits(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s) if (c >= '0' && c <= '9') sb.Append(c);
        return sb.ToString();
    }

    // GB 11643: weighted sum of the first 17 digits selects one of "10X98765432".
    public static bool ValidIdCard(string s)
    {
        if (s.Length != 18) return false;
        s = s.ToUpperInvariant();
        int sum = 0;
        for (int i = 0; i < 17; i++)
        {
            if (s[i] < '0' || s[i] > '9') return false;
            sum += (s[i] - '0') * IdWeights[i];
        }
        if (IdCheck[sum % 11] != s[17]) return false;
        if (!int.TryParse(s[6..10], out int year) || !int.TryParse(s[10..12], out int month)
            || !int.TryParse(s[12..14], out int day)) return false;
        try
        {
            var born = new DateTime(year, month, day);
            return year >= 1900 && born <= DateTime.Today;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    public static bool Luhn(string digits)
    {
        if (digits.Length == 0) return false;
        int sum = 0;
        bool doubleIt = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            if (digits[i] < '0' || digits[i] > '9') return false;
            int d = digits[i] - '0';
            if (doubleIt) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
            doubleIt = !doubleIt;
        }
        return sum % 10 == 0;
    }

    static bool AnyValidDate(string text)
    {
        foreach (Match m in DateYmdRx.Matches(text))
        {
            int year = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            int day = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            if (month is >= 1 and <= 12 && day is >= 1 and <= 31 && year is >= 1900 and <= 2200) return true;
        }
        foreach (Match m in DateMdRx.Matches(text))
        {
            int month = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int day = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (month is >= 1 and <= 12 && day is >= 1 and <= 31) return true;
        }
        return false;
    }
}
