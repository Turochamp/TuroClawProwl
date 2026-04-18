using System.Text;
using FluentAssertions;
using TuroClawProwl.Infrastructure.Security;
using TuroClawProwl.Infrastructure.Tests.TestSupport;

namespace TuroClawProwl.Infrastructure.Tests.Security;

public class DpapiTokenStoreTests
{
    [Fact]
    public async Task Reads_null_when_file_does_not_exist()
    {
        using var tmp = new TempDirectory();
        var store = new DpapiTokenStore(Path.Combine(tmp.Path, "token.dat"));

        var token = await store.GetTokenAsync();

        token.Should().BeNull();
    }

    [Fact]
    public async Task Roundtrip_returns_same_token()
    {
        using var tmp = new TempDirectory();
        var store = new DpapiTokenStore(Path.Combine(tmp.Path, "token.dat"));

        await store.SetTokenAsync("s3cret-123");
        var roundtripped = await store.GetTokenAsync();

        roundtripped.Should().Be("s3cret-123");
    }

    [Fact]
    public async Task Raw_file_bytes_do_not_contain_plaintext_token()
    {
        using var tmp = new TempDirectory();
        var filePath = Path.Combine(tmp.Path, "token.dat");
        var store = new DpapiTokenStore(filePath);
        var recognizableSeed = "TURO-CLAW-PROWL-PLAINTEXT-MARKER";

        await store.SetTokenAsync(recognizableSeed);

        var raw = await File.ReadAllBytesAsync(filePath);
        var rawAscii = Encoding.ASCII.GetString(raw);
        rawAscii.Should().NotContain(recognizableSeed);
    }

    [Fact]
    public async Task Set_token_creates_missing_parent_directory()
    {
        using var tmp = new TempDirectory();
        var nested = Path.Combine(tmp.Path, "sub", "folder", "token.dat");
        var store = new DpapiTokenStore(nested);

        await store.SetTokenAsync("x");

        File.Exists(nested).Should().BeTrue();
    }

    [Fact]
    public void Constructor_rejects_empty_file_path()
    {
        Action act = () => new DpapiTokenStore("");
        act.Should().Throw<ArgumentException>();
    }
}
