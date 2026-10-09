namespace GharsPlatform.Tests.Infrastructure;

/// <summary>Tests sharing one application instance and one scratch database; they run one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class AppCollection : ICollectionFixture<GharsAppFactory>
{
    public const string Name = "app";
}

/// <summary>
/// Tests whose assertions depend on every row of a kind in the database (the whole Super Admin
/// roster), so they get a database of their own.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IsolatedCollection : ICollectionFixture<IsolatedGharsAppFactory>
{
    public const string Name = "isolated";
}
