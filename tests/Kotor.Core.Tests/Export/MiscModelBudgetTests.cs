using Kotor.Core.Export;

namespace Kotor.Core.Tests.Export;

public class MiscModelBudgetTests
{
    [Fact]
    public void Heaviest_instances_per_entity_are_taken_first_within_the_budget()
    {
        var candidates = new List<MiscModelCandidate>
        {
            new(0, "rock", 500, 1),
            new(1, "building", 20000, 2),   // 10 000 par entite
            new(2, "building", 20000, 2),
            new(3, "statue", 8000, 1),
            new(4, "crate", 50, 1),         // sous le seuil
        };

        IReadOnlySet<int> chosen = MiscModelBudget.Select(candidates, budget: 5, minTrianglesPerEntity: 100);

        Assert.Equal([1, 2, 3], chosen.Order());
    }

    [Fact]
    public void A_candidate_too_big_for_the_remaining_budget_is_skipped_but_smaller_ones_still_fit()
    {
        var candidates = new List<MiscModelCandidate> { new(0, "a", 9000, 4), new(1, "b", 4000, 2), new(2, "c", 1000, 1) };

        Assert.Equal([1, 2], MiscModelBudget.Select(candidates, budget: 3, minTrianglesPerEntity: 1).Order());
    }

    [Fact]
    public void Never_exceeds_the_engine_limit()
    {
        var candidates = Enumerable.Range(0, 5000).Select(i => new MiscModelCandidate(i, "m" + (i % 7), 1000 + i, 1 + i % 3)).ToList();
        IReadOnlySet<int> chosen = MiscModelBudget.Select(candidates, MiscModelBudget.EngineMaxEntities, 1);
        Assert.InRange(candidates.Where(c => chosen.Contains(c.Id)).Sum(c => c.Md3Files), 1, MiscModelBudget.EngineMaxEntities);
    }
}
