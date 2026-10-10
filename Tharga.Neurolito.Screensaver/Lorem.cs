namespace Tharga.Neurolito.Screensaver;

/// <summary>Stand-in words: the screen shows how much text moves, never the text.</summary>
internal sealed class Lorem
{
    private static readonly string[] Words =
    (
        "lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et dolore " +
        "magna aliqua ut enim ad minim veniam quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo " +
        "consequat duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur " +
        "excepteur sint occaecat cupidatat non proident sunt in culpa qui officia deserunt mollit anim id est laborum"
    ).Split(' ');

    private int _next;

    /// <summary>Starts somewhere in the text, so two jobs do not begin with the same words.</summary>
    public Lorem(int seed = 0)
    {
        _next = Math.Abs(seed) % Words.Length;
    }

    public string Next()
    {
        var word = Words[_next];
        _next = (_next + 1) % Words.Length;
        return word;
    }

    public static int Count => Words.Length;
}
