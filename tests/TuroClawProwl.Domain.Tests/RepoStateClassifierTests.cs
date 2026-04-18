using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class RepoStateClassifierTests
{
    public static IEnumerable<object[]> ClassificationCases => new[]
    {
        // hasUncommitted, unpushedCount, hasUpstream, expectedIsClean, expectedNeedsAttention, caseName
        new object[] { false,   0, true,  true,  false, "pristine clone" },
        new object[] { false,   0, false, false, true,  "no upstream alone" },
        new object[] { false,   1, true,  false, true,  "single unpushed commit" },
        new object[] { false,   2, true,  false, true,  "two unpushed commits" },
        new object[] { false,   3, true,  false, true,  "three unpushed commits" },
        new object[] { false,   5, true,  false, true,  "five unpushed commits" },
        new object[] { false, 100, true,  false, true,  "many unpushed commits" },
        new object[] { false,   1, false, false, true,  "unpushed and no upstream" },
        new object[] { false,  10, false, false, true,  "ten unpushed no upstream" },
        new object[] { true,    0, true,  false, true,  "dirty working tree only" },
        new object[] { true,    0, false, false, true,  "dirty and no upstream" },
        new object[] { true,    1, true,  false, true,  "dirty and one unpushed" },
        new object[] { true,    2, true,  false, true,  "dirty and two unpushed" },
        new object[] { true,    5, true,  false, true,  "dirty and several unpushed" },
        new object[] { true,   10, true,  false, true,  "dirty and ten unpushed" },
        new object[] { true,   50, true,  false, true,  "dirty and fifty unpushed" },
        new object[] { true,    1, false, false, true,  "dirty unpushed no upstream" },
        new object[] { true,  100, false, false, true,  "worst case everything wrong" },
        new object[] { false,   4, true,  false, true,  "four unpushed" },
        new object[] { false,   7, true,  false, true,  "seven unpushed" },
        new object[] { true,    3, false, false, true,  "dirty three unpushed no upstream" },
    };

    [Theory]
    [MemberData(nameof(ClassificationCases))]
    public void Classifier_returns_state_matching_inputs_and_correct_clean_predicates(
        bool hasUncommitted,
        int unpushedCount,
        bool hasUpstream,
        bool expectedIsClean,
        bool expectedNeedsAttention,
        string caseName)
    {
        var state = RepoStateClassifier.Classify(
            new PorcelainSummary(hasUncommitted),
            unpushedCount,
            hasUpstream);

        state.UnpushedCount.Should().Be(unpushedCount, caseName);
        state.HasUncommitted.Should().Be(hasUncommitted, caseName);
        state.HasUpstream.Should().Be(hasUpstream, caseName);
        state.IsClean.Should().Be(expectedIsClean, caseName);
        state.NeedsAttention.Should().Be(expectedNeedsAttention, caseName);
    }

    [Fact]
    public void Classifier_is_pure_identical_inputs_produce_equal_states()
    {
        var porcelain = new PorcelainSummary(HasUncommitted: true);
        var a = RepoStateClassifier.Classify(porcelain, unpushedCount: 3, hasUpstream: true);
        var b = RepoStateClassifier.Classify(porcelain, unpushedCount: 3, hasUpstream: true);
        a.Should().Be(b);
    }

    [Fact]
    public void Classifier_rejects_negative_unpushed_count()
    {
        Action act = () => RepoStateClassifier.Classify(new PorcelainSummary(false), -1, true);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Classifier_rejects_null_porcelain_summary()
    {
        Action act = () => RepoStateClassifier.Classify(null!, 0, true);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Only_zero_unpushed_plus_no_uncommitted_plus_has_upstream_is_clean()
    {
        var state = RepoStateClassifier.Classify(new PorcelainSummary(false), 0, true);
        state.IsClean.Should().BeTrue();
        state.NeedsAttention.Should().BeFalse();
    }
}
