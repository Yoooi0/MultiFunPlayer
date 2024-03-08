using MultiFunPlayer.MediaSource.MediaResource;

namespace MultiFunPlayer.Tests;

public class MediaResourceInfoTests
{
    public static IEnumerable<object[]> ValidSamplePaths => [
        [@"folder\file.ext", "file.ext", "folder"],
        [@".\folder\file.ext", "file.ext", @".\folder"],
        [@"file.ext", "file.ext", ""],
        [@"/Users/user/file.ext", "file.ext", "/Users/user"],
        [@"/Users/user/filenoext", "filenoext", "/Users/user"],
        [@"/Users/user/folder", "folder", "/Users/user"],
        [@"C:\filenoext", "filenoext", @"C:\"],
        [@"X:\file.ext", "file.ext", @"X:\"],
        [@"\\127.0.0.1\folder\file.ext", "file.ext", @"\\127.0.0.1\folder"],
        [@"\\contoso.local\folder\file.ext", "file.ext", @"\\contoso.local\folder"],
        [@"http://in.ter.net/subfolder/filenoext", "filenoext", "http://in.ter.net/subfolder"],
        [@"http://in.ter.net/subfolder/file.ext", "file.ext", "http://in.ter.net/subfolder"],
        [@"http://in.ter.net/subfolder/file.ext?query=yes", "file.ext", "http://in.ter.net/subfolder"],
        [@"http://in.ter.net/subfolder/file.ext?query=yes#fragment", "file.ext", "http://in.ter.net/subfolder"],
        [@"http://127.0.0.1:9999/file/file with (spaces).ext", "file with (spaces).ext", "http://127.0.0.1:9999/file"],
        [@"http://127.0.0.1:9999/file/file%20with%20%28spaces%29.ext", "file with (spaces).ext", "http://127.0.0.1:9999/file"],
        [@"http://127.0.0.1:9999/file/file%20with%20%28spaces%29.ext?query=yes#fragment", "file with (spaces).ext", "http://127.0.0.1:9999/file"],
        [@"http://www.example.com/düsseldörf/日本語/위키백과:대문/file.ext", "file.ext", "http://www.example.com/düsseldörf/日本語/위키백과:대문"],
        [@"http://www.example.com/d%C3%BCsseld%C3%B6rf/%E6%97%A5%E6%9C%AC%E8%AA%9E/%EC%9C%84%ED%82%A4%EB%B0%B1%EA%B3%BC:%EB%8C%80%EB%AC%B8/file.ext", "file.ext", "http://www.example.com/düsseldörf/日本語/위키백과:대문"],
        [@"https://user:password@www.contoso.com:80/Home/file.ext?query1=yes&query2=yes#fragment", "file.ext", "https://www.contoso.com/Home"],
        [@"file://localhost/etc/file", "file", @"\\localhost\etc"],
        [@"file:///etc/file", "file", "/etc"],
        [@"file://localhost/C:/file.ext", "file.ext", @"\\localhost\C:"],
        [@"file:///C:/file.ext", "file.ext", @"C:\"],
        [@"file:///C:/file with (spaces).ext", "file with (spaces).ext", @"C:\"],
        [@"file:///C:/file%20with%20%28spaces%29.ext", "file with (spaces).ext", @"C:\"],
    ];

    public static IEnumerable<object[]> InvalidSamplePaths => [
        [@"\\127.0.0.1"],
        [@"\\127.0.0.1\file.ext"],
        [@"http://in.ter.net"],
        [@"http://in.ter.net/"],
        [@"http://in.ter.net/subfolder/"],
    ];

    [Theory]
    [MemberData(nameof(ValidSamplePaths))]
    public void MediaResourceHasExpectedNameAndSource(string path, string expectedName, string expectedSource)
    {
        var builder = new MediaResourceInfoBuilder(path);
        var resource = builder.Build();

        Assert.Equal(expectedName, resource.Name);
        Assert.Equal(expectedSource, resource.Source);
    }

    [Theory]
    [MemberData(nameof(InvalidSamplePaths))]
    public void MediaResourceIsNullWithInvalidPath(string path)
    {
        var builder = new MediaResourceInfoBuilder(path);
        var resource = builder.Build();

        Assert.Null(resource);
    }
}
