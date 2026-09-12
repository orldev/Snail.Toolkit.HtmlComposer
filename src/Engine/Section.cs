namespace Snail.Toolkit.HtmlComposer.Engine;

/// <summary>One run of pages of a document, with the sheet those pages are made up on.</summary>
/// <param name="Html">The markup set in this run.</param>
/// <param name="Setup">The sheet its pages are made up on.</param>
/// <remarks>
/// Sections are what lets a cover differ from the pages that follow it — its own margins, its own background
/// and its own running lines — while staying one document in one file. Page numbers run through the whole
/// document rather than restarting, so a running line counts every page of every section.
/// </remarks>
public sealed record Section(string Html, PageSetup Setup);
