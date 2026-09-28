using Xunit;

namespace Kazuko.Infrastructure.Tests;

/// <summary>
/// Serializes Firebird integration tests that may temporarily retarget
/// <see cref="Kazuko.Infrastructure.Data.FirebirdDatabase"/> to a copied database.
/// </summary>
[CollectionDefinition(nameof(FirebirdTestCollection), DisableParallelization = true)]
public sealed class FirebirdTestCollection;
