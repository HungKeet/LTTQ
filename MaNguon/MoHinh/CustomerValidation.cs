using System.Text;
using System.Text.RegularExpressions;
using PhoneNumbers;

namespace Hotel.Core;

/// <summary>Validation shared by customer forms and business operations.</summary>
public static class CustomerValidation
{
    public static string FullName(string? value)
    {
        var name = Regex.Replace((value ?? "").Trim().Normalize(NormalizationForm.FormC), @"\s+", " ");
        if (name.Length < 2 || name.Length > 100 || !Regex.IsMatch(name, @"\p{L}") ||
            !Regex.IsMatch(name, @"^[\p{L}\p{M} '\u2019-]+$"))
            throw new ArgumentException("Họ tên cần 2–100 ký tự, gồm chữ, khoảng trắng, dấu gạch nối hoặc dấu nháy.");
        return name;
    }

    public static string Phone(string? value)
    {
        var raw = (value ?? "").Trim();
        if (raw.Length == 0 || raw.Any(c => !(c is >= '0' and <= '9' || c is '+' or '-' or '.' || char.IsWhiteSpace(c))))
            throw new ArgumentException("Vui lòng nhập số điện thoại Việt Nam hợp lệ.");
        var phone = new string(raw.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '.').ToArray());
        if (!phone.StartsWith("0", StringComparison.Ordinal) && !phone.StartsWith("+84", StringComparison.Ordinal))
            throw new ArgumentException("Vui lòng nhập số điện thoại Việt Nam hợp lệ (ví dụ 0912345678 hoặc +84912345678).");
        try
        {
            var util = PhoneNumberUtil.GetInstance();
            var parsed = util.Parse(phone, "VN");
            var type = util.GetNumberType(parsed);
            if (parsed.HasExtension || !util.IsValidNumberForRegion(parsed, "VN") ||
                type is not (PhoneNumberType.MOBILE or PhoneNumberType.FIXED_LINE or PhoneNumberType.FIXED_LINE_OR_MOBILE))
                throw new ArgumentException("Vui lòng nhập số điện thoại Việt Nam hợp lệ (ví dụ 0912345678 hoặc +84912345678).");
            return "0" + parsed.NationalNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (NumberParseException)
        {
            throw new ArgumentException("Vui lòng nhập số điện thoại Việt Nam hợp lệ (ví dụ 0912345678 hoặc +84912345678).");
        }
    }

    public static string Identity(string? value)
    {
        var identity = (value ?? "").Trim();
        if (identity.Length > 30) throw new ArgumentException("Giấy tờ không quá 30 ký tự.");
        return identity;
    }

    public static string Address(string? value)
    {
        var address = (value ?? "").Trim();
        if (address.Length > 250 || (address.Length > 0 && !Regex.IsMatch(address, @"[\p{L}\p{N}]")))
            throw new ArgumentException("Địa chỉ tối đa 250 ký tự và cần chứa chữ hoặc số.");
        return address;
    }
}
