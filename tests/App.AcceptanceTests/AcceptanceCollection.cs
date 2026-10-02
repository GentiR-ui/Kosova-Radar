namespace App.AcceptanceTests;

/// <summary>All acceptance tests share one running API + database. Use [Collection(AcceptanceCollection.Name)].</summary>
[CollectionDefinition(Name)]
public sealed class AcceptanceCollection : ICollectionFixture<AppFactory>
{
    public const string Name = "acceptance";
}
