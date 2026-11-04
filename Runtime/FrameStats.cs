using UnityEngine;

namespace FunBrainLab.VRLink
{
    /// <summary>1초 단위 FPS와 느린 프레임(목표의 1.5배 초과) 개수</summary>
    public static class FrameStats
    {
        static int frames;
        static int slow;
        static float elapsed;

        public static float Fps { get; private set; }
        public static int SlowFramesLastSecond { get; private set; }
        public static float TargetRate { get; set; } = 72f;

        public static void Tick()
        {
            float dt = Time.unscaledDeltaTime;
            frames++;
            elapsed += dt;
            if (TargetRate > 0 && dt > 1.5f / TargetRate) slow++;
            if (elapsed >= 1f)
            {
                Fps = frames / elapsed;
                SlowFramesLastSecond = slow;
                frames = 0; slow = 0; elapsed = 0f;
            }
        }
    }
}
