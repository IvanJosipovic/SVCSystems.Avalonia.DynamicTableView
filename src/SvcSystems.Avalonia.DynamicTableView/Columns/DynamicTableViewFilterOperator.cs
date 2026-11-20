namespace SvcSystems.Avalonia.DynamicTableView;

/// <summary>Filter operators supported by built-in and custom filters.</summary>
public enum DynamicTableViewFilterOperator
{
    /// <summary>The value contains the filter text.</summary>
    Contains,

    /// <summary>The value does not contain the filter text.</summary>
    DoesNotContain,

    /// <summary>The value equals the filter value.</summary>
    Equals,

    /// <summary>The value does not equal the filter value.</summary>
    NotEquals,

    /// <summary>The value starts with the filter text.</summary>
    StartsWith,

    /// <summary>The value does not start with the filter text.</summary>
    DoesNotStartWith,

    /// <summary>The value ends with the filter text.</summary>
    EndsWith,

    /// <summary>The value does not end with the filter text.</summary>
    DoesNotEndWith,

    /// <summary>The value is greater than the filter value.</summary>
    GreaterThan,

    /// <summary>The value is greater than or equal to the filter value.</summary>
    GreaterThanOrEqual,

    /// <summary>The value is less than the filter value.</summary>
    LessThan,

    /// <summary>The value is less than or equal to the filter value.</summary>
    LessThanOrEqual,

    /// <summary>The value is between the first and second filter values.</summary>
    Between,

    /// <summary>The value is not between the first and second filter values.</summary>
    NotBetween,

    /// <summary>The value is one of the selected filter choices.</summary>
    In,

    /// <summary>The Boolean value is true.</summary>
    IsTrue,

    /// <summary>The Boolean value is false.</summary>
    IsFalse,

    /// <summary>The value is null.</summary>
    IsNull,

    /// <summary>The value is not null.</summary>
    IsNotNull
}
