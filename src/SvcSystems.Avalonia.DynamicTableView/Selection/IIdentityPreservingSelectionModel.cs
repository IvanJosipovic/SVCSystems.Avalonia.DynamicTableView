namespace SvcSystems.Avalonia.DynamicTableView;

internal interface IIdentityPreservingSelectionModel<T> : ISelectionModel, IDisposable
    where T : notnull
{
    void SetIdentitySource(IEnumerable source);
    void OnBoundChangeset(bool isSingleSameKeyUpdate, T previousItem, T currentItem);
}
