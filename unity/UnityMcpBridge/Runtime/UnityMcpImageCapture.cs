using System;
using System.Collections;

namespace UnityMcpBridge
{
    public sealed class UnityMcpCaptureRequest
    {
        public string CaptureName { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Format { get; set; }
        public string OptionsJson { get; set; }
    }

    public sealed class UnityMcpImageResult
    {
        public byte[] Data { get; set; }
        public string MimeType { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Error { get; set; }
    }

    public interface IUnityMcpImageProvider
    {
        // The bridge advances the routine once per Editor update. Yielded instruction objects are not interpreted.
        IEnumerator CaptureMcpImage(
            UnityMcpCaptureRequest request,
            Action<UnityMcpImageResult> completed);
    }
}
