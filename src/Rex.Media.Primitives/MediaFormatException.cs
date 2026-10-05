namespace Rex.Media.Primitives;

/// <summary>
/// The bytes are not what their format says they should be: a truncated box, an impossible header
/// field, a read past the end of a bitstream. Parsers throw this and nothing else for bad input, so
/// the engine can tell a broken file from a broken program and keep playing the streams that are fine.
/// </summary>
public sealed class MediaFormatException : IOException
{
    public MediaFormatException()
    {
    }

    public MediaFormatException(string message)
        : base(message)
    {
    }

    public MediaFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
