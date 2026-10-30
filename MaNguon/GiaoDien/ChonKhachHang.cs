using System.Data;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Hotel.WinForms;

internal sealed class CustomerPicker : ComboBox
{
    private sealed record CustomerChoice(int Id, string Name)
    {
        public override string ToString() => Name;
    }

    private DataTable customers = new();
    private CustomerChoice? committedCustomer;
    private bool binding, uncommittedText;
    private readonly ToolTip help = new();
    private readonly System.Windows.Forms.Timer searchTimer = new() { Interval = 120 };
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr window, string text);

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get
        {
            if (!IsHandleCreated) return base.Text;
            var text = new StringBuilder(512);
            GetWindowText(Handle, text, text.Capacity);
            return text.ToString();
        }
        set => base.Text = value;
    }

    public CustomerPicker(DataTable data, int? selected = null)
    {
        DropDownStyle = ComboBoxStyle.DropDown;
        DisplayMember = "Name";
        ValueMember = "Id";
        BindingContext = new BindingContext();
        AutoCompleteMode = AutoCompleteMode.None;
        IntegralHeight = false;
        DropDownHeight = 240;
        MaxDropDownItems = 10;
        MaxLength = 125;
        Width = 340;
        AccessibleName = "Tìm và chọn khách hàng";
        help.SetToolTip(this, "Gõ tên hoặc điện thoại để tìm; bấm mũi tên để chọn. Xóa nội dung để xem toàn bộ danh sách.");
        searchTimer.Tick += (_, _) => { searchTimer.Stop(); if (uncommittedText) FilterCustomers(); };
        Reload(data, selected);
    }

    public void Reload(DataTable data, int? selected = null)
    {
        searchTimer.Stop();
        customers = data.Copy();
        Bind(customers.Copy(), "", 0, selected);
        committedCustomer = SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] as CustomerChoice : null;
        uncommittedText = false;
    }

    public int SelectedCustomerId()
    {
        if (!uncommittedText && committedCustomer is { } row && row.Name == Text)
            return row.Id;
        throw new ArgumentException("Hãy chọn khách hàng trong danh sách kết quả hoặc thêm khách hàng mới.");
    }

    protected override void OnTextUpdate(EventArgs e)
    {
        if (!binding)
        {
            uncommittedText = true;
            // Wait for the native edit message to finish before replacing list items.
            searchTimer.Stop();
            searchTimer.Start();
        }
        base.OnTextUpdate(e);
    }

    private void FilterCustomers()
    {
        if (!binding && !IsDisposed)
        {
            var text = new StringBuilder(512);
            GetWindowText(Handle, text, text.Capacity);
            var typed = text.ToString();
            var caret = SelectionStart;
            var words = SearchText(typed).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var matches = customers.Clone();
            foreach (DataRow row in customers.Rows)
            {
                var name = SearchText(Convert.ToString(row["Name"]) ?? "");
                if (words.All(word => name.Contains(word, StringComparison.Ordinal)))
                    matches.ImportRow(row);
            }
            DroppedDown = false;
            Bind(matches, typed, caret);
        }
    }

    protected override void OnDropDown(EventArgs e)
    {
        // Never replace native items while the dropdown is open.
        var pending = searchTimer.Enabled;
        searchTimer.Stop();
        base.OnDropDown(e);
        if (pending && IsHandleCreated)
            BeginInvoke((Action)(() =>
            {
                if (IsDisposed || Disposing || !uncommittedText) return;
                FilterCustomers();
                if (Focused && Enabled && Items.Count > 0) DroppedDown = true;
            }));
    }

    protected override void OnSelectionChangeCommitted(EventArgs e)
    {
        searchTimer.Stop();
        committedCustomer = SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] as CustomerChoice : null;
        uncommittedText = committedCustomer is null;
        base.OnSelectionChangeCommitted(e);
    }

    private void Bind(DataTable data, string typed, int caret, int? selected = null)
    {
        binding = true;
        BeginUpdate();
        try
        {
            DroppedDown = false;
            SelectedIndex = -1;
            Items.Clear();
            foreach (DataRow row in data.Rows)
                Items.Add(new CustomerChoice(row.Field<int>("Id"), row.Field<string>("Name") ?? ""));
            SelectedIndex = -1;
            var selectedItem = Items.Cast<CustomerChoice>().FirstOrDefault(row => row.Id == selected);
            if (selectedItem is not null)
                SelectedItem = selectedItem;
            else
            {
                if (IsHandleCreated) SetWindowText(Handle, typed);
                else Text = typed;
                SelectionStart = Math.Min(caret, Text.Length);
                SelectionLength = 0;
            }
        }
        finally { EndUpdate(); binding = false; }
    }

    private static string SearchText(string text)
    {
        var result = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                result.Append(c is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(c));
        return result.ToString().Normalize(NormalizationForm.FormC);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { searchTimer.Dispose(); help.Dispose(); }
        base.Dispose(disposing);
    }
}
