// Doc-pipeline tests exercise CompileCommand end-to-end, which resolves the
// repo root from the *process* working directory. CompileCaptureSkipTests has
// to point that at a temp fixture repo, and SourceSnippetSanityTests reads it
// back expecting the real one — a cross-collection race that xUnit's default
// parallelism would make intermittent rather than impossible.
//
// The whole assembly runs in well under a second, so serializing collections
// costs nothing measurable and removes the entire class of interference.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
