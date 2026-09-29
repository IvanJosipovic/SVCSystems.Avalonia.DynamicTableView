namespace SvcSystems.Avalonia.DynamicTableView;

internal interface IIdentityPreservingSelectionModel : ISelectionModel, IDisposable
{
    void SetIdentitySource(IEnumerable source);
}
