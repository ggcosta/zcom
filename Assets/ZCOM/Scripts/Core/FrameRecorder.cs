using System.IO;
using UnityEngine;

namespace Zcom
{
    /// <summary>
    /// Dev tool for gameplay videos: with Time.captureFramerate fixed, saves the camera's render texture every frame as a
    /// numbered JPG (encode with ffmpeg afterwards). Reads in LateUpdate, so each file holds the previous frame's image -
    /// a constant one-frame lag that does not matter for a video. Driven by the smoke test's -zcomVideo mode.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public class FrameRecorder : MonoBehaviour
    {
        RenderTexture source;
        string dir;
        Texture2D buffer;
        int index, lastFrame = -1;
        public int Frames => index;

        public static FrameRecorder Begin(RenderTexture rt, string outDir)
        {
            var go = new GameObject("FrameRecorder");
            var r = go.AddComponent<FrameRecorder>();
            r.source = rt;
            r.dir = outDir;
            Directory.CreateDirectory(outDir);
            r.buffer = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            return r;
        }

        void LateUpdate()
        {
            if (!source || Time.frameCount == lastFrame) return;
            if (lastFrame < 0) { lastFrame = Time.frameCount; return; }      // nothing rendered yet
            lastFrame = Time.frameCount;
            var prev = RenderTexture.active;
            RenderTexture.active = source;
            buffer.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            buffer.Apply(false);
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(dir, $"f{index++:00000}.jpg"), buffer.EncodeToJPG(93));
        }

        void OnDestroy() { if (buffer) Destroy(buffer); }
    }
}
