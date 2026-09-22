// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;

namespace Reactor.Tests.Shared;

/// <summary>
/// Tells "another process holds this file" apart from "this process cannot open this file".
/// </summary>
/// <remarks>
/// <para>Both coordination schemes in the test tiers — the E2E startup gate and the packaged
/// tier's layout locks — are built on an exclusive open that is polled until it succeeds. Both
/// therefore have to answer the same question about a failed open, and getting it wrong is not
/// symmetric. Reading contention as a fault aborts a run that only needed to wait; reading a
/// fault as contention spends the whole timeout and then reports a competing run that does not
/// exist, while discarding the error that actually explains the failure.</para>
/// <para>Only a sharing or lock violation is evidence that somebody else holds the file.
/// Everything else — an ACL denial, a directory occupying the name, a missing parent, a full
/// volume — says something about this process or this storage, and no amount of waiting makes
/// it truer. Measured shapes on Windows: a second exclusive open raises
/// <see cref="IOException"/> with <c>ERROR_SHARING_VIOLATION</c>; a directory in the way raises
/// <see cref="UnauthorizedAccessException"/> (<c>ERROR_ACCESS_DENIED</c>); a missing parent
/// raises <see cref="DirectoryNotFoundException"/> (<c>ERROR_PATH_NOT_FOUND</c>) — an
/// <see cref="IOException"/> subclass, which is why the code below tests the code and not the
/// type.</para>
/// <para>Deliberately not a decision about whether to retry. Callers keep polling through
/// non-contention failures too, because refusal has a genuine transient form: both schemes
/// unlink their files on release, and a third party holding one with delete sharing leaves it
/// delete-pending, which surfaces as access denied and clears on its own. This only decides
/// what a failure that <em>outlasted</em> the wait should be reported as.</para>
/// </remarks>
internal static class FileContention
{
    /// <summary>Another process has the file open with incompatible sharing.</summary>
    private const int ErrorSharingViolation = 32;

    /// <summary>A byte range of the file is locked by another process.</summary>
    private const int ErrorLockViolation = 33;

    /// <summary>
    /// Whether <paramref name="failure"/> establishes that another process holds the file.
    /// </summary>
    /// <remarks>
    /// Answers <see langword="false"/> for anything it does not recognise, so an unfamiliar
    /// failure is reported as itself rather than silently attributed to a competitor. That is
    /// the safe direction: the report still names the real exception, whereas a wrong
    /// <see langword="true"/> erases it.
    /// </remarks>
    internal static bool IsHeldByAnother(Exception failure) =>
        failure is IOException and not FileNotFoundException and not DirectoryNotFoundException &&
        (failure.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation;
}
