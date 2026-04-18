using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class TransitionDetectorTests
{
    [Fact]
    public void First_observation_emits_no_transition()
    {
        var detector = new TransitionDetector<string>();
        detector.Observe("initial").Should().BeNull();
    }

    [Fact]
    public void One_hundred_consecutive_identical_readings_emit_zero_transitions()
    {
        var detector = new TransitionDetector<string>();
        detector.Observe("steady");
        for (var i = 0; i < 100; i++)
        {
            detector.Observe("steady").Should().BeNull();
        }
    }

    [Fact]
    public void Change_emits_transition_with_previous_and_current()
    {
        var detector = new TransitionDetector<string>();
        detector.Observe("A");
        detector.Observe("B").Should().Be(new StateTransition<string>("A", "B"));
    }

    [Fact]
    public void After_change_subsequent_identical_readings_emit_no_transition()
    {
        var detector = new TransitionDetector<string>();
        detector.Observe("A");
        detector.Observe("B");
        detector.Observe("B").Should().BeNull();
        detector.Observe("B").Should().BeNull();
    }

    [Fact]
    public void Back_and_forth_emits_two_transitions()
    {
        var detector = new TransitionDetector<string>();
        detector.Observe("A");
        var t1 = detector.Observe("B");
        var t2 = detector.Observe("A");

        t1.Should().Be(new StateTransition<string>("A", "B"));
        t2.Should().Be(new StateTransition<string>("B", "A"));
    }

    [Fact]
    public void Detector_works_for_value_record_states()
    {
        var detector = new TransitionDetector<GatewayHealth>();
        var healthy = new GatewayHealth.Healthy(DateTimeOffset.UnixEpoch, null);
        var unreachable = new GatewayHealth.Unreachable(DateTimeOffset.UnixEpoch);

        detector.Observe(healthy).Should().BeNull();
        detector.Observe(healthy).Should().BeNull();

        var t = detector.Observe(unreachable);

        t.Should().NotBeNull();
        t!.From.Should().Be(healthy);
        t.To.Should().Be(unreachable);
    }

    [Fact]
    public void Detector_honours_custom_equality_comparer()
    {
        var detector = new TransitionDetector<string>(StringComparer.OrdinalIgnoreCase);
        detector.Observe("healthy");
        detector.Observe("HEALTHY").Should().BeNull();
        detector.Observe("unreachable").Should().NotBeNull();
    }
}
