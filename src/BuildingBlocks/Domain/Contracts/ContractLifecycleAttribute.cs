namespace BuildingBlocks.Domain.Contracts;

public enum ContractLifecycleStatus
{
    Active = 0,
    Superseded = 1
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ContractLifecycleAttribute : Attribute
{
    public ContractLifecycleAttribute(string introducedOn, ContractLifecycleStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(introducedOn);
        IntroducedOn = introducedOn;
        Status = status;
    }

    public string IntroducedOn { get; }

    public ContractLifecycleStatus Status { get; }

    public Type? SupersededBy { get; init; }

    public string? RetireOn { get; init; }
}
