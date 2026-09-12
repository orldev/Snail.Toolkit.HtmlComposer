namespace Snail.Toolkit.HtmlComposer.Engine.Layout;

/// <summary>Where each flow sits in a vertical stack once the margins between neighbours collapse.</summary>
internal static class Stacking
{
    /// <summary>The gap two adjoining vertical margins leave between them.</summary>
    /// <remarks>
    /// CSS 2.1, section 8.3.1: two positive margins give the larger, two negative margins the more negative,
    /// and a positive and a negative margin their sum. Adding them instead doubles the gap between every pair
    /// of paragraphs, which is the first thing anyone comparing the output to a browser notices.
    /// </remarks>
    public static float Collapse(float above, float below) => (above, below) switch
    {
        ( >= 0, >= 0) => Math.Max(above, below),
        ( < 0, < 0) => Math.Min(above, below),
        _ => above + below,
    };

    public static IReadOnlyList<float> Offsets(IReadOnlyList<Flow> flows)
    {
        List<float> offsets = [];
        float top = 0;
        float pending = 0;

        foreach (Flow flow in flows)
        {
            top += offsets.Count == 0 ? flow.MarginTop : Collapse(pending, flow.MarginTop);
            offsets.Add(top);
            top += flow.Height;
            pending = flow.MarginBottom;
        }

        return offsets;
    }

    public static float Height(IReadOnlyList<Flow> flows)
    {
        if (flows.Count == 0)
        {
            return 0;
        }

        IReadOnlyList<float> offsets = Offsets(flows);

        return offsets[^1] + flows[^1].Height + flows[^1].MarginBottom;
    }
}
