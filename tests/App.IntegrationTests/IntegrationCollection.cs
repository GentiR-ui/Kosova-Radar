namespace App.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<AppFactory>
{
    public const string Name = "integration";
}
