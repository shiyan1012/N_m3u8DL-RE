using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Util;
using System.Net;
using System.Net.Http.Headers;
 
namespace N_m3u8DL_RE.Util;
 
internal static class LargeSingleFileSplitUtil
{
    class Clip
    {
        public required int Index;
        public required long From;
        public required long To;
    }
 
    /// <summary>
    /// URL大文件切片处理
    /// </summary>
    /// <param name="segment"></param>
    /// <param name="headers"></param>
    /// <returns></returns>
    public static async Task<List<MediaSegment>?> SplitUrlAsync(MediaSegment segment, Dictionary<string,string> headers)
    {
        var url = segment.Url;
        Logger.DebugMarkUp($"[File Split] Attempting to split: {url}");
        
        // 检测1：Range支持（通过实际Range请求）
        var canSplit = await CanSplitAsync(url, headers);
        if (!canSplit)
        {
            Logger.WarnMarkUp($"[File Split] Cannot split: Range support test failed");
            return null;
        }
        
        // 检测2：segment本身不能有range
        if (segment.StartRange != null)
        {
            Logger.WarnMarkUp($"[File Split] Cannot split: segment already has range ({segment.StartRange})");
            return null;
        }
        
        // 检测3：获取文件大小
        long fileSize = await GetFileSizeAsync(url, headers);
        Logger.DebugMarkUp($"[File Split] File size: {fileSize} bytes ({fileSize / 1024.0 / 1024.0:F2} MB)");
        
        if (fileSize == 0)
        {
            Logger.WarnMarkUp($"[File Split] Cannot split: file size is 0");
            return null;
        }
        
        // 计算切分
        List<Clip> allClips = GetAllClips(fileSize);
        Logger.DebugMarkUp($"[File Split] Splitting {fileSize} bytes into {allClips.Count} segments (10MB each)");
        
        var splitSegments = new List<MediaSegment>();
        foreach (Clip clip in allClips)
        {
            splitSegments.Add(new MediaSegment()
            {
                Index = clip.Index,
                Url = url,
                StartRange = clip.From,
                ExpectLength = clip.To == -1 ? null : clip.To - clip.From + 1,
                EncryptInfo = segment.EncryptInfo,
            });
        }
 
        return splitSegments;
    }
 
    public static async Task<bool> CanSplitAsync(string url, Dictionary<string, string> headers)
    {
        try
        {
            Logger.DebugMarkUp($"[Range Detection] Testing Range support via actual request: {url}");
            
            // 直接尝试Range请求，不依赖Accept-Ranges头
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, 0);  // 只请求1个字节
            var response = await HTTPUtil.AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            
            bool supportsRange = response.StatusCode == HttpStatusCode.PartialContent;
            
            Logger.DebugMarkUp($"[Range Detection] Range test result: Status={response.StatusCode}, SupportsRange={supportsRange}");
            
            if (!supportsRange)
            {
                Logger.WarnMarkUp($"[Range Detection] Range request returned {response.StatusCode}, cannot split file");
            }
            else
            {
                Logger.DebugMarkUp($"[Range Detection] Range request successful (206), file can be split");
            }
            
            return supportsRange;
        }
        catch (HttpRequestException ex)
        {
            Logger.DebugMarkUp($"[Range Detection] HTTP request failed: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Logger.DebugMarkUp($"[Range Detection] Unexpected error: {ex.Message}");
            return false;
        }
    }
 
    private static async Task<long> GetFileSizeAsync(string url, Dictionary<string, string> headers)
    {
        using var httpRequestMessage = new HttpRequestMessage();
        httpRequestMessage.Method = HttpMethod.Head;
        httpRequestMessage.RequestUri = new(url);
        foreach (var header in headers)
        {
            httpRequestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        var response = (await HTTPUtil.AppHttpClient.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead)).EnsureSuccessStatusCode();
        long totalSizeBytes = response.Content.Headers.ContentLength ?? 0;
 
        return totalSizeBytes;
    }
 
    // 此函数主要是切片下载逻辑
    private static List<Clip> GetAllClips(long fileSize)
    {
        long originalFileSize = fileSize;
        List<Clip> clips = [];
        int index = 0;
        long counter = 0;
        int perSize = 10 * 1024 * 1024;
        while (fileSize > 0)
        {
            Clip c = new()
            {
                Index = index,
                From = counter,
                To = counter + perSize
            };
            // 没到最后
            if (fileSize - perSize > 0)
            {
                fileSize -= perSize;
                counter += perSize + 1;
                index++;
                clips.Add(c);
            }
            // 已到最后
            else
            {
                c.To = originalFileSize;
                clips.Add(c);
                break;
            }
        }
        return clips;
    }
}
