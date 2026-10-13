namespace Rex.Media.Primitives;

/// <summary>How Subject Lock's removal preview hides what was selected.</summary>
public enum RemovalMode
{
    /// <summary>Show what is behind it: the background seen in other pictures, and a fill of the scene around it.</summary>
    Remove,

    /// <summary>
    /// Cover it with the surface just around it, as a retouching brush hides a logo or a stain on a
    /// garment or an object: only a ring close around the selection is copied from, never the scene
    /// seen behind it, and the cover moves with the selection.
    /// </summary>
    Blend,
}
