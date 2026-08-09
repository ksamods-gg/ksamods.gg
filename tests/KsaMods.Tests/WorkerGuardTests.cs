using System.Net;
using KsaMods.Worker;
using Xunit;

namespace KsaMods.Tests;

public class SsrfGuardTests
{
    [Theory]
    // The ranges an SSRF payload actually aims at.
    [InlineData("127.0.0.1")]
    [InlineData("127.53.1.9")]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]   // the cloud metadata endpoint
    [InlineData("100.64.0.1")]        // CGNAT
    [InlineData("198.18.0.1")]        // benchmarking
    [InlineData("224.0.0.1")]         // multicast
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("fe80::1")]           // link-local
    [InlineData("fc00::1")]           // unique local
    [InlineData("::ffff:127.0.0.1")]  // IPv4-mapped loopback
    [InlineData("::ffff:169.254.169.254")]
    public void Rejects_addresses_that_are_not_publicly_routable(string address)
    {
        Assert.False(SafeFetcher.IsPubliclyRoutable(IPAddress.Parse(address)),
            $"{address} must never be connected to.");
    }

    [Theory]
    [InlineData("140.82.121.4")]      // github.com
    [InlineData("8.8.8.8")]
    [InlineData("2606:4700::1")]
    public void Allows_publicly_routable_addresses(string address)
    {
        Assert.True(SafeFetcher.IsPubliclyRoutable(IPAddress.Parse(address)));
    }

    [Fact]
    public void The_default_host_allowlist_is_forge_asset_domains_only()
    {
        // §5.6: an enumerable allowlist is the single biggest win of the forge-only decision.
        // Anything that lets an arbitrary host in has given that away.
        var policy = new FetchPolicy();

        Assert.Contains("github.com", policy.AllowedHosts);
        Assert.Contains("objects.githubusercontent.com", policy.AllowedHosts);

        Assert.DoesNotContain("example.com", policy.AllowedHosts);
        Assert.DoesNotContain("*", policy.AllowedHosts);
        Assert.All(policy.AllowedHosts, host => Assert.DoesNotContain('*', host));
    }
}

public class ContainerFlagTests
{
    private static IReadOnlyList<string> Flags() =>
        new ContainerRunner(new ContainerPolicy { ImageDigest = "ksamods/validator@sha256:abc" })
            .BuildArguments("/scratch/a.zip", "SomeMod");

    [Fact]
    public void The_container_has_no_network()
    {
        // The single most important flag: it is what makes a defeated parser worthless rather
        // than a pivot, and it is why the fetch happens on the host instead.
        var flags = Flags();
        var index = flags.ToList().IndexOf("--network");

        Assert.True(index >= 0, "--network must be passed");
        Assert.Equal("none", flags[index + 1]);
    }

    [Theory]
    [InlineData("--read-only")]
    [InlineData("--cap-drop ALL")]
    [InlineData("--security-opt no-new-privileges")]
    [InlineData("--user")]
    public void Applies_every_non_negotiable_hardening_flag(string flag)
    {
        var joined = string.Join(' ', Flags());
        Assert.Contains(flag, joined, StringComparison.Ordinal);
    }

    [Fact]
    public void Runs_as_a_non_root_user()
    {
        var flags = Flags();
        var index = flags.ToList().IndexOf("--user");
        Assert.DoesNotContain("0:", flags[index + 1], StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_bind_mounted_into_the_container()
    {
        // The archive goes in on stdin and the report comes back on stdout. A bind mount would be
        // resolved by the daemon on the host, where the worker's own scratch directory does not
        // exist - the daemon creates an empty directory there and the archive arrives as a folder.
        // It also means --read-only has no writable mount to undermine it.
        var joined = string.Join(' ', Flags());

        Assert.DoesNotContain("-v ", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("--mount", joined, StringComparison.Ordinal);
        Assert.Contains("KSAMODS_INPUT=-", joined, StringComparison.Ordinal);
        Assert.Contains("KSAMODS_OUTPUT=-", joined, StringComparison.Ordinal);
    }

    [Fact]
    public void Disables_swap_so_an_over_allocating_job_is_oom_killed()
    {
        // Equal memory and memory-swap means no swap: a runaway job dies instead of dragging
        // the host into thrashing.
        var flags = Flags();
        var memory = flags.Single(f => f.StartsWith("--memory=", StringComparison.Ordinal));
        var swap = flags.Single(f => f.StartsWith("--memory-swap=", StringComparison.Ordinal));

        Assert.Equal(memory["--memory=".Length..], swap["--memory-swap=".Length..]);
    }

    [Fact]
    public void Keeps_stdin_open_so_the_archive_can_be_piped_in()
    {
        // Replaces a pair of bind mounts. The archive used to arrive at /in/archive.zip and the
        // report left through a writable /out, which worked only while the worker ran on the same
        // filesystem as the daemon - and it does not, it runs in a container of its own.
        Assert.Contains("-i", Flags());
    }

    [Fact]
    public void Never_mounts_the_docker_socket()
    {
        // Mounting it would hand root on the worker host to whatever is in the archive.
        var joined = string.Join(' ', Flags());
        Assert.DoesNotContain("docker.sock", joined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--privileged", joined, StringComparison.Ordinal);
    }

    [Fact]
    public void Pins_the_image_by_digest_rather_than_tag()
    {
        // A tag is mutable, and "the image we tested" is the entire point of pinning it.
        var image = Flags().Last(f => f.StartsWith("ksamods/validator", StringComparison.Ordinal));
        Assert.Contains("@sha256:", image, StringComparison.Ordinal);
    }

    [Fact]
    public void Bounds_processes_and_file_descriptors()
    {
        var joined = string.Join(' ', Flags());
        Assert.Contains("--pids-limit=", joined, StringComparison.Ordinal);
        Assert.Contains("nofile=", joined, StringComparison.Ordinal);
    }

    [Fact]
    public void Tmpfs_is_noexec()
    {
        var tmpfs = Flags().Single(f => f.StartsWith("--tmpfs=", StringComparison.Ordinal));
        Assert.Contains("noexec", tmpfs, StringComparison.Ordinal);
        Assert.Contains("nosuid", tmpfs, StringComparison.Ordinal);
    }
}
