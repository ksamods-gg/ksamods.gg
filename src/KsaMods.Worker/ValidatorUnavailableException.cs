namespace KsaMods.Worker;

/// <summary>
/// The worker cannot validate anything, and no release is at fault.
///
/// <para>Separate from every other failure because the response is the opposite one. A bad archive
/// should fail its job and spend an attempt; this must not, because the job never got a fair run
/// and five of these would kill a job that was always fine.</para>
///
/// <para>Thrown when the pinned validator image has gone from the host. The worker resolves a tag
/// to an image id once at startup and deliberately never re-resolves - a pin that follows a moving
/// tag is not a pin - so the only honest recovery is a restart, which re-resolves legitimately.
/// <see cref="JobPump"/> stops the process, and the restart policy does the rest.</para>
/// </summary>
public sealed class ValidatorUnavailableException(string message) : Exception(message);
