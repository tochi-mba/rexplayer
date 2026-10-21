namespace Rex.Media.AppCore.Captions;

/// <summary>A reversible series of word corrections without changing the original speech or timing.</summary>
public sealed class TranscriptEditHistory
{
    private readonly TranscriptRevision _original;
    private readonly List<Transcript> _history;
    private int _index;

    public TranscriptEditHistory(TranscriptRevision original, Transcript? saved = null)
    {
        _original = original;
        _history = [original.Content];
        if (saved is { } edited && edited.Model == original.Content.Model && edited.Words.Count == original.Content.Words.Count
            && !edited.Words.SequenceEqual(original.Content.Words))
        {
            _history.Add(edited);
            _index = 1;
        }
    }

    public Transcript Current => _history[_index];
    public TranscriptRevision Snapshot => _original with { Content = Current };
    public bool CanUndo => _index > 0;
    public bool CanRedo => _index < _history.Count - 1;
    public bool IsOriginal => Current.Words.SequenceEqual(_original.Content.Words);

    public bool ReplaceWord(int index, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Current.Words.Count);

        if (Current.Words[index].Text == text)
        {
            return false;
        }

        var words = Current.Words.ToArray();
        words[index] = words[index] with { Text = text };
        _history.RemoveRange(_index + 1, _history.Count - _index - 1);
        _history.Add(Current with { Words = words });
        _index++;
        return true;
    }

    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        _index--;
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }

        _index++;
        return true;
    }

    public void Reset()
    {
        _index = 0;
        _history.RemoveRange(1, _history.Count - 1);
    }
}
