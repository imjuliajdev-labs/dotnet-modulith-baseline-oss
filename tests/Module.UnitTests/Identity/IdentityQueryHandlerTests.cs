using Identity.Application.Administration;

namespace Module.UnitTests.Identity;

public sealed class ListIdentityUsersQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_the_users_from_the_service()
    {
        var fixture = new IdentityUserAdministrationTestFixture("admin-1");
        var user = IdentityTestData.CreateUserAccount("user-1", enabled: true);
        fixture.Service.ListResult = [user];
        var handler = new ListIdentityUsersQueryHandler(fixture.Service);

        var result = await handler.Handle(new ListIdentityUsersQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([user], result.Value!.Users);
    }
}

public sealed class ListMachineClientsQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_the_clients_from_the_service()
    {
        var fixture = new MachineClientTestFixture("admin-1");
        var client = IdentityTestData.CreateMachineClientSummary("client-1", isActive: true);
        fixture.Service.ListResult = [client];
        var handler = new ListMachineClientsQueryHandler(fixture.Service);

        var result = await handler.Handle(new ListMachineClientsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([client], result.Value!.Clients);
    }
}

public sealed class GetMachineClientQueryHandlerTests
{
    [Fact]
    public async Task Handle_delegates_to_the_service_for_the_requested_client()
    {
        var fixture = new MachineClientTestFixture("admin-1");
        var client = IdentityTestData.CreateMachineClientSummary("client-42", isActive: true);
        fixture.Service.GetResult = BuildingBlocks.Application.Results.Result<MachineClientSummary>.Success(client);
        var handler = new GetMachineClientQueryHandler(fixture.Service);

        var result = await handler.Handle(new GetMachineClientQuery("client-42"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(client, result.Value);
    }
}
