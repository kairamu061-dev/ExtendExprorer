namespace ExtendExprorer.Models;

public enum ListErrorKind { NotFound, AccessDenied, Other }

public abstract record ListResult;

public sealed record ListOk(IReadOnlyList<Entry> Entries) : ListResult;

public sealed record ListError(ListErrorKind Kind, string Message) : ListResult;
