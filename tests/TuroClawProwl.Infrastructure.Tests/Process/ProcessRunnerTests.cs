using FluentAssertions;
using TuroClawProwl.Infrastructure.Process;

namespace TuroClawProwl.Infrastructure.Tests.Process;

[Trait("Category", "Integration")]
public class ProcessRunnerTests
{
    // Regression for the unobserved-task-fault fix: cancelling mid-run used to
    // leave stdOutTask/stdErrTask (started on the same token) unawaited once
    // WaitForExitAsync threw, so their eventual cancellation faulted with no
    // observer. This does not prove the fault is never unobserved (that would
    // need hooking the process-wide TaskScheduler.UnobservedTaskException event
    // and forcing GC finalization -- flaky and timing-dependent for what is a
    // straightforward await-and-swallow fix) but it does prove the fix did not
    // introduce a deadlock: RunAsync must still complete promptly and still
    // propagate the cancellation, now that it awaits both tasks before rethrowing.
    [Fact]
    public async Task Cancelling_a_running_process_propagates_cancellation_without_hanging()
    {
        using var cts = new CancellationTokenSource();
        var runTask = ProcessRunner.RunAsync("ping", ["-n", "30", "127.0.0.1"], null, cts.Token);

        // Give the process a moment to actually start before cancelling, so the
        // cancellation lands mid-run rather than racing process creation.
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        cts.Cancel();

        var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(15)));
        completed.Should().Be(runTask, "cancellation must not leave RunAsync hanging while it observes the read tasks");

        Func<Task> act = () => runTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
