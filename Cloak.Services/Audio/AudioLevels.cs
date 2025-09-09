using System;

namespace Cloak.Services.Audio
{
    public static class AudioLevels
    {
        public static event EventHandler<float>? MicLevelChanged;
        public static event EventHandler<float>? SystemLevelChanged;

        public static void ReportMic(float level)
        {
            if (level < 0f) level = 0f;
            if (level > 1f) level = 1f;
            MicLevelChanged?.Invoke(null, level);
        }

        public static void ReportSystem(float level)
        {
            if (level < 0f) level = 0f;
            if (level > 1f) level = 1f;
            SystemLevelChanged?.Invoke(null, level);
        }
    }
}


