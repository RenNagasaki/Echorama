using System.Numerics;

namespace Echorama.DataClasses
{
    public static class Constants
    {
        public static readonly Vector4 INFOLOGCOLOR = new Vector4(.3f, 1.0f, 1.0f, 1f);
        public static readonly Vector4 DEBUGLOGCOLOR = new Vector4(0.0f, 1.0f, 0.0f, 1f);
        public static readonly Vector4 ERRORLOGCOLOR = new Vector4(1.0f, 0.0f, 0.0f, 1f);
        public static readonly Vector4 ACTIVETASKCOLOR = new Vector4(.8f, .2f, .2f, 1f);
        public static float MAXVROTA = 1.569f;
    }
}
