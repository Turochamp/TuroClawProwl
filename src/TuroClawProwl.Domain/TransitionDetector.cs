namespace TuroClawProwl.Domain;

public sealed class TransitionDetector<T>
{
    private readonly IEqualityComparer<T> _comparer;
    private T? _last;
    private bool _hasLast;

    public TransitionDetector(IEqualityComparer<T>? comparer = null)
    {
        _comparer = comparer ?? EqualityComparer<T>.Default;
    }

    public StateTransition<T>? Observe(T current)
    {
        if (!_hasLast)
        {
            _last = current;
            _hasLast = true;
            return null;
        }

        if (_comparer.Equals(_last, current))
            return null;

        var transition = new StateTransition<T>(_last!, current);
        _last = current;
        return transition;
    }
}
