namespace ExtendExprorer.Models;

public record Entry(string Name, bool IsDirectory, long Size, DateTime Modified, bool IsHiddenOrSystem);
