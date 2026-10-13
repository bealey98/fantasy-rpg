namespace Battle.Content;

/// <summary>A mistake in a content file. The message always names the file and line.</summary>
public sealed class ContentException(string message) : Exception(message);
