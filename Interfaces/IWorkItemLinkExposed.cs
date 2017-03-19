namespace Aggregator.Core.Interfaces
{
    public interface IWorkItemLinkExposed
    {
        string LinkTypeEndImmutableName { get; }

        IWorkItemExposed Target { get; }
    }
}