namespace BuildingBlocks.Application.Dispatching;

public sealed class DispatcherOptions
{
    public bool RegisterBuiltInBehaviors { get; set; } = true;

    public bool EnableStartupValidation { get; set; } = true;

    public IList<Type> AdditionalPipelineBehaviorTypes { get; } = new List<Type>();
}
