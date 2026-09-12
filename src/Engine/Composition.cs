namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>A document set into pages, with a note for everything that could not be set as written.</summary>
/// <param name="Pages">The pages in reading order.</param>
/// <param name="Notes">
/// What was left out or substituted, such as a picture that could not be read, each saying what kind of
/// thing it is about; empty when the markup was set exactly as written.
/// </param>
public sealed record Composition(IReadOnlyList<Page> Pages, IReadOnlyList<Note> Notes);
