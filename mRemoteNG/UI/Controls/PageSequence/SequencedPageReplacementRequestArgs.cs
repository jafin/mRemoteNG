using System;

namespace mRemoteNG.UI.Controls.PageSequence;

public delegate void SequencedPageReplacementRequestHandler(object sender, SequencedPageReplacementRequestArgs args);

public enum RelativePagePosition
{
    PreviousPage,
    CurrentPage,
    NextPage
}

public class SequencedPageReplacementRequestArgs(SequencedControl newControl, RelativePagePosition pageToReplace)
{
    public SequencedControl NewControl { get; } = newControl ?? throw new ArgumentNullException(nameof(newControl));
    public RelativePagePosition PagePosition { get; } = pageToReplace;
}