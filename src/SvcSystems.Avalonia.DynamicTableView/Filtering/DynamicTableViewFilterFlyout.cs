using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SvcSystems.Avalonia.DynamicTableView;

internal sealed partial class DynamicTableViewFilterFlyout : TemplatedControl
{
    private const double DefaultWidth = 280;

    private readonly DynamicTableViewColumn _column;
    private readonly IDynamicTableViewSource _source;
    private readonly bool _isBoolean;
    private readonly bool _isNumber;
    private readonly bool _isDate;
    private Button? _applyButton;
    private Button? _clearButton;
    private TextBox? _valueTextBox;
    private TextBox? _secondValueTextBox;
    private NumericUpDown? _numericValueBox;
    private NumericUpDown? _numericSecondValueBox;
    private ComboBox? _choiceBox;
    private ListBox? _multipleChoiceBox;
    private object?[] _selectedMultipleChoiceValues = [];
    private StringComparison _stringComparison = StringComparison.OrdinalIgnoreCase;

    public DynamicTableViewFilterFlyout(DynamicTableViewColumn column, IDynamicTableViewSource source)
    {
        MinWidth = DefaultWidth;
        _column = column ?? throw new ArgumentNullException(nameof(column));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        ColumnHeader = column.Header.ToString() ?? string.Empty;
        _isBoolean = column.ValueType == typeof(bool);
        _isNumber = IsNumericType(column.ValueType);
        _isDate = column.ValueType == typeof(DateTime) || column.ValueType == typeof(DateTimeOffset);
        FilterChoices = GetChoices(column, _isBoolean);
        OperatorChoices = GetOperators(_isBoolean, FilterChoices.Count > 0, _isNumber, _isDate)
            .Select(static filterOperator => new DynamicTableViewFilterChoice(
                DynamicTableViewResources.GetFilterOperatorLabel(filterOperator), filterOperator))
            .ToArray();
        SelectedOperatorChoice = OperatorChoices.FirstOrDefault();
        Classes.Add("dynamic-table-view-filter-flyout");
        RestoreFilterState();
        UpdateInputVisibility();
    }

    [GeneratedDirectProperty]
    public partial string ColumnHeader { get; set; } = string.Empty;

    [GeneratedDirectProperty]
    public partial IReadOnlyList<DynamicTableViewFilterChoice> OperatorChoices { get; set; } = [];

    [GeneratedDirectProperty]
    public partial IReadOnlyList<DynamicTableViewFilterChoice> FilterChoices { get; set; } = [];

    [GeneratedDirectProperty]
    public partial DynamicTableViewFilterChoice? SelectedOperatorChoice { get; set; }

    [GeneratedDirectProperty]
    public partial DynamicTableViewFilterChoice? SelectedChoice { get; set; }

    [GeneratedDirectProperty]
    public partial string? FirstValueText { get; set; }

    [GeneratedDirectProperty]
    public partial string? SecondValueText { get; set; }

    [GeneratedDirectProperty]
    public partial decimal? FirstNumericValue { get; set; }

    [GeneratedDirectProperty]
    public partial decimal? SecondNumericValue { get; set; }

    [GeneratedDirectProperty]
    public partial bool IsValueInputVisible { get; set; }

    [GeneratedDirectProperty]
    public partial bool IsNumericValueInputVisible { get; set; }

    [GeneratedDirectProperty]
    public partial bool IsSecondValueInputVisible { get; set; }

    [GeneratedDirectProperty]
    public partial bool IsNumericSecondValueInputVisible { get; set; }

    [GeneratedDirectProperty]
    public partial bool IsSingleChoiceInputVisible { get; set; }

    [GeneratedDirectProperty]
    public partial bool IsMultiChoiceInputVisible { get; set; }

    public event EventHandler? FilterValidationFailed;

    protected override Type StyleKeyOverride => typeof(DynamicTableViewFilterFlyout);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_applyButton is not null)
            _applyButton.Click -= ApplyButtonOnClick;
        if (_clearButton is not null)
            _clearButton.Click -= ClearButtonOnClick;
        if (_multipleChoiceBox is not null)
            _multipleChoiceBox.SelectionChanged -= MultipleChoiceBoxOnSelectionChanged;

        base.OnApplyTemplate(e);
        _applyButton = e.NameScope.Find<Button>("PART_ApplyButton");
        _clearButton = e.NameScope.Find<Button>("PART_ClearButton");
        _valueTextBox = e.NameScope.Find<TextBox>("PART_ValueBox");
        _secondValueTextBox = e.NameScope.Find<TextBox>("PART_SecondValueBox");
        _numericValueBox = e.NameScope.Find<NumericUpDown>("PART_NumericValueBox");
        _numericSecondValueBox = e.NameScope.Find<NumericUpDown>("PART_NumericSecondValueBox");
        _choiceBox = e.NameScope.Find<ComboBox>("PART_ChoiceBox");
        _multipleChoiceBox = e.NameScope.Find<ListBox>("PART_MultipleChoiceBox");
        ConfigureNumericInput(_numericValueBox);
        ConfigureNumericInput(_numericSecondValueBox);
        if (_applyButton is not null)
            _applyButton.Click += ApplyButtonOnClick;
        if (_clearButton is not null)
            _clearButton.Click += ClearButtonOnClick;
        if (_multipleChoiceBox is not null)
            _multipleChoiceBox.SelectionChanged += MultipleChoiceBoxOnSelectionChanged;
        RestoreMultipleChoiceSelection();
    }

    partial void OnSelectedOperatorChoicePropertyChanged(DynamicTableViewFilterChoice? newValue)
    {
        ClearValidationErrors();
        UpdateInputVisibility();
    }

    private void UpdateInputVisibility()
    {
        DynamicTableViewFilterOperator? filterOperator = SelectedOperatorChoice?.Value is DynamicTableViewFilterOperator value ? value : null;
        var isRange = filterOperator is DynamicTableViewFilterOperator.Between or DynamicTableViewFilterOperator.NotBetween;
        var isIn = filterOperator == DynamicTableViewFilterOperator.In;
        var isValueFree = filterOperator is DynamicTableViewFilterOperator.IsTrue or DynamicTableViewFilterOperator.IsFalse or DynamicTableViewFilterOperator.IsNull or DynamicTableViewFilterOperator.IsNotNull;
        IsSingleChoiceInputVisible = FilterChoices.Count > 0 && !isIn;
        IsMultiChoiceInputVisible = FilterChoices.Count > 0 && isIn;
        IsValueInputVisible = !isValueFree && FilterChoices.Count == 0 && (!_isNumber || isIn);
        IsNumericValueInputVisible = !isValueFree && _isNumber && FilterChoices.Count == 0 && !isIn;
        IsSecondValueInputVisible = isRange && _isDate;
        IsNumericSecondValueInputVisible = isRange && _isNumber;
    }

    private void ApplyButtonOnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedOperatorChoice?.Value is not DynamicTableViewFilterOperator filterOperator)
            return;

        ClearValidationErrors();
        object? value = null;
        object? secondValue = null;
        IReadOnlyList<object?>? values = null;
        if (filterOperator is DynamicTableViewFilterOperator.IsTrue or DynamicTableViewFilterOperator.IsFalse or DynamicTableViewFilterOperator.IsNull or DynamicTableViewFilterOperator.IsNotNull)
        {
            _source.SetFilter(new(_column.Key, filterOperator));
            return;
        }

        if (FilterChoices.Count > 0)
        {
            if (filterOperator == DynamicTableViewFilterOperator.In)
            {
                values = _multipleChoiceBox?.SelectedItems
                    .OfType<DynamicTableViewFilterChoice>()
                    .Select(static choice => choice.Value)
                    .ToArray() ?? [];
                if (values.Count == 0)
                {
                    ReportValidationError(_multipleChoiceBox, DynamicTableViewResources.FilterValidationRequired);
                    return;
                }
            }
            else
            {
                if (SelectedChoice is null)
                {
                    ReportValidationError(_choiceBox, DynamicTableViewResources.FilterValidationRequired);
                    return;
                }
                value = SelectedChoice.Value;
            }
        }
        else if (filterOperator == DynamicTableViewFilterOperator.In)
        {
            var tokens = (FirstValueText ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                ReportValidationError(_valueTextBox, DynamicTableViewResources.FilterValidationRequired);
                return;
            }

            var parsed = new object?[tokens.Length];
            for (var i = 0; i < tokens.Length; i++)
            {
                if (!TryParseValue(tokens[i], out parsed[i]))
                {
                    ReportValidationError(GetFirstValueInput(), GetValueValidationMessage());
                    return;
                }
            }
            values = parsed;
        }
        else if (!TryParseValue(GetFirstValueText(), out value))
        {
            ReportValidationError(GetFirstValueInput(), GetValueValidationMessage());
            return;
        }

        if (filterOperator is DynamicTableViewFilterOperator.Between or DynamicTableViewFilterOperator.NotBetween)
        {
            if (!TryParseValue(GetSecondValueText(), out secondValue))
            {
                ReportValidationError(GetSecondValueInput(), GetValueValidationMessage());
                return;
            }
        }

        ClearValidationErrors();
        _source.SetFilter(new(_column.Key, filterOperator, value, secondValue, values, _stringComparison));
    }

    private void ClearButtonOnClick(object? sender, RoutedEventArgs e)
    {
        ClearValidationErrors();
        _source.SetFilter(null, _column.Key);
    }

    private void MultipleChoiceBoxOnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedOperatorChoice?.Value is DynamicTableViewFilterOperator selectedOperator && selectedOperator == DynamicTableViewFilterOperator.In)
            UpdateInputVisibility();
    }

    private void RestoreFilterState()
    {
        DynamicTableViewFilterDescriptor? descriptor = null;
        foreach (var filter in _source.FilterDescriptors)
        {
            if (string.Equals(filter.ColumnKey, _column.Key, StringComparison.Ordinal))
            {
                descriptor = filter;
                break;
            }
        }

        if (descriptor is null)
            return;

        _stringComparison = descriptor.StringComparison;
        SelectedOperatorChoice = OperatorChoices.FirstOrDefault(choice =>
            choice.Value is DynamicTableViewFilterOperator filterOperator && filterOperator == descriptor.Operator);

        if (FilterChoices.Count > 0)
        {
            if (descriptor.Operator == DynamicTableViewFilterOperator.In)
            {
                _selectedMultipleChoiceValues = descriptor.Values?.ToArray() ?? [];
            }
            else
            {
                SelectedChoice = FilterChoices.FirstOrDefault(choice => Equals(choice.Value, descriptor.Value));
            }
            return;
        }

        if (_isNumber)
        {
            FirstNumericValue = TryParseNumericValue(FormatFilterValue(descriptor.Value), out decimal firstValue)
                ? firstValue
                : null;
            SecondNumericValue = TryParseNumericValue(FormatFilterValue(descriptor.SecondValue), out decimal secondValue)
                ? secondValue
                : null;
        }
        else
        {
            FirstValueText = FormatFilterValue(descriptor.Value);
            SecondValueText = FormatFilterValue(descriptor.SecondValue);
        }
    }

    private void RestoreMultipleChoiceSelection()
    {
        if (_multipleChoiceBox is null || _selectedMultipleChoiceValues.Length == 0)
            return;

        foreach (var value in _selectedMultipleChoiceValues)
        {
            var choice = FilterChoices.FirstOrDefault(candidate => Equals(candidate.Value, value));
            if (choice is not null && !_multipleChoiceBox.SelectedItems.Contains(choice))
                _multipleChoiceBox.SelectedItems.Add(choice);
        }
    }

    private static string? FormatFilterValue(object? value)
        => value switch
        {
            null => null,
            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    private bool TryParseValue(string text, out object? value)
    {
        if (_isNumber)
        {
            var parsed = TryParseNumericValue(text, out var number);
            value = number;
            return parsed;
        }
        if (_isDate)
        {
            var parsed = DateTimeOffset.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var date);
            value = date;
            return parsed;
        }
        value = text;
        return !string.IsNullOrWhiteSpace(text);
    }

    private static bool TryParseNumericValue(string? text, out decimal value)
        => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private string GetFirstValueText()
        => _isNumber && SelectedOperatorChoice?.Value is not DynamicTableViewFilterOperator.In
            ? _numericValueBox?.Text ?? string.Empty
            : FirstValueText ?? string.Empty;

    private string GetSecondValueText()
        => _isNumber ? _numericSecondValueBox?.Text ?? string.Empty : SecondValueText ?? string.Empty;

    private Control? GetFirstValueInput()
        => _isNumber && SelectedOperatorChoice?.Value is not DynamicTableViewFilterOperator.In
            ? GetNumericInputValidationTarget(_numericValueBox)
            : _valueTextBox;

    private Control? GetSecondValueInput()
        => _isNumber ? GetNumericInputValidationTarget(_numericSecondValueBox) : _secondValueTextBox;

    private string GetValueValidationMessage()
        => _isNumber
            ? DynamicTableViewResources.FilterValidationNumber
            : _isDate
                ? DynamicTableViewResources.FilterValidationDate
                : DynamicTableViewResources.FilterValidationRequired;

    private static void ConfigureNumericInput(NumericUpDown? input)
    {
        if (input is null)
            return;

        input.NumberFormat = CultureInfo.InvariantCulture.NumberFormat;
        input.ParsingNumberStyle = NumberStyles.Number;
        input.ShowButtonSpinner = true;
    }

    private void ClearValidationErrors()
    {
        ClearValidationError(_valueTextBox);
        ClearValidationError(_secondValueTextBox);
        ClearValidationError(GetNumericInputValidationTarget(_numericValueBox));
        ClearValidationError(GetNumericInputValidationTarget(_numericSecondValueBox));
        ClearValidationError(_choiceBox);
        ClearValidationError(_multipleChoiceBox);
    }

    private static Control? GetNumericInputValidationTarget(NumericUpDown? input)
    {
        Control? textBox = input?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        return textBox ?? input;
    }

    private static void ClearValidationError(Control? control)
    {
        if (control is not null)
            DataValidationErrors.ClearErrors(control);
    }

    private void ReportValidationError(Control? control, string message)
    {
        if (control is not null)
            DataValidationErrors.SetErrors(control, [message]);
        FilterValidationFailed?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<DynamicTableViewFilterChoice> GetChoices(DynamicTableViewColumn column, bool isBoolean)
    {
        if (!isBoolean)
            return column.GetFilterChoices();
        return column.GetFilterChoices() is { Count: > 0 } choices
            ? choices
            : [new(DynamicTableViewResources.FilterFalse, false), new(DynamicTableViewResources.FilterTrue, true)];
    }

    private static IReadOnlyList<DynamicTableViewFilterOperator> GetOperators(bool boolean, bool choices, bool number, bool date)
    {
        if (boolean)
            return [DynamicTableViewFilterOperator.Equals, DynamicTableViewFilterOperator.NotEquals];
        if (choices)
            return [DynamicTableViewFilterOperator.Equals, DynamicTableViewFilterOperator.NotEquals, DynamicTableViewFilterOperator.In];
        if (date || number)
            return [DynamicTableViewFilterOperator.Equals, DynamicTableViewFilterOperator.NotEquals, DynamicTableViewFilterOperator.GreaterThan,
                DynamicTableViewFilterOperator.GreaterThanOrEqual, DynamicTableViewFilterOperator.LessThan,
                DynamicTableViewFilterOperator.LessThanOrEqual, DynamicTableViewFilterOperator.Between,
                DynamicTableViewFilterOperator.NotBetween, DynamicTableViewFilterOperator.In];
        return [DynamicTableViewFilterOperator.Contains, DynamicTableViewFilterOperator.DoesNotContain, DynamicTableViewFilterOperator.Equals,
            DynamicTableViewFilterOperator.NotEquals, DynamicTableViewFilterOperator.StartsWith,
            DynamicTableViewFilterOperator.DoesNotStartWith, DynamicTableViewFilterOperator.EndsWith,
            DynamicTableViewFilterOperator.DoesNotEndWith, DynamicTableViewFilterOperator.In,
            DynamicTableViewFilterOperator.IsNull, DynamicTableViewFilterOperator.IsNotNull];
    }

    private static bool IsNumericType(Type type)
        => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
           type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
           type == typeof(float) || type == typeof(double) || type == typeof(decimal);
}
