namespace Hotel.Core;

public sealed record Session(int UserId, int EmployeeId, string Username, string FullName, string Role);
public sealed record BookingInput(int CustomerId, DateTime Arrival, DateTime Departure, int Guests, decimal Deposit, string Notes, IReadOnlyList<int> RoomIds);
public sealed record InvoiceLine(string Kind, string Description, decimal Quantity, string Unit, decimal UnitPrice)
{
    public decimal Amount => Money.Round(Quantity * UnitPrice);
}
public sealed record CheckoutQuote(int BookingId, DateTime CheckoutAt, IReadOnlyList<InvoiceLine> Lines, decimal Surcharge, decimal Discount, decimal Deposit)
{
    public decimal Subtotal => Lines.Sum(x => x.Amount);
    public decimal Total => Money.Round(Subtotal + Surcharge - Discount);
    public decimal Balance => Total - Deposit;
}
public sealed record InvoiceDocument(int Id, string Number, string Customer, string Phone, string IdentityNumber, string Address, string Employee, DateTime IssuedAt, DateTime CheckIn, DateTime CheckOut, decimal Surcharge, decimal Discount, decimal Deposit, decimal Total, string PaymentMethod, string Notes, IReadOnlyList<InvoiceLine> Lines)
{
    public decimal Balance => Total - Deposit;
}
public static class Money
{
    public static decimal Round(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);
    public static int Nights(DateTime arrival, DateTime departure)
    {
        if (departure < arrival)
            throw new ArgumentException("Thời điểm trả phòng phải sau thời điểm nhận phòng.");
        return Math.Max(1, (departure.Date - arrival.Date).Days);
    }
    public static void ValidateAdjustments(decimal subtotal, decimal surcharge, decimal discount)
    {
        if (surcharge < 0 || discount < 0 || discount > subtotal + surcharge)
            throw new ArgumentException("Phụ thu, giảm giá không được âm; giảm giá không được vượt tổng tiền trước giảm.");
    }

    public static string ToWords(decimal amount)
    {
        if (amount == 0) return "Không đồng";
        if (amount < 0) return "Âm " + ToWords(Math.Abs(amount));

        string[] digits = { "không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín" };
        string[] units = { "", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ" };

        long s = (long)Math.Round(amount);
        string str = s.ToString();
        int len = str.Length;
        int groups = (len + 2) / 3;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < groups; i++)
        {
            int start = Math.Max(0, len - (groups - i) * 3);
            int end = len - (groups - i - 1) * 3;
            int num = int.Parse(str.Substring(start, end - start));

            if (num > 0)
            {
                int hundreds = num / 100;
                int tens = (num % 100) / 10;
                int ones = num % 10;

                var b = new System.Text.StringBuilder();
                if (hundreds > 0 || (i > 0 && str.Substring(start, end - start).Length < 3))
                    b.Append(digits[hundreds]).Append(" trăm ");

                if (tens > 1)
                {
                    b.Append(digits[tens]).Append(" mươi ");
                    if (ones == 1) b.Append("mốt");
                    else if (ones == 5) b.Append("lăm");
                    else if (ones > 0) b.Append(digits[ones]);
                }
                else if (tens == 1)
                {
                    b.Append("mười ");
                    if (ones == 5) b.Append("lăm");
                    else if (ones > 0) b.Append(digits[ones]);
                }
                else if (tens == 0 && (hundreds > 0 || (i > 0 && str.Substring(start, end - start).Length < 3)) && ones > 0)
                {
                    b.Append("linh ").Append(digits[ones]);
                }
                else if (tens == 0 && hundreds == 0 && ones > 0)
                {
                    b.Append(digits[ones]);
                }

                string groupStr = b.ToString().Trim();
                if (groupStr.Length > 0)
                {
                    sb.Append(groupStr).Append(" ").Append(units[groups - i - 1]).Append(" ");
                }
            }
        }

        string res = System.Text.RegularExpressions.Regex.Replace(sb.ToString().Trim(), @"\s+", " ");
        return res.Length > 0 ? char.ToUpper(res[0]) + res.Substring(1) + " đồng chẵn" : "Không đồng";
    }
}
