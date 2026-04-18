namespace TuroClawProwl.Domain;

public sealed record StateTransition<T>(T From, T To);
